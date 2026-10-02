using Elio.Application.Identity;
using Elio.Application.Invoices;
using Elio.Domain.Invoices;
using Elio.Infrastructure.Catalog;
using Elio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace Elio.Infrastructure.Invoices;

public sealed class ReceivableService(ElioDbContext database, CatalogAccess access, ICurrentOrganization current, TimeProvider clock) : IReceivableService
{
    public async Task<IReadOnlyList<ReceivableDto>> ListAsync(ReceivableQuery request)
    {
        var organization = await access.OrganizationAsync();
        var calendar = await Calendar(organization);
        if (!string.IsNullOrEmpty(request.DueState) && request.DueState is not ("all" or "NotDue" or "DueSoon" or "DueToday" or "Overdue"))
            throw new RequestFailure(400, "Choose all, NotDue, DueSoon, DueToday, or Overdue.");
        if (!string.IsNullOrEmpty(request.Status) && request.Status is not ("all" or "Unpaid" or "PartiallyPaid" or "Paid"))
            throw new RequestFailure(400, "Choose Unpaid, PartiallyPaid, Paid, or all.");
        if (!string.IsNullOrEmpty(request.Currency) && request.Currency is not ("PHP" or "USD"))
            throw new RequestFailure(400, "Choose PHP or USD.");
        var query = database.Invoices.AsNoTracking().Where(x => x.OrganizationId == organization && x.Lifecycle == InvoiceLifecycle.Finalized);
        if (!string.IsNullOrEmpty(request.Currency)) query = query.Where(x => x.Currency == request.Currency);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLowerInvariant();
            query = query.Where(x => x.InvoiceNumber!.ToLower().Contains(term) || x.IssuedClientName!.ToLower().Contains(term));
        }
        // One SQL statement observes totals and delivery state in a consistent database snapshot.
        var rows = await query.OrderBy(x => x.DueDate).ThenBy(x => x.InvoiceNumber).ThenBy(x => x.Id).Select(x => new
        {
            Invoice = x,
            Paid = database.InvoicePayments.Where(p => p.OrganizationId == organization && p.InvoiceId == x.Id).Sum(p => p.Amount),
            Sent = database.InvoiceDeliveries.Any(d => d.OrganizationId == organization && d.InvoiceId == x.Id && d.Status == InvoiceDeliveryStatus.Sent)
        }).ToListAsync();
        return rows.Select(x => Map(x.Invoice, x.Paid, x.Sent, calendar))
            .Where(x => string.IsNullOrEmpty(request.Status) || request.Status == "all" || request.Status == x.PaymentStatus)
            .Where(x => string.IsNullOrEmpty(request.DueState) || request.DueState == "all" || request.DueState == x.DueState).ToArray();
    }
    public async Task<ReceivableDetail> GetAsync(Guid id)
    {
        var organization = await access.OrganizationAsync();
        var invoice = await database.Invoices.AsNoTracking().SingleOrDefaultAsync(x => x.OrganizationId == organization && x.Id == id)
            ?? throw new RequestFailure(404, "Invoice not found.");
        RequireIssued(invoice);
        return await Detail(invoice);
    }
    public async Task<ReceivableDetail> RecordAsync(Guid id, PaymentRequest request)
    {
        var organization = await access.OrganizationAsync();
        await using var transaction = await database.Database.BeginTransactionAsync();
        // Every payment writer locks this tenant-owned invoice before reading the balance.
        // Read Committed makes the following sum see a competing writer's committed payment after waiting.
        var invoice = (await database.Invoices.FromSqlInterpolated($"SELECT * FROM \"Invoices\" WHERE \"Id\" = {id} AND \"OrganizationId\" = {organization} FOR UPDATE").ToListAsync()).SingleOrDefault()
            ?? throw new RequestFailure(404, "Invoice not found.");
        RequireIssued(invoice);
        var actor = (await current.GetAsync())?.User.Id ?? throw new RequestFailure(401, "Sign in to continue.");
        InvoicePayment? payment = null;
        CatalogAccess.Validate(() => payment = new InvoicePayment(invoice, request.Amount ?? 0, request.ReceivedAtUtc ?? default,
            request.Method, request.Reference, request.Notes, actor));
        var paid = await database.InvoicePayments.Where(x => x.OrganizationId == organization && x.InvoiceId == id).SumAsync(x => x.Amount);
        if (payment!.Amount > invoice.Total - paid) throw new RequestFailure(409, "Payment exceeds the outstanding balance. Refresh before recording another payment.");
        database.InvoicePayments.Add(payment);
        await database.SaveChangesAsync();
        var result = await Detail(invoice);
        await transaction.CommitAsync();
        return result;
    }
    private async Task<ReceivableDetail> Detail(Invoice invoice)
    {
        var payments = await database.InvoicePayments.AsNoTracking().Where(x => x.OrganizationId == invoice.OrganizationId && x.InvoiceId == invoice.Id)
            .OrderByDescending(x => x.ReceivedAtUtc).ThenByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id).ToListAsync();
        var sent = await database.InvoiceDeliveries.AnyAsync(x => x.OrganizationId == invoice.OrganizationId && x.InvoiceId == invoice.Id && x.Status == InvoiceDeliveryStatus.Sent);
        return new(Map(invoice, payments.Sum(x => x.Amount), sent, await Calendar(invoice.OrganizationId)), payments.Select(x => new PaymentDto(x.Id, x.Amount, x.Currency,
            x.ReceivedAtUtc, x.Method.ToString(), x.Reference, x.Notes, x.CreatedAtUtc, x.CreatedBy)).ToArray());
    }
    private async Task<(DateOnly Today, string Zone)> Calendar(Guid organization)
    {
        var zone = await database.Organizations.Where(x => x.Id == organization).Select(x => x.TimeZone).SingleAsync();
        return (DueSummary.BusinessDate(clock.GetUtcNow(), zone), zone);
    }
    private static ReceivableDto Map(Invoice invoice, decimal paid, bool sent, (DateOnly Today, string Zone) calendar)
    {
        var summary = PaymentSummary.Calculate(invoice.Total, paid);
        var due = DueSummary.Calculate(invoice.DueDate, calendar.Today, summary.BalanceDue);
        return new(invoice.Id, invoice.InvoiceNumber!, invoice.IssuedClientName!, invoice.Currency, invoice.IssueDate, invoice.DueDate,
            invoice.Total, summary.AmountPaid, summary.BalanceDue, summary.PaymentStatus, sent ? "Sent" : "NotSent",
            due.DueState, due.DaysOverdue, calendar.Today, calendar.Zone, invoice.IssuedClientEmail!);
    }
    private static void RequireIssued(Invoice invoice)
    {
        if (invoice.Lifecycle != InvoiceLifecycle.Finalized) throw new RequestFailure(409, "Finalize this invoice before recording or viewing payments.");
    }
}
