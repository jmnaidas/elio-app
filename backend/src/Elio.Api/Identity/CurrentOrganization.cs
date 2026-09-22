using System.Security.Claims;
using Elio.Application.Identity;
using Microsoft.AspNetCore.Authorization;

namespace Elio.Api.Identity;

public sealed class CurrentOrganization(IHttpContextAccessor accessor, IAccountService accounts,
    IOrganizationService organizations) : ICurrentOrganization
{
    private Task<AccessContext?>? resolved;
    public Task<AccessContext?> GetAsync() => resolved ??= ResolveAsync();
    private async Task<AccessContext?> ResolveAsync()
    {
        var context = accessor.HttpContext!;
        if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return null;
        var user = await accounts.GetUserAsync(id);
        if (user is null) return null;
        Guid? requested = null;
        if (context.Request.Headers.TryGetValue("X-Organization-ID", out var header))
        {
            if (!Guid.TryParse(header.ToString(), out var organizationId)) throw new RequestFailure(403, "Organization access denied.");
            requested = organizationId;
        }
        return new(user, await organizations.ResolveMembershipAsync(id, requested));
    }
}

public sealed record OrganizationRequirement(bool Membership, bool Owner) : IAuthorizationRequirement;
public sealed class OrganizationAuthorizationHandler(ICurrentOrganization current) : AuthorizationHandler<OrganizationRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, OrganizationRequirement requirement)
    {
        var access = await current.GetAsync();
        if (access is null || !access.User.EmailVerified) return;
        if (requirement.Membership && access.Membership is null) return;
        if (requirement.Owner && access.Membership?.Role != "Owner") return;
        context.Succeed(requirement);
    }
}
