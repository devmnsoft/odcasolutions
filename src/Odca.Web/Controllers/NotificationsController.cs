using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odca.Web.Models;
using Odca.Web.Services;

namespace Odca.Web.Controllers;

[Authorize]
public sealed class NotificationsController(OdcaApiClient api) : Controller
{
    [HttpGet("notificacoes")]
    public Task<IActionResult> IndexEntry(CancellationToken cancellationToken)
        => RedirectToTenantAsync(nameof(Index), cancellationToken);

    [HttpGet("organizacoes/{tenantId:guid}/notificacoes")]
    public async Task<IActionResult> Index(
        Guid tenantId,
        bool unreadOnly = false,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var token = await TenantUiHelpers.AccessTokenAsync(HttpContext);
        if (token is null)
        {
            return Challenge();
        }

        var org = await TenantUiHelpers.RequireOrganizationAsync(api, token, tenantId, cancellationToken);
        if (org is null)
        {
            return Forbid();
        }

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        TenantUiHelpers.SetTenantContext(this, org);

        var list = await api.GetNotificationsAsync(token, tenantId, unreadOnly, page, pageSize, cancellationToken);
        if (list.Status == ApiCallStatus.Unauthorized)
        {
            return Challenge();
        }

        if (list.Status == ApiCallStatus.Forbidden)
        {
            return Forbid();
        }

        var unread = await api.GetUnreadNotificationCountAsync(token, tenantId, cancellationToken);
        var unreadCount = unread.Succeeded ? unread.Value!.Count : 0;
        ViewData["UnreadNotifications"] = unreadCount;

        return View(new NotificationsPageViewModel
        {
            Organization = org,
            Items = list.Succeeded ? list.Value : null,
            UnreadOnly = unreadOnly,
            Page = page,
            PageSize = pageSize,
            LoadError = !list.Succeeded,
            UnreadCount = unreadCount
        });
    }

    [HttpPost("organizacoes/{tenantId:guid}/notificacoes/{id:guid}/lida")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkRead(
        Guid tenantId,
        Guid id,
        bool unreadOnly = false,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        var token = await TenantUiHelpers.AccessTokenAsync(HttpContext);
        if (token is null)
        {
            return Challenge();
        }

        var result = await api.MarkNotificationReadAsync(token, tenantId, id, cancellationToken);
        if (result.Status != ApiCallStatus.Success)
        {
            TempData["Error"] = TenantUiHelpers.UserFacingError(result, "Não foi possível marcar a notificação como lida.");
        }

        return RedirectToAction(nameof(Index), new { tenantId, unreadOnly, page });
    }

    [HttpPost("organizacoes/{tenantId:guid}/notificacoes/{id:guid}/abrir")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Open(
        Guid tenantId,
        Guid id,
        Guid resourceId,
        string resourceType,
        bool unreadOnly = false,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        var token = await TenantUiHelpers.AccessTokenAsync(HttpContext);
        if (token is null)
        {
            return Challenge();
        }

        _ = await api.MarkNotificationReadAsync(token, tenantId, id, cancellationToken);

        if (!string.Equals(resourceType, "contract", StringComparison.OrdinalIgnoreCase))
        {
            TempData["Error"] = "Este aviso não possui destino operacional nesta versão.";
            return RedirectToAction(nameof(Index), new { tenantId, unreadOnly, page });
        }

        var contract = await api.GetContractAsync(token, tenantId, resourceId, cancellationToken);
        if (contract.Status == ApiCallStatus.Forbidden)
        {
            TempData["Error"] = "Você não tem permissão para abrir o contrato vinculado a este aviso.";
            return RedirectToAction(nameof(Index), new { tenantId, unreadOnly, page });
        }

        if (!contract.Succeeded)
        {
            TempData["Error"] = "O contrato vinculado não está disponível ou foi removido das listas operacionais.";
            return RedirectToAction(nameof(Index), new { tenantId, unreadOnly, page });
        }

        return RedirectToAction("Details", "Contracts", new { tenantId, id = resourceId });
    }

    private async Task<IActionResult> RedirectToTenantAsync(string actionName, CancellationToken cancellationToken)
    {
        var token = await TenantUiHelpers.AccessTokenAsync(HttpContext);
        if (token is null)
        {
            return Challenge();
        }

        var tenantId = await TenantUiHelpers.ResolveDefaultTenantIdAsync(api, token, cancellationToken);
        return tenantId is null
            ? RedirectToAction("Index", "Organizations")
            : RedirectToAction(actionName, new { tenantId });
    }
}
