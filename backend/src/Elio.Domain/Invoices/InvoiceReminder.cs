using System.Net.Mail;
namespace Elio.Domain.Invoices;

public enum InvoiceReminderStatus { Pending, Sent, Failed }
public sealed class InvoiceReminder
{
    private InvoiceReminder() { }
    public InvoiceReminder(Invoice invoice, string? recipient, string channel, decimal amountPaid)
    {
        if (invoice.Lifecycle != InvoiceLifecycle.Finalized) throw new ArgumentException("Only finalized invoices can be sent.");
        recipient = (recipient ?? "").Trim();
        if (recipient.Length > 254 || recipient.Contains('\r') || recipient.Contains('\n') ||
            !MailAddress.TryCreate(recipient, out var address) || address.Address != recipient || !recipient.Contains('@'))
            throw new ArgumentException("Enter a valid recipient email address.");
        if (string.IsNullOrWhiteSpace(channel) || channel.Length > 40) throw new ArgumentException("A delivery channel is required.");
        var financial = PaymentSummary.Calculate(invoice.Total, amountPaid);
        if (financial.BalanceDue <= 0) throw new ArgumentException("Only outstanding invoices can receive reminders.");
        AmountPaid = financial.AmountPaid; BalanceDue = financial.BalanceDue;
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
    public InvoiceReminderStatus Status { get; private set; }
    public decimal AmountPaid { get; private set; }
    public decimal BalanceDue { get; private set; }
    public string? FailureCode { get; private set; }
    public void Succeed()
    {
        EnsurePending(); Status = InvoiceReminderStatus.Sent; SentAtUtc = DateTimeOffset.UtcNow;
    }
    public void Fail(string code)
    {
        EnsurePending();
        if (code is not ("provider_unavailable" or "delivery_failed" or "pdf_failed")) throw new ArgumentException("Invalid failure code.");
        Status = InvoiceReminderStatus.Failed; FailureCode = code;
    }
    private void EnsurePending()
    {
        if (Status != InvoiceReminderStatus.Pending) throw new InvalidOperationException("A completed delivery attempt cannot be changed.");
    }
}
