using Odca.Application.Plans;

namespace Odca.Application.Onboarding;

public sealed record CustomerRegistrationSubmission(
    string PlanCode,
    string Document,
    string ResponsibleName,
    string Email,
    string Password,
    bool AcceptedTerms,
    bool AcknowledgedPrivacyNotice,
    bool MarketingConsent);

public sealed record CustomerRegistrationResult(
    bool Succeeded,
    IReadOnlyList<string> Errors,
    Guid? RegistrationId,
    string? DevelopmentConfirmationToken);

public sealed record CustomerConfirmationResult(bool Succeeded, IReadOnlyList<string> Errors);

public sealed record CustomerHome(
    Guid TenantId,
    string OrganizationName,
    string TenantStatus,
    string CommercialState,
    PlanCatalogItem Plan);

public sealed record CustomerRegistrationDraft(
    Guid Id,
    Guid UserId,
    Guid TenantId,
    Guid PlanVersionId,
    int PlanVersion,
    string PlanCode,
    string PlanDisplayName,
    string DocumentNormalized,
    string DocumentType,
    string ResponsibleName,
    string Email,
    string EmailNormalized,
    string PasswordHash,
    bool MarketingConsent,
    DateTimeOffset ExpiresAt);
