using Elio.Application.Identity;
using Elio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace Elio.Infrastructure.Catalog;

// Shared tenant boundary for catalog and draft invoice features, independent of HTTP policies.
public sealed class CatalogAccess(ICurrentOrganization current, ElioDbContext database)
{
    public async Task<Guid> OrganizationAsync()
    {
        var access = await current.GetAsync();
        if (access is null) throw new RequestFailure(401, "Sign in to continue.");
        if (!access.User.EmailVerified || access.Membership is null ||
            !await database.Memberships.AnyAsync(x => x.Id == access.Membership.Id &&
                x.UserId == access.User.Id && x.OrganizationId == access.Membership.OrganizationId))
            throw new RequestFailure(403, "Organization membership is required.");
        return access.Membership.OrganizationId;
    }
    public static void CheckVersion(Guid current, Guid? supplied)
    {
        if (current != supplied) throw new RequestFailure(409, "This record changed. Reload it before saving again.");
    }
    public static void Validate(Action action)
    {
        try { action(); } catch (ArgumentException ex) { throw new RequestFailure(400, ex.Message); }
    }
    public async Task SaveAsync()
    {
        try { await database.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException) { throw new RequestFailure(409, "This record changed. Reload it before saving again."); }
    }
}
