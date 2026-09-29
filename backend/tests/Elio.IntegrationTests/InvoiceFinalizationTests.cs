using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elio.Application.Identity;
using Elio.Application.Invoices;
using Elio.Domain.Invoices;
using Elio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PdfSharp.Pdf.IO;

namespace Elio.IntegrationTests;

public sealed partial class InvoiceTests
{
    private static async Task<InvoiceDto> Finalize(HttpClient owner, InvoiceDto draft)
    {
        var response = await Send(owner, $"/api/invoices/{draft.Id}/finalize", new { draft.Version });
        response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<InvoiceDto>())!;
    }
    [Theory, InlineData("PHP"), InlineData("USD")]
    public async Task Issued_snapshot_and_pdf_survive_all_master_data_changes(string currency)
    {
        using var owner = await Account(); var client = await Source(owner, "clients", currency); var service = await Source(owner, "services", currency);
        client["phone"] = "+63 555 0100"; client["billingAddress"] = "10 Design Street\nManila";
        var clientUpdate = await Send(owner, $"/api/clients/{client["id"]}", client, HttpMethod.Patch); clientUpdate.EnsureSuccessStatusCode();
        client = (await clientUpdate.Content.ReadFromJsonAsync<JsonObject>())!;
        var input = Input(client, Line(serviceId: service["id"]!.GetValue<string>()), Line("0.25", "0.02")); input["currency"] = currency;
        var draft = await Create(owner, input); var issued = await Finalize(owner, draft);
        Assert.Equal("Finalized", issued.Lifecycle); Assert.Equal("INV-000001", issued.InvoiceNumber);
        Assert.Equal(15.03m, issued.Total); Assert.NotNull(issued.FinalizedAtUtc); Assert.Equal("Invoice tests", issued.SellerName);
        Assert.Equal("+63 555 0100", issued.ClientPhone); Assert.Equal("10 Design Street\nManila", issued.BillingAddress);
        Assert.Equal("Bank transfer", issued.PaymentInstructions);
        var path = $"/api/invoices/{draft.Id}";
        var pdf = await owner.GetAsync(path + "/pdf"); pdf.EnsureSuccessStatusCode();
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType!.MediaType);
        Assert.Contains("INV-000001.pdf", pdf.Content.Headers.ContentDisposition!.ToString());
        var before = await pdf.Content.ReadAsByteArrayAsync(); Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(before, 0, 4));
        using (var parsed = PdfReader.Open(new MemoryStream(before), PdfDocumentOpenMode.Import)) Assert.True(parsed.PageCount >= 1);
        client["name"] = "Changed client"; client["email"] = "new@example.test"; client["phone"] = "999"; client["billingAddress"] = "Changed address";
        (await Send(owner, $"/api/clients/{client["id"]}", client, HttpMethod.Patch)).EnsureSuccessStatusCode();
        service["name"] = "Changed service"; service["description"] = "Changed work"; service["defaultUnitPrice"] = "999.99";
        (await Send(owner, $"/api/services/{service["id"]}", service, HttpMethod.Patch)).EnsureSuccessStatusCode();
        var session = (await owner.GetFromJsonAsync<SessionDto>("/api/auth/session"))!;
        (await Send(owner, "/api/organization", new { name = "Changed seller", timeZone = "America/New_York", defaultCurrency = "USD", version = session.Organization!.Version }, HttpMethod.Patch)).EnsureSuccessStatusCode();
        var after = (await owner.GetFromJsonAsync<InvoiceDto>(path))!;
        Assert.Equal(JsonSerializer.Serialize(issued), JsonSerializer.Serialize(after));
        Assert.Equal(before, await owner.GetByteArrayAsync(path + "/pdf"));
        Assert.Single((await owner.GetFromJsonAsync<InvoiceDto[]>("/api/invoices?search=Example&status=finalized"))!);
        Assert.Empty((await owner.GetFromJsonAsync<InvoiceDto[]>("/api/invoices?search=Changed&status=finalized"))!);
        Assert.Single((await owner.GetFromJsonAsync<InvoiceDto[]>("/api/invoices?search=INV-000001"))!);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(owner, path, Edit(issued), HttpMethod.Patch)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(owner, path + "/finalize", new { issued.Version })).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await Send(owner, path, new { }, HttpMethod.Delete)).StatusCode);
        SaveQaPdf($"issued-{currency}.pdf", before);
    }
    [Fact]
    public async Task Concurrent_finalizations_allocate_unique_sequential_numbers_per_organization()
    {
        using var a = await Account(); using var b = await Account(); var clientA = await Source(a, "clients"); var clientB = await Source(b, "clients");
        var drafts = new List<InvoiceDto>(); for (var i = 0; i < 8; i++) drafts.Add(await Create(a, Input(clientA, Line())));
        var issued = await Task.WhenAll(drafts.Select(d => Finalize(a, d)));
        Assert.Equal(Enumerable.Range(1, 8).Select(x => $"INV-{x:D6}"), issued.Select(x => x.InvoiceNumber).Order());
        var other = await Finalize(b, await Create(b, Input(clientB, Line()))); Assert.Equal("INV-000001", other.InvoiceNumber);
        var next = await Finalize(a, await Create(a, Input(clientA, Line()))); Assert.Equal("INV-000009", next.InvoiceNumber);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ElioDbContext>();
        var duplicate = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"Invoices\" SET \"SequenceValue\" = {1L}, \"InvoiceNumber\" = {"INV-000001"} WHERE \"Id\" = {next.Id}"));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicate.SqlState);
    }
    [Fact]
    public async Task Racing_finalization_of_same_draft_issues_only_once_and_stale_version_issues_nothing()
    {
        using var owner = await Account(); var client = await Source(owner, "clients"); var draft = await Create(owner, Input(client, Line()));
        Assert.Equal(HttpStatusCode.Conflict, (await Send(owner, $"/api/invoices/{draft.Id}/finalize", new { version = Guid.NewGuid() })).StatusCode);
        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Send(owner, $"/api/invoices/{draft.Id}/finalize", new { draft.Version })));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK); Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        var issued = (await owner.GetFromJsonAsync<InvoiceDto>($"/api/invoices/{draft.Id}"))!; Assert.Equal("INV-000001", issued.InvoiceNumber);
        var next = await Finalize(owner, await Create(owner, Input(client, Line()))); Assert.Equal("INV-000002", next.InvoiceNumber);
    }
    [Fact]
    public async Task Failed_persistence_rolls_back_lifecycle_snapshot_and_counter_allocation()
    {
        using var owner = await Account(); var client = await Source(owner, "clients"); var draft = await Create(owner, Input(client, Line()));
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ElioDbContext>();
        // Test schema only: simulate a persistence failure after numbering, not a pre-validation failure.
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION reject_test_issuance() RETURNS trigger LANGUAGE plpgsql AS $body$
            BEGIN IF NEW."Lifecycle" = 'Finalized' THEN RAISE EXCEPTION 'Simulated persistence failure'; END IF; RETURN NEW; END; $body$;
            CREATE TRIGGER reject_test_issuance BEFORE UPDATE ON "Invoices" FOR EACH ROW EXECUTE FUNCTION reject_test_issuance();
            """);
        try { Assert.Equal(HttpStatusCode.InternalServerError, (await Send(owner, $"/api/invoices/{draft.Id}/finalize", new { draft.Version })).StatusCode); }
        finally { await db.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_test_issuance ON \"Invoices\"; DROP FUNCTION reject_test_issuance();"); }
        var after = (await owner.GetFromJsonAsync<InvoiceDto>($"/api/invoices/{draft.Id}"))!;
        Assert.Equal("Draft", after.Lifecycle); Assert.Null(after.InvoiceNumber); Assert.Null(after.SellerName); Assert.Equal(draft.Version, after.Version);
        Assert.Equal("INV-000001", (await Finalize(owner, after)).InvoiceNumber);
    }
    [Fact]
    public async Task Finalization_and_pdf_enforce_tenancy_authentication_membership_and_csrf()
    {
        using var a = await Account(); using var b = await Account(); var client = await Source(a, "clients"); var draft = await Create(a, Input(client, Line()));
        var path = $"/api/invoices/{draft.Id}";
        Assert.Equal(HttpStatusCode.NotFound, (await Send(b, path + "/finalize", new { draft.Version })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync(path + "/pdf")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await a.GetAsync(path + "/pdf")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await a.PostAsJsonAsync(path + "/finalize", new { draft.Version })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(a, path + "/finalize", new { })).StatusCode);
        using var anonymous = factory.CreateClient(); using var pending = await Account(false, false); using var noOrg = await Account(false);
        foreach (var (user, status) in new[] { (anonymous, HttpStatusCode.Unauthorized), (pending, HttpStatusCode.Forbidden), (noOrg, HttpStatusCode.Forbidden) })
        { Assert.Equal(status, (await user.GetAsync(path + "/pdf")).StatusCode); Assert.Equal(status, (await Send(user, path + "/finalize", new { draft.Version })).StatusCode); }
        var issued = await Finalize(a, draft);
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync(path + "/pdf")).StatusCode);
        var bClient = await Source(b, "clients"); Assert.Equal("INV-000001", (await Finalize(b, await Create(b, Input(bClient, Line())))).InvoiceNumber);
        var session = (await a.GetFromJsonAsync<SessionDto>("/api/auth/session"))!;
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ElioDbContext>().Memberships.Where(x => x.Id == session.Membership!.Id).ExecuteDeleteAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await a.GetAsync(path + "/pdf")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(a, path + "/finalize", new { issued.Version })).StatusCode);
    }
    [Fact]
    public async Task Pdf_paginates_long_content_and_large_values_and_inactive_sources_can_be_issued()
    {
        using var owner = await Account(); var client = await Source(owner, "clients"); var service = await Source(owner, "services");
        var lines = Enumerable.Range(1, 30).Select(i => { var line = Line("1", "10.01"); line["description"] = $"Delivery item {i}: discovery, design and implementation."; return line; }).ToArray();
        lines[0]["description"] = new string('W', 2000); lines[0]["unitPrice"] = "999999990000.00";
        lines[1]["serviceId"] = service["id"]!.GetValue<string>();
        var input = Input(client, lines); input["notes"] = string.Join("\n", Enumerable.Repeat("Project notes for the issued document.", 40));
        var draft = await Create(owner, input);
        foreach (var (kind, source) in new[] { ("clients", client), ("services", service) })
            (await Send(owner, $"/api/{kind}/{source["id"]}/deactivate", new { version = source["version"]!.GetValue<string>() })).EnsureSuccessStatusCode();
        var issued = await Finalize(owner, draft); Assert.False(issued.ClientIsActive);
        var bytes = await owner.GetByteArrayAsync($"/api/invoices/{issued.Id}/pdf");
        using var parsed = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import);
        Assert.True(parsed.PageCount >= 3); SaveQaPdf("issued-long.pdf", bytes);
    }
    private static void SaveQaPdf(string name, byte[] bytes)
    {
        var output = Environment.GetEnvironmentVariable("ELIO_PDF_QA_DIR");
        if (string.IsNullOrWhiteSpace(output)) return;
        Directory.CreateDirectory(output); File.WriteAllBytes(Path.Combine(output, name), bytes);
    }
}
