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
        var route = new RouteValueDictionary(result.Value.Filters) { ["tenantId"] = tenantId };
        return RedirectToAction(nameof(Index), route);
    }

    [HttpPost("vistas/{viewId:guid}/atualizar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateView(Guid tenantId, Guid viewId, string name, bool isDefault, long version, CancellationToken ct)
    {
        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null) return Challenge();
        var result = await api.UpdateSavedViewAsync(token, tenantId, viewId, new UpdateSavedViewRequest(name, isDefault, version), ct);
        if (result.Status == ApiCallStatus.Unauthorized) return Challenge();
        if (result.Status == ApiCallStatus.Forbidden) return Forbid();
        TempData[result.Succeeded ? "SavedViewNotice" : "SavedViewError"] = result.Succeeded ? "Vista atualizada." : "A vista mudou em outra sessão. Atualize a página e tente novamente.";
        return RedirectToAction(nameof(Index), new { tenantId });
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
    public async Task<IActionResult> Index(Guid tenantId,string scope="mine",Guid? contractId=null,Guid? ownerId=null,string? category=null,string? status=null,DateOnly? from=null,DateOnly? to=null,string? search=null,int page=1,CancellationToken ct=default)
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
        var result=await api.GetObligationsAsync(token,tenantId,scope,contractId,ownerId,category,status,from,to,search,page,20,ct);
        if(result.Status==ApiCallStatus.Unauthorized)return Challenge();if(result.Status==ApiCallStatus.Forbidden)return Forbid();
        if(!result.Succeeded){Response.StatusCode=503;return View("ServiceUnavailable");}
        var savedViews = await api.GetSavedViewsAsync(token, tenantId, "obligations", ct);
        ViewData["TenantId"]=tenantId;ViewData["Scope"]=scope;
        return View(new ObligationWorkspaceViewModel(result.Value!, savedViews.Succeeded ? savedViews.Value! : []));
    }

    private static void Add(IDictionary<string, string> filters, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) filters[key] = value;
    }
}
