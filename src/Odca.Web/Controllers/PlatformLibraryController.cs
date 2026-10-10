using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odca.Contracts.Administration;
using Odca.Web.Services;

namespace Odca.Web.Controllers;

/// <summary>
/// Operação contratual Bloco B: biblioteca de modelos oficiais da plataforma. Lista as
/// linhas globais publicadas/retiradas e permite publicar/retirar com justificativa; a
/// mudança fica auditada na função platform_catalog_set_status (v045).
/// </summary>
[Authorize(Roles = "SuperAdministrator")]
[Route("administracao/biblioteca")]
public sealed class PlatformLibraryController(OdcaApiClient api) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        ViewData["Title"] = "Biblioteca de modelos";
        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null) return Challenge();
        var result = await api.GetTemplateCatalogAsync(token, ct);
        if (!result.Succeeded)
        {
            Response.StatusCode = 503;
            return View("ServiceUnavailable");
        }
        return View(result.Value!);
    }

    [HttpPost("{key}/status")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Status(string key, string action, string? reason, CancellationToken ct)
    {
        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null) return Challenge();
        var normalizedAction = action?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalizedAction is not ("publish" or "retire"))
        {
            TempData["Error"] = "Ação inválida para o catálogo.";
            return RedirectToAction(nameof(Index));
        }
        var normalizedReason = reason?.Trim() ?? string.Empty;
        if (normalizedReason.Length is < 5 or > 500)
        {
            TempData["Error"] = "Informe uma justificativa entre 5 e 500 caracteres.";
            return RedirectToAction(nameof(Index));
        }

        var result = await api.SetTemplateCatalogStatusAsync(token, key, new TemplateCatalogStatusRequest(normalizedAction, normalizedReason), ct);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded
            ? (normalizedAction == "retire"
                ? "Modelo retirado do catálogo global. Novas organizações não o recebem; cópias já instaladas seguem intactas."
                : "Modelo publicado no catálogo global. Organizações sem cópia própria passam a resolvê-lo pela linha global.")
            : result.UserMessage("A operação não foi aplicada.");
        return RedirectToAction(nameof(Index));
    }
}
