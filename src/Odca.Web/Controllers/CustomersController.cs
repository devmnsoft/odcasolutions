using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odca.Contracts.Consumption;
using Odca.Web.Models;
using Odca.Web.Services;
namespace Odca.Web.Controllers;
[Authorize(Roles="SuperAdministrator")][Route("administracao/clientes")]
public sealed class CustomersController(OdcaApiClient api):Controller
{
 [HttpGet]public async Task<IActionResult> Index(string? search,CancellationToken ct){var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Challenge();var result=await api.GetCustomersAsync(token,search,ct);if(!result.Succeeded){Response.StatusCode=503;return View("ServiceUnavailable");}return View(new CustomerListViewModel(result.Value!,search));}
 [HttpGet("{tenantId:guid}")]public async Task<IActionResult> Details(Guid tenantId,CancellationToken ct){var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Challenge();var result=await api.GetCustomerAsync(token,tenantId,ct);if(!result.Succeeded||result.Value is null)return result.Status==ApiCallStatus.NotFound?NotFound():StatusCode(503);ViewData["OrganizationName"]=result.Value.Summary.OrganizationName;return View(result.Value);}
 [HttpPost("{tenantId:guid}/solicitacoes/{requestId:guid}")][ValidateAntiForgeryToken]public async Task<IActionResult> Decide(Guid tenantId,Guid requestId,string decision,string? reason,CancellationToken ct){var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Challenge();var result=await api.DecideStorageAsync(token,tenantId,requestId,new(decision,reason),ct);TempData[result.Succeeded?"Success":"Error"]=result.Succeeded?"Decisão confirmada e auditada.":result.UserMessage("A solicitação já foi decidida ou não está disponível.");return RedirectToAction(nameof(Details),new{tenantId});}
 [HttpPost("{tenantId:guid}/concessoes")][ValidateAntiForgeryToken]public async Task<IActionResult> Grant(Guid tenantId,long quantityBytes,string reason,Guid idempotencyKey,CancellationToken ct){var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Challenge();var result=await api.GrantStorageAsync(token,tenantId,new(quantityBytes,reason,idempotencyKey,null),ct);TempData[result.Succeeded?"Success":"Error"]=result.Succeeded?"Capacidade adicional concedida e auditada.":result.UserMessage("Não foi possível conceder a capacidade.");return RedirectToAction(nameof(Details),new{tenantId});}
}
