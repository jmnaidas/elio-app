using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
namespace Elio.Application.Invoices;

public sealed record InvoiceQuery([MaxLength(160)] string? Search = null, string? Currency = null, Guid? ClientId = null);
public sealed record InvoiceRequest(Guid ClientId, [Required] string Currency, DateOnly IssueDate, DateOnly DueDate,
    [MaxLength(4000)] string? Notes, [MaxLength(4000)] string? PaymentInstructions,
    [Required, MinLength(1), MaxLength(100)] InvoiceLineRequest[] Lines, Guid? Version = null);
public sealed record InvoiceLineRequest(Guid? Id, Guid? ServiceId, [Required, MaxLength(2000)] string Description,
    [Required] decimal? Quantity, [Required] decimal? UnitPrice);
[JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)]
public sealed record InvoiceLineDto(Guid Id, Guid? ServiceId, string Description, decimal Quantity, decimal UnitPrice, decimal LineTotal,
    [property: JsonNumberHandling(JsonNumberHandling.Strict)] int SortOrder);
[JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)]
public sealed record InvoiceDto(Guid Id, Guid ClientId, string ClientName, string ClientEmail, string? BillingAddress,
    bool ClientIsActive, string Currency, DateOnly IssueDate, DateOnly DueDate, string? Notes, string? PaymentInstructions,
    string Lifecycle, decimal Subtotal, decimal Total, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc,
    Guid Version, IReadOnlyList<InvoiceLineDto> Lines);
public interface IInvoiceService
{
    Task<IReadOnlyList<InvoiceDto>> ListAsync(InvoiceQuery query);
    Task<InvoiceDto> GetAsync(Guid id);
    Task<InvoiceDto> CreateAsync(InvoiceRequest request);
    Task<InvoiceDto> UpdateAsync(Guid id, InvoiceRequest request);
}
