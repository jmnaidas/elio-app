using Elio.Application.Identity;
using Elio.Domain.Organizations;
using Elio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Elio.Infrastructure.Organizations;

public sealed class OrganizationService(ElioDbContext database) : IOrganizationService
{
    public async Task<MembershipDto?> ResolveMembershipAsync(Guid userId, Guid? requestedOrganization)
    {
        var query = database.Memberships.AsNoTracking().Where(x => x.UserId == userId);
        if (requestedOrganization.HasValue) query = query.Where(x => x.OrganizationId == requestedOrganization);
        var membership = await query.OrderBy(x => x.CreatedAtUtc).FirstOrDefaultAsync();
        if (membership is null && requestedOrganization.HasValue) throw new RequestFailure(403, "Organization access denied.");
        return membership is null ? null : new(membership.Id, membership.OrganizationId, membership.Role.ToString());
    }
    public async Task<OrganizationDto> GetAsync(Guid userId, Guid organizationId)
    {
        await RequireOwner(userId, organizationId);
        return Map(await database.Organizations.AsNoTracking().SingleAsync(x => x.Id == organizationId));
    }
    public async Task<OrganizationDto> CreateAsync(Guid userId, OrganizationRequest request)
    {
        Organization organization;
        try { organization = new(request.Name, request.TimeZone, request.DefaultCurrency); }
        catch (ArgumentException ex) { throw new RequestFailure(400, ex.Message); }
        await using var transaction = await database.Database.BeginTransactionAsync();
        // Serialize onboarding for this user without preventing multiple memberships in the future.
        await database.Users.FromSqlInterpolated($"SELECT * FROM \"AspNetUsers\" WHERE \"Id\" = {userId} FOR UPDATE").ToListAsync();
        if (await database.Memberships.AnyAsync(x => x.UserId == userId)) throw new RequestFailure(409, "Your organization is already set up.");
        database.Organizations.Add(organization);
        database.Memberships.Add(new Membership(userId, organization.Id));
        await database.SaveChangesAsync();
        await transaction.CommitAsync();
        return Map(organization);
    }
    public async Task<OrganizationDto> UpdateAsync(Guid userId, Guid organizationId, OrganizationRequest request)
    {
        await RequireOwner(userId, organizationId);
        var organization = await database.Organizations.SingleAsync(x => x.Id == organizationId);
        if (request.Version != organization.Version) throw new RequestFailure(409, "These settings changed. Reload before saving again.");
        try { organization.Update(request.Name, request.TimeZone, request.DefaultCurrency); }
        catch (ArgumentException ex) { throw new RequestFailure(400, ex.Message); }
        try { await database.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException) { throw new RequestFailure(409, "These settings changed. Reload before saving again."); }
        return Map(organization);
    }
    private async Task RequireOwner(Guid userId, Guid organizationId)
    {
        if (!await database.Memberships.AnyAsync(x => x.UserId == userId && x.OrganizationId == organizationId && x.Role == MembershipRole.Owner))
            throw new RequestFailure(403, "Organization access denied.");
    }
    private static OrganizationDto Map(Organization value) => new(value.Id, value.Name, value.TimeZone, value.DefaultCurrency, value.Version);
}
