namespace Odca.Contracts.Contracts;

public sealed record CounterpartyResponse(
    Guid Id,
    string PersonType,
    string LegalName,
    string DisplayName,
    string DocumentType,
    string? DocumentNormalized,
    string? DocumentDisplay,
    string? Email,
    string? Phone,
    bool IsClient,
    bool IsSupplier,
    bool IsPartner,
    bool IsProvider,
    string Status,
    string? AddressLine1,
    string? AddressLine2,
    string? AddressCity,
    string? AddressState,
    string? AddressPostalCode,
    string? AddressCountry,
    long Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record UpsertCounterpartyRequest(
    string PersonType,
    string LegalName,
    string DisplayName,
    string DocumentType,
    string? Document,
    string? Email,
    string? Phone,
    bool IsClient,
    bool IsSupplier,
    bool IsPartner,
    bool IsProvider,
    string? AddressLine1,
    string? AddressLine2,
    string? AddressCity,
    string? AddressState,
    string? AddressPostalCode,
    string? AddressCountry,
    long? Version);

public sealed record ContractTypeResponse(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    string? Guidance,
    string Status,
    bool IsSystemDemo,
    long Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record UpsertContractTypeRequest(
    string Code,
    string Name,
    string? Description,
    string? Guidance,
    long? Version);

public sealed record SetContractTypeStatusRequest(string Status, long Version);

public sealed record ContractListItemResponse(
    Guid Id,
    string? ReferenceNumber,
    string Title,
    Guid TypeId,
    string TypeName,
    Guid PrimaryCounterpartyId,
    string PrimaryCounterpartyName,
    Guid? OwnerUserId,
    string? OwnerName,
    DateOnly StartDate,
    DateOnly? EndDate,
    bool IsIndefinite,
    string OperationalStatus,
    string TemporalStatus,
    int TermCycle,
    long Version,
    DateTimeOffset UpdatedAt);

public sealed record ContractPartyResponse(Guid CounterpartyId, string CounterpartyName, string Role);

public sealed record ContractRenewalResponse(
    Guid Id,
    DateOnly PreviousEndDate,
    DateOnly NewEndDate,
    int PreviousTermCycle,
    int NewTermCycle,
    string? Reason,
    Guid CreatedBy,
    DateTimeOffset CreatedAt);

public sealed record ContractEventResponse(
    long Id,
    string Action,
    Guid? ActorUserId,
    DateTimeOffset OccurredAt,
    string MetadataJson);

public sealed record ContractDetailResponse(
    Guid Id,
    string? ReferenceNumber,
    string Title,
    string? Summary,
    Guid TypeId,
    string TypeName,
    Guid PrimaryCounterpartyId,
    string PrimaryCounterpartyName,
    Guid? OwnerUserId,
    string? OwnerName,
    DateOnly StartDate,
    DateOnly? EndDate,
    bool IsIndefinite,
    decimal? Amount,
    string? Currency,
    string? AmountPeriodicity,
    int? RenewalNoticeDays,
    string RenewalDecision,
    string OperationalStatus,
    string TemporalStatus,
    int TermCycle,
    long Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ClosedAt,
    DateTimeOffset? CancelledAt,
    IReadOnlyList<ContractPartyResponse> Parties,
    IReadOnlyList<ContractRenewalResponse> Renewals,
    IReadOnlyList<ContractEventResponse> Events);

public sealed record UpsertContractRequest(
    string? ReferenceNumber,
    string Title,
    string? Summary,
    Guid TypeId,
    Guid PrimaryCounterpartyId,
    Guid? OwnerUserId,
    DateOnly StartDate,
    DateOnly? EndDate,
    bool IsIndefinite,
    decimal? Amount,
    string? Currency,
    string? AmountPeriodicity,
    int? RenewalNoticeDays,
    string? RenewalDecision,
    Guid[]? AdditionalCounterpartyIds,
    long? Version);

public sealed record ContractVersionRequest(long Version);

public sealed record RenewContractRequest(DateOnly NewEndDate, string? Reason, long Version);

public sealed record SoftDeleteContractRequest(string Reason, long Version);

public sealed record ContractOverviewResponse(
    int Draft,
    int Active,
    int Approaching,
    int Expired,
    int Indefinite,
    int Closed,
    int Unassigned);

public sealed record UserNotificationResponse(
    Guid Id,
    string Category,
    string Title,
    string Body,
    string ResourceType,
    Guid ResourceId,
    string EventKey,
    string Status,
    bool IsObsolete,
    DateTimeOffset CreatedAt,
    DateOnly? RelevantDate,
    DateTimeOffset? ReadAt);

public sealed record UnreadNotificationsResponse(int Count);
