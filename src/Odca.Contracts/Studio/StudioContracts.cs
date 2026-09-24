using System.Text.Json;

namespace Odca.Contracts.Studio;

public sealed record TemplateCatalogItem(Guid Id, string Name, string? Description, string ContractType, string Scope, string Status, int Version, string Author, DateTimeOffset CreatedAt, DateTimeOffset? PublishedAt);
public sealed record TemplateCatalogPage(IReadOnlyList<TemplateCatalogItem> Items, int Page, int PageSize, int Total);
public sealed record CreateDraftRequest(Guid TemplateId, string Title, string? Reference, Guid? PatientId = null);
public sealed record DraftCreatedResponse(Guid Id, Guid ContractId);
public sealed record DraftResponse(Guid Id, Guid ContractId, string Title, Guid SourceTemplateId, Guid SourceTemplateVersionId, JsonElement Content, JsonElement Fields, JsonElement Values, long Version, Guid? LastClientRevision, DateTimeOffset UpdatedAt);
public sealed record SaveDraftRequest(JsonElement Content, JsonElement Fields, JsonElement Values, long ExpectedVersion, Guid ClientRevision);
public sealed record SaveDraftResponse(long Version, Guid ClientRevision, DateTimeOffset SavedAt);
public sealed record GenerateVersionRequest(Guid IdempotencyKey, long ExpectedVersion = 0);
public sealed record ConfirmPatientVersionRequest(long ExpectedDraftVersion, long ExpectedPatientVersion);
public sealed record PatientDataChange(string Field, string? Before, string? After);
public sealed record DocumentPendingItem(string Code, string Message, string Reason, string CorrectionTarget, bool CanCorrect);
public sealed record DocumentConferenceResponse(Guid DraftId, long DraftVersion, string Organization, string Template,
    int TemplateVersion, string DocumentType, string TemplateStatus, Guid? PatientId, string? PatientName,
    string? RepresentativeName, long? SelectedPatientVersion, long? CurrentPatientVersion, bool PatientActive,
    bool CanEdit, bool CanGenerate, bool PatientReconfirmationRequired, string ReviewStatus, string NextAction,
    IReadOnlyList<PatientDataChange> PatientChanges, IReadOnlyList<DocumentPendingItem> PendingItems);
public sealed record GeneratedVersionResponse(Guid Id, int Number, string Sha256, long ByteSize, DateTimeOffset CreatedAt, string Status);
public sealed record GeneratedVersionDetail(Guid Id, Guid ContractId, Guid DraftId, int Number, string Title,
    string DocumentType, string Organization, string Template, int TemplateVersion, string Author,
    DateTimeOffset CreatedAt, string Sha256, string ReviewStatus, string SignatureStatus, Guid? ReviewId,
    JsonElement? PatientSnapshot, JsonElement Content, JsonElement Fields, JsonElement Values,
    string RenderedHtml, string PdfStatus, long? PdfByteSize, DateTimeOffset? PdfCompletedAt,
    SignaturePreparationResponse? SignaturePreparation);
public sealed record SignatureParticipantInput(Guid Id, string ParticipantType, Guid? SourceId, string Role, string Name, string? Email, string? Phone, int Position);
public sealed record SaveSignaturePreparationRequest(long ExpectedVersion, bool Confirm, IReadOnlyList<SignatureParticipantInput> Participants);
public sealed record SignaturePreparationResponse(Guid Id, string Status, long Version, IReadOnlyList<SignatureParticipantInput> Participants);
public sealed record SubmitReviewRequest(Guid GeneratedVersionId, Guid ReviewerId, DateTimeOffset? DueAt, string? Instructions, Guid IdempotencyKey);
public sealed record ReviewSubmittedResponse(Guid ReviewId, Guid GeneratedVersionId, string Status);
public sealed record StudioVersionItem(Guid Id, int Number, string Author, DateTimeOffset CreatedAt, string Status);
public sealed record StudioReviewerItem(Guid Id, string Name);
public sealed record VersionComparisonResponse(StudioVersionItem Before, StudioVersionItem After, IReadOnlyList<StudioChangeItem> Changes);
public sealed record StudioChangeItem(string Category, string Reference, string? Before, string? After);
public sealed record ChecklistResponse(bool CanSubmit, IReadOnlyList<ChecklistItem> Items);
public sealed record ChecklistItem(string Severity, string Code, string Message, string? Reference);
public sealed record CreateStudioCommentRequest(Guid? VersionId, long DraftRevision, string Reference, string Body, Guid? ParentId);
public sealed record ChangeStudioCommentStateRequest(bool ExpectedResolved, string? Observation = null);
public sealed record StudioCommentItem(Guid Id, Guid? VersionId, long DraftRevision, string Reference, string Body, Guid AuthorId, string Author,
    Guid? ParentId, DateTimeOffset CreatedAt, bool Resolved, DateTimeOffset? ResolvedAt, bool ReferenceLocated,
    string? ResolvedBy = null, DateTimeOffset? LastMovementAt = null, int? OriginVersion = null);

public sealed record CreateTemplateRequest(string Name, string? Description, string ContractType, string Scope, JsonElement Content, JsonElement Fields);
public sealed record UpdateTemplateRequest(string Name, string? Description, string ContractType, JsonElement Content, JsonElement Fields, long ExpectedVersion);
public sealed record TemplateMutationResponse(Guid Id, int Version, long RowVersion, string Status);
public sealed record TemplatePreview(Guid Id, string Name, string? Description, string ContractType, string Scope, string Status, int Version, JsonElement Fields, IReadOnlyList<string> FieldLabels);
public sealed record OfficialTemplateInstallResponse(int Installed, int AlreadyPresent, IReadOnlyList<string> Names);
