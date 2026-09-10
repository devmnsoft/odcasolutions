using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Odca.Application.Common;
using Odca.Application.Identity;
using Odca.Contracts.Identity;

namespace Odca.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(
    AuthenticationService authentication,
    MfaService mfa,
    IIdentityRepository repository,
    IClock clock)
    : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<LoginResponse>> Login(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        var result = await authentication.LoginAsync(
            request.Login,
            request.Password,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers.UserAgent.ToString(),
            cancellationToken);
        if (!result.Succeeded || result.User is null || result.Token is null)
        {
            return Unauthorized(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Não foi possível autenticar.",
                Detail = result.FailureMessage
            });
        }

        return Ok(ToResponse(result.User, result.Session!, result.Token));
    }

    [HttpPost("change-password")]
    [Authorize]
    public async Task<ActionResult<LoginResponse>> ChangePassword(
        ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetIdentity(out var userId, out var sessionId))
        {
            return Unauthorized();
        }

        var result = await authentication.ChangePasswordAsync(
            userId,
            sessionId,
            request.CurrentPassword,
            request.NewPassword,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers.UserAgent.ToString(),
            cancellationToken);
        if (!result.Succeeded || result.User is null || result.Token is null)
        {
            return ValidationProblem(new ValidationProblemDetails(
                new Dictionary<string, string[]> { ["newPassword"] = [.. result.Errors] }));
        }

        return Ok(ToResponse(result.User, result.Session!, result.Token));
    }

    [HttpPost("mfa/enrollment")]
    [Authorize]
    public async Task<ActionResult<MfaEnrollmentResponse>> StartMfaEnrollment(CancellationToken cancellationToken)
    {
        if (!TryGetIdentity(out var userId, out var sessionId, out var securityVersion))
        {
            return Unauthorized();
        }

        var enrollment = await mfa.StartEnrollmentAsync(userId, sessionId, securityVersion, cancellationToken);
        if (enrollment is null)
        {
            return Forbid();
        }

        return Ok(new MfaEnrollmentResponse(enrollment.ManualKey, enrollment.OtpAuthUri));
    }

    [HttpPost("mfa/enrollment/confirm")]
    [Authorize]
    public async Task<ActionResult<MfaVerificationResponse>> ConfirmMfaEnrollment(
        ConfirmMfaEnrollmentRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetIdentity(out var userId, out var sessionId, out var securityVersion))
        {
            return Unauthorized();
        }

        var result = await mfa.ConfirmEnrollmentAsync(
            userId,
            sessionId,
            securityVersion,
            request.Code,
            cancellationToken);
        if (!result.Succeeded || result.User is null || result.Session is null || result.Token is null)
        {
            return ValidationProblem(new ValidationProblemDetails(
                new Dictionary<string, string[]> { ["code"] = [.. result.Errors] }));
        }

        return Ok(ToMfaResponse(result.User, result.Token, result.RecoveryCodes));
    }

    [HttpPost("mfa/challenge")]
    [Authorize]
    public async Task<ActionResult<MfaVerificationResponse>> VerifyMfaChallenge(
        MfaChallengeRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetIdentity(out var userId, out var sessionId, out var securityVersion))
        {
            return Unauthorized();
        }

        var result = await mfa.VerifyChallengeAsync(
            userId,
            sessionId,
            securityVersion,
            request.Code,
            cancellationToken);
        if (!result.Succeeded || result.User is null || result.Session is null || result.Token is null)
        {
            return ValidationProblem(new ValidationProblemDetails(
                new Dictionary<string, string[]> { ["code"] = [.. result.Errors] }));
        }

        return Ok(ToMfaResponse(result.User, result.Token, []));
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        if (TryGetIdentity(out var userId, out var sessionId))
        {
            await repository.RevokeSessionAsync(userId, sessionId, clock.UtcNow, cancellationToken);
        }

        return NoContent();
    }

    private bool TryGetIdentity(out Guid userId, out Guid sessionId)
    {
        var hasIdentity = TryGetIdentity(out userId, out sessionId, out _);
        return hasIdentity;
    }

    private bool TryGetIdentity(out Guid userId, out Guid sessionId, out int securityVersion)
    {
        var hasUser = Guid.TryParse(User.FindFirstValue("sub"), out userId);
        var hasSession = Guid.TryParse(User.FindFirstValue("sid"), out sessionId);
        var hasSecurityVersion = int.TryParse(User.FindFirstValue("security_version"), out securityVersion);
        return hasUser && hasSession && hasSecurityVersion;
    }

    private static LoginResponse ToResponse(UserCredential user, SessionRecord session, IssuedToken token)
    {
        var mfaVerified = session.AuthenticationLevel == "mfa";
        return new(
            token.AccessToken,
            token.ExpiresAt,
            user.DisplayName,
            user.MustChangePassword,
            user.IsPlatformAdministrator && !user.MustChangePassword && user.MfaConfirmedAt is null,
            user.IsPlatformAdministrator && !user.MustChangePassword && user.MfaConfirmedAt is not null && !mfaVerified,
            mfaVerified,
            user.IsPlatformAdministrator);
    }

    private static MfaVerificationResponse ToMfaResponse(
        UserCredential user,
        IssuedToken token,
        IReadOnlyList<string> recoveryCodes) =>
        new(
            token.AccessToken,
            token.ExpiresAt,
            user.DisplayName,
            true,
            user.IsPlatformAdministrator,
            recoveryCodes.Count == 0 ? null : [.. recoveryCodes]);
}
