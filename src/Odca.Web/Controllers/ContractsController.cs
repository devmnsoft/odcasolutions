using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Odca.Contracts.Contracts;
using Odca.Contracts.Obligations;
using Odca.Contracts.Renewals;
using Odca.Contracts.Studio;
using Odca.Configuration.Localization;
using Odca.Web.Models;
using Odca.Web.Services;

namespace Odca.Web.Controllers;

[Authorize]
[Route("organizacoes/{tenantId:guid}/contratos/{contractId:guid}")]
public sealed class ContractsController(OdcaApiClient api, IUserTenantContext tenantContext, LocalizationService loc) : Controller
{
    // D-OC37 (F-2): padrão canônico de cultura (CultureMiddleware) — Controller.Culture não é populado aqui.
    private string? Culture => HttpContext.Items[Odca.Web.Middleware.CultureMiddleware.ItemsKey] as string;
    private string L(string key, params string[] args) => loc.T(key, Culture, args);
    [HttpGet]
    [Route("~/organizacoes/{tenantId:guid}/contratos")]
    public async Task<IActionResult> List(
        Guid tenantId,
        [FromQuery] string? q = null,
        [FromQuery] string? status = null,
        [FromQuery] int page = 1,
        CancellationToken ct = default)
    {
        ViewData["Title"] = L("meus-contratos");
        ViewData["TenantId"] = tenantId;

        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null) return Challenge();

        var access = await tenantContext.GetAccessAsync(tenantId, ct);
        if (access is null) return Forbid();

        var normalizedStatus = status?.Trim().ToLowerInvariant() switch
        {
            "archived" or "arquivados" => "archived",
            "closed" or "encerrados" => "closed",
            "all" or "todos" => "all",
            _ => "active"
        };
        page = Math.Max(1, page);

        var result = await api.GetContractsAsync(token, tenantId, string.IsNullOrWhiteSpace(q) ? null : q.Trim(), normalizedStatus, page, ct);
        if (result.Status == ApiCallStatus.Unauthorized) return Challenge();
        if (result.Status == ApiCallStatus.Forbidden) return Forbid();

