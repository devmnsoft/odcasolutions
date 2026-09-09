using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace Odca.Infrastructure.Database;

public sealed class PostgresReadinessHealthCheck(NpgsqlDataSource dataSource) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using (var preflight = new NpgsqlCommand(
                "SELECT current_setting('server_version_num')::integer, to_regclass('odca.schema_migrations');",
                connection))
            await using (var reader = await preflight.ExecuteReaderAsync(cancellationToken))
            {
                await reader.ReadAsync(cancellationToken);
                if (reader.GetInt32(0) < 180000 || reader.IsDBNull(1))
                {
                    return HealthCheckResult.Unhealthy("Schema de banco incompatível.");
                }
            }

            await using var schema = new NpgsqlCommand(
                "SELECT count(*)::integer, COALESCE(max(version), 0)::integer FROM odca.schema_migrations;",
                connection);
            await using var schemaReader = await schema.ExecuteReaderAsync(cancellationToken);
            await schemaReader.ReadAsync(cancellationToken);
            if (schemaReader.GetInt32(0) != DatabaseSchema.CurrentVersion ||
                schemaReader.GetInt32(1) != DatabaseSchema.CurrentVersion)
            {
                return HealthCheckResult.Unhealthy("Schema de banco incompatível.");
            }

            return HealthCheckResult.Healthy();
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("PostgreSQL indisponível.", exception);
        }
    }
}
