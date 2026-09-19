using Odca.Contracts.SavedViews;

namespace Odca.Domain.Tests;

public sealed class RelativeDateResolverTests
{
    private static readonly TimeZoneInfo SaoPauloTimeZone = TimeZoneInfo.FindSystemTimeZoneById(
        OperatingSystem.IsWindows() ? "E. South America Standard Time" : "America/Sao_Paulo");

    [Theory]
    [InlineData("today")]
    [InlineData("overdue")]
    [InlineData("dueToday")]
    [InlineData("dueThisWeek")]
    [InlineData("nextDays:0")]
    [InlineData("nextDays:7")]
    [InlineData("nextDays:30")]
    [InlineData("nextDays:365")]
    public void ValidTokensAreRecognized(string token)
    {
        Assert.True(RelativeDateResolver.IsValid(token));
        Assert.True(RelativeDateResolver.TryNormalize(token, out var normalized));
        Assert.False(string.IsNullOrWhiteSpace(normalized));
    }

    [Theory]
    [InlineData("Today", "today")]
    [InlineData("OVERDUE", "overdue")]
    [InlineData("duetoday", "dueToday")]
    [InlineData("DueToday", "dueToday")]
    [InlineData("duethisweek", "dueThisWeek")]
    [InlineData("DueThisWeek", "dueThisWeek")]
    [InlineData("NEXTDAYS:15", "nextDays:15")]
    [InlineData("nextdays:3", "nextDays:3")]
    public void ValidTokensAreNormalizedToCanonicalForm(string input, string expected)
    {
        Assert.True(RelativeDateResolver.TryNormalize(input, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("tomorrow")]
    [InlineData("yesterday")]
    [InlineData("nextWeek")]
    [InlineData("nextDays:-1")]
    [InlineData("nextDays:366")]
    [InlineData("nextDays:abc")]
    [InlineData("due_today")]
    [InlineData("due_this_week")]
    public void InvalidTokensAreRejected(string token)
    {
        Assert.False(RelativeDateResolver.IsValid(token));
        Assert.False(RelativeDateResolver.TryNormalize(token, out _));
    }

    [Fact]
    public void RangesAreCorrectlyResolvedForBaseDate()
    {
        var civilDate = new DateOnly(2026, 9, 19);

        var (todayFrom, todayTo) = RelativeDateResolver.ResolveRange("today", civilDate);
        Assert.Equal(civilDate, todayFrom);
        Assert.Equal(civilDate, todayTo);

        var (dueTodayFrom, dueTodayTo) = RelativeDateResolver.ResolveRange("dueToday", civilDate);
        Assert.Equal(civilDate, dueTodayFrom);
        Assert.Equal(civilDate, dueTodayTo);

        var (overdueFrom, overdueTo) = RelativeDateResolver.ResolveRange("overdue", civilDate);
        Assert.Null(overdueFrom);
        Assert.Equal(new DateOnly(2026, 9, 18), overdueTo);

        var (weekFrom, weekTo) = RelativeDateResolver.ResolveRange("dueThisWeek", civilDate);
        Assert.Equal(civilDate, weekFrom);
        Assert.Equal(new DateOnly(2026, 9, 26), weekTo);

        var (nextDaysFrom, nextDaysTo) = RelativeDateResolver.ResolveRange("nextDays:5", civilDate);
        Assert.Equal(civilDate, nextDaysFrom);
        Assert.Equal(new DateOnly(2026, 9, 24), nextDaysTo);
    }

    [Fact]
    public void DueThisWeekTransitionsAcrossTenantCivilMidnightWithTimeProvider()
    {
        // 2026-09-21 02:59:59 UTC = 2026-09-20 23:59:59 in America/Sao_Paulo (UTC-3)
        var timeBeforeMidnight = new FakeTimeProvider(new DateTimeOffset(2026, 9, 21, 2, 59, 59, TimeSpan.Zero));
        var civilBefore = RelativeDateResolver.ResolveCivilDate(timeBeforeMidnight, SaoPauloTimeZone);
        Assert.Equal(new DateOnly(2026, 9, 20), civilBefore);

        var (rangeBeforeFrom, rangeBeforeTo) = RelativeDateResolver.ResolveRange("dueThisWeek", civilBefore);
        Assert.Equal(new DateOnly(2026, 9, 20), rangeBeforeFrom);
        Assert.Equal(new DateOnly(2026, 9, 27), rangeBeforeTo);

        // 2026-09-21 03:00:01 UTC = 2026-09-21 00:00:01 in America/Sao_Paulo (UTC-3)
        var timeAfterMidnight = new FakeTimeProvider(new DateTimeOffset(2026, 9, 21, 3, 0, 1, TimeSpan.Zero));
        var civilAfter = RelativeDateResolver.ResolveCivilDate(timeAfterMidnight, SaoPauloTimeZone);
        Assert.Equal(new DateOnly(2026, 9, 21), civilAfter);

        var (rangeAfterFrom, rangeAfterTo) = RelativeDateResolver.ResolveRange("dueThisWeek", civilAfter);
        Assert.Equal(new DateOnly(2026, 9, 21), rangeAfterFrom);
        Assert.Equal(new DateOnly(2026, 9, 28), rangeAfterTo);
    }

    [Fact]
    public void ProposalDefaultCivilDateDivergesFromUtcMidnight()
    {
        // 2026-09-20 01:30:00 UTC is Sunday in UTC, but Saturday 22:30:00 in America/Sao_Paulo
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 9, 20, 1, 30, 0, TimeSpan.Zero));
        var civilDate = RelativeDateResolver.ResolveCivilDate(timeProvider, SaoPauloTimeZone);

        Assert.Equal(new DateOnly(2026, 9, 19), civilDate);
        Assert.NotEqual(DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime), civilDate);
    }

    [Theory]
    [InlineData("overdue", "Overdue")]
    [InlineData("today", "DueToday")]
    [InlineData("dueToday", "DueToday")]
    [InlineData("dueThisWeek", "DueThisWeek")]
    [InlineData("nextDays:7", null)]
    public void UrgencyMappingMatchesExpectedInboxUrgency(string token, string? expectedUrgency)
    {
        var urgency = RelativeDateResolver.MapToUrgency(token);
        Assert.Equal(expectedUrgency, urgency);
    }

    private sealed class FakeTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
