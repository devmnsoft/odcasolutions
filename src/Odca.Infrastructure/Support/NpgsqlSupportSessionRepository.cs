using Dapper;
using Npgsql;
using Odca.Application.Support;

namespace Odca.Infrastructure.Support;

public sealed class NpgsqlSupportSessionRepository(NpgsqlDataSource dataSource) : ISupportSessionRepository
{
    private const string SelectColumns = """
        SELECT s.id AS Id,
               s.tenant_id AS TenantId,
               COALESCE(odca.support_session_tenant_name(@actorId, s.tenant_id), '-') AS TenantName,
               s.superadmin_user_id AS SuperadminUserId,
               COALESCE(u.display_name, '-') AS SuperadminName,
               s.reason AS Reason,
               s.status AS Status,
               (s.status = 'active' AND s.expires_at > now()) AS Valid,
               s.started_at AS StartedAt,
               s.expires_at AS ExpiresAt,
               s.ended_at AS EndedAt
           FROM odca.support_sessions s
           JOIN odca.users u ON u.id = s.superadmin_user_id
        """;

    private sealed class SessionRow
    {
        public Guid Id { get; init; }
        public Guid TenantId { get; init; }
        public string TenantName { get; init; } = "";
        public Guid SuperadminUserId { get; init; }
        public string SuperadminName { get; init; } = "";
        public string Reason { get; init; } = "";
        public string Status { get; init; } = "";
        public bool Valid { get; init; }
        public DateTimeOffset StartedAt { get; init; }
        public DateTimeOffset ExpiresAt { get; init; }
        public DateTimeOffset? EndedAt { get; init; }
    }

    private sealed class EventRow
    {
        public Guid SessionId { get; init; }
        public Guid ActorUserId { get; init; }
        public string ActorName { get; init; } = "";
        public string EventType { get; init; } = "";
        public DateTimeOffset OccurredAt { get; init; }
        public string Details { get; init; } = "";
    }

    private static SupportSessionInfo ToInfo(SessionRow row) => new(
        row.Id, row.TenantId, row.TenantName, row.SuperadminUserId, row.SuperadminName,
        row.Reason, row.Status, row.Valid, row.StartedAt, row.ExpiresAt, row.EndedAt);

    public async Task<SupportSessionListResult> ListRecentAsync(Guid actorId, int limit, CancellationToken cancellationToken)
    {
        try
        {
            var rows = await QueryRowsAsync(
                actorId, SelectColumns + " ORDER BY s.started_at DESC LIMIT @limit", new { limit }, cancellationToken);
            return new(true, rows.Select(ToInfo).ToArray());
        }
        catch (PostgresException)
        {
            return new(false, []);
        }
    }

    public async Task<SupportSessionInfo?> GetAsync(Guid actorId, Guid sessionId, CancellationToken cancellationToken)
    {
        try
        {
            var row = await QueryRowsAsync(actorId, SelectColumns + " WHERE s.id = @id", new { id = sessionId }, cancellationToken);
            return row.Count == 1 ? ToInfo(row[0]) : null;
        }
        catch (PostgresException)
        {
            return null;
        }
    }

