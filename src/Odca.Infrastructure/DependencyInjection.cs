using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.DataProtection;
using Npgsql;
using Odca.Application.Common;
using Odca.Application.Dashboard;
using Odca.Application.Identity;
using Odca.Application.Onboarding;
using Odca.Application.Plans;
using Odca.Application.Privacy;
using Odca.Infrastructure.Common;
using Odca.Infrastructure.Database;
using Odca.Infrastructure.Dashboard;
using Odca.Infrastructure.Identity;
using Odca.Infrastructure.Onboarding;
using Odca.Infrastructure.Plans;
using Odca.Infrastructure.Privacy;
using Odca.Application.Contracts;
using Odca.Application.Tenancy;
using Odca.Infrastructure.Contracts;
using Odca.Infrastructure.Tenancy;

namespace Odca.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddOdcaInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Options and factories are deliberately evaluated when the host starts/resolves
        // services. WebApplicationFactory adds its isolated configuration after Program's
        // service-registration phase, so reading values eagerly here bypasses supported
        // test-host configuration composition.
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName));
        services.AddSingleton(provider =>
        {
            var jwt = provider.GetRequiredService<IOptions<JwtOptions>>().Value;
            return new AuthenticationPolicy(TimeSpan.FromMinutes(jwt.AccessTokenMinutes));
        });
        services.AddSingleton(provider =>
        {
            var currentConfiguration = provider.GetRequiredService<IConfiguration>();
            var connectionString = currentConfiguration.GetConnectionString("Database")
                ?? throw new InvalidOperationException("ConnectionStrings:Database não foi configurada.");
            return NpgsqlDataSource.Create(connectionString);
        });
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IPasswordService, AspNetPasswordService>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        var dataProtection = services.AddDataProtection()
            .SetApplicationName(configuration["DataProtection:ApplicationName"] ?? "ODCA Solutions");
        var keysPath = configuration["DataProtection:KeysPath"];
        if (!string.IsNullOrWhiteSpace(keysPath))
        {
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
        }
        services.AddSingleton<IMfaSecretProtector, DataProtectionMfaSecretProtector>();
        services.AddSingleton<TotpService>();
        services.AddScoped<IIdentityRepository, NpgsqlIdentityRepository>();
        services.AddScoped<IPlatformDashboardRepository, NpgsqlPlatformDashboardRepository>();
        services.AddScoped<ICustomerOnboardingRepository, NpgsqlCustomerOnboardingRepository>();
        services.AddScoped<IPlanCatalogRepository, NpgsqlPlanCatalogRepository>();
        services.AddScoped<IPrivacyRequestRepository, NpgsqlPrivacyRequestRepository>();
        services.AddScoped<PrivacyRequestService>();
        services.AddScoped<ITenantAdministrationRepository, NpgsqlTenantAdministrationRepository>();
        services.AddScoped<TenantAdministrationService>();
        services.AddScoped<ICounterpartyRepository, NpgsqlCounterpartyRepository>();
        services.AddScoped<IContractTypeRepository, NpgsqlContractTypeRepository>();
        services.AddScoped<IContractRepository, NpgsqlContractRepository>();
        services.AddScoped<IUserNotificationRepository, NpgsqlUserNotificationRepository>();
        services.AddScoped<IContractAlertRepository, NpgsqlContractAlertRepository>();
        services.AddScoped<CounterpartyService>();
        services.AddScoped<ContractTypeService>();
        services.AddScoped<ContractService>();
        services.AddScoped<UserNotificationService>();
        services.AddScoped<AuthenticationService>();
        services.AddScoped<MfaService>();
        services.AddScoped<CustomerOnboardingService>();
        services.AddSingleton<PostgresReadinessHealthCheck>();
        return services;
    }
}
