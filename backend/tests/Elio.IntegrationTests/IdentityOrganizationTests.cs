using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Elio.Application.Identity;
using Elio.Domain.Organizations;
using Elio.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Elio.IntegrationTests;

public sealed class CapturedAccountEmail : IAccountEmailSender
{
    public ConcurrentDictionary<string, string> Links { get; } = new();
    public void EnsureAvailable() { }
    public Task SendAsync(string recipient, string purpose, string link) { Links[$"{recipient}:{purpose}"] = link; return Task.CompletedTask; }
    public VerifyRequest Token(string email, string purpose = "verify-email")
    {
        var values = QueryHelpers.ParseQuery(new Uri(Links[$"{email}:{purpose}"]).Fragment.TrimStart('#'));
        return new(values["userId"].ToString(), values["token"].ToString());
    }
}

// Exercise PostgreSQL constraints/transactions in a unique, disposable schema; never delete development tables.
public sealed class PostgresFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string schema = "elio_test_" + Guid.NewGuid().ToString("N");
    private string connection = "";
    public CapturedAccountEmail Mail { get; } = new();
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Database", connection);
        builder.UseSetting("Account:RequestsPerMinute", "1000");
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureServices(services => { services.RemoveAll<IAccountEmailSender>(); services.AddSingleton<IAccountEmailSender>(Mail); });
    }
    public async Task InitializeAsync()
    {
        var config = new ConfigurationBuilder().AddUserSecrets<Program>(optional: true).Build();
        var source = Environment.GetEnvironmentVariable("ELIO_TEST_DATABASE") ?? config.GetConnectionString("Database")
            ?? throw new InvalidOperationException("Real PostgreSQL tests require ELIO_TEST_DATABASE or the API development User Secret.");
        var settings = new NpgsqlConnectionStringBuilder(source) { SearchPath = schema };
        connection = settings.ConnectionString;
        await using var db = new NpgsqlConnection(connection);
        await db.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", db);
        await command.ExecuteNonQueryAsync();
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ElioDbContext>().Database.MigrateAsync();
    }
    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        if (!schema.StartsWith("elio_test_", StringComparison.Ordinal) || schema.Length != 42) throw new InvalidOperationException("Unsafe test schema.");
        await using var db = new NpgsqlConnection(connection);
        await db.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", db);
        await command.ExecuteNonQueryAsync();
    }
}

