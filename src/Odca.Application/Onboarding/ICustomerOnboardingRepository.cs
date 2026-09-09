namespace Odca.Application.Onboarding;

public interface ICustomerOnboardingRepository
{
    Task<CustomerRegistrationDraft?> CreateRegistrationAsync(
        CustomerRegistrationDraft draft,
        string idempotencyKey,
        string confirmationTokenHash,
        string? developmentConfirmationToken,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<bool> ConfirmRegistrationAsync(
        Guid registrationId,
        string tokenHash,
        DateTimeOffset confirmedAt,
        CancellationToken cancellationToken);

    Task<CustomerHome?> GetHomeAsync(
        Guid userId,
        Guid? tenantId,
        CancellationToken cancellationToken);
}
