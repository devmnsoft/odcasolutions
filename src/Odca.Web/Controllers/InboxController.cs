using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Odca.Contracts.SavedViews;
using Odca.Web.Models;
using Odca.Web.Services;

namespace Odca.Web.Controllers;

[Authorize]
[Route("organizacoes/{tenantId:guid}/caixa")]
public sealed class InboxController(OdcaApiClient api) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(
        Guid tenantId,
        string scope = "mine",
        string? kind = null,
        string? urgency = null,
        Guid? contractId = null,
        Guid? ownerId = null,
        Guid? viewId = null,
        int page = 1,
        CancellationToken ct = default)
    {
        ViewData["Title"] = "Caixa operacional";
        ViewData["TenantId"] = tenantId;
        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null) return Challenge();

        var savedViewsResult = await api.GetSavedViewsAsync(token, tenantId, "inbox", ct);
        var views = savedViewsResult.Succeeded ? savedViewsResult.Value! : [];
        var currentView = viewId.HasValue ? views.FirstOrDefault(x => x.Id == viewId) : null;

        if (!viewId.HasValue && !HasExplicitFilters(Request.Query) && views.FirstOrDefault(x => x.IsDefault) is { } defaultView)
        {
            return RedirectToAction(nameof(OpenView), new { tenantId, viewId = defaultView.Id });
        }

        var result = await api.GetInboxAsync(token, tenantId, scope, kind, urgency, contractId, ownerId, page, 20, ct);
        if (result.Status == ApiCallStatus.Unauthorized) return Challenge();
        if (result.Status == ApiCallStatus.Forbidden) return Forbid();

        return View(new InboxWorkspaceViewModel
        {
            Page = result.Value ?? new([], page, 20, 0, 0, 0, 0),
            Scope = scope,
            Kind = kind,
            Urgency = urgency,
            Views = views,
            CurrentView = currentView,
            Error = result.Succeeded ? null : result.UserMessage("Não foi possível abrir a caixa operacional.")
        });
    }

    [HttpPost("vistas")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveView(
        Guid tenantId,
        string name,
        string scope = "mine",
        string? kind = null,
        string? urgency = null,
        string? relativeDate = null,
        Guid? contractId = null,
        Guid? ownerId = null,
        CancellationToken ct = default)
    {
        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null) return Challenge();

        var filters = new Dictionary<string, string>();
        Add(filters, "scope", scope);
        Add(filters, "kind", kind);
        Add(filters, "urgency", urgency);
        Add(filters, "relativeDate", relativeDate);
        Add(filters, "contractId", contractId?.ToString());
        Add(filters, "ownerId", ownerId?.ToString());

        var result = await api.CreateSavedViewAsync(token, tenantId, new SaveViewRequest(name, "inbox", filters, "due_date"), ct);
        if (result.Status == ApiCallStatus.Unauthorized) return Challenge();
        if (result.Status == ApiCallStatus.Forbidden) return Forbid();

        TempData[result.Succeeded ? "SavedViewNotice" : "SavedViewError"] = result.Succeeded
            ? "Vista pessoal salva para a caixa operacional."
            : "Não foi possível salvar a vista da caixa. Confira o nome e os filtros.";

        var savedViewId = result.Succeeded ? result.Value?.Id : null;
        return RedirectToAction(nameof(Index), new { tenantId, scope, kind, urgency, contractId, ownerId, viewId = savedViewId });
    }

    [HttpGet("vistas/{viewId:guid}")]
    public async Task<IActionResult> OpenView(Guid tenantId, Guid viewId, CancellationToken ct)
    {
        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null) return Challenge();

        var result = await api.GetSavedViewAsync(token, tenantId, viewId, ct);
        if (result.Status == ApiCallStatus.Unauthorized) return Challenge();
        if (result.Status == ApiCallStatus.Forbidden) return Forbid();
        if (result.Status == ApiCallStatus.NotFound) return NotFound();
        if (!result.Succeeded || result.Value is null)
        {
            TempData["SavedViewError"] = "Esta vista não está disponível.";
            return RedirectToAction(nameof(Index), new { tenantId });
        }

        var route = BuildSavedViewRoute(tenantId, result.Value);
        return RedirectToAction(nameof(Index), route);
    }

    [HttpPost("vistas/{viewId:guid}/padrao")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetDefaultView(Guid tenantId, Guid viewId, [FromForm] long? version = null, CancellationToken ct = default)
    {
        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null) return Challenge();

        var viewResult = await api.GetSavedViewAsync(token, tenantId, viewId, ct);
        if (viewResult.Status == ApiCallStatus.Unauthorized) return Challenge();
        if (viewResult.Status == ApiCallStatus.Forbidden) return Forbid();
        if (viewResult.Status == ApiCallStatus.NotFound || !viewResult.Succeeded || viewResult.Value is null)
            return NotFound();

        var targetVersion = version ?? viewResult.Value.Version;
        var updateRequest = new UpdateSavedViewRequest(
            viewResult.Value.Name,
            IsDefault: true,
            Version: targetVersion);

        var result = await api.UpdateSavedViewAsync(token, tenantId, viewId, updateRequest, ct);
        if (result.Status == ApiCallStatus.Unauthorized) return Challenge();
        if (result.Status == ApiCallStatus.Forbidden) return Forbid();
        if (result.Status == ApiCallStatus.NotFound) return NotFound();
        if (result.Status == ApiCallStatus.Conflict)
        {
            TempData["SavedViewError"] = "A vista foi alterada em outra sessão (versão divergente).";
            return StatusCode(409);
        }

        TempData[result.Succeeded ? "SavedViewNotice" : "SavedViewError"] = result.Succeeded
            ? "Vista definida como padrão para a caixa operacional."
            : "Não foi possível definir a vista como padrão.";

        return RedirectToAction(nameof(OpenView), new { tenantId, viewId });
    }

    [HttpPost("vistas/{viewId:guid}/inativar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeactivateView(Guid tenantId, Guid viewId, CancellationToken ct)
    {
        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null) return Challenge();

        var result = await api.DeactivateSavedViewAsync(token, tenantId, viewId, ct);
        if (result.Status == ApiCallStatus.Unauthorized) return Challenge();
        if (result.Status == ApiCallStatus.Forbidden) return Forbid();
        if (result.Status == ApiCallStatus.NotFound) return NotFound();

        TempData[result.Succeeded ? "SavedViewNotice" : "SavedViewError"] = result.Succeeded
            ? "Vista inativada."
            : "Não foi possível inativar a vista.";

        return RedirectToAction(nameof(Index), new { tenantId });
    }

    public static RouteValueDictionary BuildSavedViewRoute(Guid tenantId, SavedViewItem view)
    {
        var route = new RouteValueDictionary();
        if (!SavedViewFilterPolicy.TryNormalize(view.ListingType, view.Filters, view.Sort, out var filters, out _, out _))
            return new RouteValueDictionary { ["tenantId"] = tenantId };

        foreach (var filter in filters)
        {
            if (SavedViewFilterPolicy.IsAllowedRouteKey(view.ListingType, filter.Key))
                route[filter.Key] = filter.Value;
        }

        route["tenantId"] = tenantId;
        route["viewId"] = view.Id;
        route["page"] = 1;
        return route;
    }

    private static bool HasExplicitFilters(Microsoft.AspNetCore.Http.IQueryCollection query) =>
        query.Keys.Any(key => SavedViewFilterPolicy.IsAllowedRouteKey("inbox", key));

    private static void Add(Dictionary<string, string> filters, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) filters[key] = value;
    }
}