    private async Task<IReadOnlyList<SessionRow>> QueryRowsAsync(
        Guid actorId, string sql, object args, CancellationToken cancellationToken)
    {
        var parameters = new DynamicParameters(args);
        parameters.Add("actorId", actorId);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT set_config('odca.user_id', @actor, true)",
            new { actor = actorId.ToString() },
            transaction,
            cancellationToken: cancellationToken));
        return (await connection.QueryAsync<SessionRow>(new CommandDefinition(
            sql, parameters, transaction, cancellationToken: cancellationToken))).AsList();
    }

    public async Task<(Guid Id, SupportSessionOpenError Error)> OpenAsync(
        Guid actorId, Guid tenantId, string reason, int durationMinutes, CancellationToken cancellationToken)
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
            var id = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
                "SELECT odca.open_support_session(@actor, @tenant, @reason, @minutes)",
                new { actor = actorId, tenant = tenantId, reason, minutes = durationMinutes },
                transaction,
                cancellationToken: cancellationToken));
            await transaction.CommitAsync(cancellationToken);
            return (id, SupportSessionOpenError.None);
        }
        catch (PostgresException exception) when (exception.SqlState is "23505" or "22023" or "23503")
        {
            return (Guid.Empty, exception.SqlState == "23505"
                ? SupportSessionOpenError.AlreadyActive
                : SupportSessionOpenError.Validation);
        }
        catch (PostgresException)
        {
            return (Guid.Empty, SupportSessionOpenError.Unavailable);
        }
    }

    public async Task<DateTimeOffset?> ExtendAsync(
        Guid actorId, Guid sessionId, int minutes, CancellationToken cancellationToken)
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
            var newExpires = await connection.ExecuteScalarAsync<DateTime?>(new CommandDefinition(
                "SELECT odca.extend_support_session(@actor, @id, @minutes)",
                new { actor = actorId, id = sessionId, minutes },
                transaction,
                cancellationToken: cancellationToken));
            await transaction.CommitAsync(cancellationToken);
            return newExpires is null ? null : new DateTimeOffset(newExpires.Value, TimeSpan.Zero);
        }
        catch (PostgresException)
        {
            return null;
        }
    }

    public async Task<bool> CloseAsync(Guid actorId, Guid sessionId, bool revoke, CancellationToken cancellationToken)
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
            await connection.ExecuteAsync(new CommandDefinition(
                "SELECT odca.close_support_session(@actor, @id, @revoke)",
                new { actor = actorId, id = sessionId, revoke },
                transaction,
                cancellationToken: cancellationToken));
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch (PostgresException)
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<SupportSessionEvent>> EventsAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            var rows = (await connection.QueryAsync<EventRow>(new CommandDefinition("""
                SELECT e.session_id AS SessionId,
                       e.actor_user_id AS ActorUserId,
                       COALESCE(u.display_name, '-') AS ActorName,
                       e.event_type AS EventType,
                       e.occurred_at AS OccurredAt,
                       e.details::text AS Details
                  FROM odca.support_session_events e
                  JOIN odca.users u ON u.id = e.actor_user_id
                 WHERE e.session_id = @id
                 ORDER BY e.occurred_at DESC, e.id DESC
                 LIMIT 100
                """,
                new { id = sessionId },
                cancellationToken: cancellationToken))).AsList();
            return rows.Select(r => new SupportSessionEvent(
                r.SessionId, r.ActorUserId, r.ActorName, r.EventType, r.OccurredAt, r.Details)).ToArray();
        }
        catch (PostgresException)
        {
            return [];
        }
    }

    // D-OC43 (Sprint 9/F-1): impersonação profunda na janela de suporte.

    private sealed class ImpersonationRow
    {
        public Guid imp_grant { get; init; }
        public Guid imp_target_session { get; init; }
        public DateTimeOffset imp_expires { get; init; }
    }

    private sealed class MemberRow
    {
        public Guid user_id { get; init; }
        public string display_name { get; init; } = "";
        public string email { get; init; } = "";
    }

    private static SupportImpersonationStart Failed(SupportImpersonationStartError error) => new(
        false, error, Guid.Empty, Guid.Empty, Guid.Empty, DateTimeOffset.MinValue);

    public async Task<SupportImpersonationStart> StartImpersonationAsync(
        Guid actorId, Guid sessionId, Guid targetUserId, int minutes, CancellationToken cancellationToken)
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
            var row = await connection.QueryFirstOrDefaultAsync<ImpersonationRow>(new CommandDefinition(
                "SELECT * FROM odca.start_support_impersonation(@actor, @session, @target, @minutes)",
                new { actor = actorId, session = sessionId, target = targetUserId, minutes },
                transaction,
                cancellationToken: cancellationToken));
            await transaction.CommitAsync(cancellationToken);
            return row is null
                ? Failed(SupportImpersonationStartError.Unavailable)
                : new(true, SupportImpersonationStartError.None, row.imp_grant, targetUserId, row.imp_target_session, row.imp_expires);
        }
        catch (PostgresException exception) when (exception.SqlState is "23505" or "02000" or "22023")
        {
            return Failed(exception.SqlState == "23505"
                ? SupportImpersonationStartError.AlreadyActive
                : SupportImpersonationStartError.Validation);
        }
        catch (PostgresException)
        {
            return Failed(SupportImpersonationStartError.Unavailable);
        }
    }

    public async Task<bool> EndImpersonationAsync(
        Guid actorId, Guid grantId, bool revoked, CancellationToken cancellationToken)
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
            var ended = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                "SELECT odca.end_support_impersonation(@actor, @grant, @revoked)",
                new { actor = actorId, grant = grantId, revoked },
                transaction,
                cancellationToken: cancellationToken));
            await transaction.CommitAsync(cancellationToken);
            return ended;
        }
        catch (PostgresException)
        {
            return false;
        }
    }

    public async Task<bool> IsImpersonationValidAsync(Guid grantId, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                "SELECT odca.support_impersonation_is_valid(@grant)",
                new { grant = grantId },
                cancellationToken: cancellationToken));
        }
        catch (PostgresException)
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<SupportSessionMemberInfo>> ListMembersAsync(
        Guid actorId, Guid tenantId, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            var rows = (await connection.QueryAsync<MemberRow>(new CommandDefinition(
                "SELECT * FROM odca.support_session_members(@actor, @tenant)",
                new { actor = actorId, tenant = tenantId },
                cancellationToken: cancellationToken))).AsList();
            return rows.Select(r => new SupportSessionMemberInfo(r.user_id, r.display_name, r.email)).ToArray();
        }
        catch (PostgresException)
        {
            return [];
        }
    }
}
