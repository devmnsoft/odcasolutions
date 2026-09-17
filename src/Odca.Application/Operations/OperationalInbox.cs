namespace Odca.Application.Operations;

public enum OperationalWorkKind
{
    Review,
    Obligation,
    Renewal
}

public enum OperationalUrgency
{
    Overdue,
    DueToday,
    DueThisWeek,
    Upcoming,
    Unscheduled
}

public sealed record OperationalWorkItem(
    OperationalWorkKind Kind,
    Guid TenantId,
    Guid SourceId,
    Guid? OwnerId,
    DateOnly? DueOn);

/// <summary>
/// Read projection rules for the operational inbox. Does not persist a task table.
/// Visibility follows the same authorization as the source lists: tenant readers
/// see the tenant set; own-only readers see only items they own.
/// </summary>
public static class OperationalInbox
{
    public static OperationalUrgency Classify(DateOnly? dueOn, DateOnly today)
    {
        if (dueOn is null) return OperationalUrgency.Unscheduled;
        if (dueOn.Value < today) return OperationalUrgency.Overdue;
        if (dueOn.Value == today) return OperationalUrgency.DueToday;
        if (dueOn.Value <= today.AddDays(7)) return OperationalUrgency.DueThisWeek;
        return OperationalUrgency.Upcoming;
    }

    public static bool IsVisibleTo(OperationalWorkItem item, Guid viewerId, bool canReadTenant)
    {
        if (item.TenantId == Guid.Empty || item.SourceId == Guid.Empty || viewerId == Guid.Empty)
            return false;
        if (canReadTenant) return true;
        return item.OwnerId == viewerId;
    }

    public static IReadOnlyList<OperationalWorkItem> Rank(
        IEnumerable<OperationalWorkItem> items,
        DateOnly today,
        Guid viewerId,
        bool canReadTenant)
    {
        ArgumentNullException.ThrowIfNull(items);
        return items
            .Where(item => IsVisibleTo(item, viewerId, canReadTenant))
            .OrderBy(item => (int)Classify(item.DueOn, today))
            .ThenBy(item => item.DueOn ?? DateOnly.MaxValue)
            .ThenBy(item => (int)item.Kind)
            .ThenBy(item => item.SourceId)
            .ToArray();
    }
}

/// <summary>
/// Visible month used by the obligation agenda. Callers must query only this
/// inclusive window; they must not load the whole catalog into the browser.
/// </summary>
public static class MonthlyAgendaWindow
{
    public static (DateOnly From, DateOnly To) ForMonth(int year, int month)
    {
        if (year is < 1 or > 9999) throw new ArgumentOutOfRangeException(nameof(year));
        if (month is < 1 or > 12) throw new ArgumentOutOfRangeException(nameof(month));
        var from = new DateOnly(year, month, 1);
        var to = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        return (from, to);
    }

    public static (DateOnly From, DateOnly To) Containing(DateOnly anchor) =>
        ForMonth(anchor.Year, anchor.Month);

    public static bool Includes(DateOnly dueOn, DateOnly from, DateOnly to)
    {
        EnsureInclusiveRange(from, to);
        return dueOn >= from && dueOn <= to;
    }

    public static void EnsureInclusiveRange(DateOnly from, DateOnly to)
    {
        if (from > to)
            throw new ArgumentException("O intervalo da agenda não pode ter início depois do término.", nameof(from));
    }
}
