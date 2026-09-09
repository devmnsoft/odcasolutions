using System.ComponentModel.DataAnnotations;

namespace Odca.Contracts.Onboarding;

public sealed record StartCustomerRegistrationRequest(
    [Required] string PlanCode,
    [Required, StringLength(32)] string Document,
    [Required, StringLength(160)] string ResponsibleName,
    [Required, EmailAddress, StringLength(254)] string Email,
    [Required, MinLength(12), StringLength(256)] string Password,
    [Required] bool AcceptedTerms,
    [Required] bool AcknowledgedPrivacyNotice,
    bool MarketingConsent);

public sealed record StartCustomerRegistrationResponse(
    Guid RegistrationId,
    string Message,
    string? DevelopmentConfirmationToken = null);

public sealed record ConfirmCustomerRegistrationRequest(
    [Required] Guid RegistrationId,
    [Required, StringLength(96, MinimumLength = 32)] string Token);

public sealed record ConfirmCustomerRegistrationResponse(string Message);

public sealed record CustomerHomeResponse(
    Guid TenantId,
    string OrganizationName,
    string TenantStatus,
    string CommercialState,
    string PlanCode,
    string PlanName,
    int PlanVersion,
    int ActiveSeats,
    long StorageBytes);
