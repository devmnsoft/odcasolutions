namespace Odca.Contracts.Renewals;

public sealed record RenewalSummary(int Expiring, int Expired, int Preparing, int InReview, int Scheduled, int NotRenewing, int Indeterminate = 0);
public sealed record RenewalListItem(Guid ContractId, Guid? RequestId, string Name, string? Counterparty, string? OwnerName,
    DateOnly? EndDate, DateOnly? DecisionDueOn, string Status, string NextAction, int? DaysRemaining, long ContractVersion, string? Priority = null, string? Kind = null);
public sealed record RenewalPage(IReadOnlyList<RenewalListItem> Items, RenewalSummary Summary, int Page, int PageSize, int Total, DateOnly? Today = null);
public sealed record RenewalFilterOptions(IReadOnlyList<RenewalOption> Owners, IReadOnlyList<RenewalOption> ContractTypes, IReadOnlyList<RenewalOption> Counterparties);
public sealed record RenewalOption(string Value, string Label);
public sealed record CreateRenewalRequest(Guid IdempotencyKey, Guid ResponsibleId, string Kind, string Reason,
    DateOnly EffectiveOn, DateOnly? ProposedStartDate, DateOnly? ProposedEndDate, decimal? ProposedValue,
    string? Currency, string? ProposedScope, Guid? SourceDocumentVersionId, long ContractVersion,
    Guid? DraftId = null, string? ValueChangeMode = null, string? Priority = null);
public sealed record RenewalRequestDetails(Guid Id, Guid ContractId, string ContractName, string Kind, string Status,
    string ApplicationStatus, string Reason, string? Priority, Guid? ResponsibleId, string? ResponsibleName,
    DateOnly? CurrentStartDate, DateOnly? CurrentEndDate, decimal? CurrentValue,
    DateOnly? ProposedStartDate, DateOnly? ProposedEndDate, decimal? ProposedValue,
    string? Currency, DateOnly EffectiveOn, long BaseContractVersion, long RowVersion, Guid? EvidenceVersionId,
    DateTimeOffset? FormalizedAt, string? FormalizationJustification,
    IReadOnlyList<RenewalChangeEvent> Events, IReadOnlyList<RenewalEvidenceOption> EvidenceCandidates);
public sealed record RenewalChangeEvent(string EventType, DateTimeOffset OccurredAt, string? ActorName);
public sealed record RenewalEvidenceOption(Guid VersionId, int VersionNumber, string DisplayName, string SecurityStatus, DateTimeOffset UploadedAt);
public sealed record FormalizeRenewalRequest(Guid EvidenceVersionId, DateOnly FormalizedOn, string Justification, long RowVersion);
public sealed record ApplyRenewalRequest(long RowVersion);
public sealed record ApplyRenewalResponse(bool Applied, long ContractVersion, bool ObligationsPreserved, DateOnly? EndsOn);
public sealed record SubmitRenewalRequest(long RowVersion);
public sealed record CancelRenewalRequest(long RowVersion, string? Reason = null);
public sealed record RenewalReminderConfig(int[] Days);
