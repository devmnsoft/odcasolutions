namespace Odca.Contracts.Contracts;

/// <summary>
/// Contrato na lista consolidada e no detalhe (GET contracts, GET contracts/{id}).
/// Espelha o payload camelCase do Odca.Api.
/// </summary>
public sealed record ContractListItem(
    Guid Id,
    string Title,
    string? Reference,
    string? ContractType,
    string? Counterparty,
    DateOnly? StartDate,
    DateOnly? EndDate,
    decimal? Value,
    string? Currency,
    int? RenewalNoticeDays,
    Guid? OwnerId,
    string? Owner,
    long Version,
    int? LatestVersionNumber,
    string? LatestReviewStatus,
    DateTime? ArchivedAt,
    string? ArchiveReason,
    DateTime? ClosedAt,
    DateOnly? ClosedOn,
    string? ClosureReason,
    DateTime UpdatedAt);

public sealed record ContractPage(
    IReadOnlyList<ContractListItem> Items,
    int Page,
    int PageSize,
    int Total);

/// <summary>Evento do histórico contratual (contract_events).</summary>
public sealed record ContractEventItem(
    long Id,
    string Type,
    DateTime OccurredAt,
    Guid? ActorId,
    string? Actor,
    string? Details);

public sealed record ContractEventPage(
    IReadOnlyList<ContractEventItem> Items,
    int Page,
    int PageSize,
    int Total);

public sealed record ContractUpdateRequest(
    string? Title,
    string? Reference,
    Guid? OwnerId,
    bool ClearOwner);

public sealed record ContractArchiveRequest(string? Reason);

public sealed record ContractCloseRequest(DateOnly ClosedOn, string Reason);

/// <summary>Resposta de arquivar/restaurar/encerrar (idempotente via replayed).</summary>
public sealed record ContractLifecycleResult(bool Replayed, long Version);

public sealed record DuplicateContractResult(Guid ContractId, Guid DraftId);
