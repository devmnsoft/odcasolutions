using System.ComponentModel.DataAnnotations;

namespace Odca.Contracts.Identity;

public sealed record LoginRequest(
    [Required, StringLength(254)] string Login,
    [Required, StringLength(256)] string Password);

public sealed record LoginResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    string DisplayName,
    bool MustChangePassword);

public sealed record ChangePasswordRequest(
    [Required] string CurrentPassword,
    [Required, MinLength(12)] string NewPassword);

public sealed record DashboardResponse(
    string DisplayName,
    int ActiveTenants,
    int ActiveUsers,
    int PendingPrivacyItems);
