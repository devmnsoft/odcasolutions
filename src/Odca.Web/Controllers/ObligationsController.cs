using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odca.Web.Services;

namespace Odca.Web.Controllers;

[Authorize]
[Route("organizacoes/{tenantId:guid}/obrigacoes")]
public sealed class ObligationsController(OdcaApiClient api) : Controller
{
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
            return View(new Odca.Contracts.Obligations.ObligationPage([], 1, 20, 0));
        }
        var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Challenge();
        var result=await api.GetObligationsAsync(token,tenantId,scope,contractId,ownerId,category,status,from,to,search,page,20,ct);
        if(result.Status==ApiCallStatus.Unauthorized)return Challenge();if(result.Status==ApiCallStatus.Forbidden)return Forbid();
        if(!result.Succeeded){Response.StatusCode=503;return View("ServiceUnavailable");}
        ViewData["TenantId"]=tenantId;ViewData["Scope"]=scope;return View(result.Value);
    }
}
