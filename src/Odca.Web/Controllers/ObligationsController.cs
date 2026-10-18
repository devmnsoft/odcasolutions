using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Odca.Contracts.Obligations;
using Odca.Configuration.Localization;
using Odca.Web.Services;
using Odca.Contracts.SavedViews;
using Odca.Web.Models;

namespace Odca.Web.Controllers;

[Authorize]
[Route("organizacoes/{tenantId:guid}/obrigacoes")]
public sealed class ObligationsController(OdcaApiClient api, LocalizationService loc) : Controller
{
    // D-OC41 (S7): padrão canônico de cultura (CultureMiddleware) — Controller.Culture não é populado aqui.
    private string? Culture => HttpContext.Items[Odca.Web.Middleware.CultureMiddleware.ItemsKey] as string;

    private string L(string key, params string[] args) => loc.T(key, Culture, args);
    [HttpPost("vistas")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveView(Guid tenantId, string name, string scope = "mine", Guid? contractId = null, Guid? ownerId = null, string? category = null, string? status = null, DateOnly? from = null, DateOnly? to = null, string? search = null, CancellationToken ct = default)
    {
        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null) return Challenge();
        var filters = new Dictionary<string, string>();
        Add(filters, "scope", scope); Add(filters, "contractId", contractId?.ToString()); Add(filters, "ownerId", ownerId?.ToString());
        Add(filters, "category", category); Add(filters, "status", status); Add(filters, "from", from?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        Add(filters, "to", to?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)); Add(filters, "search", search);
        var result = await api.CreateSavedViewAsync(token, tenantId, new SaveViewRequest(name, "obligations", filters, "due_date"), ct);
        if (result.Status == ApiCallStatus.Unauthorized) return Challenge();
        if (result.Status == ApiCallStatus.Forbidden) return Forbid();
        TempData[result.Succeeded ? "SavedViewNotice" : "SavedViewError"] = result.Succeeded
            ? L("obl.view_saved")
            : L("obl.view_save_fail");
        return RedirectToAction(nameof(Index), new { tenantId, scope, contractId, ownerId, category, status, from, to, search });
    }

