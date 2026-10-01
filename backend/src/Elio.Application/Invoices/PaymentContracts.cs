using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
namespace Elio.Application.Invoices;

public sealed record PaymentRequest([Required] decimal? Amount, [Required] DateTimeOffset? ReceivedAtUtc,
    [Required] string Method, [MaxLength(160)] string? Reference = null, [MaxLength(2000)] string? Notes = null);
public sealed record ReceivableQuery([MaxLength(160)] string? Search = null, string? Status = null, string? Currency = null);
[JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)]
public sealed record PaymentDto(Guid Id, decimal Amount, string Currency, DateTimeOffset ReceivedAtUtc,
    string Method, string? Reference, string? Notes, DateTimeOffset CreatedAtUtc, Guid CreatedBy);
[JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)]
public sealed record ReceivableDto(Guid InvoiceId, string InvoiceNumber, string ClientName, string Currency,
    DateOnly IssueDate, DateOnly DueDate, decimal Total, decimal AmountPaid, decimal BalanceDue, string PaymentStatus, string DeliveryStatus);
public sealed record ReceivableDetail(ReceivableDto Summary, IReadOnlyList<PaymentDto> Payments);
public interface IReceivableService
{
    Task<IReadOnlyList<ReceivableDto>> ListAsync(ReceivableQuery query);
    Task<ReceivableDetail> GetAsync(Guid id);
    Task<ReceivableDetail> RecordAsync(Guid id, PaymentRequest request);
}
