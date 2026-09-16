using Odca.Api.Controllers;

namespace Odca.IntegrationTests;

public sealed class ContractImportDateRangeTests
{
    [Fact]
    public void NoDatesProduceNoTimestampLimits()
    {
        var range = ContractImportsController.CreateUtcRange(null, null, "UTC");

        Assert.Null(range.FromInclusive);
        Assert.Null(range.ToExclusive);
    }

    [Fact]
    public void ASingleDayUsesInclusiveStartAndExclusiveFollowingStart()
    {
        var range = ContractImportsController.CreateUtcRange(
            new DateOnly(2026, 9, 16),
            new DateOnly(2026, 9, 16),
            "UTC");

        Assert.Equal(new DateTimeOffset(2026, 9, 16, 0, 0, 0, TimeSpan.Zero), range.FromInclusive);
        Assert.Equal(new DateTimeOffset(2026, 9, 17, 0, 0, 0, TimeSpan.Zero), range.ToExclusive);
    }

    [Fact]
    public void OrganizationTimeZoneControlsBothLimitsAcrossDaylightSavingChange()
    {
        var range = ContractImportsController.CreateUtcRange(
            new DateOnly(2026, 3, 8),
            new DateOnly(2026, 3, 8),
            "America/New_York");

        Assert.Equal(new DateTimeOffset(2026, 3, 8, 5, 0, 0, TimeSpan.Zero), range.FromInclusive);
        Assert.Equal(new DateTimeOffset(2026, 3, 9, 4, 0, 0, TimeSpan.Zero), range.ToExclusive);
    }

    [Fact]
    public void MaximumFinalDateDoesNotOverflow()
    {
        var range = ContractImportsController.CreateUtcRange(null, DateOnly.MaxValue, "UTC");

        Assert.Null(range.FromInclusive);
        Assert.Null(range.ToExclusive);
    }
}
