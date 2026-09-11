namespace Odca.Domain.Contracts;

public enum ContractOperationalStatus
{
    Draft = 0,
    Active = 1,
    Closed = 2,
    Cancelled = 3,
    Inactive = 4
}

public enum ContractTemporalStatus
{
    Upcoming = 0,
    Active = 1,
    ApproachingEnd = 2,
    Expired = 3,
    Indefinite = 4
}

public enum ContractAlertKind
{
    MonthsBefore3 = 0,
    DaysBefore30 = 1,
    DaysBefore7 = 2,
    RenewalNotice = 3
}

public static class ContractOperationalStatusParser
{
    public static bool TryParse(string? value, out ContractOperationalStatus status)
    {
        status = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        switch (value.Trim().ToLowerInvariant())
        {
            case "draft":
                status = ContractOperationalStatus.Draft;
                return true;
            case "active":
                status = ContractOperationalStatus.Active;
                return true;
            case "closed":
                status = ContractOperationalStatus.Closed;
                return true;
            case "cancelled":
                status = ContractOperationalStatus.Cancelled;
                return true;
            case "inactive":
                status = ContractOperationalStatus.Inactive;
                return true;
            default:
                return false;
        }
    }

    public static string ToStorage(ContractOperationalStatus status)
        => status switch
        {
            ContractOperationalStatus.Draft => "draft",
            ContractOperationalStatus.Active => "active",
            ContractOperationalStatus.Closed => "closed",
            ContractOperationalStatus.Cancelled => "cancelled",
            ContractOperationalStatus.Inactive => "inactive",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
        };
}

public static class ContractAlertKindCodes
{
    public const string MonthsBefore3 = "m3";
    public const string DaysBefore30 = "d30";
    public const string DaysBefore7 = "d7";
    public const string RenewalNotice = "renewal_notice";

    public static string ToCode(ContractAlertKind kind)
        => kind switch
        {
            ContractAlertKind.MonthsBefore3 => MonthsBefore3,
            ContractAlertKind.DaysBefore30 => DaysBefore30,
            ContractAlertKind.DaysBefore7 => DaysBefore7,
            ContractAlertKind.RenewalNotice => RenewalNotice,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };
}
