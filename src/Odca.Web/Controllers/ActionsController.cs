using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odca.Configuration.Localization;
using Odca.Web.Services;

namespace Odca.Web.Controllers;

/// <summary>
/// Central de Acoes — entrada no menu que reutiliza o endpoint /operations/action-center.
/// Nao cria dashboard paralelo: exibe categorias, filtros por fonte e link ao registro.
/// </summary>
[Authorize]
[Route("organizacoes/{tenantId:guid}/acoes")]
public sealed class ActionsController(OdcaApiClient api, LocalizationService loc) : Controller
{
    // D-OC47 (S13): cultura canônica via CultureMiddleware e mensagens do BFF localizadas.
    private string? Culture => HttpContext.Items[Odca.Web.Middleware.CultureMiddleware.ItemsKey] as string;
    private string L(string key, params string[] args) => loc.T(key, Culture, args);

    [HttpGet("")]
    public async Task<IActionResult> Index(
        Guid tenantId,
        string scope = "mine",
        string? source = null,
        int page = 1,
        CancellationToken ct = default)
    {
        ViewData["Title"] = L("actions.title");
        ViewData["TenantId"] = tenantId;
        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null) return Challenge();

        var result = await api.GetActionCenterAsync(token, tenantId, scope, source, page, 25, ct);
        if (result.Status == ApiCallStatus.Unauthorized) return Challenge();
        if (result.Status == ApiCallStatus.Forbidden) return Forbid();
        if (!result.Succeeded || result.Value is null)
            return View(new ActionCenterViewModel
            {
                Error = result.UserMessage(L("actions.msg_load_fail")),
                Scope = scope,
                Source = source,
                Page = page,
                Total = 0,
                Today = DateOnly.MinValue
            });

        var ac = result.Value;
        return View(new ActionCenterViewModel
        {
            Data = ac,
            Scope = scope,
            Source = source,
            Page = page,
            Total = ac.Total,
            Today = ac.Today
        });
    }
}

public sealed class ActionCenterViewModel
{
    public Odca.Contracts.Operations.ActionCenterResponse? Data { get; init; }
    public string? Error { get; init; }
    public string Scope { get; init; } = "mine";
    public string? Source { get; init; }
    public int Page { get; init; } = 1;
    public int Total { get; init; }
    public DateOnly Today { get; init; }
}
