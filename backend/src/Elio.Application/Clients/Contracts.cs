using System.ComponentModel.DataAnnotations;
using Elio.Application.Catalog;
namespace Elio.Application.Clients;

public sealed record ClientRequest(
    [Required, MaxLength(160)] string Name, [Required, EmailAddress, MaxLength(254)] string Email,
    [MaxLength(50)] string? Phone, [MaxLength(1000)] string? BillingAddress, [MaxLength(2000)] string? Notes,
    [Required] string Currency, Guid? Version = null);
public sealed record ClientDto(Guid Id, string Name, string Email, string? Phone, string? BillingAddress,
    string? Notes, string Currency, bool IsActive, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, Guid Version);
public interface IClientService
{
    Task<IReadOnlyList<ClientDto>> ListAsync(CatalogQuery query);
    Task<ClientDto> GetAsync(Guid id);
    Task<ClientDto> CreateAsync(ClientRequest request);
    Task<ClientDto> UpdateAsync(Guid id, ClientRequest request);
    Task<ClientDto> SetActiveAsync(Guid id, bool active, Guid? version);
}
