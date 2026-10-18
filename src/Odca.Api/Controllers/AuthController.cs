using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Odca.Api.Localization;
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
            var culture = this.Culture();
            var localizer = HttpContext.RequestServices.GetRequiredService<Odca.Configuration.Localization.LocalizationService>();
            // D-OC27: título e detalhe localizados por código estável; o Detalhe canônico PT
            // ("E-mail/CPF ou senha inválidos.") resolve para api.auth.invalid_credentials.
            return Unauthorized(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = localizer.T("api.auth.login_failed", culture),
                // Detalhe canônico PT resolvido por mapa reverso; texto desconhecido volta como veio.
                Detail = ApiMessages.Localize(localizer, result.FailureMessage, culture) ?? localizer.T("api.auth.invalid_credentials", culture)
            });
        }

        return Ok(ToResponse(result.User, result.Session!, result.Token));
    }

    [HttpPost("change-password")]
    [Authorize]
    // D-OC39 (Sprint 5/F-1): reautenticação recente (isenta o fluxo de troca forçada via must_change_password).
    [Authorize(Policy = "RecentMfa")]
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
            // D-OC27: cada mensagem de política de senha (canônica PT) resolve para uma chave api.auth.password_*;
            // mensagem desconhecida volta como veio (nunca se inventa texto).
            var culture = this.Culture();
            var localizer = HttpContext.RequestServices.GetRequiredService<Odca.Configuration.Localization.LocalizationService>();
            return ValidationProblem(new ValidationProblemDetails
            {
                Title = localizer.T("api.validation.invalid_fields", culture),
                Extensions = { ["code"] = "api.change_password.failed" },
                Errors =
                {
                    ["newPassword"] = [.. result.Errors.Select(error => ApiMessages.Localize(localizer, error, culture) ?? error)]
                }
            });
        }

        return Ok(ToResponse(result.User, result.Session!, result.Token));
    }

    [HttpPost("mfa/enrollment")]
    [Authorize]
    [Authorize(Policy = "RecentMfa")]
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

    [HttpPost("mfa/enrollment/regenerate")]
    [Authorize]
    [Authorize(Policy = "RecentMfa")]
    public async Task<ActionResult<MfaEnrollmentResponse>> RegenerateMfaEnrollment(CancellationToken cancellationToken)
    {
        if (!TryGetIdentity(out var userId, out var sessionId, out var securityVersion))
        {
            return Unauthorized();
        }

        var enrollment = await mfa.RegenerateEnrollmentAsync(userId, sessionId, securityVersion, cancellationToken);
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
            // D-OC27: chave com escopo de endpoint (mfa.confirm.*) — o mesmo texto canônico pode
            // aparecer em endpoints diferentes e o código no wire deve permanecer estável.
            var culture = this.Culture();
            var localizer = HttpContext.RequestServices.GetRequiredService<Odca.Configuration.Localization.LocalizationService>();
            var message = LocalizedMfaError(localizer, result.Errors is { Count: > 0 } ? result.Errors[0] : null, "api.auth.mfa_confirm_", culture);
            return ValidationProblem(new ValidationProblemDetails
            {
                Title = localizer.T("api.validation.invalid_fields", culture),
                Extensions = { ["code"] = message.Key },
                Errors = { ["code"] = [message.Text] }
            });
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
            // D-OC27: chave com escopo de endpoint (mfa.challenge.*).
            var culture = this.Culture();
            var localizer = HttpContext.RequestServices.GetRequiredService<Odca.Configuration.Localization.LocalizationService>();
            var message = LocalizedMfaError(localizer, result.Errors is { Count: > 0 } ? result.Errors[0] : null, "api.auth.mfa_challenge_", culture);
            return ValidationProblem(new ValidationProblemDetails
            {
                Title = localizer.T("api.validation.invalid_fields", culture),
                Extensions = { ["code"] = message.Key },
                Errors = { ["code"] = [message.Text] }
            });
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

    /// <summary>D-OC27: mapeia o texto canônico de erro do MFA (PT, Odca.Application.Identity.MfaService)
    /// para a chave api.auth.&lt;escopo&gt;.&lt;razão&gt; do catálogo. Texto desconhecido → chave unknown
    /// + mensagem original preservada.</summary>
    private static (string Key, string Text) LocalizedMfaError(
        Odca.Configuration.Localization.LocalizationService localizer,
        string? canonicalText,
        string scopePrefix,
        string culture)
    {
        if (canonicalText is null)
        {
            return ("api.auth.unknown", localizer.T("api.auth.unknown", culture));
        }
        var suffix = ApiMessages.MfaErrorSuffix(canonicalText) ?? "unknown";
        var key = $"{scopePrefix}{suffix}";
        // Chave ausente no catálogo → texto canônico preservado.
        var localized = localizer.T(key, culture);
        return (key, localized == key ? canonicalText : localized);
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
