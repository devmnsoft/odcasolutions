using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odca.Configuration.Localization;
using Odca.Contracts.Identity;
using Odca.Web.Models;
using Odca.Web.Services;
using QRCoder;

namespace Odca.Web.Controllers;

public sealed class AccountController(OdcaApiClient apiClient, IUserTenantContext tenantContext, LocalizationService loc, IConfiguration configuration) : Controller
{
    private string? Culture => HttpContext.Items[Odca.Web.Middleware.CultureMiddleware.ItemsKey] as string;
    private string L(string key, params string[] args) => loc.T(key, Culture, args);
    // D-OC39 (Sprint 5/F-1): espelho BFF da janela Identity:RecentAuthMinutes (mesma chave da API).
    private int RecentMfaWindowMinutes => Math.Max(1, configuration.GetValue("Identity:RecentAuthMinutes", 15));
    [AllowAnonymous]
    [HttpGet("entrar")]
    public IActionResult Login(string? returnUrl = null) =>
        View(new LoginViewModel { ReturnUrl = SafeReturnUrl(returnUrl) });

    [Authorize]
    [HttpGet("minha-conta")]
    public IActionResult Conta()
    {
        ViewData["Title"] = "Minha conta";
        return View();
    }

    [Authorize]
    [HttpGet("minha-conta/notificacoes")]
    public async Task<IActionResult> Notifications(int page = 1, int pageSize = 25, CancellationToken ct = default)
    {
        ViewData["Title"] = L("notificacoes");
        var access = await tenantContext.GetCurrentOrFirstActiveAsync(null, ct);
        if (access is null) return View(new Odca.Contracts.Account.NotificationPage(Array.Empty<Odca.Contracts.Account.NotificationItem>(), 0, 1, 25, 0, false));
        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null) return Challenge();
        var result = await apiClient.GetMyNotificationsAsync(token, access.TenantId, page, pageSize, ct);
        if (result.Status == ApiCallStatus.Unauthorized) return Challenge();
        if (result.Status == ApiCallStatus.Forbidden) return Forbid();
        if (!result.Succeeded || result.Value is null) { Response.StatusCode = 503; return View("ServiceUnavailable"); }
        ViewData["TenantId"] = access.TenantId;
        return View(result.Value);
    }

    [Authorize]
    [HttpPost("minha-conta/notificacoes/marcar-lidas")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkNotificationsRead(CancellationToken ct)
    {
        var access = await tenantContext.GetCurrentOrFirstActiveAsync(null, ct);
        if (access is null) return RedirectToAction(nameof(Notifications));
        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null) return Challenge();
        await apiClient.MarkMyNotificationsReadAsync(token, access.TenantId, ct);
        TempData["Success"] = L("notif.msg.marked_all_read");
        return RedirectToAction(nameof(Notifications));
    }

    [Authorize]
    [HttpPost("minha-conta/notificacoes/{id:guid}/ler")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkOneNotificationRead(Guid id, CancellationToken ct)
    {
        var access = await tenantContext.GetCurrentOrFirstActiveAsync(null, ct);
        if (access is null) return RedirectToAction(nameof(Notifications));
        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null) return Challenge();
        await apiClient.MarkOneNotificationReadAsync(token, access.TenantId, id, ct);
        TempData["Success"] = L("notif.msg.marked_read");
        return RedirectToAction(nameof(Notifications));
    }

    [Authorize]
    [HttpGet("minha-conta/notificacoes/preferencias")]
    public async Task<IActionResult> NotificationPreferences(CancellationToken ct)
    {
        ViewData["Title"] = L("notif.prefs_card_title");
        var access = await tenantContext.GetCurrentOrFirstActiveAsync(null, ct);
        if (access is null) return RedirectToAction(nameof(Notifications));
        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null) return Challenge();
        var result = await apiClient.GetMyNotificationPreferencesAsync(token, access.TenantId, ct);
        if (result.Status == ApiCallStatus.Unauthorized) return Challenge();
        if (result.Status == ApiCallStatus.Forbidden) return Forbid();
        if (!result.Succeeded || result.Value is null) { Response.StatusCode = 503; return View("ServiceUnavailable"); }
        ViewData["TenantId"] = access.TenantId;
        return View(result.Value);
    }

    [Authorize]
    [HttpPost("minha-conta/notificacoes/preferencias")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveNotificationPreferences(Odca.Contracts.Account.UpdateNotificationPreferencesRequest model, CancellationToken ct)
    {
        var access = await tenantContext.GetCurrentOrFirstActiveAsync(null, ct);
        if (access is null) return RedirectToAction(nameof(NotificationPreferences));
        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null) return Challenge();
        var result = await apiClient.PutMyNotificationPreferencesAsync(token, access.TenantId, model, ct);
        if (result.Status == ApiCallStatus.Unauthorized) return Challenge();
        if (result.Status == ApiCallStatus.Forbidden) return Forbid();
        if (!result.Succeeded)
        {
            ModelState.AddModelError("", L("notif.msg.save_fail"));
            var current = await apiClient.GetMyNotificationPreferencesAsync(token, access.TenantId, ct);
            if (current.Succeeded && current.Value is not null)
                return View("NotificationPreferences", ToUpdateRequest(current.Value));
            return RedirectToAction(nameof(NotificationPreferences));
        }
        TempData["Success"] = L("notif.msg.saved");
        return RedirectToAction(nameof(NotificationPreferences));
    }

    [Authorize]
    [HttpPost("minha-conta/idioma")]
    public IActionResult SetLanguage(string? lang, string? returnUrl)
    {
        var code = Odca.Configuration.Localization.LocalizationService.ResolveCultureCode(lang) ?? "pt-BR";
        Response.Cookies.Append(Middleware.CultureMiddleware.CookieName, code, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = Request.IsHttps,
            MaxAge = TimeSpan.FromDays(365)
        });
        return Redirect(SafeReturnUrl(returnUrl ?? "/") ?? "/");
    }

    [AllowAnonymous]
    [HttpPost("entrar")]
    public async Task<IActionResult> Login(LoginViewModel model, CancellationToken cancellationToken)
    {
        model.ReturnUrl = SafeReturnUrl(model.ReturnUrl);
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await apiClient.LoginAsync(new LoginRequest(model.Login, model.Password), cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Status switch
            {
                ApiCallStatus.RateLimited => "Muitas tentativas. Aguarde um minuto e tente novamente.",
                ApiCallStatus.Timeout => "O serviço demorou para responder. Tente novamente.",
                ApiCallStatus.Unavailable => "O serviço está temporariamente indisponível.",
                _ => "E-mail/CPF ou senha inválidos."
            });
            return View(model);
        }

        var response = result.Value!;
        await SignInAsync(response);
        return RedirectAfterAuthentication(response, model.ReturnUrl);
    }

    [Authorize]
    [HttpGet("alterar-senha")]
    public IActionResult ChangePassword() => View(new ChangePasswordViewModel());

    [Authorize]
    [HttpPost("alterar-senha")]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null)
        {
            return RedirectToAction(nameof(Login));
        }

        var result = await apiClient.ChangePasswordAsync(
            token,
            new ChangePasswordRequest(model.CurrentPassword, model.NewPassword),
            cancellationToken);
        if (!result.Succeeded)
        {
            if (result.Status == ApiCallStatus.Unauthorized)
            {
                await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return RedirectToAction(nameof(Login));
            }

            // D-OC39 (Sprint 5/F-1): MFA recente obsoleto → desafio com retorno à tela de troca.
            if (result.Status == ApiCallStatus.Forbidden &&
                RecentAuthGuard.NeedsFreshChallenge(token, RecentMfaWindowMinutes))
            {
                return RedirectToAction(nameof(MfaChallenge), new { returnUrl = Request.Path });
            }

            ModelState.AddModelError(string.Empty, result.Status switch
            {
                ApiCallStatus.Forbidden => "Você não tem permissão para alterar esta senha.",
                ApiCallStatus.RateLimited => "Muitas tentativas. Aguarde e tente novamente.",
                ApiCallStatus.Timeout or ApiCallStatus.Unavailable => "O serviço está temporariamente indisponível.",
                _ => "Não foi possível alterar a senha. Confira a senha atual e os requisitos informados."
            });
            return View(model);
        }

        await SignInAsync(result.Value!);
        TempData["Success"] = "Senha alterada com segurança.";
        return RedirectAfterAuthentication(result.Value!);
    }

    [Authorize]
    [HttpGet("mfa/inscricao")]
    public async Task<IActionResult> MfaEnrollment(CancellationToken cancellationToken)
    {
        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null)
        {
            return RedirectToAction(nameof(Login));
        }

        var result = await apiClient.StartMfaEnrollmentAsync(token, cancellationToken);
        if (!result.Succeeded)
        {
            return result.Status == ApiCallStatus.Unauthorized
                ? RedirectToAction(nameof(Login))
                : RedirectToAction(nameof(MfaChallenge));
        }

        return View(new MfaEnrollmentViewModel
        {
            ManualKey = result.Value!.ManualKey,
            OtpAuthUri = result.Value.OtpAuthUri,
            QrDataUri = BuildQrDataUri(result.Value.OtpAuthUri)
        });
    }

    [Authorize]
    [HttpPost("mfa/inscricao")]
    public async Task<IActionResult> MfaEnrollment(MfaEnrollmentViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null)
        {
            return RedirectToAction(nameof(Login));
        }

        var result = await apiClient.ConfirmMfaEnrollmentAsync(
            token,
            new ConfirmMfaEnrollmentRequest(model.Code),
            cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, FieldMessage(result, () => result.Status switch
            {
                ApiCallStatus.Unauthorized => "Sua sessão expirou. Entre novamente.",
                ApiCallStatus.RateLimited => "Muitas tentativas. Aguarde e tente novamente.",
                ApiCallStatus.Timeout or ApiCallStatus.Unavailable => "O serviço está temporariamente indisponível.",
                _ => "Código inválido ou expirado."
            }));
            return View(model);
        }

        await SignInAsync(result.Value!);
        return View("RecoveryCodes", new MfaRecoveryCodesViewModel { Codes = result.Value!.RecoveryCodes ?? [] });
    }

    [Authorize]
    [HttpPost("mfa/inscricao/regenerar")]
    public async Task<IActionResult> MfaEnrollmentRegenerate(CancellationToken cancellationToken)
    {
        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null)
        {
            return RedirectToAction(nameof(Login));
        }

        var result = await apiClient.RegenerateMfaEnrollmentAsync(token, cancellationToken);
        if (!result.Succeeded || result.Value is null)
        {
            var fallback = await apiClient.StartMfaEnrollmentAsync(token, cancellationToken);
            if (fallback.Succeeded && fallback.Value is not null)
            {
                var model = new MfaEnrollmentViewModel
                {
                    ManualKey = fallback.Value.ManualKey,
                    OtpAuthUri = fallback.Value.OtpAuthUri,
                    QrDataUri = BuildQrDataUri(fallback.Value.OtpAuthUri)
                };
                ModelState.AddModelError(string.Empty, "Não foi possível gerar uma nova chave agora. A chave exibida continua válida.");
                return View("MfaEnrollment", model);
            }

            return RedirectToAction(nameof(MfaChallenge));
        }

        return View(new MfaEnrollmentViewModel
        {
            ManualKey = result.Value.ManualKey,
            OtpAuthUri = result.Value.OtpAuthUri,
            QrDataUri = BuildQrDataUri(result.Value.OtpAuthUri),
            Regenerated = true
        });
    }

    [Authorize]
    [HttpGet("mfa/desafio")]
    public IActionResult MfaChallenge(string? returnUrl = null) => View(new MfaChallengeViewModel { ReturnUrl = SafeReturnUrl(returnUrl) });

    [Authorize]
    [HttpPost("mfa/desafio")]
    public async Task<IActionResult> MfaChallenge(MfaChallengeViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null)
        {
            return RedirectToAction(nameof(Login));
        }

        var result = await apiClient.VerifyMfaChallengeAsync(
            token,
            new MfaChallengeRequest(model.Code),
            cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, FieldMessage(result, () => result.Status switch
            {
                ApiCallStatus.Unauthorized => "Sua sessão expirou. Entre novamente.",
                ApiCallStatus.RateLimited => "Muitas tentativas. Aguarde e tente novamente.",
                ApiCallStatus.Timeout or ApiCallStatus.Unavailable => "O serviço está temporariamente indisponível.",
                _ => "Código inválido, expirado ou já utilizado."
            }));
            return View(model);
        }

        await SignInAsync(result.Value!);
        var target = SafeReturnUrl(model.ReturnUrl);
        return string.IsNullOrWhiteSpace(target) ? RedirectToAction("Index", "Home") : Redirect(target);
    }

    [Authorize]
    [HttpPost("sair")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var token = await HttpContext.GetTokenAsync("access_token");
        var remotelyRevoked = token is null;
        try
        {
            if (token is not null)
            {
                remotelyRevoked = await apiClient.LogoutAsync(token, cancellationToken);
            }
        }
        finally
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }

        if (!remotelyRevoked)
        {
            TempData["LogoutWarning"] = "A sessão local foi encerrada. A revogação remota não pôde ser confirmada e o acesso expirará automaticamente.";
        }
        return RedirectToAction(nameof(Login));
    }

    [AllowAnonymous]
    [HttpGet("acesso-negado")]
    public IActionResult AccessDenied() => View();

    private IActionResult RedirectAfterAuthentication(LoginResponse response, string? returnUrl = null)
    {
        if (response.MustChangePassword)
        {
            return RedirectToAction(nameof(ChangePassword));
        }

        if (response.RequiresMfaEnrollment)
        {
            return RedirectToAction(nameof(MfaEnrollment));
        }

        if (response.RequiresMfaChallenge)
        {
            return RedirectToAction(nameof(MfaChallenge), new { returnUrl });
        }

        if (!string.IsNullOrWhiteSpace(returnUrl))
        {
            return Redirect(returnUrl);
        }

        return RedirectToAction("Index", "Home");
    }

    private static string? SafeReturnUrl(string? returnUrl) =>
        TenancyDisplay.IsSafeLocalUrl(returnUrl) ? returnUrl : null;

    private async Task SignInAsync(LoginResponse response)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, response.DisplayName),
            new(ClaimTypes.Role, response.IsPlatformAdministrator ? "SuperAdministrator" : "Customer"),
            new("must_change_password", response.MustChangePassword ? "true" : "false"),
            new("mfa_verified", response.MfaVerified ? "true" : "false"),
            new("requires_mfa_enrollment", response.RequiresMfaEnrollment ? "true" : "false"),
            new("requires_mfa_challenge", response.RequiresMfaChallenge ? "true" : "false")
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var properties = new AuthenticationProperties
        {
            IsPersistent = false,
            ExpiresUtc = response.ExpiresAt,
            AllowRefresh = false
        };
        properties.StoreTokens([new AuthenticationToken { Name = "access_token", Value = response.AccessToken }]);
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            properties);
    }

    private async Task SignInAsync(MfaVerificationResponse response)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, response.DisplayName),
            new(ClaimTypes.Role, response.IsPlatformAdministrator ? "SuperAdministrator" : "Customer"),
            new("must_change_password", "false"),
            new("mfa_verified", response.MfaVerified ? "true" : "false"),
            new("requires_mfa_enrollment", "false"),
            new("requires_mfa_challenge", "false")
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var properties = new AuthenticationProperties
        {
            IsPersistent = false,
            ExpiresUtc = response.ExpiresAt,
            AllowRefresh = false
        };
        properties.StoreTokens([new AuthenticationToken { Name = "access_token", Value = response.AccessToken }]);
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            properties);
    }

    private static string FieldMessage(ApiCallResult<MfaVerificationResponse> result, Func<string> fallback) =>
        result.ValidationErrors is not null
        && result.ValidationErrors.TryGetValue("code", out var messages)
        && messages.Length > 0
            ? messages[0]
            : fallback();

    private static string BuildQrDataUri(string otpAuthUri)
    {
        var qrCodeData = new QRCodeGenerator().CreateQrCode(
            otpAuthUri, QRCodeGenerator.ECCLevel.M, false, false, QRCodeGenerator.EciMode.Default, -1);
        var svg = new SvgQRCode(qrCodeData).GetGraphic(4);
        return $"data:image/svg+xml;base64,{Convert.ToBase64String(Encoding.UTF8.GetBytes(svg))}";
    }

    private static Odca.Contracts.Account.UpdateNotificationPreferencesRequest ToUpdateRequest(Odca.Contracts.Account.NotificationPreferences prefs) =>
        new()
        {
            PreferredLanguage = prefs.PreferredLanguage,
            Kinds = prefs.Kinds.Select(k => new Odca.Contracts.Account.NotificationKindPreference { Kind = k.Kind, Enabled = k.Enabled, ChannelInApp = k.ChannelInApp, ChannelEmail = k.ChannelEmail }).ToList()
        };
}
