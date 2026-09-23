using Elio.Application.Clients;
using Elio.Application.Catalog;
using Elio.Application.Identity;
using Elio.Domain.Clients;
using Elio.Infrastructure.Catalog;
using Elio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace Elio.Infrastructure.Clients;

public sealed class ClientService(ElioDbContext database, CatalogAccess access) : IClientService
{
    public async Task<IReadOnlyList<ClientDto>> ListAsync(CatalogQuery request)
    {
        var organization = await access.OrganizationAsync();
        var query = database.Clients.AsNoTracking().Where(x => x.OrganizationId == organization);
        if (request.Status == "active") query = query.Where(x => x.IsActive);
        else if (request.Status == "inactive") query = query.Where(x => !x.IsActive);
        else if (request.Status != "all") throw new RequestFailure(400, "Choose active, inactive, or all.");
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLowerInvariant();
            // Contains treats '%' and '_' literally rather than as SQL wildcard input.
            query = query.Where(x => x.Name.ToLower().Contains(term) || x.Email.ToLower().Contains(term));
        }
        return (await query.OrderBy(x => x.Name).ThenBy(x => x.Id).ToListAsync()).Select(Map).ToArray();
    }
    public async Task<ClientDto> GetAsync(Guid id) => Map(await FindAsync(id));
    public async Task<ClientDto> CreateAsync(ClientRequest request)
    {
        var organization = await access.OrganizationAsync();
        Client? entity = null;
        CatalogAccess.Validate(() => entity = new Client(organization, request.Name, request.Email, request.Phone, request.BillingAddress, request.Notes, request.Currency));
        database.Clients.Add(entity!);
        await access.SaveAsync();
        return Map(entity!);
    }
    public async Task<ClientDto> UpdateAsync(Guid id, ClientRequest request)
    {
        var entity = await FindAsync(id);
        CatalogAccess.CheckVersion(entity.Version, request.Version);
        CatalogAccess.Validate(() => entity.Update(request.Name, request.Email, request.Phone, request.BillingAddress, request.Notes, request.Currency));
        await access.SaveAsync();
        return Map(entity);
    }
    public async Task<ClientDto> SetActiveAsync(Guid id, bool active, Guid? version)
    {
        var entity = await FindAsync(id);
        CatalogAccess.CheckVersion(entity.Version, version);
        entity.SetActive(active);
        await access.SaveAsync();
        return Map(entity);
    }
    private async Task<Client> FindAsync(Guid id)
    {
        var organization = await access.OrganizationAsync();
        return await database.Clients.SingleOrDefaultAsync(x => x.OrganizationId == organization && x.Id == id)
            ?? throw new RequestFailure(404, "Client not found.");
    }
    private static ClientDto Map(Client x) => new(x.Id, x.Name, x.Email, x.Phone, x.BillingAddress, x.Notes, x.Currency, x.IsActive, x.CreatedAtUtc, x.UpdatedAtUtc, x.Version);
}
