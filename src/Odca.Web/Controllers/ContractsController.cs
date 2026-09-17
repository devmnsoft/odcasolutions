using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odca.Web.Models;
using Odca.Web.Services;

namespace Odca.Web.Controllers;

[Authorize]
[Route("organizacoes/{tenantId:guid}/contratos/{contractId:guid}")]
public sealed class ContractsController(OdcaApiClient api) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Sheet(Guid tenantId, Guid contractId, CancellationToken ct)
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

        return View(new ContractSheetViewModel
        {
            Sheet = result.Value,
            Error = result.Succeeded ? null : result.UserMessage("Não foi possível abrir a ficha do contrato.")
        });
    }

    [HttpPost("biblioteca-oficial")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> InstallOfficialLibrary(Guid tenantId, Guid contractId, CancellationToken ct)
    {
        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null) return Challenge();
        var result = await api.InstallOfficialStudioTemplatesAsync(token, tenantId, ct);
        TempData[result.Succeeded ? "ContractSheetNotice" : "ContractSheetError"] = result.Succeeded
            ? "Biblioteca oficial instalada ou já presente neste tenant."
            : result.UserMessage("Não foi possível instalar a biblioteca oficial.");
        return RedirectToAction(nameof(Sheet), new { tenantId, contractId });
    }
}
