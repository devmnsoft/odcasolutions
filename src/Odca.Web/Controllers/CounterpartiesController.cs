using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odca.Contracts.Contracts;
using Odca.Web.Models;
using Odca.Web.Services;

namespace Odca.Web.Controllers;

[Authorize]
public sealed class CounterpartiesController(OdcaApiClient api) : Controller
{
    [HttpGet("contrapartes")]
    public Task<IActionResult> IndexEntry(CancellationToken cancellationToken)
        => RedirectToTenantAsync(nameof(Index), cancellationToken);

    [HttpGet("organizacoes/{tenantId:guid}/contrapartes")]
    public async Task<IActionResult> Index(
        Guid tenantId,
        string? search,
        string? status,
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

        var list = await api.GetCounterpartiesAsync(token, tenantId, search, status, page, pageSize, cancellationToken);
        if (list.Status == ApiCallStatus.Unauthorized)
        {
            return Challenge();
        }

        if (list.Status == ApiCallStatus.Forbidden)
        {
            return Forbid();
        }

        var unread = await api.GetUnreadNotificationCountAsync(token, tenantId, cancellationToken);
        if (unread.Succeeded)
        {
            ViewData["UnreadNotifications"] = unread.Value!.Count;
        }

        return View(new CounterpartyListPageViewModel
        {
            Organization = org,
            Items = list.Succeeded ? list.Value : null,
            Search = search,
            Status = status,
            Page = page,
            PageSize = pageSize,
            LoadError = !list.Succeeded,
            CanManage = TenantUiHelpers.HasPermission(org, "tenant.counterparties.manage")
        });
    }

    [HttpGet("organizacoes/{tenantId:guid}/contrapartes/nova")]
    public async Task<IActionResult> Create(Guid tenantId, CancellationToken cancellationToken)
    {
        var token = await TenantUiHelpers.AccessTokenAsync(HttpContext);
        if (token is null)
        {
            return Challenge();
        }

        var org = await TenantUiHelpers.RequireOrganizationAsync(api, token, tenantId, cancellationToken);
        if (org is null || !TenantUiHelpers.HasPermission(org, "tenant.counterparties.manage"))
        {
            return Forbid();
        }

        TenantUiHelpers.SetTenantContext(this, org);
        return View("Form", new CounterpartyFormPageViewModel
        {
            Organization = org,
            Form = new CounterpartyFormViewModel(),
            IsEdit = false,
            CanManage = true
        });
    }

    [HttpPost("organizacoes/{tenantId:guid}/contrapartes/nova")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        Guid tenantId,
        [Bind(Prefix = "Form")] CounterpartyFormViewModel model,
        CancellationToken cancellationToken)
    {
        var token = await TenantUiHelpers.AccessTokenAsync(HttpContext);
        if (token is null)
        {
            return Challenge();
        }

        var org = await TenantUiHelpers.RequireOrganizationAsync(api, token, tenantId, cancellationToken);
        if (org is null || !TenantUiHelpers.HasPermission(org, "tenant.counterparties.manage"))
        {
            return Forbid();
        }

        TenantUiHelpers.SetTenantContext(this, org);
        if (!ModelState.IsValid || !HasClassification(model))
        {
            if (!HasClassification(model))
            {
                ModelState.AddModelError(string.Empty, "Selecione ao menos uma classificação (cliente, fornecedor, parceiro ou prestador).");
            }

            return View("Form", new CounterpartyFormPageViewModel
            {
                Organization = org,
                Form = model,
                IsEdit = false,
                CanManage = true
            });
        }

        var result = await api.CreateCounterpartyAsync(token, tenantId, ToRequest(model), cancellationToken);
        if (result.Succeeded)
        {
            TempData["Success"] = "Contraparte cadastrada.";
            return RedirectToAction(nameof(Index), new { tenantId });
        }

        if (result.Status == ApiCallStatus.Unauthorized)
        {
            return Challenge();
        }

        if (result.Status == ApiCallStatus.Forbidden)
        {
            return Forbid();
        }

        ModelState.AddModelError(string.Empty, TenantUiHelpers.UserFacingError(result, "Não foi possível salvar a contraparte."));
        return View("Form", new CounterpartyFormPageViewModel
        {
            Organization = org,
            Form = model,
            IsEdit = false,
            CanManage = true
        });
    }

    [HttpGet("organizacoes/{tenantId:guid}/contrapartes/{id:guid}")]
    public async Task<IActionResult> Edit(Guid tenantId, Guid id, CancellationToken cancellationToken)
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

