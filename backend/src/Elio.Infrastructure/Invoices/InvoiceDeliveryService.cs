using System.Globalization;
using Elio.Application.Identity;
using Elio.Application.Invoices;
using Elio.Domain.Invoices;
using Elio.Infrastructure.Catalog;
using Elio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
namespace Elio.Infrastructure.Invoices;

public sealed class InvoiceDeliveryService(ElioDbContext database, CatalogAccess access, IInvoiceService invoices,
    IInvoicePdfGenerator pdf, IInvoiceEmailSender sender) : IInvoiceDeliveryService
{
    public async Task<IReadOnlyList<InvoiceDeliveryDto>> ListAsync(Guid id)
    {
        var organization = await access.OrganizationAsync();
        if (!await database.Invoices.AnyAsync(x => x.OrganizationId == organization && x.Id == id))
            throw new RequestFailure(404, "Invoice not found.");
        return (await database.InvoiceDeliveries.AsNoTracking().Where(x => x.OrganizationId == organization && x.InvoiceId == id)
            .OrderByDescending(x => x.AttemptedAtUtc).ThenByDescending(x => x.Id).ToListAsync()).Select(Map).ToArray();
    }
    public async Task<InvoiceDeliveryDto> SendAsync(Guid id, SendInvoiceRequest request)
    {
        var organization = await access.OrganizationAsync();
        var invoice = await database.Invoices.SingleOrDefaultAsync(x => x.OrganizationId == organization && x.Id == id)
            ?? throw new RequestFailure(404, "Invoice not found.");
        if (invoice.Lifecycle != InvoiceLifecycle.Finalized) throw new RequestFailure(409, "Finalize this invoice before sending it.");
        CatalogAccess.CheckVersion(invoice.Version, request.Version);
        InvoiceDelivery? attempt = null;
        CatalogAccess.Validate(() => attempt = new InvoiceDelivery(invoice, request.RecipientEmail ?? invoice.IssuedClientEmail, sender.Channel));
        database.InvoiceDeliveries.Add(attempt!);
        try { await database.SaveChangesAsync(); }
        catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { throw new RequestFailure(409, "A delivery attempt is already pending. Refresh delivery history before retrying."); }
        // The attempt is durable before any provider call. No transaction is open during PDF generation or sending.
        var stage = "pdf_failed";
        try
        {
            var snapshot = await invoices.GetAsync(id);
            var bytes = pdf.Generate(snapshot);
            var total = snapshot.Total.ToString("N2", CultureInfo.InvariantCulture) + " " + snapshot.Currency;
            var subject = $"Invoice {snapshot.InvoiceNumber} from {snapshot.SellerName}".Replace('\r', ' ').Replace('\n', ' ');
            var body = $"Hello {snapshot.ClientName},\n\nPlease find your invoice {snapshot.InvoiceNumber} attached.\n" +
                $"Issued: {snapshot.IssueDate:yyyy-MM-dd}\nDue: {snapshot.DueDate:yyyy-MM-dd}\nTotal: {total}\n\n" +
                (string.IsNullOrWhiteSpace(snapshot.PaymentInstructions) ? "" : $"Payment instructions:\n{snapshot.PaymentInstructions}\n\n") +
                $"Thank you,\n{snapshot.SellerName}";
            stage = "delivery_failed";
            await sender.SendAsync(new(attempt!.Id, attempt.RecipientEmail, subject, body, snapshot.InvoiceNumber!, snapshot.InvoiceNumber + ".pdf", bytes));
        }
        catch (Exception error)
        {
            var code = error is InvoiceEmailUnavailableException ? "provider_unavailable" : stage;
            attempt!.Fail(code); await database.SaveChangesAsync();
            throw new RequestFailure(503, code == "provider_unavailable" ? "Invoice email delivery is not configured." : "Invoice delivery failed. Review delivery history before retrying.");
        }
        // Do not classify a persistence failure after provider acceptance as a failed send: leave Pending for reconciliation.
        attempt!.Succeed(); await database.SaveChangesAsync(); await database.Entry(attempt).ReloadAsync();
        return Map(attempt);
    }
    private static InvoiceDeliveryDto Map(InvoiceDelivery x) => new(x.Id, x.RecipientEmail, x.AttemptedAtUtc, x.SentAtUtc, x.Status.ToString(), x.Channel, x.FailureCode);
}
