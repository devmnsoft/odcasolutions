namespace Odca.Domain.Contracts;

public static class ContractLifecycle
{
    public static ContractTemporalStatus DeriveTemporalStatus(
        ContractOperationalStatus operationalStatus,
        DateOnly startDate,
        DateOnly? endDate,
        bool isIndefinite,
        DateOnly referenceDate)
    {
        if (operationalStatus is ContractOperationalStatus.Closed
            or ContractOperationalStatus.Cancelled
            or ContractOperationalStatus.Inactive
            or ContractOperationalStatus.Draft)
        {
            // Temporal view still describes the configured term relative to the reference date.
        }

        if (isIndefinite)
        {
            return startDate > referenceDate
                ? ContractTemporalStatus.Upcoming
                : ContractTemporalStatus.Indefinite;
        }

        if (endDate is null)
        {
            throw new ArgumentException("Contratos com prazo determinado exigem data final.", nameof(endDate));
        }

        if (startDate > referenceDate)
        {
            return ContractTemporalStatus.Upcoming;
        }

        if (endDate.Value < referenceDate)
        {
            return ContractTemporalStatus.Expired;
        }

        var approachingFrom = CalendarAlertSchedule.ApproachingWindowStart(endDate.Value);
        if (referenceDate >= approachingFrom)
        {
            return ContractTemporalStatus.ApproachingEnd;
        }

        return ContractTemporalStatus.Active;
    }

    public static bool CanTransition(ContractOperationalStatus from, ContractOperationalStatus to)
        => (from, to) switch
        {
            (ContractOperationalStatus.Draft, ContractOperationalStatus.Active) => true,
            (ContractOperationalStatus.Draft, ContractOperationalStatus.Cancelled) => true,
            (ContractOperationalStatus.Active, ContractOperationalStatus.Closed) => true,
            (ContractOperationalStatus.Active, ContractOperationalStatus.Cancelled) => true,
            (ContractOperationalStatus.Active, ContractOperationalStatus.Inactive) => true,
            (ContractOperationalStatus.Inactive, ContractOperationalStatus.Active) => true,
            _ => false
        };

    public static bool CanEditContent(ContractOperationalStatus status)
        => status is ContractOperationalStatus.Draft or ContractOperationalStatus.Active;

    public static bool CanRenew(ContractOperationalStatus status, bool isIndefinite)
        => status == ContractOperationalStatus.Active && !isIndefinite;

    public static bool IsMonitoredForAlerts(ContractOperationalStatus status)
        => status == ContractOperationalStatus.Active;
}
