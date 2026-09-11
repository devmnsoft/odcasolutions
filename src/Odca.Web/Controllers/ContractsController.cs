using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odca.Contracts.Contracts;
using Odca.Contracts.Tenancy;
using Odca.Web.Models;
using Odca.Web.Services;

namespace Odca.Web.Controllers;

[Authorize]
public sealed class ContractsController(OdcaApiClient api) : Controller
{
    [HttpGet("contratos")]
    public Task<IActionResult> IndexEntry(CancellationToken cancellationToken)
        => RedirectToTenantAsync(nameof(Index), cancellationToken);

    [HttpGet("organizacoes/{tenantId:guid}/contratos")]
    public async Task<IActionResult> Index(
        Guid tenantId,
        string? title,
        Guid? counterpartyId,
        Guid? typeId,
        Guid? ownerUserId,
        string? operationalStatus,
        string? temporalStatus,
        DateOnly? endFrom,
        DateOnly? endTo,
        bool mine = false,
        bool approaching = false,
        bool unassigned = false,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var token = await TenantUiHelpers.AccessTokenAsync(HttpContext);
        if (token is null)
        {
            return Challenge();
        }

        var org = await TenantUiHelpers.RequireOrganizationAsync(api, token, tenantId, cancellationToken);
        if (org is null)
        {
            return Forbid();
        }

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        TenantUiHelpers.SetTenantContext(this, org);
        await AttachUnreadAsync(token, tenantId, cancellationToken);

        var overview = await api.GetContractOverviewAsync(token, tenantId, cancellationToken);
        if (overview.Status == ApiCallStatus.Unauthorized)
        {
            return Challenge();
        }

        var list = await api.GetContractsAsync(
            token,
            tenantId,
            title,
            counterpartyId,
            typeId,
            ownerUserId,
            operationalStatus,
            temporalStatus,
            endFrom,
            endTo,
            mine,
            approaching,
            unassigned,
            page,
            pageSize,
            cancellationToken);

        if (list.Status == ApiCallStatus.Unauthorized)
        {
            return Challenge();
        }

        if (list.Status == ApiCallStatus.Forbidden || overview.Status == ApiCallStatus.Forbidden)
        {
            return Forbid();
        }

        var loadError = !list.Succeeded;
        var types = await api.GetContractTypesAsync(token, tenantId, null, cancellationToken);
        var counterparties = await api.GetCounterpartiesAsync(token, tenantId, null, "active", 1, 100, cancellationToken);
        var members = await api.GetMembersAsync(token, tenantId, null, "active", 1, 100, cancellationToken);

        return View(new ContractListPageViewModel
        {
            Organization = org,
            Overview = overview.Succeeded ? overview.Value : null,
            Items = list.Succeeded ? list.Value : null,
            Types = types.Succeeded ? types.Value! : [],
            Counterparties = counterparties.Succeeded ? counterparties.Value!.Items : [],
            Members = members.Succeeded ? members.Value!.Items : [],
            Title = title,
            CounterpartyId = counterpartyId,
            TypeId = typeId,
            OwnerUserId = ownerUserId,
            OperationalStatus = operationalStatus,
            TemporalStatus = temporalStatus,
            EndFrom = endFrom,
            EndTo = endTo,
            Mine = mine,
            Approaching = approaching,
            Unassigned = unassigned,
            Page = page,
            PageSize = pageSize,
            LoadError = loadError,
            CanManage = TenantUiHelpers.HasPermission(org, "tenant.contracts.manage"),
            CanLifecycle = TenantUiHelpers.HasPermission(org, "tenant.contracts.lifecycle")
        });
    }

