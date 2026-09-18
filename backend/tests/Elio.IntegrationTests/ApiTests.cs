using System.Net;
using System.Text.Json;
using Elio.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elio.IntegrationTests;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Database",
            Environment.GetEnvironmentVariable("ELIO_TEST_DATABASE")
            ?? "Host=127.0.0.1;Port=1;Database=elio_test;Username=test;Password=test;Timeout=1");
    }
}

public sealed class ApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory factory;
    public ApiTests(ApiFactory factory) => this.factory = factory;

    [Fact]
    public async Task Liveness_does_not_depend_on_database()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-ID", "integration-123");
        var response = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("integration-123", response.Headers.GetValues("X-Correlation-ID").Single());
    }

    [Fact]
    public async Task Readiness_checks_real_database_connection()
    {
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/health/ready");
        var expected = Environment.GetEnvironmentVariable("ELIO_TEST_DATABASE") is null
            ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK;
        Assert.Equal(expected, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("postgresql", body);
        Assert.DoesNotContain("Password", body);
    }

    [Fact]
    public async Task Not_found_uses_problem_details_and_replaces_invalid_correlation()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Correlation-ID", "invalid value");
        var response = await client.GetAsync("/does-not-exist");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var correlation = response.Headers.GetValues("X-Correlation-ID").Single();
        Assert.NotEqual("invalid value", correlation);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(correlation, json.RootElement.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task Openapi_is_available_in_development()
    {
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/openapi/v1.json")).StatusCode);
    }

    [Fact]
    public void Database_model_has_no_business_entities()
    {
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ElioDbContext>();
        Assert.Empty(database.Model.GetEntityTypes());
        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", database.Database.ProviderName);
    }
}

