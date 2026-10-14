using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odca.Configuration.Localization;
using Odca.Contracts.Studio;
using Odca.Web.Models;
using Odca.Web.Services;

namespace Odca.Web.Controllers;

[Authorize]
[Route("organizacoes/{tenantId:guid}/estudio")]
public sealed class StudioController(OdcaApiClient api, IConfiguration configuration, IUserTenantContext tenantContext, LocalizationService loc) : Controller
{
    private string? Culture => HttpContext.Items[Odca.Web.Middleware.CultureMiddleware.ItemsKey] as string;
    private string L(string key, params string[] args) => loc.T(key, Culture, args);
    [HttpGet("")]
    public async Task<IActionResult> Index(Guid tenantId,string? search,string? type,string? scope,string? purpose,Guid? patientId,int page=1,CancellationToken ct=default)
    {
        var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Challenge();
        var access = await tenantContext.GetAccessAsync(tenantId, ct);
        var isSuperAdmin = User.IsInRole("SuperAdministrator") || User.HasClaim("role", "SuperAdministrator") || (User.FindFirst("permissions")?.Value.Contains("tenant:all") == true);
        var canManage = isSuperAdmin || (access?.HasPermission("tenant.templates.manage") ?? false);
        ViewData["Title"]=L("studio.cat_title");ViewData["TenantId"]=tenantId;ViewData["Search"]=search;ViewData["Type"]=type;ViewData["Scope"]=scope;ViewData["Purpose"]=purpose;ViewData["PatientId"]=patientId;
        ViewData["CanManageTemplates"]=canManage;
        var result=await GetCatalogAsync(token,tenantId,search,type,scope,purpose,page,ct);
        if(!result.Succeeded){if(result.Status==ApiCallStatus.Forbidden)return Forbid();ViewData["LoadError"]=result.UserMessage(L("studio.cat_load_error_title"));return View(new TemplateCatalogPage([],Math.Max(1,page),12,0));}
        return View(result.Value!);
    }

