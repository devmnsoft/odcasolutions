using System.Text.Json;
using Dapper;
using Npgsql;
using Odca.Application.Administration;
using Odca.Contracts.Administration;

namespace Odca.Infrastructure.Administration;

public sealed class NpgsqlPlatformAuditRepository(NpgsqlDataSource dataSource) : IPlatformAuditRepository
{
    private const string EventColumns = """
        SELECT event.id AS Id,
               event.scope_type AS ScopeType,
               event.tenant_id AS TenantId,
               event.tenant_name AS TenantName,
               event.actor_user_id AS ActorUserId,
               event.actor_name AS ActorName,
               event.action AS Action,
               event.entity_type AS EntityType,
               event.entity_id AS EntityId,
               event.occurred_at AS OccurredAt,
               event.result AS Result,
               event.metadata::text AS Metadata
          FROM odca.platform_audit_page(@actor, @page, @pageSize, @search, @tenant) AS event
        """;

    public async Task<PlatformAuditPage> GetPageAsync(
        Guid actorId,
        string? search,
        Guid? tenantId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var trimmedSearch = search?.Trim();
        if (trimmedSearch is { Length: 0 })
        {
            trimmedSearch = null;
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

            var parameters = new
            {
                actor = actorId,
                page,
                pageSize,
                search = trimmedSearch,
                tenant = tenantId
            };

            var rows = (await connection.QueryAsync<PlatformAuditEventRow>(new CommandDefinition(
                EventColumns,
                parameters,
                transaction,
                cancellationToken: cancellationToken))).AsList();
            var total = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                "SELECT odca.platform_audit_count(@actor, @search, @tenant)",
                new { actor = actorId, search = trimmedSearch, tenant = tenantId },
                transaction,
                cancellationToken: cancellationToken));
            await transaction.CommitAsync(cancellationToken);

            return new(
                PlatformAuditAccess.Ok,
                new PlatformAuditPageResponse(
                    rows.Select(row => new PlatformAuditEvent(
                        row.Id,
                        row.ScopeType,
                        row.TenantId,
                        row.TenantName,
                        row.ActorUserId,
                        row.ActorName,
                        row.Action,
                        row.EntityType,
                        row.EntityId,
                        new DateTimeOffset(DateTime.SpecifyKind(row.OccurredAt, DateTimeKind.Utc)),
                        row.Result,
                        ParseMetadata(row.Metadata))).ToArray(),
                    page,
                    pageSize,
                    (int)Math.Min(total, int.MaxValue)));
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.InsufficientPrivilege)
        {
            return new(PlatformAuditAccess.Forbidden, null);
        }
    }

    private static JsonElement ParseMetadata(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return JsonDocument.Parse("{}").RootElement.Clone();
        }

        try
        {
            return JsonDocument.Parse(raw).RootElement.Clone();
        }
        catch (JsonException)
        {
            return JsonDocument.Parse("{}").RootElement.Clone();
        }
    }

    private sealed class PlatformAuditEventRow
    {
        public long Id { get; set; }
        public string ScopeType { get; set; } = string.Empty;
        public Guid? TenantId { get; set; }
        public string? TenantName { get; set; }
        public Guid? ActorUserId { get; set; }
        public string? ActorName { get; set; }
        public string Action { get; set; } = string.Empty;
        public string EntityType { get; set; } = string.Empty;
        public Guid? EntityId { get; set; }
        public DateTime OccurredAt { get; set; }
        public string Result { get; set; } = string.Empty;
        public string? Metadata { get; set; }
    }
}
