using Elio.Application.Invoices;
using Elio.Domain.Invoices;
using Elio.Infrastructure.Invoices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using System.Text.Json;
namespace Elio.UnitTests;

public sealed class InvoiceDeliveryTests
{
    [Fact]
    public void Delivery_requires_issuance_and_completed_attempts_are_immutable()
    {
        var invoice = new Invoice(Guid.NewGuid(), Guid.NewGuid(), "PHP", new(2026, 9, 29), new(2026, 9, 30), null, null,
            [new(null, null, "Work", 1, 10)]);
        Assert.Throws<ArgumentException>(() => new InvoiceDelivery(invoice, "billing@example.test", "test"));
        invoice.FinalizeInvoice(1, "Seller", "Asia/Manila", "Client", "billing@example.test", null, null, true);
        Assert.Throws<ArgumentException>(() => new InvoiceDelivery(invoice, null, "test"));
        Assert.Throws<ArgumentException>(() => new InvoiceDelivery(invoice, "  ", "test"));
        var version = invoice.Version; var attempt = new InvoiceDelivery(invoice, " billing@example.test ", "test");
        Assert.Equal("billing@example.test", attempt.RecipientEmail); Assert.Equal(InvoiceDeliveryStatus.Pending, attempt.Status);
        attempt.Succeed(); Assert.NotNull(attempt.SentAtUtc);
        Assert.Throws<InvalidOperationException>(() => attempt.Fail("delivery_failed"));
        Assert.Throws<InvalidOperationException>(() => attempt.Succeed()); Assert.Equal(version, invoice.Version);
        var failed = new InvoiceDelivery(invoice, "billing@example.test", "test"); failed.Fail("delivery_failed");
        Assert.Throws<InvalidOperationException>(() => failed.Succeed());
    }
    [Fact]
    public async Task Production_sender_fails_closed_without_creating_capture_files()
    {
        var directory = Path.Combine(Path.GetTempPath(), "elio-invoice-" + Guid.NewGuid().ToString("N"));
        var sender = new InvoiceEmailSender(new TestEnvironment("Production"), Configuration(directory));
        await Assert.ThrowsAsync<InvoiceEmailUnavailableException>(() => sender.SendAsync(Message()));
        Assert.False(Directory.Exists(directory)); Assert.Equal("unconfigured", sender.Channel);
    }
    [Fact]
    public async Task Development_sender_captures_exact_attachment_and_safe_message_fields()
    {
        var directory = Path.Combine(Path.GetTempPath(), "elio-invoice-" + Guid.NewGuid().ToString("N"));
        try
        {
            var message = Message(); var sender = new InvoiceEmailSender(new TestEnvironment("Development"), Configuration(directory));
            await sender.SendAsync(message);
            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Directory.GetFiles(directory, "*.json").Single()));
            Assert.Equal(message.RecipientEmail, json.RootElement.GetProperty("recipient").GetString());
            Assert.Equal(message.Subject, json.RootElement.GetProperty("subject").GetString());
            Assert.Equal(message.AttachmentFileName, json.RootElement.GetProperty("attachmentFileName").GetString());
            Assert.Equal(message.Pdf, await File.ReadAllBytesAsync(Directory.GetFiles(directory, "*.pdf").Single()));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    private static InvoiceEmail Message() => new(Guid.NewGuid(), "billing@example.test", "Invoice INV-000001 from Seller", "Total 10.00 PHP", "INV-000001", "INV-000001.pdf", [37, 80, 68, 70]);
    private static IConfiguration Configuration(string path) => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["InvoiceEmail:CaptureDirectory"] = path }).Build();
    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Elio.Tests";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
