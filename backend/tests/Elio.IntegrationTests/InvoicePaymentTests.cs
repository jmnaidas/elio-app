using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elio.Application.Identity;
using Elio.Application.Invoices;
using Elio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
namespace Elio.IntegrationTests;
public sealed partial class InvoiceTests
{
    private static object PaymentBody(string amount = "5.00", string method = "BankTransfer") => new
    { amount, receivedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1), method, reference = " REF-001 ", notes = " Partial payment ", currency = "USD", organizationId = Guid.NewGuid() };
    [Fact]
    public async Task Payments_derive_balances_preserve_issued_document_delivery_and_exact_pdf()
    {
        using var owner = await Account(); var client = await Source(owner, "clients");
        var invoice = await Finalize(owner, await Create(owner, Input(client, Line("1", "20.03")))); var path = $"/api/invoices/{invoice.Id}";
        Assert.Equal("Unpaid", (await owner.GetFromJsonAsync<ReceivableDetail>($"/api/receivables/{invoice.Id}"))!.Summary.PaymentStatus);
        var pdf = await owner.GetByteArrayAsync(path + "/pdf");
        var first = await Send(owner, path + "/payments", PaymentBody("5.01")); first.EnsureSuccessStatusCode();
        var partial = (await first.Content.ReadFromJsonAsync<ReceivableDetail>())!;
        Assert.Equal(5.01m, partial.Summary.AmountPaid); Assert.Equal(15.02m, partial.Summary.BalanceDue); Assert.Equal("PartiallyPaid", partial.Summary.PaymentStatus);
        var payment = Assert.Single(partial.Payments); Assert.Equal("PHP", payment.Currency); Assert.Equal("REF-001", payment.Reference); Assert.Equal("Partial payment", payment.Notes);
        Assert.Equal((await owner.GetFromJsonAsync<SessionDto>("/api/auth/session"))!.User!.Id, payment.CreatedBy);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(owner, path + "/payments", PaymentBody("15.03"))).StatusCode);
        // Sent is not required for the first payment; a sent invoice may also receive the remaining balance.
        (await Send(owner, path + "/send", new { invoice.Version })).EnsureSuccessStatusCode();
        var before = await owner.GetStringAsync(path); var deliveries = await owner.GetStringAsync(path + "/deliveries");
        var full = await Send(owner, path + "/payments", PaymentBody("15.02", "Cash")); full.EnsureSuccessStatusCode();
        var paid = (await full.Content.ReadFromJsonAsync<ReceivableDetail>())!;
        Assert.Equal("Paid", paid.Summary.PaymentStatus); Assert.Equal(0, paid.Summary.BalanceDue); Assert.Equal(20.03m, paid.Summary.AmountPaid); Assert.Equal("Sent", paid.Summary.DeliveryStatus);
        Assert.Equal(2, paid.Payments.Count); Assert.Equal("Cash", paid.Payments[0].Method);
        Assert.Equal(paid.Payments.OrderByDescending(x => x.ReceivedAtUtc), paid.Payments);
        Assert.Equal(before, await owner.GetStringAsync(path)); Assert.Equal(deliveries, await owner.GetStringAsync(path + "/deliveries")); Assert.Equal(pdf, await owner.GetByteArrayAsync(path + "/pdf"));
        Assert.Equal(2, (await owner.GetFromJsonAsync<PaymentDto[]>(path + "/payments"))!.Length);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(owner, path + "/payments", PaymentBody("0.01"))).StatusCode);
        foreach (var method in new[] { HttpMethod.Patch, HttpMethod.Delete })
        {
            Assert.Equal(HttpStatusCode.MethodNotAllowed, (await Send(owner, path + "/payments", PaymentBody(), method)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Send(owner, path + $"/payments/{payment.Id}", PaymentBody(), method)).StatusCode);
        }
    }
    [Fact]
    public async Task Payment_validation_rejects_drafts_invalid_amounts_methods_dates_and_missing_fields()
    {
        using var owner = await Account(); var client = await Source(owner, "clients"); var draft = await Create(owner, Input(client, Line()));
        var path = $"/api/invoices/{draft.Id}/payments";
        Assert.Equal(HttpStatusCode.Conflict, (await Send(owner, path, PaymentBody())).StatusCode);
        Assert.Empty((await owner.GetFromJsonAsync<ReceivableDto[]>("/api/receivables"))!);
        await Finalize(owner, draft);
        foreach (var amount in new[] { "0", "-1", "1.001", "1000000000000", "not-money" })
            Assert.Equal(HttpStatusCode.BadRequest, (await Send(owner, path, PaymentBody(amount))).StatusCode);
        foreach (var method in new[] { "", "Stripe", "0", "cash" })
            Assert.Equal(HttpStatusCode.BadRequest, (await Send(owner, path, PaymentBody(method: method))).StatusCode);
        foreach (var body in new object[] { new { }, new { amount = 1, method = "Cash" }, new { amount = 1, method = "Cash", receivedAtUtc = DateTimeOffset.UtcNow.AddDays(1) },
            new { amount = 1, method = "Cash", receivedAtUtc = DateTimeOffset.UtcNow, reference = new string('x', 161) },
            new { amount = 1, method = "Cash", receivedAtUtc = DateTimeOffset.UtcNow, notes = new string('x', 2001) } })
            Assert.Equal(HttpStatusCode.BadRequest, (await Send(owner, path, body)).StatusCode);
        Assert.Empty((await owner.GetFromJsonAsync<PaymentDto[]>(path))!);
    }
    [Fact]
    public async Task Receivables_search_filter_snapshot_and_currency_are_tenant_scoped()
    {
        using var owner = await Account(); using var foreign = await Account(); var client = await Source(owner, "clients", name: "100% Studio");
        var unpaid = await Finalize(owner, await Create(owner, Input(client, Line())));
        var partial = await Finalize(owner, await Create(owner, Input(client, Line())));
        var input = Input(client, Line("1", "0")); input["currency"] = "USD";
        var zero = await Finalize(owner, await Create(owner, input));
        (await Send(owner, $"/api/invoices/{partial.Id}/payments", PaymentBody())).EnsureSuccessStatusCode();
        client["name"] = "Changed client"; (await Send(owner, $"/api/clients/{client["id"]}", client, HttpMethod.Patch)).EnsureSuccessStatusCode();
        Assert.Equal(3, (await owner.GetFromJsonAsync<ReceivableDto[]>("/api/receivables?search=%25"))!.Length);
        Assert.Single((await owner.GetFromJsonAsync<ReceivableDto[]>("/api/receivables?status=Unpaid"))!);
        Assert.Single((await owner.GetFromJsonAsync<ReceivableDto[]>("/api/receivables?status=PartiallyPaid&currency=PHP"))!);
        Assert.Equal(zero.Id, Assert.Single((await owner.GetFromJsonAsync<ReceivableDto[]>("/api/receivables?status=Paid&currency=USD"))!).InvoiceId);
        Assert.Equal(unpaid.Id, Assert.Single((await owner.GetFromJsonAsync<ReceivableDto[]>("/api/receivables?search=" + unpaid.InvoiceNumber))!).InvoiceId);
        Assert.Empty((await owner.GetFromJsonAsync<ReceivableDto[]>("/api/receivables?search=Changed"))!);
        Assert.Empty((await foreign.GetFromJsonAsync<ReceivableDto[]>("/api/receivables"))!);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync("/api/receivables?status=Overdue")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync("/api/receivables?currency=EUR")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(owner, $"/api/invoices/{zero.Id}/payments", PaymentBody("0.01"))).StatusCode);
    }
    [Fact]
    public async Task Payments_enforce_tenancy_authentication_live_membership_and_csrf()
    {
        using var owner = await Account(); using var foreign = await Account(); var client = await Source(owner, "clients");
        var invoice = await Finalize(owner, await Create(owner, Input(client, Line()))); var path = $"/api/invoices/{invoice.Id}/payments";
        foreach (var suffix in new[] { $"/api/receivables/{invoice.Id}", path })
        {
            var response = await foreign.GetAsync(suffix); var missing = await foreign.GetAsync(suffix.Replace(invoice.Id.ToString(), Guid.NewGuid().ToString()));
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode); Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            Assert.Equal((await response.Content.ReadFromJsonAsync<JsonObject>())!["title"]!.ToString(), (await missing.Content.ReadFromJsonAsync<JsonObject>())!["title"]!.ToString());
        }
        foreach (var id in new[] { invoice.Id, Guid.NewGuid() }) Assert.Equal(HttpStatusCode.NotFound, (await Send(foreign, $"/api/invoices/{id}/payments", PaymentBody())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(path, PaymentBody())).StatusCode);
        using var anonymous = factory.CreateClient(); using var unverified = await Account(false, false); using var noOrg = await Account(false);
        foreach (var (actor, expected) in new[] { (anonymous, HttpStatusCode.Unauthorized), (unverified, HttpStatusCode.Forbidden), (noOrg, HttpStatusCode.Forbidden) })
        {
            Assert.Equal(expected, (await actor.GetAsync("/api/receivables")).StatusCode);
            Assert.Equal(expected, (await actor.GetAsync(path)).StatusCode);
            Assert.Equal(expected, (await Send(actor, path, PaymentBody())).StatusCode);
        }
        var session = (await owner.GetFromJsonAsync<SessionDto>("/api/auth/session"))!;
        using var scope = factory.Services.CreateScope(); await scope.ServiceProvider.GetRequiredService<ElioDbContext>().Memberships.Where(x => x.UserId == session.User!.Id).ExecuteDeleteAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync("/api/receivables")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(owner, path, PaymentBody())).StatusCode);
    }
    [Fact]
    public async Task Failed_payment_insert_rolls_back_without_changing_invoice_or_balance()
    {
        using var owner = await Account(); var client = await Source(owner, "clients");
        var invoice = await Finalize(owner, await Create(owner, Input(client, Line()))); var path = $"/api/invoices/{invoice.Id}";
        var before = await owner.GetStringAsync(path);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ElioDbContext>();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION reject_payment_insert() RETURNS trigger LANGUAGE plpgsql AS $body$
            BEGIN RAISE EXCEPTION 'Simulated payment persistence failure'; END; $body$;
            CREATE TRIGGER reject_payment_insert BEFORE INSERT ON "InvoicePayments" FOR EACH ROW EXECUTE FUNCTION reject_payment_insert();
            """);
        try { Assert.Equal(HttpStatusCode.InternalServerError, (await Send(owner, path + "/payments", PaymentBody())).StatusCode); }
        finally { await db.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_payment_insert ON \"InvoicePayments\"; DROP FUNCTION reject_payment_insert();"); }
        var detail = (await owner.GetFromJsonAsync<ReceivableDetail>($"/api/receivables/{invoice.Id}"))!;
        Assert.Empty(detail.Payments); Assert.Equal(0, detail.Summary.AmountPaid); Assert.Equal(invoice.Total, detail.Summary.BalanceDue);
        Assert.Equal(before, await owner.GetStringAsync(path));
        (await Send(owner, path + "/payments", PaymentBody())).EnsureSuccessStatusCode();
    }
    [Fact]
    public async Task Concurrent_payments_cannot_exceed_balance_in_PostgreSQL()
    {
        using var owner = await Account(); var client = await Source(owner, "clients");
        var invoice = await Finalize(owner, await Create(owner, Input(client, Line("1", "1000"))));
        var path = $"/api/invoices/{invoice.Id}/payments";
        var responses = await Task.WhenAll(Send(owner, path, PaymentBody("700")), Send(owner, path, PaymentBody("700")));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
        var result = (await owner.GetFromJsonAsync<ReceivableDetail>($"/api/receivables/{invoice.Id}"))!;
        Assert.Single(result.Payments); Assert.Equal(700, result.Summary.AmountPaid); Assert.Equal(300, result.Summary.BalanceDue);
    }
}
