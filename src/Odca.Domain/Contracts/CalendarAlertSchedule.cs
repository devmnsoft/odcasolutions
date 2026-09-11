using System.Globalization;

namespace Odca.Domain.Contracts;

public static class CalendarAlertSchedule
{
    public const int ApproachingMonths = 3;
    public const int DaysBefore30 = 30;
    public const int DaysBefore7 = 7;

    /// <summary>
    /// Adds calendar months with month-end clamping (e.g. 31 Jan + 1 month => 28/29 Feb).
    /// </summary>
    public static DateOnly AddCalendarMonths(DateOnly date, int months)
    {
        var year = date.Year;
        var month = date.Month + months;
        while (month > 12)
        {
            month -= 12;
            year++;
        }

        while (month < 1)
        {
            month += 12;
            year--;
        }

        var day = Math.Min(date.Day, DateTime.DaysInMonth(year, month));
        return new DateOnly(year, month, day);
    }

    public static DateOnly ApproachingWindowStart(DateOnly endDate)
        => AddCalendarMonths(endDate, -ApproachingMonths);

    public static DateOnly DaysBefore(DateOnly endDate, int days)
        => endDate.AddDays(-days);

    public static IReadOnlyList<ContractAlertOccurrence> DueExpiryAlerts(
        Guid contractId,
        int termCycle,
        DateOnly endDate,
        DateOnly today)
    {
        var results = new List<ContractAlertOccurrence>(3);
        MaybeAdd(results, contractId, termCycle, ContractAlertKind.MonthsBefore3, ApproachingWindowStart(endDate), endDate, today);
        MaybeAdd(results, contractId, termCycle, ContractAlertKind.DaysBefore30, DaysBefore(endDate, DaysBefore30), endDate, today);
        MaybeAdd(results, contractId, termCycle, ContractAlertKind.DaysBefore7, DaysBefore(endDate, DaysBefore7), endDate, today);
        return results;
    }

    public static ContractAlertOccurrence? DueRenewalNotice(
        Guid contractId,
        int termCycle,
        DateOnly endDate,
        int? renewalNoticeDays,
        DateOnly today)
    {
        if (renewalNoticeDays is null or < 0)
        {
            return null;
        }

        var anchor = DaysBefore(endDate, renewalNoticeDays.Value);
        if (today < anchor)
        {
            return null;
        }

        return Create(contractId, termCycle, ContractAlertKind.RenewalNotice, anchor, endDate);
    }

    public static string BuildEventKey(Guid contractId, int termCycle, ContractAlertKind kind, DateOnly anchorDate)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"{contractId:D}:{termCycle}:{ContractAlertKindCodes.ToCode(kind)}:{anchorDate:yyyy-MM-dd}");

    private static void MaybeAdd(
        List<ContractAlertOccurrence> results,
        Guid contractId,
        int termCycle,
        ContractAlertKind kind,
        DateOnly anchor,
        DateOnly endDate,
        DateOnly today)
    {
        if (today < anchor)
        {
            return;
        }

        results.Add(Create(contractId, termCycle, kind, anchor, endDate));
    }

    private static ContractAlertOccurrence Create(
        Guid contractId,
        int termCycle,
        ContractAlertKind kind,
        DateOnly anchor,
        DateOnly endDate)
        => new(
            kind,
            anchor,
            endDate,
            BuildEventKey(contractId, termCycle, kind, anchor));
}

public sealed record ContractAlertOccurrence(
    ContractAlertKind Kind,
    DateOnly AnchorDate,
    DateOnly EndDate,
    string EventKey);
