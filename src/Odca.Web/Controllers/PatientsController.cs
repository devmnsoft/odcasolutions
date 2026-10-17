using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odca.Contracts.Patients;
using Odca.Configuration.Localization;
using Odca.Web.Models;
using Odca.Web.Services;

namespace Odca.Web.Controllers;

[Authorize]
[Route("organizacoes/{tenantId:guid}/pacientes")]
public sealed class PatientsController(OdcaApiClient api, LocalizationService loc) : Controller
{
    private string? Culture => HttpContext.Items[Odca.Web.Middleware.CultureMiddleware.ItemsKey] as string;
    private string L(string key, params string[] args) => loc.T(key, Culture, args);

    [HttpGet("")]
    public async Task<IActionResult> Index(Guid tenantId, string? search, bool includeInactive = false, int page = 1, CancellationToken ct = default)
    {
        var token = await Token(); if (token is null) return Challenge();
        ViewData["Title"] = L("pacientes"); ViewData["TenantId"] = tenantId; ViewData["Search"] = search; ViewData["IncludeInactive"] = includeInactive;
        var result = await api.GetPatientsAsync(token, tenantId, search, includeInactive, Math.Max(1, page), ct);
        if (result.Status == ApiCallStatus.Forbidden) { ViewData["Denied"] = true; return View(new PatientPage([], page, 20, 0)); }
        if (!result.Succeeded) { ViewData["LoadError"] = result.UserMessage(L("patient.list_load_error")); return View(new PatientPage([], page, 20, 0)); }
        return View(result.Value!);
    }

    [HttpGet("novo")]
    public IActionResult Create(Guid tenantId, [FromQuery] string? returnUrl = null)
    {
        ViewData["Title"] = L("patient.new_cta");
        ViewData["TenantId"] = tenantId;
        ViewData["ReturnUrl"] = returnUrl;
        return View("Form", new PatientFormViewModel());
    }