        var detail = await api.GetCounterpartyAsync(token, tenantId, id, cancellationToken);
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
            TempData["Error"] = "Contraparte não encontrada.";
            return RedirectToAction(nameof(Index), new { tenantId });
        }

        TenantUiHelpers.SetTenantContext(this, org);
        return View("Form", new CounterpartyFormPageViewModel
        {
            Organization = org,
            Form = FromResponse(detail.Value!),
            IsEdit = true,
            CanManage = TenantUiHelpers.HasPermission(org, "tenant.counterparties.manage")
        });
    }

    [HttpPost("organizacoes/{tenantId:guid}/contrapartes/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        Guid tenantId,
        Guid id,
        [Bind(Prefix = "Form")] CounterpartyFormViewModel model,
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
        if (org is null || !TenantUiHelpers.HasPermission(org, "tenant.counterparties.manage"))
        {
            return Forbid();
        }

        TenantUiHelpers.SetTenantContext(this, org);
        model.Id = id;
        if (!ModelState.IsValid || !HasClassification(model))
        {
            if (!HasClassification(model))
            {
                ModelState.AddModelError(string.Empty, "Selecione ao menos uma classificação (cliente, fornecedor, parceiro ou prestador).");
            }

            return View("Form", new CounterpartyFormPageViewModel
            {
                Organization = org,
                Form = model,
                IsEdit = true,
                CanManage = true
            });
        }

        var result = await api.UpdateCounterpartyAsync(token, tenantId, id, ToRequest(model), cancellationToken);
        if (result.Succeeded)
        {
            TempData["Success"] = "Contraparte atualizada.";
            return RedirectToAction(nameof(Index), new { tenantId });
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
                result.UserMessage("A contraparte foi alterada em outra sessão. Recarregue antes de salvar."));
            return View("Form", new CounterpartyFormPageViewModel
            {
                Organization = org,
                Form = model,
                IsEdit = true,
                CanManage = true
            });
        }

        ModelState.AddModelError(string.Empty, TenantUiHelpers.UserFacingError(result, "Não foi possível salvar a contraparte."));
        return View("Form", new CounterpartyFormPageViewModel
        {
            Organization = org,
            Form = model,
            IsEdit = true,
            CanManage = true
        });
    }

    [HttpPost("organizacoes/{tenantId:guid}/contrapartes/{id:guid}/inativar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Inactivate(
        Guid tenantId,
        Guid id,
        ContractVersionActionViewModel model,
        string? search,
        string? status,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        var token = await TenantUiHelpers.AccessTokenAsync(HttpContext);
        if (token is null)
        {
            return Challenge();
        }

        var result = await api.InactivateCounterpartyAsync(
            token,
            tenantId,
            id,
            new ContractVersionRequest(model.Version),
            cancellationToken);

        TempData[result.Status == ApiCallStatus.Success ? "Success" : "Error"] =
            result.Status == ApiCallStatus.Success
                ? "Contraparte inativada. Ela deixa de aparecer para novos contratos."
                : TenantUiHelpers.UserFacingError(result, "Não foi possível inativar a contraparte.");
        return RedirectToAction(nameof(Index), new { tenantId, search, status, page });
    }

    private async Task<IActionResult> RedirectToTenantAsync(string actionName, CancellationToken cancellationToken)
    {
        var token = await TenantUiHelpers.AccessTokenAsync(HttpContext);
        if (token is null)
        {
            return Challenge();
        }

        var tenantId = await TenantUiHelpers.ResolveDefaultTenantIdAsync(api, token, cancellationToken);
        return tenantId is null
            ? RedirectToAction("Index", "Organizations")
            : RedirectToAction(actionName, new { tenantId });
    }

    private static bool HasClassification(CounterpartyFormViewModel model)
        => model.IsClient || model.IsSupplier || model.IsPartner || model.IsProvider;

    private static UpsertCounterpartyRequest ToRequest(CounterpartyFormViewModel model)
        => new(
            model.PersonType,
            model.LegalName,
            model.DisplayName,
            model.DocumentType,
            model.Document,
            model.Email,
            model.Phone,
            model.IsClient,
            model.IsSupplier,
            model.IsPartner,
            model.IsProvider,
            model.AddressLine1,
            model.AddressLine2,
            model.AddressCity,
            model.AddressState,
            model.AddressPostalCode,
            model.AddressCountry,
            model.Version);

    private static CounterpartyFormViewModel FromResponse(CounterpartyResponse row)
        => new()
        {
            Id = row.Id,
            PersonType = row.PersonType,
            LegalName = row.LegalName,
            DisplayName = row.DisplayName,
            DocumentType = row.DocumentType,
            Document = row.DocumentDisplay,
            DocumentDisplay = row.DocumentDisplay,
            Email = row.Email,
            Phone = row.Phone,
            IsClient = row.IsClient,
            IsSupplier = row.IsSupplier,
            IsPartner = row.IsPartner,
            IsProvider = row.IsProvider,
            AddressLine1 = row.AddressLine1,
            AddressLine2 = row.AddressLine2,
            AddressCity = row.AddressCity,
            AddressState = row.AddressState,
            AddressPostalCode = row.AddressPostalCode,
            AddressCountry = row.AddressCountry,
            Status = row.Status,
            Version = row.Version
        };
}
