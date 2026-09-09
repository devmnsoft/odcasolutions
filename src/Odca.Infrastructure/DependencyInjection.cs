using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Odca.Application.Common;
using Odca.Application.Dashboard;
using Odca.Application.Identity;
using Odca.Application.Plans;
using Odca.Application.Privacy;
using Odca.Infrastructure.Common;
using Odca.Infrastructure.Database;
using Odca.Infrastructure.Dashboard;
using Odca.Infrastructure.Identity;
using Odca.Infrastructure.Plans;
using Odca.Infrastructure.Privacy;

namespace Odca.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddOdcaInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Database")
            ?? throw new InvalidOperationException("ConnectionStrings:Database não foi configurada.");

        services.Configure<JwtOptions>(configuration.GetRequiredSection(JwtOptions.SectionName));
        var accessTokenMinutes = configuration.GetValue<int>($"{JwtOptions.SectionName}:AccessTokenMinutes");
        services.AddSingleton(new AuthenticationPolicy(TimeSpan.FromMinutes(accessTokenMinutes)));
        services.AddSingleton(NpgsqlDataSource.Create(connectionString));
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IPasswordService, AspNetPasswordService>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddScoped<IIdentityRepository, NpgsqlIdentityRepository>();
        services.AddScoped<IPlatformDashboardRepository, NpgsqlPlatformDashboardRepository>();
        services.AddScoped<IPlanCatalogRepository, NpgsqlPlanCatalogRepository>();
        services.AddScoped<IPrivacyRequestRepository, NpgsqlPrivacyRequestRepository>();
        services.AddScoped<PrivacyRequestService>();
        services.AddScoped<AuthenticationService>();
        services.AddSingleton<PostgresReadinessHealthCheck>();
        return services;
    }
}