        return View("Index", new ContractListViewModel
        {
            TenantId = tenantId,
            Contracts = result.Succeeded && result.Value is not null ? result.Value.Items : [],
            Page = page,
            PageSize = result.Value?.PageSize ?? 20,
            Total = result.Value?.Total ?? 0,
            Search = string.IsNullOrWhiteSpace(q) ? null : q.Trim(),
            Status = normalizedStatus,
            CanCreateDocument = access.HasAnyPermission("tenant.contract_drafts.manage", "tenant.documents.manage"),
            CanManageContracts = access.HasPermission("tenant.contracts.manage"),
            CanReadHistory = access.HasAnyPermission("tenant.contracts.history", "tenant.contracts.read"),
            Error = result.Succeeded ? null : result.UserMessage(L("sheet.ct_load_list_fail"))
        });
    }
    [HttpGet("")]
    public async Task<IActionResult> Sheet(
        Guid tenantId,
        Guid contractId,
        [FromQuery] Guid? obrigacao = null,
        [FromQuery] string? from = null,
        [FromQuery] int? year = null,
        [FromQuery] int? month = null,
        [FromQuery] string? scope = null,
        [FromQuery] string? kind = null,
        [FromQuery] string? urgency = null,
        [FromQuery] Guid? viewId = null,
        [FromQuery] int eventsPage = 1,
        CancellationToken ct = default)
    {
        ViewData["Title"] = L("ficha-contrato");
        ViewData["TenantId"] = tenantId;
        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null) return Challenge();

        var result = await api.GetContractSheetAsync(token, tenantId, contractId, ct);
        if (result.Status == ApiCallStatus.Unauthorized) return Challenge();
        if (result.Status == ApiCallStatus.Forbidden) return Forbid();
        if (result.Status == ApiCallStatus.NotFound)
        {
            TempData["ContractSheetError"] = L("sheet.ct_sheet_unavailable");
            return RedirectToAction("Index", "Inbox", new { tenantId });
        }

        var membersResult = await api.GetMembersAsync(token, tenantId, null, "active", 1, 100, ct);
        var activeMembers = membersResult.Succeeded ? membersResult.Value?.Items ?? [] : [];

        IReadOnlyList<StudioCommentItem> comments = [];
        IReadOnlyList<StudioVersionItem> versions = [];
        IReadOnlyList<StudioReviewerItem> reviewers = [];

        if (result.Value?.DraftId is { } draftId)
        {
            var commentsRes = await api.GetStudioCommentsAsync(token, tenantId, draftId, true, ct);
            if (commentsRes.Succeeded && commentsRes.Value is not null) comments = commentsRes.Value;

            var versionsRes = await api.GetStudioVersionsAsync(token, tenantId, draftId, ct);
            if (versionsRes.Succeeded && versionsRes.Value is not null) versions = versionsRes.Value;

            var reviewersRes = await api.GetStudioReviewersAsync(token, tenantId, ct);
            if (reviewersRes.Succeeded && reviewersRes.Value is not null) reviewers = reviewersRes.Value;
        }

        var access = await tenantContext.GetAccessAsync(tenantId, ct);
        var canManageContracts = access?.HasPermission("tenant.contracts.manage") ?? false;
        var canReadHistory = access is not null && access.HasAnyPermission("tenant.contracts.history", "tenant.contracts.read");
        var canSignDocuments = access?.HasPermission("tenant.contract_drafts.manage") ?? false;
        var canReadDocuments = access is not null && access.HasAnyPermission("tenant.patients.documents.read", "tenant.contract_drafts.read");

        // B.3.2c — histórico embutido na ficha (contract_events), 50 eventos por página.
        IReadOnlyList<ContractEventItem> events = [];
        int eventTotal = 0;
        if (canReadHistory)
        {
            var eventsPageNumber = Math.Max(1, eventsPage);
            var eventsResult = await api.GetContractEventsAsync(token, tenantId, contractId, eventsPageNumber, ct);
            if (eventsResult.Succeeded && eventsResult.Value is not null)
            {
                events = eventsResult.Value.Items;
                eventTotal = eventsResult.Value.Total;
            }
        }

        return View(new ContractSheetViewModel
        {
            Sheet = result.Value,
            // D-OC31 B.3 — resumo contínuo da jornada calculado no BFF a partir dos dados já projetados na ficha.
            Journey = result.Value is { } sheetData ? ContractJourney.Build(sheetData) : null,
            ActiveMembers = activeMembers,
            SelectedObligationId = obrigacao,
            FromSource = from,
            Year = year,
            Month = month,
            Scope = scope,
            Kind = kind,
            Urgency = urgency,
            ViewId = viewId,
            Comments = comments,
            Versions = versions,
            Reviewers = reviewers,
            Events = events,
            EventTotal = eventTotal,
            EventPage = Math.Max(1, eventsPage),
            CanManageContracts = canManageContracts,
            CanReadHistory = canReadHistory,
            CanSignDocuments = canSignDocuments,
            CanReadDocuments = canReadDocuments,
            Error = result.Succeeded ? null : result.UserMessage(L("sheet.ct_sheet_load_fail"))
        });
    }

    public static string BuildReturnUrl(
        Guid tenantId,
        Guid contractId,
        string anchor,
        string? from,
        int? year,
        int? month,
        string? scope,
        string? kind,
        string? urgency,
        Guid? viewId,
        Guid? obrigacao)
    {
        return $"/organizacoes/{tenantId}/contratos/{contractId}{BuildReturnUrlQuery(from, year, month, scope, kind, urgency, viewId, obrigacao)}{anchor}";
    }

    /// <summary>Query string (com "?") que carrega o contexto de navegação entre ações; reutilizada em redirects e nos links da ficha.</summary>
    public static string BuildReturnUrlQuery(
        string? from,
        int? year,
        int? month,
        string? scope,
        string? kind,
        string? urgency,
        Guid? viewId,
        Guid? obrigacao)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(from)) query.Add($"from={Uri.EscapeDataString(from)}");
        if (year.HasValue) query.Add($"year={year.Value}");
        if (month.HasValue) query.Add($"month={month.Value}");
        if (!string.IsNullOrWhiteSpace(scope)) query.Add($"scope={Uri.EscapeDataString(scope)}");
        if (!string.IsNullOrWhiteSpace(kind)) query.Add($"kind={Uri.EscapeDataString(kind)}");
        if (!string.IsNullOrWhiteSpace(urgency)) query.Add($"urgency={Uri.EscapeDataString(urgency)}");
        if (viewId.HasValue) query.Add($"viewId={viewId.Value}");
        if (obrigacao.HasValue) query.Add($"obrigacao={obrigacao.Value}");
        return query.Count > 0 ? "?" + string.Join('&', query) : "";
    }

    [HttpPost("obrigacoes")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateObligation(
        Guid tenantId,
        Guid contractId,
        string title,
        string obligatedParty,
        string category,
        string priority,
        DateOnly dueDate,
        Guid ownerId,
        decimal? amount,
        string? currency,
        string? postTermReason,
        bool evidenceRequired,
        string? description,
        [FromQuery] string? from = null,
        [FromQuery] int? year = null,
        [FromQuery] int? month = null,
        [FromQuery] string? scope = null,
        [FromQuery] string? kind = null,
        [FromQuery] string? urgency = null,
        [FromQuery] Guid? viewId = null,
        [FromQuery] Guid? obrigacao = null,
        CancellationToken ct = default)
    {
        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null) return Challenge();

        var request = new CreateObligationRequest(
            title,
            description,
            category,
            obligatedParty,
            ownerId,
            dueDate,
            priority,
            "manual",
            amount,
            currency,
            null,
            evidenceRequired,
            postTermReason,
            null,
            null);

        var result = await api.CreateObligationAsync(token, tenantId, contractId, request, ct);
        if (result.Status == ApiCallStatus.Unauthorized) return Challenge();
        if (result.Status == ApiCallStatus.Forbidden) return Forbid();

        TempData[result.Succeeded ? "ContractSheetNotice" : "ContractSheetError"] = result.Succeeded
            ? L("sheet.ct_oblig_created_ok")
            : result.UserMessage(L("sheet.ct_oblig_create_fail"));

        return Redirect(BuildReturnUrl(tenantId, contractId, "#obrigacoes", from, year, month, scope, kind, urgency, viewId, obrigacao));
    }

    [HttpPost("obrigacoes/{id:guid}/{operation}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PerformObligationAction(
        Guid tenantId,
        Guid contractId,
        Guid id,
        string operation,
        long version,
        DateTimeOffset? effectiveAt,
        Guid? evidenceDocumentVersionId,
        string? reason,
        DateOnly? dueDate,
        Guid? ownerId,
        string? note,
        [FromQuery] string? from = null,
        [FromQuery] int? year = null,
        [FromQuery] int? month = null,
        [FromQuery] string? scope = null,
        [FromQuery] string? kind = null,
        [FromQuery] string? urgency = null,
        [FromQuery] Guid? viewId = null,
        [FromQuery] Guid? obrigacao = null,
        CancellationToken ct = default)
    {
        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null) return Challenge();

        var request = new ObligationActionRequest(
            version,
            reason?.Trim(),
            note?.Trim(),
            effectiveAt,
            evidenceDocumentVersionId,
            ownerId,
            dueDate);

        var result = await api.PerformObligationActionAsync(token, tenantId, id, operation, request, ct);
        if (result.Status == ApiCallStatus.Unauthorized) return Challenge();
        if (result.Status == ApiCallStatus.Forbidden) return Forbid();
        if (result.Status == ApiCallStatus.Conflict)
        {
            TempData["ContractSheetError"] = result.ErrorCode == "obligation.version.conflict"
                ? L("sheet.ct_oblig_conflict")
                : result.UserMessage(L("sheet.ct_oblig_update_fail"));
            return Redirect(BuildReturnUrl(tenantId, contractId, "#obrigacoes", from, year, month, scope, kind, urgency, viewId, obrigacao));
        }

        TempData[result.Succeeded ? "ContractSheetNotice" : "ContractSheetError"] = result.Succeeded
            ? L("sheet.ct_action_ok", operation)
            : result.UserMessage(L("sheet.ct_oblig_update_fail"));

        return Redirect(BuildReturnUrl(tenantId, contractId, "#obrigacoes", from, year, month, scope, kind, urgency, viewId, obrigacao));
    }

    [HttpPost("biblioteca-oficial")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> InstallOfficialLibrary(
        Guid tenantId,
        Guid contractId,
        [FromQuery] string? from = null,
        [FromQuery] int? year = null,
        [FromQuery] int? month = null,
        [FromQuery] string? scope = null,
        [FromQuery] string? kind = null,
        [FromQuery] string? urgency = null,
        [FromQuery] Guid? viewId = null,
        [FromQuery] Guid? obrigacao = null,
        CancellationToken ct = default)
    {
        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null) return Challenge();
        var result = await api.InstallOfficialStudioTemplatesAsync(token, tenantId, ct);
        if (result.Status == ApiCallStatus.Unauthorized) return Challenge();
        if (result.Status == ApiCallStatus.Forbidden) return Forbid();
        TempData[result.Succeeded ? "ContractSheetNotice" : "ContractSheetError"] = result.Succeeded
            ? (result.Value!.Installed == 0 ? L("sheet.ct_templates_already_installed", result.Value.AlreadyPresent.ToString(System.Globalization.CultureInfo.InvariantCulture)) : L("sheet.ct_templates_installed", result.Value.Installed.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            : result.UserMessage(L("sheet.ct_library_install_fail"));
        return Redirect(BuildReturnUrl(tenantId, contractId, "#minutas", from, year, month, scope, kind, urgency, viewId, obrigacao));
    }

    // D-OC42 (S8): chaves oficiais de minuta — resolução sempre por KEY; os títulos vêm do catálogo do tenant.
    private static readonly HashSet<string> OfficialTemplateKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "nda-unilateral",
        "nda-mutual",
        "services-agreement",
        "contract-amendment",
        "supply-agreement",
        "lease-agreement"
    };

    [HttpPost("rascunho-oficial")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StartOfficialDraft(
        Guid tenantId,
        Guid contractId,
        string officialKey,
        long sheetVersion,
        [FromQuery] string? from = null,
        [FromQuery] int? year = null,
        [FromQuery] int? month = null,
        [FromQuery] string? scope = null,
        [FromQuery] string? kind = null,
        [FromQuery] string? urgency = null,
        [FromQuery] Guid? viewId = null,
        [FromQuery] Guid? obrigacao = null,
        CancellationToken ct = default)
    {
        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null) return Challenge();

        var sheetResult = await api.GetContractSheetAsync(token, tenantId, contractId, ct);
        if (sheetResult.Status == ApiCallStatus.Unauthorized) return Challenge();
        if (sheetResult.Status == ApiCallStatus.Forbidden) return Forbid();
        if (!sheetResult.Succeeded || sheetResult.Value is null)
        {
            TempData["ContractSheetError"] = L("sheet.ct_unavailable_short");
            return Redirect(BuildReturnUrl(tenantId, contractId, "#minutas", from, year, month, scope, kind, urgency, viewId, obrigacao));
        }

        var sheet = sheetResult.Value;

        if (sheet.Version != sheetVersion)
        {
            TempData["ContractSheetError"] = L("sheet.ct_version_conflict");;
            return Redirect(BuildReturnUrl(tenantId, contractId, "#minutas", from, year, month, scope, kind, urgency, viewId, obrigacao));
        }

        var isAmendment = string.Equals(officialKey, "contract-amendment", StringComparison.OrdinalIgnoreCase);
        var inWindow = sheet.Renewal?.InThreeMonthWindow == true;
        var hasOpenReview = sheet.CurrentReview != null &&
            (sheet.CurrentReview.Status is "in_review" or "changes_requested");

        if (hasOpenReview && !isAmendment)
        {
            return BadRequest(L("sheet.ct_draft_locked_review"));
        }

        if (isAmendment && !inWindow)
        {
            return BadRequest(L("sheet.ct_amendment_outside_window"));
        }

        if (!OfficialTemplateKeys.Contains(officialKey))
        {
            return BadRequest(L("sheet.ct_template_unknown"));
        }

        // Resolução por KEY: Proibido FirstOrDefault(x => x.Name == título)
        var templateResult = await api.GetOfficialStudioTemplateByKeyAsync(token, tenantId, officialKey, ct);
        if (!templateResult.Succeeded && sheet.CanInstallOfficialLibrary)
        {
            await api.InstallOfficialStudioTemplatesAsync(token, tenantId, ct);
            templateResult = await api.GetOfficialStudioTemplateByKeyAsync(token, tenantId, officialKey, ct);
        }

        if (!templateResult.Succeeded || templateResult.Value is null)
        {
            TempData["ContractSheetError"] = L("sheet.ct_template_not_found");
            return Redirect(BuildReturnUrl(tenantId, contractId, "#minutas", from, year, month, scope, kind, urgency, viewId, obrigacao));
        }

        var contractTitle = sheet.Title;
        var draftTitle = $"{contractTitle} — {templateResult.Value.Name}";
        if (draftTitle.Length > 160) draftTitle = draftTitle[..160];

        var draftResult = await api.CreateStudioDraftAsync(token, tenantId, new Odca.Contracts.Studio.CreateDraftRequest(templateResult.Value.Id, draftTitle, null, null, contractId), ct);
        if (!draftResult.Succeeded)
        {
            TempData["ContractSheetError"] = draftResult.UserMessage(L("sheet.ct_draft_create_fail"));
            return Redirect(BuildReturnUrl(tenantId, contractId, "#minutas", from, year, month, scope, kind, urgency, viewId, obrigacao));
        }

        var draftId = draftResult.Value.GetProperty("id").GetGuid();
        var returnUrl = BuildReturnUrl(tenantId, contractId, "#minutas", from, year, month, scope, kind, urgency, viewId, obrigacao);
        return Redirect($"/organizacoes/{tenantId}/estudio/minutas/{draftId}?returnUrl={Uri.EscapeDataString(returnUrl)}");
    }

    [HttpPost("proposta-renovacao")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateRenewalProposal(
        Guid tenantId,
        Guid contractId,
        string kind,
        DateOnly? proposedStartDate,
        DateOnly? proposedEndDate,
        DateOnly effectiveOn,
        string reason,
        long contractVersion,
        Guid? responsibleId,
        decimal? proposedValue,
        string? currency,
        string? priority = null,
        [FromQuery] string? from = null,
        [FromQuery] int? year = null,
        [FromQuery] int? month = null,
        [FromQuery] string? scope = null,
        [FromQuery] string? kindFilter = null,
        [FromQuery] string? urgency = null,
        [FromQuery] Guid? viewId = null,
        [FromQuery] Guid? obrigacao = null,
        CancellationToken ct = default)
    {
        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null) return Challenge();

        var sheetResult = await api.GetContractSheetAsync(token, tenantId, contractId, ct);
        if (sheetResult.Status == ApiCallStatus.Unauthorized) return Challenge();
        if (sheetResult.Status == ApiCallStatus.Forbidden) return Forbid();

        if (responsibleId is null || responsibleId.Value == Guid.Empty)
        {
            TempData["ContractSheetError"] = L("sheet.ct_propose_responsible_required");;
            return Redirect(BuildReturnUrl(tenantId, contractId, "#renovacao", from, year, month, scope, kindFilter, urgency, viewId, obrigacao));
        }

        var inWindow = sheetResult.Value?.Renewal?.InThreeMonthWindow == true;
        var requiresAmendment = inWindow || (sheetResult.Value?.EndsOn is { } cur && proposedEndDate is { } prop && prop != cur);
        if (requiresAmendment)
        {
            kind = "amendment";
        }

        var idempotencyKey = Guid.NewGuid();
        var request = new CreateRenewalRequest(
            idempotencyKey,
            responsibleId.Value,
            kind,
            reason.Trim(),
            effectiveOn,
            proposedStartDate,
            proposedEndDate,
            proposedValue,
            currency?.Trim().ToUpperInvariant(),
            null,
            null,
            contractVersion,
            Priority: priority);

        var result = await api.CreateRenewalProposalAsync(token, tenantId, contractId, request, ct);
        if (result.Status == ApiCallStatus.Unauthorized) return Challenge();
        if (result.Status == ApiCallStatus.Forbidden) return Forbid();
        if (result.Status == ApiCallStatus.Conflict)
        {
            TempData["ContractSheetError"] = result.ErrorCode == "contract.version.conflict"
                ? L("sheet.ct_contract_conflict")
                : result.UserMessage(L("sheet.ct_propose_fail"));
            return Redirect(BuildReturnUrl(tenantId, contractId, "#renovacao", from, year, month, scope, kindFilter, urgency, viewId, obrigacao));
        }

        TempData[result.Succeeded ? "ContractSheetNotice" : "ContractSheetError"] = result.Succeeded
            ? L("sheet.ct_propose_ok")
            : result.UserMessage(L("sheet.ct_propose_fail"));

        return Redirect(BuildReturnUrl(tenantId, contractId, "#renovacao", from, year, month, scope, kindFilter, urgency, viewId, obrigacao));
    }

    [HttpPost("revisao/submeter")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SubmitReview(
        Guid tenantId,
        Guid contractId,
        Guid generatedVersionId,
        Guid reviewerId,
        DateTimeOffset? dueAt,
        string? instructions,
        [FromQuery] string? from = null,
        [FromQuery] int? year = null,
        [FromQuery] int? month = null,
        [FromQuery] string? scope = null,
        [FromQuery] string? kind = null,
        [FromQuery] string? urgency = null,
        [FromQuery] Guid? viewId = null,
        [FromQuery] Guid? obrigacao = null,
        CancellationToken ct = default)
    {
        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null) return Challenge();

        var request = new SubmitReviewRequest(generatedVersionId, reviewerId, dueAt, instructions?.Trim(), Guid.NewGuid());
        var result = await api.SubmitStudioReviewAsync(token, tenantId, request, ct);
        if (result.Status == ApiCallStatus.Unauthorized) return Challenge();
        if (result.Status == ApiCallStatus.Forbidden) return Forbid();

        if (result.Status == ApiCallStatus.Conflict)
        {
            TempData["ContractSheetError"] = L("sheet.ct_review_conflict");;
            return Redirect(BuildReturnUrl(tenantId, contractId, "#revisao", from, year, month, scope, kind, urgency, viewId, obrigacao));
        }

        TempData[result.Succeeded ? "ContractSheetNotice" : "ContractSheetError"] = result.Succeeded
            ? L("sheet.ct_review_submitted_ok")
            : result.UserMessage(L("sheet.ct_review_submit_fail"));

        return Redirect(BuildReturnUrl(tenantId, contractId, "#revisao", from, year, month, scope, kind, urgency, viewId, obrigacao));
    }

    [HttpPost("revisao/comentarios")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddComment(
        Guid tenantId,
        Guid contractId,
        Guid draftId,
        string body,
        string? reference,
        Guid? versionId,
        long? draftRevision,
        Guid? parentId,
        [FromQuery] string? from = null,
        [FromQuery] int? year = null,
        [FromQuery] int? month = null,
        [FromQuery] string? scope = null,
        [FromQuery] string? kind = null,
        [FromQuery] string? urgency = null,
        [FromQuery] Guid? viewId = null,
        [FromQuery] Guid? obrigacao = null,
        CancellationToken ct = default)
    {
        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null) return Challenge();

        var request = new CreateStudioCommentRequest(
            versionId,
            draftRevision ?? 0L,
            string.IsNullOrWhiteSpace(reference) ? "geral" : reference.Trim(),
            body.Trim(),
            parentId);

        var result = await api.AddStudioCommentAsync(token, tenantId, draftId, request, ct);
        if (result.Status == ApiCallStatus.Unauthorized) return Challenge();
        if (result.Status == ApiCallStatus.Forbidden) return Forbid();

        TempData[result.Succeeded ? "ContractSheetNotice" : "ContractSheetError"] = result.Succeeded
            ? L("sheet.ct_comment_added_ok")
            : result.UserMessage(L("sheet.ct_comment_add_fail"));

        return Redirect(BuildReturnUrl(tenantId, contractId, "#revisao", from, year, month, scope, kind, urgency, viewId, obrigacao));
    }

    [HttpPost("revisao/comentarios/{commentId:guid}/{operation}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangeCommentState(
        Guid tenantId,
        Guid contractId,
        Guid draftId,
        Guid commentId,
        string operation,
        bool currentResolved,
        string? observation,
        [FromQuery] string? from = null,
        [FromQuery] int? year = null,
        [FromQuery] int? month = null,
        [FromQuery] string? scope = null,
        [FromQuery] string? kind = null,
        [FromQuery] string? urgency = null,
        [FromQuery] Guid? viewId = null,
        [FromQuery] Guid? obrigacao = null,
        CancellationToken ct = default)
    {
        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null) return Challenge();

        var request = new ChangeStudioCommentStateRequest(
            ExpectedResolved: currentResolved,
            Observation: string.IsNullOrWhiteSpace(observation) && operation == "reopen" ? "Reaberto pela ficha" : observation?.Trim());

        var result = await api.SetStudioCommentStateAsync(token, tenantId, draftId, commentId, operation, request, ct);
        if (result.Status == ApiCallStatus.Unauthorized) return Challenge();
        if (result.Status == ApiCallStatus.Forbidden) return Forbid();

        if (result.Status == ApiCallStatus.Conflict)
        {
            TempData["ContractSheetError"] = L("sheet.ct_comment_conflict");;
            return Redirect(BuildReturnUrl(tenantId, contractId, "#revisao", from, year, month, scope, kind, urgency, viewId, obrigacao));
        }

        TempData[result.Succeeded ? "ContractSheetNotice" : "ContractSheetError"] = result.Succeeded
            ? L("sheet.ct_comment_state_ok", operation == "resolve" ? L("sheet.ct_word_resolved") : L("sheet.ct_word_reopened"))
            : result.UserMessage(L("sheet.ct_comment_update_fail"));

        return Redirect(BuildReturnUrl(tenantId, contractId, "#revisao", from, year, month, scope, kind, urgency, viewId, obrigacao));
    }
    [HttpGet("documentos/{documentId:guid}/versoes/{versionId:guid}/baixar")]
    public async Task<IActionResult> DownloadDocument(
        Guid tenantId,
        Guid contractId,
        Guid documentId,
        Guid versionId,
        [FromQuery] string? from = null,
        [FromQuery] int? year = null,
        [FromQuery] int? month = null,
        [FromQuery] string? scope = null,
        [FromQuery] string? kind = null,
        [FromQuery] string? urgency = null,
        [FromQuery] Guid? viewId = null,
        CancellationToken ct = default)
    {
        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null) return Challenge();

        // Validate permission early from sheet data
        var sheetResult = await api.GetContractSheetAsync(token, tenantId, contractId, ct);
        if (sheetResult.Status == ApiCallStatus.Unauthorized) return Challenge();
        if (sheetResult.Status == ApiCallStatus.Forbidden) return Forbid();

        var doc = sheetResult.Value?.Documents.FirstOrDefault(d => d.DocumentId == documentId && d.VersionId == versionId);
        if (doc is null) return NotFound();

        if (!string.Equals(doc.SafetyState, "safe", StringComparison.OrdinalIgnoreCase))
        {
            TempData["ContractSheetError"] = L("sheet.quarantine_tip");;
            return Redirect(BuildReturnUrl(tenantId, contractId, "#documentos", from, year, month, scope, kind, urgency, viewId, null));
        }

        var preview = await api.GetDocumentPreviewAsync(token, tenantId, contractId, documentId, versionId, ct);
        if (preview.Content is null)
        {
            if (preview.Status == System.Net.HttpStatusCode.Forbidden) return Forbid();
            TempData["ContractSheetError"] = L("sheet.ct_doc_download_fail");;
            return Redirect(BuildReturnUrl(tenantId, contractId, "#documentos", from, year, month, scope, kind, urgency, viewId, null));
        }

        var contentType = preview.ContentType ?? "application/octet-stream";
        var fileName = doc.Name.Contains('.') ? doc.Name : $"{doc.Name}.bin";
        Response.Headers.ContentDisposition = $"inline; filename*=UTF-8''{Uri.EscapeDataString(fileName)}";
        return File(preview.Content, contentType);
    }

    // B.3.2c — ações completas da jornada na ficha do contrato.

    [HttpPost("duplicar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Duplicate(
        Guid tenantId,
        Guid contractId,
        [FromQuery] string? from = null,
        [FromQuery] int? year = null,
        [FromQuery] int? month = null,
        [FromQuery] string? scope = null,
        [FromQuery] string? kind = null,
        [FromQuery] string? urgency = null,
        [FromQuery] Guid? viewId = null,
        [FromQuery] Guid? obrigacao = null,
        CancellationToken ct = default)
    {
        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null) return Challenge();

        var returnUrl = BuildReturnUrl(tenantId, contractId, "#acoes", from, year, month, scope, kind, urgency, viewId, obrigacao);
        var result = await api.DuplicateContractAsync(token, tenantId, contractId, ct);
        if (result.Status == ApiCallStatus.Unauthorized) return Challenge();
        if (result.Status == ApiCallStatus.Forbidden) return Forbid();
        if (result.Status == ApiCallStatus.NotFound)
        {
            TempData["ContractSheetError"] = L("sheet.ct_sheet_unavailable");
            return Redirect(returnUrl);
        }

        if (result.Status == ApiCallStatus.Conflict)
        {
            TempData["ContractSheetError"] = result.ErrorCode switch
            {
                "contract.closed" => L("sheet.ct_duplicate_closed"),
                "contract.no_draft" => L("sheet.ct_duplicate_no_draft"),
                _ => result.UserMessage(L("sheet.ct_duplicate_fail"))
            };
            return Redirect(returnUrl);
        }

        if (!result.Succeeded || result.Value is null)
        {
            TempData["ContractSheetError"] = result.UserMessage(L("sheet.ct_duplicate_fail"));
            return Redirect(returnUrl);
        }

        TempData["ContractSheetNotice"] = L("sheet.ct_duplicated_ok");;
        return Redirect($"/organizacoes/{tenantId}/contratos/{result.Value.ContractId}");
    }

    [HttpPost("detalhes")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateDetails(
        Guid tenantId,
        Guid contractId,
        long version,
        string? title = null,
        string? reference = null,
        Guid? ownerId = null,
        bool clearOwner = false,
        [FromQuery] string? from = null,
        [FromQuery] int? year = null,
        [FromQuery] int? month = null,
        [FromQuery] string? scope = null,
        [FromQuery] string? kind = null,
        [FromQuery] string? urgency = null,
        [FromQuery] Guid? viewId = null,
        [FromQuery] Guid? obrigacao = null,
        CancellationToken ct = default)
    {
        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null) return Challenge();

        var trimmedTitle = title?.Trim() ?? string.Empty;
        var trimmedReference = reference?.Trim() ?? string.Empty;
        var hasOwnerChange = clearOwner || ownerId.HasValue;
        var returnUrl = BuildReturnUrl(tenantId, contractId, "#detalhes", from, year, month, scope, kind, urgency, viewId, obrigacao);

        if (trimmedTitle.Length == 0 && trimmedReference.Length == 0 && !hasOwnerChange)
        {
            TempData["ContractSheetError"] = L("sheet.ct_details_empty");;
            return Redirect(returnUrl);
        }

        // Título vazio não é enviado (o título sempre existe); referência vazia limpa o campo no servidor.
        var request = new ContractUpdateRequest(
            trimmedTitle.Length == 0 ? null : trimmedTitle,
            trimmedReference,
            clearOwner ? null : ownerId,
            clearOwner);

        var result = await api.UpdateContractAsync(token, tenantId, contractId, version, request, ct);
        if (result.Status == ApiCallStatus.Unauthorized) return Challenge();
        if (result.Status == ApiCallStatus.Forbidden) return Forbid();
        if (result.Status == ApiCallStatus.NotFound)
        {
            TempData["ContractSheetError"] = L("sheet.ct_sheet_unavailable");
            return Redirect(returnUrl);
        }

        if (result.Status == ApiCallStatus.Conflict)
        {
            TempData["ContractSheetError"] = result.ErrorCode == "contract.version.conflict"
                ? L("sheet.ct_contract_conflict_changes")
                : result.UserMessage(L("sheet.ct_details_update_fail"));
            return Redirect(returnUrl);
        }

        if (!result.Succeeded)
        {
            TempData["ContractSheetError"] = result.UserMessage(L("sheet.ct_details_update_fail"));
            return Redirect(returnUrl);
        }

        TempData["ContractSheetNotice"] = L("sheet.ct_details_updated_ok");;
        return Redirect(returnUrl);
    }

    [HttpPost("arquivar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Archive(
        Guid tenantId,
        Guid contractId,
        long version,
        string? reason = null,
        [FromQuery] string? from = null,
        [FromQuery] int? year = null,
        [FromQuery] int? month = null,
        [FromQuery] string? scope = null,
        [FromQuery] string? kind = null,
        [FromQuery] string? urgency = null,
        [FromQuery] Guid? viewId = null,
        [FromQuery] Guid? obrigacao = null,
        CancellationToken ct = default)
    {
        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null) return Challenge();

        var trimmedReason = reason?.Trim() ?? string.Empty;
        var returnUrl = BuildReturnUrl(tenantId, contractId, "#acoes", from, year, month, scope, kind, urgency, viewId, obrigacao);

        if (trimmedReason.Length > 500)
        {
            TempData["ContractSheetError"] = L("sheet.ct_archive_reason_long");;
            return Redirect(returnUrl);
        }

        var result = await api.ArchiveContractAsync(token, tenantId, contractId, version, trimmedReason.Length == 0 ? null : trimmedReason, ct);
        if (result.Status == ApiCallStatus.Unauthorized) return Challenge();
        if (result.Status == ApiCallStatus.Forbidden) return Forbid();
        if (result.Status == ApiCallStatus.NotFound)
        {
            TempData["ContractSheetError"] = L("sheet.ct_sheet_unavailable");
            return Redirect(returnUrl);
        }

        if (result.Status == ApiCallStatus.Conflict)
        {
            TempData["ContractSheetError"] = result.ErrorCode == "contract.version.conflict"
                ? L("sheet.ct_contract_conflict_action")
                : result.UserMessage(L("sheet.ct_archive_fail"));
            return Redirect(returnUrl);
        }

        if (!result.Succeeded || result.Value is null)
        {
            TempData["ContractSheetError"] = result.UserMessage(L("sheet.ct_archive_fail"));
            return Redirect(returnUrl);
        }

        TempData["ContractSheetNotice"] = result.Value.Replayed
            ? L("sheet.ct_archive_replayed_ok")
            : L("sheet.ct_archive_ok");
        return Redirect(returnUrl);
    }

    [HttpPost("restaurar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Restore(
        Guid tenantId,
        Guid contractId,
        long version,
        [FromQuery] string? from = null,
        [FromQuery] int? year = null,
        [FromQuery] int? month = null,
        [FromQuery] string? scope = null,
        [FromQuery] string? kind = null,
        [FromQuery] string? urgency = null,
        [FromQuery] Guid? viewId = null,
        [FromQuery] Guid? obligacao = null,
        CancellationToken ct = default)
    {
        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null) return Challenge();

        var returnUrl = BuildReturnUrl(tenantId, contractId, "#acoes", from, year, month, scope, kind, urgency, viewId, obligacao);
        var result = await api.RestoreContractAsync(token, tenantId, contractId, version, ct);
        if (result.Status == ApiCallStatus.Unauthorized) return Challenge();
        if (result.Status == ApiCallStatus.Forbidden) return Forbid();
        if (result.Status == ApiCallStatus.NotFound)
        {
            TempData["ContractSheetError"] = L("sheet.ct_sheet_unavailable");
            return Redirect(returnUrl);
        }

        if (result.Status == ApiCallStatus.Conflict)
        {
            TempData["ContractSheetError"] = result.ErrorCode == "contract.version.conflict"
                ? L("sheet.ct_contract_conflict_action")
                : result.UserMessage(L("sheet.ct_restore_fail"));
            return Redirect(returnUrl);
        }

        if (!result.Succeeded || result.Value is null)
        {
            TempData["ContractSheetError"] = result.UserMessage(L("sheet.ct_restore_fail"));
            return Redirect(returnUrl);
        }

        TempData["ContractSheetNotice"] = result.Value.Replayed
            ? L("sheet.ct_restore_replayed_ok")
            : L("sheet.ct_restore_ok");
        return Redirect(returnUrl);
    }

    [HttpPost("encerrar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Close(
        Guid tenantId,
        Guid contractId,
        long version,
        DateOnly closedOn,
        string? closureReason = null,
        [FromQuery] string? from = null,
        [FromQuery] int? year = null,
        [FromQuery] int? month = null,
        [FromQuery] string? scope = null,
        [FromQuery] string? kind = null,
        [FromQuery] string? urgency = null,
        [FromQuery] Guid? viewId = null,
        [FromQuery] Guid? obrigacao = null,
        CancellationToken ct = default)
    {
        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null) return Challenge();

        var trimmedReason = closureReason?.Trim() ?? string.Empty;
        var returnUrl = BuildReturnUrl(tenantId, contractId, "#acoes", from, year, month, scope, kind, urgency, viewId, obrigacao);

        if (trimmedReason.Length < 5 || trimmedReason.Length > 2000)
        {
            TempData["ContractSheetError"] = L("sheet.ct_close_reason_invalid");;
            return Redirect(returnUrl);
        }

        var result = await api.CloseContractAsync(token, tenantId, contractId, version, new ContractCloseRequest(closedOn, trimmedReason), ct);
        if (result.Status == ApiCallStatus.Unauthorized) return Challenge();
        if (result.Status == ApiCallStatus.Forbidden) return Forbid();
        if (result.Status == ApiCallStatus.NotFound)
        {
            TempData["ContractSheetError"] = L("sheet.ct_sheet_unavailable");
            return Redirect(returnUrl);
        }

        if (result.Status == ApiCallStatus.Conflict)
        {
            TempData["ContractSheetError"] = result.ErrorCode == "contract.version.conflict"
                ? L("sheet.ct_contract_conflict_action")
                : result.UserMessage(L("sheet.ct_close_fail"));
            return Redirect(returnUrl);
        }

        if (!result.Succeeded || result.Value is null)
        {
            TempData["ContractSheetError"] = result.UserMessage(L("sheet.ct_close_fail"));
            return Redirect(returnUrl);
        }

        TempData["ContractSheetNotice"] = result.Value.Replayed
            ? L("sheet.ct_close_replayed_ok")
            : L("sheet.ct_close_ok");
        return Redirect(returnUrl);
    }

    [HttpPost("assinaturas/assinar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SignParticipant(
        Guid tenantId,
        Guid contractId,
        Guid versionId,
        Guid preparationId,
        Guid participantClientId,
        string? signMode = null,
        string? evidence = null,
        string? evidenceDocumentVersion = null,
        string? participantName = null,
        [FromQuery] string? from = null,
        [FromQuery] int? year = null,
        [FromQuery] int? month = null,
        [FromQuery] string? scope = null,
        [FromQuery] string? kind = null,
        [FromQuery] string? urgency = null,
        [FromQuery] Guid? viewId = null,
        [FromQuery] Guid? obrigacao = null,
        CancellationToken ct = default)
    {
        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null) return Challenge();

        var name = string.IsNullOrWhiteSpace(participantName) ? L("sheet.sigmsg_participant_fallback") : participantName.Trim();
        var returnUrl = BuildReturnUrl(tenantId, contractId, "#assinatura", from, year, month, scope, kind, urgency, viewId, obrigacao);

        // D-OC31 B.2: vínculo opcional de documento do acervo como evidência (modo "evidence");
        // o hash é copiado do servidor — apenas o id da versão viaja no formulário.
        var evidenceDoc = Guid.TryParse(evidenceDocumentVersion?.Trim(), out var evDocParsed) ? evDocParsed : Guid.Empty;
        var result = await api.SignSignatureParticipantAsync(token, tenantId, versionId, preparationId, participantClientId, new SignParticipantRequest(signMode, evidence, evidenceDoc == Guid.Empty ? null : evidenceDoc), ct);
        if (result.Status == ApiCallStatus.Unauthorized) return Challenge();
        if (result.Status == ApiCallStatus.Forbidden) return Forbid();
        if (result.Status == ApiCallStatus.NotFound)
        {
            TempData["ContractSheetError"] = L("sheet.sigmsg_sign_notfound");
            return Redirect(returnUrl);
        }

        if (result.Status == ApiCallStatus.Conflict)
        {
            TempData["ContractSheetError"] = result.ErrorCode switch
            {
                "preparation.not_confirmed" => L("sheet.sigmsg_prep_not_confirmed"),
                "participant.already_signed" => L("sheet.sigmsg_already_signed", name),
                "participant.identity_not_linked" => L("sheet.sigmsg_identity_not_linked"),
                "sign.mfa_required" => L("sheet.sigmsg_mfa_required"),
                _ => result.UserMessage(L("sheet.sigmsg_sign_failed"))
            };
            return Redirect(returnUrl);
        }

        if (!result.Succeeded)
        {
            TempData["ContractSheetError"] = result.UserMessage(L("sheet.sigmsg_sign_failed"));
            return Redirect(returnUrl);
        }

        var replayed = result.Value.TryGetProperty("replayed", out var r) && r.ValueKind == JsonValueKind.True;
        var allSigned = result.Value.TryGetProperty("allSigned", out var a) && a.ValueKind == JsonValueKind.True;
        // D-OC31 B.2: o ato registra o hash do PDF confirmado; com vínculo de acervo, a evidência documental é registrada junto.
        var evidenceLinked = result.Value.TryGetProperty("evidenceDocumentVersionId", out var ev) && ev.ValueKind == JsonValueKind.String;
        var evidenceSuffix = evidenceLinked ? L("sheet.sigmsg_evidence_linked_suffix") : string.Empty;
        if (replayed)
            TempData["ContractSheetNotice"] = L("sheet.sigmsg_already_signed", name);
        else if (allSigned)
            TempData["ContractSheetNotice"] = L("sheet.sigmsg_signed_all", name, evidenceSuffix);
        else
            TempData["ContractSheetNotice"] = L("sheet.sigmsg_signed_session", name, evidenceSuffix);
        return Redirect(returnUrl);
    }

    [HttpPost("assinaturas/vincular")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LinkParticipantIdentity(
        Guid tenantId,
        Guid contractId,
        Guid versionId,
        Guid preparationId,
        Guid participantClientId,
        string? participantName = null,
        [FromQuery] string? from = null,
        [FromQuery] int? year = null,
        [FromQuery] int? month = null,
        [FromQuery] string? scope = null,
        [FromQuery] string? kind = null,
        [FromQuery] string? urgency = null,
        [FromQuery] Guid? viewId = null,
        [FromQuery] Guid? obrigacao = null,
        CancellationToken ct = default)
    {
        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null) return Challenge();

        var name = string.IsNullOrWhiteSpace(participantName) ? L("sheet.sigmsg_participant_fallback") : participantName.Trim();
        var returnUrl = BuildReturnUrl(tenantId, contractId, "#assinatura", from, year, month, scope, kind, urgency, viewId, obrigacao);

        var result = await api.LinkSignatureParticipantIdentityAsync(token, tenantId, versionId, preparationId, participantClientId, ct);
        if (result.Status == ApiCallStatus.Unauthorized) return Challenge();
        if (result.Status == ApiCallStatus.Forbidden) return Forbid();
        if (result.Status is ApiCallStatus.NotFound or ApiCallStatus.Conflict)
        {
            TempData["ContractSheetError"] = result.ErrorCode switch
            {
                "preparation.not_confirmed" => L("sheet.sigmsg_prep_not_confirmed"),
                "link.not_member" => L("sheet.sigmsg_link_not_member"),
                _ => result.UserMessage(L("sheet.sigmsg_link_failed"))
            };
            return Redirect(returnUrl);
        }
        if (!result.Succeeded)
        {
            TempData["ContractSheetError"] = result.UserMessage(L("sheet.sigmsg_link_failed"));
            return Redirect(returnUrl);
        }

        TempData["ContractSheetNotice"] = L("sheet.sigmsg_linked_success", name);
        return Redirect(returnUrl);
    }

    [HttpPost("assinaturas/lembrete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemindSignature(
        Guid tenantId,
        Guid contractId,
        Guid versionId,
        Guid preparationId,
        [FromQuery] string? from = null,
        [FromQuery] int? year = null,
        [FromQuery] int? month = null,
        [FromQuery] string? scope = null,
        [FromQuery] string? kind = null,
        [FromQuery] string? urgency = null,
        [FromQuery] Guid? viewId = null,
        [FromQuery] Guid? obrigacao = null,
        CancellationToken ct = default)
    {
        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null) return Challenge();

        var returnUrl = BuildReturnUrl(tenantId, contractId, "#assinatura", from, year, month, scope, kind, urgency, viewId, obrigacao);
        var result = await api.RemindSignaturePreparationAsync(token, tenantId, versionId, preparationId, ct);
        if (result.Status == ApiCallStatus.Unauthorized) return Challenge();
        if (result.Status == ApiCallStatus.Forbidden) return Forbid();
        if (result.Status == ApiCallStatus.NotFound)
        {
            TempData["ContractSheetError"] = L("sheet.sigmsg_remind_notfound");
            return Redirect(returnUrl);
        }

        if (result.Status == ApiCallStatus.Conflict)
        {
            TempData["ContractSheetError"] = result.ErrorCode switch
            {
                "reminder.too_soon" => result.UserMessage(L("sheet.sigmsg_remind_too_soon")),
                "preparation.not_confirmed" => L("sheet.sigmsg_prep_not_confirmed"),
                _ => result.UserMessage(L("sheet.sigmsg_remind_failed"))
            };
            return Redirect(returnUrl);
        }

        if (!result.Succeeded)
        {
            TempData["ContractSheetError"] = result.UserMessage(L("sheet.sigmsg_remind_failed"));
            return Redirect(returnUrl);
        }

        if (result.Value is { ValueKind: System.Text.Json.JsonValueKind.Object } body
            && body.TryGetProperty("delivery", out var delivery)
            && delivery.TryGetProperty("queued", out var queuedEl)
            && queuedEl.TryGetInt32(out var queued))
        {
            // D-OC31 B.1: o texto reflete o estado real — enfileirado na outbox (entrega
            // assíncrona) ou apenas registrado quando nenhum participante tem canal e-mail.
            TempData["ContractSheetNotice"] = queued > 0
                ? L("sheet.sigmsg_remind_queued", queued.ToString(System.Globalization.CultureInfo.InvariantCulture))
                : L("sheet.sigmsg_remind_no_channel");
        }
        else
        {
            TempData["ContractSheetNotice"] = L("sheet.sigmsg_remind_default");
        }
        return Redirect(returnUrl);
    }

    [HttpPost("versoes/{versionId:guid}/pdf-gerar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GenerateVersionPdf(
        Guid tenantId,
        Guid contractId,
        Guid versionId,
        [FromQuery] string? from = null,
        [FromQuery] int? year = null,
        [FromQuery] int? month = null,
        [FromQuery] string? scope = null,
        [FromQuery] string? kind = null,
        [FromQuery] string? urgency = null,
        [FromQuery] Guid? viewId = null,
        [FromQuery] Guid? obrigacao = null,
        CancellationToken ct = default)
    {
        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null) return Challenge();

        var returnUrl = BuildReturnUrl(tenantId, contractId, "#assinatura", from, year, month, scope, kind, urgency, viewId, obrigacao);
        var result = await api.GenerateStudioPdfAsync(token, tenantId, versionId, ct);
        if (result.Status == ApiCallStatus.Unauthorized) return Challenge();
        if (result.Status == ApiCallStatus.Forbidden) return Forbid();
        if (result.Status == ApiCallStatus.NotFound)
        {
            TempData["ContractSheetError"] = L("sheet.ct_pdf_version_gone");;
            return Redirect(returnUrl);
        }

        if (result.Status == ApiCallStatus.Conflict)
        {
            TempData["ContractSheetError"] = result.UserMessage(L("sheet.ct_pdf_busy"));
            return Redirect(returnUrl);
        }

        if (!result.Succeeded)
        {
            TempData["ContractSheetError"] = result.UserMessage(L("sheet.ct_pdf_generate_fail"));
            return Redirect(returnUrl);
        }

        TempData["ContractSheetNotice"] = L("sheet.ct_pdf_generated_ok");;
        return Redirect(returnUrl);
    }

    [HttpGet("versoes/{versionId:guid}/pdf-baixar")]
    public async Task<IActionResult> DownloadVersionPdf(
        Guid tenantId,
        Guid contractId,
        Guid versionId,
        [FromQuery] string? from = null,
        [FromQuery] int? year = null,
        [FromQuery] int? month = null,
        [FromQuery] string? scope = null,
        [FromQuery] string? kind = null,
        [FromQuery] string? urgency = null,
        [FromQuery] Guid? viewId = null,
        [FromQuery] Guid? obrigacao = null,
        CancellationToken ct = default)
    {
        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null) return Challenge();

        using var response = await api.DownloadStudioPdfAsync(token, tenantId, versionId, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized) return Challenge();
        if (response.StatusCode == System.Net.HttpStatusCode.Forbidden) return Forbid();
        if (!response.IsSuccessStatusCode)
        {
            TempData["ContractSheetError"] = response.StatusCode == System.Net.HttpStatusCode.NotFound
                ? L("sheet.ct_pdf_not_generated")
                : L("sheet.ct_pdf_download_fail");
            return Redirect(BuildReturnUrl(tenantId, contractId, "#assinatura", from, year, month, scope, kind, urgency, viewId, obrigacao));
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        return File(bytes, "application/pdf", response.Content.Headers.ContentDisposition?.FileNameStar ?? "contrato.pdf");
    }

    [HttpGet("versoes/{versionId:guid}/imprimir")]
    public async Task<IActionResult> PrintVersion(
        Guid tenantId,
        Guid contractId,
        Guid versionId,
        CancellationToken ct = default)
    {
        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null) return Challenge();

        var result = await api.GetStudioVersionAsync(token, tenantId, versionId, ct);
        if (result.Status == ApiCallStatus.Unauthorized) return Challenge();
        if (result.Status == ApiCallStatus.Forbidden) return Forbid();
        if (result.Status == ApiCallStatus.NotFound) return NotFound();
        if (!result.Succeeded || result.Value is null) return NotFound();

        return View("Print", result.Value);
    }
}
