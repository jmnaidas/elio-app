using Elio.Application.Clients;
using Elio.Application.Catalog;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace Elio.Api.Clients;

[ApiController, Route("api/clients"), Authorize(Policy = "Member")]
public sealed class ClientsController(IClientService service) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<ClientDto>> List([FromQuery] CatalogQuery query) => service.ListAsync(query);
    [HttpGet("{id:guid}")]
    public Task<ClientDto> Get(Guid id) => service.GetAsync(id);
    [HttpPost, ProducesResponseType<ClientDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<ClientDto>> Create(ClientRequest request)
    {
        var result = await service.CreateAsync(request);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }
    [HttpPatch("{id:guid}")]
    public Task<ClientDto> Update(Guid id, ClientRequest request) => service.UpdateAsync(id, request);
    [HttpPost("{id:guid}/deactivate")]
    public Task<ClientDto> Deactivate(Guid id, StatusRequest request) => service.SetActiveAsync(id, false, request.Version);
    [HttpPost("{id:guid}/reactivate")]
    public Task<ClientDto> Reactivate(Guid id, StatusRequest request) => service.SetActiveAsync(id, true, request.Version);
}
