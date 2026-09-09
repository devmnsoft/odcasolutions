using Dapper;
using Npgsql;
using Odca.Application.Dashboard;

namespace Odca.Infrastructure.Dashboard;

public sealed class NpgsqlPlatformDashboardRepository(NpgsqlDataSource dataSource)
    : IPlatformDashboardRepository
{
    public async Task<PlatformDashboardSnapshot> GetAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                (SELECT count(*)::int FROM odca.tenants WHERE status = 'active' AND NOT is_deleted) AS "ActiveTenants",
                (SELECT count(*)::int FROM odca.users WHERE NOT is_deleted) AS "ActiveUsers",
                (SELECT count(*)::int FROM odca.processing_activities WHERE legal_validation_status = 'pending') AS "PendingPrivacyItems";
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleAsync<PlatformDashboardSnapshot>(
            new CommandDefinition(sql, cancellationToken: cancellationToken));
    }
}
