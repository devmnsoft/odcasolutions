namespace Odca.Contracts.Renewals;

public sealed record RenewalSummary(int Expiring, int Expired, int Preparing, int InReview, int Scheduled, int NotRenewing);
public sealed record RenewalListItem(Guid ContractId, Guid? RequestId, string Name, string? Counterparty, string? OwnerName,
    DateOnly? EndDate, DateOnly? DecisionDueOn, string Status, string NextAction, int? DaysRemaining, long ContractVersion);
public sealed record RenewalPage(IReadOnlyList<RenewalListItem> Items, RenewalSummary Summary, int Page, int PageSize, int Total, DateOnly? Today = null);
public sealed record RenewalFilterOptions(IReadOnlyList<RenewalOption> Owners, IReadOnlyList<RenewalOption> ContractTypes, IReadOnlyList<RenewalOption> Counterparties);
public sealed record RenewalOption(string Value, string Label);
public sealed record CreateRenewalRequest(Guid IdempotencyKey, Guid ResponsibleId, string Kind, string Reason,
    DateOnly EffectiveOn, DateOnly? ProposedStartDate, DateOnly? ProposedEndDate, decimal? ProposedValue,
    string? Currency, string? ProposedScope, Guid? SourceDocumentVersionId, long ContractVersion);
public sealed record RenewalRequestDetails(Guid Id, Guid ContractId, string ContractName, string Kind, string Status,
    string ApplicationStatus, string Reason, DateOnly? CurrentStartDate, DateOnly? CurrentEndDate,
    DateOnly? ProposedStartDate, DateOnly? ProposedEndDate, decimal? CurrentValue, decimal? ProposedValue,
    string? Currency, DateOnly EffectiveOn, long BaseContractVersion, long RowVersion, Guid? EvidenceVersionId,
    DateTimeOffset? FormalizedAt, string? FormalizationJustification);
public sealed record FormalizeRenewalRequest(Guid EvidenceVersionId, DateOnly FormalizedOn, string Justification, long RowVersion);
public sealed record ApplyRenewalRequest(long RowVersion);
