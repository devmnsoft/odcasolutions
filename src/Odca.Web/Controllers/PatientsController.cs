using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odca.Contracts.Patients;
using Odca.Web.Models;
using Odca.Web.Services;

namespace Odca.Web.Controllers;

[Authorize]
[Route("organizacoes/{tenantId:guid}/pacientes")]
public sealed class PatientsController(OdcaApiClient api) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(Guid tenantId, string? search, bool includeInactive = false, int page = 1, CancellationToken ct = default)
    {
        var token = await Token(); if (token is null) return Challenge();
        ViewData["Title"] = "Pacientes"; ViewData["TenantId"] = tenantId; ViewData["Search"] = search; ViewData["IncludeInactive"] = includeInactive;
        var result = await api.GetPatientsAsync(token, tenantId, search, includeInactive, Math.Max(1, page), ct);
        if (!result.Succeeded) { ViewData["LoadError"] = result.UserMessage("Não foi possível carregar os pacientes."); return View(new PatientPage([], page, 20, 0)); }
        return View(result.Value!);
    }

    [HttpGet("novo")]
    public IActionResult Create(Guid tenantId) { ViewData["Title"] = "Novo paciente"; ViewData["TenantId"] = tenantId; return View("Form", new PatientFormViewModel()); }

    [HttpPost("novo")]
    public async Task<IActionResult> Create(Guid tenantId, PatientFormViewModel model, CancellationToken ct)
    {
        ViewData["Title"] = "Novo paciente"; ViewData["TenantId"] = tenantId;
        if (!ModelState.IsValid) return View("Form", model);
        var token = await Token(); if (token is null) return Challenge();
        var result = await api.CreatePatientAsync(token, tenantId, model.ToRequest(), ct);
        if (!result.Succeeded) { ModelState.AddModelError(string.Empty, result.UserMessage("Revise os dados e tente novamente.")); return View("Form", model); }
        TempData["PatientSuccess"] = "Paciente cadastrado."; return RedirectToAction(nameof(Details), new { tenantId, patientId = result.Value!.Id });
    }

    [HttpGet("{patientId:guid}")]
    public async Task<IActionResult> Details(Guid tenantId, Guid patientId, string? returnUrl, int documentPage = 1, CancellationToken ct = default)
    {
        var token = await Token(); if (token is null) return Challenge();
        var patient = await api.GetPatientAsync(token, tenantId, patientId, ct);
        if (!patient.Succeeded) return patient.Status == ApiCallStatus.NotFound ? NotFound() : StatusCode(503);
        var archive = await api.GetPatientArchiveAsync(token, tenantId, patientId, documentPage, ct);
        ViewData["Title"] = patient.Value!.FullName; ViewData["TenantId"] = tenantId;
        if (!archive.Succeeded) ViewData["ArchiveError"] = archive.UserMessage("Não foi possível carregar o acervo.");
        var safeReturn = Url.IsLocalUrl(returnUrl) ? returnUrl! : Url.Action(nameof(Index), new { tenantId })!;
        return View(new PatientDetailsViewModel(patient.Value, archive.Value ?? new([], documentPage, 20, 0), safeReturn));
    }

    [HttpGet("{patientId:guid}/editar")]
    public async Task<IActionResult> Edit(Guid tenantId, Guid patientId, CancellationToken ct)
    {
        var token = await Token(); if (token is null) return Challenge(); var result = await api.GetPatientAsync(token, tenantId, patientId, ct);
        if (!result.Succeeded) return result.Status == ApiCallStatus.NotFound ? NotFound() : StatusCode(503);
        ViewData["Title"] = "Editar paciente"; ViewData["TenantId"] = tenantId; return View("Form", PatientFormViewModel.From(result.Value!));
    }

    [HttpPost("{patientId:guid}/editar")]
    public async Task<IActionResult> Edit(Guid tenantId, Guid patientId, PatientFormViewModel model, CancellationToken ct)
    {
        ViewData["Title"] = "Editar paciente"; ViewData["TenantId"] = tenantId; model.Id = patientId;
        if (!ModelState.IsValid) return View("Form", model);
        var token = await Token(); if (token is null) return Challenge(); var result = await api.UpdatePatientAsync(token, tenantId, patientId, model.ToRequest(), ct);
        if (!result.Succeeded) { ModelState.AddModelError(string.Empty, result.Status == ApiCallStatus.Conflict ? "O cadastro mudou em outra sessão. Reabra a ficha para conferir antes de salvar." : result.UserMessage("Revise os dados e tente novamente.")); return View("Form", model); }
        TempData["PatientSuccess"] = "Cadastro atualizado."; return RedirectToAction(nameof(Details), new { tenantId, patientId });
    }

    [HttpPost("{patientId:guid}/situacao")]
    public async Task<IActionResult> SetActive(Guid tenantId, Guid patientId, bool active, long expectedVersion, CancellationToken ct)
    {
        var token = await Token(); if (token is null) return Challenge(); var result = await api.SetPatientActiveAsync(token, tenantId, patientId, active, expectedVersion, ct);
        TempData[result.Succeeded ? "PatientSuccess" : "PatientError"] = result.Succeeded ? (active ? "Paciente restaurado." : "Paciente inativado; documentos e histórico foram preservados.") : result.UserMessage("A situação não pôde ser alterada. Recarregue a ficha.");
        return RedirectToAction(nameof(Details), new { tenantId, patientId });
    }

    private Task<string?> Token() => HttpContext.GetTokenAsync("access_token");
}
