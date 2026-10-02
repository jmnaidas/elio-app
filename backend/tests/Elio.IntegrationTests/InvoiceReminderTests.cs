using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Elio.Application.Identity;
using Elio.Application.Invoices;
using Elio.Domain.Invoices;
using Elio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
namespace Elio.IntegrationTests;

public sealed partial class InvoiceTests
{
    [Fact]
    public async Task Due_states_combine_filters_and_remove_paid_from_overdue()
    {
        using var owner = await Account(); var client = await Source(owner, "clients");
        var today = DueSummary.BusinessDate(DateTimeOffset.UtcNow, "Asia/Manila");
        var ids = new List<Guid>();
        foreach (var offset in new[] { -7, 0, 7, 8 })
        {
            var input = Input(client, Line("1", "100")); input["issueDate"] = today.AddDays(-30).ToString("yyyy-MM-dd"); input["dueDate"] = today.AddDays(offset).ToString("yyyy-MM-dd");
            ids.Add((await Finalize(owner, await Create(owner, input))).Id);
        }
        (await Send(owner, $"/api/invoices/{ids[0]}/payments", PaymentBody("25"))).EnsureSuccessStatusCode();
        var overdue = Assert.Single((await owner.GetFromJsonAsync<ReceivableDto[]>("/api/receivables?dueState=Overdue&status=PartiallyPaid&currency=PHP&search=Example"))!);
        Assert.Equal(7, overdue.DaysOverdue); Assert.Equal(today, overdue.AsOfDate); Assert.Equal("Asia/Manila", overdue.TimeZone);
        foreach (var state in new[] { "DueToday", "DueSoon", "NotDue" }) Assert.Single((await owner.GetFromJsonAsync<ReceivableDto[]>("/api/receivables?dueState=" + state))!);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync("/api/receivables?dueState=invalid")).StatusCode);
        (await Send(owner, $"/api/invoices/{ids[0]}/payments", PaymentBody("75"))).EnsureSuccessStatusCode();
        Assert.Empty((await owner.GetFromJsonAsync<ReceivableDto[]>("/api/receivables?dueState=Overdue"))!);
        var paid = (await owner.GetFromJsonAsync<ReceivableDetail>($"/api/receivables/{ids[0]}"))!;
        Assert.Equal("Paid", paid.Summary.DueState); Assert.Equal(0, paid.Summary.DaysOverdue);
    }
    [Fact]
    public async Task Reminder_uses_frozen_recipient_balance_and_pdf_without_mutating_other_history()
    {
        using var owner = await Account(); var client = await Source(owner, "clients");
        var invoice = await Finalize(owner, await Create(owner, Input(client, Line("1", "100")))); var path = $"/api/invoices/{invoice.Id}";
        (await Send(owner, path + "/payments", PaymentBody("25"))).EnsureSuccessStatusCode();
        client["email"] = "changed@example.test"; (await Send(owner, $"/api/clients/{client["id"]}", client, HttpMethod.Patch)).EnsureSuccessStatusCode();
        var before = await owner.GetStringAsync(path); var payments = await owner.GetStringAsync(path + "/payments"); var deliveries = await owner.GetStringAsync(path + "/deliveries"); var pdf = await owner.GetByteArrayAsync(path + "/pdf");
        var response = await Send(owner, path + "/reminders", new { }); response.EnsureSuccessStatusCode();
        var sent = (await response.Content.ReadFromJsonAsync<InvoiceReminderDto>())!;
        Assert.Equal("Sent", sent.Status); Assert.Equal("billing@example.test", sent.RecipientEmail); Assert.Equal(25, sent.AmountPaid); Assert.Equal(75, sent.BalanceDue);
        var message = factory.InvoiceMail.Messages[sent.Id]; Assert.Equal(pdf, message.Pdf); Assert.Contains("Payment reminder:", message.Subject);
        Assert.Contains("Amount paid: 25.00 PHP", message.Body); Assert.Contains("Outstanding balance: 75.00 PHP", message.Body); Assert.Contains("Bank transfer", message.Body); Assert.Contains("Due: 2026-09-30", message.Body);
        (await Send(owner, path + "/reminders", new { recipientEmail = "override@example.test" })).EnsureSuccessStatusCode();
        var history = (await owner.GetFromJsonAsync<InvoiceReminderDto[]>(path + "/reminders"))!;
        Assert.Equal(2, history.Length); Assert.Equal("override@example.test", history[0].RecipientEmail);
        Assert.Equal(before, await owner.GetStringAsync(path)); Assert.Equal(payments, await owner.GetStringAsync(path + "/payments")); Assert.Equal(deliveries, await owner.GetStringAsync(path + "/deliveries")); Assert.Equal(pdf, await owner.GetByteArrayAsync(path + "/pdf"));
        foreach (var method in new[] { HttpMethod.Patch, HttpMethod.Delete }) Assert.Equal(HttpStatusCode.MethodNotAllowed, (await Send(owner, path + "/reminders", new { }, method)).StatusCode);
    }
    [Fact]
    public async Task Reminders_enforce_eligibility_validation_tenant_boundary_and_live_security()
    {
        using var owner = await Account(); using var foreign = await Account(); var client = await Source(owner, "clients");
        var draft = await Create(owner, Input(client, Line("1", "100"))); var path = $"/api/invoices/{draft.Id}/reminders";
        Assert.Equal(HttpStatusCode.Conflict, (await Send(owner, path, new { })).StatusCode); await Finalize(owner, draft);
        foreach (var recipient in new[] { "", "invalid", "Name <a@example.test>", "a@example.test\r\nBcc: x@example.test", new string('x',255) })
            Assert.Equal(HttpStatusCode.BadRequest, (await Send(owner, path, new { recipientEmail = recipient })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(path, new { })).StatusCode);
        foreach (var method in new[] { HttpMethod.Get, HttpMethod.Post })
        {
            var response = await Send(foreign, path, new { }, method); var missing = await Send(foreign, $"/api/invoices/{Guid.NewGuid()}/reminders", new { }, method);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode); Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            Assert.Equal((await response.Content.ReadFromJsonAsync<JsonObject>())!["title"]!.ToString(), (await missing.Content.ReadFromJsonAsync<JsonObject>())!["title"]!.ToString());
        }
        using var anonymous = factory.CreateClient(); using var unverified = await Account(false, false); using var noOrg = await Account(false);
        foreach (var (actor, expected) in new[] { (anonymous, HttpStatusCode.Unauthorized), (unverified, HttpStatusCode.Forbidden), (noOrg, HttpStatusCode.Forbidden) })
        { Assert.Equal(expected, (await actor.GetAsync(path)).StatusCode); Assert.Equal(expected, (await Send(actor, path, new { })).StatusCode); }
        (await Send(owner, $"/api/invoices/{draft.Id}/payments", PaymentBody("100"))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await Send(owner, path, new { })).StatusCode); Assert.Empty((await owner.GetFromJsonAsync<InvoiceReminderDto[]>(path))!);
        var session = (await owner.GetFromJsonAsync<SessionDto>("/api/auth/session"))!;
        using var scope = factory.Services.CreateScope(); await scope.ServiceProvider.GetRequiredService<ElioDbContext>().Memberships.Where(x => x.UserId == session.User!.Id).ExecuteDeleteAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync(path)).StatusCode); Assert.Equal(HttpStatusCode.Forbidden, (await Send(owner, path, new { })).StatusCode);
    }
    [Theory, InlineData("fail@example.test", "delivery_failed"), InlineData("unconfigured@example.test", "provider_unavailable")]
    public async Task Reminder_failure_is_audited_with_safe_diagnostics(string recipient, string code)
    {
        using var owner = await Account(); var client = await Source(owner, "clients"); var invoice = await Finalize(owner, await Create(owner, Input(client, Line()))); var path = $"/api/invoices/{invoice.Id}";
        var response = await Send(owner, path + "/reminders", new { recipientEmail = recipient });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode); Assert.DoesNotContain("Sensitive provider", await response.Content.ReadAsStringAsync());
        var attempt = Assert.Single((await owner.GetFromJsonAsync<InvoiceReminderDto[]>(path + "/reminders"))!);
        Assert.Equal("Failed", attempt.Status); Assert.Equal(code, attempt.FailureCode); Assert.Null(attempt.SentAtUtc);
        Assert.Equal("NotSent", (await owner.GetFromJsonAsync<InvoiceDto>(path))!.DeliveryStatus);
        (await Send(owner, path + "/reminders", new { })).EnsureSuccessStatusCode();
    }
    [Fact]
    public async Task Pending_reminder_blocks_duplicates_but_does_not_block_payment_and_keeps_confirmation_snapshot()
    {
        using var owner = await Account(); var client = await Source(owner, "clients"); var invoice = await Finalize(owner, await Create(owner, Input(client, Line("1", "100")))); var path = $"/api/invoices/{invoice.Id}";
        factory.InvoiceMail.Gate = new(); factory.InvoiceMail.Entered = new();
        var sending = Send(owner, path + "/reminders", new { recipientEmail = "pending@example.test" });
        try
        {
            await factory.InvoiceMail.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal("Pending", Assert.Single((await owner.GetFromJsonAsync<InvoiceReminderDto[]>(path + "/reminders"))!).Status);
            Assert.Equal(HttpStatusCode.Conflict, (await Send(owner, path + "/reminders", new { })).StatusCode);
            (await Send(owner, path + "/payments", PaymentBody("100")).WaitAsync(TimeSpan.FromSeconds(10))).EnsureSuccessStatusCode();
        }
        finally { factory.InvoiceMail.Gate.TrySetResult(); }
        (await sending).EnsureSuccessStatusCode(); factory.InvoiceMail.Gate = null; factory.InvoiceMail.Entered = null;
        var attempt = Assert.Single((await owner.GetFromJsonAsync<InvoiceReminderDto[]>(path + "/reminders"))!);
        Assert.Equal(100, attempt.BalanceDue); Assert.Equal("Sent", attempt.Status);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(owner, path + "/reminders", new { })).StatusCode);
    }
    [Fact]
    public async Task Accepted_reminder_with_failed_completion_write_stays_pending()
    {
        using var owner = await Account(); var client = await Source(owner, "clients"); var invoice = await Finalize(owner, await Create(owner, Input(client, Line()))); var path = $"/api/invoices/{invoice.Id}/reminders";
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ElioDbContext>();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION reject_reminder_completion() RETURNS trigger LANGUAGE plpgsql AS $body$
            BEGIN IF NEW."Status" = 'Sent' THEN RAISE EXCEPTION 'Simulated completion failure'; END IF; RETURN NEW; END; $body$;
            CREATE TRIGGER reject_reminder_completion BEFORE UPDATE ON "InvoiceReminders" FOR EACH ROW EXECUTE FUNCTION reject_reminder_completion();
            """);
        try { Assert.Equal(HttpStatusCode.InternalServerError, (await Send(owner, path, new { })).StatusCode); }
        finally { await db.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_reminder_completion ON \"InvoiceReminders\"; DROP FUNCTION reject_reminder_completion();"); }
        var attempt = Assert.Single((await owner.GetFromJsonAsync<InvoiceReminderDto[]>(path))!);
        Assert.True(factory.InvoiceMail.Messages.ContainsKey(attempt.Id)); Assert.Equal("Pending", attempt.Status); Assert.Null(attempt.FailureCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(owner, path, new { })).StatusCode);
    }
}
