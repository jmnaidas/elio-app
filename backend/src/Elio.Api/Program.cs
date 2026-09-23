using System.Diagnostics;
using Elio.Api.Errors;
using Elio.Api.Middleware;
using Elio.Infrastructure;
using Elio.Api.Identity;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddIdentityApi(builder.Environment, builder.Configuration);
builder.Services.AddOpenApi(options => options.AddOperationTransformer((operation, context, _) =>
{
    if (context.Description.RelativePath?.StartsWith("api/", StringComparison.Ordinal) == true)
        operation.Description = "First-party cookie API. Keep cookies between requests. Before every mutation obtain GET /api/auth/antiforgery and send its requestToken as X-XSRF-TOKEN. Client/service access requires a verified account and live organization membership; organization settings require Owner. Updates and status changes require the current version. Errors use ProblemDetails.";
    return Task.CompletedTask;
}));
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    context.ProblemDetails.Extensions["correlationId"] = context.HttpContext.Items[CorrelationIdMiddleware.HeaderName];
    context.ProblemDetails.Extensions["traceId"] = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
});

var app = builder.Build();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseApiCsrf();
app.MapControllers();
if (app.Environment.IsDevelopment()) app.MapOpenApi();

static HealthCheckOptions HealthOptions(bool readiness) => new()
{
    Predicate = registration => readiness && registration.Tags.Contains("ready"),
    ResponseWriter = async (context, report) =>
    {
        // Public health output deliberately excludes exceptions and connection details.
        await context.Response.WriteAsJsonAsync(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(entry => new { name = entry.Key, status = entry.Value.Status.ToString() })
        });
    }
};
app.MapHealthChecks("/health", HealthOptions(false));
app.MapHealthChecks("/health/ready", HealthOptions(true));
app.Run();

public partial class Program;
