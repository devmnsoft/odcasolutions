using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odca.Configuration.Localization;
using Odca.Contracts.Support;
using Odca.Web.Models;
using Odca.Web.Services;

namespace Odca.Web.Controllers;

// D-OC39 (Sprint 5/F-2): sessao temporaria de suporte — area da plataforma.
// Superadministrador abre uma janela limitada (1..480 min, motivo obrigatorio) para uma
// organizacao, com MFA recente exigido em todas as escritas; expiracao e revogacao sao
// aplicadas no SQL e o ator real fica em audit/events. Superficie pt-first (divida i18n registrada).
[Authorize(Roles = "SuperAdministrator")]
[Route("administracao/suporte-sessoes")]
public sealed class SupportSessionsController(OdcaApiClient api, IConfiguration configuration, LocalizationService loc) : Controller
{
    // D-OC39 (Sprint 5/F-1): espelho BFF da janela Identity:RecentAuthMinutes (mesma chave da API).
    private int RecentMfaWindowMinutes => Math.Max(1, configuration.GetValue("Identity:RecentAuthMinutes", 15));

    // D-OC42 (S8): padrão canônico de cultura (CultureMiddleware) e localização das mensagens BFF.
    private string? Culture => HttpContext.Items[Odca.Web.Middleware.CultureMiddleware.ItemsKey] as string;
    private string L(string key, params string[] args) => loc.T(key, Culture, args);

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var token = await AccessTokenAsync();
        if (token is null)
        {
            return Challenge();
        }

        ViewData["Title"] = L("support.title");
        var sessions = await api.GetSupportSessionsAsync(token, 50, cancellationToken);
        var customers = await api.GetCustomersAsync(token, null, null, null, cancellationToken);
        return View(new SupportSessionsViewModel
        {
            Sessions = sessions.Succeeded ? sessions.Value! : [],
            Customers = customers.Succeeded ? customers.Value! : [],
            SessionsLoadFailed = !sessions.Succeeded,
            CustomersLoadFailed = !customers.Succeeded
        });
    }

    [HttpPost("abrir")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Open(SupportSessionCreateViewModel model, CancellationToken cancellationToken)
    {
        var token = await AccessTokenAsync();
        if (token is null)
        {
            return Challenge();
        }

        var result = await api.OpenSupportSessionAsync(
            token,
            new OpenSupportSessionRequest(model.TenantId, model.Reason ?? "", model.DurationMinutes),
            cancellationToken);

        if (!result.Succeeded)
        {
            var redirect = RecentAuthRedirect(token);
            if (redirect is not null)
            {
                return redirect;
            }

            TempData["Error"] = result.Status switch
            {
                ApiCallStatus.Conflict => result.UserMessage(L("support.err_active_exists")),
                ApiCallStatus.InvalidRequest or ApiCallStatus.NotFound => result.UserMessage(L("support.err_invalid")),
                ApiCallStatus.Forbidden => L("support.err_forbidden"),
                _ => L("support.err_open_fail")
            };
            return RedirectToAction(nameof(Index));
        }

        TempData["Success"] = L("support.ok_opened", model.DurationMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("estender")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Extend(SupportSessionExtendViewModel model, CancellationToken cancellationToken)
        => await CloseOrExtendAsync(model.SessionId, model.Minutes, revoke: false, end: false, cancellationToken);

    [HttpPost("encerrar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> End(SupportSessionExtendViewModel model, CancellationToken cancellationToken)
        => await CloseOrExtendAsync(model.SessionId, 0, revoke: false, end: true, cancellationToken);

    [HttpPost("revogar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Revoke(SupportSessionExtendViewModel model, CancellationToken cancellationToken)
        => await CloseOrExtendAsync(model.SessionId, 0, revoke: true, end: false, cancellationToken);

    private async Task<IActionResult> CloseOrExtendAsync(Guid sessionId, int minutes, bool revoke, bool end, CancellationToken cancellationToken)
    {
        if (sessionId == Guid.Empty)
        {
            return RedirectToAction(nameof(Index));
        }

        var token = await AccessTokenAsync();
        if (token is null)
        {
            return Challenge();
        }

        var result =
            !end && !revoke ? await api.ExtendSupportSessionAsync(token, sessionId, new ExtendSupportSessionRequest(minutes), cancellationToken)
            : end ? await api.EndSupportSessionAsync(token, sessionId, cancellationToken)
            : await api.RevokeSupportSessionAsync(token, sessionId, cancellationToken);

        if (!result.Succeeded)
        {
            var redirect = RecentAuthRedirect(token);
            if (redirect is not null)
            {
                return redirect;
            }

            TempData["Error"] = result.UserMessage(L("support.err_not_active"));
            return RedirectToAction(nameof(Index));
        }

        TempData["Success"] = !end && !revoke
            ? L("support.ok_extended", minutes.ToString(System.Globalization.CultureInfo.InvariantCulture), result.Value!.ExpiresAt.ToString("dd/MM/yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture))
            : end ? L("support.ok_ended") : L("support.ok_revoked");
        return RedirectToAction(nameof(Index));
    }

    private RedirectToActionResult? RecentAuthRedirect(string? token)
        => RecentAuthGuard.NeedsFreshChallenge(token, RecentMfaWindowMinutes)
            ? RedirectToAction("MfaChallenge", "Account", new { returnUrl = Request.Path + Request.QueryString })
            : null;

    private async Task<string?> AccessTokenAsync() => await HttpContext.GetTokenAsync("access_token");
}
