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
public sealed class PlatformLibraryController(OdcaApiClient api, Odca.Configuration.Localization.LocalizationService loc) : Controller
{
    // D-OC49 (S15): padrão canônico de cultura (CultureMiddleware) e localização das mensagens BFF.
    private string? Culture => HttpContext.Items[Odca.Web.Middleware.CultureMiddleware.ItemsKey] as string;

    private string L(string key, params string[] args) => loc.T(key, Culture, args);

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        ViewData["Title"] = L("biblioteca-modelos");
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
        ViewData["Title"] = L("platform.template_create_title");
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
            TempData["Error"] = L("platform.lib_err_name_min");
            return View("Create", new { name, description, contractType, documentPurpose });
        }

        // Minimal default content: empty sections array with one placeholder section.
        var content = @"{""schema"":""odca-contract/v1"",""sections"":[{""id"":""clausula-1"",""title"":""Cláusula 1"",""body"":""[Conteúdo inicial — edite este modelo para adicionar as cláusulas definitivas.]""}]}";
        var fields = "[]";

        var result = await api.CreateOfficialTemplateAsync(token,
            new CreateOfficialTemplateRequest(name.Trim(), description?.Trim(), contractType, documentPurpose?.Trim(), content, fields, null), ct);

        if (result.Succeeded)
        {
            TempData["Success"] = L("platform.lib_ok_created");
            return RedirectToAction(nameof(Index));
        }
        TempData["Error"] = result.UserMessage(L("platform.lib_err_create"));
        return View("Create", new { name, description, contractType, documentPurpose });
    }

    [HttpGet("{key}/nova-revisao")]
    public async Task<IActionResult> NewRevision(string key, CancellationToken ct)
    {
        ViewData["Title"] = L("platform.rev_title");
        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null) return Challenge();
        var result = await api.GetTemplateRevisionCurrentAsync(token, key, ct);
        if (!result.Succeeded)
        {
            TempData["Error"] = result.UserMessage(L("platform.rev_err_load"));
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
            TempData["Error"] = L("platform.rev_err_summary");
            var current = await api.GetTemplateRevisionCurrentAsync(token, key, ct);
            if (current.Succeeded) return View(current.Value!);
            return RedirectToAction(nameof(Index));
        }

        var result = await api.PublishTemplateRevisionAsync(
            token, key, new TemplateRevisionPublishRequest(normalizedSummary, contentJson, fieldsJson, expectedRevision), ct);
        if (result.Succeeded)
        {
            var newRevision = expectedRevision + 1;
            TempData["Success"] = L("platform.rev_ok_published", newRevision.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return RedirectToAction(nameof(Index));
        }

        // Conflito ou falha: recarrega o estado do servidor para o superadmin repetir a partir do ponto atual.
        TempData["Error"] = result.UserMessage(L("platform.rev_err_conflict"));
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
            TempData["Error"] = L("platform.lib_err_action");
            return RedirectToAction(nameof(Index));
        }
        var normalizedReason = reason?.Trim() ?? string.Empty;
        if (normalizedReason.Length is < 5 or > 500)
        {
            TempData["Error"] = L("platform.lib_err_reason");
            return RedirectToAction(nameof(Index));
        }

        var result = await api.SetTemplateCatalogStatusAsync(token, key, new TemplateCatalogStatusRequest(normalizedAction, normalizedReason), ct);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded
            ? (normalizedAction == "retire"
                ? L("platform.lib_ok_retired")
                : L("platform.lib_ok_published"))
            : result.UserMessage(L("platform.lib_err_apply"));
        return RedirectToAction(nameof(Index));
    }
}
