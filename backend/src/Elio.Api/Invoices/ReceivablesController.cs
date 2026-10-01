using Elio.Application.Invoices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace Elio.Api.Invoices;

[ApiController, Route("api/receivables"), Authorize(Policy = "Member")]
public sealed class ReceivablesController(IReceivableService service) : ControllerBase
{
    [HttpGet] public Task<IReadOnlyList<ReceivableDto>> List([FromQuery] ReceivableQuery query) => service.ListAsync(query);
    [HttpGet("{id:guid}")] public Task<ReceivableDetail> Get(Guid id) => service.GetAsync(id);
    [HttpGet("/api/invoices/{id:guid}/payments")]
    public async Task<IReadOnlyList<PaymentDto>> Payments(Guid id) => (await service.GetAsync(id)).Payments;
    [HttpPost("/api/invoices/{id:guid}/payments")]
    public Task<ReceivableDetail> Record(Guid id, PaymentRequest request) => service.RecordAsync(id, request);
}
