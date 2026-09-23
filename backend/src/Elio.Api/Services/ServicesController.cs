using Elio.Application.Services;
using Elio.Application.Catalog;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace Elio.Api.Services;

[ApiController, Route("api/services"), Authorize(Policy = "Member")]
public sealed class ServicesController(IServiceLibrary service) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<ServiceDto>> List([FromQuery] CatalogQuery query) => service.ListAsync(query);
    [HttpGet("{id:guid}")]
    public Task<ServiceDto> Get(Guid id) => service.GetAsync(id);
    [HttpPost, ProducesResponseType<ServiceDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<ServiceDto>> Create(ServiceRequest request)
    {
        var result = await service.CreateAsync(request);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }
    [HttpPatch("{id:guid}")]
    public Task<ServiceDto> Update(Guid id, ServiceRequest request) => service.UpdateAsync(id, request);
    [HttpPost("{id:guid}/deactivate")]
    public Task<ServiceDto> Deactivate(Guid id, StatusRequest request) => service.SetActiveAsync(id, false, request.Version);
    [HttpPost("{id:guid}/reactivate")]
    public Task<ServiceDto> Reactivate(Guid id, StatusRequest request) => service.SetActiveAsync(id, true, request.Version);
}
