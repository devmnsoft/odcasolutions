using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odca.Configuration.Localization;
using Odca.Contracts.Solicitations;
using Odca.Web.Services;

namespace Odca.Web.Controllers;

/// <summary>
/// Seção C: Central de solicitações da organização (cliente -> ODCA). O portal lista as
/// solicitações do próprio tenant e permite abrir, responder, encerrar e cancelar; os
/// atendimentos da fila ODCA são respondidos na administração da plataforma.
/// </summary>
[Authorize]
[Route("organizacoes/{tenantId:guid}/central-solicitacoes")]
public sealed class SolicitacoesController(OdcaApiClient api, IUserTenantContext tenants, LocalizationService loc) : Controller
{
    private string? Culture => HttpContext.Items[Odca.Web.Middleware.CultureMiddleware.ItemsKey] as string;
    private string L(string key, params string[] args) => loc.T(key, Culture, args);

    [HttpGet]
    public async Task<IActionResult> Index(Guid tenantId, string? service, string? status, string? search, CancellationToken ct)
    {
        ViewData["Title"] = L("sol.center"); ViewData["TenantId"] = tenantId;
        var token = await HttpContext.GetTokenAsync("access_token"); if (token is null) return Challenge();
        var result = await api.GetSolicitationsAsync(token, tenantId, service, status, search, ct);
        if (result.Status == ApiCallStatus.Forbidden) return Forbid();
        var access = await tenants.GetAccessAsync(tenantId, ct);
        ViewData["Service"] = service; ViewData["Status"] = status; ViewData["Search"] = search;
        ViewData["CanManage"] = access?.HasPermission("tenant.solicitations.manage") == true;
        ViewData["LoadError"] = result.Succeeded ? null : result.UserMessage(L("sol.msg.load_error"));
        return View(result.Value ?? []);
    }

    [HttpGet("nova")]
    public async Task<IActionResult> Nova(Guid tenantId, [FromQuery] string? service, CancellationToken ct)
    {
        var access = await tenants.GetAccessAsync(tenantId, ct);
        if (access?.HasPermission("tenant.solicitations.manage") != true) return Forbid();
        ViewData["Title"] = L("sol.new"); ViewData["TenantId"] = tenantId;
        var normalized = service?.Trim().ToLowerInvariant();
        if (normalized is "revisao" or "adaptacao" or "esclarecimento" or "suporte_tecnico")
        {
            TempData["SolService"] = normalized;
        }
        return View();
    }

    [HttpPost("nova")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Guid tenantId, string service, string? priority, string subject, string body, string? contractContext, CancellationToken ct)
    {
        var token = await HttpContext.GetTokenAsync("access_token"); if (token is null) return Challenge();
        Guid? contractId = Guid.TryParse(contractContext, out var cid) ? cid : null;
        var result = await api.OpenSolicitationAsync(token, tenantId, new OpenSolicitationRequest(service, priority, subject, body, Guid.NewGuid(), contractId), ct);
        if (result.Status == ApiCallStatus.Forbidden) return Forbid();
        if (!result.Succeeded)
        {
            TempData["SolService"] = service; TempData["SolPriority"] = priority; TempData["SolSubject"] = subject; TempData["SolBody"] = body;
            TempData["SolError"] = result.UserMessage(service == "suporte_tecnico" ? L("sol.msg.create_error_support") : L("sol.msg.create_error_enterprise"));
            return RedirectToAction(nameof(Nova), new { tenantId });
        }
        var protocol = TryProtocol(result.Value);
        TempData["SolNotice"] = L("sol.msg.created", protocol);
        return RedirectToAction(nameof(Detalhes), new { tenantId, solicitationId = TryId(result.Value) });
    }

    [HttpGet("{solicitationId:guid}")]
    public async Task<IActionResult> Detalhes(Guid tenantId, Guid solicitationId, CancellationToken ct)
    {
        ViewData["Title"] = L("sol.detail_title"); ViewData["TenantId"] = tenantId;
        var token = await HttpContext.GetTokenAsync("access_token"); if (token is null) return Challenge();
        var result = await api.GetSolicitationAsync(token, tenantId, solicitationId, ct);
        if (result.Status == ApiCallStatus.Forbidden) return Forbid();
        if (result.Status == ApiCallStatus.NotFound) return NotFound();
        if (!result.Succeeded) return RedirectToAction(nameof(Index), new { tenantId });
        var access = await tenants.GetAccessAsync(tenantId, ct);
        ViewData["CanManage"] = access?.HasPermission("tenant.solicitations.manage") == true;
        return View(result.Value);
    }

    [HttpPost("{solicitationId:guid}/mensagens")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddMessage(Guid tenantId, Guid solicitationId, string body, long expectedVersion, CancellationToken ct)
    {
        var token = await HttpContext.GetTokenAsync("access_token"); if (token is null) return Challenge();
        var result = await api.SolicitationMessageAsync(token, tenantId, solicitationId, new SolicitationMessageRequest(body, expectedVersion), ct);
        TempData[result.Succeeded ? "SolNotice" : "SolError"] = result.Succeeded
            ? L("sol.msg.message_registered")
            : result.UserMessage(L("sol.msg.message_fail"));
        return RedirectToAction(nameof(Detalhes), new { tenantId, solicitationId });
    }

    [HttpPost("{solicitationId:guid}/acoes")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Act(Guid tenantId, Guid solicitationId, string action, string? text, long expectedVersion, CancellationToken ct)
    {
        var token = await HttpContext.GetTokenAsync("access_token"); if (token is null) return Challenge();
        var result = await api.SolicitationActionAsync(token, tenantId, solicitationId, new SolicitationActionRequest(action, text, null, null, expectedVersion), ct);
        TempData[result.Succeeded ? "SolNotice" : "SolError"] = result.Succeeded
            ? action == "encerrar" ? L("sol.msg.closed") : L("sol.msg.cancelled")
            : result.UserMessage(L("sol.msg.action_fail"));
        return RedirectToAction(nameof(Detalhes), new { tenantId, solicitationId });
    }

    private static string TryProtocol(JsonElement payload) =>
        payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("protocol", out var p) ? p.GetString() ?? "" : "";

    private static Guid TryId(JsonElement payload) =>
        payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("id", out var p) && p.ValueKind == JsonValueKind.String && Guid.TryParse(p.GetString(), out var id) ? id : Guid.Empty;
}
