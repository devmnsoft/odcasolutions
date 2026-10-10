using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odca.Contracts.Administration;
using Odca.Contracts.Consumption;
using Odca.Contracts.Plans;
using Odca.Contracts.Tenancy;
using Odca.Web.Models;
using Odca.Web.Services;
namespace Odca.Web.Controllers;
[Authorize(Roles="SuperAdministrator")][Route("administracao/clientes")]
public sealed class CustomersController(OdcaApiClient api):Controller
{
 [HttpGet]public async Task<IActionResult> Index(string? search,string? plan,string? status,CancellationToken ct){var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Challenge();var result=await api.GetCustomersAsync(token,search,plan,status,ct);if(!result.Succeeded){Response.StatusCode=503;return View("ServiceUnavailable");}return View(new CustomerListViewModel(result.Value!,search,plan,status));}
  [HttpGet("usuarios")]public async Task<IActionResult> Users(string? search,CancellationToken ct){var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Challenge();ViewData["Title"]="Localizar usuários";var result=await api.SearchPlatformUsersAsync(token,search,100,ct);if(!result.Succeeded){Response.StatusCode=503;return View("ServiceUnavailable");}return View(new UserSearchViewModel(result.Value!,search));}
 [HttpGet("{tenantId:guid}")]public Task<IActionResult> Details(Guid tenantId,CancellationToken ct)=>RenderDetails(tenantId,ct);
 [HttpPost("{tenantId:guid}/plano/analise")][ValidateAntiForgeryToken]public async Task<IActionResult> PreviewPlan(Guid tenantId,string planCode,string? justification,CancellationToken ct){var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Challenge();var reason=justification?.Trim()??string.Empty;if(string.IsNullOrWhiteSpace(planCode)||reason.Length is <5 or >500){TempData["Error"]="Escolha o plano pelo nome e informe uma justificativa entre 5 e 500 caracteres.";return RedirectToAction(nameof(Details),new{tenantId});}var preview=await api.PreviewOrganizationPlanChangeAsync(token,new PlanChangeRequest(tenantId,planCode.Trim(),reason),ct);if(!preview.Succeeded||preview.Value is null){TempData["Error"]=preview.UserMessage("Não foi possível analisar o impacto. Nenhuma alteração foi aplicada.");return RedirectToAction(nameof(Details),new{tenantId});}ViewData["PlanPreview"]=preview.Value;ViewData["PlanCode"]=planCode.Trim();ViewData["PlanJustification"]=reason;return await RenderDetails(tenantId,ct);}
 [HttpPost("{tenantId:guid}/plano")][ValidateAntiForgeryToken]public async Task<IActionResult> ApplyPlan(Guid tenantId,string planCode,string? justification,CancellationToken ct){var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Challenge();var reason=justification?.Trim()??string.Empty;if(string.IsNullOrWhiteSpace(planCode)||reason.Length is <5 or >500){TempData["Error"]="A confirmação exige o plano e uma justificativa entre 5 e 500 caracteres.";return RedirectToAction(nameof(Details),new{tenantId});}var result=await api.ApplyOrganizationPlanChangeAsync(token,new PlanChangeRequest(tenantId,planCode.Trim(),reason),ct);TempData[result.Succeeded?"Success":"Error"]=result.Succeeded?"Plano alterado e auditado. Pacientes, documentos e histórico foram preservados. Nenhum pagamento foi registrado.":result.UserMessage("A alteração de plano não foi aplicada.");return RedirectToAction(nameof(Details),new{tenantId});}
 [HttpPost("{tenantId:guid}/solicitacoes/{requestId:guid}")][ValidateAntiForgeryToken]public async Task<IActionResult> Decide(Guid tenantId,Guid requestId,string decision,string? reason,CancellationToken ct){var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Challenge();var result=await api.DecideStorageAsync(token,tenantId,requestId,new(decision,reason),ct);TempData[result.Succeeded?"Success":"Error"]=result.Succeeded?"Decisão confirmada e auditada.":result.UserMessage("A solicitação já foi decidida ou não está disponível.");return RedirectToAction(nameof(Details),new{tenantId});}
 [HttpPost("{tenantId:guid}/concessoes")][ValidateAntiForgeryToken]public async Task<IActionResult> Grant(Guid tenantId,decimal? quantity,string? unit,long? quantityBytes,string reason,Guid idempotencyKey,CancellationToken ct){var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Challenge();long bytes;if(quantity.HasValue&&quantity.Value>0){var cleanUnit=unit?.Trim().ToUpperInvariant()??"GB";bytes=cleanUnit switch{"TB"=>(long)Math.Round(quantity.Value*1024L*1024L*1024L*1024L),"MB"=>(long)Math.Round(quantity.Value*1024L*1024L),_ =>(long)Math.Round(quantity.Value*1024L*1024L*1024L)};}else if(quantityBytes.HasValue&&quantityBytes.Value>0){bytes=quantityBytes.Value;}else{TempData["Error"]="Informe uma quantidade válida maior que zero.";return RedirectToAction(nameof(Details),new{tenantId});}if(bytes<=0||bytes>100L*1024L*1024L*1024L*1024L){TempData["Error"]="A capacidade informada está fora dos limites operacionais permitidos.";return RedirectToAction(nameof(Details),new{tenantId});}var result=await api.GrantStorageAsync(token,tenantId,new(bytes,reason,idempotencyKey,null),ct);TempData[result.Succeeded?"Success":"Error"]=result.Succeeded?"Capacidade adicional concedida e auditada.":result.UserMessage("Não foi possível conceder a capacidade.");return RedirectToAction(nameof(Details),new{tenantId});}
 [HttpGet("novo")]
 public IActionResult Create()
 {
     ViewData["Title"] = "Nova organização";
     return View(new CreateCustomerViewModel());
 }

