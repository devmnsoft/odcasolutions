using Odca.Application.Common;
using Odca.Application.Identity;

namespace Odca.Domain.Tests;

public sealed class AuthenticationServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task LockedAccountDoesNotExtendItsLockWindow()
    {
        var repository = new FakeRepository(CreateUser() with { LockedUntil = Now.AddMinutes(5) });
        var service = CreateService(repository, passwordIsValid: false, sessionMinutes: 15);

        var result = await service.LoginAsync("USER@EXAMPLE.TEST", "wrong", null, null, default);

        Assert.False(result.Succeeded);
        Assert.Equal(0, repository.FailedLoginWrites);
        Assert.Null(repository.CreatedSessionExpiresAt);
    }

    [Fact]
    public async Task SessionLifetimeUsesValidatedConfiguration()
    {
        var repository = new FakeRepository(CreateUser());
        var service = CreateService(repository, passwordIsValid: true, sessionMinutes: 23);

        var result = await service.LoginAsync("USER@EXAMPLE.TEST", "valid", null, null, default);

        Assert.True(result.Succeeded);
        Assert.Equal(Now.AddMinutes(23), repository.CreatedSessionExpiresAt);
    }

    private static AuthenticationService CreateService(
        FakeRepository repository,
        bool passwordIsValid,
        int sessionMinutes) => new(
            repository,
            new FakePasswordService(passwordIsValid),
            new FakeTokenService(),
            new FakeClock(),
            new AuthenticationPolicy(TimeSpan.FromMinutes(sessionMinutes)));

    private static UserCredential CreateUser() => new(
        Guid.Parse("40000000-0000-0000-0000-000000000001"),
        "user@example.test",
        "Test user",
        "hash",
        1,
        false,
        false,
        false,
        null);

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class FakePasswordService(bool valid) : IPasswordService
    {
        public string Hash(UserCredential user, string password) => "hash";

        public bool Verify(UserCredential user, string passwordHash, string providedPassword) => valid;
    }

    private sealed class FakeTokenService : ITokenService
    {
        public IssuedToken Issue(UserCredential user, SessionRecord session) =>
            new("token", session.ExpiresAt);
    }

    private sealed class FakeRepository(UserCredential user) : IIdentityRepository
    {
        public int FailedLoginWrites { get; private set; }

        public DateTimeOffset? CreatedSessionExpiresAt { get; private set; }

        public Task<UserCredential?> FindByLoginAsync(string normalizedLogin, CancellationToken cancellationToken) =>
            Task.FromResult<UserCredential?>(user);

        public Task<UserCredential?> FindByIdAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<UserCredential?>(user);

        public Task RecordFailedLoginAsync(Guid userId, DateTimeOffset occurredAt, CancellationToken cancellationToken)
        {
            FailedLoginWrites++;
            return Task.CompletedTask;
        }

        public Task<SessionRecord> CreateSessionAsync(
            Guid userId,
            int securityVersion,
            DateTimeOffset expiresAt,
            string? ipAddress,
            string? userAgent,
            CancellationToken cancellationToken)
        {
            CreatedSessionExpiresAt = expiresAt;
            return Task.FromResult(new SessionRecord(Guid.NewGuid(), userId, securityVersion, expiresAt));
        }

        public Task<bool> IsSessionValidAsync(
            Guid userId,
            Guid sessionId,
            int securityVersion,
            DateTimeOffset now,
            CancellationToken cancellationToken) => Task.FromResult(true);

        public Task RevokeSessionAsync(
            Guid userId,
            Guid sessionId,
            DateTimeOffset revokedAt,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<PasswordChangeResult> ChangePasswordAsync(
            Guid userId,
            string passwordHash,
            DateTimeOffset changedAt,
            CancellationToken cancellationToken) => Task.FromResult(new PasswordChangeResult(user.SecurityVersion + 1));
    }
}
