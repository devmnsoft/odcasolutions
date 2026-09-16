using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odca.Contracts.Consumption;
using Odca.Web.Models;
using Odca.Web.Services;
namespace Odca.Web.Controllers;

[Authorize]
[Route("organizacoes/{tenantId:guid}/plano-e-consumo")]
public sealed class ConsumptionController(OdcaApiClient api):Controller
{
 [HttpGet] public async Task<IActionResult> Index(Guid tenantId,CancellationToken ct){var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Challenge();var summary=await api.GetConsumptionAsync(token,tenantId,ct);if(summary.Status==ApiCallStatus.Forbidden)return Forbid();if(!summary.Succeeded||summary.Value is null){Response.StatusCode=503;return View("ServiceUnavailable");}var packages=await api.GetStoragePackagesAsync(token,ct);var requests=await api.GetStorageRequestsAsync(token,tenantId,ct);if(!packages.Succeeded||!requests.Succeeded){Response.StatusCode=503;return View("ServiceUnavailable");}ViewData["OrganizationName"]=summary.Value.OrganizationName;return View(new ConsumptionViewModel(summary.Value,packages.Value!,requests.Value!));}
 [HttpPost("solicitacoes")] public async Task<IActionResult> Request(Guid tenantId,Guid packageId,int quantity,Guid idempotencyKey,CancellationToken ct){var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Challenge();var result=await api.RequestStorageAsync(token,tenantId,new(packageId,quantity,idempotencyKey),ct);if(!result.Succeeded){TempData["Error"]=result.UserMessage("Não foi possível registrar a solicitação. Revise o pacote e tente novamente.");}else TempData["Success"]="Solicitação registrada. A capacidade só mudará após aprovação explícita.";return RedirectToAction(nameof(Index),new{tenantId});}
}
