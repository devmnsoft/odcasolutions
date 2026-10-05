using Dapper;
using Npgsql;
using Odca.Application.Plans;

namespace Odca.Infrastructure.Plans;

public sealed class NpgsqlPlanCatalogRepository(NpgsqlDataSource dataSource) : IPlanCatalogRepository
{
    private const string PublishedPlanProjection = """
        SELECT p.id AS "Id", p.code AS "Code", p.version AS "Version", p.display_name AS "DisplayName",
               max(e.limit_value) FILTER (WHERE e.entitlement_code = 'active_seats') AS "ActiveSeats",
               max(e.limit_value) FILTER (WHERE e.entitlement_code = 'storage_bytes') AS "StorageBytes",
               max(e.limit_value) FILTER (WHERE e.entitlement_code = 'user_storage_bytes') AS "UserStorageBytes",
               max(e.limit_value) FILTER (WHERE e.entitlement_code = 'file_bytes') AS "FileBytes",
               max(e.limit_value) FILTER (WHERE e.entitlement_code = 'ocr_pages_monthly') AS "OcrPagesMonthly",
               max(e.limit_value) FILTER (WHERE e.entitlement_code = 'signature_envelopes_monthly') AS "SignatureEnvelopesMonthly",
               COALESCE(array_agg(DISTINCT e.entitlement_code) FILTER (WHERE e.entitlement_code LIKE 'module.%'), ARRAY[]::text[]) AS "EnabledModules"
          FROM odca.plan_versions p
          JOIN odca.plan_entitlements e ON e.plan_version_id = p.id AND e.enabled
         WHERE p.status = 'published'
           AND p.effective_from <= now()
           AND (p.effective_until IS NULL OR p.effective_until > now())
        """;

    public async Task<IReadOnlyList<PlanCatalogItem>> ListPublishedAsync(CancellationToken cancellationToken)
    {
        var sql = PublishedPlanProjection + """
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

        return items.Select(ToItem).ToArray();
    }

    public async Task<PlanCatalogSelection?> FindPublishedAsync(string code, CancellationToken cancellationToken)
    {
        var sql = PublishedPlanProjection + """
           AND p.code = @code
         GROUP BY p.id, p.code, p.version, p.display_name;
        """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = (await connection.QueryAsync<PlanCatalogRow>(
            new CommandDefinition(sql, new { code }, cancellationToken: cancellationToken))).AsList();
        if (rows.Count > 1)
        {
            throw new InvalidDataException($"O catálogo contém mais de uma versão vigente para o plano '{code}'.");
        }

        var row = rows.SingleOrDefault();
        if (row is null)
        {
            return null;
        }

        return new PlanCatalogSelection(row.Id, ToItem(row));
    }

    private static PlanCatalogItem ToItem(PlanCatalogRow item)
    {
        return new PlanCatalogItem(
            item.Code,
            item.Version,
            item.DisplayName,
            (int)item.ActiveSeats!.Value,
            item.StorageBytes!.Value,
            item.UserStorageBytes!.Value,
            item.FileBytes!.Value,
            (int)item.OcrPagesMonthly!.Value,
            (int)item.SignatureEnvelopesMonthly!.Value,
            item.EnabledModules ?? []);
    }

    public async Task<PlanChangePreview?> PreviewOrganizationPlanChangeAsync(
        Guid actorId,
        Guid tenantId,
        string planCode,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(planCode))
        {
            return null;
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT set_config('odca.user_id', @actor, true)",
            new { actor = actorId.ToString() },
            transaction,
            cancellationToken: cancellationToken));
        var row = await connection.QuerySingleOrDefaultAsync<PlanChangePreviewRow>(new CommandDefinition(
            """
            SELECT current_code AS "CurrentCode",
                   current_version AS "CurrentVersion",
                   current_plan_version_id AS "CurrentPlanVersionId",
                   target_code AS "TargetCode",
                   target_version AS "TargetVersion",
                   target_plan_version_id AS "TargetPlanVersionId",
                   active_members AS "ActiveMembers",
                   reserved_invitations AS "ReservedInvitations",
                   current_seat_limit AS "CurrentSeatLimit",
                   target_seat_limit AS "TargetSeatLimit",
                   seats_over_limit AS "SeatsOverLimit",
                   patient_count AS "PatientCount",
                   document_count AS "DocumentCount",
                   policy AS "Policy"
              FROM odca.preview_organization_plan_change(@actor, @tenant, @plan)
            """,
            new { actor = actorId, tenant = tenantId, plan = planCode.Trim() },
            transaction,
            cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return row is null
            ? null
            : new PlanChangePreview(
                row.CurrentCode,
                row.CurrentVersion,
                row.CurrentPlanVersionId,
                row.TargetCode,
                row.TargetVersion,
                row.TargetPlanVersionId,
                row.ActiveMembers,
                row.ReservedInvitations,
                row.CurrentSeatLimit,
                row.TargetSeatLimit,
                row.SeatsOverLimit,
                row.PatientCount,
                row.DocumentCount,
                row.Policy);
    }

    public async Task<string> ApplyOrganizationPlanChangeAsync(
        Guid actorId,
        Guid tenantId,
        string planCode,
        string justification,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await connection.ExecuteAsync(new CommandDefinition(
                "SELECT set_config('odca.user_id', @actor, true)",
                new { actor = actorId.ToString() },
                transaction,
                cancellationToken: cancellationToken));
            var result = await connection.ExecuteScalarAsync<string>(new CommandDefinition(
                "SELECT odca.apply_organization_plan_change(@actor, @tenant, @plan, @justification)",
                new { actor = actorId, tenant = tenantId, plan = planCode.Trim(), justification },
                transaction,
                cancellationToken: cancellationToken));
            if (result == "applied")
            {
                await transaction.CommitAsync(cancellationToken);
            }
            else
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            return result ?? "invalid";
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.InsufficientPrivilege)
        {
            return "forbidden";
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return "conflict";
        }
    }

    private sealed class PlanCatalogRow
    {
        public Guid Id { get; init; }

        public string Code { get; init; } = string.Empty;

        public int Version { get; init; }

        public string DisplayName { get; init; } = string.Empty;

        public long? ActiveSeats { get; init; }

        public long? StorageBytes { get; init; }

        public long? UserStorageBytes { get; init; }

        public long? FileBytes { get; init; }

        public long? OcrPagesMonthly { get; init; }

        public long? SignatureEnvelopesMonthly { get; init; }

        public string[]? EnabledModules { get; init; }
    }

    private sealed class PlanChangePreviewRow
    {
        public string CurrentCode { get; init; } = string.Empty;

        public int CurrentVersion { get; init; }

        public Guid CurrentPlanVersionId { get; init; }

        public string TargetCode { get; init; } = string.Empty;

        public int TargetVersion { get; init; }

        public Guid TargetPlanVersionId { get; init; }

        public int ActiveMembers { get; init; }

        public int ReservedInvitations { get; init; }

        public long CurrentSeatLimit { get; init; }

        public long TargetSeatLimit { get; init; }

        public bool SeatsOverLimit { get; init; }

        public long PatientCount { get; init; }

        public long DocumentCount { get; init; }

        public string Policy { get; init; } = string.Empty;
    }
}
