using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odca.Contracts.Reviews;
using Odca.Web.Services;

namespace Odca.Web.Controllers;

[Authorize]
[Route("organizacoes/{tenantId:guid}/solicitacoes")]
public sealed class ReviewsController(OdcaApiClient api) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(Guid tenantId, string? status, bool mine = true, int page = 1, CancellationToken ct = default)
    {
        ViewData["Title"]="Solicitações de revisão"; ViewData["TenantId"]=tenantId;
        var token=await HttpContext.GetTokenAsync("access_token"); if(token is null)return Challenge();
        var result=await api.GetReviewsAsync(token,tenantId,status,mine,page,ct);
        if(result.Status==ApiCallStatus.Forbidden)return Forbid();
        ViewData["Status"]=status;ViewData["Mine"]=mine;ViewData["LoadError"]=result.Succeeded?null:result.UserMessage("Não foi possível carregar as solicitações.");
        return View(result.Value??new ReviewQueuePage([],page,20,0));
    }

    [HttpGet("{reviewId:guid}")]
    public async Task<IActionResult> Detail(Guid tenantId,Guid reviewId,CancellationToken ct)
    {
        ViewData["Title"]="Detalhe da solicitação";ViewData["TenantId"]=tenantId;
        var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Challenge();
        var result=await api.GetReviewAsync(token,tenantId,reviewId,ct);
        if(result.Status==ApiCallStatus.Forbidden)return Forbid();if(result.Status==ApiCallStatus.NotFound)return NotFound();
        return result.Succeeded?View(result.Value):RedirectToAction(nameof(Index),new{tenantId});
    }

    [HttpPost("{reviewId:guid}/mensagens")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddMessage(Guid tenantId,Guid reviewId,string body,string visibility="client",CancellationToken ct=default)
    {
        var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Challenge();
        var result=await api.AddReviewMessageAsync(token,tenantId,reviewId,new AddReviewMessageRequest(body,visibility,null,Guid.NewGuid()),ct);
        TempData[result.Succeeded?"ReviewNotice":"ReviewError"]=result.Succeeded?"Mensagem registrada no histórico.":result.UserMessage("Não foi possível registrar a mensagem.");
        return RedirectToAction(nameof(Detail),new{tenantId,reviewId});
    }

    [HttpPost("{reviewId:guid}/decisao")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Decide(Guid tenantId,Guid reviewId,string action,string justification,long expectedVersion,CancellationToken ct=default)
    {
        var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Challenge();
        var result=await api.DecideReviewAsync(token,tenantId,reviewId,new DecideReviewRequest(action,justification,expectedVersion,Guid.NewGuid()),ct);
        if(result.Status==ApiCallStatus.Forbidden)return Forbid();
        TempData[result.Succeeded?"ReviewNotice":"ReviewError"]=result.Succeeded
            ? action=="approve"?"Versão aprovada internamente. A formalização ainda deve ser registrada.":"Ajustes solicitados; uma nova versão deverá ser enviada."
            : result.UserMessage("Não foi possível registrar a decisão. Atualize a página e tente novamente.");
        return RedirectToAction(nameof(Detail),new{tenantId,reviewId});
    }
}
