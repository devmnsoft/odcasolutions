using System.ComponentModel.DataAnnotations;

namespace Odca.Contracts.Identity;

public sealed record LoginRequest(
    [Required, StringLength(254)] string Login,
    [Required, StringLength(256)] string Password);

public sealed record LoginResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    string DisplayName,
    bool MustChangePassword,
    bool RequiresMfaEnrollment,
    bool RequiresMfaChallenge,
    bool MfaVerified);

public sealed record ChangePasswordRequest(
    [Required] string CurrentPassword,
    [Required, MinLength(12)] string NewPassword);

public sealed record MfaEnrollmentResponse(
    string ManualKey,
    string OtpAuthUri);

public sealed record ConfirmMfaEnrollmentRequest(
    [Required, StringLength(16, MinimumLength = 6)] string Code);

public sealed record MfaChallengeRequest(
    [Required, StringLength(32, MinimumLength = 6)] string Code);

public sealed record MfaVerificationResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    string DisplayName,
    bool MfaVerified,
    string[]? RecoveryCodes = null);

public sealed record DashboardResponse(
    string DisplayName,
    int ActiveTenants,
    int ActiveUsers,
    int PendingPrivacyItems);
