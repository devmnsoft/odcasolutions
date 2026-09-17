using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odca.Web.Models;
using Odca.Web.Services;

namespace Odca.Web.Controllers;

[Authorize]
[Route("organizacoes/{tenantId:guid}/caixa")]
public sealed class InboxController(OdcaApiClient api) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(
        Guid tenantId,
        string scope = "mine",
        string? kind = null,
        string? urgency = null,
        Guid? contractId = null,
        Guid? ownerId = null,
        int page = 1,
        CancellationToken ct = default)
    {
        ViewData["Title"] = "Caixa operacional";
        ViewData["TenantId"] = tenantId;
        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null) return Challenge();

        var result = await api.GetInboxAsync(token, tenantId, scope, kind, urgency, contractId, ownerId, page, 20, ct);
        if (result.Status == ApiCallStatus.Unauthorized) return Challenge();
        if (result.Status == ApiCallStatus.Forbidden) return Forbid();

        return View(new InboxWorkspaceViewModel
        {
            Page = result.Value ?? new([], page, 20, 0, 0, 0, 0),
            Scope = scope,
            Kind = kind,
            Urgency = urgency,
            Error = result.Succeeded ? null : result.UserMessage("Não foi possível abrir a caixa operacional.")
        });
    }
}
