namespace Odca.Contracts.Operations;

public sealed record OperationalInboxQuery(
    string Scope = "mine",
    string? Kind = null,
    string? Urgency = null,
    Guid? ContractId = null,
    Guid? OwnerId = null,
    int Page = 1,
    int PageSize = 20);

public sealed record OperationalInboxItemDto(
    string Kind,
    Guid SourceId,
    Guid ContractId,
    string ContractTitle,
    string Title,
    Guid? OwnerId,
    string? OwnerName,
    DateOnly? DueOn,
    string Urgency,
    string Status,
    string OpenUrl);

public sealed record OperationalInboxPageDto(
    IReadOnlyList<OperationalInboxItemDto> Items,
    int Page,
    int PageSize,
    int Total,
    int Overdue,
    int DueToday,
    int DueThisWeek);

public sealed record MonthlyAgendaQuery(
    int Year,
    int Month,
    string Scope = "mine",
    Guid? OwnerId = null);

public sealed record MonthlyAgendaDayDto(
    DateOnly Day,
    IReadOnlyList<OperationalInboxItemDto> Items);

public sealed record MonthlyAgendaPageDto(
    int Year,
    int Month,
    DateOnly From,
    DateOnly To,
    IReadOnlyList<MonthlyAgendaDayDto> Days,
    int Total);

public sealed record ContractSheetDocumentDto(
    Guid DocumentId,
    Guid VersionId,
    string Name,
    string SafetyState,
    DateTimeOffset UpdatedAt);

public sealed record ContractSheetReviewDto(
    Guid ReviewId,
    string Status,
    Guid? CurrentReviewerId,
    string? CurrentReviewerName,
    DateTimeOffset? DueAt);

public sealed record ContractSheetRenewalDto(
    DateOnly? EndsOn,
    DateOnly? NoticeDueOn,
    bool InThreeMonthWindow,
    string? CommercialState);

public sealed record OfficialTemplateRecommendationDto(
    string Key,
    string Name,
    string ContractType,
    string Reason);

public sealed record ContractSheetDto(
    Guid ContractId,
    string Title,
    string Status,
    DateOnly? StartsOn,
    DateOnly? EndsOn,
    IReadOnlyList<ContractSheetDocumentDto> Documents,
    ContractSheetReviewDto? CurrentReview,
    IReadOnlyList<OperationalInboxItemDto> OpenObligations,
    ContractSheetRenewalDto? Renewal,
    long Version,
    string? ContractType = null,
    string? OwnerName = null,
    IReadOnlyList<OfficialTemplateRecommendationDto>? RecommendedTemplates = null,
    bool CanInstallOfficialLibrary = false,
    bool HasImportAwaitingReview = false);

public sealed record StartOfficialDraftRequest(string OfficialKey, long SheetVersion);

public sealed record StartOfficialDraftResponse(
    Guid DraftId,
    string OfficialKey,
    string RedirectPath);

public sealed record CreateSheetRenewalProposalRequest(
    string Kind,
    DateOnly? ProposedStartDate,
    DateOnly? ProposedEndDate,
    DateOnly EffectiveOn,
    string Reason,
    long ContractVersion,
    Guid? ResponsibleId,
    decimal? ProposedValue,
    string? Currency);
