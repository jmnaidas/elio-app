using Elio.Application.Services;
using Elio.Application.Catalog;
using Elio.Application.Identity;
using Elio.Domain.Services;
using Elio.Infrastructure.Catalog;
using Elio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace Elio.Infrastructure.Services;

public sealed class ServiceLibrary(ElioDbContext database, CatalogAccess access) : IServiceLibrary
{
    public async Task<IReadOnlyList<ServiceDto>> ListAsync(CatalogQuery request)
    {
        var organization = await access.OrganizationAsync();
        var query = database.Services.AsNoTracking().Where(x => x.OrganizationId == organization);
        if (request.Status == "active") query = query.Where(x => x.IsActive);
        else if (request.Status == "inactive") query = query.Where(x => !x.IsActive);
        else if (request.Status != "all") throw new RequestFailure(400, "Choose active, inactive, or all.");
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLowerInvariant();
            // Contains treats '%' and '_' literally rather than as SQL wildcard input.
            query = query.Where(x => x.Name.ToLower().Contains(term));
        }
        return (await query.OrderBy(x => x.Name).ThenBy(x => x.Id).ToListAsync()).Select(Map).ToArray();
    }
    public async Task<ServiceDto> GetAsync(Guid id) => Map(await FindAsync(id));
    public async Task<ServiceDto> CreateAsync(ServiceRequest request)
    {
        var organization = await access.OrganizationAsync();
        Service? entity = null;
        CatalogAccess.Validate(() => entity = new Service(organization, request.Name, request.Description, request.DefaultUnitPrice ?? throw new ArgumentException("Price is required."), request.Currency));
        database.Services.Add(entity!);
        await access.SaveAsync();
        return Map(entity!);
    }
    public async Task<ServiceDto> UpdateAsync(Guid id, ServiceRequest request)
    {
        var entity = await FindAsync(id);
        CatalogAccess.CheckVersion(entity.Version, request.Version);
        CatalogAccess.Validate(() => entity.Update(request.Name, request.Description, request.DefaultUnitPrice ?? throw new ArgumentException("Price is required."), request.Currency));
        await access.SaveAsync();
        return Map(entity);
    }
    public async Task<ServiceDto> SetActiveAsync(Guid id, bool active, Guid? version)
    {
        var entity = await FindAsync(id);
        CatalogAccess.CheckVersion(entity.Version, version);
        entity.SetActive(active);
        await access.SaveAsync();
        return Map(entity);
    }
    private async Task<Service> FindAsync(Guid id)
    {
        var organization = await access.OrganizationAsync();
        return await database.Services.SingleOrDefaultAsync(x => x.OrganizationId == organization && x.Id == id)
            ?? throw new RequestFailure(404, "Service not found.");
    }
    private static ServiceDto Map(Service x) => new(x.Id, x.Name, x.Description, x.DefaultUnitPrice, x.Currency, x.IsActive, x.CreatedAtUtc, x.UpdatedAtUtc, x.Version);
}
