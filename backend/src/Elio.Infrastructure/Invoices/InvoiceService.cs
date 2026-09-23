using Elio.Application.Identity;
using Elio.Application.Invoices;
using Elio.Domain.Clients;
using Elio.Domain.Invoices;
using Elio.Infrastructure.Catalog;
using Elio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace Elio.Infrastructure.Invoices;

public sealed class InvoiceService(ElioDbContext database, CatalogAccess access) : IInvoiceService
{
    public async Task<IReadOnlyList<InvoiceDto>> ListAsync(InvoiceQuery request)
    {
        var organization = await access.OrganizationAsync();
        var query = database.Invoices.AsNoTracking().Include(x => x.Lines)
            .Where(x => x.OrganizationId == organization && x.Lifecycle == InvoiceLifecycle.Draft);
        if (!string.IsNullOrEmpty(request.Currency))
        {
            if (request.Currency is not ("PHP" or "USD")) throw new RequestFailure(400, "Choose PHP or USD.");
            query = query.Where(x => x.Currency == request.Currency);
        }
        if (request.ClientId.HasValue) query = query.Where(x => x.ClientId == request.ClientId.Value);
        var clients = database.Clients.AsNoTracking().Where(x => x.OrganizationId == organization);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLowerInvariant();
            var matching = clients.Where(x => x.Name.ToLower().Contains(term) || x.Email.ToLower().Contains(term)).Select(x => x.Id);
            query = query.Where(x => matching.Contains(x.ClientId));
        }
        var invoices = await query.OrderByDescending(x => x.UpdatedAtUtc).ThenBy(x => x.Id).ToListAsync();
        var ids = invoices.Select(x => x.ClientId).Distinct().ToArray();
        var clientMap = await clients.Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
        return invoices.Select(x => Map(x, clientMap[x.ClientId])).ToArray();
    }
    public async Task<InvoiceDto> GetAsync(Guid id)
    {
        var invoice = await FindAsync(id);
        return Map(invoice, await ClientAsync(invoice.OrganizationId, invoice.ClientId));
    }
    public async Task<InvoiceDto> CreateAsync(InvoiceRequest request)
    {
        var organization = await access.OrganizationAsync();
        var client = await ValidateSources(organization, request, null);
        Invoice? invoice = null;
        CatalogAccess.Validate(() => invoice = new Invoice(organization, request.ClientId, request.Currency,
            request.IssueDate, request.DueDate, request.Notes, request.PaymentInstructions, Lines(request)));
        database.Invoices.Add(invoice!);
        await access.SaveAsync();
        return Map(invoice!, client);
    }
    public async Task<InvoiceDto> UpdateAsync(Guid id, InvoiceRequest request)
    {
        var invoice = await FindAsync(id);
        CatalogAccess.CheckVersion(invoice.Version, request.Version);
        var client = await ValidateSources(invoice.OrganizationId, request, invoice);
        var original = invoice.Lines.ToArray();
        CatalogAccess.Validate(() => invoice.Update(request.ClientId, request.Currency, request.IssueDate,
            request.DueDate, request.Notes, request.PaymentInstructions, Lines(request)));
        // Required restrictive FKs prevent deleting an invoice; removed aggregate children are explicitly deleted.
        database.RemoveRange(original.Where(x => !invoice.Lines.Contains(x)));
        foreach (var added in invoice.Lines.Where(x => !original.Contains(x))) database.Add(added);
        await access.SaveAsync();
        return Map(invoice, client);
    }
    private static DraftLine[] Lines(InvoiceRequest request) => request.Lines.Select(x =>
        new DraftLine(x.Id, x.ServiceId, x.Description, x.Quantity ?? 0, x.UnitPrice ?? -1)).ToArray();
    private async Task<Client> ValidateSources(Guid organization, InvoiceRequest request, Invoice? existing)
    {
        if (request.Lines is null || request.Lines.Length is < 1 or > 100 || request.Lines.Any(x => x is null))
            throw new RequestFailure(400, "Include between 1 and 100 valid lines.");
        if (request.ClientId == Guid.Empty) throw new RequestFailure(400, "Choose a client.");
        var client = await ClientAsync(organization, request.ClientId);
        if (!client.IsActive && existing?.ClientId != client.Id) throw new RequestFailure(400, "Choose an active client for a new selection.");
        foreach (var line in request.Lines)
        {
            if (line.ServiceId is not Guid id) continue;
            var service = await database.Services.SingleOrDefaultAsync(x => x.OrganizationId == organization && x.Id == id)
                ?? throw new RequestFailure(404, "Service not found.");
            var previous = existing?.Lines.SingleOrDefault(x => x.Id == line.Id);
            var retained = previous?.ServiceId == id;
            if (!retained && !service.IsActive) throw new RequestFailure(400, "Choose an active service for a new selection.");
            // A retained copy does not follow later service edits, including its currency.
            if ((!retained || existing!.Currency != request.Currency) && service.Currency != request.Currency)
                throw new RequestFailure(400, "Service currency must match the invoice currency. Use a matching service or a manual line; no conversion is applied.");
        }
        return client;
    }
    private async Task<Client> ClientAsync(Guid organization, Guid id) =>
        await database.Clients.SingleOrDefaultAsync(x => x.OrganizationId == organization && x.Id == id)
        ?? throw new RequestFailure(404, "Client not found.");
    private async Task<Invoice> FindAsync(Guid id)
    {
        var organization = await access.OrganizationAsync();
        return await database.Invoices.Include(x => x.Lines).SingleOrDefaultAsync(x => x.OrganizationId == organization && x.Id == id)
            ?? throw new RequestFailure(404, "Invoice not found.");
    }
    private static InvoiceDto Map(Invoice x, Client client) => new(x.Id, x.ClientId, client.Name, client.Email,
        client.BillingAddress, client.IsActive, x.Currency, x.IssueDate, x.DueDate, x.Notes, x.PaymentInstructions,
        x.Lifecycle.ToString(), x.Subtotal, x.Total, x.CreatedAtUtc, x.UpdatedAtUtc, x.Version,
        x.Lines.OrderBy(l => l.SortOrder).ThenBy(l => l.Id).Select(l => new InvoiceLineDto(l.Id, l.ServiceId,
            l.Description, l.Quantity, l.UnitPrice, l.LineTotal, l.SortOrder)).ToArray());
}
