using Dapper;
using Npgsql;
using Odca.Application.Operations;

namespace Odca.Infrastructure.Operations;

public sealed class OperationalInboxRepository(NpgsqlDataSource dataSource)
    : IOperationalInboxRepository, IMonthlyAgendaRepository
{
    public async Task<TenantCalendarContext> ReadCalendarAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleAsync<(string TimeZoneId, DateOnly Today)>(new CommandDefinition(
            """
            SELECT timezone AS TimeZoneId, (now() AT TIME ZONE timezone)::date AS Today
              FROM odca.tenants
             WHERE id = @tenantId AND NOT is_deleted
            """,
            new { tenantId },
            cancellationToken: cancellationToken));
        return new TenantCalendarContext(row.TimeZoneId, row.Today);
    }

    public Task<IReadOnlyList<OperationalInboxRow>> ListCandidatesAsync(
        Guid tenantId,
        Guid viewerId,
        bool canReadObligations,
        bool canReadTenantObligations,
        bool canReadReviews,
        bool canReadTenantReviews,
        bool canReadRenewals,
        bool canReadTenantRenewals,
        Guid? contractId,
        Guid? ownerId,
        DateOnly today,
        DateOnly renewalWindowEnd,
        CancellationToken cancellationToken) =>
        QueryAsync(
            tenantId,
            viewerId,
            canReadObligations,
            canReadTenantObligations,
            canReadReviews,
            canReadTenantReviews,
            canReadRenewals,
            canReadTenantRenewals,
            contractId,
            ownerId,
            today,
            renewalWindowEnd,
            windowFrom: null,
            windowTo: null,
            cancellationToken);

    public Task<IReadOnlyList<OperationalInboxRow>> ListWindowAsync(
        Guid tenantId,
        Guid viewerId,
        bool canReadObligations,
        bool canReadTenantObligations,
        bool canReadReviews,
        bool canReadTenantReviews,
        bool canReadRenewals,
        bool canReadTenantRenewals,
        Guid? ownerId,
        DateOnly windowStart,
        DateOnly windowEnd,
        CancellationToken cancellationToken) =>
        QueryAsync(
            tenantId,
            viewerId,
            canReadObligations,
            canReadTenantObligations,
            canReadReviews,
            canReadTenantReviews,
            canReadRenewals,
            canReadTenantRenewals,
            contractId: null,
            ownerId,
            today: windowStart,
            renewalWindowEnd: windowEnd,
            windowFrom: windowStart,
            windowTo: windowEnd,
            cancellationToken);

    private async Task<IReadOnlyList<OperationalInboxRow>> QueryAsync(
        Guid tenantId,
        Guid viewerId,
        bool canReadObligations,
        bool canReadTenantObligations,
        bool canReadReviews,
        bool canReadTenantReviews,
        bool canReadRenewals,
        bool canReadTenantRenewals,
        Guid? contractId,
        Guid? ownerId,
        DateOnly today,
        DateOnly renewalWindowEnd,
        DateOnly? windowFrom,
        DateOnly? windowTo,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            SELECT set_config('odca.tenant_id', @tenant, true),
                   set_config('odca.actor_id', @actor, true);
            """,
            new { tenant = tenantId.ToString(), actor = viewerId.ToString() },
            transaction,
            cancellationToken: cancellationToken));

        const string sql = """
            SELECT kind AS Kind, source_id AS SourceId, tenant_id AS TenantId, contract_id AS ContractId,
                   contract_title AS ContractTitle, title AS Title, owner_id AS OwnerId,
                   owner_name AS OwnerName, due_on AS DueOn, status AS Status, version AS Version
              FROM (
                SELECT 'Obligation'::text AS kind, o.id AS source_id, o.tenant_id, o.contract_id,
                       c.title AS contract_title, o.title, o.owner_id, u.display_name AS owner_name,
                       o.due_date AS due_on, o.status, o.row_version::bigint AS version
                  FROM odca.contract_obligations o
                  JOIN odca.contracts c ON c.id = o.contract_id AND c.tenant_id = o.tenant_id
                  JOIN odca.users u ON u.id = o.owner_id
                 WHERE o.tenant_id = @tenantId
                   AND o.deleted_at IS NULL
                   AND o.status IN ('open','in_progress')
                   AND @canReadObligations
                   AND (@canReadTenantObligations OR o.owner_id = @viewerId)
                   AND (CAST(@contractId AS uuid) IS NULL OR o.contract_id = @contractId)
                   AND (CAST(@ownerId AS uuid) IS NULL OR o.owner_id = @ownerId)
                   AND (CAST(@windowFrom AS date) IS NULL OR o.due_date >= CAST(@windowFrom AS date))
                   AND (CAST(@windowTo AS date) IS NULL OR o.due_date <= CAST(@windowTo AS date))
                UNION ALL
                SELECT 'Review', r.id, r.tenant_id, r.contract_id, c.title,
                       COALESCE(NULLIF(btrim(r.instructions), ''), c.title),
                       s.reviewer_id, ru.display_name,
                       (r.due_at AT TIME ZONE t.timezone)::date, r.status, r.row_version::bigint
                  FROM odca.contract_reviews r
                  JOIN odca.contracts c ON c.id = r.contract_id AND c.tenant_id = r.tenant_id
                  JOIN odca.tenants t ON t.id = r.tenant_id
                  JOIN odca.contract_review_steps s
                    ON s.review_id = r.id AND s.tenant_id = r.tenant_id AND s.status = 'current'
                  JOIN odca.users ru ON ru.id = s.reviewer_id
                 WHERE r.tenant_id = @tenantId
                   AND r.status IN ('in_review','changes_requested')
                   AND @canReadReviews
                   AND (@canReadTenantReviews OR s.reviewer_id = @viewerId OR r.requested_by = @viewerId)
                   AND (CAST(@contractId AS uuid) IS NULL OR r.contract_id = @contractId)
                   AND (CAST(@ownerId AS uuid) IS NULL OR s.reviewer_id = @ownerId)
                   AND (CAST(@windowFrom AS date) IS NULL OR (r.due_at AT TIME ZONE t.timezone)::date >= CAST(@windowFrom AS date))
                   AND (CAST(@windowTo AS date) IS NULL OR (r.due_at AT TIME ZONE t.timezone)::date <= CAST(@windowTo AS date))
                UNION ALL
                SELECT 'Renewal', c.id, c.tenant_id, c.id, c.title, c.title, c.owner_id, ou.display_name,
                       CASE
                         WHEN c.renewal_notice_amount IS NULL OR c.end_date IS NULL THEN c.end_date
                         WHEN c.renewal_notice_unit = 'calendar_months'
                           THEN (c.end_date - (c.renewal_notice_amount || ' months')::interval)::date
                         ELSE (c.end_date - c.renewal_notice_amount)::date
                       END,
                       CASE WHEN c.end_date < @today THEN 'expired' ELSE 'expiring' END, c.row_version::bigint
                  FROM odca.contracts c
                  LEFT JOIN odca.users ou ON ou.id = c.owner_id
                 WHERE c.tenant_id = @tenantId
                   AND c.end_date IS NOT NULL
                   AND c.end_date <= @renewalWindowEnd
                   AND @canReadRenewals
                   AND (@canReadTenantRenewals OR c.owner_id = @viewerId)
                   AND (CAST(@contractId AS uuid) IS NULL OR c.id = @contractId)
                   AND (CAST(@ownerId AS uuid) IS NULL OR c.owner_id = @ownerId)
                   AND (
                        CAST(@windowFrom AS date) IS NULL
                        OR COALESCE(
                             CASE
                               WHEN c.renewal_notice_amount IS NULL OR c.end_date IS NULL THEN c.end_date
                               WHEN c.renewal_notice_unit = 'calendar_months'
                                 THEN (c.end_date - (c.renewal_notice_amount || ' months')::interval)::date
                               ELSE (c.end_date - c.renewal_notice_amount)::date
                             END, c.end_date) BETWEEN CAST(@windowFrom AS date) AND CAST(@windowTo AS date)
                       )
              ) inbox
            """;

        var rows = await connection.QueryAsync<OperationalInboxRow>(new CommandDefinition(
            sql,
            new
            {
                tenantId,
                viewerId,
                contractId,
                ownerId,
                today,
                renewalWindowEnd,
                windowFrom,
                windowTo,
                canReadObligations,
                canReadTenantObligations,
                canReadReviews,
                canReadTenantReviews,
                canReadRenewals,
                canReadTenantRenewals
            },
            transaction,
            cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return rows.AsList();
    }
}
