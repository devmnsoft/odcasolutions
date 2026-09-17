namespace Odca.Application.Identity;

/// <summary>
/// Lockout after consecutive failed password checks. Persistence remains in
/// odca.users (failed_login_count / locked_until). The HTTP login always returns
/// a generic failure so accounts are not enumerated.
/// </summary>
public static class LoginLockoutPolicy
{
    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan Duration = TimeSpan.FromMinutes(15);

    public static bool IsLocked(DateTimeOffset? lockedUntil, DateTimeOffset now) =>
        lockedUntil is not null && lockedUntil.Value > now;

    public static DateTimeOffset? NextLockUntil(int failedCountAfterAttempt, DateTimeOffset now) =>
        failedCountAfterAttempt >= MaxFailedAttempts ? now.Add(Duration) : null;
}
