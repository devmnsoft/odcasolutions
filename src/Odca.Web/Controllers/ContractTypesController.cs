using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odca.Contracts.Contracts;
using Odca.Web.Models;
using Odca.Web.Services;

namespace Odca.Web.Controllers;

[Authorize]
public sealed class ContractTypesController(OdcaApiClient api) : Controller
{
    [HttpGet("tipos-contrato")]
    public Task<IActionResult> IndexEntry(CancellationToken cancellationToken)
        => RedirectToTenantAsync(nameof(Index), cancellationToken);

    [HttpGet("organizacoes/{tenantId:guid}/tipos-contrato")]
    public async Task<IActionResult> Index(Guid tenantId, string? status, CancellationToken cancellationToken)
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

        TenantUiHelpers.SetTenantContext(this, org);
        var list = await api.GetContractTypesAsync(token, tenantId, status, cancellationToken);
        if (list.Status == ApiCallStatus.Unauthorized)
        {
            return Challenge();
        }

        if (list.Status == ApiCallStatus.Forbidden)
        {
            return Forbid();
        }

        return View(new ContractTypeListPageViewModel
        {
            Organization = org,
            Items = list.Succeeded ? list.Value! : [],
            Status = status,
            LoadError = !list.Succeeded,
            CanManage = TenantUiHelpers.HasPermission(org, "tenant.contract_types.manage")
        });
    }

    [HttpGet("organizacoes/{tenantId:guid}/tipos-contrato/novo")]
    public async Task<IActionResult> Create(Guid tenantId, CancellationToken cancellationToken)
    {
        var token = await TenantUiHelpers.AccessTokenAsync(HttpContext);
        if (token is null)
        {
            return Challenge();
        }

        var org = await TenantUiHelpers.RequireOrganizationAsync(api, token, tenantId, cancellationToken);
        if (org is null || !TenantUiHelpers.HasPermission(org, "tenant.contract_types.manage"))
        {
            return Forbid();
        }

        TenantUiHelpers.SetTenantContext(this, org);
        return View("Form", new ContractTypeFormPageViewModel
        {
            Organization = org,
            Form = new ContractTypeFormViewModel(),
            IsEdit = false,
            CanManage = true
        });
    }

    [HttpPost("organizacoes/{tenantId:guid}/tipos-contrato/novo")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        Guid tenantId,
        [Bind(Prefix = "Form")] ContractTypeFormViewModel model,
        CancellationToken cancellationToken)
    {
        var token = await TenantUiHelpers.AccessTokenAsync(HttpContext);
        if (token is null)
        {
            return Challenge();
        }

        var org = await TenantUiHelpers.RequireOrganizationAsync(api, token, tenantId, cancellationToken);
        if (org is null || !TenantUiHelpers.HasPermission(org, "tenant.contract_types.manage"))
        {
            return Forbid();
        }

        TenantUiHelpers.SetTenantContext(this, org);
        if (!ModelState.IsValid)
        {
            return View("Form", new ContractTypeFormPageViewModel
            {
                Organization = org,
                Form = model,
                IsEdit = false,
                CanManage = true
            });
        }

        var result = await api.CreateContractTypeAsync(
            token,
            tenantId,
            new UpsertContractTypeRequest(model.Code, model.Name, model.Description, model.Guidance, null),
            cancellationToken);

        if (result.Succeeded)
        {
            TempData["Success"] = "Tipo de contrato criado.";
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

        ModelState.AddModelError(string.Empty, TenantUiHelpers.UserFacingError(result, "Não foi possível criar o tipo."));
        return View("Form", new ContractTypeFormPageViewModel
        {
            Organization = org,
            Form = model,
            IsEdit = false,
            CanManage = true
        });
    }

    [HttpGet("organizacoes/{tenantId:guid}/tipos-contrato/{id:guid}")]
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

        var detail = await api.GetContractTypeAsync(token, tenantId, id, cancellationToken);
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
            TempData["Error"] = "Tipo de contrato não encontrado.";
            return RedirectToAction(nameof(Index), new { tenantId });
        }

        var row = detail.Value!;
        TenantUiHelpers.SetTenantContext(this, org);
        return View("Form", new ContractTypeFormPageViewModel
        {
            Organization = org,
            Form = new ContractTypeFormViewModel
            {
                Id = row.Id,
                Code = row.Code,
                Name = row.Name,
                Description = row.Description,
                Guidance = row.Guidance,
                Status = row.Status,
                Version = row.Version,
                IsSystemDemo = row.IsSystemDemo
            },
            IsEdit = true,
            CanManage = TenantUiHelpers.HasPermission(org, "tenant.contract_types.manage")
        });
    }

    [HttpPost("organizacoes/{tenantId:guid}/tipos-contrato/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        Guid tenantId,
        Guid id,
        [Bind(Prefix = "Form")] ContractTypeFormViewModel model,
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
        if (org is null || !TenantUiHelpers.HasPermission(org, "tenant.contract_types.manage"))
        {
            return Forbid();
        }

        TenantUiHelpers.SetTenantContext(this, org);
        model.Id = id;
        if (!ModelState.IsValid)
        {
            return View("Form", new ContractTypeFormPageViewModel
            {
                Organization = org,
                Form = model,
                IsEdit = true,
                CanManage = true
            });
        }

        var result = await api.UpdateContractTypeAsync(
            token,
            tenantId,
            id,
            new UpsertContractTypeRequest(model.Code, model.Name, model.Description, model.Guidance, model.Version),
            cancellationToken);

        if (result.Succeeded)
        {
            TempData["Success"] = "Tipo de contrato atualizado.";
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
                result.UserMessage("O tipo foi alterado em outra sessão. Recarregue antes de salvar."));
            return View("Form", new ContractTypeFormPageViewModel
            {
                Organization = org,
                Form = model,
                IsEdit = true,
                CanManage = true
            });
        }

        ModelState.AddModelError(string.Empty, TenantUiHelpers.UserFacingError(result, "Não foi possível salvar o tipo."));
        return View("Form", new ContractTypeFormPageViewModel
        {
            Organization = org,
            Form = model,
            IsEdit = true,
            CanManage = true
        });
    }

    [HttpPost("organizacoes/{tenantId:guid}/tipos-contrato/{id:guid}/status")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetStatus(
        Guid tenantId,
        Guid id,
        long version,
        string status,
        CancellationToken cancellationToken)
    {
        if (status is not ("active" or "inactive"))
        {
            return BadRequest();
        }

        var token = await TenantUiHelpers.AccessTokenAsync(HttpContext);
        if (token is null)
        {
            return Challenge();
        }

        var result = await api.SetContractTypeStatusAsync(
            token,
            tenantId,
            id,
            new SetContractTypeStatusRequest(status, version),
            cancellationToken);

        TempData[result.Status == ApiCallStatus.Success ? "Success" : "Error"] =
            result.Status == ApiCallStatus.Success
                ? (status == "active"
                    ? "Tipo reativado e disponível para novos contratos."
                    : "Tipo inativado. Continua visível no histórico, mas não pode ser escolhido em novos contratos.")
                : TenantUiHelpers.UserFacingError(result, "Não foi possível alterar o estado do tipo.");
        return RedirectToAction(nameof(Index), new { tenantId });
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
}
