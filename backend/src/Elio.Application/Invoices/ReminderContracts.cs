using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
namespace Elio.Application.Invoices;

public sealed record SendReminderRequest([MaxLength(254)] string? RecipientEmail = null);
[JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)]
public sealed record InvoiceReminderDto(Guid Id, string RecipientEmail, DateTimeOffset AttemptedAtUtc,
    DateTimeOffset? SentAtUtc, string Status, string Channel, string? FailureCode, decimal AmountPaid, decimal BalanceDue);
public interface IInvoiceReminderService
{
    Task<InvoiceReminderDto> SendAsync(Guid id, SendReminderRequest request);
    Task<IReadOnlyList<InvoiceReminderDto>> ListAsync(Guid id);
}
