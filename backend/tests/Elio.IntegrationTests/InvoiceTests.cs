using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elio.Application.Identity;
using Elio.Application.Invoices;
using Elio.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
namespace Elio.IntegrationTests;

public sealed class InvoiceTests(PostgresFactory factory) : IClassFixture<PostgresFactory>
{
    private static async Task<HttpResponseMessage> Send(HttpClient client, string path, object body, HttpMethod? method = null)
    {
        var token = await client.GetFromJsonAsync<JsonElement>("/api/auth/antiforgery");
        using var request = new HttpRequestMessage(method ?? HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-XSRF-TOKEN", token.GetProperty("requestToken").GetString());
        return await client.SendAsync(request);
    }
    private async Task<HttpClient> Account(bool organization = true, bool verified = true)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var email = $"invoice-{Guid.NewGuid():N}@example.test"; const string password = "Invoice-Test-123!";
        (await Send(client, "/api/auth/register", new RegisterRequest(email, password, password))).EnsureSuccessStatusCode();
        if (verified) (await Send(client, "/api/auth/verify-email", factory.Mail.Token(email))).EnsureSuccessStatusCode();
        (await Send(client, "/api/auth/login", new LoginRequest(email, password))).EnsureSuccessStatusCode();
        if (organization) (await Send(client, "/api/organization", new OrganizationRequest("Invoice tests", "Asia/Manila", "PHP"))).EnsureSuccessStatusCode();
        return client;
    }
    private static async Task<JsonObject> Source(HttpClient owner, string kind, string currency = "PHP", string name = "Example client")
    {
        object input = kind == "clients" ? new { name, email = "billing@example.test", currency } :
            new { name = "Consulting", description = "Original consulting", defaultUnitPrice = "10.01", currency };
        var response = await Send(owner, "/api/" + kind, input); response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonObject>())!;
    }
    private static JsonObject Line(string quantity = "1.5", string price = "10.01", string? serviceId = null) => new()
    {
        ["description"] = "Copied work", ["quantity"] = quantity, ["unitPrice"] = price, ["serviceId"] = serviceId,
        ["lineTotal"] = "999999", ["sortOrder"] = 99 // ignored: backend owns derived values and ordering
    };
    private static JsonObject Input(JsonObject client, params JsonObject[] lines) => new()
    {
        ["clientId"] = client["id"]!.GetValue<string>(), ["currency"] = "PHP", ["issueDate"] = "2026-09-23",
        ["dueDate"] = "2026-09-30", ["notes"] = " Draft note ", ["paymentInstructions"] = " Bank transfer ",
        ["lines"] = new JsonArray(lines.Select(x => (JsonNode)x).ToArray()), ["total"] = "1", ["lifecycle"] = "Finalized",
        ["organizationId"] = Guid.NewGuid().ToString(), ["invoiceNumber"] = "IGNORED"
    };
    private static async Task<InvoiceDto> Create(HttpClient owner, JsonObject input)
    {
        var response = await Send(owner, "/api/invoices", input);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = (await response.Content.ReadFromJsonAsync<InvoiceDto>())!;
        Assert.EndsWith(dto.Id.ToString(), response.Headers.Location!.ToString());
        return dto;
    }
    private static JsonObject Edit(InvoiceDto invoice) => new()
    {
        ["clientId"] = invoice.ClientId.ToString(), ["currency"] = invoice.Currency,
        ["issueDate"] = invoice.IssueDate.ToString("yyyy-MM-dd"), ["dueDate"] = invoice.DueDate.ToString("yyyy-MM-dd"),
        ["notes"] = invoice.Notes, ["paymentInstructions"] = invoice.PaymentInstructions, ["version"] = invoice.Version.ToString(),
        ["lines"] = new JsonArray(invoice.Lines.Select(x => (JsonNode)new JsonObject
        {
            ["id"] = x.Id.ToString(), ["serviceId"] = x.ServiceId?.ToString(), ["description"] = x.Description,
            ["quantity"] = x.Quantity, ["unitPrice"] = x.UnitPrice
        }).ToArray())
    };
    [Fact]
    public async Task Draft_create_read_update_calculates_authoritative_totals_and_replaces_ordered_lines()
    {
        using var owner = await Account(); var client = await Source(owner, "clients"); var service = await Source(owner, "services");
        var draft = await Create(owner, Input(client, Line(serviceId: service["id"]!.GetValue<string>()), Line("0.25", "0.02"), Line("1", "0")));
        Assert.Equal(15.03m, draft.Total); Assert.Equal(draft.Total, draft.Subtotal); Assert.Equal("Draft", draft.Lifecycle);
        Assert.Equal(15.02m, draft.Lines[0].LineTotal); Assert.Equal(0.01m, draft.Lines[1].LineTotal);
        Assert.Equal(new[] { 0, 1, 2 }, draft.Lines.Select(x => x.SortOrder));
        var path = $"/api/invoices/{draft.Id}";
        var read = (await owner.GetFromJsonAsync<InvoiceDto>(path))!;
        Assert.Equal(draft.Total, read.Total); Assert.Equal("Draft note", read.Notes);
        var update = Edit(read); var lines = (JsonArray)update["lines"]!;
        var moved = lines[1]!.DeepClone(); lines.RemoveAt(1); lines.Insert(0, moved); lines.RemoveAt(2);
        lines.Add(Line("2", "3")); lines[1]!["description"] = "Independent edit";
        update["dueDate"] = "2026-10-01";
        var response = await Send(owner, path, update, HttpMethod.Patch); response.EnsureSuccessStatusCode();
        var saved = (await response.Content.ReadFromJsonAsync<InvoiceDto>())!;
        Assert.Equal(21.03m, saved.Total); Assert.Equal(read.Lines[1].Id, saved.Lines[0].Id);
        Assert.Equal("Independent edit", saved.Lines[1].Description); Assert.NotEqual(read.Version, saved.Version);
        Assert.Equal(read.CreatedAtUtc, saved.CreatedAtUtc); Assert.Equal(new DateOnly(2026, 10, 1), saved.DueDate);
        var persisted = (await owner.GetFromJsonAsync<InvoiceDto>(path))!;
        Assert.Equal(saved.Lines.Select(x => x.Id), persisted.Lines.Select(x => x.Id));
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ElioDbContext>();
        Assert.Equal(3, await db.InvoiceLines.CountAsync(x => x.InvoiceId == draft.Id));
        Assert.False(await db.InvoiceLines.AnyAsync(x => x.Id == read.Lines[2].Id));
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await Send(owner, path, new { }, HttpMethod.Delete)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(owner, path + "/finalize", new { })).StatusCode);
    }
    [Fact]
    public async Task Foreign_invoices_clients_services_and_line_ids_cannot_be_used()
    {
        using var a = await Account(); using var b = await Account();
        var ca = await Source(a, "clients"); var cb = await Source(b, "clients"); var sb = await Source(b, "services");
        var draft = await Create(a, Input(ca, Line())); var foreignDraft = await Create(b, Input(cb, Line()));
        var list = (await b.GetFromJsonAsync<InvoiceDto[]>("/api/invoices"))!;
        Assert.DoesNotContain(list, x => x.Id == draft.Id);
        var foreign = await b.GetAsync($"/api/invoices/{draft.Id}"); var missing = await b.GetAsync($"/api/invoices/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal((await foreign.Content.ReadFromJsonAsync<JsonObject>())!["title"]!.ToString(), (await missing.Content.ReadFromJsonAsync<JsonObject>())!["title"]!.ToString());
        Assert.Equal(HttpStatusCode.NotFound, (await Send(b, $"/api/invoices/{draft.Id}", Edit(draft), HttpMethod.Patch)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(a, "/api/invoices", Input(cb, Line()))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(a, "/api/invoices", Input(ca, Line(serviceId: sb["id"]!.GetValue<string>())))).StatusCode);
        var update = Edit(draft); update["clientId"] = cb["id"]!.GetValue<string>();
        Assert.Equal(HttpStatusCode.NotFound, (await Send(a, $"/api/invoices/{draft.Id}", update, HttpMethod.Patch)).StatusCode);
        update = Edit(draft); update["lines"]![0]!["serviceId"] = sb["id"]!.GetValue<string>();
        Assert.Equal(HttpStatusCode.NotFound, (await Send(a, $"/api/invoices/{draft.Id}", update, HttpMethod.Patch)).StatusCode);
        update = Edit(draft); update["lines"]![0]!["id"] = foreignDraft.Lines[0].Id.ToString();
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(a, $"/api/invoices/{draft.Id}", update, HttpMethod.Patch)).StatusCode);
        var org = (await a.GetFromJsonAsync<SessionDto>("/api/auth/session"))!.Organization!;
        b.DefaultRequestHeaders.Add("X-Organization-ID", org.Id.ToString());
        Assert.Equal(HttpStatusCode.Forbidden, (await b.GetAsync("/api/invoices")).StatusCode);
    }
    [Fact]
    public async Task Stale_update_is_conflict_and_cannot_partially_change_lines()
    {
        using var owner = await Account(); var client = await Source(owner, "clients");
        var draft = await Create(owner, Input(client, Line())); var update = Edit(draft); update["lines"]![0]!["unitPrice"] = "20";
        (await Send(owner, $"/api/invoices/{draft.Id}", update, HttpMethod.Patch)).EnsureSuccessStatusCode();
        update["lines"] = new JsonArray(Line("1", "99"));
        Assert.Equal(HttpStatusCode.Conflict, (await Send(owner, $"/api/invoices/{draft.Id}", update, HttpMethod.Patch)).StatusCode);
        var read = (await owner.GetFromJsonAsync<InvoiceDto>($"/api/invoices/{draft.Id}"))!;
        Assert.Equal(30, read.Total); Assert.Equal(draft.Lines[0].Id, read.Lines[0].Id);
    }
    [Fact]
    public async Task Inactive_and_changed_sources_preserve_copies_and_do_not_block_existing_draft_edits()
    {
        using var owner = await Account(); var client = await Source(owner, "clients"); var service = await Source(owner, "services");
        var draft = await Create(owner, Input(client, Line(serviceId: service["id"]!.GetValue<string>())));
        service["description"] = "Changed source"; service["currency"] = "USD"; service["defaultUnitPrice"] = "99";
        var edited = await Send(owner, $"/api/services/{service["id"]}", service, HttpMethod.Patch); edited.EnsureSuccessStatusCode();
        service = (await edited.Content.ReadFromJsonAsync<JsonObject>())!;
        foreach (var (kind, source) in new[] { ("clients", client), ("services", service) })
            (await Send(owner, $"/api/{kind}/{source["id"]}/deactivate", new { version = source["version"]!.GetValue<string>() })).EnsureSuccessStatusCode();
        var read = (await owner.GetFromJsonAsync<InvoiceDto>($"/api/invoices/{draft.Id}"))!;
        Assert.False(read.ClientIsActive); Assert.Equal("Copied work", read.Lines[0].Description); Assert.Equal(10.01m, read.Lines[0].UnitPrice);
        var update = Edit(read); update["notes"] = "Still editable";
        var result = await Send(owner, $"/api/invoices/{draft.Id}", update, HttpMethod.Patch); result.EnsureSuccessStatusCode();
        Assert.Equal(15.02m, (await result.Content.ReadFromJsonAsync<InvoiceDto>())!.Total);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(owner, "/api/invoices", Input(client, Line()))).StatusCode);
        var activeClient = await Source(owner, "clients");
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(owner, "/api/invoices", Input(activeClient, Line(serviceId: service["id"]!.GetValue<string>())))).StatusCode);
    }
    [Fact]
    public async Task Currency_mismatch_is_rejected_on_new_selection_and_currency_change()
    {
        using var owner = await Account(); var client = await Source(owner, "clients"); var service = await Source(owner, "services", "USD");
        var input = Input(client, Line(serviceId: service["id"]!.GetValue<string>()));
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(owner, "/api/invoices", input)).StatusCode);
        input["currency"] = "USD"; var draft = await Create(owner, input);
        var update = Edit(draft); update["currency"] = "PHP";
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(owner, $"/api/invoices/{draft.Id}", update, HttpMethod.Patch)).StatusCode);
        update["lines"]![0]!["serviceId"] = null;
        (await Send(owner, $"/api/invoices/{draft.Id}", update, HttpMethod.Patch)).EnsureSuccessStatusCode();
    }
    [Fact]
    public async Task Search_filters_by_client_name_email_id_and_currency()
    {
        using var owner = await Account(); var client = await Source(owner, "clients", name: "100% Studio");
        await Create(owner, Input(client, Line())); var usd = Input(client, Line()); usd["currency"] = "USD"; await Create(owner, usd);
        Assert.Equal(2, (await owner.GetFromJsonAsync<InvoiceDto[]>("/api/invoices?search=STUDIO"))!.Length);
        Assert.Equal(2, (await owner.GetFromJsonAsync<InvoiceDto[]>("/api/invoices?search=%25"))!.Length);
        Assert.Single((await owner.GetFromJsonAsync<InvoiceDto[]>("/api/invoices?search=BILLING%40EXAMPLE&currency=USD"))!);
        Assert.Equal(2, (await owner.GetFromJsonAsync<InvoiceDto[]>($"/api/invoices?clientId={client["id"]}"))!.Length);
        Assert.Empty((await owner.GetFromJsonAsync<InvoiceDto[]>($"/api/invoices?clientId={Guid.NewGuid()}"))!);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync("/api/invoices?currency=EUR")).StatusCode);
    }
    [Fact]
    public async Task Authentication_verification_membership_and_csrf_are_required()
    {
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/invoices")).StatusCode);
        using var unverified = await Account(false, false); using var noOrg = await Account(false);
        foreach (var client in new[] { unverified, noOrg }) Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/invoices")).StatusCode);
        using var owner = await Account(); var source = await Source(owner, "clients"); var input = Input(source, Line());
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync("/api/invoices", input)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(anonymous, "/api/invoices", input)).StatusCode);
        var draft = await Create(owner, input);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PatchAsJsonAsync($"/api/invoices/{draft.Id}", Edit(draft))).StatusCode);
        var session = (await owner.GetFromJsonAsync<SessionDto>("/api/auth/session"))!;
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ElioDbContext>().Memberships.Where(x => x.Id == session.Membership!.Id).ExecuteDeleteAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync($"/api/invoices/{draft.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(owner, $"/api/invoices/{draft.Id}", Edit(draft), HttpMethod.Patch)).StatusCode);
    }
    [Fact]
    public async Task Invalid_headers_lines_and_excess_precision_return_problem_details()
    {
        using var owner = await Account(); var client = await Source(owner, "clients");
        var invalid = new List<JsonObject>();
        foreach (var (key, value) in new[] { ("clientId", Guid.Empty.ToString()), ("currency", "EUR"), ("issueDate", "not-a-date"), ("dueDate", "2026-09-01") })
        { var input = Input(client, Line()); input[key] = value; invalid.Add(input); }
        foreach (var (key, value) in new[] { ("description", " "), ("quantity", "0"), ("quantity", "-1"), ("quantity", "1.00001"), ("unitPrice", "-1"), ("unitPrice", "1.001") })
        { var input = Input(client, Line()); input["lines"]![0]![key] = value; invalid.Add(input); }
        var empty = Input(client); invalid.Add(empty);
        var nullLine = Input(client); nullLine["lines"] = new JsonArray((JsonNode?)null); invalid.Add(nullLine);
        foreach (var input in invalid)
        {
            var response = await Send(owner, "/api/invoices", input);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        }
        Assert.Empty((await owner.GetFromJsonAsync<InvoiceDto[]>("/api/invoices"))!);
    }
}
