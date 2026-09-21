using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odca.Web.Models;
using Odca.Web.Services;

namespace Odca.Web.Controllers;

[Authorize]
[Route("organizacoes/{tenantId:guid}/renovacoes")]
public sealed class RenewalsController(OdcaApiClient api) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(Guid tenantId,DateOnly? from=null,DateOnly? to=null,Guid? ownerId=null,string? contractType=null,string? counterparty=null,string? status=null,bool mine=false,bool withoutOwner=false,int page=1,CancellationToken ct=default)
    {
        if(from.HasValue&&to.HasValue&&from>to){ModelState.AddModelError(nameof(to),"O fim do período deve ser posterior ao início.");Response.StatusCode=400;}
        var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Challenge();
        var result=await api.GetRenewalsAsync(token,tenantId,from,to,ownerId,contractType,counterparty,status,mine,withoutOwner,page,20,ct);
        if (result.Status == ApiCallStatus.Unauthorized) return Challenge();
        if (result.Status == ApiCallStatus.Forbidden) return Forbid();
        if (!result.Succeeded || result.Value is null) { Response.StatusCode = 503; return View("ServiceUnavailable"); }
        ViewData["TenantId"] = tenantId;
        var today = result.Value.Today ?? DateOnly.FromDateTime(DateTime.Today);
        return View(new RenewalWorkspaceViewModel(result.Value, today));
    }

    [HttpPost("{requestId:guid}/aplicar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Apply(Guid tenantId, Guid requestId, long rowVersion, CancellationToken ct = default)
    {
        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null) return Challenge();

        var result = await api.ApplyRenewalAsync(token, tenantId, requestId, new Odca.Contracts.Renewals.ApplyRenewalRequest(rowVersion), ct);
        if (result.Status == ApiCallStatus.Unauthorized) return Challenge();
        if (result.Status == ApiCallStatus.Forbidden) return Forbid();

        if (result.Status == ApiCallStatus.Conflict)
        {
            TempData["Conflict"] = "O registro mudou. Recarregue.";
            return RedirectToAction(nameof(Index), new { tenantId });
        }

        if (result.Succeeded)
        {
            TempData["Success"] = result.Value?.EndsOn is { } endsOn
                ? $"Alteração aplicada com sucesso. Nova vigência até {endsOn:dd/MM/yyyy}."
                : "Alteração de vigência aplicada com sucesso.";
        }
        else
        {
            TempData["Error"] = result.UserMessage("Não foi possível aplicar a alteração de vigência.");
        }

        return RedirectToAction(nameof(Index), new { tenantId });
    }
}
