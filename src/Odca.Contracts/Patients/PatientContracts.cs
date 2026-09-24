namespace Odca.Contracts.Patients;

public sealed record PatientIdentifierInput(string Type, string Value);
public sealed record PatientRepresentativeInput(string FullName, string? IdentifierType, string? IdentifierValue, string Relationship);
public sealed record SavePatientRequest(string FullName, string? PreferredName, DateOnly? BirthDate, string? Email, string? Phone,
    string? Address, PatientIdentifierInput? Identifier, PatientRepresentativeInput? Representative, long ExpectedVersion = 0);
public sealed record PatientSummary(Guid Id, string FullName, string? PreferredName, string? MaskedIdentifier, bool Active, long Version, DateTimeOffset UpdatedAt);
public sealed record PatientPage(IReadOnlyList<PatientSummary> Items, int Page, int PageSize, int Total);
public sealed record PatientDetails(Guid Id, string FullName, string? PreferredName, DateOnly? BirthDate, string? Email, string? Phone,
    string? Address, string? IdentifierType, string? IdentifierValue, PatientRepresentativeInput? Representative, bool Active, long Version, DateTimeOffset UpdatedAt);
public sealed record PatientDocument(Guid Id, Guid ContractId, string Title, string Type, int Version, string Author, DateTimeOffset CreatedAt,
    string DocumentStatus, string ReviewStatus, string SignatureStatus, string NextAction, Guid DraftId, Guid? ReviewId);
public sealed record PatientArchivePage(IReadOnlyList<PatientDocument> Items, int Page, int PageSize, int Total);
