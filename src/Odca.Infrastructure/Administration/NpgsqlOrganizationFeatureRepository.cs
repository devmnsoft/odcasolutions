using Dapper;
using Npgsql;
using Odca.Application.Administration;
using Odca.Contracts.Administration;

namespace Odca.Infrastructure.Administration;

public sealed class NpgsqlOrganizationFeatureRepository(NpgsqlDataSource dataSource) : IOrganizationFeatureRepository
{
    public async Task<OrganizationFeatureList> ListAsync(Guid actorId, Guid tenantId, CancellationToken cancellationToken)
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
            var rows = (await connection.QueryAsync<FeatureRow>(new CommandDefinition(
                """
                SELECT feature_code AS FeatureCode,
                       state AS State,
                       reason AS Reason,
                       blocked_at AS BlockedAt,
                       plan_entitlement AS PlanEntitlement,
                       tenant_status AS TenantStatus
                  FROM odca.list_organization_features(@actor, @tenant)
                """,
                new { actor = actorId, tenant = tenantId },
                transaction,
                cancellationToken: cancellationToken))).AsList();
            await transaction.CommitAsync(cancellationToken);
            if (rows.Count == 0)
            {
                return new(OrganizationFeatureAccess.Missing, null);
            }

            var features = rows.Select(row => new OrganizationFeatureStatus(
                row.FeatureCode,
                OrganizationFeatureCatalog.DisplayName(row.FeatureCode),
                row.State,
                row.Reason,
                row.BlockedAt is null ? null : new DateTimeOffset(DateTime.SpecifyKind(row.BlockedAt.Value, DateTimeKind.Utc)),
                row.PlanEntitlement)).ToArray();
            return new(OrganizationFeatureAccess.Ok, new OrganizationFeatureCatalogResponse(tenantId, rows[0].TenantStatus, features));
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.InsufficientPrivilege)
        {
            return new(OrganizationFeatureAccess.Forbidden, null);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.NoDataFound)
        {
            return new(OrganizationFeatureAccess.Missing, null);
        }
    }

    public async Task<OrganizationFeatureMutation> SetAsync(
        Guid actorId,
        Guid tenantId,
        string featureCode,
        bool blocked,
        string reason,
        CancellationToken cancellationToken)
    {
        var trimmed = reason.Trim();
        if (!OrganizationFeatureCatalog.IsKnown(featureCode) || trimmed.Length is < 5 or > 500)
        {
            return new(OrganizationFeatureAccess.Invalid, null);
        }

        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await connection.ExecuteAsync(new CommandDefinition(
                "SELECT set_config('odca.user_id', @actor, true)",
                new { actor = actorId.ToString() },
                transaction,
                cancellationToken: cancellationToken));
            var outcome = await connection.ExecuteScalarAsync<string>(new CommandDefinition(
                "SELECT odca.set_organization_feature_block(@actor, @tenant, @feature, @blocked, @reason)",
                new { actor = actorId, tenant = tenantId, feature = featureCode, blocked, reason = trimmed },
                transaction,
                cancellationToken: cancellationToken));
            await transaction.CommitAsync(cancellationToken);
            if (outcome == "missing")
            {
                return new(OrganizationFeatureAccess.Missing, null);
            }

            var listed = await ListAsync(actorId, tenantId, cancellationToken);
            var feature = listed.Catalog?.Features.FirstOrDefault(item => item.FeatureCode == featureCode);
            return feature is null
                ? new(listed.Access, null)
                : new(OrganizationFeatureAccess.Ok, feature);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.InsufficientPrivilege)
        {
            return new(OrganizationFeatureAccess.Forbidden, null);
        }
        catch (PostgresException exception) when (exception.SqlState is PostgresErrorCodes.InvalidParameterValue or PostgresErrorCodes.CheckViolation)
        {
            return new(OrganizationFeatureAccess.Invalid, null);
        }
    }

    private sealed class FeatureRow
    {
        public string FeatureCode { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string? Reason { get; set; }
        public DateTime? BlockedAt { get; set; }
        public string? PlanEntitlement { get; set; }
        public string TenantStatus { get; set; } = string.Empty;
    }
}
