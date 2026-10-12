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

    [HttpGet("{key}/nova-revisao")]
    public async Task<IActionResult> NewRevision(string key, CancellationToken ct)
    {
        ViewData["Title"] = "Nova revisão de modelo";
        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null) return Challenge();
        var result = await api.GetTemplateRevisionCurrentAsync(token, key, ct);
        if (!result.Succeeded)
        {
            TempData["Error"] = result.UserMessage("Não foi possível carregar a revisão atual do modelo.");
            return RedirectToAction(nameof(Index));
        }
        return View(result.Value!);
    }

    [HttpPost("{key}/nova-revisao")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> NewRevisionPost(string key, string summary, string contentJson, string fieldsJson, int expectedRevision, CancellationToken ct)
    {
        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null) return Challenge();
        var normalizedSummary = summary?.Trim() ?? string.Empty;
        if (normalizedSummary.Length is < 5 or > 500)
        {
            TempData["Error"] = "Informe um resumo da mudança entre 5 e 500 caracteres.";
            var current = await api.GetTemplateRevisionCurrentAsync(token, key, ct);
            if (current.Succeeded) return View(current.Value!);
            return RedirectToAction(nameof(Index));
        }

        var result = await api.PublishTemplateRevisionAsync(
            token, key, new TemplateRevisionPublishRequest(normalizedSummary, contentJson, fieldsJson, expectedRevision), ct);
        if (result.Succeeded)
        {
            var newRevision = expectedRevision + 1;
            TempData["Success"] = $"Revisão {newRevision} publicada. Documentos emitidos a partir de revisões anteriores permanecem intactos (fixados à versão de origem).";
            return RedirectToAction(nameof(Index));
        }

        // Conflito ou falha: recarrega o estado do servidor para o superadmin repetir a partir do ponto atual.
        TempData["Error"] = result.UserMessage("A revisão não foi publicada; verifique a mensagem do servidor.");
        var reloaded = await api.GetTemplateRevisionCurrentAsync(token, key, ct);
        if (reloaded.Succeeded) return View(reloaded.Value!);
        return RedirectToAction(nameof(Index));
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
