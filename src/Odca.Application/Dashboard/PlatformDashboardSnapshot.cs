namespace Odca.Application.Dashboard;

public sealed record PlatformDashboardSnapshot(
    int TotalTenants,
    int ActiveTenants,
    int BlockedTenants,
    int InactiveTenants,
    int ActiveUsers,
    int Contracts,
    int ContractsExpiring,
    int OpenObligations,
    int OverdueObligations,
    int UpcomingRenewals,
    int PendingInvoices,
    int OverdueInvoices,
    long StorageBytes,
    int PendingPrivacyItems,
    IReadOnlyList<PlatformAuditEvent> RecentAuditEvents);

public sealed record PlatformAuditEvent(
    DateTimeOffset OccurredAt,
    string Action,
    string EntityType,
    string Result,
    string? ActorName,
    string? TenantName);
