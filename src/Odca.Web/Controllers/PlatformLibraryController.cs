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

    [HttpGet("novo")]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        ViewData["Title"] = "Novo modelo oficial";
        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null) return Challenge();
        return View();
    }

    [HttpPost("novo")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreatePost(string name, string? description, string contractType, string? documentPurpose, CancellationToken ct)
    {
        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null) return Challenge();

        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length < 3)
        {
            TempData["Error"] = "Nome deve ter pelo menos 3 caracteres.";
            return View("Create", new { name, description, contractType, documentPurpose });
        }

        // Minimal default content: empty sections array with one placeholder section.
        var content = @"{""schema"":""odca-contract/v1"",""sections"":[{""id"":""clausula-1"",""title"":""Cláusula 1"",""body"":""[Conteúdo inicial — edite este modelo para adicionar as cláusulas definitivas.]""}]}";
        var fields = "[]";

        var result = await api.CreateOfficialTemplateAsync(token,
            new CreateOfficialTemplateRequest(name.Trim(), description?.Trim(), contractType, documentPurpose?.Trim(), content, fields, null), ct);

        if (result.Succeeded)
        {
            TempData["Success"] = "Modelo criado em rascunho. Publique-o para torná-lo disponível às organizações.";
            return RedirectToAction(nameof(Index));
        }
        TempData["Error"] = result.UserMessage("Não foi possível criar o modelo.");
        return View("Create", new { name, description, contractType, documentPurpose });
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
