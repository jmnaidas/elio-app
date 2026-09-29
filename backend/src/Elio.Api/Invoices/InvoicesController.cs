using Elio.Application.Invoices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace Elio.Api.Invoices;

[ApiController, Route("api/invoices"), Authorize(Policy = "Member")]
public sealed class InvoicesController(IInvoiceService service) : ControllerBase
{
    [HttpGet] public Task<IReadOnlyList<InvoiceDto>> List([FromQuery] InvoiceQuery query) => service.ListAsync(query);
    [HttpGet("{id:guid}")] public Task<InvoiceDto> Get(Guid id) => service.GetAsync(id);
    [HttpPost, ProducesResponseType<InvoiceDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<InvoiceDto>> Create(InvoiceRequest request)
    {
        var result = await service.CreateAsync(request);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }
    [HttpPatch("{id:guid}")] public Task<InvoiceDto> Update(Guid id, InvoiceRequest request) => service.UpdateAsync(id, request);
    [HttpPost("{id:guid}/finalize")]
    public Task<InvoiceDto> FinalizeInvoice(Guid id, FinalizeInvoiceRequest request) => service.FinalizeAsync(id, request.Version);
    [HttpGet("{id:guid}/pdf"), Produces("application/pdf")]
    public async Task<IActionResult> Pdf(Guid id)
    {
        var result = await service.PdfAsync(id);
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(result.Bytes, "application/pdf", result.FileName);
    }
}
