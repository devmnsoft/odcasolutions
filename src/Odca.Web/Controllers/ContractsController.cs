using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Odca.Contracts.Obligations;
using Odca.Contracts.Renewals;
using Odca.Contracts.Studio;
using Odca.Web.Models;
using Odca.Web.Services;

namespace Odca.Web.Controllers;

[Authorize]
[Route("organizacoes/{tenantId:guid}/contratos/{contractId:guid}")]
public sealed class ContractsController(OdcaApiClient api) : Controller
{
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
        CancellationToken ct = default)
    {
        ViewData["Title"] = "Ficha do contrato";
        ViewData["TenantId"] = tenantId;
        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null) return Challenge();

        var result = await api.GetContractSheetAsync(token, tenantId, contractId, ct);
        if (result.Status == ApiCallStatus.Unauthorized) return Challenge();
        if (result.Status == ApiCallStatus.Forbidden) return Forbid();
        if (result.Status == ApiCallStatus.NotFound)
        {
            TempData["ContractSheetError"] = "Este contrato não está disponível no contexto atual.";
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

        return View(new ContractSheetViewModel
        {
            Sheet = result.Value,
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
            Error = result.Succeeded ? null : result.UserMessage("Não foi possível abrir a ficha do contrato.")
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
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(from)) query.Add($"from={Uri.EscapeDataString(from)}");
        if (year.HasValue) query.Add($"year={year.Value}");
        if (month.HasValue) query.Add($"month={month.Value}");
        if (!string.IsNullOrWhiteSpace(scope)) query.Add($"scope={Uri.EscapeDataString(scope)}");
        if (!string.IsNullOrWhiteSpace(kind)) query.Add($"kind={Uri.EscapeDataString(kind)}");
        if (!string.IsNullOrWhiteSpace(urgency)) query.Add($"urgency={Uri.EscapeDataString(urgency)}");
        if (viewId.HasValue) query.Add($"viewId={viewId.Value}");
        if (obrigacao.HasValue) query.Add($"obrigacao={obrigacao.Value}");

        var queryString = query.Count > 0 ? "?" + string.Join('&', query) : "";
        return $"/organizacoes/{tenantId}/contratos/{contractId}{queryString}{anchor}";
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
            ? "Obrigação criada com sucesso na ficha do contrato."
            : result.UserMessage("Não foi possível criar a obrigação.");

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

        TempData[result.Succeeded ? "ContractSheetNotice" : "ContractSheetError"] = result.Succeeded
            ? $"Ação '{operation}' executada com sucesso na obrigação."
            : result.UserMessage("Não foi possível atualizar a obrigação.");

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
            ? (result.Value!.Installed == 0 ? $"Os {result.Value.AlreadyPresent} modelos oficiais já estavam disponíveis." : $"{result.Value.Installed} modelo(s) oficial(is) publicado(s).")
            : result.UserMessage("Não foi possível instalar a biblioteca oficial.");
        return Redirect(BuildReturnUrl(tenantId, contractId, "#minutas", from, year, month, scope, kind, urgency, viewId, obrigacao));
    }

    private static readonly Dictionary<string, string> OfficialTemplateNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["nda-unilateral"] = "Acordo de Confidencialidade (NDA) — Unilateral",
        ["nda-mutual"] = "Acordo de Confidencialidade (NDA) — Mútuo",
        ["services-agreement"] = "Contrato de Prestação de Serviços Técnicos",
        ["contract-amendment"] = "Termo Aditivo Contratual",
        ["supply-agreement"] = "Contrato de Fornecimento de Bens",
        ["lease-agreement"] = "Contrato de Locação de Imóvel Comercial"
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
            TempData["ContractSheetError"] = "Ficha indisponível.";
            return Redirect(BuildReturnUrl(tenantId, contractId, "#minutas", from, year, month, scope, kind, urgency, viewId, obrigacao));
        }

        var sheet = sheetResult.Value;

        if (sheet.Version != sheetVersion)
        {
            TempData["ContractSheetError"] = "A ficha do contrato foi alterada em outra sessão (versão divergente). Recarregue a página.";
            return Redirect(BuildReturnUrl(tenantId, contractId, "#minutas", from, year, month, scope, kind, urgency, viewId, obrigacao));
        }

        var isAmendment = string.Equals(officialKey, "contract-amendment", StringComparison.OrdinalIgnoreCase);
        var inWindow = sheet.Renewal?.InThreeMonthWindow == true;
        var hasOpenReview = sheet.CurrentReview != null &&
            (sheet.CurrentReview.Status is "in_review" or "changes_requested");

        if (hasOpenReview && !isAmendment)
        {
            return BadRequest("Não é permitido iniciar nova minuta (como NDA, serviços, fornecimento ou locação) enquanto houver revisão aberta no contrato.");
        }

        if (isAmendment && !inWindow)
        {
            return BadRequest("Termo aditivo só é permitido quando a vigência estiver na janela de renovação ou houver proposta de alteração de prazo.");
        }

        if (!OfficialTemplateNames.ContainsKey(officialKey))
        {
            return BadRequest("Chave oficial de minuta desconhecida.");
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
            TempData["ContractSheetError"] = "O modelo oficial solicitado não foi encontrado no catálogo do tenant.";
            return Redirect(BuildReturnUrl(tenantId, contractId, "#minutas", from, year, month, scope, kind, urgency, viewId, obrigacao));
        }

        var contractTitle = sheet.Title;
        var draftTitle = $"{contractTitle} — {templateResult.Value.Name}";
        if (draftTitle.Length > 160) draftTitle = draftTitle[..160];

        var draftResult = await api.CreateStudioDraftAsync(token, tenantId, new Odca.Contracts.Studio.CreateDraftRequest(templateResult.Value.Id, draftTitle, contractId.ToString()), ct);
        if (!draftResult.Succeeded)
        {
            TempData["ContractSheetError"] = draftResult.UserMessage("Não foi possível criar o rascunho da minuta oficial.");
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
            TempData["ContractSheetError"] = "Selecione um responsável pela proposta de renovação/aditivo.";
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
            contractVersion);

        var result = await api.CreateRenewalProposalAsync(token, tenantId, contractId, request, ct);
        if (result.Status == ApiCallStatus.Unauthorized) return Challenge();
        if (result.Status == ApiCallStatus.Forbidden) return Forbid();

        TempData[result.Succeeded ? "ContractSheetNotice" : "ContractSheetError"] = result.Succeeded
            ? "Proposta de alteração/renovação registrada com sucesso na ficha."
            : result.UserMessage("Não foi possível registrar a proposta.");

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
            TempData["ContractSheetError"] = "A versão já foi encaminhada para revisão ou houve conflito. A página foi recarregada.";
            return Redirect(BuildReturnUrl(tenantId, contractId, "#revisao", from, year, month, scope, kind, urgency, viewId, obrigacao));
        }

        TempData[result.Succeeded ? "ContractSheetNotice" : "ContractSheetError"] = result.Succeeded
            ? "Revisão encaminhada com sucesso para o revisor."
            : result.UserMessage("Não foi possível encaminhar a revisão.");

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
            ? "Comentário registrado no rascunho (comentário não equivale à aprovação formal)."
            : result.UserMessage("Não foi possível adicionar o comentário.");

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
            TempData["ContractSheetError"] = "A pendência foi atualizada por outra pessoa. A página foi recarregada.";
            return Redirect(BuildReturnUrl(tenantId, contractId, "#revisao", from, year, month, scope, kind, urgency, viewId, obrigacao));
        }

        TempData[result.Succeeded ? "ContractSheetNotice" : "ContractSheetError"] = result.Succeeded
            ? $"Comentário {(operation == "resolve" ? "resolvido" : "reaberto")} com sucesso."
            : result.UserMessage("Não foi possível atualizar o comentário.");

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
            TempData["ContractSheetError"] = "Documento em análise de segurança. Download indisponível.";
            return Redirect(BuildReturnUrl(tenantId, contractId, "#documentos", from, year, month, scope, kind, urgency, viewId, null));
        }

        var preview = await api.GetDocumentPreviewAsync(token, tenantId, contractId, documentId, versionId, ct);
        if (preview.Content is null)
        {
            if (preview.Status == System.Net.HttpStatusCode.Forbidden) return Forbid();
            TempData["ContractSheetError"] = "Não foi possível baixar o documento no momento.";
            return Redirect(BuildReturnUrl(tenantId, contractId, "#documentos", from, year, month, scope, kind, urgency, viewId, null));
        }

        var contentType = preview.ContentType ?? "application/octet-stream";
        var fileName = doc.Name.Contains('.') ? doc.Name : $"{doc.Name}.bin";
        return File(preview.Content, contentType, fileName);
    }
}
