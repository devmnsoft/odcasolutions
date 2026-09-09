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
}
