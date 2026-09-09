using Dapper;
using Npgsql;
using Odca.Application.Plans;

namespace Odca.Infrastructure.Plans;

public sealed class NpgsqlPlanCatalogRepository(NpgsqlDataSource dataSource) : IPlanCatalogRepository
{
    public async Task<IReadOnlyList<PlanCatalogItem>> ListPublishedAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT p.code AS "Code", p.version AS "Version", p.display_name AS "DisplayName",
                   max(e.limit_value) FILTER (WHERE e.entitlement_code = 'active_seats')::integer AS "ActiveSeats",
                   max(e.limit_value) FILTER (WHERE e.entitlement_code = 'storage_bytes') AS "StorageBytes",
                   max(e.limit_value) FILTER (WHERE e.entitlement_code = 'user_storage_bytes') AS "UserStorageBytes",
                   max(e.limit_value) FILTER (WHERE e.entitlement_code = 'file_bytes') AS "FileBytes",
                   max(e.limit_value) FILTER (WHERE e.entitlement_code = 'ocr_pages_monthly')::integer AS "OcrPagesMonthly",
                   max(e.limit_value) FILTER (WHERE e.entitlement_code = 'signature_envelopes_monthly')::integer AS "SignatureEnvelopesMonthly"
              FROM odca.plan_versions p
              JOIN odca.plan_entitlements e ON e.plan_version_id = p.id AND e.enabled
             WHERE p.status = 'published'
               AND p.effective_from <= now()
               AND (p.effective_until IS NULL OR p.effective_until > now())
             GROUP BY p.id, p.code, p.version, p.display_name
             ORDER BY min(e.limit_value) FILTER (WHERE e.entitlement_code = 'active_seats');
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var items = await connection.QueryAsync<PlanCatalogItem>(
            new CommandDefinition(sql, cancellationToken: cancellationToken));
        return items.AsList();
    }
}
