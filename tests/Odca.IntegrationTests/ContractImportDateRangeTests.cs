using Dapper;
using Npgsql;
using Odca.Api.Controllers;

namespace Odca.IntegrationTests;

public sealed class ContractImportDateRangeTests
{
    [Fact]
    public void NoDatesProduceNoTimestampLimits()
    {
        var range = ContractImportsController.CreateUtcRange(null, null, TimeZoneInfo.Utc);

        Assert.Null(range.FromInclusive);
        Assert.Null(range.ToExclusive);
    }

    [Fact]
    public void ASingleDayUsesInclusiveStartAndExclusiveFollowingStart()
    {
        var range = ContractImportsController.CreateUtcRange(
            new DateOnly(2026, 9, 16),
            new DateOnly(2026, 9, 16),
            TimeZoneInfo.Utc);

        Assert.Equal(new DateTimeOffset(2026, 9, 16, 0, 0, 0, TimeSpan.Zero), range.FromInclusive);
        Assert.Equal(new DateTimeOffset(2026, 9, 17, 0, 0, 0, TimeSpan.Zero), range.ToExclusive);
    }

    [Fact]
    public void OrganizationTimeZoneControlsBothLimitsAcrossDaylightSavingChange()
    {
        var range = ContractImportsController.CreateUtcRange(
            new DateOnly(2026, 3, 8),
            new DateOnly(2026, 3, 8),
            TimeZoneInfo.FindSystemTimeZoneById("America/New_York"));

        Assert.Equal(new DateTimeOffset(2026, 3, 8, 5, 0, 0, TimeSpan.Zero), range.FromInclusive);
        Assert.Equal(new DateTimeOffset(2026, 3, 9, 4, 0, 0, TimeSpan.Zero), range.ToExclusive);
    }

    [Fact]
    public void MaximumFinalDateDoesNotOverflow()
    {
        var range = ContractImportsController.CreateUtcRange(null, DateOnly.MaxValue, TimeZoneInfo.Utc);

        Assert.Null(range.FromInclusive);
        Assert.Null(range.ToExclusive);
    }

    [Fact]
    public void InvertedRangeIsRejected()
    {
        Assert.Throws<ArgumentException>(() => ContractImportsController.CreateUtcRange(
            new DateOnly(2026, 9, 17), new DateOnly(2026, 9, 16), TimeZoneInfo.Utc));
    }

    [Fact]
    public void MinimumInitialDateDoesNotOverflow()
    {
        var range = ContractImportsController.CreateUtcRange(DateOnly.MinValue, null, TimeZoneInfo.Utc);

        Assert.Equal(new DateTimeOffset(DateTime.MinValue, TimeSpan.Zero), range.FromInclusive);
    }

    [Fact]
    public void AnInvalidLocalMidnightIsRejected()
    {
        // Spring-forward at 00:00 makes that local midnight nonexistent on every platform.
        var zone = TimeZoneInfo.CreateCustomTimeZone(
            "ODCA/InvalidMidnight",
            TimeSpan.FromHours(-5),
            "ODCA Invalid Midnight",
            "Standard",
            "Daylight",
            [
                TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
                    DateTime.MinValue.Date,
                    DateTime.MaxValue.Date,
                    TimeSpan.FromHours(1),
                    TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1), 3, 8),
                    TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 2, 0, 0), 11, 1))
            ]);

        Assert.Throws<InvalidOperationException>(() => ContractImportsController.CreateUtcRange(
            new DateOnly(2026, 3, 8), null, zone));
    }

    [Fact]
    public void AmbiguousLocalMidnightUsesTheEarlierInstant()
    {
        // Fall-back at 01:00 makes 00:00 occur twice; the inclusive start must pick the earlier offset.
        var zone = TimeZoneInfo.CreateCustomTimeZone(
            "ODCA/AmbiguousMidnight",
            TimeSpan.FromHours(-5),
            "ODCA Ambiguous Midnight",
            "Standard",
            "Daylight",
            [
                TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
                    DateTime.MinValue.Date,
                    DateTime.MaxValue.Date,
                    TimeSpan.FromHours(1),
                    TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 8),
                    TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 1, 0, 0), 11, 1))
            ]);

        var range = ContractImportsController.CreateUtcRange(new DateOnly(2026, 11, 1), null, zone);

        Assert.Equal(new DateTimeOffset(2026, 11, 1, 4, 0, 0, TimeSpan.Zero), range.FromInclusive);
    }

    [Fact]
    public void FilterArgumentsExposeNullableDateTimeOffsetForDapperNotDateOnly()
    {
        var range = ContractImportsController.CreateUtcRange(
            new DateOnly(2026, 9, 16),
            new DateOnly(2026, 9, 16),
            TimeZoneInfo.Utc);
        var args = new { range.FromInclusive, range.ToExclusive };

        Assert.IsType<DateTimeOffset>(args.FromInclusive);
        Assert.IsType<DateTimeOffset>(args.ToExclusive);
        Assert.Equal(typeof(DateTimeOffset?), args.GetType().GetProperty(nameof(args.FromInclusive))!.PropertyType);
        Assert.Equal(typeof(DateTimeOffset?), args.GetType().GetProperty(nameof(args.ToExclusive))!.PropertyType);
    }

    [Fact]
    public async Task DapperBindsDateTimeOffsetLimitsAgainstPostgresTimestamptz()
    {
        var admin = Environment.GetEnvironmentVariable("ODCA_TEST_ADMIN_CONNECTION");
        if (string.IsNullOrWhiteSpace(admin))
            return;

        var range = ContractImportsController.CreateUtcRange(
            new DateOnly(2026, 9, 16),
            new DateOnly(2026, 9, 16),
            TimeZoneInfo.Utc);
        await using var connection = new NpgsqlConnection(admin);
        await connection.OpenAsync();
        var matched = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            """
            SELECT CAST(@FromInclusive AS timestamptz) < CAST(@ToExclusive AS timestamptz)
               AND CAST(@FromInclusive AS timestamptz) = TIMESTAMPTZ '2026-09-16 00:00:00+00'
            """,
            new { range.FromInclusive, range.ToExclusive }));

        Assert.True(matched);
    }
}
