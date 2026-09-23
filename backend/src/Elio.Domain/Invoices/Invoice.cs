using Elio.Domain.Catalog;
namespace Elio.Domain.Invoices;

public enum InvoiceLifecycle { Draft, Finalized, Void }
public sealed record DraftLine(Guid? Id, Guid? ServiceId, string Description, decimal Quantity, decimal UnitPrice);

public sealed class Invoice
{
    public const decimal MaxMoney = 999999999999.99m;
    private Invoice() { }
    public Invoice(Guid organizationId, Guid clientId, string currency, DateOnly issueDate, DateOnly dueDate,
        string? notes, string? paymentInstructions, IReadOnlyList<DraftLine> lines)
    {
        if (organizationId == Guid.Empty) throw new ArgumentException("Organization is required.");
        Id = Guid.NewGuid(); OrganizationId = organizationId; CreatedAtUtc = DateTimeOffset.UtcNow;
        Update(clientId, currency, issueDate, dueDate, notes, paymentInstructions, lines);
    }
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid ClientId { get; private set; }
    public string Currency { get; private set; } = "";
    public DateOnly IssueDate { get; private set; }
    public DateOnly DueDate { get; private set; }
    public string? Notes { get; private set; }
    public string? PaymentInstructions { get; private set; }
    public InvoiceLifecycle Lifecycle { get; private set; } = InvoiceLifecycle.Draft;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public Guid Version { get; private set; }
    private readonly List<InvoiceLine> lines = [];
    public IReadOnlyCollection<InvoiceLine> Lines => lines.AsReadOnly();
    public decimal Subtotal => lines.Sum(x => x.LineTotal);
    public decimal Total => Subtotal;

    public void Update(Guid clientId, string currency, DateOnly issueDate, DateOnly dueDate,
        string? notes, string? paymentInstructions, IReadOnlyList<DraftLine> input)
    {
        if (Lifecycle != InvoiceLifecycle.Draft) throw new ArgumentException("Only drafts can be edited.");
        if (clientId == Guid.Empty) throw new ArgumentException("Choose a client.");
        var validCurrency = CatalogRules.Currency(currency);
        if (issueDate == default || dueDate == default || dueDate < issueDate)
            throw new ArgumentException("Enter valid dates, with the due date on or after the issue date.");
        var validNotes = CatalogRules.Optional(notes, 4000, "Notes");
        var validInstructions = CatalogRules.Optional(paymentInstructions, 4000, "Payment instructions");
        if (input is null || input.Count is < 1 or > 100) throw new ArgumentException("Include between 1 and 100 lines.");
        var suppliedIds = input.Where(x => x.Id.HasValue).Select(x => x.Id!.Value).ToArray();
        if (suppliedIds.Distinct().Count() != suppliedIds.Length || suppliedIds.Any(id => lines.All(x => x.Id != id)))
            throw new ArgumentException("Line IDs must identify unique lines already in this draft.");
        // Validate the entire replacement before changing any existing state.
        var validated = input.Select((x, index) => new InvoiceLine(x.Id ?? Guid.NewGuid(), Id, x.ServiceId,
            x.Description, x.Quantity, x.UnitPrice, index)).ToArray();
        if (validated.Sum(x => x.LineTotal) > MaxMoney) throw new ArgumentException("Invoice total exceeds the supported amount.");
        lines.RemoveAll(x => validated.All(next => next.Id != x.Id));
        foreach (var next in validated)
        {
            var existing = lines.SingleOrDefault(x => x.Id == next.Id);
            if (existing is null) lines.Add(next);
            else existing.CopyFrom(next);
        }
        ClientId = clientId; Currency = validCurrency; IssueDate = issueDate; DueDate = dueDate;
        Notes = validNotes; PaymentInstructions = validInstructions;
        UpdatedAtUtc = DateTimeOffset.UtcNow; Version = Guid.NewGuid();
    }
}

public sealed class InvoiceLine
{
    private InvoiceLine() { }
    internal InvoiceLine(Guid id, Guid invoiceId, Guid? serviceId, string description, decimal quantity, decimal unitPrice, int sortOrder)
    {
        if (quantity <= 0 || quantity > 999999.9999m || decimal.Round(quantity, 4) != quantity)
            throw new ArgumentException("Quantity must be greater than zero, at most 999999.9999, with at most four decimal places.");
        if (unitPrice < 0 || unitPrice > Invoice.MaxMoney || decimal.Round(unitPrice, 2) != unitPrice)
            throw new ArgumentException("Unit price must be non-negative, at most 999999999999.99, with at most two decimal places.");
        Description = CatalogRules.Required(description, 2000, "Line description");
        LineTotal = decimal.Round(quantity * unitPrice, 2, MidpointRounding.AwayFromZero);
        if (LineTotal > Invoice.MaxMoney) throw new ArgumentException("Line total exceeds the supported amount.");
        Id = id; InvoiceId = invoiceId; ServiceId = serviceId; Quantity = quantity; UnitPrice = unitPrice; SortOrder = sortOrder;
    }
    public Guid Id { get; private set; }
    public Guid InvoiceId { get; private set; }
    public Guid? ServiceId { get; private set; }
    public string Description { get; private set; } = "";
    public decimal Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal LineTotal { get; private set; }
    public int SortOrder { get; private set; }
    internal void CopyFrom(InvoiceLine next)
    {
        ServiceId = next.ServiceId; Description = next.Description; Quantity = next.Quantity;
        UnitPrice = next.UnitPrice; LineTotal = next.LineTotal; SortOrder = next.SortOrder;
    }
}