public sealed class IdentityOrganizationTests(PostgresFactory factory) : IClassFixture<PostgresFactory>
{
    private const string Password = "Elio-Testing-123!";
    private static string Address() => $"test-{Guid.NewGuid():N}@example.test";
    private HttpClient Client() => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    private static async Task<HttpResponseMessage> Send(HttpClient client, string route, object body, HttpMethod? method = null)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/antiforgery");
        using var request = new HttpRequestMessage(method ?? HttpMethod.Post, route) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-XSRF-TOKEN", csrf.GetProperty("requestToken").GetString());
        return await client.SendAsync(request);
    }
    private async Task<string> Account(HttpClient client, bool verified = true)
    {
        var email = Address();
        Assert.Equal(HttpStatusCode.OK, (await Send(client, "/api/auth/register", new RegisterRequest(email, Password, Password))).StatusCode);
        if (verified) Assert.Equal(HttpStatusCode.NoContent, (await Send(client, "/api/auth/verify-email", factory.Mail.Token(email))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Send(client, "/api/auth/login", new LoginRequest(email, Password))).StatusCode);
        return email;
    }
    private static async Task<OrganizationDto> Organization(HttpClient client, string name = "Example Studio")
    {
        var response = await Send(client, "/api/organization", new OrganizationRequest(name, "Asia/Manila", "PHP"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<OrganizationDto>())!;
    }

    [Fact]
    public async Task Registration_duplicate_verification_login_session_and_logout_are_real()
    {
        using var client = Client();
        var email = Address(); var registration = new RegisterRequest(email, Password, Password);
        var first = await Send(client, "/api/auth/register", registration);
        var duplicate = await Send(client, "/api/auth/register", registration);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(await first.Content.ReadAsStringAsync(), await duplicate.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NoContent, (await Send(client, "/api/auth/login", new LoginRequest(email, Password))).StatusCode);
        var pending = await client.GetFromJsonAsync<SessionDto>("/api/auth/session");
        Assert.False(pending!.User!.EmailVerified);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(client, "/api/organization", new OrganizationRequest("Example", "Asia/Manila", "PHP"))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Send(client, "/api/auth/verify-email", factory.Mail.Token(email))).StatusCode);
        var session = await client.GetFromJsonAsync<SessionDto>("/api/auth/session");
        Assert.True(session!.User!.EmailVerified);
        Assert.Equal(email, session.User.Email);
        Assert.Null(session.Organization);
        Assert.Equal(HttpStatusCode.NoContent, (await Send(client, "/api/auth/logout", new { })).StatusCode);
        Assert.Null((await client.GetFromJsonAsync<SessionDto>("/api/auth/session"))!.User);
    }

    [Fact]
    public async Task Invalid_credentials_tokens_and_password_confirmation_are_rejected()
    {
        using var client = Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(client, "/api/auth/login", new LoginRequest(Address(), Password))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(client, "/api/auth/register", new RegisterRequest(Address(), Password, "different"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(client, "/api/auth/register", new RegisterRequest(Address(), "weak", "weak"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(client, "/api/auth/verify-email", new VerifyRequest("invalid-guid", "invalid"))).StatusCode);
        var email = await Account(client);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(client, "/api/auth/login", new LoginRequest(email, "wrong-password"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(client, "/api/auth/verify-email", new VerifyRequest(factory.Mail.Token(email).UserId, "invalid"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(client, "/api/auth/reset-password", new ResetRequest(factory.Mail.Token(email).UserId, "invalid", Password, Password))).StatusCode);
    }

    [Fact]
    public async Task Recovery_is_generic_resets_password_and_revokes_existing_cookie()
    {
        using var client = Client(); var email = await Account(client);
        var existing = await Send(client, "/api/auth/forgot-password", new EmailRequest(email));
        var missing = await Send(client, "/api/auth/forgot-password", new EmailRequest(Address()));
        Assert.Equal(HttpStatusCode.OK, existing.StatusCode);
        Assert.Equal(existing.StatusCode, missing.StatusCode);
        Assert.Equal(await existing.Content.ReadAsStringAsync(), await missing.Content.ReadAsStringAsync());
        var token = factory.Mail.Token(email, "reset-password");
        const string replacement = "Replacement-12345!";
        using var recovery = Client();
        Assert.Equal(HttpStatusCode.NoContent, (await Send(recovery, "/api/auth/reset-password", new ResetRequest(token.UserId, token.Token, replacement, replacement))).StatusCode);
        Assert.Null((await client.GetFromJsonAsync<SessionDto>("/api/auth/session"))!.User);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(recovery, "/api/auth/reset-password", new ResetRequest(token.UserId, token.Token, replacement, replacement))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(client, "/api/auth/login", new LoginRequest(email, Password))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Send(client, "/api/auth/login", new LoginRequest(email, replacement))).StatusCode);
    }

    [Fact]
    public async Task Anonymous_and_missing_or_invalid_csrf_requests_are_rejected()
    {
        using var client = Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/organization")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(Address(), Password, Password))).StatusCode);
        await Account(client);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/organization", new OrganizationRequest("Example", "Asia/Manila", "PHP"))).StatusCode);
        using var invalid = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout") { Content = JsonContent.Create(new { }) };
        invalid.Headers.Add("X-XSRF-TOKEN", "invalid");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(invalid)).StatusCode);
        Assert.NotNull((await client.GetFromJsonAsync<SessionDto>("/api/auth/session"))!.User);
    }

    [Fact]
    public async Task Onboarding_creates_owner_membership_and_settings_are_validated_and_versioned()
    {
        using var client = Client(); await Account(client);
        foreach (var request in new[] { new OrganizationRequest(" ", "Asia/Manila", "PHP"), new OrganizationRequest("Example", "+08:00", "PHP"), new OrganizationRequest("Example", "Asia/Manila", "EUR") })
            Assert.Equal(HttpStatusCode.BadRequest, (await Send(client, "/api/organization", request)).StatusCode);
        var organization = await Organization(client);
        var session = (await client.GetFromJsonAsync<SessionDto>("/api/auth/session"))!;
        Assert.Equal("Owner", session.Membership!.Role);
        Assert.Equal(organization.Id, session.Membership.OrganizationId);
        Assert.Equal(organization, await client.GetFromJsonAsync<OrganizationDto>("/api/organization"));
        var update = new OrganizationRequest("Updated Studio", "America/New_York", "USD", organization.Version);
        var response = await Send(client, "/api/organization", update, HttpMethod.Patch);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<OrganizationDto>();
        Assert.Equal("Updated Studio", updated!.Name); Assert.Equal("USD", updated.DefaultCurrency);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(client, "/api/organization", update, HttpMethod.Patch)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(client, "/api/organization", new OrganizationRequest("Second", "Asia/Manila", "PHP"))).StatusCode);
    }

    [Fact]
    public async Task User_A_cannot_read_or_update_organization_B_even_with_forged_tenant_header()
    {
        using var a = Client(); using var b = Client(); await Account(a); await Account(b);
        var organizationA = await Organization(a, "Studio A"); var organizationB = await Organization(b, "Studio B");
        a.DefaultRequestHeaders.Add("X-Organization-ID", organizationB.Id.ToString());
        Assert.Equal(HttpStatusCode.Forbidden, (await a.GetAsync("/api/organization")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(a, "/api/organization", new OrganizationRequest("Hijacked", "Asia/Manila", "PHP", organizationB.Version), HttpMethod.Patch)).StatusCode);
        Assert.Equal(organizationB, await b.GetFromJsonAsync<OrganizationDto>("/api/organization"));
        a.DefaultRequestHeaders.Remove("X-Organization-ID");
        Assert.Equal(organizationA, await a.GetFromJsonAsync<OrganizationDto>("/api/organization"));
        // Application service also enforces access; the policy/header is not the only boundary.
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IOrganizationService>();
        var userA = (await a.GetFromJsonAsync<SessionDto>("/api/auth/session"))!.User!;
        Assert.Equal(403, (await Assert.ThrowsAsync<RequestFailure>(() => service.GetAsync(userA.Id, organizationB.Id))).Status);
    }

    [Fact]
    public async Task Organization_and_membership_rollback_together_on_foreign_key_failure()
    {
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ElioDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<IOrganizationService>();
        var name = "Atomic-" + Guid.NewGuid();
        await Assert.ThrowsAsync<DbUpdateException>(() => service.CreateAsync(Guid.NewGuid(), new(name, "Asia/Manila", "PHP")));
        using var checkScope = factory.Services.CreateScope();
        var check = checkScope.ServiceProvider.GetRequiredService<ElioDbContext>();
        Assert.False(await check.Organizations.AnyAsync(x => x.Name == name));
    }

    [Fact]
    public async Task Removing_membership_immediately_removes_access()
    {
        using var client = Client(); await Account(client); var organization = await Organization(client);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ElioDbContext>();
        await db.Memberships.Where(x => x.OrganizationId == organization.Id).ExecuteDeleteAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/organization")).StatusCode);
    }

    [Fact]
    public async Task Logout_rejects_replay_of_a_previously_valid_authentication_cookie()
    {
        using var client = Client(); var email = await Account(client);
        var login = await Send(client, "/api/auth/login", new LoginRequest(email, Password));
        var cookie = login.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("Elio.Auth=", StringComparison.Ordinal)).Split(';')[0];
        Assert.Contains("httponly", login.Headers.GetValues("Set-Cookie").First(x => x.StartsWith("Elio.Auth=", StringComparison.Ordinal)), StringComparison.OrdinalIgnoreCase);
        using var replay = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        replay.DefaultRequestHeaders.Add("Cookie", cookie);
        Assert.NotNull((await replay.GetFromJsonAsync<SessionDto>("/api/auth/session"))!.User);
        Assert.Equal(HttpStatusCode.NoContent, (await Send(client, "/api/auth/logout", new { })).StatusCode);
        Assert.Null((await replay.GetFromJsonAsync<SessionDto>("/api/auth/session"))!.User);
    }

    [Fact]
    public async Task Production_cookies_are_secure_HttpOnly_and_host_scoped()
    {
        using var production = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("AccountEmail:FrontendOrigin", "https://localhost");
        });
        using var client = production.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        var email = await Account(client);
        var response = await Send(client, "/api/auth/login", new LoginRequest(email, Password));
        var cookie = response.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("__Host-Elio.Auth=", StringComparison.Ordinal));
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/openapi/v1.json")).StatusCode);
    }

    [Fact]
    public async Task Concurrent_onboarding_creates_exactly_one_organization_and_membership()
    {
        using var client = Client(); await Account(client);
        var user = (await client.GetFromJsonAsync<SessionDto>("/api/auth/session"))!.User!;
        async Task<int> Create()
        {
            using var scope = factory.Services.CreateScope();
            try { await scope.ServiceProvider.GetRequiredService<IOrganizationService>().CreateAsync(user.Id, new("Concurrent", "Asia/Manila", "PHP")); return 201; }
            catch (RequestFailure failure) { return failure.Status; }
        }
        var results = await Task.WhenAll(Create(), Create());
        Assert.Contains(201, results); Assert.Contains(409, results);
        using var check = factory.Services.CreateScope();
        Assert.Equal(1, await check.ServiceProvider.GetRequiredService<ElioDbContext>().Memberships.CountAsync(x => x.UserId == user.Id));
    }

    [Fact]
    public async Task Missing_production_email_provider_fails_without_disclosing_account_existence()
    {
        using var setup = Client(); var email = await Account(setup);
        using var production = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IAccountEmailSender>();
                services.AddScoped<IAccountEmailSender, Elio.Infrastructure.Identity.AccountEmailSender>();
            });
        });
        using var client = production.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var existing = await Send(client, "/api/auth/forgot-password", new EmailRequest(email));
        var missing = await Send(client, "/api/auth/forgot-password", new EmailRequest(Address()));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, existing.StatusCode);
        Assert.Equal(existing.StatusCode, missing.StatusCode);
        var a = await existing.Content.ReadFromJsonAsync<JsonElement>(); var b = await missing.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(a.GetProperty("title").GetString(), b.GetProperty("title").GetString());
    }
}