 [HttpPost("novo")]
 [ValidateAntiForgeryToken]
 public async Task<IActionResult> Create(CreateCustomerViewModel model, CancellationToken ct)
 {
     ViewData["Title"] = "Nova organização";
     if (!ModelState.IsValid) return View(model);
     var token = await HttpContext.GetTokenAsync("access_token");
     if (token is null) return Challenge();
     var result = await api.CreatePlatformCustomerAsync(token, new CreatePlatformCustomerRequest(
         model.OrganizationName.Trim(),
         model.Document.Trim(),
         model.PlanCode.Trim(),
         model.AdminName.Trim(),
         model.AdminEmail.Trim(),
         model.InitialPassword,
         model.ActivateDirectly), ct);
     if (!result.Succeeded)
     {
         ModelState.AddModelError(string.Empty, result.ErrorDetail ?? result.ErrorTitle ?? "Não foi possível cadastrar a organização. Verifique se o CNPJ/CPF já está em uso.");
         return View(model);
     }
     TempData["Success"] = $"Organização '{result.Value!.OrganizationName}' cadastrada com sucesso.";
     return RedirectToAction(nameof(Details), new { tenantId = result.Value.TenantId });
 }

 [HttpPost("{tenantId:guid}/funcionalidades/{featureCode}")]
 [ValidateAntiForgeryToken]
 public async Task<IActionResult> SetFeature(Guid tenantId, string featureCode, bool blocked, string reason, CancellationToken ct)
 {
     var token = await HttpContext.GetTokenAsync("access_token");
     if (token is null) return Challenge();
     var result = await api.SetOrganizationFeatureAsync(token, tenantId, featureCode, new SetOrganizationFeatureRequest(blocked, reason ?? string.Empty), ct);
     TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded
         ? (blocked
             ? "Funcionalidade bloqueada para esta organização. Plano, permissões e documentos foram preservados."
             : "Bloqueio administrativo removido. Se o plano não incluir a funcionalidade, ela continua indisponível por restrição de plano.")
         : result.UserMessage("Não foi possível registrar a decisão. Nenhuma liberação ou pagamento foi presumido.");
     return RedirectToAction(nameof(Details), new { tenantId });
 }

 [HttpPost("{tenantId:guid}/situacao")][ValidateAntiForgeryToken]public async Task<IActionResult> ChangeStatus(Guid tenantId,bool restore,string reason,CancellationToken ct){var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Challenge();if(string.IsNullOrWhiteSpace(reason)||reason.Trim().Length<5){TempData["Error"]="Informe uma justificativa com pelo menos 5 caracteres.";return RedirectToAction(nameof(Details),new{tenantId});}var result=await api.ChangeOrganizationStatusAsync(token,tenantId,restore,new(reason.Trim()),ct);TempData[result.Succeeded?"Success":"Error"]=result.Succeeded?(restore?"Organização restaurada. Os bloqueios individuais foram preservados.":"Organização suspensa. Novas operações do cliente foram bloqueadas."):result.UserMessage("A situação da organização não pôde ser alterada.");return RedirectToAction(nameof(Details),new{tenantId});}

 private async Task<IActionResult> RenderDetails(Guid tenantId,CancellationToken ct)
 {
  var token=await HttpContext.GetTokenAsync("access_token");
  if(token is null)return Challenge();
  var result=await api.GetCustomerAsync(token,tenantId,ct);
  if(!result.Succeeded||result.Value is null)return result.Status==ApiCallStatus.NotFound?NotFound():StatusCode(503);
  var features=await api.GetOrganizationFeaturesAsync(token,tenantId,ct);
  var plans=await api.GetPlansAsync(token,ct);
  ViewData["OrganizationName"]=result.Value.Summary.OrganizationName;
  ViewData["Features"]=features.Succeeded?features.Value:null;
  ViewData["FeaturesError"]=features.Succeeded?null:features.UserMessage("Não foi possível consultar as funcionalidades. Esta falha não significa que a organização esteja sem bloqueios.");
  ViewData["Plans"]=plans.Succeeded?plans.Value:Array.Empty<PlanCatalogResponse>();
  ViewData["PlansError"]=plans.Succeeded?null:plans.UserMessage("Não foi possível consultar o catálogo vigente.");
  return View("Details",result.Value);
 }
}
