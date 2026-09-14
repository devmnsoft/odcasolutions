using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Odca.Web.Services;
using Odca.Contracts.SavedViews;
using Odca.Web.Models;

namespace Odca.Web.Controllers;

[Authorize]
[Route("organizacoes/{tenantId:guid}/obrigacoes")]
public sealed class ObligationsController(OdcaApiClient api) : Controller
{
    [HttpPost("vistas")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveView(Guid tenantId, string name, string scope = "mine", Guid? contractId = null, Guid? ownerId = null, string? category = null, string? status = null, DateOnly? from = null, DateOnly? to = null, string? search = null, CancellationToken ct = default)
    {
        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null) return Challenge();
        var filters = new Dictionary<string, string>();
        Add(filters, "scope", scope); Add(filters, "contractId", contractId?.ToString()); Add(filters, "ownerId", ownerId?.ToString());
        Add(filters, "category", category); Add(filters, "status", status); Add(filters, "from", from?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        Add(filters, "to", to?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)); Add(filters, "search", search);
        var result = await api.CreateSavedViewAsync(token, tenantId, new SaveViewRequest(name, "obligations", filters, "due_date"), ct);
        if (result.Status == ApiCallStatus.Unauthorized) return Challenge();
        if (result.Status == ApiCallStatus.Forbidden) return Forbid();
        TempData[result.Succeeded ? "SavedViewNotice" : "SavedViewError"] = result.Succeeded
            ? "Vista pessoal salva. Os resultados serão sempre recalculados com suas permissões atuais."
            : "Não foi possível salvar a vista. Confira o nome e os filtros.";
        return RedirectToAction(nameof(Index), new { tenantId, scope, contractId, ownerId, category, status, from, to, search });
    }

