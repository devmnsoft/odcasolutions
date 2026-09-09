namespace Odca.Application.Privacy;

public sealed record PrivacyRequestSubmission(
    Guid Id,
    string Protocol,
    string Email,
    string RequestType,
    string? Details);
