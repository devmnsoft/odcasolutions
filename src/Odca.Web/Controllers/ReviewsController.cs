using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odca.Configuration.Localization;
using Odca.Contracts.Reviews;
using Odca.Web.Services;

namespace Odca.Web.Controllers;

[Authorize]
[Route("organizacoes/{tenantId:guid}/solicitacoes")]
public sealed class ReviewsController(OdcaApiClient api, IUserTenantContext tenants, LocalizationService loc) : Controller
{
    // D-OC47 (S13): cultura canônica via CultureMiddleware e mensagens do BFF localizadas.
    private string? Culture => HttpContext.Items[Odca.Web.Middleware.CultureMiddleware.ItemsKey] as string;
    private string L(string key, params string[] args) => loc.T(key, Culture, args);

    [HttpGet]
    public async Task<IActionResult> Index(Guid tenantId, string? status, string scope = "requested", Guid? assigneeId = null,
        Guid? contractId = null, DateOnly? from = null, DateOnly? to = null, string? search = null, int page = 1, CancellationToken ct = default)
    {
        ViewData["Title"]=L("rev.title"); ViewData["TenantId"]=tenantId;
        var token=await HttpContext.GetTokenAsync("access_token"); if(token is null)return Challenge();
        var result=await api.GetReviewsAsync(token,tenantId,status,scope,assigneeId,contractId,from,to,search,page,ct);
        if(result.Status==ApiCallStatus.Forbidden)return Forbid();
        ViewData["Status"]=status; ViewData["Scope"]=scope; ViewData["AssigneeId"]=assigneeId; ViewData["ContractId"]=contractId;
        ViewData["From"]=from; ViewData["To"]=to; ViewData["Search"]=search;
        var assignees=await api.GetReviewAssigneesAsync(token,tenantId,ct);
        ViewData["Assignees"]=assignees.Succeeded && assignees.Value is not null ? assignees.Value : Array.Empty<ReviewAssignee>();
        ViewData["LoadError"]=result.Succeeded?null:result.UserMessage(L("rev.msg_load_fail"));
        return View(result.Value??new ReviewQueuePage([],page,20,0));
    }

    [HttpGet("{reviewId:guid}")]
    public async Task<IActionResult> Detail(Guid tenantId,Guid reviewId,CancellationToken ct)
    {
        ViewData["Title"]=L("rev.detail_title");ViewData["TenantId"]=tenantId;
        var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Challenge();
        var result=await api.GetReviewAsync(token,tenantId,reviewId,ct);
        if(result.Status==ApiCallStatus.Forbidden)return Forbid();if(result.Status==ApiCallStatus.NotFound)return NotFound();
        var assignees=await api.GetReviewAssigneesAsync(token,tenantId,ct);
        var access=await tenants.GetAccessAsync(tenantId,ct);
        ViewData["Assignees"]=assignees.Succeeded && assignees.Value is not null ? assignees.Value : Array.Empty<ReviewAssignee>();
        ViewData["CanDecide"]=access?.HasPermission("tenant.reviews.decide")==true;
        ViewData["CanCancel"]=access?.HasPermission("tenant.reviews.cancel")==true;
        return result.Succeeded?View(result.Value):RedirectToAction(nameof(Index),new{tenantId});
    }

    [HttpPost("{reviewId:guid}/mensagens")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddMessage(Guid tenantId,Guid reviewId,string body,string visibility="client",CancellationToken ct=default)
    {
        var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Challenge();
        var result=await api.AddReviewMessageAsync(token,tenantId,reviewId,new AddReviewMessageRequest(body,visibility,null,Guid.NewGuid()),ct);
        if (!result.Succeeded) { TempData["ReviewBody"]=body; TempData["ReviewVisibility"]=visibility; }
        TempData[result.Succeeded?"ReviewNotice":"ReviewError"]=result.Succeeded?L("rev.msg_message_saved"):result.UserMessage(L("rev.msg_message_fail"));
        return RedirectToAction(nameof(Detail),new{tenantId,reviewId});
    }

    [HttpPost("{reviewId:guid}/decisao")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Decide(Guid tenantId,Guid reviewId,string action,string justification,long expectedVersion,CancellationToken ct=default)
    {
        var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Challenge();
        var result=await api.DecideReviewAsync(token,tenantId,reviewId,new DecideReviewRequest(action,justification,expectedVersion,Guid.NewGuid()),ct);
        if(result.Status==ApiCallStatus.Forbidden)return Forbid();
        if (!result.Succeeded) { TempData["ReviewJustification"]=justification; TempData["ReviewConflict"]=result.Status==ApiCallStatus.Conflict; }
        TempData[result.Succeeded?"ReviewNotice":"ReviewError"]=result.Succeeded
            ? action=="approve"?L("rev.msg_approved"):L("rev.msg_changes_requested")
            : result.UserMessage(L("rev.msg_decision_fail"));
        return RedirectToAction(nameof(Detail),new{tenantId,reviewId});
    }

    [HttpPost("{reviewId:guid}/cancelamento")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(Guid tenantId, Guid reviewId, string justification, long expectedVersion, CancellationToken ct = default)
    {
        var token = await HttpContext.GetTokenAsync("access_token"); if (token is null) return Challenge();
        var result = await api.CancelReviewAsync(token, tenantId, reviewId, new CancelReviewRequest(justification, expectedVersion, Guid.NewGuid()), ct);
        if (result.Status == ApiCallStatus.Forbidden) return Forbid();
        TempData[result.Succeeded ? "ReviewNotice" : "ReviewError"] = result.Succeeded
            ? L("rev.msg_cancelled")
            : result.UserMessage(L("rev.msg_cancel_fail"));
        return RedirectToAction(nameof(Detail), new { tenantId, reviewId });
    }

    [HttpPost("{reviewId:guid}/atribuicao")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reassign(Guid tenantId,Guid reviewId,Guid assigneeId,string reason,long expectedVersion,CancellationToken ct=default)
    {
        var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Challenge();
        var result=await api.ReassignReviewAsync(token,tenantId,reviewId,new ReassignReviewRequest(assigneeId,reason,expectedVersion,Guid.NewGuid()),ct);
        if(result.Status==ApiCallStatus.Forbidden)return Forbid();
        if(!result.Succeeded){TempData["ReviewAssignmentReason"]=reason;TempData["ReviewAssignmentConflict"]=result.Status==ApiCallStatus.Conflict;}
        TempData[result.Succeeded?"ReviewNotice":"ReviewError"]=result.Succeeded?L("rev.msg_reassigned"):result.UserMessage(L("rev.msg_reassign_fail"));
        return RedirectToAction(nameof(Detail),new{tenantId,reviewId});
    }
}
