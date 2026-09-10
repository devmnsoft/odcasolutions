using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odca.Application.Tenancy;
using Odca.Contracts.Tenancy;
using System.Security.Cryptography;
using System.Text;

namespace Odca.Api.Controllers;

[ApiController]
[Route("api/v1/organizations")]
[Authorize(Policy = "PasswordChanged")]
public sealed class OrganizationsController(ITenantAdministrationRepository repository) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<OrganizationSummary>>> List(CancellationToken cancellationToken)
    {
        if (!Actor(out var actor)) return Unauthorized();
        var rows=await repository.ListOrganizationsAsync(actor,cancellationToken);
        return Ok(rows.Select(x=>new OrganizationSummary(x.Id,x.Name,x.Status,x.Version,x.Permissions)));
    }

    [HttpGet("{tenantId:guid}")]
    public async Task<ActionResult<OrganizationDetails>> Get(Guid tenantId,CancellationToken cancellationToken)
    {
        if(!Actor(out var actor))return Unauthorized(); var row=await repository.GetOrganizationAsync(actor,tenantId,cancellationToken);
        return row is null?Forbid():Ok(new OrganizationDetails(row.Id,row.Name,row.Timezone,row.Status,row.Version));
    }

    [HttpPut("{tenantId:guid}")]
    public async Task<IActionResult> Update(Guid tenantId,[FromBody]UpdateOrganizationRequest request,CancellationToken cancellationToken)
    {
        if(!Actor(out var actor))return Unauthorized();
        if(string.IsNullOrWhiteSpace(request.Name)||request.Name.Trim().Length>160||string.IsNullOrWhiteSpace(request.Timezone)||request.Timezone.Length>80)return ValidationProblem();
        var result=await repository.UpdateOrganizationAsync(actor,tenantId,request.Name.Trim(),request.Timezone.Trim(),request.Version,cancellationToken);
        return result switch { UpdateOrganizationResult.Updated=>NoContent(),UpdateOrganizationResult.Conflict=>Conflict(new ProblemDetails{Title="A organização foi alterada em outra sessão.",Detail="Recarregue os dados e revise suas alterações."}),_=>Forbid()};
    }

    [HttpGet("{tenantId:guid}/members")]
    public async Task<ActionResult<IReadOnlyList<TeamMemberResponse>>> Members(Guid tenantId,[FromQuery]string? search,[FromQuery]string? status,CancellationToken cancellationToken)
    {
        if(!Actor(out var actor))return Unauthorized(); if(status is not null&&status is not("active" or "blocked" or "inactive"))return BadRequest();
        var rows=await repository.ListMembersAsync(actor,tenantId,search,status,cancellationToken); return Ok(rows.Select(x=>new TeamMemberResponse(x.UserId,x.Name,x.Email,x.Status,x.Roles)));
    }

    [HttpGet("{tenantId:guid}/roles")]
    public async Task<ActionResult<IReadOnlyList<TenantRoleResponse>>> Roles(Guid tenantId,CancellationToken cancellationToken)
    { if(!Actor(out var actor))return Unauthorized();var rows=await repository.ListRolesAsync(actor,tenantId,cancellationToken);return Ok(rows.Select(x=>new TenantRoleResponse(x.Id,x.Name,x.IsSystem,x.Permissions))); }

    [HttpPost("{tenantId:guid}/roles")]
    public async Task<ActionResult<TenantRoleResponse>> CreateRole(Guid tenantId,CreateTenantRoleRequest request,CancellationToken cancellationToken)
    { if(!Actor(out var actor))return Unauthorized();if(string.IsNullOrWhiteSpace(request.Name)||request.Name.Length>100||request.Permissions.Length is 0 or >20)return ValidationProblem();var role=await repository.CreateRoleAsync(actor,tenantId,request.Name.Trim(),request.Permissions,cancellationToken);return role is null?Forbid():CreatedAtAction(nameof(Roles),new{tenantId},new TenantRoleResponse(role.Id,role.Name,role.IsSystem,role.Permissions)); }

    [HttpPost("{tenantId:guid}/invitations")]
    public async Task<ActionResult<InvitationResponse>> Invite(Guid tenantId,CreateInvitationRequest request,CancellationToken cancellationToken)
    { if(!Actor(out var actor))return Unauthorized();if(!System.Net.Mail.MailAddress.TryCreate(request.Email,out _)||string.IsNullOrWhiteSpace(request.IdempotencyKey)||request.IdempotencyKey.Length>100)return ValidationProblem();var invitation=await repository.CreateInvitationAsync(actor,tenantId,request.Email.Trim(),request.RoleId,request.IdempotencyKey,cancellationToken);return invitation is null?Conflict(new ProblemDetails{Title="Convite não disponível",Detail="Verifique a permissão, o perfil, o estado comercial e o limite de assentos."}):Ok(new InvitationResponse(invitation.Id,invitation.Recipient,invitation.State,invitation.ExpiresAt)); }

    [HttpPost("invitations/accept")]
    public async Task<IActionResult> Accept(AcceptInvitationRequest request,CancellationToken cancellationToken)
    { if(!Actor(out var actor))return Unauthorized();if(request.Token.Length is < 32 or > 256)return BadRequest();var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.Token))).ToLowerInvariant();return await repository.AcceptInvitationAsync(actor,request.InvitationId,hash,cancellationToken)?Ok(new{accepted=true}):Conflict(new ProblemDetails{Title="Convite inválido",Detail="O convite não pertence ao e-mail verificado desta conta, expirou ou já foi utilizado."}); }

    private bool Actor(out Guid actor)=>Guid.TryParse(User.FindFirstValue("sub"),out actor);
}
