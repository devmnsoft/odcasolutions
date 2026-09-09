using Odca.Application.Common;
using Odca.Domain.Identity;

namespace Odca.Application.Identity;

public sealed class AuthenticationService(
    IIdentityRepository repository,
    IPasswordService passwordService,
    ITokenService tokenService,
    IClock clock)
{
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromMinutes(15);

    public async Task<LoginResult> LoginAsync(
        string login,
        string password,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        var normalizedLogin = NormalizeLogin(login);
        var user = await repository.FindByLoginAsync(normalizedLogin, cancellationToken);
        var now = clock.UtcNow;

        if (user is null || user.IsDeleted || user.LockedUntil > now ||
            !passwordService.Verify(user, user.PasswordHash, password))
        {
            if (user is not null && !user.IsDeleted)
            {
                await repository.RecordFailedLoginAsync(user.Id, now, cancellationToken);
            }

            return LoginResult.Failed();
        }

        var session = await repository.CreateSessionAsync(
            user.Id,
            user.SecurityVersion,
            now.Add(SessionLifetime),
            ipAddress,
            userAgent,
            cancellationToken);
        var token = tokenService.Issue(user, session);
        return new LoginResult(true, null, user, session, token);
    }

    public async Task<ChangePasswordOutcome> ChangePasswordAsync(
        Guid userId,
        Guid currentSessionId,
        string currentPassword,
        string newPassword,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        var user = await repository.FindByIdAsync(userId, cancellationToken);
        if (user is null || !passwordService.Verify(user, user.PasswordHash, currentPassword))
        {
            return new(false, ["A senha atual está incorreta."], null, null, null);
        }

        var errors = PasswordPolicy.Validate(newPassword);
        if (errors.Count > 0)
        {
            return new(false, errors, null, null, null);
        }

        var now = clock.UtcNow;
        var hash = passwordService.Hash(user, newPassword);
        var changed = await repository.ChangePasswordAsync(userId, hash, now, cancellationToken);
        await repository.RevokeSessionAsync(userId, currentSessionId, now, cancellationToken);

        var updatedUser = user with
        {
            PasswordHash = hash,
            SecurityVersion = changed.SecurityVersion,
            MustChangePassword = false
        };
        var session = await repository.CreateSessionAsync(
            userId,
            changed.SecurityVersion,
            now.Add(SessionLifetime),
            ipAddress,
            userAgent,
            cancellationToken);
        return new(true, [], updatedUser, session, tokenService.Issue(updatedUser, session));
    }

    public Task LogoutAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken) =>
        repository.RevokeSessionAsync(userId, sessionId, clock.UtcNow, cancellationToken);

    public static string NormalizeLogin(string login)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(login);
        var trimmed = login.Trim();
        return trimmed.Contains('@', StringComparison.Ordinal)
            ? trimmed.ToUpperInvariant()
            : string.Concat(trimmed.Where(char.IsLetterOrDigit)).ToUpperInvariant();
    }
}
