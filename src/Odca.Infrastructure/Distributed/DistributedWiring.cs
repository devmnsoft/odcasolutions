using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Odca.Infrastructure.Distributed;

/// <summary>
/// D-OC31 A.4: seleção condicional do armazenamento compartilhado. Sem
/// configuração (desenvolvimento) o comportamento atual é preservado: cache em
/// memória e chaves de Data Protection em filesystem. Com
/// <c>Cache:Store=postgres</c> e/ou <c>DataProtection:KeysStore=postgres</c>
/// entram em operação os stores do PostgreSQL (tabela v054), permitindo que
/// múltiplas instâncias compartilhem sessões e chaves.
/// </summary>
public static class DistributedWiring
{
    private const string PostgresStore = "postgres";

    public static IServiceCollection AddOdcaDistributedCache(this IServiceCollection services, IConfiguration configuration)
    {
        if (IsPostgres(configuration["Cache:Store"]))
        {
            EnsureDataSource(services, configuration);
            services.AddSingleton<IDistributedCache, PostgresDistributedCache>();
        }
        else
        {
            services.AddDistributedMemoryCache();
        }
        return services;
    }

    public static IServiceCollection AddOdcaDataProtection(this IServiceCollection services, IConfiguration configuration)
    {
        var dataProtection = services.AddDataProtection()
            .SetApplicationName(configuration["DataProtection:ApplicationName"] ?? "ODCA Solutions");
        if (IsPostgres(configuration["DataProtection:KeysStore"]))
        {
            // net10: o repositório é injetado em KeyManagementOptions.XmlRepository
            // (PersistKeysToXmlRepository foi removido do framework); sem isso o
            // Data Protection roda efêmero e ignora o IXmlRepository do container.
            var connStr = configuration.GetConnectionString("Database")
                ?? throw new InvalidOperationException("ConnectionStrings:Database não foi configurada.");
            var source = NpgsqlDataSource.Create(connStr);
            services.AddSingleton(source);
            dataProtection.AddKeyManagementOptions(o => o.XmlRepository = new PostgresKeysRepository(source));
        }
        else
        {
            var keysPath = configuration["DataProtection:KeysPath"];
            if (!string.IsNullOrWhiteSpace(keysPath))
            {
                dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
            }
        }
        return services;
    }

    private static bool IsPostgres(string? value) => string.Equals(value, PostgresStore, StringComparison.OrdinalIgnoreCase);

    // O BFF não registra o DataSource do repositório de dados; nos modos postgres
    // os stores precisam dele e o criam a partir da mesma connection string usada
    // por API/Worker (evita um módulo paralelo de configuração).
    private static void EnsureDataSource(IServiceCollection services, IConfiguration configuration)
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(NpgsqlDataSource))) return;
        services.AddSingleton(_ => NpgsqlDataSource.Create(
            configuration.GetConnectionString("Database")
                ?? throw new InvalidOperationException("ConnectionStrings:Database não foi configurada.")));
    }
}