    [HttpGet("organizacoes/{tenantId:guid}/contratos/novo")]
    public async Task<IActionResult> Create(Guid tenantId, string? returnQuery, CancellationToken cancellationToken)
    {
        var token = await TenantUiHelpers.AccessTokenAsync(HttpContext);
        if (token is null)
        {
            return Challenge();
        }

        var org = await TenantUiHelpers.RequireOrganizationAsync(api, token, tenantId, cancellationToken);
        if (org is null)
        {
            return Forbid();
        }

        if (!TenantUiHelpers.HasPermission(org, "tenant.contracts.manage"))
        {
            return Forbid();
        }

        var page = await BuildFormPageAsync(token, org, new ContractFormViewModel { ReturnQuery = returnQuery }, isEdit: false, cancellationToken);
        if (page is null)
        {
            return View("~/Views/Shared/ServiceUnavailable.cshtml");
        }

        TenantUiHelpers.SetTenantContext(this, org);
        return View("Form", page);
    }

    [HttpPost("organizacoes/{tenantId:guid}/contratos/novo")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        Guid tenantId,
        [Bind(Prefix = "Form")] ContractFormViewModel model,
        CancellationToken cancellationToken)
    {
        var token = await TenantUiHelpers.AccessTokenAsync(HttpContext);
        if (token is null)
        {
            return Challenge();
        }

        var org = await TenantUiHelpers.RequireOrganizationAsync(api, token, tenantId, cancellationToken);
        if (org is null || !TenantUiHelpers.HasPermission(org, "tenant.contracts.manage"))
        {
            return Forbid();
        }

        TenantUiHelpers.SetTenantContext(this, org);
        NormalizeForm(model);
        if (!ModelState.IsValid)
        {
            var invalid = await BuildFormPageAsync(token, org, model, false, cancellationToken);
            return invalid is null
                ? View("~/Views/Shared/ServiceUnavailable.cshtml")
                : View("Form", invalid);
        }

        var result = await api.CreateContractAsync(token, tenantId, ToRequest(model), cancellationToken);
        if (result.Succeeded)
        {
            TempData["Success"] = "Contrato salvo como rascunho. A ativação inicia o monitoramento de prazos — não é assinatura digital.";
            return RedirectToAction(nameof(Details), new { tenantId, id = result.Value!.Id });
        }

        if (result.Status == ApiCallStatus.Unauthorized)
        {
            return Challenge();
        }

        if (result.Status == ApiCallStatus.Forbidden)
        {
            return Forbid();
        }

        ModelState.AddModelError(string.Empty, TenantUiHelpers.UserFacingError(result, "Não foi possível salvar o contrato."));
        var page = await BuildFormPageAsync(token, org, model, false, cancellationToken);
        return page is null
            ? View("~/Views/Shared/ServiceUnavailable.cshtml")
            : View("Form", page);
    }

    [HttpGet("organizacoes/{tenantId:guid}/contratos/{id:guid}")]
    public async Task<IActionResult> Details(
        Guid tenantId,
        Guid id,
        string? tab,
        string? returnQuery,
        CancellationToken cancellationToken)
    {
        var token = await TenantUiHelpers.AccessTokenAsync(HttpContext);
        if (token is null)
        {
            return Challenge();
        }

        var org = await TenantUiHelpers.RequireOrganizationAsync(api, token, tenantId, cancellationToken);
        if (org is null)
        {
            return Forbid();
        }

        var detail = await api.GetContractAsync(token, tenantId, id, cancellationToken);
        if (detail.Status == ApiCallStatus.Unauthorized)
        {
            return Challenge();
        }

        if (detail.Status == ApiCallStatus.Forbidden)
        {
            return Forbid();
        }

        if (!detail.Succeeded)
        {
            TempData["Error"] = "Contrato não encontrado ou indisponível.";
            return RedirectToAction(nameof(Index), MergeReturn(tenantId, returnQuery));
        }

        TenantUiHelpers.SetTenantContext(this, org);
        await AttachUnreadAsync(token, tenantId, cancellationToken);
        var contract = detail.Value!;
        return View(new ContractDetailPageViewModel
        {
            Organization = org,
            Contract = contract,
            Tab = NormalizeDetailTab(tab),
            CanManage = TenantUiHelpers.HasPermission(org, "tenant.contracts.manage"),
            CanLifecycle = TenantUiHelpers.HasPermission(org, "tenant.contracts.lifecycle"),
            ReturnQuery = returnQuery,
            Renew = new RenewContractFormViewModel
            {
                Version = contract.Version,
                NewEndDate = contract.EndDate?.AddYears(1) ?? DateOnly.FromDateTime(DateTime.Today).AddYears(1)
            },
            SoftDelete = new SoftDeleteContractFormViewModel { Version = contract.Version }
        });
    }

