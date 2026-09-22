using Elio.Application.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Elio.Api.Identity;

[ApiController, Route("api/organization")]
public sealed class OrganizationController(ICurrentOrganization current, IOrganizationService organizations) : ControllerBase
{
    [HttpGet, Authorize(Policy = "Owner")]
    public async Task<OrganizationDto> Get()
    {
        var access = (await current.GetAsync())!;
        return await organizations.GetAsync(access.User.Id, access.Membership!.OrganizationId);
    }
    [HttpPost, Authorize(Policy = "Verified"), ProducesResponseType<OrganizationDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<OrganizationDto>> Create(OrganizationRequest request)
    {
        var access = (await current.GetAsync())!;
        return Created("/api/organization", await organizations.CreateAsync(access.User.Id, request));
    }
    [HttpPatch, Authorize(Policy = "Owner")]
    public async Task<OrganizationDto> Update(OrganizationRequest request)
    {
        var access = (await current.GetAsync())!;
        return await organizations.UpdateAsync(access.User.Id, access.Membership!.OrganizationId, request);
    }
}
