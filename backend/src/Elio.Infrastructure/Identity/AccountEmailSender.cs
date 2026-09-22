using System.Text.Json;
using Elio.Application.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Elio.Infrastructure.Identity;

public sealed class AccountEmailSender(IHostEnvironment environment, IConfiguration configuration) : IAccountEmailSender
{
    public void EnsureAvailable()
    {
        if (!environment.IsDevelopment()) throw new RequestFailure(503, "Account email delivery is not configured.");
    }
    public async Task SendAsync(string recipient, string purpose, string link)
    {
        // Fail closed: production must supply a real implementation. Never capture tokens there.
        if (!environment.IsDevelopment()) throw new RequestFailure(503, "Account email delivery is not configured.");
        var directory = configuration["AccountEmail:CaptureDirectory"]
            ?? Path.GetFullPath(Path.Combine(environment.ContentRootPath, "../../../artifacts/dev-mail"));
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.json"),
            JsonSerializer.Serialize(new { recipient, purpose, link }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