    [HttpGet("organizacoes/{tenantId:guid}/contratos/{id:guid}/editar")]
    public async Task<IActionResult> Edit(Guid tenantId, Guid id, string? returnQuery, CancellationToken cancellationToken)
    {
        var token = await TenantUiHelpers.AccessTokenAsync(HttpContext);
        if (token is null)
        {
            return Challenge();
        }

        var org = await TenantUiHelpers.RequireOrganizationAsync(api, token, tenantId, cancellationToken);
        if (org is null || !TenantUiHelpers.HasPermission(org, "tenant.contracts.manage"))
        {
            return Forbid();
        }

        var detail = await api.GetContractAsync(token, tenantId, id, cancellationToken);
        if (detail.Status == ApiCallStatus.Unauthorized)
        {
            return Challenge();
        }

        if (detail.Status == ApiCallStatus.Forbidden)
        {
            return Forbid();
        }

        if (!detail.Succeeded)
        {
            TempData["Error"] = "Contrato não encontrado ou indisponível.";
            return RedirectToAction(nameof(Index), MergeReturn(tenantId, returnQuery));
        }

        var contract = detail.Value!;
        if (contract.OperationalStatus is not ("draft" or "active"))
        {
            TempData["Error"] = "Somente rascunhos e contratos ativos podem ser editados.";
            return RedirectToAction(nameof(Details), new { tenantId, id, returnQuery });
        }

        var form = FromDetail(contract);
        form.ReturnQuery = returnQuery;
        var page = await BuildFormPageAsync(token, org, form, true, cancellationToken);
        if (page is null)
        {
            return View("~/Views/Shared/ServiceUnavailable.cshtml");
        }

        TenantUiHelpers.SetTenantContext(this, org);
        return View("Form", page);
    }

