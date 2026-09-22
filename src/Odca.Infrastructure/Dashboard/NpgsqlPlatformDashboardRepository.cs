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
            SELECT total_tenants AS "TotalTenants",
                   active_tenants AS "ActiveTenants",
                   blocked_tenants AS "BlockedTenants",
                   inactive_tenants AS "InactiveTenants",
                   active_users AS "ActiveUsers",
                   contracts AS "Contracts",
                   contracts_expiring AS "ContractsExpiring",
                   open_obligations AS "OpenObligations",
                   overdue_obligations AS "OverdueObligations",
                   upcoming_renewals AS "UpcomingRenewals",
                   pending_invoices AS "PendingInvoices",
                   overdue_invoices AS "OverdueInvoices",
                   storage_bytes AS "StorageBytes",
                   pending_privacy_items AS "PendingPrivacyItems"
              FROM odca.platform_dashboard_snapshot(@requestingUserId);
            SELECT occurred_at AS "OccurredAt", action AS "Action", entity_type AS "EntityType",
                   result AS "Result", actor_name AS "ActorName", tenant_name AS "TenantName"
              FROM odca.platform_dashboard_recent_audit(@requestingUserId, 8);
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT set_config('odca.user_id', @userId, true);",
            new { userId = requestingUserId.ToString() },
            transaction,
            cancellationToken: cancellationToken));
        using var results = await connection.QueryMultipleAsync(
            new CommandDefinition(sql, new { requestingUserId }, transaction, cancellationToken: cancellationToken));
        var counters = await results.ReadSingleAsync<DashboardCounters>();
        var events = (await results.ReadAsync<PlatformAuditEvent>()).AsList();
        await transaction.CommitAsync(cancellationToken);
        return new PlatformDashboardSnapshot(
            counters.TotalTenants, counters.ActiveTenants, counters.BlockedTenants, counters.InactiveTenants,
            counters.ActiveUsers, counters.Contracts, counters.ContractsExpiring, counters.OpenObligations,
            counters.OverdueObligations, counters.UpcomingRenewals, counters.PendingInvoices,
            counters.OverdueInvoices, counters.StorageBytes, counters.PendingPrivacyItems, events);
    }

    private sealed record DashboardCounters(
        int TotalTenants, int ActiveTenants, int BlockedTenants, int InactiveTenants, int ActiveUsers,
        int Contracts, int ContractsExpiring, int OpenObligations, int OverdueObligations,
        int UpcomingRenewals, int PendingInvoices, int OverdueInvoices, long StorageBytes,
        int PendingPrivacyItems);
}
