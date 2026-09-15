using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odca.Contracts.Studio;
using Odca.Web.Services;

namespace Odca.Web.Controllers;

[Authorize]
[Route("organizacoes/{tenantId:guid}/estudio")]
public sealed class StudioController(OdcaApiClient api) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(Guid tenantId,string? search,int page=1,CancellationToken ct=default)
    {
        var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Challenge();
        var result=await api.GetStudioTemplatesAsync(token,tenantId,search,page,ct);
        if(!result.Succeeded)return result.Status==ApiCallStatus.Forbidden?Forbid():View("~/Views/Shared/ServiceUnavailable.cshtml");
        ViewData["Title"]="Modelos de contrato";ViewData["TenantId"]=tenantId;ViewData["Search"]=search;return View(result.Value!);
    }
    [HttpPost("minutas")]
    public async Task<IActionResult> Create(Guid tenantId,Guid templateId,string title,string? reference,CancellationToken ct)
    {
        var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Challenge();var result=await api.CreateStudioDraftAsync(token,tenantId,new(templateId,title,reference),ct);
        if(!result.Succeeded){TempData["StudioError"]=result.ErrorDetail??result.ErrorTitle??"Não foi possível criar a minuta.";return RedirectToAction(nameof(Index),new{tenantId});}
        var id=result.Value.GetProperty("id").GetGuid();return RedirectToAction(nameof(Edit),new{tenantId,draftId=id});
    }
    [HttpGet("minutas/{draftId:guid}")]
    public async Task<IActionResult> Edit(Guid tenantId,Guid draftId,CancellationToken ct)
    {
        var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Challenge();var result=await api.GetStudioDraftAsync(token,tenantId,draftId,ct);
        if(!result.Succeeded)return result.Status==ApiCallStatus.NotFound?NotFound():View("~/Views/Shared/ServiceUnavailable.cshtml");ViewData["Title"]="Estúdio de contratos";ViewData["TenantId"]=tenantId;return View(result.Value);
    }
    [HttpPut("minutas/{draftId:guid}")]
    public async Task<IActionResult> Save(Guid tenantId,Guid draftId,[FromBody] SaveDraftRequest request,CancellationToken ct)
    {var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Unauthorized();var r=await api.SaveStudioDraftAsync(token,tenantId,draftId,request,ct);return r.Succeeded?Json(r.Value):StatusCode(r.Status==ApiCallStatus.Conflict?409:422,new{title=r.ErrorTitle,detail=r.ErrorDetail});}
    [HttpPost("minutas/{draftId:guid}/gerar")]
    public async Task<IActionResult> Generate(Guid tenantId,Guid draftId,CancellationToken ct)
    {var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Challenge();var r=await api.GenerateStudioVersionAsync(token,tenantId,draftId,ct);TempData[r.Succeeded?"StudioSuccess":"StudioError"]=r.Succeeded?$"Versão {r.Value!.Number} gerada com hash {r.Value.Sha256[..12]}…":r.ErrorDetail??"Preencha e confirme os campos obrigatórios.";return RedirectToAction(nameof(Edit),new{tenantId,draftId});}
}
