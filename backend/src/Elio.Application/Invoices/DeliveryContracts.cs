using System.ComponentModel.DataAnnotations;
namespace Elio.Application.Invoices;

public sealed record SendInvoiceRequest([Required] Guid? Version, [MaxLength(254)] string? RecipientEmail = null);
public sealed record InvoiceDeliveryDto(Guid Id, string RecipientEmail, DateTimeOffset AttemptedAtUtc,
    DateTimeOffset? SentAtUtc, string Status, string Channel, string? FailureCode);
public sealed record InvoiceEmail(Guid DeliveryId, string RecipientEmail, string Subject, string Body,
    string InvoiceNumber, string AttachmentFileName, byte[] Pdf);
public interface IInvoiceEmailSender
{
    string Channel { get; }
    Task SendAsync(InvoiceEmail email);
}
public sealed class InvoiceEmailUnavailableException : Exception;
public interface IInvoiceDeliveryService
{
    Task<InvoiceDeliveryDto> SendAsync(Guid id, SendInvoiceRequest request);
    Task<IReadOnlyList<InvoiceDeliveryDto>> ListAsync(Guid id);
}
