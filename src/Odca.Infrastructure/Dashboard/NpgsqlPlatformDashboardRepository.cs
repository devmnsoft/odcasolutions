using Dapper;
using Npgsql;
using Odca.Application.Dashboard;

namespace Odca.Infrastructure.Dashboard;

public sealed class NpgsqlPlatformDashboardRepository(NpgsqlDataSource dataSource)
    : IPlatformDashboardRepository
{
    public async Task<PlatformDashboardSnapshot> GetAsync(Guid requestingUserId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT active_tenants AS "ActiveTenants",
                   active_users AS "ActiveUsers",
                   pending_privacy_items AS "PendingPrivacyItems"
              FROM odca.platform_dashboard_snapshot(@requestingUserId);
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT set_config('odca.user_id', @userId, true);",
            new { userId = requestingUserId.ToString() },
            transaction,
            cancellationToken: cancellationToken));
        var snapshot = await connection.QuerySingleAsync<PlatformDashboardSnapshot>(
            new CommandDefinition(sql, new { requestingUserId }, transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return snapshot;
    }
}