    [HttpGet("vistas/{viewId:guid}")]
    public async Task<IActionResult> OpenView(Guid tenantId, Guid viewId, CancellationToken ct)
    {
        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null) return Challenge();
        var result = await api.GetSavedViewAsync(token, tenantId, viewId, ct);
        if (result.Status == ApiCallStatus.Unauthorized) return Challenge();
        if (result.Status == ApiCallStatus.Forbidden) return Forbid();
        if (!result.Succeeded || result.Value is null)
        {
            TempData["SavedViewError"] = L("obl.view_unavailable");
            return RedirectToAction(nameof(Index), new { tenantId });
        }
        var route = BuildSavedViewRoute(tenantId, result.Value);
        return RedirectToAction(nameof(Index), route);
    }

    [HttpPost("vistas/{viewId:guid}/atualizar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateView(Guid tenantId, Guid viewId, string name, bool isDefault, long version, bool replaceFilters = false, string scope = "mine", Guid? contractId = null, Guid? ownerId = null, string? category = null, string? status = null, DateOnly? from = null, DateOnly? to = null, string? search = null, CancellationToken ct = default)
    {
        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null) return Challenge();
        Dictionary<string, string>? filters = null;
        if (replaceFilters)
        {
            filters = BuildFilters(scope, contractId, ownerId, category, status, from, to, search);
        }
        var result = await api.UpdateSavedViewAsync(token, tenantId, viewId, new UpdateSavedViewRequest(name, isDefault, version, filters), ct);
        if (result.Status == ApiCallStatus.Unauthorized) return Challenge();
        if (result.Status == ApiCallStatus.Forbidden) return Forbid();
        TempData[result.Succeeded ? "SavedViewNotice" : "SavedViewError"] = result.Succeeded ? L("obl.view_updated") : L("obl.view_changed");
        return RedirectToAction(nameof(OpenView), new { tenantId, viewId });
    }

    [HttpPost("vistas/{viewId:guid}/inativar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeactivateView(Guid tenantId, Guid viewId, CancellationToken ct)
    {
        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null) return Challenge();
        var result = await api.DeactivateSavedViewAsync(token, tenantId, viewId, ct);
        if (result.Status == ApiCallStatus.Unauthorized) return Challenge();
        if (result.Status == ApiCallStatus.Forbidden) return Forbid();
        TempData[result.Succeeded ? "SavedViewNotice" : "SavedViewError"] = result.Succeeded ? L("obl.view_deactivated") : L("obl.view_deactivate_fail");
        return RedirectToAction(nameof(Index), new { tenantId });
    }

    [HttpGet("{id:guid}/historico")]
    public async Task<IActionResult> History(Guid tenantId, Guid id, CancellationToken ct)
    {
        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null) return Unauthorized();
        var result = await api.GetObligationHistoryAsync(token, tenantId, id, ct);
        return result.Status switch
        {
            ApiCallStatus.Unauthorized => Unauthorized(),
            ApiCallStatus.Forbidden => Forbid(),
            ApiCallStatus.NotFound => NotFound(),
            _ when result.Succeeded => Json(result.Value),
            _ => StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = L("obl.history_load_fail") })
        };
    }

    // D-OC38 (Sprint 4/A-1): cadastro e ações de obrigações na central do cliente.
    // D-OC41 (S7): telas localizadas em 4 idiomas via catálogos; a API permanece autoridade
    // de validação, permissão, versão e auditoria; o BFF preserva valores após rejeição e conflito 409.

    [HttpGet("nova")]
    public async Task<IActionResult> Nova(Guid tenantId, CancellationToken ct)
    {
        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null) return Challenge();
        ViewData["TenantId"] = tenantId;
        var (contracts, members, catalogStatus) = await LoadObligationCatalogsAsync(token, tenantId, ct);
        if (contracts is null) return catalogStatus == ApiCallStatus.Forbidden ? Forbid() : View("ServiceUnavailable");
        return View(new ObligationCreateModel { Contracts = contracts, Members = members });
    }

    [HttpPost("nova")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Guid tenantId, ObligationCreateModel model, CancellationToken ct)
    {
        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null) return Challenge();
        ViewData["TenantId"] = tenantId;
        model.ReminderDays ??= [];
        if (model.ContractId is null || string.IsNullOrWhiteSpace(model.Title) || string.IsNullOrWhiteSpace(model.ObligatedParty) || model.OwnerId is null || model.DueDate is null)
        {
            model.ErrorMessage = L("obl.create_required");
            var (c1, m1, _) = await LoadObligationCatalogsAsync(token, tenantId, ct);
            model.Contracts = c1 ?? []; model.Members = m1 ?? [];
            return View("Nova", model);
        }
        var recurrence = model.RecurrenceEnabled && model.RecurrenceBaseDate is not null
            ? new MonthlyRecurrenceRequest(model.RecurrenceBaseDate.Value, model.RecurrenceIntendedDay, model.RecurrenceEndsOn, model.RecurrenceOccurrenceCount, string.IsNullOrWhiteSpace(model.RecurrenceTimeZone) ? "America/Sao_Paulo" : model.RecurrenceTimeZone.Trim())
            : null;
        var reminders = model.ReminderDays.Where(x => int.TryParse(x, out var d) && d is >= 0 and <= 365).Select(int.Parse).Distinct().ToArray();
        var result = await api.CreateObligationAsync(token, tenantId, model.ContractId.Value, new CreateObligationRequest(
            model.Title.Trim(), string.IsNullOrWhiteSpace(model.Description) ? null : model.Description.Trim(),
            model.Category, model.ObligatedParty.Trim(), model.OwnerId.Value, model.DueDate.Value, model.Priority, "manual",
            model.Amount, string.IsNullOrWhiteSpace(model.Currency) ? null : model.Currency.Trim().ToUpperInvariant(),
            model.ClauseDocumentVersionId, model.EvidenceRequired,
            string.IsNullOrWhiteSpace(model.PostTermReason) ? null : model.PostTermReason.Trim(),
            recurrence, reminders.Length > 0 ? reminders : null), ct);
        if (result.Status == ApiCallStatus.Unauthorized) return Challenge();
        if (result.Status == ApiCallStatus.Forbidden) return Forbid();
        if (result.Succeeded)
        {
            int count = 1;
            if (result.Value is System.Text.Json.JsonElement el && el.ValueKind == System.Text.Json.JsonValueKind.Object && el.TryGetProperty("ids", out var ids) && ids.ValueKind == System.Text.Json.JsonValueKind.Array) count = ids.GetArrayLength();
            TempData["ObligationNotice"] = count > 1
                ? L("obl.created_series", count.ToString(System.Globalization.CultureInfo.InvariantCulture))
                : L("obl.created_single");
            return RedirectToAction(nameof(Index), new { tenantId });
        }
        model.ErrorMessage = result.UserMessage(L("obl.create_fail"));
        var (c2, m2, _) = await LoadObligationCatalogsAsync(token, tenantId, ct);
        model.Contracts = c2 ?? []; model.Members = m2 ?? [];
        return View("Nova", model);
    }

    /// <summary>D-OC38 (Sprint 4/A-1): opções de versão de documento do contrato para o seletor
    /// de cláusula — mantido em JSON para não perder os demais campos já digitados no formulário.</summary>
    [HttpGet("nova/documentos")]
    public async Task<IActionResult> DocumentOptions(Guid tenantId, Guid contractId, CancellationToken ct)
    {
        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null) return Unauthorized();
        var r = await api.GetContractDocumentVersionsAsync(token, tenantId, contractId, ct);
        if (!r.Succeeded) return StatusCode(503, new { title = L("obl.docs_load_fail") });
        return Json(ParseDocumentOptions(r.Value!));
    }

    [HttpGet("{id:guid}/acoes")]
    public async Task<IActionResult> Actions(Guid tenantId, Guid id, CancellationToken ct)
    {
        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null) return Challenge();
        ViewData["TenantId"] = tenantId;
        var vm = await ReloadActionModelAsync(token, tenantId, id, ct);
        if (vm is null)
        {
            var probe = await api.GetObligationAsync(token, tenantId, id, ct);
            return probe.Status switch
            {
                ApiCallStatus.Unauthorized => Challenge(),
                ApiCallStatus.Forbidden => Forbid(),
                ApiCallStatus.NotFound => NotFound(),
                _ => View("ServiceUnavailable")
            };
        }
        return View("Acoes", vm);
    }

    [HttpPost("{id:guid}/acoes")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PerformAction(Guid tenantId, Guid id, string operation, long expectedVersion, string? reason, string? note, DateOnly? dueDate, Guid? ownerId, Guid? evidenceDocumentVersionId, CancellationToken ct)
    {
        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null) return Challenge();
        ViewData["TenantId"] = tenantId;
        var fieldError = (operation, string.IsNullOrWhiteSpace(reason), dueDate.HasValue, ownerId.HasValue) switch
        {
            ("cancel", true, _, _) or ("reopen", true, _, _) => L("obl.reason_required"),
            ("reschedule", true, false, _) => L("obl.reschedule_required"),
            ("reassign", true, _, false) => L("obl.reassign_required"),
            _ => null
        };
        var body = new ObligationActionRequest(expectedVersion, reason, note, null,
            operation == "fulfill" ? evidenceDocumentVersionId : null,
            operation == "reassign" ? ownerId : null,
            operation == "reschedule" ? dueDate : null);
        var (status, errorTitle, errorCode, currentVersion) = await api.PerformObligationActionDetailedAsync(token, tenantId, id, operation, body, ct);
        switch (status)
        {
            case ApiCallStatus.Success:
                TempData["ObligationNotice"] = OperationNotice(operation);
                return RedirectToAction(nameof(Index), new { tenantId });
            case ApiCallStatus.InvalidCredentials or ApiCallStatus.Unauthorized:
                return Challenge();
            case ApiCallStatus.Forbidden:
                return Forbid();
            case ApiCallStatus.NotFound:
                TempData["ObligationError"] = L("obl.not_available");
                return RedirectToAction(nameof(Index), new { tenantId });
            case ApiCallStatus.Conflict when currentVersion.HasValue:
                return await RebuildActionsViewAsync(token, tenantId, id, new ObligationActionModel
                {
                    ConflictMessage = L("obl.conflict_msg"),
                    OperationError = fieldError,
                    Reason = reason, Note = note, DueDate = dueDate, OwnerId = ownerId, EvidenceDocumentVersionId = evidenceDocumentVersionId
                }, ct);
            case ApiCallStatus.InvalidRequest:
                return await RebuildActionsViewAsync(token, tenantId, id, new ObligationActionModel
                {
                    OperationError = fieldError ?? errorTitle ?? L("obl.invalid_action"),
                    Reason = reason, Note = note, DueDate = dueDate, OwnerId = ownerId, EvidenceDocumentVersionId = evidenceDocumentVersionId
                }, ct);
            default:
                return View("ServiceUnavailable");
        }
    }

    private async Task<(ObligationContractOption[]? Contracts, ObligationMemberOption[] Members, ApiCallStatus? Status)> LoadObligationCatalogsAsync(string token, Guid tenantId, CancellationToken ct)
    {
        var contractsResult = await api.GetContractsAsync(token, tenantId, null, "active", 1, ct);
        var membersResult = await api.GetMembersAsync(token, tenantId, null, "active", 1, 50, ct);
        if (contractsResult.Status == ApiCallStatus.Unauthorized || membersResult.Status == ApiCallStatus.Unauthorized) return ([], [], ApiCallStatus.Unauthorized);
        if (contractsResult.Status == ApiCallStatus.Forbidden || membersResult.Status == ApiCallStatus.Forbidden) return ([], [], ApiCallStatus.Forbidden);
        var contracts = contractsResult.Succeeded ? contractsResult.Value!.Items.Select(x => new ObligationContractOption(x.Id, x.Title, x.EndDate?.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture))).ToArray() : null;
        var members = membersResult.Succeeded ? membersResult.Value!.Items.Select(x => new ObligationMemberOption(x.UserId, x.Name, x.Email)).ToArray() : [];
        return (contracts, members, contracts is null ? ApiCallStatus.Unavailable : null);
    }

    private async Task<ObligationActionModel?> ReloadActionModelAsync(string token, Guid tenantId, Guid id, CancellationToken ct)
    {
        var result = await api.GetObligationAsync(token, tenantId, id, ct);
        if (!result.Succeeded) return null;
        var item = result.Value!;
        var docsResult = await api.GetContractDocumentVersionsAsync(token, tenantId, item.ContractId, ct);
        var membersResult = await api.GetMembersAsync(token, tenantId, null, "active", 1, 50, ct);
        return new ObligationActionModel
        {
            Item = item,
            ExpectedVersion = item.Version,
            Documents = docsResult.Succeeded ? ParseDocumentOptions(docsResult.Value!) : [],
            Members = membersResult.Succeeded ? membersResult.Value!.Items.Select(x => new ObligationMemberOption(x.UserId, x.Name, x.Email)).ToArray() : []
        };
    }

    private async Task<IActionResult> RebuildActionsViewAsync(string token, Guid tenantId, Guid id, ObligationActionModel preset, CancellationToken ct)
    {
        var vm = await ReloadActionModelAsync(token, tenantId, id, ct);
        if (vm is null) return View("ServiceUnavailable");
        vm.ConflictMessage = preset.ConflictMessage;
        vm.OperationError = preset.OperationError;
        vm.Reason = preset.Reason;
        vm.Note = preset.Note;
        vm.DueDate = preset.DueDate;
        vm.OwnerId = preset.OwnerId;
        vm.EvidenceDocumentVersionId = preset.EvidenceDocumentVersionId;
        return View("Acoes", vm);
    }

    private List<ObligationDocumentOption> ParseDocumentOptions(System.Text.Json.JsonElement[] versions)
    {
        string Str(System.Text.Json.JsonElement e, string snake, string camel)
        {
            if (e.TryGetProperty(snake, out var s) && s.ValueKind == System.Text.Json.JsonValueKind.String) return s.GetString() ?? "";
            if (e.TryGetProperty(camel, out var c) && c.ValueKind == System.Text.Json.JsonValueKind.String) return c.GetString() ?? "";
            return "";
        }
        int Num(System.Text.Json.JsonElement e, string snake, string camel)
        {
            if (e.TryGetProperty(snake, out var s) && s.ValueKind == System.Text.Json.JsonValueKind.Number) return s.GetInt32();
            if (e.TryGetProperty(camel, out var c) && c.ValueKind == System.Text.Json.JsonValueKind.Number) return c.GetInt32();
            return 0;
        }
        var list = new List<ObligationDocumentOption>();
        foreach (var v in versions)
        {
            var versionIdStr = Str(v, "version_id", "versionId");
            if (!Guid.TryParse(versionIdStr, out var versionId)) continue;
            var safe = Str(v, "security_status", "securityStatus") == "safe";
            var number = Num(v, "version_number", "versionNumber");
            var title = Str(v, "title", "title");
            list.Add(new ObligationDocumentOption(versionId, $"{(string.IsNullOrWhiteSpace(title) ? L("obl.doc_generic") : title)} · v{number}{(safe ? "" : L("obl.doc_under_review"))}", safe));
        }
        return list;
    }

    private string OperationNotice(string operation) => operation switch
    {
        "start" => L("obl.notice_start"),
        "fulfill" => L("obl.notice_fulfill"),
        "cancel" => L("obl.notice_cancel"),
        "reopen" => L("obl.notice_reopen"),
        "reschedule" => L("obl.notice_reschedule"),
        _ => L("obl.notice_reassign")
    };

    [HttpGet]
    public async Task<IActionResult> Index(Guid tenantId,string scope="mine",Guid? contractId=null,Guid? ownerId=null,string? category=null,string? status=null,DateOnly? from=null,DateOnly? to=null,string? search=null,Guid? viewId=null,int page=1,CancellationToken ct=default)
    {
        if (from.HasValue && to.HasValue && from.Value > to.Value)
        {
            ModelState.AddModelError(nameof(to), L("obl.date_range_invalid"));
            ViewData["TenantId"] = tenantId;
            ViewData["Scope"] = scope;
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return View(new ObligationWorkspaceViewModel(new Odca.Contracts.Obligations.ObligationPage([], 1, 20, 0), []));
        }
        var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Challenge();
        var savedViews = await api.GetSavedViewsAsync(token, tenantId, "obligations", ct);
        var views = savedViews.Succeeded ? savedViews.Value! : [];
        var currentView = viewId.HasValue ? views.FirstOrDefault(x => x.Id == viewId) : null;
        // Explicit query filters (including dashboard links) always win over the personal default.
        if (!viewId.HasValue && !HasExplicitFilters(Request.Query) && views.FirstOrDefault(x => x.IsDefault) is { } defaultView)
            return RedirectToAction(nameof(OpenView), new { tenantId, viewId = defaultView.Id });
        var result=await api.GetObligationsAsync(token,tenantId,scope,contractId,ownerId,category,status,from,to,search,page,20,ct);
        if(result.Status==ApiCallStatus.Unauthorized)return Challenge();if(result.Status==ApiCallStatus.Forbidden)return Forbid();
        if(!result.Succeeded){Response.StatusCode=503;return View("ServiceUnavailable");}
        ViewData["TenantId"]=tenantId;ViewData["Scope"]=scope;
        var currentFilters = BuildFilters(scope, contractId, ownerId, category, status, from, to, search);
        var modified = currentView is not null && !FiltersEqual(currentView.Filters, currentFilters);
        return View(new ObligationWorkspaceViewModel(result.Value!, views, currentView, modified));
    }

    public static RouteValueDictionary BuildSavedViewRoute(Guid tenantId, SavedViewItem view)
    {
        var route = new RouteValueDictionary();
        if (!SavedViewFilterPolicy.TryNormalize(view.ListingType, view.Filters, view.Sort, out var filters, out _, out _))
            return new RouteValueDictionary { ["tenantId"] = tenantId };
        foreach (var filter in filters)
        {
            if (SavedViewFilterPolicy.IsAllowedRouteKey(view.ListingType, filter.Key)) route[filter.Key] = filter.Value;
        }
        route["tenantId"] = tenantId;
        route["viewId"] = view.Id;
        route["page"] = 1;
        return route;
    }

    private static Dictionary<string, string> BuildFilters(string scope, Guid? contractId, Guid? ownerId, string? category, string? status, DateOnly? from, DateOnly? to, string? search)
    {
        var filters = new Dictionary<string, string>();
        Add(filters, "scope", scope); Add(filters, "contractId", contractId?.ToString()); Add(filters, "ownerId", ownerId?.ToString());
        Add(filters, "category", category); Add(filters, "status", status); Add(filters, "from", from?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        Add(filters, "to", to?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)); Add(filters, "search", search);
        return filters;
    }

    private static bool HasExplicitFilters(Microsoft.AspNetCore.Http.IQueryCollection query) =>
        query.Keys.Any(key => SavedViewFilterPolicy.IsAllowedRouteKey("obligations", key));

    private static bool FiltersEqual(IReadOnlyDictionary<string, string> left, Dictionary<string, string> right) =>
        left.Count == right.Count && left.All(pair => right.TryGetValue(pair.Key, out var value) && value == pair.Value);

    private static void Add(Dictionary<string, string> filters, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) filters[key] = value;
    }
}
