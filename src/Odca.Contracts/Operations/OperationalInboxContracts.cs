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
    Guid? ContractId,
    string ContractTitle,
    string Title,
    Guid? OwnerId,
    string? OwnerName,
    DateOnly? DueOn,
    string Urgency,
    string Status,
    string OpenUrl,
    long Version);

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
    Guid? OwnerId = null,
    string? Kind = null);

public sealed record MonthlyAgendaDayDto(
    DateOnly Day,
    IReadOnlyList<OperationalInboxItemDto> Items);

public sealed record MonthlyAgendaPageDto(
    int Year,
    int Month,
    DateOnly From,
    DateOnly To,
    DateOnly Today,
    IReadOnlyList<MonthlyAgendaDayDto> Days,
    int Total);

public sealed record ContractSheetDocumentDto(
    Guid DocumentId,
    Guid VersionId,
    string Name,
    string SafetyState,
    DateTimeOffset UpdatedAt,
    string? SecurityEngine = null);

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
    bool HasImportAwaitingReview = false,
    Guid? DraftId = null,
    DateOnly? Today = null,
    Guid? ImportId = null,
    Guid? OwnerId = null,
    string? Reference = null,
    DateTimeOffset? ArchivedAt = null,
    string? ArchiveReason = null,
    DateTimeOffset? ClosedAt = null,
    DateOnly? ClosedOn = null,
    string? ClosureReason = null,
    ContractSheetSignatureDto? Signature = null,
    IReadOnlyList<ContractSheetSolicitationDto>? Solicitations = null,
    IReadOnlyList<ContractSheetChangeDto>? PendingChanges = null,
    IReadOnlyList<ContractSheetDerivedDocumentDto>? DerivedDocuments = null);

/// <summary>
/// Versão gerada na ficha do contrato (item 12: sequência de documentos derivados).
/// Quando existe alteração com generated_version_id nesta versão, traz origem (kind/razão),
/// quem aplicou e o delta before→after das condições comerciais registradas na aplicação.
/// </summary>
public sealed record ContractSheetDerivedDocumentDto(
    Guid VersionId,
    int VersionNumber,
    DateTimeOffset GeneratedAt,
    string ReviewStatus,
    string? Sha256Prefix,
    string? DerivedFromKind = null,
    DateOnly? EffectiveOn = null,
    string? OriginReason = null,
    DateTimeOffset? AppliedAt = null,
    string? AppliedByName = null,
    DateOnly? BeforeStart = null,
    DateOnly? BeforeEnd = null,
    decimal? BeforeValue = null,
    string? BeforeCurrency = null,
    DateOnly? AfterStart = null,
    DateOnly? AfterEnd = null,
    decimal? AfterValue = null,
    string? AfterCurrency = null);

/// <summary>Solicitação ODCA projetada na ficha do contrato (Bloco B/9).</summary>
public sealed record ContractSheetSolicitationDto(
    Guid Id,
    string Subject,
    string ServiceType,
    string Priority,
    string Status,
    DateOnly? SlaDueOn,
    string StatusLabel = "",
    string ServiceLabel = "");

/// <summary>Participante da preparação de assinatura projetado na ficha do contrato.</summary>
public sealed record ContractSheetSignatureParticipantDto(
    Guid ClientId,
    string Name,
    string Role,
    string ParticipantType,
    DateTimeOffset? SignedAt,
    // v043: modalidade registrada na assinatura (nulo em linhas pré-v043) e vínculo de identidade com o espectador.
    string? SignedThrough = null,
    bool IdentityLinkedToMe = false);

/// <summary>
/// Versão gerada mais recente com preparação de assinatura (B.3.2c).
/// Em preparação confirmada os participantes refletidos são os da revisão confirmada (imutável).
/// </summary>
public sealed record ContractSheetSignatureDto(
    Guid VersionId,
    int VersionNumber,
    Guid PreparationId,
    string PreparationStatus,
    int CompositionRevision,
    int? ConfirmedRevision,
    string ReviewStatus,
    string PdfStatus,
    IReadOnlyList<ContractSheetSignatureParticipantDto> Participants,
    DateTimeOffset? LastRemindedAt = null);

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

/// <summary>Alteração contratual projetada na ficha para separar condições vigentes de propostas (Item 10).</summary>
public sealed record ContractSheetChangeDto(
    Guid Id,
    string Kind,
    string Status,
    string ApplicationStatus,
    DateOnly? EffectiveOn,
    string Reason,
    DateOnly? ProposedStartDate,
    DateOnly? ProposedEndDate,
    decimal? ProposedValue,
    string? Currency);

// Central de Acao (Operations Center)
public sealed record ActionItemDto(
    string SourceType,
    string SourceId,
    string Context,
    string Reason,
    string? Responsible,
    DateOnly? Deadline,
    string NextAction,
    string Priority,
    string Category);

public sealed record ActionCenterResponse(
    IReadOnlyList<ActionItemDto> MyAction,
    IReadOnlyList<ActionItemDto> WaitingOther,
    IReadOnlyList<ActionItemDto> UpcomingDeadlines,
    IReadOnlyList<ActionItemDto> Overdue,
    IReadOnlyList<ActionItemDto> ConflictsAndFailures,
    IReadOnlyList<ActionItemDto> Info,
    int Total,
    DateOnly Today,
    int Page,
    int PageSize);
