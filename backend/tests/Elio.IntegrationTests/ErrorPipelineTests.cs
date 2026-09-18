using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Elio.IntegrationTests;

// Adds a failure after the production exception handler, only in the test host.
public sealed class FailureStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        next(app);
        app.Run(_ => throw new InvalidOperationException("sensitive-test-detail"));
    };
}

public sealed class ErrorPipelineTests
{
    [Fact]
    public async Task Unexpected_error_is_sanitized_problem_details()
    {
        await using var factory = new ApiFactory();
        await using var failingHost = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddTransient<IStartupFilter, FailureStartupFilter>()));
        using var client = failingHost.CreateClient();
        var response = await client.GetAsync("/test-only-failure");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("sensitive-test-detail", body);
        Assert.Contains("correlationId", body);
    }

    [Fact]
    public async Task Production_does_not_expose_openapi()
    {
        await using var factory = new ApiFactory();
        await using var production = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        using var client = production.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/openapi/v1.json")).StatusCode);
    }
}
