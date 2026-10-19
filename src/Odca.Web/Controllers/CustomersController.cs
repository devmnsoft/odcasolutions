using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Odca.Configuration.Localization;
using Odca.Contracts.Administration;
using Odca.Contracts.Consumption;
using Odca.Contracts.Plans;
using Odca.Contracts.Tenancy;
using Odca.Web.Models;
using Odca.Web.Services;
namespace Odca.Web.Controllers;
[Authorize(Roles="SuperAdministrator")][Route("administracao/clientes")]
public sealed class CustomersController(OdcaApiClient api, LocalizationService loc):Controller
{
 // D-OC50 (S16): padrão canônico de cultura (CultureMiddleware) e localização das mensagens BFF.
 private string? Culture => HttpContext.Items[Odca.Web.Middleware.CultureMiddleware.ItemsKey] as string;
 private string L(string key, params string[] args) => loc.T(key, Culture, args);
 [HttpGet]public async Task<IActionResult> Index(string? search,string? plan,string? status,CancellationToken ct){var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Challenge();var result=await api.GetCustomersAsync(token,search,plan,status,ct);if(!result.Succeeded){Response.StatusCode=503;return View("ServiceUnavailable");}return View(new CustomerListViewModel(result.Value!,search,plan,status));}
  [HttpGet("usuarios")]public async Task<IActionResult> Users(string? search,CancellationToken ct){var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Challenge();ViewData["Title"]=L("localizar-usuarios");var result=await api.SearchPlatformUsersAsync(token,search,100,ct);if(!result.Succeeded){Response.StatusCode=503;return View("ServiceUnavailable");}return View(new UserSearchViewModel(result.Value!,search));}
 [HttpGet("{tenantId:guid}")]public Task<IActionResult> Details(Guid tenantId,CancellationToken ct)=>RenderDetails(tenantId,ct);
 [HttpPost("{tenantId:guid}/plano/analise")][ValidateAntiForgeryToken]public async Task<IActionResult> PreviewPlan(Guid tenantId,string planCode,string? justification,CancellationToken ct){var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Challenge();var reason=justification?.Trim()??string.Empty;if(string.IsNullOrWhiteSpace(planCode)||reason.Length is <5 or >500){TempData["Error"]=L("cust.err_plan_justify");return RedirectToAction(nameof(Details),new{tenantId});}var preview=await api.PreviewOrganizationPlanChangeAsync(token,new PlanChangeRequest(tenantId,planCode.Trim(),reason),ct);if(!preview.Succeeded||preview.Value is null){TempData["Error"]=preview.UserMessage(L("cust.err_preview"));return RedirectToAction(nameof(Details),new{tenantId});}ViewData["PlanPreview"]=preview.Value;ViewData["PlanCode"]=planCode.Trim();ViewData["PlanJustification"]=reason;return await RenderDetails(tenantId,ct);}
 [HttpPost("{tenantId:guid}/plano")][ValidateAntiForgeryToken]public async Task<IActionResult> ApplyPlan(Guid tenantId,string planCode,string? justification,CancellationToken ct){var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Challenge();var reason=justification?.Trim()??string.Empty;if(string.IsNullOrWhiteSpace(planCode)||reason.Length is <5 or >500){TempData["Error"]=L("cust.err_apply_justify");return RedirectToAction(nameof(Details),new{tenantId});}var result=await api.ApplyOrganizationPlanChangeAsync(token,new PlanChangeRequest(tenantId,planCode.Trim(),reason),ct);TempData[result.Succeeded?"Success":"Error"]=result.Succeeded?L("cust.ok_apply"):result.UserMessage(L("cust.err_apply"));return RedirectToAction(nameof(Details),new{tenantId});}
 [HttpPost("{tenantId:guid}/solicitacoes/{requestId:guid}")][ValidateAntiForgeryToken]public async Task<IActionResult> Decide(Guid tenantId,Guid requestId,string decision,string? reason,CancellationToken ct){var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Challenge();var result=await api.DecideStorageAsync(token,tenantId,requestId,new(decision,reason),ct);TempData[result.Succeeded?"Success":"Error"]=result.Succeeded?L("cust.ok_decide"):result.UserMessage(L("cust.err_decide"));return RedirectToAction(nameof(Details),new{tenantId});}
 [HttpPost("{tenantId:guid}/concessoes")][ValidateAntiForgeryToken]public async Task<IActionResult> Grant(Guid tenantId,decimal? quantity,string? unit,long? quantityBytes,string reason,Guid idempotencyKey,CancellationToken ct){var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Challenge();long bytes;if(quantity.HasValue&&quantity.Value>0){var cleanUnit=unit?.Trim().ToUpperInvariant()??"GB";bytes=cleanUnit switch{"TB"=>(long)Math.Round(quantity.Value*1024L*1024L*1024L*1024L),"MB"=>(long)Math.Round(quantity.Value*1024L*1024L),_ =>(long)Math.Round(quantity.Value*1024L*1024L*1024L)};}else if(quantityBytes.HasValue&&quantityBytes.Value>0){bytes=quantityBytes.Value;}else{TempData["Error"]=L("cust.err_grant_qty");return RedirectToAction(nameof(Details),new{tenantId});}if(bytes<=0||bytes>100L*1024L*1024L*1024L*1024L){TempData["Error"]=L("cust.err_grant_range");return RedirectToAction(nameof(Details),new{tenantId});}var result=await api.GrantStorageAsync(token,tenantId,new(bytes,reason,idempotencyKey,null),ct);TempData[result.Succeeded?"Success":"Error"]=result.Succeeded?L("cust.ok_grant"):result.UserMessage(L("cust.err_grant"));return RedirectToAction(nameof(Details),new{tenantId});}
 [HttpGet("novo")]
 public async Task<IActionResult> Create(CancellationToken ct)
 {
     ViewData["Title"] = L("cust.new_title");
     var token = await HttpContext.GetTokenAsync("access_token");
     if (token is null) return Challenge();
     // D-OC34: a lista de planos vem do catálogo vigente (sem código fantasma hardcoded); o
     // default selecionado e sempre um codigo valido do catalogo.
     var plans = await api.GetPlansAsync(token, ct);
     var model = new CreateCustomerViewModel();
     if (plans.Succeeded && plans.Value is not null)
     {
         if (!plans.Value.Any(plan => string.Equals(plan.Code, model.PlanCode, StringComparison.Ordinal))) model.PlanCode = plans.Value[0].Code;
         ViewData["PlanItems"] = PlanItems(plans.Value);
         return View(model);
     }
     ViewData["PlanItems"] = new List<SelectListItem> { new(L("cust.err_plans_catalog"), string.Empty) };
     return View(model);
 }

