using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odca.Web.Services;
using Odca.Contracts.DocumentImports;

namespace Odca.Web.Controllers;

[Authorize]
[Route("organizacoes/{tenantId:guid}/importacoes")]
public sealed class ImportsController(OdcaApiClient api) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(Guid tenantId, string? status = null, bool mine = false, bool awaitingReview = false,
        DateOnly? from = null, DateOnly? to = null, CancellationToken ct = default)
    {
        var token = await HttpContext.GetTokenAsync("access_token"); if (token is null) return Challenge();
        ViewData["Title"] = "Central de importações"; ViewData["TenantId"] = tenantId; ViewData["Status"] = status;
        ViewData["Mine"] = mine; ViewData["AwaitingReview"] = awaitingReview; ViewData["From"] = from; ViewData["To"] = to;
        if (from.HasValue && to.HasValue && from.Value > to.Value)
        {
            ViewData["DateError"] = "A data final deve ser igual ou posterior à data inicial.";
            return View(new ContractImportPage([], 0));
        }
        var result = await api.GetContractImportsAsync(token, tenantId, status, mine, awaitingReview, from, to, ct);
        if (!result.Succeeded)
        {
            if (result.Status == ApiCallStatus.Forbidden) return Forbid();
            ViewData["LoadError"] = result.UserMessage("Não foi possível carregar as importações.");
            return View(new ContractImportPage([], 0));
        }
        return View(result.Value!);
    }

    [HttpGet("{importId:guid}")]
    public async Task<IActionResult> Review(Guid tenantId, Guid importId, CancellationToken ct)
    {
        var token = await HttpContext.GetTokenAsync("access_token"); if (token is null) return Challenge();
        var result = await api.GetContractImportAsync(token, tenantId, importId, ct);
        if (!result.Succeeded) return result.Status == ApiCallStatus.NotFound ? NotFound() : View("~/Views/Shared/ServiceUnavailable.cshtml");
        ViewData["Title"] = "Revisar importação"; ViewData["TenantId"] = tenantId;
        return View(result.Value);
    }

    [HttpGet("preview/{contractId:guid}/{documentId:guid}/{versionId:guid}")]
    public async Task<IActionResult> Preview(Guid tenantId, Guid contractId, Guid documentId, Guid versionId, CancellationToken ct)
    {
        var token = await HttpContext.GetTokenAsync("access_token"); if (token is null) return Challenge();
        var result = await api.GetDocumentPreviewAsync(token, tenantId, contractId, documentId, versionId, ct);
        if (result.Content is null) return StatusCode((int)result.Status);
        Response.Headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'self'; sandbox";
        return File(result.Content, result.ContentType ?? "application/octet-stream", enableRangeProcessing: false);
    }

    [HttpPost("{importId:guid}/review/{contractId:guid}/{extractionId:guid}")]
    public async Task<IActionResult> SaveReview(Guid tenantId, Guid importId, Guid contractId, Guid extractionId, [FromBody] SaveImportReview request, CancellationToken ct)
    {
        var token = await HttpContext.GetTokenAsync("access_token"); if (token is null) return Unauthorized();
        var result = await api.SaveImportReviewAsync(token, tenantId, contractId, extractionId, request, ct);
        return result.Succeeded ? Json(result.Value) : StatusCode(result.Status == ApiCallStatus.Conflict ? 409 : 422, new { title = result.ErrorTitle, detail = result.ErrorDetail });
    }

    [HttpPost("{importId:guid}/dados-manuais")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveManualData(Guid tenantId, Guid importId, SaveManualContractImport request, CancellationToken ct)
    {
        var token = await HttpContext.GetTokenAsync("access_token"); if (token is null) return Challenge();
        var result = await api.SaveManualImportDataAsync(token, tenantId, importId, request, ct);
        if (result.Status == ApiCallStatus.Forbidden) return Forbid();
        TempData[result.Succeeded ? "ImportNotice" : "ImportError"] = result.Succeeded
            ? "Dados conferidos e salvos. Revise o resumo antes de confirmar."
            : result.UserMessage("Não foi possível salvar os dados; suas informações foram preservadas.");
        return RedirectToAction(nameof(Review), new { tenantId, importId });
    }

    [HttpPost("{importId:guid}/confirmar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Confirm(Guid tenantId, Guid importId, long reviewVersion, CancellationToken ct)
    {
        var token = await HttpContext.GetTokenAsync("access_token"); if (token is null) return Challenge();
        var result = await api.ConfirmContractImportAsync(token, tenantId, importId, new ConfirmContractImport(reviewVersion, Guid.NewGuid()), ct);
        if (result.Status == ApiCallStatus.Forbidden) return Forbid();
        if (result.Succeeded && result.Value is not null)
        {
            TempData["ContractSheetNotice"] = result.Value.Repeated ? "Esta importação já estava confirmada." : "Contrato importado com rastreabilidade.";
            return RedirectToAction("Sheet", "Contracts", new { tenantId, contractId = result.Value.ContractId, from = "imports" });
        }
        TempData["ImportError"] = result.UserMessage("Não foi possível confirmar. Confira os dados e tente novamente.");
        return RedirectToAction(nameof(Review), new { tenantId, importId });
    }
}
