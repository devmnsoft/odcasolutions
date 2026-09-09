namespace Odca.Application.Identity;

public sealed record UserCredential(
    Guid Id,
    string Email,
    string DisplayName,
    string PasswordHash,
    int SecurityVersion,
    bool MustChangePassword,
    bool IsPlatformAdministrator,
    bool IsDeleted,
    DateTimeOffset? LockedUntil);

public sealed record SessionRecord(Guid Id, Guid UserId, int SecurityVersion, DateTimeOffset ExpiresAt);

public sealed record IssuedToken(string AccessToken, DateTimeOffset ExpiresAt);

public sealed record LoginResult(
    bool Succeeded,
    string? FailureMessage,
    UserCredential? User,
    SessionRecord? Session,
    IssuedToken? Token)
{
    public static LoginResult Failed() => new(false, "E-mail/CPF ou senha inválidos.", null, null, null);
}

public sealed record PasswordChangeResult(int SecurityVersion);

public sealed record ChangePasswordOutcome(
    bool Succeeded,
    IReadOnlyList<string> Errors,
    UserCredential? User,
    SessionRecord? Session,
    IssuedToken? Token);
