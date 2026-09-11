namespace Odca.Application.Contracts;

public sealed record CounterpartyRecord(
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

public sealed record CounterpartyWriteModel(
    string PersonType,
    string LegalName,
    string DisplayName,
    string DocumentType,
    string? DocumentRaw,
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
    string? DocumentNormalized = null,
    string? DocumentDisplay = null);

public sealed record ContractTypeRecord(
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

public sealed record ContractTypeWriteModel(
    string Code,
    string Name,
    string? Description,
    string? Guidance);

public sealed record ContractListItem(
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

public sealed record ContractPartyRecord(Guid CounterpartyId, string CounterpartyName, string Role);

public sealed record ContractRenewalRecord(
    Guid Id,
    DateOnly PreviousEndDate,
    DateOnly NewEndDate,
    int PreviousTermCycle,
    int NewTermCycle,
    string? Reason,
    Guid CreatedBy,
    DateTimeOffset CreatedAt);

public sealed record ContractEventRecord(
    long Id,
    string Action,
    Guid? ActorUserId,
    DateTimeOffset OccurredAt,
    string MetadataJson);

public sealed record ContractDetail(
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
    IReadOnlyList<ContractPartyRecord> Parties,
    IReadOnlyList<ContractRenewalRecord> Renewals,
    IReadOnlyList<ContractEventRecord> Events);

public sealed record ContractWriteModel(
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
    IReadOnlyList<Guid>? AdditionalCounterpartyIds);

public sealed record ContractListFilter(
    string? Title,
    Guid? CounterpartyId,
    Guid? TypeId,
    Guid? OwnerUserId,
    string? OperationalStatus,
    string? TemporalStatus,
    DateOnly? EndFrom,
    DateOnly? EndTo,
    bool Mine,
    bool Approaching,
    bool Unassigned,
    DateOnly ReferenceDate);

public sealed record ContractOverviewMetrics(
    int Draft,
    int Active,
    int Approaching,
    int Expired,
    int Indefinite,
    int Closed,
    int Unassigned);

public sealed record UserNotificationRecord(
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

public enum MutationStatus
{
    Succeeded,
    Forbidden,
    NotFound,
    Conflict,
    ValidationFailed
}

public sealed record MutationResult(MutationStatus Status, string? ErrorCode = null, Guid? Id = null);

public sealed record MutationResult<T>(MutationStatus Status, T? Value = default, string? ErrorCode = null);
