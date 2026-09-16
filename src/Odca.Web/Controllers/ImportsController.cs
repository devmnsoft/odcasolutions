using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odca.Web.Services;
using Odca.Contracts.Imports;

namespace Odca.Web.Controllers;

[Authorize]
[Route("organizacoes/{tenantId:guid}/importacoes")]
public sealed class ImportsController(OdcaApiClient api) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(Guid tenantId, string? status = null, bool mine = false, bool awaitingReview = false, CancellationToken ct = default)
    {
        var token = await HttpContext.GetTokenAsync("access_token"); if (token is null) return Challenge();
        var result = await api.GetContractImportsAsync(token, tenantId, status, mine, awaitingReview, ct);
        if (!result.Succeeded) return result.Status == ApiCallStatus.Forbidden ? Forbid() : View("~/Views/Shared/ServiceUnavailable.cshtml");
        ViewData["Title"] = "Central de importações"; ViewData["TenantId"] = tenantId; ViewData["Status"] = status;
        ViewData["Mine"] = mine; ViewData["AwaitingReview"] = awaitingReview;
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
}
