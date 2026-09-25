namespace Odca.Contracts.Consumption;

public sealed record ConsumptionSummary(
    Guid TenantId, string OrganizationName, string TenantStatus, string SubscriptionStatus,
    string PlanCode, string PlanName, int PlanVersion, DateTimeOffset? PeriodStart, DateTimeOffset? PeriodEnd,
    int ContractedSeats, int ActiveUsers, int ReservedInvitations, int AvailableSeats,
    long ContractedStorageBytes, long AdditionalStorageBytes, long UsedStorageBytes,
    long ReservedStorageBytes, long AvailableStorageBytes, long MaximumFileBytes,
    IReadOnlyList<ResourceBalance> ConsumableCredits, IReadOnlyList<ConsumptionEvent> History);

public sealed record ResourceBalance(string Resource, string Unit, long Contracted, long Additional, long Consumed, long Reserved, long Available);
public sealed record ConsumptionEvent(long Id, string Type, string Resource, long Quantity, string Unit, string? Reason, string ActorName, DateTimeOffset OccurredAt);
public sealed record StoragePackage(Guid Id, string Code, int Version, string Name, long QuantityBytes, decimal? UnitPrice, string? Currency, string Terms);
public sealed record AdditionalStorageRequest(Guid Id, string PackageName, int PackageVersion, int Quantity, long TotalBytes, decimal? TotalPrice, string? Currency, string Terms, string Status, string RequestedBy, DateTimeOffset RequestedAt, string? DecidedBy, DateTimeOffset? DecidedAt, string? DecisionReason);
public sealed record CreateStorageRequest(Guid PackageId, int Quantity, Guid IdempotencyKey);
public sealed record DecideStorageRequest(string Decision, string? Reason);
public sealed record ManualStorageGrant(long QuantityBytes, string Reason, Guid IdempotencyKey, DateTimeOffset? ValidUntil);
public sealed record ChangeOrganizationStatusRequest(string Reason);
public sealed record PlatformCustomer(Guid TenantId, string Name, string MaskedDocument, string PlanName, string TenantStatus, string SubscriptionStatus, int ActiveUsers, long UsedBytes, long LimitBytes, int PendingRequests, DateTimeOffset? LastActivity);
public sealed record PlatformCustomerDetail(ConsumptionSummary Summary, IReadOnlyList<AdditionalStorageRequest> Requests);
