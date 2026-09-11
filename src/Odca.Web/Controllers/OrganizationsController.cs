using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odca.Contracts.Tenancy;
using Odca.Web.Models;
using Odca.Web.Services;

namespace Odca.Web.Controllers;

[Authorize]
public sealed class OrganizationsController(OdcaApiClient api) : Controller
{
    private static readonly HashSet<string> MemberActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "block", "unblock", "inactivate", "restore"
    };

    [HttpGet("organizacoes")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var token = await AccessTokenAsync();
        if (token is null)
        {
            return Challenge();
        }

        var result = await api.GetOrganizationsAsync(token, cancellationToken);
        if (result.Status == ApiCallStatus.Unauthorized)
        {
            return Challenge();
        }

        if (!result.Succeeded)
        {
            return result.Status == ApiCallStatus.Forbidden
                ? Forbid()
                : View("~/Views/Shared/ServiceUnavailable.cshtml");
        }

        return View(result.Value);
    }

    [HttpGet("organizacoes/{tenantId:guid}/editar")]
    public async Task<IActionResult> Edit(Guid tenantId, CancellationToken cancellationToken)
    {
        var token = await AccessTokenAsync();
        if (token is null)
        {
            return Challenge();
        }

        var org = await RequireOrganizationAsync(token, tenantId, cancellationToken);
        if (org is null)
        {
            return Forbid();
        }

        var details = await api.GetOrganizationAsync(token, tenantId, cancellationToken);
        if (details.Status == ApiCallStatus.Unauthorized)
        {
            return Challenge();
        }

        if (!details.Succeeded)
        {
            return MapReadFailure(details.Status);
        }

        ViewData["OrganizationName"] = details.Value!.Name;
        ViewData["TenantId"] = details.Value.Id;
        return View(new EditOrganizationViewModel
        {
            TenantId = details.Value.Id,
            Name = details.Value.Name,
            Timezone = details.Value.Timezone,
            Status = details.Value.Status,
            Version = details.Value.Version
        });
    }

    [HttpPost("organizacoes/{tenantId:guid}/editar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid tenantId, EditOrganizationViewModel model, CancellationToken cancellationToken)
    {
        if (tenantId != model.TenantId)
        {
            return BadRequest();
        }

        ViewData["OrganizationName"] = model.Name;
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var token = await AccessTokenAsync();
        if (token is null)
        {
            return Challenge();
        }

        var result = await api.UpdateOrganizationAsync(
            token,
            tenantId,
            new UpdateOrganizationRequest(model.Name.Trim(), model.Timezone.Trim(), model.Version),
            cancellationToken);

        if (result.Status == ApiCallStatus.Success)
        {
            TempData["Success"] = "Organização atualizada.";
            return RedirectToAction(nameof(Team), new { tenantId });
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
                result.UserMessage("A organização foi alterada em outra sessão. Recarregue os dados antes de salvar."));
            return View(model);
        }

        ModelState.AddModelError(string.Empty, UserFacingError(result, "Não foi possível salvar a organização."));
        return View(model);
    }

    [HttpGet("organizacoes/{tenantId:guid}/equipe")]
    public async Task<IActionResult> Team(
        Guid tenantId,
        string? tab,
        string? search,
        string? status,
        string? invitationStatus,
        int page = 1,
        int pageSize = 20,
        string? panel = null,
        Guid? editRoleId = null,
        Guid? editMemberId = null,
        CancellationToken cancellationToken = default)
    {
        var token = await AccessTokenAsync();
        if (token is null)
        {
            return Challenge();
        }

        var org = await RequireOrganizationAsync(token, tenantId, cancellationToken);
        if (org is null)
        {
            return Forbid();
        }

        var normalizedTab = NormalizeTab(tab);
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var rolesResult = await api.GetRolesAsync(token, tenantId, cancellationToken);
        if (rolesResult.Status == ApiCallStatus.Unauthorized)
        {
            return Challenge();
        }

        if (!rolesResult.Succeeded)
        {
            return MapReadFailure(rolesResult.Status);
        }

        var overview = await api.GetOrganizationOverviewAsync(token, tenantId, cancellationToken);
        var model = new TeamPageViewModel
        {
            Organization = org,
            Overview = overview.Succeeded ? overview.Value : null,
            Tab = normalizedTab,
            Search = search,
            Status = status,
            InvitationStatus = invitationStatus,
            Page = page,
            PageSize = pageSize,
            Roles = rolesResult.Value!,
            OpenPanel = panel,
            EditRoleId = editRoleId,
            EditMemberId = editMemberId,
            CanManageTeam = org.Permissions.Contains("tenant.team.manage", StringComparer.Ordinal),
            CanManageOrganization = org.Permissions.Contains("tenant.organization.manage", StringComparer.Ordinal),
            Invite = RestoreInvite(tenantId),
            RoleForm = RestoreRoleForm(editRoleId, rolesResult.Value!)
        };

        if (normalizedTab == "pessoas")
        {
            var members = await api.GetMembersAsync(token, tenantId, search, status, page, pageSize, cancellationToken);
            if (members.Status == ApiCallStatus.Unauthorized)
            {
                return Challenge();
            }

            if (!members.Succeeded)
            {
                return MapReadFailure(members.Status);
            }

            model.Members = members.Value;
            if (editMemberId is Guid memberId)
            {
                var detail = await api.GetMemberAsync(token, tenantId, memberId, cancellationToken);
                if (detail.Succeeded)
                {
                    model.MemberRoles = new MemberRolesViewModel
                    {
                        UserId = detail.Value!.UserId,
                        MemberName = detail.Value.Name,
                        RoleIds = detail.Value.RoleIds
                    };
                    model.OpenPanel ??= "member-roles";
                }
            }
        }
        else if (normalizedTab == "convites")
        {
            var invitations = await api.GetInvitationsAsync(token, tenantId, invitationStatus, page, pageSize, cancellationToken);
            if (invitations.Status == ApiCallStatus.Unauthorized)
            {
                return Challenge();
            }

            if (!invitations.Succeeded)
            {
                return MapReadFailure(invitations.Status);
            }

            model.Invitations = invitations.Value;
        }

        if (editRoleId is Guid roleId)
        {
            var role = rolesResult.Value!.FirstOrDefault(x => x.Id == roleId);
            if (role is not null)
            {
                model.RoleForm = new RoleFormViewModel
                {
                    RoleId = role.Id,
                    Name = role.Name,
                    Permissions = role.Permissions,
                    IsSystem = role.IsSystem
                };
                model.OpenPanel ??= "role";
            }
        }

        ViewData["OrganizationName"] = org.Name;
        ViewData["TenantId"] = org.Id;
        return View(model);
    }

    [HttpPost("organizacoes/{tenantId:guid}/convites")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Invite(Guid tenantId, InviteViewModel model, CancellationToken cancellationToken)
    {
        if (tenantId != model.TenantId)
        {
            return BadRequest();
        }

        PreserveInvite(model);
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Revise os dados do convite.";
            return RedirectToTeam(tenantId, "convites", panel: "invite");
        }

        var token = await AccessTokenAsync();
        if (token is null)
        {
            return Challenge();
        }

        var result = await api.InviteAsync(
            token,
            tenantId,
            new CreateInvitationRequest(model.Email.Trim(), model.RoleId, model.IdempotencyKey),
            cancellationToken);

        if (result.Succeeded)
        {
            TempData.Remove("InviteEmail");
            TempData.Remove("InviteRoleId");
            TempData.Remove("PreservedIdempotencyKey");
            TempData["Success"] = "Convite reservado e enfileirado. A fila não confirma entrega imediata.";
            return RedirectToTeam(tenantId, "convites");
        }

        PreserveInvite(model);
        TempData["Error"] = UserFacingError(result, DefaultInviteError(result.Status));
        return RedirectToTeam(tenantId, "convites", panel: "invite");
    }

    [HttpPost("organizacoes/{tenantId:guid}/convites/{invitationId:guid}/cancelar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelInvitation(Guid tenantId, Guid invitationId, CancellationToken cancellationToken)
    {
        var token = await AccessTokenAsync();
        if (token is null)
        {
            return Challenge();
        }

        var result = await api.CancelInvitationAsync(token, tenantId, invitationId, cancellationToken);
        TempData[result.Status == ApiCallStatus.Success ? "Success" : "Error"] =
            result.Status == ApiCallStatus.Success
                ? "Convite cancelado."
                : UserFacingError(result, "Não foi possível cancelar o convite.");
        return RedirectToTeam(tenantId, "convites");
    }

    [HttpPost("organizacoes/{tenantId:guid}/convites/{invitationId:guid}/reenviar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResendInvitation(Guid tenantId, Guid invitationId, CancellationToken cancellationToken)
    {
        var token = await AccessTokenAsync();
        if (token is null)
        {
            return Challenge();
        }

        var result = await api.ResendInvitationAsync(token, tenantId, invitationId, cancellationToken);
        TempData[result.Status == ApiCallStatus.Success ? "Success" : "Error"] =
            result.Status == ApiCallStatus.Success
                ? "Reenvio enfileirado. A fila não confirma entrega imediata."
                : UserFacingError(result, "Não foi possível reenviar o convite.");
        return RedirectToTeam(tenantId, "convites");
    }

    [HttpPost("organizacoes/{tenantId:guid}/perfis")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateRole(Guid tenantId, RoleFormViewModel model, CancellationToken cancellationToken)
    {
        PreserveRoleForm(model);
        if (string.IsNullOrWhiteSpace(model.Name) || model.Permissions is null || model.Permissions.Length == 0)
        {
            TempData["Error"] = "Informe o nome e ao menos uma permissão.";
            return RedirectToTeam(tenantId, "perfis", panel: "role");
        }

        var token = await AccessTokenAsync();
        if (token is null)
        {
            return Challenge();
        }

        var result = await api.CreateRoleAsync(
            token,
            tenantId,
            new CreateTenantRoleRequest(model.Name.Trim(), model.Permissions),
            cancellationToken);

        if (result.Succeeded)
        {
            ClearRoleFormTempData();
            TempData["Success"] = "Perfil criado.";
            return RedirectToTeam(tenantId, "perfis");
        }

        PreserveRoleForm(model);
        TempData["Error"] = UserFacingError(result, "Não foi possível criar o perfil. Verifique as permissões que você pode delegar.");
        return RedirectToTeam(tenantId, "perfis", panel: "role");
    }

    [HttpPost("organizacoes/{tenantId:guid}/perfis/{roleId:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateRole(
        Guid tenantId,
        Guid roleId,
        RoleFormViewModel model,
        CancellationToken cancellationToken)
    {
        model.RoleId = roleId;
        PreserveRoleForm(model);
        if (string.IsNullOrWhiteSpace(model.Name) || model.Permissions is null || model.Permissions.Length == 0)
        {
            TempData["Error"] = "Informe o nome e ao menos uma permissão.";
            return RedirectToTeam(tenantId, "perfis", panel: "role", editRoleId: roleId);
        }

        var token = await AccessTokenAsync();
        if (token is null)
        {
            return Challenge();
        }

        var result = await api.UpdateRolePermissionsAsync(
            token,
            tenantId,
            roleId,
            new UpdateTenantRolePermissionsRequest(model.Permissions),
            cancellationToken);

        if (result.Succeeded)
        {
            ClearRoleFormTempData();
            var affected = result.Value!.AffectedMemberCount;
            TempData["Success"] = affected > 0
                ? $"Permissões atualizadas. {affected} membro(s) afetado(s)."
                : "Permissões do perfil atualizadas.";
            return RedirectToTeam(tenantId, "perfis");
        }

        if (result.Status == ApiCallStatus.Conflict)
        {
            TempData["Error"] = result.UserMessage("O perfil foi alterado em outra sessão. Recarregue e revise.");
            return RedirectToTeam(tenantId, "perfis", panel: "role", editRoleId: roleId);
        }

        PreserveRoleForm(model);
        TempData["Error"] = UserFacingError(result, "Não foi possível atualizar o perfil.");
        return RedirectToTeam(tenantId, "perfis", panel: "role", editRoleId: roleId);
    }

    [HttpPost("organizacoes/{tenantId:guid}/membros/{userId:guid}/{actionName}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MemberAction(
        Guid tenantId,
        Guid userId,
        string actionName,
        MemberActionViewModel model,
        string? search,
        string? status,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        if (!MemberActions.Contains(actionName) || model.UserId != userId)
        {
            return BadRequest();
        }

        var token = await AccessTokenAsync();
        if (token is null)
        {
            return Challenge();
        }

        var reason = string.IsNullOrWhiteSpace(model.Reason)
            ? "Ação administrativa pela central da equipe."
            : model.Reason.Trim();

        var result = await api.MemberActionAsync(
            token,
            tenantId,
            userId,
            actionName.ToLowerInvariant(),
            new MemberStatusChangeRequest(reason),
            cancellationToken);

        TempData[result.Status == ApiCallStatus.Success ? "Success" : "Error"] =
            result.Status == ApiCallStatus.Success
                ? MemberActionSuccess(actionName)
                : UserFacingError(result, "Não foi possível concluir a ação no membro.");
        return RedirectToTeam(tenantId, "pessoas", search, status, page: page);
    }

    [HttpPost("organizacoes/{tenantId:guid}/membros/{userId:guid}/perfis")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateMemberRoles(
        Guid tenantId,
        Guid userId,
        MemberRolesViewModel model,
        string? search,
        string? status,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        if (model.UserId != userId)
        {
            return BadRequest();
        }

        var token = await AccessTokenAsync();
        if (token is null)
        {
            return Challenge();
        }

        var result = await api.UpdateMemberRolesAsync(
            token,
            tenantId,
            userId,
            new UpdateMemberRolesRequest(model.RoleIds ?? []),
            cancellationToken);

        TempData[result.Status == ApiCallStatus.Success ? "Success" : "Error"] =
            result.Status == ApiCallStatus.Success
                ? "Perfis do membro atualizados."
                : UserFacingError(result, "Não foi possível atualizar os perfis do membro.");
        return RedirectToTeam(tenantId, "pessoas", search, status, page: page);
    }

    [AllowAnonymous]
    [HttpGet("convites/aceitar")]
    public async Task<IActionResult> Accept(Guid invitationId, string? token, CancellationToken cancellationToken)
    {
        if (invitationId == Guid.Empty || string.IsNullOrWhiteSpace(token))
        {
            return View(new AcceptInvitationViewModel
            {
                InvitationId = invitationId,
                Token = token ?? string.Empty,
                IsAuthenticated = User.Identity?.IsAuthenticated == true
            });
        }

        InvitationPreviewResponse? preview = null;
        var previewResult = await api.GetInvitationPreviewAsync(invitationId, token, cancellationToken);
        if (previewResult.Succeeded)
        {
            preview = previewResult.Value;
        }

        var returnUrl = Url.Action(nameof(Accept), "Organizations", new { invitationId, token });
        return View(new AcceptInvitationViewModel
        {
            InvitationId = invitationId,
            Token = token,
            Preview = preview,
            IsAuthenticated = User.Identity?.IsAuthenticated == true,
            ReturnUrl = returnUrl
        });
    }

    [Authorize]
    [HttpPost("convites/aceitar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Accept(AcceptInvitationViewModel model, CancellationToken cancellationToken)
    {
        var access = await AccessTokenAsync();
        if (access is null)
        {
            return Challenge();
        }

        if (!ModelState.IsValid || string.IsNullOrWhiteSpace(model.Token))
        {
            model.IsAuthenticated = true;
            return View(model);
        }

        var result = await api.AcceptInvitationAsync(
            access,
            new AcceptInvitationRequest(model.InvitationId, model.Token),
            cancellationToken);

        if (result.Status != ApiCallStatus.Success)
        {
            model.IsAuthenticated = true;
            ModelState.AddModelError(
                string.Empty,
                UserFacingError(result, "Convite inválido, expirado, usado ou destinado a outro e-mail verificado."));
            var previewResult = await api.GetInvitationPreviewAsync(model.InvitationId, model.Token, cancellationToken);
            model.Preview = previewResult.Succeeded ? previewResult.Value : model.Preview;
            return View(model);
        }

        await HttpContext.SignOutAsync();
        TempData["Success"] = "Convite aceito. Entre novamente para carregar as permissões atualizadas.";
        return RedirectToAction("Login", "Account");
    }

    private async Task<string?> AccessTokenAsync() => await HttpContext.GetTokenAsync("access_token");

    private async Task<OrganizationSummary?> RequireOrganizationAsync(
        string token,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var orgs = await api.GetOrganizationsAsync(token, cancellationToken);
        if (!orgs.Succeeded)
        {
            return null;
        }

        return orgs.Value!.SingleOrDefault(x => x.Id == tenantId);
    }

    private IActionResult MapReadFailure(ApiCallStatus status) => status switch
    {
        ApiCallStatus.Forbidden => Forbid(),
        ApiCallStatus.Unauthorized => Challenge(),
        _ => View("~/Views/Shared/ServiceUnavailable.cshtml")
    };

    private static string NormalizeTab(string? tab) => tab?.Trim().ToLowerInvariant() switch
    {
        "convites" => "convites",
        "perfis" => "perfis",
        _ => "pessoas"
    };

    private RedirectToActionResult RedirectToTeam(
        Guid tenantId,
        string tab,
        string? search = null,
        string? status = null,
        string? invitationStatus = null,
        int page = 1,
        string? panel = null,
        Guid? editRoleId = null)
        => RedirectToAction(nameof(Team), new
        {
            tenantId,
            tab,
            search,
            status,
            invitationStatus,
            page,
            panel,
            editRoleId
        });

    private InviteViewModel RestoreInvite(Guid tenantId)
    {
        var model = new InviteViewModel
        {
            TenantId = tenantId,
            IdempotencyKey = TempData["PreservedIdempotencyKey"] as string ?? Guid.NewGuid().ToString("N")
        };

        if (TempData["InviteEmail"] is string email)
        {
            model.Email = email;
        }

        if (TempData["InviteRoleId"] is string roleText && Guid.TryParse(roleText, out var roleId))
        {
            model.RoleId = roleId;
        }

        TempData.Keep("PreservedIdempotencyKey");
        TempData.Keep("InviteEmail");
        TempData.Keep("InviteRoleId");
        return model;
    }

    private void PreserveInvite(InviteViewModel model)
    {
        TempData["PreservedIdempotencyKey"] = model.IdempotencyKey;
        TempData["InviteEmail"] = model.Email;
        TempData["InviteRoleId"] = model.RoleId.ToString();
    }

    private RoleFormViewModel RestoreRoleForm(Guid? editRoleId, TenantRoleResponse[] roles)
    {
        var preservedPermissions = (TempData["RolePermissions"] as string)?
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            ?? [];

        if (editRoleId is Guid id)
        {
            var existing = roles.FirstOrDefault(x => x.Id == id);
            if (existing is not null)
            {
                return new RoleFormViewModel
                {
                    RoleId = existing.Id,
                    Name = TempData["RoleName"] as string ?? existing.Name,
                    Permissions = preservedPermissions.Length > 0 ? preservedPermissions : existing.Permissions,
                    IsSystem = existing.IsSystem
                };
            }
        }

        return new RoleFormViewModel
        {
            Name = TempData["RoleName"] as string ?? string.Empty,
            Permissions = preservedPermissions
        };
    }

    private void PreserveRoleForm(RoleFormViewModel model)
    {
        TempData["RoleName"] = model.Name;
        TempData["RolePermissions"] = string.Join(',', model.Permissions ?? []);
    }

    private void ClearRoleFormTempData()
    {
        TempData.Remove("RoleName");
        TempData.Remove("RolePermissions");
    }

    private static string DefaultInviteError(ApiCallStatus status) => status switch
    {
        ApiCallStatus.Conflict => "Conflito de convite ou limite de assentos excedido.",
        ApiCallStatus.Forbidden => "Você não tem permissão para convidar nesta organização.",
        ApiCallStatus.Unauthorized => "Sua sessão expirou. Entre novamente.",
        _ => "Não foi possível convidar. Verifique assentos, perfil e permissões."
    };

    private static string MemberActionSuccess(string action) => action.ToLowerInvariant() switch
    {
        "block" => "Membro bloqueado. O acesso à organização fica suspenso imediatamente.",
        "unblock" => "Bloqueio removido. O membro pode voltar a acessar conforme seus perfis.",
        "inactivate" => "Membro inativado. O assento deixa de contar como ativo.",
        "restore" => "Membro restaurado como ativo.",
        _ => "Ação concluída."
    };

    private static string UserFacingError<T>(ApiCallResult<T> result, string fallback) => result.Status switch
    {
        ApiCallStatus.Unauthorized => "Sua sessão expirou. Entre novamente.",
        ApiCallStatus.Forbidden => result.UserMessage("Você não tem permissão para esta ação."),
        ApiCallStatus.Conflict => result.UserMessage("A operação conflitou com outro estado. Recarregue e tente novamente."),
        ApiCallStatus.RateLimited => "Muitas tentativas. Aguarde e tente novamente.",
        ApiCallStatus.Timeout or ApiCallStatus.Unavailable => "O serviço está temporariamente indisponível.",
        ApiCallStatus.InvalidRequest => result.UserMessage(fallback),
        _ => result.UserMessage(fallback)
    };
}
