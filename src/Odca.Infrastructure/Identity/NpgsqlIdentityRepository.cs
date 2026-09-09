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
                   mfa_confirmed_at AS "MfaConfirmedAt",
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
                   mfa_confirmed_at AS "MfaConfirmedAt",
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
                (id, user_id, security_version, expires_at, ip_address, user_agent, authentication_level)
            VALUES (@sessionId, @userId, @securityVersion, @expiresAt, @ipAddress::inet, @userAgent, 'password');

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
        return new SessionRecord(sessionId, userId, securityVersion, expiresAt, "password", null);
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
                   AND s.authentication_level IN ('password', 'mfa')
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

    public async Task SavePendingMfaSecretAsync(
        Guid userId,
        string protectedSecret,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE odca.users
               SET mfa_secret_protected = @protectedSecret,
                   mfa_confirmed_at = NULL,
                   mfa_last_accepted_time_step = NULL,
                   updated_at = @now
             WHERE id = @userId
               AND is_platform_administrator
               AND NOT must_change_password
               AND NOT is_deleted;

            INSERT INTO odca.audit_events
                (scope_type, actor_user_id, action, entity_type, entity_id, occurred_at, result)
            VALUES ('platform', @userId, 'identity.mfa.enrollment.started', 'user', @userId, @now, 'success');
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { userId, protectedSecret, now },
            cancellationToken: cancellationToken));
    }

    public async Task<string?> GetProtectedMfaSecretAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT mfa_secret_protected FROM odca.users WHERE id = @userId AND NOT is_deleted;",
            new { userId },
            cancellationToken: cancellationToken));
    }

    public async Task<bool> ConfirmMfaAsync(
        Guid userId,
        Guid sessionId,
        int securityVersion,
        DateTimeOffset verifiedAt,
        long acceptedTimeStep,
        IReadOnlyList<string> recoveryCodeHashes,
        CancellationToken cancellationToken)
    {
        const string updateSql = """
            UPDATE odca.users
               SET mfa_confirmed_at = @verifiedAt,
                   mfa_last_accepted_time_step = @acceptedTimeStep,
                   updated_at = @verifiedAt
             WHERE id = @userId
               AND security_version = @securityVersion
               AND mfa_secret_protected IS NOT NULL
               AND mfa_confirmed_at IS NULL
               AND NOT must_change_password
               AND NOT is_deleted;
            """;
        const string sessionSql = """
            UPDATE odca.sessions
               SET authentication_level = 'mfa',
                   mfa_completed_at = @verifiedAt,
                   mfa_failed_attempts = 0
             WHERE id = @sessionId
               AND user_id = @userId
               AND security_version = @securityVersion
               AND revoked_at IS NULL
               AND expires_at > @verifiedAt;
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var updated = await connection.ExecuteAsync(new CommandDefinition(
            updateSql,
            new { userId, securityVersion, verifiedAt, acceptedTimeStep },
            transaction,
            cancellationToken: cancellationToken));
        var sessionUpdated = await connection.ExecuteAsync(new CommandDefinition(
            sessionSql,
            new { userId, sessionId, securityVersion, verifiedAt },
            transaction,
            cancellationToken: cancellationToken));
        if (updated != 1 || sessionUpdated != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM odca.mfa_recovery_codes WHERE user_id = @userId;",
            new { userId },
            transaction,
            cancellationToken: cancellationToken));
        foreach (var recoveryCodeHash in recoveryCodeHashes)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO odca.mfa_recovery_codes (user_id, code_hash, created_at)
                VALUES (@userId, @recoveryCodeHash, @verifiedAt);
                """,
                new { userId, recoveryCodeHash, verifiedAt },
                transaction,
                cancellationToken: cancellationToken));
        }

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO odca.audit_events
                (scope_type, actor_user_id, action, entity_type, entity_id, occurred_at, result)
            VALUES ('platform', @userId, 'identity.mfa.enrollment.confirmed', 'session', @sessionId, @verifiedAt, 'success');
            """,
            new { userId, sessionId, verifiedAt },
            transaction,
            cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<MfaChallengeState?> GetMfaChallengeStateAsync(
        Guid userId,
        Guid sessionId,
        int securityVersion,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT u.mfa_secret_protected AS "ProtectedSecret",
                   u.mfa_confirmed_at AS "ConfirmedAt",
                   u.mfa_last_accepted_time_step AS "LastAcceptedTimeStep",
                   s.mfa_failed_attempts AS "FailedAttempts"
              FROM odca.users u
              JOIN odca.sessions s ON s.user_id = u.id
             WHERE u.id = @userId
               AND u.security_version = @securityVersion
               AND s.id = @sessionId
               AND s.security_version = @securityVersion
               AND s.revoked_at IS NULL
               AND s.expires_at > @now
               AND NOT u.is_deleted;
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<MfaChallengeState>(new CommandDefinition(
            sql,
            new { userId, sessionId, securityVersion, now },
            cancellationToken: cancellationToken));
    }

    public async Task<bool> CompleteMfaChallengeAsync(
        Guid userId,
        Guid sessionId,
        int securityVersion,
        DateTimeOffset verifiedAt,
        long? acceptedTimeStep,
        string? recoveryCodeHash,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (acceptedTimeStep is not null)
        {
            var updatedUser = await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE odca.users
                   SET mfa_last_accepted_time_step = @acceptedTimeStep,
                       updated_at = @verifiedAt
                 WHERE id = @userId
                   AND security_version = @securityVersion
                   AND mfa_confirmed_at IS NOT NULL
                   AND (mfa_last_accepted_time_step IS NULL OR mfa_last_accepted_time_step < @acceptedTimeStep);
                """,
                new { userId, securityVersion, acceptedTimeStep, verifiedAt },
                transaction,
                cancellationToken: cancellationToken));
            if (updatedUser != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }
        }
        else if (recoveryCodeHash is not null)
        {
            var consumed = await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE odca.mfa_recovery_codes
                   SET consumed_at = @verifiedAt,
                       consumed_session_id = @sessionId
                 WHERE user_id = @userId
                   AND code_hash = @recoveryCodeHash
                   AND consumed_at IS NULL;
                """,
                new { userId, recoveryCodeHash, sessionId, verifiedAt },
                transaction,
                cancellationToken: cancellationToken));
            if (consumed != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }
        }
        else
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        var sessionUpdated = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE odca.sessions
               SET authentication_level = 'mfa',
                   mfa_completed_at = @verifiedAt,
                   mfa_failed_attempts = 0
             WHERE id = @sessionId
               AND user_id = @userId
               AND security_version = @securityVersion
               AND revoked_at IS NULL
               AND expires_at > @verifiedAt;
            """,
            new { userId, sessionId, securityVersion, verifiedAt },
            transaction,
            cancellationToken: cancellationToken));
        if (sessionUpdated != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO odca.audit_events
                (scope_type, actor_user_id, action, entity_type, entity_id, occurred_at, result)
            VALUES ('platform', @userId, 'identity.mfa.challenge.succeeded', 'session', @sessionId, @verifiedAt, 'success');
            """,
            new { userId, sessionId, verifiedAt },
            transaction,
            cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task RecordFailedMfaChallengeAsync(
        Guid userId,
        Guid sessionId,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE odca.sessions
               SET mfa_failed_attempts = mfa_failed_attempts + 1,
                   revoked_at = CASE
                       WHEN mfa_failed_attempts + 1 >= 5 THEN COALESCE(revoked_at, @occurredAt)
                       ELSE revoked_at
                   END
             WHERE id = @sessionId AND user_id = @userId AND revoked_at IS NULL;

            INSERT INTO odca.audit_events
                (scope_type, actor_user_id, action, entity_type, entity_id, occurred_at, result)
            VALUES ('platform', @userId, 'identity.mfa.challenge.failed', 'session', @sessionId, @occurredAt, 'denied');
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { userId, sessionId, occurredAt },
            cancellationToken: cancellationToken));
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

        public DateTime? MfaConfirmedAt { get; init; }

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
            MfaConfirmedAt is null
                ? null
                : new DateTimeOffset(DateTime.SpecifyKind(MfaConfirmedAt.Value, DateTimeKind.Utc)),
            IsDeleted,
            LockedUntil is null
                ? null
                : new DateTimeOffset(DateTime.SpecifyKind(LockedUntil.Value, DateTimeKind.Utc)));
    }
}
