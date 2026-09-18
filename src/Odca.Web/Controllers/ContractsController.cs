using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Odca.Contracts.Obligations;
using Odca.Contracts.Renewals;
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

        return View(new ContractSheetViewModel
        {
            Sheet = result.Value,
            ActiveMembers = activeMembers,
            SelectedObligationId = obrigacao,
            FromSource = from,
            Error = result.Succeeded ? null : result.UserMessage("Não foi possível abrir a ficha do contrato.")
        });
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
        CancellationToken ct)
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

        return Redirect($"/organizacoes/{tenantId}/contratos/{contractId}#obrigacoes");
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
        CancellationToken ct)
    {
        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null) return Challenge();

        if (operation == "fulfill" && effectiveAt is null)
        {
            effectiveAt = DateTimeOffset.UtcNow;
        }

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

        return Redirect($"/organizacoes/{tenantId}/contratos/{contractId}#obrigacoes");
    }

    [HttpPost("biblioteca-oficial")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> InstallOfficialLibrary(Guid tenantId, Guid contractId, CancellationToken ct)
    {
        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null) return Challenge();
        var result = await api.InstallOfficialStudioTemplatesAsync(token, tenantId, ct);
        if (result.Status == ApiCallStatus.Unauthorized) return Challenge();
        if (result.Status == ApiCallStatus.Forbidden) return Forbid();
        TempData[result.Succeeded ? "ContractSheetNotice" : "ContractSheetError"] = result.Succeeded
            ? (result.Value!.Installed == 0 ? $"Os {result.Value.AlreadyPresent} modelos oficiais já estavam disponíveis." : $"{result.Value.Installed} modelo(s) oficial(is) publicado(s).")
            : result.UserMessage("Não foi possível instalar a biblioteca oficial.");
        return Redirect($"/organizacoes/{tenantId}/contratos/{contractId}#minutas");
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
        CancellationToken ct)
    {
        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null) return Challenge();

        var sheetResult = await api.GetContractSheetAsync(token, tenantId, contractId, ct);
        if (sheetResult.Status == ApiCallStatus.Unauthorized) return Challenge();
        if (sheetResult.Status == ApiCallStatus.Forbidden) return Forbid();
        if (!sheetResult.Succeeded || sheetResult.Value is null)
        {
            TempData["ContractSheetError"] = "Ficha indisponível.";
            return RedirectToAction(nameof(Sheet), new { tenantId, contractId });
        }

        if (sheetResult.Value.Version != sheetVersion)
        {
            TempData["ContractSheetError"] = "A ficha do contrato foi alterada em outra sessão (versão divergente). Recarregue a página.";
            return RedirectToAction(nameof(Sheet), new { tenantId, contractId });
        }

        if (!OfficialTemplateNames.TryGetValue(officialKey, out var officialTemplateName))
        {
            TempData["ContractSheetError"] = "Chave oficial de minuta desconhecida.";
            return RedirectToAction(nameof(Sheet), new { tenantId, contractId });
        }

        var catalog = await api.GetStudioTemplatesAsync(token, tenantId, officialTemplateName, 1, ct);
        var templateItem = catalog.Value?.Items.FirstOrDefault(x => string.Equals(x.Name, officialTemplateName, StringComparison.OrdinalIgnoreCase) && x.Status == "published");

        if (templateItem is null && sheetResult.Value.CanInstallOfficialLibrary)
        {
            await api.InstallOfficialStudioTemplatesAsync(token, tenantId, ct);
            catalog = await api.GetStudioTemplatesAsync(token, tenantId, officialTemplateName, 1, ct);
            templateItem = catalog.Value?.Items.FirstOrDefault(x => string.Equals(x.Name, officialTemplateName, StringComparison.OrdinalIgnoreCase) && x.Status == "published");
        }

        if (templateItem is null)
        {
            TempData["ContractSheetError"] = "O modelo oficial solicitado não foi encontrado no catálogo do tenant.";
            return RedirectToAction(nameof(Sheet), new { tenantId, contractId });
        }

        var contractTitle = sheetResult.Value.Title;
        var draftTitle = $"{contractTitle} — {officialTemplateName}";
        if (draftTitle.Length > 160) draftTitle = draftTitle[..160];

        var draftResult = await api.CreateStudioDraftAsync(token, tenantId, new Odca.Contracts.Studio.CreateDraftRequest(templateItem.Id, draftTitle, contractId.ToString()), ct);
        if (!draftResult.Succeeded)
        {
            TempData["ContractSheetError"] = draftResult.UserMessage("Não foi possível criar o rascunho da minuta oficial.");
            return RedirectToAction(nameof(Sheet), new { tenantId, contractId });
        }

        var draftId = draftResult.Value.GetProperty("id").GetGuid();
        var returnUrl = Url.Action(nameof(Sheet), "Contracts", new { tenantId, contractId });
        return Redirect($"/organizacoes/{tenantId}/estudio/minutas/{draftId}?returnUrl={Uri.EscapeDataString(returnUrl ?? string.Empty)}");
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
        CancellationToken ct)
    {
        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null) return Challenge();

        var sheetResult = await api.GetContractSheetAsync(token, tenantId, contractId, ct);
        if (sheetResult.Status == ApiCallStatus.Unauthorized) return Challenge();
        if (sheetResult.Status == ApiCallStatus.Forbidden) return Forbid();

        if (responsibleId is null || responsibleId.Value == Guid.Empty)
        {
            TempData["ContractSheetError"] = "Selecione um responsável pela proposta de renovação/aditivo.";
            return Redirect($"/organizacoes/{tenantId}/contratos/{contractId}#renovacao");
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

        return Redirect($"/organizacoes/{tenantId}/contratos/{contractId}#renovacao");
    }
}
