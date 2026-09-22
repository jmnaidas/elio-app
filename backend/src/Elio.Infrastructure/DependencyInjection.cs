using Elio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Elio.Application.Identity;
using Elio.Infrastructure.Identity;
using Elio.Infrastructure.Organizations;
using Microsoft.AspNetCore.Identity;

namespace Elio.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Database");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Configure ConnectionStrings:Database using environment variables or user secrets.");

        services.AddDbContext<ElioDbContext>(options => options.UseNpgsql(connectionString));
        services.AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
        {
            options.User.RequireUniqueEmail = true;
            options.Password.RequiredLength = 12;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            // Unverified users can sign in, but only the verification flow is authorized.
            options.SignIn.RequireConfirmedEmail = false;
        }).AddEntityFrameworkStores<ElioDbContext>().AddDefaultTokenProviders();
        services.Configure<DataProtectionTokenProviderOptions>(options => options.TokenLifespan = TimeSpan.FromHours(1));
        services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.Zero);
        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<IAccountEmailSender, AccountEmailSender>();
        services.AddScoped<IOrganizationService, OrganizationService>();
        services.AddHealthChecks().AddCheck<PostgresHealthCheck>("postgresql", tags: ["ready"], timeout: TimeSpan.FromSeconds(5));
        return services;
    }
}
