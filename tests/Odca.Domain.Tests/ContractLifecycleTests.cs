using System.Globalization;
using Odca.Domain.Contracts;

namespace Odca.Domain.Tests;

public sealed class ContractLifecycleTests
{
    [Theory]
    [InlineData("2026-01-01", "2026-12-31", false, "2025-12-01", ContractTemporalStatus.Upcoming)]
    [InlineData("2026-01-01", "2026-12-31", false, "2026-06-01", ContractTemporalStatus.Active)]
    [InlineData("2026-01-01", "2026-12-31", false, "2026-10-01", ContractTemporalStatus.ApproachingEnd)]
    [InlineData("2026-01-01", "2026-12-31", false, "2027-01-01", ContractTemporalStatus.Expired)]
    [InlineData("2026-01-01", null, true, "2026-06-01", ContractTemporalStatus.Indefinite)]
    [InlineData("2026-06-01", null, true, "2026-01-01", ContractTemporalStatus.Upcoming)]
    public void DeriveTemporalStatusUsesReferenceDate(
        string start,
        string? end,
        bool indefinite,
        string reference,
        ContractTemporalStatus expected)
    {
        var status = ContractLifecycle.DeriveTemporalStatus(
            ContractOperationalStatus.Active,
            DateOnly.Parse(start, CultureInfo.InvariantCulture),
            end is null ? null : DateOnly.Parse(end, CultureInfo.InvariantCulture),
            indefinite,
            DateOnly.Parse(reference, CultureInfo.InvariantCulture));

        Assert.Equal(expected, status);
    }

    [Fact]
    public void TransitionRulesAllowLifecycleActions()
    {
        Assert.True(ContractLifecycle.CanTransition(ContractOperationalStatus.Draft, ContractOperationalStatus.Active));
        Assert.True(ContractLifecycle.CanTransition(ContractOperationalStatus.Active, ContractOperationalStatus.Closed));
        Assert.True(ContractLifecycle.CanRenew(ContractOperationalStatus.Active, isIndefinite: false));
        Assert.False(ContractLifecycle.CanRenew(ContractOperationalStatus.Active, isIndefinite: true));
        Assert.False(ContractLifecycle.CanTransition(ContractOperationalStatus.Closed, ContractOperationalStatus.Active));
        Assert.True(ContractLifecycle.IsMonitoredForAlerts(ContractOperationalStatus.Active));
        Assert.False(ContractLifecycle.IsMonitoredForAlerts(ContractOperationalStatus.Draft));
    }
}

public sealed class CalendarAlertScheduleTests
{
    [Theory]
    [InlineData("2024-05-31", -3, "2024-02-29")]
    [InlineData("2025-05-31", -3, "2025-02-28")]
    [InlineData("2026-03-31", -1, "2026-02-28")]
    [InlineData("2024-01-31", 1, "2024-02-29")]
    [InlineData("2026-01-15", -3, "2025-10-15")]
    public void AddCalendarMonthsClampsMonthEnds(string start, int months, string expected)
    {
        var actual = CalendarAlertSchedule.AddCalendarMonths(DateOnly.Parse(start, CultureInfo.InvariantCulture), months);
        Assert.Equal(DateOnly.Parse(expected, CultureInfo.InvariantCulture), actual);
    }

    [Fact]
    public void DueExpiryAlertsAreIdempotentByEventKey()
    {
        var contractId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var end = DateOnly.Parse("2026-12-31", CultureInfo.InvariantCulture);
        var today = DateOnly.Parse("2026-12-25", CultureInfo.InvariantCulture);
        var due = CalendarAlertSchedule.DueExpiryAlerts(contractId, termCycle: 2, end, today);

        Assert.Equal(3, due.Count);
        Assert.Contains(due, x => x.Kind == ContractAlertKind.MonthsBefore3);
        Assert.Contains(due, x => x.Kind == ContractAlertKind.DaysBefore30);
        Assert.Contains(due, x => x.Kind == ContractAlertKind.DaysBefore7);
        Assert.All(due, x => Assert.Contains(":2:", x.EventKey, StringComparison.Ordinal));
        Assert.Equal(
            CalendarAlertSchedule.BuildEventKey(contractId, 2, ContractAlertKind.DaysBefore7, end.AddDays(-7)),
            due.Single(x => x.Kind == ContractAlertKind.DaysBefore7).EventKey);
    }

    [Fact]
    public void RenewalNoticeIsSeparateFromExpiryLadder()
    {
        var contractId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var end = DateOnly.Parse("2026-06-30", CultureInfo.InvariantCulture);
        var notice = CalendarAlertSchedule.DueRenewalNotice(contractId, 1, end, renewalNoticeDays: 45, today: DateOnly.Parse("2026-05-20", CultureInfo.InvariantCulture));
        Assert.NotNull(notice);
        Assert.Equal(ContractAlertKind.RenewalNotice, notice!.Kind);
        Assert.Contains(":renewal_notice:", notice.EventKey, StringComparison.Ordinal);

        var early = CalendarAlertSchedule.DueRenewalNotice(contractId, 1, end, 45, DateOnly.Parse("2026-04-01", CultureInfo.InvariantCulture));
        Assert.Null(early);
    }
}
