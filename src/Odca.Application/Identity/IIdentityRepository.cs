namespace Odca.Application.Identity;

public interface IIdentityRepository
{
    Task<UserCredential?> FindByLoginAsync(string normalizedLogin, CancellationToken cancellationToken);

    Task<UserCredential?> FindByIdAsync(Guid userId, CancellationToken cancellationToken);

    Task RecordFailedLoginAsync(Guid userId, DateTimeOffset occurredAt, CancellationToken cancellationToken);

    Task<SessionRecord> CreateSessionAsync(
        Guid userId,
        int securityVersion,
        DateTimeOffset expiresAt,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken);

    Task<bool> IsSessionValidAsync(
        Guid userId,
        Guid sessionId,
        int securityVersion,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task RevokeSessionAsync(Guid userId, Guid sessionId, DateTimeOffset revokedAt, CancellationToken cancellationToken);

    Task<PasswordChangeResult> ChangePasswordAsync(
        Guid userId,
        string passwordHash,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken);

    Task<bool> SavePendingMfaSecretAsync(
        Guid userId,
        Guid sessionId,
        int securityVersion,
        string protectedSecret,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<string?> GetProtectedMfaSecretAsync(Guid userId, CancellationToken cancellationToken);

    Task<bool> ConfirmMfaAsync(
        Guid userId,
        Guid sessionId,
        int securityVersion,
        DateTimeOffset verifiedAt,
        DateTimeOffset expiresAt,
        string expectedProtectedSecret,
        long acceptedTimeStep,
        IReadOnlyList<string> recoveryCodeHashes,
        CancellationToken cancellationToken);

    Task<MfaChallengeState?> GetMfaChallengeStateAsync(
        Guid userId,
        Guid sessionId,
        int securityVersion,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<bool> CompleteMfaChallengeAsync(
        Guid userId,
        Guid sessionId,
        int securityVersion,
        DateTimeOffset verifiedAt,
        DateTimeOffset expiresAt,
        long? acceptedTimeStep,
        string? recoveryCodeHash,
        CancellationToken cancellationToken);

    Task RecordFailedMfaChallengeAsync(
        Guid userId,
        Guid sessionId,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken);
}

public sealed record MfaChallengeState(
    string ProtectedSecret,
    DateTimeOffset? ConfirmedAt,
    long? LastAcceptedTimeStep,
    int FailedAttempts);
