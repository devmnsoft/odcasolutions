using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odca.Contracts.Studio;
using Odca.Web.Services;

namespace Odca.Web.Controllers;

[Authorize]
[Route("organizacoes/{tenantId:guid}/estudio")]
public sealed class StudioController(OdcaApiClient api, IConfiguration configuration) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(Guid tenantId,string? search,string? type,string? scope,Guid? patientId,int page=1,CancellationToken ct=default)
    {
        var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Challenge();
        ViewData["Title"]="Modelos de contrato";ViewData["TenantId"]=tenantId;ViewData["Search"]=search;ViewData["Type"]=type;ViewData["Scope"]=scope;ViewData["PatientId"]=patientId;
        var result=await GetCatalogAsync(token,tenantId,search,type,scope,page,ct);
        if(!result.Succeeded){if(result.Status==ApiCallStatus.Forbidden)return Forbid();ViewData["LoadError"]=result.UserMessage("Não foi possível carregar o catálogo.");return View(new TemplateCatalogPage([],Math.Max(1,page),12,0));}
        return View(result.Value!);
    }
    [HttpPost("biblioteca-oficial")]
    public async Task<IActionResult> InstallOfficial(Guid tenantId,string? search,string? type,string? scope,CancellationToken ct)
    {
        var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Challenge();
        var result=await SendCatalogAsync<OfficialTemplateInstallResponse>(HttpMethod.Post,$"api/v1/organizations/{tenantId}/studio/templates/official",token,ct);
        TempData[result.Succeeded?"StudioSuccess":"StudioError"]=result.Succeeded
            ? (result.Value!.Installed==0?$"Os {result.Value.AlreadyPresent} modelos oficiais já estavam disponíveis.":$"{result.Value.Installed} modelo(s) oficial(is) publicado(s) para esta organização.")
            : result.ErrorDetail??result.ErrorTitle??"Não foi possível instalar a biblioteca oficial.";
        return RedirectToAction(nameof(Index),new{tenantId,search,type,scope});
    }
    [HttpPost("modelos/{templateId:guid}/duplicar")]
    public async Task<IActionResult> Duplicate(Guid tenantId,Guid templateId,string? search,string? type,string? scope,CancellationToken ct)
    {
        var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Challenge();
        var result=await SendCatalogAsync<TemplateMutationResponse>(HttpMethod.Post,$"api/v1/organizations/{tenantId}/studio/templates/{templateId}/duplicate",token,ct);
        TempData[result.Succeeded?"StudioSuccess":"StudioError"]=result.Succeeded
            ?"Cópia particular criada como rascunho. Publique-a depois de revisar o conteúdo."
            :result.ErrorDetail??result.ErrorTitle??"Não foi possível duplicar o modelo.";
        return RedirectToAction(nameof(Index),new{tenantId,search,type,scope});
    }
    [HttpPost("minutas")]
    public async Task<IActionResult> Create(Guid tenantId,Guid templateId,string title,string? reference,Guid? patientId,CancellationToken ct)
    {
        var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Challenge();var result=await api.CreateStudioDraftAsync(token,tenantId,new(templateId,title,reference,patientId),ct);
        if(!result.Succeeded){TempData["StudioError"]=result.ErrorDetail??result.ErrorTitle??"Não foi possível criar a minuta.";return RedirectToAction(nameof(Index),new{tenantId,patientId});}
        var id=result.Value.GetProperty("id").GetGuid();return RedirectToAction(nameof(Edit),new{tenantId,draftId=id});
    }
    [HttpGet("minutas/{draftId:guid}")]
    public async Task<IActionResult> Edit(Guid tenantId,Guid draftId,CancellationToken ct)
    {
        var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Challenge();var result=await api.GetStudioDraftAsync(token,tenantId,draftId,ct);
        if(!result.Succeeded)return result.Status==ApiCallStatus.NotFound?NotFound():View("~/Views/Shared/ServiceUnavailable.cshtml");
        var conference=await api.GetDocumentConferenceAsync(token,tenantId,draftId,ct);
        ViewData["Title"]="Estúdio de contratos";ViewData["TenantId"]=tenantId;
        ViewData["Conference"]=conference.Value;ViewData["ConferenceError"]=conference.Succeeded?null:conference.UserMessage("Não foi possível carregar a conferência documental.");
        return View(result.Value);
    }
    [HttpPost("minutas/{draftId:guid}/confirmar-paciente")]
    public async Task<IActionResult> ConfirmPatient(Guid tenantId,Guid draftId,long expectedDraftVersion,long expectedPatientVersion,CancellationToken ct)
    {
        var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Challenge();
        var result=await api.ConfirmDraftPatientAsync(token,tenantId,draftId,new(expectedDraftVersion,expectedPatientVersion),ct);
        TempData[result.Succeeded?"StudioSuccess":"StudioError"]=result.Succeeded
            ?"Dados cadastrais reconferidos. A minuta foi preservada e as pendências foram recalculadas."
            :result.UserMessage("O cadastro ou a minuta mudou novamente. Refaça a conferência.");
        return RedirectToAction(nameof(Edit),new{tenantId,draftId});
    }
    [HttpPut("minutas/{draftId:guid}")]
    public async Task<IActionResult> Save(Guid tenantId,Guid draftId,[FromBody] SaveDraftRequest request,CancellationToken ct)
    {var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Unauthorized();var r=await api.SaveStudioDraftAsync(token,tenantId,draftId,request,ct);return r.Succeeded?Json(r.Value):StatusCode(r.Status==ApiCallStatus.Conflict?409:422,new{title=r.ErrorTitle,detail=r.ErrorDetail});}
    [HttpPost("minutas/{draftId:guid}/gerar")]
    public async Task<IActionResult> Generate(Guid tenantId,Guid draftId,long expectedVersion,Guid idempotencyKey,CancellationToken ct)
    {var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Challenge();var r=await api.GenerateStudioVersionAsync(token,tenantId,draftId,new(idempotencyKey,expectedVersion),ct);TempData[r.Succeeded?"StudioSuccess":"StudioError"]=r.Succeeded?$"Versão {r.Value!.Number} gerada com hash {r.Value.Sha256[..12]}…":r.ErrorDetail??"Preencha e confirme os campos obrigatórios.";return RedirectToAction(nameof(Edit),new{tenantId,draftId});}
    [HttpGet("versoes/{versionId:guid}")]
    public async Task<IActionResult> Version(Guid tenantId,Guid versionId,CancellationToken ct)
    {var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Challenge();var r=await api.GetStudioVersionAsync(token,tenantId,versionId,ct);if(r.Status==ApiCallStatus.Forbidden)return Forbid();if(r.Status==ApiCallStatus.NotFound)return NotFound();if(!r.Succeeded)return View("~/Views/Shared/ServiceUnavailable.cshtml");var reviewers=await api.GetStudioReviewersAsync(token,tenantId,ct);ViewData["Title"]=$"{r.Value!.Title} · versão {r.Value.Number}";ViewData["TenantId"]=tenantId;ViewData["Reviewers"]=reviewers.Succeeded?reviewers.Value:Array.Empty<StudioReviewerItem>();ViewData["ReviewersError"]=reviewers.Succeeded?null:reviewers.UserMessage("Falha ao consultar responsáveis. Tente novamente.");return View(r.Value);}
    [HttpPost("versoes/{versionId:guid}/pdf")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GeneratePdf(Guid tenantId,Guid versionId,CancellationToken ct){var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Challenge();var r=await api.GenerateStudioPdfAsync(token,tenantId,versionId,ct);TempData[r.Succeeded?"StudioSuccess":"StudioError"]=r.Succeeded?"PDF final gerado e protegido no armazenamento privado.":r.UserMessage("Não foi possível gerar o PDF.");return RedirectToAction(nameof(Version),new{tenantId,versionId});}
    [HttpGet("versoes/{versionId:guid}/pdf")]
    public async Task<IActionResult> DownloadPdf(Guid tenantId,Guid versionId,CancellationToken ct){var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Challenge();using var response=await api.DownloadStudioPdfAsync(token,tenantId,versionId,ct);if(response.StatusCode==System.Net.HttpStatusCode.Forbidden)return Forbid();if(!response.IsSuccessStatusCode)return response.StatusCode==System.Net.HttpStatusCode.NotFound?NotFound():StatusCode((int)response.StatusCode);var bytes=await response.Content.ReadAsByteArrayAsync(ct);return File(bytes,"application/pdf",response.Content.Headers.ContentDisposition?.FileNameStar??"documento.pdf");}
    [HttpPost("versoes/{versionId:guid}/participantes")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveParticipants(Guid tenantId,Guid versionId,long expectedVersion,bool confirm,List<SignatureParticipantInput> participants,Guid? operationId,CancellationToken ct){var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Challenge();var r=await api.SaveSignaturePreparationAsync(token,tenantId,versionId,new(expectedVersion,confirm,participants,operationId),ct);TempData[r.Succeeded?"StudioSuccess":"StudioError"]=r.Succeeded?(confirm?"Composição conferida. Nenhum envio ou assinatura foi realizado.":"Preparação salva para continuar depois."):r.UserMessage("Não foi possível salvar a preparação.");return RedirectToAction(nameof(Version),new{tenantId,versionId});}
    [HttpPost("versoes/{versionId:guid}/participantes/reabrir")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReopenParticipants(Guid tenantId,Guid versionId,long expectedVersion,string justification,Guid operationId,CancellationToken ct){var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Challenge();var r=await api.ReopenSignaturePreparationAsync(token,tenantId,versionId,new(expectedVersion,justification,operationId),ct);TempData[r.Succeeded?"StudioSuccess":"StudioError"]=r.Succeeded?"Preparação reaberta. A confirmação anterior foi preservada no histórico e uma nova conferência será necessária.":r.UserMessage("Não foi possível reabrir a preparação.");return RedirectToAction(nameof(Version),new{tenantId,versionId});}
    [HttpPost("versoes/{versionId:guid}/solicitar-revisao")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestReview(Guid tenantId,Guid versionId,Guid reviewerId,DateTimeOffset? dueAt,string? instructions,Guid idempotencyKey,CancellationToken ct)
    {var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Challenge();var r=await api.SubmitStudioReviewAsync(token,tenantId,new(versionId,reviewerId,dueAt,instructions,idempotencyKey),ct);TempData[r.Succeeded?"StudioSuccess":"StudioError"]=r.Succeeded?"Solicitação registrada e notificação interna criada. Nenhum envio externo foi confirmado.":r.UserMessage("Não foi possível solicitar a revisão.");if(!r.Succeeded){TempData["ReviewReviewerId"]=reviewerId.ToString();TempData["ReviewDueAt"]=dueAt?.ToString("O");TempData["ReviewInstructions"]=instructions;TempData["ReviewIdempotencyKey"]=idempotencyKey.ToString();}return r.Succeeded?RedirectToAction("Detail","Reviews",new{tenantId,reviewId=r.Value!.ReviewId}):RedirectToAction(nameof(Version),new{tenantId,versionId});}
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

    private async Task<ApiCallResult<TemplateCatalogPage>> GetCatalogAsync(string token,Guid tenantId,string? search,string? type,string? scope,int page,CancellationToken ct)
    {
        var query=new List<string>{$"page={page}"};
        if(!string.IsNullOrWhiteSpace(search))query.Add($"search={Uri.EscapeDataString(search)}");
        if(!string.IsNullOrWhiteSpace(type))query.Add($"type={Uri.EscapeDataString(type)}");
        if(!string.IsNullOrWhiteSpace(scope))query.Add($"scope={Uri.EscapeDataString(scope)}");
        return await SendCatalogAsync<TemplateCatalogPage>(HttpMethod.Get,$"api/v1/organizations/{tenantId}/studio/templates?{string.Join('&',query)}",token,ct);
    }

    private async Task<ApiCallResult<T>> SendCatalogAsync<T>(HttpMethod method,string path,string token,CancellationToken ct)
    {
        var baseUrl=configuration["Api:BaseUrl"]??throw new InvalidOperationException("Api:BaseUrl não foi configurada.");
        using var http=new HttpClient{BaseAddress=new Uri(baseUrl),Timeout=TimeSpan.FromSeconds(15)};
        using var message=new HttpRequestMessage(method,path);
        message.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
        using var response=await http.SendAsync(message,ct);
        if(response.IsSuccessStatusCode)
        {
            var value=await response.Content.ReadFromJsonAsync<T>(cancellationToken:ct);
            return new ApiCallResult<T>(ApiCallStatus.Success,value);
        }
        var status=response.StatusCode==System.Net.HttpStatusCode.Forbidden?ApiCallStatus.Forbidden:response.StatusCode==System.Net.HttpStatusCode.Conflict?ApiCallStatus.Conflict:response.StatusCode==System.Net.HttpStatusCode.NotFound?ApiCallStatus.NotFound:ApiCallStatus.InvalidRequest;
        return new ApiCallResult<T>(status,default,"Não foi possível concluir a operação.");
    }
}
