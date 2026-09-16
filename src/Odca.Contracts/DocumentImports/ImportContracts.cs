namespace Odca.Contracts.DocumentImports;

public sealed record ContractImportListItem(Guid Id, string DocumentName, string Requester, DateTimeOffset CreatedAt,
    string Status, string CurrentStep, string? DiagnosticCode, Guid? ResultContractId, long ReviewVersion);
public sealed record ContractImportPage(IReadOnlyList<ContractImportListItem> Items, int Total);
public sealed record CreateContractImport(Guid DocumentVersionId, Guid? ExtractionJobId);
public sealed record ConfirmContractImport(long ReviewVersion, Guid IdempotencyKey);
public sealed record ContractImportResult(Guid ImportId, Guid ContractId, string Status, bool Repeated);
public sealed record ImportReviewDecision(Guid SuggestionId, string Status, string? Value);
public sealed record SaveImportReview(Guid IdempotencyKey, long ContractVersion, IReadOnlyList<ImportReviewDecision> Decisions);

public enum ImportIssueSeverity { Information, Warning, Blocking }
public sealed record ImportValidationIssue(string Code, ImportIssueSeverity Severity, string Message, string? Field = null);
