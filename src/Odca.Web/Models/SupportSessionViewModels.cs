using Odca.Contracts.Consumption;
using Odca.Contracts.Support;

namespace Odca.Web.Models;

/// <summary>D-OC39 (Sprint 5/F-2): listagem de sessões de suporte + seletor de organização.</summary>
public sealed class SupportSessionsViewModel
{
    public required SupportSessionResponse[] Sessions { get; init; }
    public required PlatformCustomer[] Customers { get; init; }
    public bool SessionsLoadFailed { get; init; }
    public bool CustomersLoadFailed { get; init; }
}

public sealed class SupportSessionCreateViewModel
{
    public Guid TenantId { get; set; }
    public string Reason { get; set; } = "";
    public int DurationMinutes { get; set; } = 60;
}

public sealed class SupportSessionExtendViewModel
{
    public Guid SessionId { get; set; }
    public int Minutes { get; set; } = 30;
}