 [HttpPost("novo")]
 [ValidateAntiForgeryToken]
 public async Task<IActionResult> Create(CreateCustomerViewModel model, CancellationToken ct)
 {
     ViewData["Title"] = L("cust.new_title");
     if (!ModelState.IsValid) return View(model);
     var token = await HttpContext.GetTokenAsync("access_token");
     if (token is null) return Challenge();
     var plans = await api.GetPlansAsync(token, ct);
     // D-OC34: plano desconhecido (ou catalogo indisponivel) falha com erro estavel antes de
     // tocar no cadastro (a API continua revalidando com 400 api.consumption.invalid_plan).
     if (!plans.Succeeded || plans.Value is null) { Response.StatusCode = 503; return View("ServiceUnavailable"); }
     if (!plans.Value.Any(plan => string.Equals(plan.Code, model.PlanCode.Trim(), StringComparison.Ordinal)))
     {
         ModelState.AddModelError(nameof(CreateCustomerViewModel.PlanCode), L("cust.err_plan_invalid"));
         ViewData["PlanItems"] = PlanItems(plans.Value);
         return View(model);
     }
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
         ModelState.AddModelError(string.Empty, result.ErrorDetail ?? result.ErrorTitle ?? L("cust.err_create"));
         ViewData["PlanItems"] = PlanItems(plans.Value);
         return View(model);
     }
     TempData["Success"] = L("cust.ok_create", result.Value!.OrganizationName);
     return RedirectToAction(nameof(Details), new { tenantId = result.Value.TenantId });
 }

 private System.Collections.Generic.List<SelectListItem> PlanItems(PlanCatalogResponse[] plans)
     => plans.Select(plan => new SelectListItem(L("cust.plan_item", plan.DisplayName, plan.ActiveSeats.ToString(System.Globalization.CultureInfo.InvariantCulture)), plan.Code)).ToList();

 [HttpPost("{tenantId:guid}/funcionalidades/{featureCode}")]
 [ValidateAntiForgeryToken]
 public async Task<IActionResult> SetFeature(Guid tenantId, string featureCode, bool blocked, string reason, CancellationToken ct)
 {
     var token = await HttpContext.GetTokenAsync("access_token");
     if (token is null) return Challenge();
     var result = await api.SetOrganizationFeatureAsync(token, tenantId, featureCode, new SetOrganizationFeatureRequest(blocked, reason ?? string.Empty), ct);
     TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded
         ? (blocked
             ? L("cust.ok_feature_block")
             : L("cust.ok_feature_unblock"))
         : result.UserMessage(L("cust.err_feature"));
     return RedirectToAction(nameof(Details), new { tenantId });
 }

 [HttpPost("{tenantId:guid}/situacao")][ValidateAntiForgeryToken]public async Task<IActionResult> ChangeStatus(Guid tenantId,bool restore,string reason,CancellationToken ct){var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Challenge();if(string.IsNullOrWhiteSpace(reason)||reason.Trim().Length<5){TempData["Error"]=L("cust.err_reason_short");return RedirectToAction(nameof(Details),new{tenantId});}var result=await api.ChangeOrganizationStatusAsync(token,tenantId,restore,new(reason.Trim()),ct);TempData[result.Succeeded?"Success":"Error"]=result.Succeeded?(restore?L("cust.ok_restore"):L("cust.ok_suspend")):result.UserMessage(L("cust.err_status"));return RedirectToAction(nameof(Details),new{tenantId});}

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
  ViewData["FeaturesError"]=features.Succeeded?null:features.UserMessage(L("cust.err_features"));
  ViewData["Plans"]=plans.Succeeded?plans.Value:Array.Empty<PlanCatalogResponse>();
  ViewData["PlansError"]=plans.Succeeded?null:plans.UserMessage(L("cust.err_catalog"));
  return View("Details",result.Value);
 }
}
