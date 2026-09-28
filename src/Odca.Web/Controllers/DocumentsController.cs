using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odca.Contracts.Patients;
using Odca.Contracts.Studio;
using Odca.Web.Models;
using Odca.Web.Services;

namespace Odca.Web.Controllers;

[Authorize]
[Route("organizacoes/{tenantId:guid}/documentos")]
public sealed class DocumentsController(OdcaApiClient api, IUserTenantContext tenantContext) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(
        Guid tenantId,
        [FromQuery] string? search = null,
        [FromQuery] string? type = null,
        [FromQuery] string? stage = null,
        [FromQuery] Guid? patientId = null,
        [FromQuery] string? patientSearch = null,
        [FromQuery] int page = 1,
        CancellationToken ct = default)
    {
        ViewData["Title"] = "Central de Documentos";
        ViewData["TenantId"] = tenantId;

        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null) return Challenge();

        var access = await tenantContext.GetAccessAsync(tenantId, ct);
        if (access is null) return Forbid();

        var canReadPatients = access.HasPermission("tenant.patients.read");
        var canManagePatients = access.HasAnyPermission("tenant.patients.manage", "tenant.patients.read");
        var canCreateDocument = access.HasAnyPermission("tenant.contract_drafts.manage", "tenant.templates.read");

        // Load patient options and resolve selected patient if needed
        IReadOnlyList<PatientSummary> patientOptions = [];
        PatientSummary? selectedPatient = null;
        string? patientErrorMessage = null;

        if (canReadPatients)
        {
            var patientsRes = await api.GetPatientsAsync(token, tenantId, patientSearch?.Trim(), includeInactive: true, 1, ct);
            if (patientsRes.Succeeded && patientsRes.Value is not null)
            {
                patientOptions = patientsRes.Value.Items;
            }
            else if (!patientsRes.Succeeded && patientsRes.Status != ApiCallStatus.Forbidden)
            {
                patientErrorMessage = patientsRes.UserMessage("Não foi possível carregar a lista de pacientes.");
            }

            if (patientId.HasValue)
            {
                selectedPatient = patientOptions.FirstOrDefault(p => p.Id == patientId.Value);
                if (selectedPatient is null)
                {
                    // Fetch directly so selected patient is preserved even if on another page
                    var singlePatientRes = await api.GetPatientAsync(token, tenantId, patientId.Value, ct);
                    if (singlePatientRes.Succeeded && singlePatientRes.Value is not null)
                    {
                        var det = singlePatientRes.Value;
                        selectedPatient = new PatientSummary(
                            det.Id,
                            det.FullName,
                            det.PreferredName,
                            string.IsNullOrWhiteSpace(det.IdentifierValue) ? null : MaskIdentifier(det.IdentifierValue),
                            det.Active,
                            det.Version,
                            det.UpdatedAt);
                    }
                }
            }
        }

        // Query studio documents
        var result = await api.GetStudioDocumentsAsync(token, tenantId, search?.Trim(), type?.Trim(), stage?.Trim(), patientId, Math.Max(1, page), 15, ct);
        if (result.Status == ApiCallStatus.Unauthorized) return Challenge();
        if (result.Status == ApiCallStatus.Forbidden) return Forbid();

        string? errorMessage = null;
        StudioDocumentPage documentsPage;
        if (!result.Succeeded)
        {
            errorMessage = result.UserMessage("Não foi possível carregar a lista de documentos.");
            documentsPage = new StudioDocumentPage([], Math.Max(1, page), 15, 0);
        }
        else
        {
            documentsPage = result.Value!;
        }

        var viewModel = new DocumentsIndexViewModel
        {
            TenantId = tenantId,
            Documents = documentsPage,
            Search = search,
            Type = type,
            Stage = stage,
            PatientId = patientId,
            PatientSearch = patientSearch,
            PatientOptions = patientOptions,
            SelectedPatient = selectedPatient,
            ErrorMessage = errorMessage,
            PatientErrorMessage = patientErrorMessage,
            CanCreateDocument = canCreateDocument,
            CanManagePatients = canManagePatients,
            CanReadPatients = canReadPatients
        };

        return View(viewModel);
    }

    [HttpGet("novo")]
    public async Task<IActionResult> NewDocument(
        Guid tenantId,
        [FromQuery] Guid? patientId = null,
        CancellationToken ct = default)
    {
        if (patientId.HasValue)
        {
            var token = await HttpContext.GetTokenAsync("access_token");
            if (token is not null)
            {
                var patient = await api.GetPatientAsync(token, tenantId, patientId.Value, ct);
                if (patient.Succeeded && patient.Value is not null && !patient.Value.Active)
                {
                    TempData["ErrorMessage"] = "Pacientes inativos não podem ser selecionados para novas emissões.";
                    return RedirectToAction(nameof(Index), new { tenantId });
                }
            }
        }
        return RedirectToAction("Index", "Studio", new { tenantId, patientId });
    }

    private static string MaskIdentifier(string value)
    {
        var clean = value.Trim();
        if (clean.Length == 11) // CPF
        {
            return $"{clean[..3]}.***.***-{clean[^2..]}";
        }
        return clean.Length > 4 ? $"***{clean[^4..]}" : clean;
    }
}
