using System.Net.Mail;
namespace Elio.Domain.Invoices;

public enum InvoiceDeliveryStatus { Pending, Sent, Failed }
public sealed class InvoiceDelivery
{
    private InvoiceDelivery() { }
    public InvoiceDelivery(Invoice invoice, string? recipient, string channel)
    {
        if (invoice.Lifecycle != InvoiceLifecycle.Finalized) throw new ArgumentException("Only finalized invoices can be sent.");
        recipient = (recipient ?? "").Trim();
        if (recipient.Length > 254 || recipient.Contains('\r') || recipient.Contains('\n') ||
            !MailAddress.TryCreate(recipient, out var address) || address.Address != recipient || !recipient.Contains('@'))
            throw new ArgumentException("Enter a valid recipient email address.");
        if (string.IsNullOrWhiteSpace(channel) || channel.Length > 40) throw new ArgumentException("A delivery channel is required.");
        Id = Guid.NewGuid(); OrganizationId = invoice.OrganizationId; InvoiceId = invoice.Id;
        RecipientEmail = recipient; Channel = channel; AttemptedAtUtc = DateTimeOffset.UtcNow;
    }
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid InvoiceId { get; private set; }
    public string RecipientEmail { get; private set; } = "";
    public string Channel { get; private set; } = "";
    public DateTimeOffset AttemptedAtUtc { get; private set; }
    public DateTimeOffset? SentAtUtc { get; private set; }
    public InvoiceDeliveryStatus Status { get; private set; }
    public string? FailureCode { get; private set; }
    public void Succeed()
    {
        EnsurePending(); Status = InvoiceDeliveryStatus.Sent; SentAtUtc = DateTimeOffset.UtcNow;
    }
    public void Fail(string code)
    {
        EnsurePending();
        if (code is not ("provider_unavailable" or "delivery_failed" or "pdf_failed")) throw new ArgumentException("Invalid failure code.");
        Status = InvoiceDeliveryStatus.Failed; FailureCode = code;
    }
    private void EnsurePending()
    {
        if (Status != InvoiceDeliveryStatus.Pending) throw new InvalidOperationException("A completed delivery attempt cannot be changed.");
    }
}
