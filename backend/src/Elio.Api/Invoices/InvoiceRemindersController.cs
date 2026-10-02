using Elio.Application.Invoices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace Elio.Api.Invoices;

[ApiController, Route("api/invoices/{id:guid}/reminders"), Authorize(Policy = "Member")]
public sealed class InvoiceRemindersController(IInvoiceReminderService service) : ControllerBase
{
    [HttpGet] public Task<IReadOnlyList<InvoiceReminderDto>> List(Guid id) => service.ListAsync(id);
    [HttpPost] public Task<InvoiceReminderDto> Send(Guid id, SendReminderRequest request) => service.SendAsync(id, request);
}
