using Dapper;
using Npgsql;
using Odca.Application.Identity;

namespace Odca.Infrastructure.Identity;

public sealed class NpgsqlIdentityRepository(NpgsqlDataSource dataSource) : IIdentityRepository
{
    public async Task<UserCredential?> FindByLoginAsync(string normalizedLogin, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id AS "Id", email AS "Email", display_name AS "DisplayName",
                   password_hash AS "PasswordHash", security_version AS "SecurityVersion",
                   must_change_password AS "MustChangePassword",
                   is_platform_administrator AS "IsPlatformAdministrator",
                   is_deleted AS "IsDeleted", locked_until AS "LockedUntil"
            FROM odca.users
            WHERE email_normalized = @normalizedLogin OR login_normalized = @normalizedLogin
            LIMIT 1;
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<UserCredentialRow>(
            new CommandDefinition(sql, new { normalizedLogin }, cancellationToken: cancellationToken));
        return row?.ToModel();
    }

    public async Task<UserCredential?> FindByIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id AS "Id", email AS "Email", display_name AS "DisplayName",
                   password_hash AS "PasswordHash", security_version AS "SecurityVersion",
                   must_change_password AS "MustChangePassword",
                   is_platform_administrator AS "IsPlatformAdministrator",
                   is_deleted AS "IsDeleted", locked_until AS "LockedUntil"
            FROM odca.users WHERE id = @userId;
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<UserCredentialRow>(
            new CommandDefinition(sql, new { userId }, cancellationToken: cancellationToken));
        return row?.ToModel();
    }

    public async Task RecordFailedLoginAsync(Guid userId, DateTimeOffset occurredAt, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE odca.users
               SET failed_login_count = failed_login_count + 1,
                   locked_until = CASE WHEN failed_login_count + 1 >= 5 THEN @lockedUntil ELSE locked_until END,
                   updated_at = @occurredAt
             WHERE id = @userId;

            INSERT INTO odca.audit_events
                (scope_type, actor_user_id, action, entity_type, entity_id, occurred_at, result)
            VALUES ('platform', @userId, 'identity.login.failed', 'user', @userId, @occurredAt, 'denied');
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { userId, occurredAt, lockedUntil = occurredAt.AddMinutes(15) },
            cancellationToken: cancellationToken));
    }

    public async Task<SessionRecord> CreateSessionAsync(
        Guid userId,
        int securityVersion,
        DateTimeOffset expiresAt,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        var sessionId = Guid.NewGuid();
        const string sql = """
            INSERT INTO odca.sessions
                (id, user_id, security_version, expires_at, ip_address, user_agent)
            VALUES (@sessionId, @userId, @securityVersion, @expiresAt, @ipAddress::inet, @userAgent);

            UPDATE odca.users
               SET failed_login_count = 0, locked_until = NULL, last_login_at = now(), updated_at = now()
             WHERE id = @userId;

            INSERT INTO odca.audit_events
                (scope_type, actor_user_id, action, entity_type, entity_id, result)
            VALUES ('platform', @userId, 'identity.login.succeeded', 'session', @sessionId, 'success');
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                sessionId,
                userId,
                securityVersion,
                expiresAt,
                ipAddress = string.IsNullOrWhiteSpace(ipAddress) ? null : ipAddress,
                userAgent = Truncate(userAgent, 512)
            },
            cancellationToken: cancellationToken));
        return new SessionRecord(sessionId, userId, securityVersion, expiresAt);
    }

    public async Task<bool> IsSessionValidAsync(
        Guid userId,
        Guid sessionId,
        int securityVersion,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS (
                SELECT 1
                  FROM odca.sessions s
                  JOIN odca.users u ON u.id = s.user_id
                 WHERE s.id = @sessionId
                   AND s.user_id = @userId
                   AND s.revoked_at IS NULL
                   AND s.expires_at > @now
                   AND s.security_version = @securityVersion
                   AND u.security_version = @securityVersion
                   AND NOT u.is_deleted
                   AND (u.locked_until IS NULL OR u.locked_until <= @now)
            );
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            sql,
            new { sessionId, userId, securityVersion, now },
            cancellationToken: cancellationToken));
    }

    public async Task RevokeSessionAsync(
        Guid userId,
        Guid sessionId,
        DateTimeOffset revokedAt,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE odca.sessions
               SET revoked_at = COALESCE(revoked_at, @revokedAt)
             WHERE id = @sessionId AND user_id = @userId;

            INSERT INTO odca.audit_events
                (scope_type, actor_user_id, action, entity_type, entity_id, occurred_at, result)
            SELECT 'platform', @userId, 'identity.logout', 'session', @sessionId, @revokedAt, 'success'
             WHERE EXISTS (SELECT 1 FROM odca.sessions WHERE id = @sessionId AND user_id = @userId);
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { userId, sessionId, revokedAt },
            cancellationToken: cancellationToken));
    }

    public async Task<PasswordChangeResult> ChangePasswordAsync(
        Guid userId,
        string passwordHash,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE odca.users
               SET password_hash = @passwordHash,
                   must_change_password = false,
                   security_version = security_version + 1,
                   password_changed_at = @changedAt,
                   updated_at = @changedAt
             WHERE id = @userId
             RETURNING security_version;
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var securityVersion = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            sql,
            new { userId, passwordHash, changedAt },
            transaction,
            cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE odca.sessions SET revoked_at = COALESCE(revoked_at, @changedAt) WHERE user_id = @userId;",
            new { userId, changedAt },
            transaction,
            cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO odca.audit_events
                (scope_type, actor_user_id, action, entity_type, entity_id, occurred_at, result)
            VALUES ('platform', @userId, 'identity.password.changed', 'user', @userId, @changedAt, 'success');
            """,
            new { userId, changedAt },
            transaction,
            cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return new PasswordChangeResult(securityVersion);
    }

    private static string? Truncate(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength];

    private sealed class UserCredentialRow
    {
        public Guid Id { get; init; }

        public string Email { get; init; } = string.Empty;

        public string DisplayName { get; init; } = string.Empty;

        public string PasswordHash { get; init; } = string.Empty;

        public int SecurityVersion { get; init; }

        public bool MustChangePassword { get; init; }

        public bool IsPlatformAdministrator { get; init; }

        public bool IsDeleted { get; init; }

        public DateTime? LockedUntil { get; init; }

        public UserCredential ToModel() => new(
            Id,
            Email,
            DisplayName,
            PasswordHash,
            SecurityVersion,
            MustChangePassword,
            IsPlatformAdministrator,
            IsDeleted,
            LockedUntil is null
                ? null
                : new DateTimeOffset(DateTime.SpecifyKind(LockedUntil.Value, DateTimeKind.Utc)));
    }
}
