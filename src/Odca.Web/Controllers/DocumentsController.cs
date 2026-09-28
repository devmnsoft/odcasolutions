using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odca.Contracts.Patients;
using Odca.Contracts.Studio;
using Odca.Web.Services;

namespace Odca.Web.Controllers;

[Authorize]
[Route("organizacoes/{tenantId:guid}/documentos")]
public sealed class DocumentsController(OdcaApiClient api) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(
        Guid tenantId,
        [FromQuery] string? search = null,
        [FromQuery] string? type = null,
        [FromQuery] string? stage = null,
        [FromQuery] Guid? patientId = null,
        [FromQuery] int page = 1,
        CancellationToken ct = default)
    {
        ViewData["Title"] = "Central de Documentos";
        ViewData["TenantId"] = tenantId;
        ViewData["Search"] = search;
        ViewData["Type"] = type;
        ViewData["Stage"] = stage;
        ViewData["PatientId"] = patientId;

        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null) return Challenge();

        var result = await api.GetStudioDocumentsAsync(token, tenantId, search, type, stage, patientId, page, 15, ct);
        if (result.Status == ApiCallStatus.Unauthorized) return Challenge();
        if (result.Status == ApiCallStatus.Forbidden) return Forbid();

        IReadOnlyList<PatientListItem> patients = [];
        var patientsRes = await api.GetPatientsAsync(token, tenantId, null, false, 1, 100, ct);
        if (patientsRes.Succeeded && patientsRes.Value is not null)
        {
            patients = patientsRes.Value.Items;
        }
        ViewData["Patients"] = patients;

        if (!result.Succeeded)
        {
            ViewData["LoadError"] = result.UserMessage("Não foi possível carregar a lista de documentos.");
            return View(new StudioDocumentPage([], Math.Max(1, page), 15, 0));
        }

        return View(result.Value!);
    }

    [HttpGet("novo")]
    public IActionResult NewDocument(
        Guid tenantId,
        [FromQuery] Guid? patientId = null)
    {
        return RedirectToAction("Index", "Studio", new { tenantId, patientId });
    }
}
