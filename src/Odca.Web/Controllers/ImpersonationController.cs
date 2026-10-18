using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odca.Configuration.Localization;
using Odca.Contracts.Support;
using Odca.Web.Models;
using Odca.Web.Services;

namespace Odca.Web.Controllers;

// D-OC43 (Sprint 9/F-1): visão do cliente (impersonação profunda) dentro da janela de suporte.
// A área de plataformas continua exigindo SuperAdministrator para abrir/entrar; o ENCERRAR
// precisa funcionar vindo do próprio cookie da visão do cliente (papel Customer), por isso
// este controller usa [Authorize] na raiz e restringe por ação. A autoridade final é a API,
// que revalida a concessão em cada request e atribui os atos ao operador real.
[Authorize]
[Route("administracao/suporte-sessoes")]
public sealed class ImpersonationController(OdcaApiClient api, IConfiguration configuration, LocalizationService loc) : Controller
{
    private int RecentMfaWindowMinutes => Math.Max(1, configuration.GetValue("Identity:RecentAuthMinutes", 15));
    private string? Culture => HttpContext.Items[Odca.Web.Middleware.CultureMiddleware.ItemsKey] as string;
    private string L(string key, params string[] args) => loc.T(key, Culture, args);

    /// <summary>Lista os membros do tenant da janela para o operador escolher o alvo.</summary>
    [HttpGet("{sessionId:guid}/cliente")]
    [Authorize(Roles = "SuperAdministrator")]
    public async Task<IActionResult> Cliente(Guid sessionId, CancellationToken cancellationToken)
    {
        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null)
        {
            return Challenge();
        }

        var sessions = await api.GetSupportSessionsAsync(token, 50, cancellationToken);
        var session = sessions.Succeeded ? sessions.Value!.FirstOrDefault(s => s.Id == sessionId) : null;
        if (session is null || !session.Valid)
        {
            TempData["Error"] = L("support.err_not_active");
            return RedirectToAction("Index", "SupportSessions");
        }

        var members = await api.GetSupportSessionMembersAsync(token, sessionId, cancellationToken);
        ViewData["Title"] = L("support.imp_view_title");
        return View(new SupportImpersonationViewModel
        {
            Session = session,
            Members = members.Succeeded ? members.Value! : [],
            MembersLoadFailed = !members.Succeeded
        });
    }

    /// <summary>Inicia a visão do cliente: troca o cookie pela identidade do alvo e preserva o token do operador.</summary>
    [HttpPost("cliente/entrar")]
    [Authorize(Roles = "SuperAdministrator")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Entrar(SupportImpersonationStartViewModel model, CancellationToken cancellationToken)
    {
        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null)
        {
            return Challenge();
        }

        if (model.SessionId == Guid.Empty || model.TenantId == Guid.Empty || model.TargetUserId == Guid.Empty ||
            model.Minutes is < 5 or > 60)
        {
            TempData["Error"] = L("support.imp_err_invalid");
            return RedirectToAction(nameof(Cliente), new { sessionId = model.SessionId });
        }

        var result = await api.StartSupportImpersonationAsync(
            token, model.SessionId, new StartSupportImpersonationRequest(model.TargetUserId, model.Minutes), cancellationToken);

        if (!result.Succeeded)
        {
            var redirect = RecentAuthGuard.NeedsFreshChallenge(token, RecentMfaWindowMinutes)
                ? RedirectToAction("MfaChallenge", "Account", new { returnUrl = Request.Path })
                : null;
            if (redirect is not null)
            {
                return redirect;
            }

            TempData["Error"] = result.Status switch
            {
                ApiCallStatus.Conflict => L("support.imp_err_active_exists"),
                ApiCallStatus.NotFound or ApiCallStatus.InvalidRequest => L("support.imp_err_invalid"),
                ApiCallStatus.Forbidden => L("support.err_forbidden"),
                _ => L("support.imp_err_start_fail")
            };
            return RedirectToAction(nameof(Cliente), new { sessionId = model.SessionId });
        }

        var value = result.Value!;
        var operatorName = User.Identity?.Name ?? string.Empty;
        var principal = ImpersonationAuth.BuildTargetIdentity(
            model.TargetName, model.TenantId, model.SessionId, value.GrantId.ToString(), operatorName, value.ExpiresAt);

        var properties = new AuthenticationProperties
        {
            IsPersistent = false,
            ExpiresUtc = value.ExpiresAt,
            AllowRefresh = false
        };
        properties.StoreTokens([
            new AuthenticationToken { Name = "access_token", Value = value.AccessToken },
            new AuthenticationToken { Name = ImpersonationAuth.OperatorTokenProperty, Value = token }
        ]);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, properties);

        return RedirectToAction("Index", "Home");
    }

    /// <summary>Encerra a concessão (se ainda ativa) e restaura a identidade/token do operador.</summary>
    [HttpPost("cliente/sair")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Sair(CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ImpersonationAuth.GrantClaim), out var grantId))
        {
            return RedirectToAction("Index", "SupportSessions");
        }

        var supportSessionId = Guid.TryParse(User.FindFirstValue(ImpersonationAuth.SupportSessionClaim), out var s) ? s : Guid.Empty;
        var token = await HttpContext.GetTokenAsync("access_token");
        var operatorToken = await HttpContext.GetTokenAsync(ImpersonationAuth.OperatorTokenProperty);

        // Tenta encerrar de forma limpa; se já expirou, segue para restaurar mesmo assim.
        if (token is not null && supportSessionId != Guid.Empty)
        {
            _ = await api.EndSupportImpersonationAsync(token, supportSessionId, grantId, cancellationToken);
        }

        var restored = ImpersonationAuth.RestoreOperator(operatorToken);
        if (restored is null)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction(nameof(AccountController.Login));
        }

        var (operatorIdentity, operatorExpires) = restored.Value;
        var properties = new AuthenticationProperties
        {
            IsPersistent = false,
            ExpiresUtc = operatorExpires,
            AllowRefresh = false
        };
        properties.StoreTokens([new AuthenticationToken { Name = "access_token", Value = operatorToken! }]);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, operatorIdentity, properties);

        return RedirectToAction("Index", "SupportSessions");
    }
}
