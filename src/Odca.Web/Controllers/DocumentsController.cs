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
        [FromQuery] int patientPage = 1,
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
        var canManagePatients = access.HasPermission("tenant.patients.manage");
        var canCreateDocument = access.HasPermission("tenant.contract_drafts.manage");

        // Load patient options and resolve selected patient if needed
        IReadOnlyList<PatientSummary> patientOptions = [];
        PatientSummary? selectedPatient = null;
        string? patientErrorMessage = null;
        int patientTotal = 0;
        int patientPageSize = 20;

        if (canReadPatients)
        {
            var patientsRes = await api.GetPatientsAsync(token, tenantId, patientSearch?.Trim(), includeInactive: true, Math.Max(1, patientPage), ct);
            if (patientsRes.Succeeded && patientsRes.Value is not null)
            {
                patientOptions = patientsRes.Value.Items;
                patientTotal = patientsRes.Value.Total;
                patientPageSize = patientsRes.Value.PageSize;
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
            CanReadPatients = canReadPatients,
            PatientPage = Math.Max(1, patientPage),
            PatientTotal = patientTotal,
            PatientPageSize = patientPageSize
        };

        return View(viewModel);
    }

    [HttpGet("novo")]
    public async Task<IActionResult> NewDocument(
        Guid tenantId,
        [FromQuery] Guid? patientId = null,
        CancellationToken ct = default)
    {
        ViewData["Title"] = "Novo documento";
        ViewData["TenantId"] = tenantId;

        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null) return Challenge();

        var access = await tenantContext.GetAccessAsync(tenantId, ct);
        if (access is null) return Forbid();

        if (!access.HasPermission("tenant.contract_drafts.manage"))
        {
            TempData["ErrorMessage"] = "Você não possui permissão para elaborar novos documentos.";
            return RedirectToAction(nameof(Index), new { tenantId });
        }

        PatientDetails? selectedPatient = null;
        if (patientId.HasValue)
        {
            var patient = await api.GetPatientAsync(token, tenantId, patientId.Value, ct);
            if (patient.Succeeded && patient.Value is not null)
            {
                if (!patient.Value.Active)
                {
                    TempData["ErrorMessage"] = "Pacientes inativos não podem ser selecionados para novas emissões.";
                    return RedirectToAction(nameof(Index), new { tenantId });
                }
                selectedPatient = patient.Value;
            }
        }

        var activePatients = new List<PatientSummary>();
        if (access.HasPermission("tenant.patients.read"))
        {
            var pRes = await api.GetPatientsAsync(token, tenantId, null, includeInactive: false, 1, ct);
            if (pRes.Succeeded && pRes.Value is not null)
            {
                activePatients.AddRange(pRes.Value.Items);
            }
            if (selectedPatient != null && !activePatients.Any(p => p.Id == selectedPatient.Id))
            {
                activePatients.Insert(0, new PatientSummary(
                    selectedPatient.Id,
                    selectedPatient.FullName,
                    selectedPatient.PreferredName,
                    !string.IsNullOrWhiteSpace(selectedPatient.IdentifierValue) ? MaskIdentifier(selectedPatient.IdentifierValue) : null,
                    selectedPatient.Active,
                    selectedPatient.Version,
                    selectedPatient.UpdatedAt));
            }
        }

        var vm = new NewDocumentViewModel
        {
            TenantId = tenantId,
            PatientId = patientId,
            Patients = activePatients,
            CanManagePatients = access.HasPermission("tenant.patients.manage")
        };

        return View(vm);
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
