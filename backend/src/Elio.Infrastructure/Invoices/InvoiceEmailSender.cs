using System.Text.Json;
using Elio.Application.Invoices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
namespace Elio.Infrastructure.Invoices;

public sealed class InvoiceEmailSender(IHostEnvironment environment, IConfiguration configuration) : IInvoiceEmailSender
{
    public string Channel => environment.IsDevelopment() ? "development-capture" : "unconfigured";
    public async Task SendAsync(InvoiceEmail email)
    {
        if (!environment.IsDevelopment()) throw new InvoiceEmailUnavailableException();
        var directory = configuration["InvoiceEmail:CaptureDirectory"]
            ?? Path.GetFullPath(Path.Combine(environment.ContentRootPath, "../../../artifacts/dev-mail/invoices"));
        Directory.CreateDirectory(directory);
        var attachment = $"{email.DeliveryId:N}-{email.AttachmentFileName}";
        await File.WriteAllBytesAsync(Path.Combine(directory, attachment), email.Pdf);
        // JSON is written last, so a capture is complete only when both files exist.
        await File.WriteAllTextAsync(Path.Combine(directory, $"{email.DeliveryId:N}.json"), JsonSerializer.Serialize(new
        {
            recipient = email.RecipientEmail, subject = email.Subject, body = email.Body, invoiceNumber = email.InvoiceNumber,
            attachmentFileName = email.AttachmentFileName, attachmentPath = attachment, channel = Channel
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
