using System.ComponentModel.DataAnnotations;

namespace Odca.Contracts.Privacy;

public sealed record CreatePrivacyRequest(
    [Required, EmailAddress, StringLength(254)] string Email,
    [Required, RegularExpression("^(access|correction|sharing|portability|blocking|deletion|revocation|review)$")]
    string RequestType,
    [StringLength(2000)] string? Details);

public sealed record PrivacyRequestCreated(string Protocol, string Message);
