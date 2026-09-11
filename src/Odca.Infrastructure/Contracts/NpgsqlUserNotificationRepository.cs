using Dapper;
using Npgsql;
using Odca.Application.Contracts;
using Odca.Application.Tenancy;

namespace Odca.Infrastructure.Contracts;

public sealed class NpgsqlUserNotificationRepository(NpgsqlDataSource dataSource) : IUserNotificationRepository
{
    public async Task<QueryAccess<TenantPage<UserNotificationRecord>>> ListAsync(
        Guid actorId,
        Guid tenantId,
        bool unreadOnly,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await TenantSql.BeginAuthorizedAsync(connection, actorId, tenantId, "tenant.notifications.read", cancellationToken);
        if (tx is null)
        {
            return new QueryAccess<TenantPage<UserNotificationRecord>>(QueryAccessStatus.Forbidden, null);
        }

        var total = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT count(*)::int
              FROM odca.user_notifications
             WHERE tenant_id = @tenantId
               AND user_id = @actorId
               AND NOT is_obsolete
               AND (NOT @unreadOnly OR read_at IS NULL);
            """,
            new { tenantId, actorId, unreadOnly },
            tx,
            cancellationToken: cancellationToken));

        var rows = await connection.QueryAsync<UserNotificationRecord>(new CommandDefinition(
            $"""
            {SelectColumns}
              FROM odca.user_notifications
             WHERE tenant_id = @tenantId
               AND user_id = @actorId
               AND NOT is_obsolete
               AND (NOT @unreadOnly OR read_at IS NULL)
             ORDER BY created_at DESC
             OFFSET @offset LIMIT @pageSize;
            """,
            new { tenantId, actorId, unreadOnly, offset = Pagination.Offset(page, pageSize), pageSize },
            tx,
            cancellationToken: cancellationToken));

        await tx.CommitAsync(cancellationToken);
        return new QueryAccess<TenantPage<UserNotificationRecord>>(
            QueryAccessStatus.Ok,
            new TenantPage<UserNotificationRecord>(rows.AsList(), total, page, pageSize));
    }

    public async Task<QueryAccess<int>> UnreadCountAsync(Guid actorId, Guid tenantId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await TenantSql.BeginAuthorizedAsync(connection, actorId, tenantId, "tenant.notifications.read", cancellationToken);
        if (tx is null)
        {
            return new QueryAccess<int>(QueryAccessStatus.Forbidden, 0);
        }

        var count = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT count(*)::int
              FROM odca.user_notifications
             WHERE tenant_id = @tenantId
               AND user_id = @actorId
               AND NOT is_obsolete
               AND status = 'open'
               AND read_at IS NULL;
            """,
            new { tenantId, actorId },
            tx,
            cancellationToken: cancellationToken));
        await tx.CommitAsync(cancellationToken);
        return new QueryAccess<int>(QueryAccessStatus.Ok, count);
    }

    public async Task<QueryAccess<UserNotificationRecord?>> GetAsync(
        Guid actorId,
        Guid tenantId,
        Guid id,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await TenantSql.BeginAuthorizedAsync(connection, actorId, tenantId, "tenant.notifications.read", cancellationToken);
        if (tx is null)
        {
            return new QueryAccess<UserNotificationRecord?>(QueryAccessStatus.Forbidden, null);
        }

        var row = await connection.QuerySingleOrDefaultAsync<UserNotificationRecord>(new CommandDefinition(
            $"""
            {SelectColumns}
              FROM odca.user_notifications
             WHERE tenant_id = @tenantId
               AND id = @id
               AND user_id = @actorId;
            """,
            new { tenantId, id, actorId },
            tx,
            cancellationToken: cancellationToken));
        await tx.CommitAsync(cancellationToken);
        return new QueryAccess<UserNotificationRecord?>(QueryAccessStatus.Ok, row);
    }

    public async Task<MutationResult> MarkReadAsync(
        Guid actorId,
        Guid tenantId,
        Guid id,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await TenantSql.BeginAuthorizedAsync(connection, actorId, tenantId, "tenant.notifications.read", cancellationToken);
        if (tx is null)
        {
            return new MutationResult(MutationStatus.Forbidden);
        }

        var updated = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE odca.user_notifications
               SET read_at = coalesce(read_at, now())
             WHERE tenant_id = @tenantId
               AND id = @id
               AND user_id = @actorId
               AND NOT is_obsolete;
            """,
            new { tenantId, id, actorId },
            tx,
            cancellationToken: cancellationToken));
        if (updated == 0)
        {
            await tx.RollbackAsync(cancellationToken);
            return new MutationResult(MutationStatus.NotFound);
        }

        await tx.CommitAsync(cancellationToken);
        return new MutationResult(MutationStatus.Succeeded);
    }

    private const string SelectColumns = """
        SELECT id AS "Id",
               category AS "Category",
               title AS "Title",
               body AS "Body",
               resource_type AS "ResourceType",
               resource_id AS "ResourceId",
               event_key AS "EventKey",
               status AS "Status",
               is_obsolete AS "IsObsolete",
               created_at AS "CreatedAt",
               relevant_date AS "RelevantDate",
               read_at AS "ReadAt"
        """;
}
