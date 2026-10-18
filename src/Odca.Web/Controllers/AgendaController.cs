using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odca.Configuration.Localization;
using Odca.Web.Models;
using Odca.Web.Services;

namespace Odca.Web.Controllers;

[Authorize]
[Route("organizacoes/{tenantId:guid}/agenda")]
public sealed class AgendaController(OdcaApiClient api, LocalizationService loc) : Controller
{
    // D-OC42 (S8): padrão canônico de cultura (CultureMiddleware) e localização das mensagens BFF.
    private string? Culture => HttpContext.Items[Odca.Web.Middleware.CultureMiddleware.ItemsKey] as string;
    private string L(string key, params string[] args) => loc.T(key, Culture, args);

    [HttpGet("")]
    public async Task<IActionResult> Index(
        Guid tenantId,
        int? year = null,
        int? month = null,
        int? day = null,
        string scope = "mine",
        Guid? ownerId = null,
        string? kind = null,
        CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        year ??= today.Year;
        month ??= today.Month;
        ViewData["Title"] = L("agenda.title");
        ViewData["TenantId"] = tenantId;
        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null) return Challenge();

        var result = await api.GetAgendaAsync(token, tenantId, year.Value, month.Value, scope, ownerId, kind, ct);
        if (result.Status == ApiCallStatus.Unauthorized) return Challenge();
        if (result.Status == ApiCallStatus.Forbidden) return Forbid();

        var fallbackDay = new DateOnly(year.Value, month.Value, 1);
        var page = result.Value ?? new(year.Value, month.Value, fallbackDay, fallbackDay, fallbackDay, [], 0);

        // Filter days by ?day= if provided
        if (day.HasValue && page.Days.Count > 0)
        {
            var filtered = page.Days.Where(d => d.Day.Day == day.Value).ToList();
            page = page with { Days = filtered };
        }

        // Toast when switching to an empty month
        if (page.Total == 0)
        {
            TempData["AgendaEmptyNotice"] = L("agenda.err_empty_month");
        }

        return View(new AgendaWorkspaceViewModel
        {
            Page = page,
            Scope = scope,
            Kind = kind,
            Day = day,
            Error = result.Succeeded ? null : result.UserMessage(L("agenda.load_error"))
        });
    }
}