    [HttpPost("organizacoes/{tenantId:guid}/contratos/{id:guid}/editar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        Guid tenantId,
        Guid id,
        [Bind(Prefix = "Form")] ContractFormViewModel model,
        CancellationToken cancellationToken)
    {
        if (model.Id is Guid formId && formId != id)
        {
            return BadRequest();
        }

        var token = await TenantUiHelpers.AccessTokenAsync(HttpContext);
        if (token is null)
        {
            return Challenge();
        }

        var org = await TenantUiHelpers.RequireOrganizationAsync(api, token, tenantId, cancellationToken);
        if (org is null || !TenantUiHelpers.HasPermission(org, "tenant.contracts.manage"))
        {
            return Forbid();
        }

        TenantUiHelpers.SetTenantContext(this, org);
        model.Id = id;
        NormalizeForm(model);
        if (!ModelState.IsValid)
        {
            var invalid = await BuildFormPageAsync(token, org, model, true, cancellationToken);
            return invalid is null
                ? View("~/Views/Shared/ServiceUnavailable.cshtml")
                : View("Form", invalid);
        }

        var result = await api.UpdateContractAsync(token, tenantId, id, ToRequest(model), cancellationToken);
        if (result.Succeeded)
        {
            TempData["Success"] = "Contrato atualizado.";
            return RedirectToAction(nameof(Details), new { tenantId, id, returnQuery = model.ReturnQuery });
        }

        if (result.Status == ApiCallStatus.Unauthorized)
        {
            return Challenge();
        }

        if (result.Status == ApiCallStatus.Forbidden)
        {
            return Forbid();
        }

        if (result.Status == ApiCallStatus.Conflict)
        {
            model.VersionConflict = true;
            ModelState.AddModelError(
                string.Empty,
                result.UserMessage("O contrato foi alterado em outra sessão. Recarregue os dados antes de salvar."));
            var conflictPage = await BuildFormPageAsync(token, org, model, true, cancellationToken);
            return conflictPage is null
                ? View("~/Views/Shared/ServiceUnavailable.cshtml")
                : View("Form", conflictPage);
        }

        ModelState.AddModelError(string.Empty, TenantUiHelpers.UserFacingError(result, "Não foi possível salvar o contrato."));
        var page = await BuildFormPageAsync(token, org, model, true, cancellationToken);
        return page is null
            ? View("~/Views/Shared/ServiceUnavailable.cshtml")
            : View("Form", page);
    }

    [HttpPost("organizacoes/{tenantId:guid}/contratos/{id:guid}/ativar")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Activate(
        Guid tenantId,
        Guid id,
        ContractVersionActionViewModel model,
        string? returnQuery,
        CancellationToken cancellationToken)
        => LifecycleAsync(
            tenantId,
            id,
            model.Version,
            returnQuery,
            (token, v, ct) => api.ActivateContractAsync(token, tenantId, id, new ContractVersionRequest(v), ct),
            "Contrato ativado. O monitoramento de prazos e alertas internos passa a valer — esta ação não é assinatura digital.",
            "Não foi possível ativar o contrato.",
            cancellationToken);

    [HttpPost("organizacoes/{tenantId:guid}/contratos/{id:guid}/encerrar")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Close(
        Guid tenantId,
        Guid id,
        ContractVersionActionViewModel model,
        string? returnQuery,
        CancellationToken cancellationToken)
        => LifecycleAsync(
            tenantId,
            id,
            model.Version,
            returnQuery,
            (token, v, ct) => api.CloseContractAsync(token, tenantId, id, new ContractVersionRequest(v), ct),
            "Contrato encerrado. Alertas futuros deixam de ser gerados.",
            "Não foi possível encerrar o contrato.",
            cancellationToken);

    [HttpPost("organizacoes/{tenantId:guid}/contratos/{id:guid}/cancelar")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Cancel(
        Guid tenantId,
        Guid id,
        ContractVersionActionViewModel model,
        string? returnQuery,
        CancellationToken cancellationToken)
        => LifecycleAsync(
            tenantId,
            id,
            model.Version,
            returnQuery,
            (token, v, ct) => api.CancelContractAsync(token, tenantId, id, new ContractVersionRequest(v), ct),
            "Contrato cancelado. O histórico permanece disponível para consulta.",
            "Não foi possível cancelar o contrato.",
            cancellationToken);

    [HttpPost("organizacoes/{tenantId:guid}/contratos/{id:guid}/renovar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Renew(
        Guid tenantId,
        Guid id,
        [Bind(Prefix = "Renew")] RenewContractFormViewModel model,
        string? returnQuery,
        string? tab,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Revise a nova data de término e o motivo da renovação.";
            return RedirectToAction(nameof(Details), new { tenantId, id, tab = "renovacoes", returnQuery });
        }

        var token = await TenantUiHelpers.AccessTokenAsync(HttpContext);
        if (token is null)
        {
            return Challenge();
        }

        var result = await api.RenewContractAsync(
            token,
            tenantId,
            id,
            new RenewContractRequest(model.NewEndDate, model.Reason, model.Version),
            cancellationToken);

        TempData[result.Status == ApiCallStatus.Success ? "Success" : "Error"] =
            result.Status == ApiCallStatus.Success
                ? "Renovação registrada. O ciclo anterior permanece no histórico."
                : TenantUiHelpers.UserFacingError(result, "Não foi possível renovar o contrato.");
        return RedirectToAction(nameof(Details), new { tenantId, id, tab = tab ?? "renovacoes", returnQuery });
    }

    [HttpPost("organizacoes/{tenantId:guid}/contratos/{id:guid}/inativar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SoftDelete(
        Guid tenantId,
        Guid id,
        [Bind(Prefix = "SoftDelete")] SoftDeleteContractFormViewModel model,
        string? returnQuery,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Informe um motivo com ao menos 3 caracteres para inativar o contrato.";
            return RedirectToAction(nameof(Details), new { tenantId, id, returnQuery });
        }

        var token = await TenantUiHelpers.AccessTokenAsync(HttpContext);
        if (token is null)
        {
            return Challenge();
        }

        var result = await api.SoftDeleteContractAsync(
            token,
            tenantId,
            id,
            new SoftDeleteContractRequest(model.Reason, model.Version),
            cancellationToken);

        if (result.Status == ApiCallStatus.Success)
        {
            TempData["Success"] = "Contrato inativado e removido das listas operacionais. O histórico permanece auditável.";
            return RedirectToAction(nameof(Index), MergeReturn(tenantId, returnQuery));
        }

        TempData["Error"] = TenantUiHelpers.UserFacingError(result, "Não foi possível inativar o contrato.");
        return RedirectToAction(nameof(Details), new { tenantId, id, returnQuery });
    }

    private async Task<IActionResult> LifecycleAsync(
        Guid tenantId,
        Guid id,
        long version,
        string? returnQuery,
        Func<string, long, CancellationToken, Task<ApiCallResult<bool>>> action,
        string successMessage,
        string fallbackError,
        CancellationToken cancellationToken)
    {
        var token = await TenantUiHelpers.AccessTokenAsync(HttpContext);
        if (token is null)
        {
            return Challenge();
        }

        var result = await action(token, version, cancellationToken);
        TempData[result.Status == ApiCallStatus.Success ? "Success" : "Error"] =
            result.Status == ApiCallStatus.Success
                ? successMessage
                : TenantUiHelpers.UserFacingError(result, fallbackError);
        return RedirectToAction(nameof(Details), new { tenantId, id, returnQuery });
    }

    private async Task<IActionResult> RedirectToTenantAsync(string actionName, CancellationToken cancellationToken)
    {
        var token = await TenantUiHelpers.AccessTokenAsync(HttpContext);
        if (token is null)
        {
            return Challenge();
        }

        var tenantId = await TenantUiHelpers.ResolveDefaultTenantIdAsync(api, token, cancellationToken);
        if (tenantId is null)
        {
            return RedirectToAction("Index", "Organizations");
        }

        return RedirectToAction(actionName, new { tenantId });
    }

    private async Task AttachUnreadAsync(string token, Guid tenantId, CancellationToken cancellationToken)
    {
        var unread = await api.GetUnreadNotificationCountAsync(token, tenantId, cancellationToken);
        if (unread.Succeeded)
        {
            ViewData["UnreadNotifications"] = unread.Value!.Count;
        }
    }

    private async Task<ContractFormPageViewModel?> BuildFormPageAsync(
        string token,
        OrganizationSummary org,
        ContractFormViewModel form,
        bool isEdit,
        CancellationToken cancellationToken)
    {
        var types = await api.GetContractTypesAsync(token, org.Id, null, cancellationToken);
        var counterparties = await api.GetCounterpartiesAsync(token, org.Id, null, "active", 1, 100, cancellationToken);
        var members = await api.GetMembersAsync(token, org.Id, null, "active", 1, 100, cancellationToken);
        if (!types.Succeeded || !counterparties.Succeeded || !members.Succeeded)
        {
            if (types.Status is ApiCallStatus.Unauthorized or ApiCallStatus.Forbidden ||
                counterparties.Status is ApiCallStatus.Unauthorized or ApiCallStatus.Forbidden ||
                members.Status is ApiCallStatus.Unauthorized or ApiCallStatus.Forbidden)
            {
                return null;
            }
        }

        var typeItems = types.Succeeded ? types.Value! : [];
        if (isEdit && form.TypeId != Guid.Empty && typeItems.All(x => x.Id != form.TypeId))
        {
            var current = await api.GetContractTypeAsync(token, org.Id, form.TypeId, cancellationToken);
            if (current.Succeeded)
            {
                typeItems = typeItems.Concat([current.Value!]).ToArray();
            }
        }

        return new ContractFormPageViewModel
        {
            Organization = org,
            Form = form,
            Types = typeItems,
            Counterparties = counterparties.Succeeded ? counterparties.Value!.Items : [],
            Members = members.Succeeded ? members.Value!.Items : [],
            IsEdit = isEdit,
            CanManage = true,
            ReturnQuery = form.ReturnQuery
        };
    }

    private static void NormalizeForm(ContractFormViewModel model)
    {
        if (model.IsIndefinite)
        {
            model.EndDate = null;
        }

        if (string.IsNullOrWhiteSpace(model.Currency))
        {
            model.Currency = null;
        }

        if (string.IsNullOrWhiteSpace(model.AmountPeriodicity))
        {
            model.AmountPeriodicity = null;
        }

        if (string.IsNullOrWhiteSpace(model.RenewalDecision))
        {
            model.RenewalDecision = "pending";
        }
    }

    private static UpsertContractRequest ToRequest(ContractFormViewModel model)
        => new(
            model.ReferenceNumber,
            model.Title,
            model.Summary,
            model.TypeId,
            model.PrimaryCounterpartyId,
            model.OwnerUserId,
            model.StartDate,
            model.EndDate,
            model.IsIndefinite,
            model.Amount,
            model.Currency,
            model.AmountPeriodicity,
            model.RenewalNoticeDays,
            model.RenewalDecision,
            model.AdditionalCounterpartyIds,
            model.Version);

    private static ContractFormViewModel FromDetail(ContractDetailResponse contract)
        => new()
        {
            Id = contract.Id,
            ReferenceNumber = contract.ReferenceNumber,
            Title = contract.Title,
            Summary = contract.Summary,
            TypeId = contract.TypeId,
            PrimaryCounterpartyId = contract.PrimaryCounterpartyId,
            OwnerUserId = contract.OwnerUserId,
            StartDate = contract.StartDate,
            EndDate = contract.EndDate,
            IsIndefinite = contract.IsIndefinite,
            Amount = contract.Amount,
            Currency = contract.Currency,
            AmountPeriodicity = contract.AmountPeriodicity,
            RenewalNoticeDays = contract.RenewalNoticeDays,
            RenewalDecision = contract.RenewalDecision,
            AdditionalCounterpartyIds = contract.Parties
                .Where(x => !string.Equals(x.Role, "primary", StringComparison.OrdinalIgnoreCase))
                .Select(x => x.CounterpartyId)
                .ToArray(),
            Version = contract.Version
        };

    private static string NormalizeDetailTab(string? tab) => tab?.Trim().ToLowerInvariant() switch
    {
        "partes" => "partes",
        "renovacoes" => "renovacoes",
        "historico" => "historico",
        _ => "resumo"
    };

    private static Dictionary<string, object?> MergeReturn(Guid tenantId, string? returnQuery)
    {
        var values = new Dictionary<string, object?> { ["tenantId"] = tenantId };
        if (string.IsNullOrWhiteSpace(returnQuery))
        {
            return values;
        }

        foreach (var pair in returnQuery.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 0 || string.IsNullOrWhiteSpace(parts[0]))
            {
                continue;
            }

            var key = Uri.UnescapeDataString(parts[0]);
            if (string.Equals(key, "tenantId", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            values[key] = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
        }

        return values;
    }
}