    [HttpPost("novo")]
    public async Task<IActionResult> Create(Guid tenantId, PatientFormViewModel model, [FromForm] string? returnUrl = null, CancellationToken ct = default)
    {
        ViewData["Title"] = L("patient.new_cta");
        ViewData["TenantId"] = tenantId;
        ViewData["ReturnUrl"] = returnUrl;
        ValidatePatientForm(model);
        if (!ModelState.IsValid) return View("Form", model);
        var token = await Token(); if (token is null) return Challenge();
        var result = await api.CreatePatientAsync(token, tenantId, model.ToRequest(), ct);
        if (!result.Succeeded) { AddPatientErrors(result, L("patient.revise_fallback")); return View("Form", model); }
        TempData["PatientSuccess"] = L("patient.created_success");
        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            var separator = returnUrl.Contains('?') ? "&" : "?";
            return Redirect($"{returnUrl}{separator}patientId={result.Value!.Id}");
        }
        return RedirectToAction(nameof(Details), new { tenantId, patientId = result.Value!.Id });
    }

    [HttpGet("{patientId:guid}")]
    public async Task<IActionResult> Details(Guid tenantId, Guid patientId, string? returnUrl, int documentPage = 1, CancellationToken ct = default)
    {
        var token = await Token(); if (token is null) return Challenge();
        var patient = await api.GetPatientAsync(token, tenantId, patientId, ct);
        if (!patient.Succeeded) return patient.Status == ApiCallStatus.NotFound ? NotFound() : StatusCode(503);
        var archive = await api.GetPatientArchiveAsync(token, tenantId, patientId, documentPage, ct);
        ViewData["Title"] = patient.Value!.FullName; ViewData["TenantId"] = tenantId;
        if (!archive.Succeeded) ViewData["ArchiveError"] = archive.UserMessage(L("patient.archive_load_error"));
        var safeReturn = Url.IsLocalUrl(returnUrl) ? returnUrl! : Url.Action(nameof(Index), new { tenantId })!;
        return View(new PatientDetailsViewModel(patient.Value, archive.Value ?? new([], documentPage, 20, 0), safeReturn));
    }

    [HttpGet("{patientId:guid}/editar")]
    public async Task<IActionResult> Edit(Guid tenantId, Guid patientId, CancellationToken ct)
    {
        var token = await Token(); if (token is null) return Challenge(); var result = await api.GetPatientAsync(token, tenantId, patientId, ct);
        if (!result.Succeeded) return result.Status == ApiCallStatus.NotFound ? NotFound() : StatusCode(503);
        ViewData["Title"] = L("patient.edit_title"); ViewData["TenantId"] = tenantId; return View("Form", PatientFormViewModel.From(result.Value!));
    }

    [HttpPost("{patientId:guid}/editar")]
    public async Task<IActionResult> Edit(Guid tenantId, Guid patientId, PatientFormViewModel model, CancellationToken ct)
    {
        ViewData["Title"] = L("patient.edit_title"); ViewData["TenantId"] = tenantId; model.Id = patientId;
        ValidatePatientForm(model);
        if (!ModelState.IsValid) return View("Form", model);
        var token = await Token(); if (token is null) return Challenge(); var result = await api.UpdatePatientAsync(token, tenantId, patientId, model.ToRequest(), ct);
        if (!result.Succeeded) { AddPatientErrors(result, result.ErrorCode == "patient.version.conflict" ? L("patient.version_conflict") : L("patient.revise_fallback")); return View("Form", model); }
        TempData["PatientSuccess"] = L("patient.updated_success"); return RedirectToAction(nameof(Details), new { tenantId, patientId });
    }

    [HttpPost("{patientId:guid}/situacao")]
    public async Task<IActionResult> SetActive(Guid tenantId, Guid patientId, bool active, long expectedVersion, CancellationToken ct)
    {
        var token = await Token(); if (token is null) return Challenge(); var result = await api.SetPatientActiveAsync(token, tenantId, patientId, active, expectedVersion, ct);
        TempData[result.Succeeded ? "PatientSuccess" : "PatientError"] = result.Succeeded ? (active ? L("patient.restored_msg") : L("patient.deactivated_msg")) : result.UserMessage(L("patient.status_change_fail"));
        return RedirectToAction(nameof(Details), new { tenantId, patientId });
    }

    private Task<string?> Token() => HttpContext.GetTokenAsync("access_token");

    // D-OC36: mensagens de validação do paciente pela camada canônica i18n (catálogo dos 4 idiomas),
    // no padrão de validação manual do Estúdio — atributos DataAnnotations permanecem apenas como
    // proteção de estrutura (sem mensagens próprias).
    private void ValidatePatientForm(PatientFormViewModel model)
    {
        if (string.IsNullOrWhiteSpace(model.FullName) || model.FullName.Length is < 2 or > 160)
        {
            ModelState.Remove(nameof(PatientFormViewModel.FullName));
            ModelState.AddModelError(nameof(PatientFormViewModel.FullName), L("patient.val_fullname"));
        }
        if (!string.IsNullOrEmpty(model.Email) && (model.Email.Length > 254 || !new EmailAddressAttribute().IsValid(model.Email)))
        {
            ModelState.Remove(nameof(PatientFormViewModel.Email));
            ModelState.AddModelError(nameof(PatientFormViewModel.Email), L("patient.val_email"));
        }
    }

    private void AddPatientErrors(ApiCallResult<PatientDetails> result, string fallback)
    {
        if (result.ErrorCode == "patient.identifier.duplicate")
        {
            ModelState.AddModelError(nameof(PatientFormViewModel.IdentifierValue), result.UserMessage(fallback));
            return;
        }

        if (result.ValidationErrors is null || result.ValidationErrors.Count == 0)
        {
            ModelState.AddModelError(string.Empty, result.UserMessage(fallback));
            return;
        }

        foreach (var (key, messages) in result.ValidationErrors)
        {
            var modelKey = PatientModelKey(key);
            foreach (var message in messages)
                ModelState.AddModelError(modelKey, message);
        }
    }

    private static string PatientModelKey(string apiKey) => apiKey switch
    {
        "fullName" => nameof(PatientFormViewModel.FullName),
        "preferredName" => nameof(PatientFormViewModel.PreferredName),
        "birthDate" => nameof(PatientFormViewModel.BirthDate),
        "email" => nameof(PatientFormViewModel.Email),
        "phone" => nameof(PatientFormViewModel.Phone),
        "address" => nameof(PatientFormViewModel.Address),
        "identifier" => nameof(PatientFormViewModel.IdentifierValue),
        "representative.fullName" => nameof(PatientFormViewModel.RepresentativeFullName),
        "representative.relationship" => nameof(PatientFormViewModel.RepresentativeRelationship),
        "representative.identifier" => nameof(PatientFormViewModel.RepresentativeIdentifierValue),
        _ => string.Empty
    };
}