    [HttpGet("modelos/{templateId:guid}/previa")]
    public async Task<IActionResult> TemplatePreview(Guid tenantId, Guid templateId, CancellationToken ct)
    {
        ViewData["TenantId"] = tenantId;
        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null) return Challenge();
        var result = await api.GetStudioTemplatePreviewHtmlAsync(token, tenantId, templateId, ct);
        if (!result.Succeeded)
        {
            if (result.Status == ApiCallStatus.Forbidden) return Forbid();
            TempData["PreviewError"] = result.UserMessage(L("studio.ctl_preview_fail"));
            return RedirectToAction(nameof(Index), new { tenantId });
        }
        return View(result.Value!);
    }

    [HttpGet("modelos/novo")]
    public async Task<IActionResult> NewTemplate(Guid tenantId, CancellationToken ct)
    {
        var token = await HttpContext.GetTokenAsync("access_token"); if (token is null) return Challenge();
        var access = await tenantContext.GetAccessAsync(tenantId, ct);
        var isSuperAdmin = User.IsInRole("SuperAdministrator") || User.HasClaim("role", "SuperAdministrator") || (User.FindFirst("permissions")?.Value.Contains("tenant:all") == true);
        if (!isSuperAdmin && !(access?.HasPermission("tenant.templates.manage") ?? false)) return Forbid();

        var vm = new TemplateEditViewModel
        {
            TenantId = tenantId,
            Status = "draft",
            Version = 1,
            RowVersion = 1,
            ContentJson = JsonSerializer.Serialize(new
            {
                type = "document",
                content = new object[]
                {
                    new { type = "heading", level = 1, content = new object[] { new { type = "text", text = "CONTRATO DE PRESTAÇÃO DE SERVIÇOS" } } },
                    new { type = "paragraph", alignment = "justify", content = new object[] { new { type = "text", text = "Pelo presente instrumento particular, as partes qualificadas têm entre si justo e acordado o presente contrato." } } }
                }
            }),
            FieldsJson = "[]"
        };
        return View("TemplateEdit", vm);
    }

    [HttpPost("modelos/novo")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateTemplate(Guid tenantId, TemplateEditViewModel model, CancellationToken ct)
    {
        var token = await HttpContext.GetTokenAsync("access_token"); if (token is null) return Challenge();

        if (string.IsNullOrWhiteSpace(model.Name))
        {
            model.TenantId = tenantId;
            model.ErrorMessage = L("studio.ctl_name_required");
            return View("TemplateEdit", model);
        }

        JsonElement contentEl;
        try
        {
            contentEl = JsonDocument.Parse(string.IsNullOrWhiteSpace(model.ContentJson) ? "{\"type\":\"document\",\"content\":[]}" : model.ContentJson).RootElement;
        }
        catch
        {
            model.TenantId = tenantId;
            model.ErrorMessage = L("studio.ctl_content_json_invalid");
            return View("TemplateEdit", model);
        }

        JsonElement fieldsEl;
        try
        {
            fieldsEl = JsonDocument.Parse(string.IsNullOrWhiteSpace(model.FieldsJson) ? "[]" : model.FieldsJson).RootElement;
        }
        catch
        {
            model.TenantId = tenantId;
            model.ErrorMessage = L("studio.ctl_fields_json_invalid");
            return View("TemplateEdit", model);
        }

        var req = new CreateTemplateRequest(model.Name.Trim(), model.Description?.Trim(), model.ContractType, model.Scope, contentEl, fieldsEl);
        var r = await api.CreateStudioTemplateAsync(token, tenantId, req, ct);
        if (!r.Succeeded)
        {
            model.TenantId = tenantId;
            model.ErrorMessage = r.ErrorDetail ?? r.ErrorTitle ?? L("studio.ctl_create_fail");
            return View("TemplateEdit", model);
        }

        TempData["StudioSuccess"] = L("studio.ctl_create_draft_ok");
        return RedirectToAction(nameof(EditTemplate), new { tenantId, templateId = r.Value!.Id });
    }

    [HttpPost("modelos/copiar-edicao")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CopyEditableTemplate(Guid tenantId, TemplateEditViewModel model, CancellationToken ct)
    {
        var token = await HttpContext.GetTokenAsync("access_token"); if (token is null) return Challenge();
        var name = string.IsNullOrWhiteSpace(model.Name) ? "Modelo" : model.Name.Trim();
        var suffix = L("studio.copy_suffix");
        if (!name.EndsWith(suffix, StringComparison.Ordinal))
            name = $"{name} {suffix}";
        JsonElement contentEl;
        JsonElement fieldsEl;
        try
        {
            contentEl = JsonDocument.Parse(string.IsNullOrWhiteSpace(model.ContentJson) ? "{\"type\":\"document\",\"content\":[]}" : model.ContentJson).RootElement;
            fieldsEl = JsonDocument.Parse(string.IsNullOrWhiteSpace(model.FieldsJson) ? "[]" : model.FieldsJson).RootElement;
        }
        catch (JsonException)
        {
            model.TenantId = tenantId;
            model.ErrorMessage = L("studio.ctl_copy_invalid");
            return View("TemplateEdit", model);
        }

        var created = await api.CreateStudioTemplateAsync(token, tenantId, new CreateTemplateRequest(name, model.Description?.Trim(), model.ContractType, "private", contentEl, fieldsEl), ct);
        if (!created.Succeeded)
        {
            model.TenantId = tenantId;
            model.ErrorMessage = L("studio.ctl_copy_failed_prefix") + (created.ErrorDetail ?? created.ErrorTitle ?? L("common.ttry_again"));
            return View("TemplateEdit", model);
        }

        TempData["StudioSuccess"] = L("studio.ctl_copy_ok");
        return RedirectToAction(nameof(EditTemplate), new { tenantId, templateId = created.Value!.Id });
    }

    [HttpGet("modelos/{templateId:guid}/editar")]
    public async Task<IActionResult> EditTemplate(Guid tenantId, Guid templateId, CancellationToken ct)
    {
        var token = await HttpContext.GetTokenAsync("access_token"); if (token is null) return Challenge();
        var access = await tenantContext.GetAccessAsync(tenantId, ct);
        var isSuperAdmin = User.IsInRole("SuperAdministrator") || User.HasClaim("role", "SuperAdministrator") || (User.FindFirst("permissions")?.Value.Contains("tenant:all") == true);
        if (!isSuperAdmin && !(access?.HasPermission("tenant.templates.manage") ?? false)) return Forbid();

        var r = await api.GetStudioTemplateAsync(token, tenantId, templateId, ct);
        if (!r.Succeeded || r.Value is null)
        {
            TempData["StudioError"] = r.UserMessage(L("studio.ctl_load_fail"));
            return RedirectToAction(nameof(Index), new { tenantId });
        }

        var t = r.Value;
        var vm = new TemplateEditViewModel
        {
            TemplateId = t.Id,
            TenantId = tenantId,
            Name = t.Name,
            Description = t.Description,
            ContractType = t.ContractType,
            Scope = t.Scope,
            Status = t.Status,
            Version = t.Version,
            RowVersion = t.RowVersion,
            ContentJson = t.Content?.GetRawText() ?? "{\"type\":\"document\",\"content\":[]}",
            FieldsJson = t.Fields.GetRawText()
        };

        return View("TemplateEdit", vm);
    }

    [HttpPost("modelos/{templateId:guid}/salvar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveTemplate(Guid tenantId, Guid templateId, TemplateEditViewModel model, CancellationToken ct)
    {
        var token = await HttpContext.GetTokenAsync("access_token"); if (token is null) return Challenge();

        JsonElement contentEl;
        try
        {
            contentEl = JsonDocument.Parse(string.IsNullOrWhiteSpace(model.ContentJson) ? "{\"type\":\"document\",\"content\":[]}" : model.ContentJson).RootElement;
        }
        catch
        {
            model.TenantId = tenantId;
            model.TemplateId = templateId;
            model.ErrorMessage = L("studio.ctl_content_json_invalid");
            return View("TemplateEdit", model);
        }

        JsonElement fieldsEl;
        try
        {
            fieldsEl = JsonDocument.Parse(string.IsNullOrWhiteSpace(model.FieldsJson) ? "[]" : model.FieldsJson).RootElement;
        }
        catch
        {
            model.TenantId = tenantId;
            model.TemplateId = templateId;
            model.ErrorMessage = L("studio.ctl_fields_json_invalid");
            return View("TemplateEdit", model);
        }

        var req = new UpdateTemplateRequest(model.Name.Trim(), model.Description?.Trim(), model.ContractType, contentEl, fieldsEl, model.RowVersion);
        var r = await api.UpdateStudioTemplateAsync(token, tenantId, templateId, req, ct);
        if (!r.Succeeded)
        {
            model.TenantId = tenantId;
            model.TemplateId = templateId;
            if (r.Status == ApiCallStatus.Conflict)
            {
                var current = await api.GetStudioTemplateAsync(token, tenantId, templateId, ct);
                if (current.Succeeded && current.Value is not null)
                {
                    model.ServerName = current.Value.Name;
                    model.ServerDescription = current.Value.Description;
                    model.ServerRowVersion = current.Value.RowVersion;
                }
            }
            model.ErrorMessage = r.Status == ApiCallStatus.Conflict
                ? L("studio.ctl_conflict_edit")
                : r.ErrorDetail ?? r.ErrorTitle ?? L("studio.ctl_save_fail");
            return View("TemplateEdit", model);
        }

        TempData["StudioSuccess"] = L("studio.ctl_save_draft_ok");
        return RedirectToAction(nameof(EditTemplate), new { tenantId, templateId });
    }

    [HttpPost("modelos/{templateId:guid}/publicar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PublishTemplate(Guid tenantId, Guid templateId, TemplateEditViewModel model, CancellationToken ct)
    {
        var token = await HttpContext.GetTokenAsync("access_token"); if (token is null) return Challenge();
        var effectiveRowVersion = model.RowVersion > 0 
            ? model.RowVersion 
            : (long.TryParse(Request.Form["RowVersion"], out var parsed) ? parsed : 0);

        if (effectiveRowVersion <= 0)
        {
            TempData["StudioError"] = L("studio.ctl_stale_version");
            return RedirectToAction(nameof(EditTemplate), new { tenantId, templateId });
        }

        var savedBeforePublish = false;
        if (!string.IsNullOrWhiteSpace(model.Name))
        {
            JsonElement contentEl;
            try
            {
                contentEl = JsonDocument.Parse(string.IsNullOrWhiteSpace(model.ContentJson) ? "{\"type\":\"document\",\"content\":[]}" : model.ContentJson).RootElement;
            }
            catch
            {
                model.TenantId = tenantId;
                model.TemplateId = templateId;
                model.ErrorMessage = L("studio.ctl_content_structured_invalid");
                return View("TemplateEdit", model);
            }

            JsonElement fieldsEl;
            try
            {
                fieldsEl = JsonDocument.Parse(string.IsNullOrWhiteSpace(model.FieldsJson) ? "[]" : model.FieldsJson).RootElement;
            }
            catch
            {
                model.TenantId = tenantId;
                model.TemplateId = templateId;
                model.ErrorMessage = L("studio.ctl_fields_invalid");
                return View("TemplateEdit", model);
            }

            var saveReq = new UpdateTemplateRequest(model.Name.Trim(), model.Description?.Trim(), model.ContractType, contentEl, fieldsEl, effectiveRowVersion);
            var saveRes = await api.UpdateStudioTemplateAsync(token, tenantId, templateId, saveReq, ct);
            if (saveRes.Succeeded)
            {
                effectiveRowVersion = saveRes.Value!.RowVersion;
                savedBeforePublish = true;
            }
            else if (saveRes.Status == ApiCallStatus.Conflict)
            {
                var current = await api.GetStudioTemplateAsync(token, tenantId, templateId, ct);
                if (current.Succeeded && current.Value!.Status == "draft" && SameTemplatePayload(current.Value, model))
                {
                    effectiveRowVersion = current.Value.RowVersion;
                    savedBeforePublish = true;
                }
                else
                {
                    model.TenantId = tenantId;
                    model.TemplateId = templateId;
                    if (current.Succeeded && current.Value is not null)
                    {
                        model.ServerName = current.Value.Name;
                        model.ServerDescription = current.Value.Description;
                        model.ServerRowVersion = current.Value.RowVersion;
                    }
                    model.ErrorMessage = L("studio.ctl_conflict_publish");
                    return View("TemplateEdit", model);
                }
            }
            else
            {
                model.TenantId = tenantId;
                model.TemplateId = templateId;
                model.ErrorMessage = L("studio.ctl_save_failed_prefix") + (saveRes.ErrorDetail ?? saveRes.ErrorTitle ?? L("studio.ctl_retry_suffix"));
                return View("TemplateEdit", model);
            }
        }

        var r = await api.PublishStudioTemplateAsync(token, tenantId, templateId, effectiveRowVersion, ct);
        if (r.Succeeded)
        {
            TempData["StudioSuccess"] = L("studio.ctl_publish_ok", r.Value!.RowVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return RedirectToAction(nameof(Index), new { tenantId });
        }

        if (r.Status == ApiCallStatus.Conflict)
        {
            var current = await api.GetStudioTemplateAsync(token, tenantId, templateId, ct);
            if (current.Succeeded && current.Value!.Status == "published")
            {
                TempData["StudioSuccess"] = L("studio.ctl_already_published", current.Value.RowVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
                return RedirectToAction(nameof(Index), new { tenantId });
            }
        }

        model.TenantId = tenantId;
        model.TemplateId = templateId;
        model.RowVersion = effectiveRowVersion;
        model.Status = "draft";
        var errText = r.ErrorDetail ?? r.ErrorTitle ?? r.Status.ToString();
        model.ErrorMessage = savedBeforePublish
            ? L("studio.ctl_publish_saved_but_failed", effectiveRowVersion.ToString(System.Globalization.CultureInfo.InvariantCulture), errText)
            : L("studio.ctl_publish_failed", errText);
        return View("TemplateEdit", model);
    }

    private static bool SameTemplatePayload(TemplatePreview current, TemplateEditViewModel model) =>
        TemplatePayloadComparison.Same(
            current.Name,
            current.Description,
            current.ContractType,
            current.Content?.GetRawText(),
            current.Fields.GetRawText(),
            model.Name,
            model.Description,
            model.ContractType,
            model.ContentJson,
            model.FieldsJson);

    [HttpPost("modelos/{templateId:guid}/arquivar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ArchiveTemplate(Guid tenantId, Guid templateId, long rowVersion, CancellationToken ct)
    {
        var token = await HttpContext.GetTokenAsync("access_token"); if (token is null) return Challenge();
        var expectedVersion = rowVersion > 0 ? rowVersion : (long.TryParse(Request.Form["RowVersion"], out var parsed) ? parsed : 0);
        if (expectedVersion <= 0)
        {
            TempData["StudioError"] = L("studio.ctl_stale_version");
            return RedirectToAction(nameof(EditTemplate), new { tenantId, templateId });
        }
        var r = await api.ArchiveStudioTemplateAsync(token, tenantId, templateId, expectedVersion, ct);
        if (r.Succeeded)
        {
            TempData["StudioSuccess"] = L("studio.ctl_archive_ok");
        }
        else
        {
            TempData["StudioError"] = r.ErrorDetail ?? r.ErrorTitle ?? L("studio.ctl_archive_fail");
        }
        return RedirectToAction(nameof(EditTemplate), new { tenantId, templateId });
    }
    [HttpPost("biblioteca-oficial")]
    public async Task<IActionResult> InstallOfficial(Guid tenantId,string? search,string? type,string? scope,CancellationToken ct)
    {
        var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Challenge();
        var result=await SendCatalogAsync<OfficialTemplateInstallResponse>(HttpMethod.Post,$"api/v1/organizations/{tenantId}/studio/templates/official",token,ct);
        TempData[result.Succeeded?"StudioSuccess":"StudioError"]=result.Succeeded
            ? (result.Value!.Installed==0?L("studio.ctl_lib_some_present",result.Value.AlreadyPresent.ToString(System.Globalization.CultureInfo.InvariantCulture)):L("studio.ctl_lib_installed",result.Value.Installed.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            : result.ErrorDetail??result.ErrorTitle??L("studio.ctl_lib_install_fail");
        return RedirectToAction(nameof(Index),new{tenantId,search,type,scope});
    }
    [HttpPost("modelos/{templateId:guid}/duplicar")]
    public async Task<IActionResult> Duplicate(Guid tenantId,Guid templateId,string? search,string? type,string? scope,CancellationToken ct)
    {
        var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Challenge();
        var result=await SendCatalogAsync<TemplateMutationResponse>(HttpMethod.Post,$"api/v1/organizations/{tenantId}/studio/templates/{templateId}/duplicate",token,ct);
        TempData[result.Succeeded?"StudioSuccess":"StudioError"]=result.Succeeded
            ?L("studio.ctl_duplicate_ok")
            :result.ErrorDetail??result.ErrorTitle??L("studio.ctl_duplicate_fail");
        return RedirectToAction(nameof(Index),new{tenantId,search,type,scope});
    }
    [HttpPost("minutas")]
    public async Task<IActionResult> Create(Guid tenantId,Guid templateId,string title,string? reference,Guid? patientId,string? contractorSource,CancellationToken ct)
    {
        var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Challenge();var result=await api.CreateStudioDraftAsync(token,tenantId,new(templateId,title,reference,patientId,ContractorSource:string.IsNullOrWhiteSpace(contractorSource)?null:contractorSource.Trim()),ct);
        if(!result.Succeeded){TempData["StudioError"]=result.ErrorDetail??result.ErrorTitle??L("studio.ctl_draft_create_fail");return RedirectToAction(nameof(Index),new{tenantId,patientId});}
        var id=result.Value.GetProperty("id").GetGuid();return RedirectToAction(nameof(Edit),new{tenantId,draftId=id});
    }
    [HttpGet("minutas/{draftId:guid}")]
    public async Task<IActionResult> Edit(Guid tenantId,Guid draftId,CancellationToken ct)
    {
        var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Challenge();var result=await api.GetStudioDraftAsync(token,tenantId,draftId,ct);
        if(!result.Succeeded)return result.Status==ApiCallStatus.NotFound?NotFound():View("~/Views/Shared/ServiceUnavailable.cshtml");
        var conference=await api.GetDocumentConferenceAsync(token,tenantId,draftId,ct);
        ViewData["Title"]=L("studio.title_edit_page");ViewData["TenantId"]=tenantId;
        ViewData["Conference"]=conference.Value;ViewData["ConferenceError"]=conference.Succeeded?null:conference.UserMessage(L("studio.ctl_conference_load_fail"));
        return View(result.Value);
    }
    [HttpPost("minutas/{draftId:guid}/confirmar-paciente")]
    public async Task<IActionResult> ConfirmPatient(Guid tenantId,Guid draftId,long expectedDraftVersion,long expectedPatientVersion,string[]? acceptedKeys,bool acknowledgeRemaining,CancellationToken ct)
    {
        var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Challenge();
        var result=await api.ConfirmDraftPatientAsync(token,tenantId,draftId,new(expectedDraftVersion,expectedPatientVersion,acceptedKeys??[],acknowledgeRemaining),ct);
        TempData[result.Succeeded?"StudioSuccess":"StudioError"]=result.Succeeded
            ?L("studio.ctl_registry_updated")
            :result.UserMessage(L("studio.ctl_conference_redo"));
        return RedirectToAction(nameof(Edit),new{tenantId,draftId});
    }
    [HttpPut("minutas/{draftId:guid}")]
    public async Task<IActionResult> Save(Guid tenantId,Guid draftId,[FromBody] SaveDraftRequest request,CancellationToken ct)
    {var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Unauthorized();var r=await api.SaveStudioDraftAsync(token,tenantId,draftId,request,ct);if(r.Succeeded)return Json(r.Value);
     // D-OC22: a API responde 400 com ValidationProblemDetails (apenas "errors", sem title/detail);
     // sem esta propagação o Estúdio exibiria falha genérica em vez da mensagem por campo.
     string? detail=r.ErrorDetail;
     if(string.IsNullOrEmpty(detail)&&r.ValidationErrors is not null) detail=string.Join(" ",r.ValidationErrors.Values.SelectMany(v=>v));
     return StatusCode(r.Status==ApiCallStatus.Conflict?409:422,new{title=r.ErrorTitle,detail});}
    [HttpPost("minutas/{draftId:guid}/gerar")]
    public async Task<IActionResult> Generate(Guid tenantId,Guid draftId,long expectedVersion,Guid idempotencyKey,CancellationToken ct)
    {var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Challenge();var r=await api.GenerateStudioVersionAsync(token,tenantId,draftId,new(idempotencyKey,expectedVersion),ct);string?pendings=null;if(!r.Succeeded&&r.ValidationErrors is not null&&r.ValidationErrors.TryGetValue("pendingFields",out var pendingFields)&&pendingFields.Length>0)pendings=string.Join(" ",pendingFields);TempData[r.Succeeded?"StudioSuccess":"StudioError"]=r.Succeeded?L("studio.ctl_version_generated",r.Value!.Number.ToString(System.Globalization.CultureInfo.InvariantCulture),r.Value.Sha256[..12]):string.IsNullOrEmpty(pendings)?r.ErrorDetail??L("studio.ctl_fill_confirm_fields"):L("studio.ctl_generation_blocked",pendings);return RedirectToAction(nameof(Edit),new{tenantId,draftId});}
    [HttpGet("versoes/{versionId:guid}")]
    public async Task<IActionResult> Version(Guid tenantId,Guid versionId,int historyPage=1,int? beforeRevision=null,int? afterRevision=null,CancellationToken ct=default)
    {var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Challenge();var r=await api.GetStudioVersionAsync(token,tenantId,versionId,ct);if(r.Status==ApiCallStatus.Forbidden)return Forbid();if(r.Status==ApiCallStatus.NotFound)return NotFound();if(!r.Succeeded)return View("~/Views/Shared/ServiceUnavailable.cshtml");var reviewers=await api.GetStudioReviewersAsync(token,tenantId,ct);var history=await api.GetSignaturePreparationHistoryAsync(token,tenantId,versionId,historyPage,ct);ApiCallResult<SignatureCompositionComparison>? comparison=null;if(beforeRevision is int before&&afterRevision is int after)comparison=await api.CompareSignaturePreparationsAsync(token,tenantId,versionId,before,after,ct);ViewData["Title"]=L("studio.title_version_page",r.Value!.Title,r.Value.Number.ToString(System.Globalization.CultureInfo.InvariantCulture));ViewData["TenantId"]=tenantId;ViewData["Reviewers"]=reviewers.Succeeded?reviewers.Value:Array.Empty<StudioReviewerItem>();ViewData["ReviewersError"]=reviewers.Succeeded?null:reviewers.UserMessage(L("studio.ctl_reviewers_error"));ViewData["PreparationHistory"]=history.Value;ViewData["PreparationComparison"]=comparison?.Value;ViewData["PreparationComparisonError"]=comparison is {Succeeded:false}?comparison.UserMessage(L("studio.ctl_compare_compositions_fail")):null;return View(r.Value);}
    [HttpPost("versoes/{versionId:guid}/pdf")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GeneratePdf(Guid tenantId,Guid versionId,CancellationToken ct){var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Challenge();var r=await api.GenerateStudioPdfAsync(token,tenantId,versionId,ct);TempData[r.Succeeded?"StudioSuccess":"StudioError"]=r.Succeeded?L("studio.ctl_pdf_ok"):r.UserMessage(L("studio.ctl_pdf_generate_fail"));return RedirectToAction(nameof(Version),new{tenantId,versionId});}
    [HttpGet("versoes/{versionId:guid}/pdf")]
    public async Task<IActionResult> DownloadPdf(Guid tenantId,Guid versionId,CancellationToken ct){var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Challenge();using var response=await api.DownloadStudioPdfAsync(token,tenantId,versionId,ct);if(response.StatusCode==System.Net.HttpStatusCode.Forbidden)return Forbid();if(!response.IsSuccessStatusCode)return response.StatusCode==System.Net.HttpStatusCode.NotFound?NotFound():StatusCode((int)response.StatusCode);var bytes=await response.Content.ReadAsByteArrayAsync(ct);return File(bytes,"application/pdf",response.Content.Headers.ContentDisposition?.FileNameStar??"documento.pdf");}
    [HttpPost("versoes/{versionId:guid}/participantes")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveParticipants(Guid tenantId,Guid versionId,long expectedVersion,bool confirm,List<SignatureParticipantInput> participants,Guid? operationId,CancellationToken ct){var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Challenge();var r=await api.SaveSignaturePreparationAsync(token,tenantId,versionId,new(expectedVersion,confirm,participants,operationId),ct);TempData[r.Succeeded?"StudioSuccess":"StudioError"]=r.Succeeded?(confirm?L("studio.ctl_composition_confirmed"):L("studio.ctl_preparation_saved")):r.UserMessage(L("studio.ctl_preparation_save_fail"));if(!r.Succeeded&&operationId is Guid key)TempData["PreparationOperationId"]=key.ToString();return RedirectToAction(nameof(Version),new{tenantId,versionId});}
    [HttpPost("versoes/{versionId:guid}/participantes/reabrir")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReopenParticipants(Guid tenantId,Guid versionId,long expectedVersion,string justification,Guid operationId,CancellationToken ct){var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Challenge();var r=await api.ReopenSignaturePreparationAsync(token,tenantId,versionId,new(expectedVersion,justification,operationId),ct);TempData[r.Succeeded?"StudioSuccess":"StudioError"]=r.Succeeded?L("studio.ctl_reopened"):r.UserMessage(L("studio.ctl_reopen_fail"));if(!r.Succeeded){TempData["ReopenOperationId"]=operationId.ToString();TempData["ReopenJustification"]=justification;}return RedirectToAction(nameof(Version),new{tenantId,versionId});}
    [HttpPost("versoes/{versionId:guid}/solicitar-revisao")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestReview(Guid tenantId,Guid versionId,Guid reviewerId,DateTimeOffset? dueAt,string? instructions,Guid idempotencyKey,CancellationToken ct)
    {var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Challenge();var r=await api.SubmitStudioReviewAsync(token,tenantId,new(versionId,reviewerId,dueAt,instructions,idempotencyKey),ct);TempData[r.Succeeded?"StudioSuccess":"StudioError"]=r.Succeeded?L("studio.ctl_review_requested"):r.UserMessage(L("studio.ctl_review_request_fail"));if(!r.Succeeded){TempData["ReviewReviewerId"]=reviewerId.ToString();TempData["ReviewDueAt"]=dueAt?.ToString("O");TempData["ReviewInstructions"]=instructions;TempData["ReviewIdempotencyKey"]=idempotencyKey.ToString();}return r.Succeeded?RedirectToAction("Detail","Reviews",new{tenantId,reviewId=r.Value!.ReviewId}):RedirectToAction(nameof(Version),new{tenantId,versionId});}
    [HttpPost("minutas/{draftId:guid}/gerar-json")]
    public async Task<IActionResult> GenerateJson(Guid tenantId,Guid draftId,[FromBody]GenerateVersionRequest request,CancellationToken ct){var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Unauthorized();var r=await api.GenerateStudioVersionAsync(token,tenantId,draftId,request,ct);return r.Succeeded?Json(r.Value):StatusCode(r.Status==ApiCallStatus.Conflict?409:422,new{title=r.ErrorTitle,detail=r.ErrorDetail});}
    [HttpGet("revisores")]
    public async Task<IActionResult> Reviewers(Guid tenantId,CancellationToken ct){var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Unauthorized();var r=await api.GetStudioReviewersAsync(token,tenantId,ct);return r.Succeeded?Json(r.Value):StatusCode(422,new{title=r.ErrorTitle});}
    [HttpPost("revisoes")]
    public async Task<IActionResult> SubmitReview(Guid tenantId,[FromBody]SubmitReviewRequest request,CancellationToken ct){var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Unauthorized();var r=await api.SubmitStudioReviewAsync(token,tenantId,request,ct);return r.Succeeded?Json(r.Value):StatusCode(r.Status==ApiCallStatus.Conflict?409:422,new{title=r.ErrorTitle,detail=r.ErrorDetail});}
    [HttpGet("minutas/{draftId:guid}/versoes")]
    public async Task<IActionResult> Versions(Guid tenantId,Guid draftId,CancellationToken ct){var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Unauthorized();var r=await api.GetStudioVersionsAsync(token,tenantId,draftId,ct);return r.Succeeded?Json(r.Value):StatusCode(422,new{title=r.ErrorTitle});}
    [HttpGet("comparar")]
    public async Task<IActionResult> Compare(Guid tenantId,Guid before,Guid after,CancellationToken ct){var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Unauthorized();var r=await api.CompareStudioVersionsAsync(token,tenantId,before,after,ct);return r.Succeeded?Json(r.Value):StatusCode(r.Status==ApiCallStatus.NotFound?404:422,new{title=r.ErrorTitle,detail=r.ErrorDetail});}
    [HttpGet("comparar-versoes")]
    public async Task<IActionResult> CompareVisual(Guid tenantId,Guid draftId,Guid? before=null,Guid? after=null,CancellationToken ct=default)
    {
        var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Challenge();
        var versionsResult=await api.GetStudioVersionsAsync(token,tenantId,draftId,ct);
        if(versionsResult.Status==ApiCallStatus.Unauthorized)return Challenge();
        if(versionsResult.Status==ApiCallStatus.Forbidden)return Forbid();
        if(!versionsResult.Succeeded||versionsResult.Value is null){Response.StatusCode=503;return View("ServiceUnavailable");}
        var versions=versionsResult.Value.OrderByDescending(v=>v.Number).ThenByDescending(v=>v.CreatedAt).ToList();
        ViewData["TenantId"]=tenantId;ViewData["DraftId"]=draftId;ViewData["Title"]=L("studio.cmpv_title");
        if(versions.Count>=2)
        {
            var afterId=after??versions[0].Id;
            var beforeId=before??versions[1].Id;
            if(!versions.Any(v=>v.Id==afterId))afterId=versions[0].Id;
            if(!versions.Any(v=>v.Id==beforeId))beforeId=versions[1].Id;
            var cmp=await api.CompareStudioVersionsAsync(token,tenantId,beforeId,afterId,ct);
            if(cmp.Succeeded&&cmp.Value is not null)return View("CompareVersions",new CompareVersionsViewModel(cmp.Value,versions,beforeId,afterId));
        }
        return View("CompareVersions",new CompareVersionsViewModel(null,versions));
    }
    [HttpGet("minutas/{draftId:guid}/checklist")]
    public async Task<IActionResult> Checklist(Guid tenantId,Guid draftId,long version,CancellationToken ct){var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Unauthorized();var r=await api.GetStudioChecklistAsync(token,tenantId,draftId,version,ct);return r.Succeeded?Json(r.Value):StatusCode(422,new{title=r.ErrorTitle});}
    [HttpGet("minutas/{draftId:guid}/comentarios")]
    public async Task<IActionResult> Comments(Guid tenantId,Guid draftId,bool resolved,CancellationToken ct){var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Unauthorized();var r=await api.GetStudioCommentsAsync(token,tenantId,draftId,resolved,ct);return r.Succeeded?Json(r.Value):StatusCode(422,new{title=r.ErrorTitle});}
    [HttpPost("minutas/{draftId:guid}/comentarios")]
    public async Task<IActionResult> AddComment(Guid tenantId,Guid draftId,[FromBody]CreateStudioCommentRequest request,CancellationToken ct){var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Unauthorized();var r=await api.AddStudioCommentAsync(token,tenantId,draftId,request,ct);return r.Succeeded?Json(r.Value):StatusCode(422,new{title=r.ErrorTitle,detail=r.ErrorDetail});}
    [HttpPost("minutas/{draftId:guid}/comentarios/{commentId:guid}/resolve")]
    public Task<IActionResult> ResolveCommentAsync(Guid tenantId,Guid draftId,Guid commentId,[FromBody]ChangeStudioCommentStateRequest request,CancellationToken ct)=>ChangeCommentStateAsync(tenantId,draftId,commentId,"resolve",request,ct);
    [HttpPost("minutas/{draftId:guid}/comentarios/{commentId:guid}/reopen")]
    public Task<IActionResult> ReopenCommentAsync(Guid tenantId,Guid draftId,Guid commentId,[FromBody]ChangeStudioCommentStateRequest request,CancellationToken ct)=>ChangeCommentStateAsync(tenantId,draftId,commentId,"reopen",request,ct);
    private async Task<IActionResult> ChangeCommentStateAsync(Guid tenantId,Guid draftId,Guid commentId,string operation,ChangeStudioCommentStateRequest request,CancellationToken ct){var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Unauthorized();var r=await api.SetStudioCommentStateAsync(token,tenantId,draftId,commentId,operation,request,ct);return r.Succeeded?NoContent():StatusCode(r.Status==ApiCallStatus.Conflict?409:422,new{title=r.ErrorTitle,detail=r.ErrorDetail});}

    private async Task<ApiCallResult<TemplateCatalogPage>> GetCatalogAsync(string token,Guid tenantId,string? search,string? type,string? scope,string? purpose,int page,CancellationToken ct)
    {
        var query=new List<string>{$"page={page}"};
        if(!string.IsNullOrWhiteSpace(search))query.Add($"search={Uri.EscapeDataString(search)}");
        if(!string.IsNullOrWhiteSpace(type))query.Add($"type={Uri.EscapeDataString(type)}");
        if(!string.IsNullOrWhiteSpace(scope))query.Add($"scope={Uri.EscapeDataString(scope)}");
        if(!string.IsNullOrWhiteSpace(purpose))query.Add($"purpose={Uri.EscapeDataString(purpose)}");
        return await SendCatalogAsync<TemplateCatalogPage>(HttpMethod.Get,$"api/v1/organizations/{tenantId}/studio/templates?{string.Join('&',query)}",token,ct);
    }

    private async Task<ApiCallResult<T>> SendCatalogAsync<T>(HttpMethod method,string path,string token,CancellationToken ct)
    {
        var baseUrl=configuration["Api:BaseUrl"]??throw new InvalidOperationException(L("studio.ctl_api_base_url_missing"));
        using var http=new HttpClient{BaseAddress=new Uri(baseUrl),Timeout=TimeSpan.FromSeconds(15)};
        using var message=new HttpRequestMessage(method,path);
        message.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
        using var response=await http.SendAsync(message,ct);
        if(response.IsSuccessStatusCode)
        {
            var value=await response.Content.ReadFromJsonAsync<T>(cancellationToken:ct);
            return new ApiCallResult<T>(ApiCallStatus.Success,value);
        }
        var status=response.StatusCode==System.Net.HttpStatusCode.Forbidden?ApiCallStatus.Forbidden:response.StatusCode==System.Net.HttpStatusCode.Conflict?ApiCallStatus.Conflict:response.StatusCode==System.Net.HttpStatusCode.NotFound?ApiCallStatus.NotFound:ApiCallStatus.InvalidRequest;
        return new ApiCallResult<T>(status,default,L("studio.ctl_operation_incomplete"));
    }
}
