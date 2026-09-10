using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odca.Contracts.Tenancy;
using Odca.Web.Models;
using Odca.Web.Services;
namespace Odca.Web.Controllers;
[Authorize]
public sealed class OrganizationsController(OdcaApiClient api):Controller
{
 [HttpGet("organizacoes")]
 public async Task<IActionResult> Index(CancellationToken ct){var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Challenge();var result=await api.GetOrganizationsAsync(token,ct);if(!result.Succeeded)return Forbid();return View(result.Value);}
 [HttpGet("organizacoes/{tenantId:guid}/equipe")]
 public async Task<IActionResult> Team(Guid tenantId,string? search,string? status,string? inviteEmail,Guid? inviteRoleId,string? inviteKey,CancellationToken ct){var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Challenge();var orgs=await api.GetOrganizationsAsync(token,ct);var org=orgs.Value?.SingleOrDefault(x=>x.Id==tenantId);if(org is null)return Forbid();var members=await api.GetMembersAsync(token,tenantId,search,status,ct);var roles=await api.GetRolesAsync(token,tenantId,ct);if(!members.Succeeded||!roles.Succeeded)return Forbid();ViewData["OrganizationName"]=org.Name;return View(new TeamPageViewModel(org,members.Value!,roles.Value!,search,status,inviteEmail??string.Empty,inviteRoleId,string.IsNullOrWhiteSpace(inviteKey)?Guid.NewGuid().ToString("N"):inviteKey));}
 [HttpPost("organizacoes/{tenantId:guid}/convites")]
 public async Task<IActionResult> Invite(Guid tenantId,InviteViewModel model,CancellationToken ct){if(tenantId!=model.TenantId) return BadRequest();if(!ModelState.IsValid){TempData["Error"]="Revise os campos do convite.";return RedirectToAction(nameof(Team),new{tenantId,inviteEmail=model.Email,inviteRoleId=model.RoleId,inviteKey=model.IdempotencyKey});}var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Challenge();var result=await api.InviteAsync(token,tenantId,new CreateInvitationRequest(model.Email,model.RoleId,model.IdempotencyKey),ct);TempData[result.Succeeded?"Success":"Error"]=result.Succeeded?"Convite reservado e enfileirado; a entrega ainda será processada.":"Não foi possível convidar. Verifique assentos, perfil ou repetição com dados diferentes.";return RedirectToAction(nameof(Team),new{tenantId,inviteEmail=result.Succeeded?null:model.Email,inviteRoleId=result.Succeeded?null:model.RoleId,inviteKey=result.Succeeded?null:model.IdempotencyKey});}
 [HttpPost("organizacoes/{tenantId:guid}/perfis")]
 public async Task<IActionResult> CreateRole(Guid tenantId,string name,string[] permissions,CancellationToken ct){var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Challenge();var result=await api.CreateRoleAsync(token,tenantId,new CreateTenantRoleRequest(name,permissions),ct);TempData[result.Succeeded?"Success":"Error"]=result.Succeeded?"Perfil criado.":"O perfil contém uma permissão que você não pode delegar.";return RedirectToAction(nameof(Team),new{tenantId});}
 [HttpGet("convites/aceitar")]
 public IActionResult Accept(Guid invitationId,string token)=>View(new AcceptInvitationRequest(invitationId,token));
 [HttpPost("convites/aceitar")]
 public async Task<IActionResult> Accept(AcceptInvitationRequest request,CancellationToken ct){var access=await HttpContext.GetTokenAsync("access_token");if(access is null)return Challenge();var result=await api.AcceptInvitationAsync(access,request,ct);if(!result.Succeeded){ModelState.AddModelError(string.Empty,"Convite inválido, expirado, usado ou destinado a outro e-mail verificado.");return View(request);}await HttpContext.SignOutAsync();TempData["Success"]="Convite aceito. Entre novamente para renovar suas permissões.";return RedirectToAction("Login","Account");}
}
