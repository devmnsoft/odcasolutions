namespace Odca.Contracts.Obligations;

public sealed record CreateObligationRequest(string Title,string? Description,string Category,string ObligatedParty,Guid OwnerId,DateOnly DueDate,string Priority,string Origin,decimal? Amount,string? Currency,Guid? ClauseDocumentVersionId,bool EvidenceRequired,string? PostTermReason,MonthlyRecurrenceRequest? Recurrence,int[]? ReminderDays);
public sealed record MonthlyRecurrenceRequest(DateOnly BaseDate,int IntendedDay,DateOnly? EndsOn,int? OccurrenceCount,string TimeZone);
public sealed record ObligationActionRequest(long Version,string? Reason,string? Note,DateTimeOffset? EffectiveAt,Guid? EvidenceDocumentVersionId,Guid? OwnerId,DateOnly? DueDate);
public sealed record ObligationItem(Guid Id,Guid ContractId,string Contract,string Title,string Category,string ObligatedParty,Guid OwnerId,string Owner,DateOnly DueDate,string Priority,string Status,bool Overdue,bool OwnerBlocked,long Version,decimal? Amount,string? Currency,string NextAction);
public sealed record ObligationPage(IReadOnlyList<ObligationItem> Items,int Page,int PageSize,int Total);
public sealed record RenewalDecisionRequest(long ContractVersion,string Decision,string Justification,DateOnly? NewStartDate,DateOnly? NewEndDate);
