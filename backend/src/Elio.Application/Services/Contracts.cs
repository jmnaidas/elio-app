using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Elio.Application.Catalog;
namespace Elio.Application.Services;

public sealed record ServiceRequest([Required, MaxLength(160)] string Name, [MaxLength(2000)] string? Description,
    [Required] decimal? DefaultUnitPrice, [Required] string Currency, Guid? Version = null);
public sealed record ServiceDto(Guid Id, string Name, string? Description,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)] decimal DefaultUnitPrice,
    string Currency, bool IsActive, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, Guid Version);
public interface IServiceLibrary
{
    Task<IReadOnlyList<ServiceDto>> ListAsync(CatalogQuery query);
    Task<ServiceDto> GetAsync(Guid id);
    Task<ServiceDto> CreateAsync(ServiceRequest request);
    Task<ServiceDto> UpdateAsync(Guid id, ServiceRequest request);
    Task<ServiceDto> SetActiveAsync(Guid id, bool active, Guid? version);
}
