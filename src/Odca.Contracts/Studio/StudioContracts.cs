using System.Text.Json;

namespace Odca.Contracts.Studio;

public sealed record TemplateCatalogItem(Guid Id, string Name, string? Description, string ContractType, string Scope, string Status, int Version, string Author, DateTimeOffset CreatedAt, DateTimeOffset? PublishedAt);
public sealed record TemplateCatalogPage(IReadOnlyList<TemplateCatalogItem> Items, int Page, int PageSize, int Total);
public sealed record CreateDraftRequest(Guid TemplateId, string Title, string? Reference);
public sealed record DraftResponse(Guid Id, Guid ContractId, string Title, Guid SourceTemplateId, Guid SourceTemplateVersionId, JsonElement Content, JsonElement Fields, JsonElement Values, long Version, Guid? LastClientRevision, DateTimeOffset UpdatedAt);
public sealed record SaveDraftRequest(JsonElement Content, JsonElement Fields, JsonElement Values, long ExpectedVersion, Guid ClientRevision);
public sealed record SaveDraftResponse(long Version, Guid ClientRevision, DateTimeOffset SavedAt);
public sealed record GenerateVersionRequest(Guid IdempotencyKey);
public sealed record GeneratedVersionResponse(Guid Id, int Number, string Sha256, long ByteSize, DateTimeOffset CreatedAt, string Status);
public sealed record SubmitReviewRequest(Guid GeneratedVersionId, Guid ReviewerId, DateTimeOffset? DueAt, string? Instructions, Guid IdempotencyKey);
public sealed record ReviewSubmittedResponse(Guid ReviewId, Guid GeneratedVersionId, string Status);

public sealed record CreateTemplateRequest(string Name, string? Description, string ContractType, string Scope, JsonElement Content, JsonElement Fields);
public sealed record UpdateTemplateRequest(string Name, string? Description, string ContractType, JsonElement Content, JsonElement Fields, long ExpectedVersion);
public sealed record TemplateMutationResponse(Guid Id, int Version, long RowVersion, string Status);
