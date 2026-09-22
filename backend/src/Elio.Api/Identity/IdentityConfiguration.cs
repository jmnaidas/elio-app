using System.Threading.RateLimiting;
using Elio.Application.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Antiforgery;

namespace Elio.Api.Identity;

public static class IdentityConfiguration
{
    public static void AddIdentityApi(this IServiceCollection services, IHostEnvironment environment, IConfiguration configuration)
    {
        services.AddControllers();
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentOrganization, CurrentOrganization>();
        services.AddScoped<IAuthorizationHandler, OrganizationAuthorizationHandler>();
        services.AddAuthorization(options =>
        {
            options.AddPolicy("Verified", policy => policy.RequireAuthenticatedUser().AddRequirements(new OrganizationRequirement(false, false)));
            options.AddPolicy("Member", policy => policy.RequireAuthenticatedUser().AddRequirements(new OrganizationRequirement(true, false)));
            options.AddPolicy("Owner", policy => policy.RequireAuthenticatedUser().AddRequirements(new OrganizationRequirement(true, true)));
        });
        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = environment.IsDevelopment() ? "Elio.Auth" : "__Host-Elio.Auth";
            options.Cookie.HttpOnly = true;
            options.Cookie.Path = "/";
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = false;
            options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; };
            options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; };
        });
        services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-XSRF-TOKEN";
            options.Cookie.Name = environment.IsDevelopment() ? "Elio.Antiforgery" : "__Host-Elio.Antiforgery";
            options.Cookie.Path = "/";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        });
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = 429;
            options.AddPolicy("account", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
                { PermitLimit = configuration.GetValue("Account:RequestsPerMinute", 30), Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });
    }

    public static void UseApiCsrf(this WebApplication app) => app.Use(async (context, next) =>
    {
        if (context.Request.Path.StartsWithSegments("/api") &&
            !HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method) && !HttpMethods.IsOptions(context.Request.Method))
        {
            try { await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context); }
            catch (AntiforgeryValidationException)
            {
                await Results.Problem(statusCode: 400, title: "Invalid request protection. Refresh the page and try again.").ExecuteAsync(context);
                return;
            }
        }
        if (context.Request.Path.StartsWithSegments("/api")) context.Response.Headers.CacheControl = "no-store";
        await next(context);
    });
}
