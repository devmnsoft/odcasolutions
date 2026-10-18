using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Odca.Contracts.Obligations;
using Odca.Web.Services;
using Odca.Contracts.SavedViews;
using Odca.Web.Models;

namespace Odca.Web.Controllers;

[Authorize]
[Route("organizacoes/{tenantId:guid}/obrigacoes")]
public sealed class ObligationsController(OdcaApiClient api) : Controller
{
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
            ? "Vista pessoal salva. Os resultados serão sempre recalculados com suas permissões atuais."
            : "Não foi possível salvar a vista. Confira o nome e os filtros.";
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
            TempData["SavedViewError"] = "Esta vista não está disponível ou contém referências que precisam ser atualizadas.";
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
        TempData[result.Succeeded ? "SavedViewNotice" : "SavedViewError"] = result.Succeeded ? "Vista atualizada." : "A vista mudou em outra sessão. Atualize a página e tente novamente.";
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
        TempData[result.Succeeded ? "SavedViewNotice" : "SavedViewError"] = result.Succeeded ? "Vista inativada." : "Não foi possível inativar a vista.";
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
            _ => StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "Não foi possível consultar o histórico agora." })
        };
    }

    // D-OC38 (Sprint 4/A-1): cadastro e ações de obrigações na central do cliente (pt-first;
    // localização das telas é dívida registrada). A API permanece autoridade de validação,
    // permissão, versão e auditoria; o BFF preserva valores após rejeição e conflito 409.

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
            model.ErrorMessage = "Preencha contrato, título, parte obrigada, responsável e prazo para registrar.";
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
                ? $"Série mensal criada com {count} prazos. Lembretes e auditoria registrados."
                : "Obrigação registrada com auditoria e lembretes.";
            return RedirectToAction(nameof(Index), new { tenantId });
        }
        model.ErrorMessage = result.UserMessage("Não foi possível registrar a obrigação. Confira os campos e tente novamente.");
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
        if (!r.Succeeded) return StatusCode(503, new { title = "Não foi possível carregar os documentos do contrato." });
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
            ("cancel", true, _, _) or ("reopen", true, _, _) => "Informe o motivo.",
            ("reschedule", true, false, _) => "Informe um novo prazo diferente do atual e o motivo.",
            ("reassign", true, _, false) => "Selecione o novo responsável e informe o motivo.",
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
                TempData["ObligationError"] = "Esta obrigação não está mais disponível.";
                return RedirectToAction(nameof(Index), new { tenantId });
            case ApiCallStatus.Conflict when currentVersion.HasValue:
                return await RebuildActionsViewAsync(token, tenantId, id, new ObligationActionModel
                {
                    ConflictMessage = "A obrigação mudou em outra sessão. A versão esperada foi atualizada e os valores digitados foram preservados — revise e repita a ação.",
                    OperationError = fieldError,
                    Reason = reason, Note = note, DueDate = dueDate, OwnerId = ownerId, EvidenceDocumentVersionId = evidenceDocumentVersionId
                }, ct);
            case ApiCallStatus.InvalidRequest:
                return await RebuildActionsViewAsync(token, tenantId, id, new ObligationActionModel
                {
                    OperationError = fieldError ?? errorTitle ?? "Os dados da ação estão inválidos. Confira os campos obrigatórios da operação.",
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

    private static List<ObligationDocumentOption> ParseDocumentOptions(System.Text.Json.JsonElement[] versions)
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
            list.Add(new ObligationDocumentOption(versionId, $"{(string.IsNullOrWhiteSpace(title) ? "Documento" : title)} · v{number}{(safe ? "" : " (em análise)")}", safe));
        }
        return list;
    }

    private static string OperationNotice(string operation) => operation switch
    {
        "start" => "Andamento iniciado com auditoria.",
        "fulfill" => "Cumprimento registrado. O prazo saiu do acompanhamento pendente.",
        "cancel" => "Obrigação cancelada com motivo auditado.",
        "reopen" => "Obrigação reaberta com motivo auditado.",
        "reschedule" => "Prazo alterado; lembretes de 7, 1 e 0 dias reagendados.",
        _ => "Responsável alterado com auditoria."
    };

    [HttpGet]
    public async Task<IActionResult> Index(Guid tenantId,string scope="mine",Guid? contractId=null,Guid? ownerId=null,string? category=null,string? status=null,DateOnly? from=null,DateOnly? to=null,string? search=null,Guid? viewId=null,int page=1,CancellationToken ct=default)
    {
        if (from.HasValue && to.HasValue && from.Value > to.Value)
        {
            ModelState.AddModelError(nameof(to), "A data final deve ser igual ou posterior à data inicial.");
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
