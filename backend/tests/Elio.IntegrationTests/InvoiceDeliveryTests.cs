using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Elio.Application.Invoices;
using Elio.Application.Identity;
using Elio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
namespace Elio.IntegrationTests;

public sealed class CapturedInvoiceEmail : IInvoiceEmailSender
{
    public string Channel => "test-capture";
    public ConcurrentDictionary<Guid, InvoiceEmail> Messages { get; } = new();
    public TaskCompletionSource? Gate { get; set; }
    public TaskCompletionSource? Entered { get; set; }
    public async Task SendAsync(InvoiceEmail email)
    {
        if (email.RecipientEmail == "fail@example.test") throw new Exception("Sensitive provider response must not escape");
        if (email.RecipientEmail == "unconfigured@example.test") throw new InvoiceEmailUnavailableException();
        if (email.RecipientEmail == "pending@example.test") { Entered?.TrySetResult(); if (Gate is not null) await Gate.Task; }
        Messages[email.DeliveryId] = email;
    }
}
public sealed partial class InvoiceTests
{
    [Fact]
    public async Task Accepted_email_with_failed_completion_write_remains_pending_for_reconciliation()
    {
        using var owner = await Account(); var client = await Source(owner, "clients");
        var invoice = await Finalize(owner, await Create(owner, Input(client, Line()))); var path = $"/api/invoices/{invoice.Id}";
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ElioDbContext>();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION reject_delivery_completion() RETURNS trigger LANGUAGE plpgsql AS $body$
            BEGIN IF NEW."Status" = 'Sent' THEN RAISE EXCEPTION 'Simulated completion failure'; END IF; RETURN NEW; END; $body$;
            CREATE TRIGGER reject_delivery_completion BEFORE UPDATE ON "InvoiceDeliveries" FOR EACH ROW EXECUTE FUNCTION reject_delivery_completion();
            """);
        try { Assert.Equal(HttpStatusCode.InternalServerError, (await Send(owner, path + "/send", new { invoice.Version })).StatusCode); }
        finally { await db.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_delivery_completion ON \"InvoiceDeliveries\"; DROP FUNCTION reject_delivery_completion();"); }
        var attempt = Assert.Single((await owner.GetFromJsonAsync<InvoiceDeliveryDto[]>(path + "/deliveries"))!);
        Assert.True(factory.InvoiceMail.Messages.ContainsKey(attempt.Id)); Assert.Equal("Pending", attempt.Status); Assert.Null(attempt.FailureCode);
        Assert.Equal("NotSent", (await owner.GetFromJsonAsync<InvoiceDto>(path))!.DeliveryStatus);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(owner, path + "/send", new { invoice.Version })).StatusCode);
    }
    [Fact]
    public async Task Send_and_resend_audit_snapshot_recipient_and_preserve_exact_invoice_pdf()
    {
        using var owner = await Account(); var client = await Source(owner, "clients");
        var invoice = await Finalize(owner, await Create(owner, Input(client, Line())));
        var path = $"/api/invoices/{invoice.Id}"; var before = await owner.GetByteArrayAsync(path + "/pdf");
        client["email"] = "changed@example.test";
        (await Send(owner, $"/api/clients/{client["id"]}", client, HttpMethod.Patch)).EnsureSuccessStatusCode();
        var first = await Send(owner, path + "/send", new { invoice.Version }); first.EnsureSuccessStatusCode();
        var sent = (await first.Content.ReadFromJsonAsync<InvoiceDeliveryDto>())!;
        Assert.Equal("Sent", sent.Status); Assert.Equal("billing@example.test", sent.RecipientEmail); Assert.NotNull(sent.SentAtUtc);
        var message = factory.InvoiceMail.Messages[sent.Id]; Assert.Equal(before, message.Pdf);
        Assert.Equal("INV-000001.pdf", message.AttachmentFileName); Assert.Contains("15.02 PHP", message.Body);
        Assert.Contains("Invoice INV-000001 from Invoice tests", message.Subject); Assert.Contains("Bank transfer", message.Body);
        (await Send(owner, path + "/send", new { invoice.Version, recipientEmail = "override@example.test" })).EnsureSuccessStatusCode();
        (await Send(owner, path + "/send", new { invoice.Version })).EnsureSuccessStatusCode();
        var history = (await owner.GetFromJsonAsync<InvoiceDeliveryDto[]>(path + "/deliveries"))!;
        Assert.Equal(3, history.Length); Assert.Equal("billing@example.test", history[0].RecipientEmail);
        Assert.Equal("override@example.test", history[1].RecipientEmail); Assert.Equal(history.OrderByDescending(x => x.AttemptedAtUtc), history);
        var after = (await owner.GetFromJsonAsync<InvoiceDto>(path))!;
        Assert.Equal("Finalized", after.Lifecycle); Assert.Equal("Sent", after.DeliveryStatus); Assert.NotNull(after.LastSentAtUtc);
        Assert.Equal(JsonSerializer.Serialize(invoice), JsonSerializer.Serialize(after with { DeliveryStatus = "NotSent", LastSentAtUtc = null }));
        Assert.Equal(before, await owner.GetByteArrayAsync(path + "/pdf"));
        Assert.Single((await owner.GetFromJsonAsync<InvoiceDto[]>("/api/invoices?status=sent&search=INV-000001"))!);
        Assert.Empty((await owner.GetFromJsonAsync<InvoiceDto[]>("/api/invoices?status=finalized"))!);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(owner, path, Edit(after), HttpMethod.Patch)).StatusCode);
    }
    [Theory, InlineData("fail@example.test", "delivery_failed"), InlineData("unconfigured@example.test", "provider_unavailable")]
    public async Task Failed_send_is_durable_safe_and_does_not_mark_sent(string recipient, string code)
    {
        using var owner = await Account(); var client = await Source(owner, "clients"); var invoice = await Finalize(owner, await Create(owner, Input(client, Line())));
        var path = $"/api/invoices/{invoice.Id}";
        var response = await Send(owner, path + "/send", new { invoice.Version, recipientEmail = recipient });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode); Assert.DoesNotContain("Sensitive provider", await response.Content.ReadAsStringAsync());
        var failed = Assert.Single((await owner.GetFromJsonAsync<InvoiceDeliveryDto[]>(path + "/deliveries"))!);
        Assert.Equal("Failed", failed.Status); Assert.Equal(code, failed.FailureCode); Assert.Null(failed.SentAtUtc);
        Assert.Equal("NotSent", (await owner.GetFromJsonAsync<InvoiceDto>(path))!.DeliveryStatus);
        (await Send(owner, path + "/send", new { invoice.Version })).EnsureSuccessStatusCode();
        await Send(owner, path + "/send", new { invoice.Version, recipientEmail = recipient });
        Assert.Equal("Sent", (await owner.GetFromJsonAsync<InvoiceDto>(path))!.DeliveryStatus);
        Assert.Equal(3, (await owner.GetFromJsonAsync<InvoiceDeliveryDto[]>(path + "/deliveries"))!.Length);
    }
    [Fact]
    public async Task Delivery_validates_draft_version_recipient_tenancy_and_csrf()
    {
        using var owner = await Account(); using var foreign = await Account(); var client = await Source(owner, "clients");
        var draft = await Create(owner, Input(client, Line())); var path = $"/api/invoices/{draft.Id}";
        Assert.Equal(HttpStatusCode.Conflict, (await Send(owner, path + "/send", new { draft.Version })).StatusCode);
        var invoice = await Finalize(owner, draft);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(owner, path + "/send", new { version = Guid.NewGuid() })).StatusCode);
        foreach (var recipient in new[] { "", "invalid", "Name <a@example.test>", "a@example.test\r\nBcc: x@example.test" })
            Assert.Equal(HttpStatusCode.BadRequest, (await Send(owner, path + "/send", new { invoice.Version, recipientEmail = recipient })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(path + "/send", new { invoice.Version })).StatusCode);
        foreach (var id in new[] { invoice.Id, Guid.NewGuid() })
        {
            var response = await Send(foreign, $"/api/invoices/{id}/send", new { invoice.Version });
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await foreign.GetAsync($"/api/invoices/{id}/deliveries")).StatusCode);
        }
        using var anonymous = factory.CreateClient(); using var unverified = await Account(false, false); using var noOrganization = await Account(false);
        foreach (var (actor, status) in new[] { (anonymous, HttpStatusCode.Unauthorized), (unverified, HttpStatusCode.Forbidden), (noOrganization, HttpStatusCode.Forbidden) })
        {
            Assert.Equal(status, (await Send(actor, path + "/send", new { invoice.Version })).StatusCode);
            Assert.Equal(status, (await actor.GetAsync(path + "/deliveries")).StatusCode);
        }
        Assert.Empty((await owner.GetFromJsonAsync<InvoiceDeliveryDto[]>(path + "/deliveries"))!);
        var session = (await owner.GetFromJsonAsync<SessionDto>("/api/auth/session"))!;
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ElioDbContext>().Memberships.Where(x => x.UserId == session.User!.Id).ExecuteDeleteAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(owner, path + "/send", new { invoice.Version })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync(path + "/deliveries")).StatusCode);
    }
    [Fact]
    public async Task Pending_attempt_is_durable_and_prevents_overlapping_provider_calls_without_holding_a_transaction()
    {
        using var owner = await Account(); var client = await Source(owner, "clients"); var invoice = await Finalize(owner, await Create(owner, Input(client, Line())));
        var path = $"/api/invoices/{invoice.Id}"; factory.InvoiceMail.Gate = new(); factory.InvoiceMail.Entered = new();
        var sending = Send(owner, path + "/send", new { invoice.Version, recipientEmail = "pending@example.test" });
        try
        {
            await factory.InvoiceMail.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal("Pending", Assert.Single((await owner.GetFromJsonAsync<InvoiceDeliveryDto[]>(path + "/deliveries"))!).Status);
            Assert.Equal("NotSent", (await owner.GetFromJsonAsync<InvoiceDto>(path))!.DeliveryStatus);
            Assert.Equal(HttpStatusCode.Conflict, (await Send(owner, path + "/send", new { invoice.Version })).StatusCode);
        }
        finally { factory.InvoiceMail.Gate.TrySetResult(); }
        (await sending).EnsureSuccessStatusCode(); factory.InvoiceMail.Gate = null; factory.InvoiceMail.Entered = null;
        Assert.Equal("Sent", Assert.Single((await owner.GetFromJsonAsync<InvoiceDeliveryDto[]>(path + "/deliveries"))!).Status);
    }
}
