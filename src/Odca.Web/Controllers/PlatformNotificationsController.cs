using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odca.Web.Services;

namespace Odca.Web.Controllers;

/// <summary>
/// B.1 (D-OC31): central de notificações da plataforma — consulta da outbox de entrega
/// com filtros (status/tipo/destino), detalhe seguro (sem payload protegido nem links)
/// e retry auditado apenas para falhas definitivas.
/// </summary>
[Authorize(Roles = "SuperAdministrator")]
[Route("administracao/notificacoes")]
public sealed class PlatformNotificationsController(OdcaApiClient api) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? status, string? kind, string? search, int page = 1, CancellationToken ct = default)
    {
        ViewData["Title"] = "Central de notificações";
        var token = await HttpContext.GetTokenAsync("access_token"); if (token is null) return Challenge();
        page = Math.Max(1, page);
        const int pageSize = 25;
        var result = await api.GetPlatformNotificationsAsync(token, status, kind, search, page, pageSize, ct);
        ViewData["Status"] = status; ViewData["Kind"] = kind; ViewData["Search"] = search; ViewData["Page"] = page;
        if (!result.Succeeded || result.Value is not { ValueKind: JsonValueKind.Object } body)
        {
            ViewData["LoadError"] = result.Succeeded ? null : result.UserMessage("Não foi possível carregar a central de notificações.");
            return View(new PageViewModel([], page, pageSize, 0));
        }
        var items = new List<ItemViewModel>();
        if (body.TryGetProperty("items", out var itemsEl))
        {
            foreach (var el in itemsEl.EnumerateArray())
            {
                items.Add(new ItemViewModel(
                    el.GetProperty("messageId").GetGuid(),
                    el.GetProperty("organizationName").GetString() ?? string.Empty,
                    el.GetProperty("kind").GetString() ?? string.Empty,
                    el.GetProperty("destination").GetString() ?? string.Empty,
                    el.GetProperty("status").GetString() ?? string.Empty,
                    el.GetProperty("attemptCount").GetInt32(),
                    el.TryGetProperty("lastErrorCode", out var lec) && lec.ValueKind == JsonValueKind.String ? lec.GetString() : null,
                    el.GetProperty("createdAt").GetDateTimeOffset(),
                    el.TryGetProperty("sentAt", out var sa) && sa.ValueKind == JsonValueKind.String && sa.GetString() is { } sentRaw
                        ? DateTimeOffset.Parse(sentRaw, CultureInfo.InvariantCulture) : null,
                    el.TryGetProperty("invitationId", out var inv) && inv.ValueKind == JsonValueKind.String && inv.GetString() is { } invRaw
                        ? Guid.Parse(invRaw) : null));
            }
        }
        var total = body.TryGetProperty("total", out var totalEl) && totalEl.ValueKind == JsonValueKind.Number ? totalEl.GetInt32() : 0;
        return View(new PageViewModel(items, page, pageSize, total));
    }

    [HttpPost("{messageId:guid}/retry")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Retry(Guid messageId, string? status, string? kind, string? search, int page = 1, CancellationToken ct = default)
    {
        var token = await HttpContext.GetTokenAsync("access_token"); if (token is null) return Challenge();
        var result = await api.RetryPlatformNotificationAsync(token, messageId, ct);
        TempData["NotificationsNotice"] = result.Succeeded
            ? "Mensagem devolvida à fila de entrega com tentativas reiniciadas; a operação foi registrada na auditoria."
            : result.ErrorCode == "notification_retry_unavailable"
                ? "Apenas mensagens com falha definitiva podem ser reprocessadas."
                : result.UserMessage("Não foi possível reprocessar a mensagem.");
        return RedirectToAction(nameof(Index), new { status, kind, search, page = Math.Max(1, page) });
    }

    public sealed record ItemViewModel(Guid MessageId, string OrganizationName, string Kind, string Destination, string Status,
        int AttemptCount, string? LastErrorCode, DateTimeOffset CreatedAt, DateTimeOffset? SentAt, Guid? InvitationId);

    public sealed record PageViewModel(IReadOnlyList<ItemViewModel> Items, int Page, int PageSize, int Total)
    {
        public int Pages => Math.Max(1, (Total + PageSize - 1) / PageSize);
    }
}
