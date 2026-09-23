using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elio.Application.Identity;
using Elio.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elio.IntegrationTests;

public sealed class CatalogTests(PostgresFactory factory) : IClassFixture<PostgresFactory>
{
    private const string Password = "Catalog-Test-123!";
    private static JsonObject Input(string kind, string name = "Example", string currency = "PHP") => kind == "clients"
        ? new() { ["name"] = name, ["email"] = "billing@example.test", ["phone"] = "+63 123", ["billingAddress"] = "Manila", ["notes"] = "Internal note", ["currency"] = currency }
        : new() { ["name"] = name, ["description"] = "Professional service", ["defaultUnitPrice"] = "1234.56", ["currency"] = currency };
    private static async Task<HttpResponseMessage> Send(HttpClient client, string path, object body, HttpMethod? method = null)
    {
        var token = await client.GetFromJsonAsync<JsonElement>("/api/auth/antiforgery");
        using var request = new HttpRequestMessage(method ?? HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-XSRF-TOKEN", token.GetProperty("requestToken").GetString());
        return await client.SendAsync(request);
    }
    private async Task<HttpClient> Account(bool organization = true, bool verified = true)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var email = $"catalog-{Guid.NewGuid():N}@example.test";
        (await Send(client, "/api/auth/register", new RegisterRequest(email, Password, Password))).EnsureSuccessStatusCode();
        if (verified) (await Send(client, "/api/auth/verify-email", factory.Mail.Token(email))).EnsureSuccessStatusCode();
        (await Send(client, "/api/auth/login", new LoginRequest(email, Password))).EnsureSuccessStatusCode();
        if (organization) (await Send(client, "/api/organization", new OrganizationRequest("Catalog tests", "Asia/Manila", "PHP"))).EnsureSuccessStatusCode();
        return client;
    }
    private static async Task<JsonElement> Create(HttpClient client, string kind, string name = "Example", string currency = "PHP")
    {
        var response = await Send(client, $"/api/{kind}", Input(kind, name, currency));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var item = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.EndsWith(item.GetProperty("id").GetString(), response.Headers.Location!.ToString());
        return item;
    }
    [Theory, InlineData("clients"), InlineData("services")]
    public async Task Members_create_read_update_and_preserve_records_through_status_changes(string kind)
    {
        using var owner = await Account(); var item = await Create(owner, kind);
        var path = $"/api/{kind}/{item.GetProperty("id").GetString()}";
        var retrieved = await owner.GetFromJsonAsync<JsonElement>(path);
        Assert.Equal(item.GetProperty("name").GetString(), retrieved.GetProperty("name").GetString());
        if (kind == "services") Assert.Equal("1234.56", retrieved.GetProperty("defaultUnitPrice").GetString());
        var update = Input(kind, "Updated", "USD"); update["version"] = item.GetProperty("version").GetString();
        var response = await Send(owner, path, update, HttpMethod.Patch);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Updated", updated.GetProperty("name").GetString());
        Assert.Equal("USD", updated.GetProperty("currency").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await Send(owner, path, update, HttpMethod.Patch)).StatusCode);
        var deactivated = await Send(owner, path + "/deactivate", new { version = updated.GetProperty("version").GetString() });
        Assert.Equal(HttpStatusCode.OK, deactivated.StatusCode);
        var inactive = await deactivated.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(inactive.GetProperty("isActive").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync(path)).StatusCode);
        Assert.Empty((await owner.GetFromJsonAsync<JsonElement[]>($"/api/{kind}"))!);
        Assert.Single((await owner.GetFromJsonAsync<JsonElement[]>($"/api/{kind}?status=inactive"))!);
        var reactivated = await Send(owner, path + "/reactivate", new { version = inactive.GetProperty("version").GetString() });
        Assert.True((await reactivated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("isActive").GetBoolean());
        Assert.Single((await owner.GetFromJsonAsync<JsonElement[]>($"/api/{kind}"))!);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await Send(owner, path, new { }, HttpMethod.Delete)).StatusCode);
    }
    [Theory, InlineData("clients"), InlineData("services")]
    public async Task Another_organization_cannot_list_read_update_deactivate_or_reactivate_records(string kind)
    {
        using var a = await Account(); using var b = await Account(); var item = await Create(a, kind);
        var path = $"/api/{kind}/{item.GetProperty("id").GetString()}";
        Assert.Empty((await b.GetFromJsonAsync<JsonElement[]>($"/api/{kind}?status=all"))!);
        var foreign = await b.GetAsync(path); var missing = await b.GetAsync($"/api/{kind}/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode); Assert.Equal(foreign.StatusCode, missing.StatusCode);
        Assert.Equal((await missing.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString(),
            (await foreign.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());
        var update = Input(kind, "Hijacked"); update["version"] = item.GetProperty("version").GetString();
        Assert.Equal(HttpStatusCode.NotFound, (await Send(b, path, update, HttpMethod.Patch)).StatusCode);
        foreach (var action in new[] { "deactivate", "reactivate" })
            Assert.Equal(HttpStatusCode.NotFound, (await Send(b, path + "/" + action, new { version = item.GetProperty("version").GetString() })).StatusCode);
        Assert.Equal("Example", (await a.GetFromJsonAsync<JsonElement>(path)).GetProperty("name").GetString());
        var org = (await a.GetFromJsonAsync<SessionDto>("/api/auth/session"))!.Organization!;
        b.DefaultRequestHeaders.Add("X-Organization-ID", org.Id.ToString());
        Assert.Equal(HttpStatusCode.Forbidden, (await b.GetAsync(path)).StatusCode);
    }
    [Theory, InlineData("clients"), InlineData("services")]
    public async Task Search_is_case_insensitive_literal_and_status_filtered_with_deterministic_order(string kind)
    {
        using var owner = await Account();
        await Create(owner, kind, "Zulu"); await Create(owner, kind, "Alpha"); await Create(owner, kind, "Alpha");
        var special = await Create(owner, kind, "100% Studio");
        var list = (await owner.GetFromJsonAsync<JsonElement[]>($"/api/{kind}?status=all"))!;
        Assert.Equal(new[] { "100% Studio", "Alpha", "Alpha", "Zulu" }, list.Select(x => x.GetProperty("name").GetString()));
        var repeated = (await owner.GetFromJsonAsync<JsonElement[]>($"/api/{kind}?status=all"))!;
        Assert.Equal(list.Select(x => x.GetProperty("id").GetString()), repeated.Select(x => x.GetProperty("id").GetString()));
        Assert.Equal(2, (await owner.GetFromJsonAsync<JsonElement[]>($"/api/{kind}?search=ALP"))!.Length);
        Assert.Single((await owner.GetFromJsonAsync<JsonElement[]>($"/api/{kind}?search=%25"))!);
        if (kind == "clients") Assert.Equal(4, (await owner.GetFromJsonAsync<JsonElement[]>("/api/clients?search=BILLING%40EXAMPLE"))!.Length);
        (await Send(owner, $"/api/{kind}/{special.GetProperty("id").GetString()}/deactivate", new { version = special.GetProperty("version").GetString() })).EnsureSuccessStatusCode();
        Assert.Empty((await owner.GetFromJsonAsync<JsonElement[]>($"/api/{kind}?search=Studio&status=active"))!);
        Assert.Single((await owner.GetFromJsonAsync<JsonElement[]>($"/api/{kind}?search=Studio&status=inactive"))!);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync($"/api/{kind}?status=unexpected")).StatusCode);
    }
    [Theory, InlineData("clients"), InlineData("services")]
    public async Task Anonymous_unverified_missing_and_revoked_membership_are_rejected(string kind)
    {
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/{kind}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(anonymous, $"/api/{kind}", Input(kind))).StatusCode);
        using var pending = await Account(false, false);
        Assert.Equal(HttpStatusCode.Forbidden, (await pending.GetAsync($"/api/{kind}")).StatusCode);
        using var noOrganization = await Account(false);
        Assert.Equal(HttpStatusCode.Forbidden, (await noOrganization.GetAsync($"/api/{kind}")).StatusCode);
        using var owner = await Account(); var session = (await owner.GetFromJsonAsync<SessionDto>("/api/auth/session"))!;
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ElioDbContext>().Memberships.Where(x => x.Id == session.Membership!.Id).ExecuteDeleteAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(owner, $"/api/{kind}", Input(kind))).StatusCode);
    }
    [Theory, InlineData("clients"), InlineData("services")]
    public async Task Invalid_inputs_and_unprotected_mutations_are_rejected(string kind)
    {
        using var owner = await Account();
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(owner, $"/api/{kind}", Input(kind, currency: "EUR"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(owner, $"/api/{kind}", Input(kind, " "))).StatusCode);
        var invalid = Input(kind); invalid[kind == "clients" ? "email" : "defaultUnitPrice"] = kind == "clients" ? "not-an-email" : "-0.01";
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(owner, $"/api/{kind}", invalid)).StatusCode);
        if (kind == "services")
        {
            foreach (var price in new[] { "1.001", "1000000000000", "NaN" })
            {
                invalid["defaultUnitPrice"] = price;
                Assert.Equal(HttpStatusCode.BadRequest, (await Send(owner, "/api/services", invalid)).StatusCode);
            }
            invalid["defaultUnitPrice"] = null;
            Assert.Equal(HttpStatusCode.BadRequest, (await Send(owner, "/api/services", invalid)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync($"/api/{kind}", Input(kind))).StatusCode);
        var item = await Create(owner, kind);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync($"/api/{kind}/{item.GetProperty("id").GetString()}/deactivate", new { version = item.GetProperty("version").GetString() })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(owner, $"/api/{kind}/{item.GetProperty("id").GetString()}/deactivate", new { })).StatusCode);
    }
}
