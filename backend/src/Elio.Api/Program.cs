using System.Diagnostics;
using Elio.Api.Errors;
using Elio.Api.Middleware;
using Elio.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddOpenApi();
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
