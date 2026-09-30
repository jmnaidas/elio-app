using Elio.Application.Identity;
using Elio.Application.Invoices;
using Elio.Domain.Clients;
using Elio.Domain.Invoices;
using Elio.Infrastructure.Catalog;
using Elio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
namespace Elio.Infrastructure.Invoices;

public sealed class InvoiceService(ElioDbContext database, CatalogAccess access, IInvoicePdfGenerator pdf) : IInvoiceService
{
    public async Task<IReadOnlyList<InvoiceDto>> ListAsync(InvoiceQuery request)
    {
        var organization = await access.OrganizationAsync();
        var query = database.Invoices.AsNoTracking().Include(x => x.Lines)
            .Where(x => x.OrganizationId == organization);
        if (!string.IsNullOrEmpty(request.Status) && request.Status != "all")
        {
            if (request.Status is not ("draft" or "finalized" or "sent")) throw new RequestFailure(400, "Choose draft, finalized, sent, or all.");
            var sent = database.InvoiceDeliveries.Where(x => x.OrganizationId == organization && x.Status == InvoiceDeliveryStatus.Sent).Select(x => x.InvoiceId);
            query = request.Status switch
            {
                "draft" => query.Where(x => x.Lifecycle == InvoiceLifecycle.Draft),
                "sent" => query.Where(x => sent.Contains(x.Id)),
                _ => query.Where(x => x.Lifecycle == InvoiceLifecycle.Finalized && !sent.Contains(x.Id))
            };
        }
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
            query = query.Where(x => (x.Lifecycle == InvoiceLifecycle.Draft && matching.Contains(x.ClientId)) ||
                (x.Lifecycle != InvoiceLifecycle.Draft && (x.IssuedClientName!.ToLower().Contains(term) || x.IssuedClientEmail!.ToLower().Contains(term) || x.InvoiceNumber!.ToLower().Contains(term))));
        }
        var invoices = await query.OrderByDescending(x => x.UpdatedAtUtc).ThenBy(x => x.Id).ToListAsync();
        var ids = invoices.Where(x => x.Lifecycle == InvoiceLifecycle.Draft).Select(x => x.ClientId).Distinct().ToArray();
        var clientMap = await clients.Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
        var invoiceIds = invoices.Select(x => x.Id).ToArray();
        var sentAt = await database.InvoiceDeliveries.Where(x => x.OrganizationId == organization && invoiceIds.Contains(x.InvoiceId) && x.Status == InvoiceDeliveryStatus.Sent)
            .GroupBy(x => x.InvoiceId).Select(x => new { Id = x.Key, SentAt = x.Max(d => d.SentAtUtc) }).ToDictionaryAsync(x => x.Id, x => x.SentAt);
        return invoices.Select(x => WithDelivery(Map(x, clientMap.GetValueOrDefault(x.ClientId)), sentAt.GetValueOrDefault(x.Id))).ToArray();
    }
    public async Task<InvoiceDto> GetAsync(Guid id)
    {
        var invoice = await FindAsync(id);
        var sentAt = await database.InvoiceDeliveries.Where(x => x.OrganizationId == invoice.OrganizationId && x.InvoiceId == id && x.Status == InvoiceDeliveryStatus.Sent)
            .MaxAsync(x => x.SentAtUtc);
        return WithDelivery(Map(invoice, invoice.Lifecycle == InvoiceLifecycle.Draft ? await ClientAsync(invoice.OrganizationId, invoice.ClientId) : null), sentAt);
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
        if (invoice.Lifecycle != InvoiceLifecycle.Draft) throw new RequestFailure(409, "Finalized invoices cannot be edited.");
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
    public async Task<InvoiceDto> FinalizeAsync(Guid id, Guid? version)
    {
        var organization = await access.OrganizationAsync();
        await using var transaction = await database.Database.BeginTransactionAsync();
        // Lock the aggregate before version/lifecycle checks. A competing finalize waits and then sees Finalized.
        var invoice = (await database.Invoices.FromSqlInterpolated($"SELECT * FROM \"Invoices\" WHERE \"Id\" = {id} AND \"OrganizationId\" = {organization} FOR UPDATE").ToListAsync()).SingleOrDefault()
            ?? throw new RequestFailure(404, "Invoice not found.");
        if (invoice.Lifecycle != InvoiceLifecycle.Draft) throw new RequestFailure(409, "This invoice is already finalized.");
        CatalogAccess.CheckVersion(invoice.Version, version);
        await database.Entry(invoice).Collection(x => x.Lines).LoadAsync();
        var client = await ClientAsync(organization, invoice.ClientId);
        var seller = await database.Organizations.SingleAsync(x => x.Id == organization);
        // Retained source references may be inactive; independent line values are deliberately not recopied.
        var serviceIds = invoice.Lines.Where(x => x.ServiceId.HasValue).Select(x => x.ServiceId!.Value).Distinct().ToArray();
        if (await database.Services.CountAsync(x => x.OrganizationId == organization && serviceIds.Contains(x.Id)) != serviceIds.Length)
            throw new RequestFailure(404, "Service not found.");
        await using var command = database.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = """
            INSERT INTO "InvoiceSequences" ("OrganizationId", "LastValue") VALUES (@organization, 1)
            ON CONFLICT ("OrganizationId") DO UPDATE SET "LastValue" = "InvoiceSequences"."LastValue" + 1
            WHERE "InvoiceSequences"."LastValue" < 9223372036854775807
            RETURNING "LastValue";
            """;
        var parameter = command.CreateParameter(); parameter.ParameterName = "organization"; parameter.Value = organization; command.Parameters.Add(parameter);
        var sequence = await command.ExecuteScalarAsync() as long? ?? throw new RequestFailure(409, "Invoice numbering capacity reached.");
        CatalogAccess.Validate(() => invoice.FinalizeInvoice(sequence, seller.Name, seller.TimeZone,
            client.Name, client.Email, client.Phone, client.BillingAddress, client.IsActive));
        await access.SaveAsync();
        // Return the persisted timestamp precision, matching every later historical read.
        await database.Entry(invoice).ReloadAsync();
        await transaction.CommitAsync();
        return Map(invoice, null);
    }
    public async Task<InvoicePdf> PdfAsync(Guid id)
    {
        var invoice = await GetAsync(id);
        if (invoice.Lifecycle != "Finalized") throw new RequestFailure(409, "Finalize this invoice before downloading its official PDF.");
        return new(pdf.Generate(invoice), invoice.InvoiceNumber + ".pdf");
    }
    private static InvoiceDto WithDelivery(InvoiceDto invoice, DateTimeOffset? sentAt) => invoice with { DeliveryStatus = sentAt.HasValue ? "Sent" : "NotSent", LastSentAtUtc = sentAt };
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
    private static InvoiceDto Map(Invoice x, Client? client) => new(x.Id, x.ClientId,
        x.IssuedClientName ?? client!.Name, x.IssuedClientEmail ?? client!.Email,
        x.Lifecycle == InvoiceLifecycle.Draft ? client!.BillingAddress : x.IssuedBillingAddress,
        x.IssuedClientIsActive ?? client!.IsActive, x.Currency, x.IssueDate, x.DueDate, x.Notes, x.PaymentInstructions,
        x.Lifecycle.ToString(), x.Subtotal, x.Total, x.CreatedAtUtc, x.UpdatedAtUtc, x.Version,
        x.Lines.OrderBy(l => l.SortOrder).ThenBy(l => l.Id).Select(l => new InvoiceLineDto(l.Id, l.ServiceId,
            l.Description, l.Quantity, l.UnitPrice, l.LineTotal, l.SortOrder)).ToArray(),
        x.InvoiceNumber, x.FinalizedAtUtc, x.SellerName, x.SellerTimeZone,
        x.Lifecycle == InvoiceLifecycle.Draft ? client!.Phone : x.IssuedClientPhone);
}
