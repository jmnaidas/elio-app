using Elio.Domain.Catalog;
namespace Elio.Domain.Invoices;

public enum PaymentMethod { BankTransfer, Cash, Check, Card, EWallet, Other }
public sealed class InvoicePayment
{
    private InvoicePayment() { }
    public InvoicePayment(Invoice invoice, decimal amount, DateTimeOffset receivedAtUtc, string method,
        string? reference, string? notes, Guid createdBy)
    {
        if (invoice.Lifecycle != InvoiceLifecycle.Finalized) throw new ArgumentException("Only issued invoices can receive payments.");
        if (amount <= 0 || amount > Invoice.MaxMoney || decimal.Round(amount, 2) != amount)
            throw new ArgumentException("Amount must be positive, at most 999999999999.99, with at most two decimal places.");
        if (receivedAtUtc == default || receivedAtUtc > DateTimeOffset.UtcNow.AddMinutes(5))
            throw new ArgumentException("Enter a received date/time that is not in the future.");
        if (!Enum.TryParse<PaymentMethod>(method, out var parsed) || !Enum.IsDefined(parsed) || parsed.ToString() != method)
            throw new ArgumentException("Choose a valid payment method.");
        if (createdBy == Guid.Empty) throw new ArgumentException("A recording user is required.");
        Id = Guid.NewGuid(); OrganizationId = invoice.OrganizationId; InvoiceId = invoice.Id;
        Amount = amount; Currency = invoice.Currency; ReceivedAtUtc = receivedAtUtc.ToUniversalTime(); Method = parsed;
        Reference = CatalogRules.Optional(reference, 160, "Reference"); Notes = CatalogRules.Optional(notes, 2000, "Notes");
        CreatedAtUtc = DateTimeOffset.UtcNow; CreatedBy = createdBy;
    }
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid InvoiceId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = "";
    public DateTimeOffset ReceivedAtUtc { get; private set; }
    public PaymentMethod Method { get; private set; }
    public string? Reference { get; private set; }
    public string? Notes { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public Guid CreatedBy { get; private set; }
}

public sealed record PaymentSummary(decimal AmountPaid, decimal BalanceDue, string PaymentStatus)
{
    public static PaymentSummary Calculate(decimal total, decimal paid)
    {
        if (total < 0 || paid < 0 || paid > total) throw new ArgumentException("Payments must remain within the invoice total.");
        return new(paid, total - paid, paid == total ? "Paid" : paid == 0 ? "Unpaid" : "PartiallyPaid");
    }
}
