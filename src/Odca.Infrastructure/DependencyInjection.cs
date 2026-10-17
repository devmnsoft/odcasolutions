using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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
using Odca.Application.Tenancy;
using Odca.Infrastructure.Tenancy;
using Odca.Infrastructure.Distributed;
using Odca.Application.Consumption;
using Odca.Infrastructure.Consumption;
using Odca.Application.Contracts;
using Odca.Infrastructure.Contracts;
using Odca.Application.Patients;
using Odca.Infrastructure.Patients;
using Odca.Application.Administration;
using Odca.Infrastructure.Administration;
using Odca.Infrastructure.Operations;
using Odca.Infrastructure.Signatures;

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
            var connectionString = currentConfiguration.GetConnectionString("Database");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException("ConnectionStrings:Database não foi configurada.");
            }

            return NpgsqlDataSource.Create(connectionString);
        });
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IPasswordService, AspNetPasswordService>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        // D-OC31 A.4: seleção condicional — dev mantém filesystem de chaves;
        // produção usa DataProtection:KeysStore=postgres (v054).
        services.AddOdcaDataProtection(configuration);
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
        services.AddScoped<IConsumptionRepository, NpgsqlConsumptionRepository>();
        services.AddScoped<ITemplateCatalogRepository, NpgsqlTemplateCatalogRepository>();
        services.AddScoped<IPatientRepository, NpgsqlPatientRepository>();
        services.AddScoped<IOrganizationFeatureRepository, NpgsqlOrganizationFeatureRepository>();
        services.AddScoped<IPlatformAuditRepository, NpgsqlPlatformAuditRepository>();
        services.AddOdcaOperationalInbox();
        // D-OC37 (Sprint 3/A-1): provedor plugável de envelope externo de assinatura
        // (Signatures:Provider; default "none" preserva a decisão "proposto, não escolhido").
        services.AddOdcaSignatureEnvelopeProvider();
        services.AddScoped<AuthenticationService>();
        services.AddScoped<MfaService>();
        services.AddScoped<CustomerOnboardingService>();
        services.AddSingleton<PostgresReadinessHealthCheck>();
        return services;
    }
}
