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
    public async Task<IActionResult> Index(Guid tenantId,string? search,string? type,string? scope,int page=1,CancellationToken ct=default)
    {
        var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Challenge();
        ViewData["Title"]="Modelos de contrato";ViewData["TenantId"]=tenantId;ViewData["Search"]=search;ViewData["Type"]=type;ViewData["Scope"]=scope;
        var result=await api.GetStudioTemplatesAsync(token,tenantId,search,type,scope,page,ct);
        if(!result.Succeeded){if(result.Status==ApiCallStatus.Forbidden)return Forbid();ViewData["LoadError"]=result.UserMessage("Não foi possível carregar o catálogo.");return View(new TemplateCatalogPage([],Math.Max(1,page),12,0));}
        return View(result.Value!);
    }
    [HttpPost("biblioteca-oficial")]
    public async Task<IActionResult> InstallOfficial(Guid tenantId,string? search,string? type,string? scope,CancellationToken ct)
    {
        var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Challenge();
        var result=await api.InstallOfficialStudioTemplatesAsync(token,tenantId,ct);
        TempData[result.Succeeded?"StudioSuccess":"StudioError"]=result.Succeeded
            ? (result.Value!.Installed==0?$"Os {result.Value.AlreadyPresent} modelos oficiais já estavam disponíveis.":$"{result.Value.Installed} modelo(s) oficial(is) publicado(s) para esta organização.")
            : result.ErrorDetail??result.ErrorTitle??"Não foi possível instalar a biblioteca oficial.";
        return RedirectToAction(nameof(Index),new{tenantId,search,type,scope});
    }
    [HttpPost("modelos/{templateId:guid}/duplicar")]
    public async Task<IActionResult> Duplicate(Guid tenantId,Guid templateId,string? search,string? type,string? scope,CancellationToken ct)
    {
        var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Challenge();
        var result=await api.DuplicateStudioTemplateAsync(token,tenantId,templateId,ct);
        TempData[result.Succeeded?"StudioSuccess":"StudioError"]=result.Succeeded
            ?"Cópia particular criada como rascunho. Publique-a depois de revisar o conteúdo."
            :result.ErrorDetail??result.ErrorTitle??"Não foi possível duplicar o modelo.";
        return RedirectToAction(nameof(Index),new{tenantId,search,type,scope});
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
    [HttpPost("minutas/{draftId:guid}/gerar-json")]
    public async Task<IActionResult> GenerateJson(Guid tenantId,Guid draftId,[FromBody]GenerateVersionRequest request,CancellationToken ct){var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Unauthorized();var r=await api.GenerateStudioVersionAsync(token,tenantId,draftId,request,ct);return r.Succeeded?Json(r.Value):StatusCode(r.Status==ApiCallStatus.Conflict?409:422,new{title=r.ErrorTitle,detail=r.ErrorDetail});}
    [HttpGet("revisores")]
    public async Task<IActionResult> Reviewers(Guid tenantId,CancellationToken ct){var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Unauthorized();var r=await api.GetStudioReviewersAsync(token,tenantId,ct);return r.Succeeded?Json(r.Value):StatusCode(422,new{title=r.ErrorTitle});}
    [HttpPost("revisoes")]
    public async Task<IActionResult> SubmitReview(Guid tenantId,[FromBody]SubmitReviewRequest request,CancellationToken ct){var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Unauthorized();var r=await api.SubmitStudioReviewAsync(token,tenantId,request,ct);return r.Succeeded?Json(r.Value):StatusCode(r.Status==ApiCallStatus.Conflict?409:422,new{title=r.ErrorTitle,detail=r.ErrorDetail});}

    [HttpGet("minutas/{draftId:guid}/versoes")]
    public async Task<IActionResult> Versions(Guid tenantId,Guid draftId,CancellationToken ct){var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Unauthorized();var r=await api.GetStudioVersionsAsync(token,tenantId,draftId,ct);return r.Succeeded?Json(r.Value):StatusCode(422,new{title=r.ErrorTitle});}
    [HttpGet("comparar")]
    public async Task<IActionResult> Compare(Guid tenantId,Guid before,Guid after,CancellationToken ct){var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Unauthorized();var r=await api.CompareStudioVersionsAsync(token,tenantId,before,after,ct);return r.Succeeded?Json(r.Value):StatusCode(r.Status==ApiCallStatus.NotFound?404:422,new{title=r.ErrorTitle,detail=r.ErrorDetail});}
    [HttpGet("minutas/{draftId:guid}/checklist")]
    public async Task<IActionResult> Checklist(Guid tenantId,Guid draftId,long version,CancellationToken ct){var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Unauthorized();var r=await api.GetStudioChecklistAsync(token,tenantId,draftId,version,ct);return r.Succeeded?Json(r.Value):StatusCode(422,new{title=r.ErrorTitle});}
    [HttpGet("minutas/{draftId:guid}/comentarios")]
    public async Task<IActionResult> Comments(Guid tenantId,Guid draftId,bool resolved,CancellationToken ct){var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Unauthorized();var r=await api.GetStudioCommentsAsync(token,tenantId,draftId,resolved,ct);return r.Succeeded?Json(r.Value):StatusCode(422,new{title=r.ErrorTitle});}
    [HttpPost("minutas/{draftId:guid}/comentarios")]
    public async Task<IActionResult> AddComment(Guid tenantId,Guid draftId,[FromBody]CreateStudioCommentRequest request,CancellationToken ct){var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Unauthorized();var r=await api.AddStudioCommentAsync(token,tenantId,draftId,request,ct);return r.Succeeded?Json(r.Value):StatusCode(422,new{title=r.ErrorTitle,detail=r.ErrorDetail});}
    [HttpPost("minutas/{draftId:guid}/comentarios/{commentId:guid}/resolve")]
    public Task<IActionResult> ResolveCommentAsync(Guid tenantId,Guid draftId,Guid commentId,[FromBody]ChangeStudioCommentStateRequest request,CancellationToken ct)=>ChangeCommentStateAsync(tenantId,draftId,commentId,"resolve",request,ct);
    [HttpPost("minutas/{draftId:guid}/comentarios/{commentId:guid}/reopen")]
    public Task<IActionResult> ReopenCommentAsync(Guid tenantId,Guid draftId,Guid commentId,[FromBody]ChangeStudioCommentStateRequest request,CancellationToken ct)=>ChangeCommentStateAsync(tenantId,draftId,commentId,"reopen",request,ct);
    private async Task<IActionResult> ChangeCommentStateAsync(Guid tenantId,Guid draftId,Guid commentId,string operation,ChangeStudioCommentStateRequest request,CancellationToken ct){var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Unauthorized();var r=await api.SetStudioCommentStateAsync(token,tenantId,draftId,commentId,operation,request,ct);return r.Succeeded?NoContent():StatusCode(r.Status==ApiCallStatus.Conflict?409:422,new{title=r.ErrorTitle,detail=r.ErrorDetail});}
}
