using Odca.Application.Identity;

namespace Odca.Domain.Tests;

public sealed class LoginLockoutPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 15, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void BelowThresholdDoesNotLock(int failedCount) =>
        Assert.Null(LoginLockoutPolicy.NextLockUntil(failedCount, Now));

    [Fact]
    public void FifthFailureLocksForFifteenMinutes()
    {
        var until = LoginLockoutPolicy.NextLockUntil(5, Now);
        Assert.Equal(Now.AddMinutes(15), until);
        Assert.True(LoginLockoutPolicy.IsLocked(until, Now));
        Assert.False(LoginLockoutPolicy.IsLocked(until, Now.AddMinutes(15)));
    }
}
