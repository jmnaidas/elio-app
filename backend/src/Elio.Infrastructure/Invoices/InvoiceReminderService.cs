using System.Globalization;
using Elio.Application.Identity;
using Elio.Application.Invoices;
using Elio.Domain.Invoices;
using Elio.Infrastructure.Catalog;
using Elio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
namespace Elio.Infrastructure.Invoices;

public sealed class InvoiceReminderService(ElioDbContext database, CatalogAccess access, IInvoiceService invoices,
    IInvoicePdfGenerator pdf, IInvoiceEmailSender sender) : IInvoiceReminderService
{
    public async Task<IReadOnlyList<InvoiceReminderDto>> ListAsync(Guid id)
    {
        var organization = await access.OrganizationAsync();
        if (!await database.Invoices.AnyAsync(x => x.OrganizationId == organization && x.Id == id))
            throw new RequestFailure(404, "Invoice not found.");
        return (await database.InvoiceReminders.AsNoTracking().Where(x => x.OrganizationId == organization && x.InvoiceId == id)
            .OrderByDescending(x => x.AttemptedAtUtc).ThenByDescending(x => x.Id).ToListAsync()).Select(Map).ToArray();
    }
    public async Task<InvoiceReminderDto> SendAsync(Guid id, SendReminderRequest request)
    {
        var organization = await access.OrganizationAsync();
        InvoiceReminder? attempt = null;
        // Serialize the eligibility/balance snapshot with Phase 6 payment writers, then release before sending.
        await using (var transaction = await database.Database.BeginTransactionAsync())
        {
            var invoice = (await database.Invoices.FromSqlInterpolated($"SELECT * FROM \"Invoices\" WHERE \"Id\" = {id} AND \"OrganizationId\" = {organization} FOR UPDATE").ToListAsync()).SingleOrDefault()
                ?? throw new RequestFailure(404, "Invoice not found.");
            if (invoice.Lifecycle != InvoiceLifecycle.Finalized) throw new RequestFailure(409, "Finalize this invoice before sending a reminder.");
            var paid = await database.InvoicePayments.Where(x => x.OrganizationId == organization && x.InvoiceId == id).SumAsync(x => x.Amount);
            if (paid >= invoice.Total) throw new RequestFailure(409, "This invoice has no outstanding balance. Refresh its payments.");
            CatalogAccess.Validate(() => attempt = new InvoiceReminder(invoice, request.RecipientEmail ?? invoice.IssuedClientEmail, sender.Channel, paid));
            database.InvoiceReminders.Add(attempt!);
            try { await database.SaveChangesAsync(); }
            catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            { throw new RequestFailure(409, "A reminder is already pending. Refresh reminder history before retrying."); }
            await transaction.CommitAsync();
        }
        var stage = "pdf_failed";
        try
        {
            var snapshot = await invoices.GetAsync(id);
            var bytes = pdf.Generate(snapshot);
            string Money(decimal value) => value.ToString("N2", CultureInfo.InvariantCulture) + " " + snapshot.Currency;
            var subject = $"Payment reminder: {snapshot.InvoiceNumber} from {snapshot.SellerName}".Replace('\r', ' ').Replace('\n', ' ');
            var body = $"Hello {snapshot.ClientName},\n\nThis is a payment reminder for invoice {snapshot.InvoiceNumber}.\n" +
                $"Due: {snapshot.DueDate:yyyy-MM-dd}\nInvoice total: {Money(snapshot.Total)}\n" +
                $"Amount paid: {Money(attempt!.AmountPaid)}\nOutstanding balance: {Money(attempt.BalanceDue)}\n" +
                $"Balance as recorded at {attempt.AttemptedAtUtc:yyyy-MM-dd HH:mm:ss} UTC. If you have paid since then, please disregard this reminder.\n\n" +
                (string.IsNullOrWhiteSpace(snapshot.PaymentInstructions) ? "" : $"Payment instructions:\n{snapshot.PaymentInstructions}\n\n") +
                $"The original issued invoice is attached.\n\nThank you,\n{snapshot.SellerName}";
            stage = "delivery_failed";
            await sender.SendAsync(new(attempt.Id, attempt.RecipientEmail, subject, body, snapshot.InvoiceNumber!, snapshot.InvoiceNumber + ".pdf", bytes));
        }
        catch (Exception error)
        {
            var code = error is InvoiceEmailUnavailableException ? "provider_unavailable" : stage;
            attempt!.Fail(code); await database.SaveChangesAsync();
            throw new RequestFailure(503, code == "provider_unavailable" ? "Invoice email delivery is not configured." : "Reminder delivery failed. Review reminder history before retrying.");
        }
        // Provider acceptance followed by persistence failure must remain Pending/unknown, never falsely Failed.
        attempt!.Succeed(); await database.SaveChangesAsync(); await database.Entry(attempt).ReloadAsync();
        return Map(attempt);
    }
    private static InvoiceReminderDto Map(InvoiceReminder x) => new(x.Id, x.RecipientEmail, x.AttemptedAtUtc,
        x.SentAtUtc, x.Status.ToString(), x.Channel, x.FailureCode, x.AmountPaid, x.BalanceDue);
}
