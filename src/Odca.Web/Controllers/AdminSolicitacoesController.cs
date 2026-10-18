using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odca.Contracts.Solicitations;
using Odca.Configuration.Localization;
using Odca.Web.Services;

namespace Odca.Web.Controllers;

/// <summary>
/// Seção C: fila da plataforma ODCA (atendimento das solicitações dos clientes) e aprovação
/// de modelos cirúrgicos oficiais por organização.
/// </summary>
[Authorize(Roles = "SuperAdministrator")]
[Route("administracao/atendimento-odca")]
public sealed class AdminSolicitacoesController(OdcaApiClient api, LocalizationService loc) : Controller
{
    // D-OC42 (S8): padrão canônico de cultura (CultureMiddleware) e localização das mensagens BFF.
    private string? Culture => HttpContext.Items[Odca.Web.Middleware.CultureMiddleware.ItemsKey] as string;
    private string L(string key, params string[] args) => loc.T(key, Culture, args);

    [HttpGet]
    public async Task<IActionResult> Index(Guid? tenantId, string? service, string? status, string? search, CancellationToken ct)
    {
        ViewData["Title"] = L("admin.queue_title");
        var token = await HttpContext.GetTokenAsync("access_token"); if (token is null) return Challenge();
        var result = await api.GetPlatformSolicitationsAsync(token, tenantId, service, status, search, ct);
        ViewData["TenantId"] = tenantId; ViewData["Service"] = service; ViewData["Status"] = status; ViewData["Search"] = search;
        ViewData["LoadError"] = result.Succeeded ? null : result.UserMessage(L("admin.queue_load_error"));
        return View(result.Value ?? []);
    }

    [HttpGet("{solicitationId:guid}")]
    public async Task<IActionResult> Detalhes(Guid solicitationId, CancellationToken ct)
    {
        ViewData["Title"] = L("admin.queue_crumb");
        var token = await HttpContext.GetTokenAsync("access_token"); if (token is null) return Challenge();
        var result = await api.GetPlatformSolicitationAsync(token, solicitationId, ct);
        if (result.Status == ApiCallStatus.NotFound) return NotFound();
        if (!result.Succeeded) return RedirectToAction(nameof(Index));
        return View(result.Value);
    }

    [HttpPost("{solicitationId:guid}/mensagens")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddMessage(Guid solicitationId, Guid tenantId, string body, long expectedVersion, CancellationToken ct)
    {
        var token = await HttpContext.GetTokenAsync("access_token"); if (token is null) return Challenge();
        var result = await api.PlatformSolicitationMessageAsync(token, solicitationId, new SolicitationMessageRequest(body, expectedVersion, tenantId), ct);
        TempData[result.Succeeded ? "SolNotice" : "SolError"] = result.Succeeded
            ? L("admin.msg_reply_ok")
            : result.UserMessage(L("admin.msg_reply_fail"));
        return RedirectToAction(nameof(Detalhes), new { solicitationId });
    }

    [HttpPost("{solicitationId:guid}/acoes")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Act(Guid solicitationId, Guid tenantId, string action, string? text, string? priority, string? assigneeUserId, long expectedVersion, CancellationToken ct)
    {
        var token = await HttpContext.GetTokenAsync("access_token"); if (token is null) return Challenge();
        Guid? assignee = null;
        if (Guid.TryParse(assigneeUserId, out var parsedAssignee)) assignee = parsedAssignee;
        var result = await api.PlatformSolicitationActionAsync(token, solicitationId,
            new SolicitationActionRequest(action, text, priority, assignee, expectedVersion, tenantId), ct);
        TempData[result.Succeeded ? "SolNotice" : "SolError"] = result.Succeeded
            ? L("admin.msg_action_ok")
            : result.UserMessage(L("admin.msg_action_fail"));
        return RedirectToAction(nameof(Detalhes), new { solicitationId });
    }

    [HttpGet("aprovacoes")]
    public async Task<IActionResult> Aprovacoes(CancellationToken ct)
    {
        ViewData["Title"] = L("admin.approvals_title");
        var token = await HttpContext.GetTokenAsync("access_token"); if (token is null) return Challenge();
        var result = await api.GetTemplateApprovalsAsync(token, ct);
        ViewData["LoadError"] = result.Succeeded ? null : result.UserMessage(L("admin.approvals_load_error"));
        return View(result.Value ?? []);
    }

    [HttpPost("aprovacoes/{tenantId:guid}/{key}/decisao")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DecideApproval(Guid tenantId, string key, string decision, string? note, CancellationToken ct)
    {
        var token = await HttpContext.GetTokenAsync("access_token"); if (token is null) return Challenge();
        var result = await api.DecideTemplateApprovalAsync(token, tenantId, key, new ApprovalDecisionRequest(decision, note), ct);
        TempData[result.Succeeded ? "SolNotice" : "SolError"] = result.Succeeded
            ? decision == "aprovado" ? L("admin.msg_decision_approved_ok") : L("admin.msg_decision_rejected_ok")
            : result.UserMessage(L("admin.msg_decision_fail"));
        return RedirectToAction(nameof(Aprovacoes));
    }
}
