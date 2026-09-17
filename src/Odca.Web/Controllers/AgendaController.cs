using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odca.Web.Models;
using Odca.Web.Services;

namespace Odca.Web.Controllers;

[Authorize]
[Route("organizacoes/{tenantId:guid}/agenda")]
public sealed class AgendaController(OdcaApiClient api) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(
        Guid tenantId,
        int? year = null,
        int? month = null,
        string scope = "mine",
        Guid? ownerId = null,
        CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        year ??= today.Year;
        month ??= today.Month;
        ViewData["Title"] = "Agenda mensal";
        ViewData["TenantId"] = tenantId;
        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null) return Challenge();

        var result = await api.GetAgendaAsync(token, tenantId, year.Value, month.Value, scope, ownerId, ct);
        if (result.Status == ApiCallStatus.Unauthorized) return Challenge();
        if (result.Status == ApiCallStatus.Forbidden) return Forbid();

        return View(new AgendaWorkspaceViewModel
        {
            Page = result.Value ?? new(year.Value, month.Value, new DateOnly(year.Value, month.Value, 1), new DateOnly(year.Value, month.Value, 1), [], 0),
            Scope = scope,
            Error = result.Succeeded ? null : result.UserMessage("Não foi possível abrir a agenda do mês.")
        });
    }
}
