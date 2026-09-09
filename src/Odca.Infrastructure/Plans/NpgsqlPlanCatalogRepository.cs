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
        var rows = (await connection.QueryAsync<PlanCatalogRow>(
            new CommandDefinition(sql, cancellationToken: cancellationToken))).AsList();
        var items = rows;
        if (items.GroupBy(item => item.Code, StringComparer.Ordinal).Any(group => group.Count() != 1))
        {
            throw new InvalidDataException("O catálogo contém mais de uma versão vigente para o mesmo plano.");
        }

        if (items.Any(item => item.ActiveSeats is null || item.StorageBytes is null ||
                              item.UserStorageBytes is null || item.FileBytes is null ||
                              item.OcrPagesMonthly is null || item.SignatureEnvelopesMonthly is null))
        {
            throw new InvalidDataException("Um plano publicado não contém todos os limites obrigatórios.");
        }

        return items.Select(item => new PlanCatalogItem(
            item.Code,
            item.Version,
            item.DisplayName,
            item.ActiveSeats!.Value,
            item.StorageBytes!.Value,
            item.UserStorageBytes!.Value,
            item.FileBytes!.Value,
            item.OcrPagesMonthly!.Value,
            item.SignatureEnvelopesMonthly!.Value)).ToArray();
    }

    private sealed class PlanCatalogRow
    {
        public string Code { get; init; } = string.Empty;

        public int Version { get; init; }

        public string DisplayName { get; init; } = string.Empty;

        public int? ActiveSeats { get; init; }

        public long? StorageBytes { get; init; }

        public long? UserStorageBytes { get; init; }

        public long? FileBytes { get; init; }

        public int? OcrPagesMonthly { get; init; }

        public int? SignatureEnvelopesMonthly { get; init; }
    }
}