    [HttpGet("vistas/{viewId:guid}")]
    public async Task<IActionResult> OpenView(Guid tenantId, Guid viewId, CancellationToken ct)
    {
        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null) return Challenge();
        var result = await api.GetSavedViewAsync(token, tenantId, viewId, ct);
        if (result.Status == ApiCallStatus.Unauthorized) return Challenge();
        if (result.Status == ApiCallStatus.Forbidden) return Forbid();
        if (!result.Succeeded || result.Value is null)
        {
            TempData["SavedViewError"] = "Esta vista não está disponível ou contém referências que precisam ser atualizadas.";
            return RedirectToAction(nameof(Index), new { tenantId });
        }
        var route = BuildSavedViewRoute(tenantId, result.Value);
        return RedirectToAction(nameof(Index), route);
    }

    [HttpPost("vistas/{viewId:guid}/atualizar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateView(Guid tenantId, Guid viewId, string name, bool isDefault, long version, bool replaceFilters = false, string scope = "mine", Guid? contractId = null, Guid? ownerId = null, string? category = null, string? status = null, DateOnly? from = null, DateOnly? to = null, string? search = null, CancellationToken ct = default)
    {
        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null) return Challenge();
        Dictionary<string, string>? filters = null;
        if (replaceFilters)
        {
            filters = BuildFilters(scope, contractId, ownerId, category, status, from, to, search);
        }
        var result = await api.UpdateSavedViewAsync(token, tenantId, viewId, new UpdateSavedViewRequest(name, isDefault, version, filters), ct);
        if (result.Status == ApiCallStatus.Unauthorized) return Challenge();
        if (result.Status == ApiCallStatus.Forbidden) return Forbid();
        TempData[result.Succeeded ? "SavedViewNotice" : "SavedViewError"] = result.Succeeded ? "Vista atualizada." : "A vista mudou em outra sessão. Atualize a página e tente novamente.";
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
        TempData[result.Succeeded ? "SavedViewNotice" : "SavedViewError"] = result.Succeeded ? "Vista inativada." : "Não foi possível inativar a vista.";
        return RedirectToAction(nameof(Index), new { tenantId });
    }

    [HttpGet("{id:guid}/historico")]
    public async Task<IActionResult> History(Guid tenantId, Guid id, CancellationToken ct)
    {
        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null) return Unauthorized();
        var result = await api.GetObligationHistoryAsync(token, tenantId, id, ct);
        return result.Status switch
        {
            ApiCallStatus.Unauthorized => Unauthorized(),
            ApiCallStatus.Forbidden => Forbid(),
            ApiCallStatus.NotFound => NotFound(),
            _ when result.Succeeded => Json(result.Value),
            _ => StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "Não foi possível consultar o histórico agora." })
        };
    }

    [HttpGet]
    public async Task<IActionResult> Index(Guid tenantId,string scope="mine",Guid? contractId=null,Guid? ownerId=null,string? category=null,string? status=null,DateOnly? from=null,DateOnly? to=null,string? search=null,Guid? viewId=null,int page=1,CancellationToken ct=default)
    {
        if (from.HasValue && to.HasValue && from.Value > to.Value)
        {
            ModelState.AddModelError(nameof(to), "A data final deve ser igual ou posterior à data inicial.");
            ViewData["TenantId"] = tenantId;
            ViewData["Scope"] = scope;
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return View(new ObligationWorkspaceViewModel(new Odca.Contracts.Obligations.ObligationPage([], 1, 20, 0), []));
        }
        var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Challenge();
        var savedViews = await api.GetSavedViewsAsync(token, tenantId, "obligations", ct);
        var views = savedViews.Succeeded ? savedViews.Value! : [];
        var currentView = viewId.HasValue ? views.FirstOrDefault(x => x.Id == viewId) : null;
        // Explicit query filters (including dashboard links) always win over the personal default.
        if (!viewId.HasValue && !HasExplicitFilters(Request.Query) && views.FirstOrDefault(x => x.IsDefault) is { } defaultView)
            return RedirectToAction(nameof(OpenView), new { tenantId, viewId = defaultView.Id });
        var result=await api.GetObligationsAsync(token,tenantId,scope,contractId,ownerId,category,status,from,to,search,page,20,ct);
        if(result.Status==ApiCallStatus.Unauthorized)return Challenge();if(result.Status==ApiCallStatus.Forbidden)return Forbid();
        if(!result.Succeeded){Response.StatusCode=503;return View("ServiceUnavailable");}
        ViewData["TenantId"]=tenantId;ViewData["Scope"]=scope;
        var currentFilters = BuildFilters(scope, contractId, ownerId, category, status, from, to, search);
        var modified = currentView is not null && !FiltersEqual(currentView.Filters, currentFilters);
        return View(new ObligationWorkspaceViewModel(result.Value!, views, currentView, modified));
    }

    public static RouteValueDictionary BuildSavedViewRoute(Guid tenantId, SavedViewItem view)
    {
        var route = new RouteValueDictionary();
        if (!SavedViewFilterPolicy.TryNormalize(view.ListingType, view.Filters, view.Sort, out var filters, out _, out _))
            return new RouteValueDictionary { ["tenantId"] = tenantId };
        foreach (var filter in filters)
        {
            if (SavedViewFilterPolicy.IsAllowedRouteKey(view.ListingType, filter.Key)) route[filter.Key] = filter.Value;
        }
        route["tenantId"] = tenantId;
        route["viewId"] = view.Id;
        route["page"] = 1;
        return route;
    }

    private static Dictionary<string, string> BuildFilters(string scope, Guid? contractId, Guid? ownerId, string? category, string? status, DateOnly? from, DateOnly? to, string? search)
    {
        var filters = new Dictionary<string, string>();
        Add(filters, "scope", scope); Add(filters, "contractId", contractId?.ToString()); Add(filters, "ownerId", ownerId?.ToString());
        Add(filters, "category", category); Add(filters, "status", status); Add(filters, "from", from?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        Add(filters, "to", to?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)); Add(filters, "search", search);
        return filters;
    }

    private static bool HasExplicitFilters(Microsoft.AspNetCore.Http.IQueryCollection query) =>
        query.Keys.Any(key => SavedViewFilterPolicy.IsAllowedRouteKey("obligations", key));

    private static bool FiltersEqual(IReadOnlyDictionary<string, string> left, Dictionary<string, string> right) =>
        left.Count == right.Count && left.All(pair => right.TryGetValue(pair.Key, out var value) && value == pair.Value);

    private static void Add(Dictionary<string, string> filters, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) filters[key] = value;
    }
}
