using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odca.Api.Localization;
using Odca.Application.Tenancy;
using Odca.Contracts.Tenancy;

namespace Odca.Api.Controllers;

[ApiController]
[Route("api/v1/organizations")]
[Authorize(Policy = "PasswordChanged")]
public sealed class OrganizationsController(TenantAdministrationService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<OrganizationSummary>>> List(CancellationToken cancellationToken)
    {
        if (!Actor(out var actor))
        {
            return Unauthorized();
        }

        var rows = await service.ListOrganizationsAsync(actor, cancellationToken);
        return Ok(rows.Select(x => new OrganizationSummary(x.Id, x.Name, x.Status, x.Version, x.Permissions)).ToList());
    }

    [HttpGet("{tenantId:guid}")]
    public async Task<ActionResult<OrganizationDetails>> Get(Guid tenantId, CancellationToken cancellationToken)
    {
        if (!Actor(out var actor))
        {
            return Unauthorized();
        }

        var row = await service.GetOrganizationAsync(actor, tenantId, cancellationToken);
        return row is null
            ? Forbid()
            : Ok(new OrganizationDetails(row.Id, row.Name, row.Timezone, row.Status, row.Version, row.ActivityProfile));
    }

    [HttpGet("{tenantId:guid}/overview")]
    public async Task<ActionResult<OrganizationOverviewResponse>> Overview(Guid tenantId, CancellationToken cancellationToken)
    {
        if (!Actor(out var actor))
        {
            return Unauthorized();
        }

        var row = await service.GetOrganizationOverviewAsync(actor, tenantId, cancellationToken);
        if (row is null)
        {
            return Forbid();
        }

        return Ok(new OrganizationOverviewResponse(
            row.ActiveMembers,
            row.ValidInvitations,
            row.SeatLimit,
            row.AvailableSeats,
            row.HasContracts,
            row.Pendencies.Select(x => new OrganizationPendency(x.Message, x.Tab, x.StatusFilter)).ToList()));
    }

    [HttpPut("{tenantId:guid}")]
    public async Task<IActionResult> Update(
        Guid tenantId,
        [FromBody] UpdateOrganizationRequest request,
        CancellationToken cancellationToken)
    {
        if (!Actor(out var actor))
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.Name) ||
            request.Name.Trim().Length > 160 ||
            string.IsNullOrWhiteSpace(request.Timezone) ||
            request.Timezone.Length > 80)
        {
            return this.Invalid("api.org.update_invalid");
        }

        var timezone = request.Timezone.Trim();
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(timezone, out _))
        {
            return this.Field("timezone", "api.org.timezone_unsupported");
        }

        var activityProfile = request.ActivityProfile?.Trim();
        if (!string.IsNullOrWhiteSpace(activityProfile) &&
            activityProfile is not ("general" or "therapy_clinic" or "plastic_surgery"))
        {
            return this.Field("activityProfile", "api.org.profile_invalid");
        }

        var result = await service.UpdateOrganizationAsync(
            actor,
            tenantId,
            request.Name.Trim(),
            timezone,
            request.Version,
            activityProfile,
            cancellationToken);

        return result switch
        {
            UpdateOrganizationResult.Updated => NoContent(),
            // D-OC27: textos por chave; código wire existente preservado.
            UpdateOrganizationResult.Conflict => Conflict(new ProblemDetails
            {
                Title = this.T("api.org.changed_other_session"),
                Detail = this.T("api.org.changed_other_session.detail"),
                Extensions = { ["code"] = "organization.version.conflict" }
            }),
            UpdateOrganizationResult.ProfileNotAllowed => Conflict(new ProblemDetails
            {
                Title = this.T("api.organization.profile.plan_required"),
                Detail = this.T("api.organization.profile.plan_required.detail"),
                Extensions = { ["code"] = "organization.profile.plan_required" }
            }),
            UpdateOrganizationResult.NotFound => NotFound(),
            _ => Forbid()
        };
    }

    [HttpGet("{tenantId:guid}/members")]
    public async Task<IActionResult> Members(
        Guid tenantId,
        [FromQuery] string? search,
        [FromQuery] string? status,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
    {
        if (!Actor(out var actor))
        {
            return Unauthorized();
        }

        if (status is not null && status is not ("active" or "blocked" or "inactive" or "invited"))
        {
            return this.Field("status", "api.org.member_status_invalid");
        }

        var result = await service.ListMembersAsync(actor, tenantId, search, status, page, pageSize, cancellationToken);
        if (result.Status == QueryAccessStatus.Forbidden)
        {
            return Forbid();
        }

        var pageResult = result.Value!;
        return Ok(new PaginatedResponse<TeamMemberResponse>(
            pageResult.Items.Select(x => new TeamMemberResponse(x.UserId, x.Name, x.Email, x.Status, x.Roles)).ToList(),
            pageResult.TotalCount,
            pageResult.Page,
            pageResult.PageSize));
    }

    [HttpGet("{tenantId:guid}/members/{userId:guid}")]
    public async Task<ActionResult<TeamMemberDetailResponse>> Member(
        Guid tenantId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (!Actor(out var actor))
        {
            return Unauthorized();
        }

        var result = await service.GetMemberAsync(actor, tenantId, userId, cancellationToken);
        if (result.Status == QueryAccessStatus.Forbidden)
        {
            return Forbid();
        }

        if (result.Value is null)
        {
            return NotFound();
        }

        var member = result.Value;
        return Ok(new TeamMemberDetailResponse(
            member.UserId,
            member.Name,
            member.Email,
            member.Status,
            member.RoleIds,
            member.Roles,
            member.SecurityVersion));
    }

    [HttpPost("{tenantId:guid}/members/{userId:guid}/block")]
    [Authorize(Policy = "RecentMfa")]
    public Task<IActionResult> BlockMember(Guid tenantId, Guid userId, [FromBody] MemberStatusChangeRequest? request, CancellationToken cancellationToken)
        => ChangeMemberStatus(tenantId, userId, "blocked", request?.Reason, cancellationToken);

    [HttpPost("{tenantId:guid}/members/{userId:guid}/unblock")]
    [Authorize(Policy = "RecentMfa")]
    public Task<IActionResult> UnblockMember(Guid tenantId, Guid userId, [FromBody] MemberStatusChangeRequest? request, CancellationToken cancellationToken)
        => ChangeMemberStatus(tenantId, userId, "active", request?.Reason, cancellationToken);

    [HttpPost("{tenantId:guid}/members/{userId:guid}/inactivate")]
    [Authorize(Policy = "RecentMfa")]
    public Task<IActionResult> InactivateMember(Guid tenantId, Guid userId, [FromBody] MemberStatusChangeRequest? request, CancellationToken cancellationToken)
        => ChangeMemberStatus(tenantId, userId, "inactive", request?.Reason, cancellationToken);

    [HttpPost("{tenantId:guid}/members/{userId:guid}/restore")]
    [Authorize(Policy = "RecentMfa")]
    public Task<IActionResult> RestoreMember(Guid tenantId, Guid userId, [FromBody] MemberStatusChangeRequest? request, CancellationToken cancellationToken)
        => ChangeMemberStatus(tenantId, userId, "active", request?.Reason, cancellationToken);

    [HttpPut("{tenantId:guid}/members/{userId:guid}/roles")]
    [Authorize(Policy = "RecentMfa")]
    public async Task<IActionResult> UpdateMemberRoles(
        Guid tenantId,
        Guid userId,
        [FromBody] UpdateMemberRolesRequest request,
        CancellationToken cancellationToken)
    {
        if (!Actor(out var actor))
        {
            return Unauthorized();
        }

        if (request.RoleIds is null || request.RoleIds.Length > 64)
        {
            return this.Invalid("api.org.roles_invalid");
        }

        var result = await service.UpdateMemberRolesAsync(actor, tenantId, userId, request.RoleIds, cancellationToken);
        return MapMemberAction(result);
    }

    [HttpGet("{tenantId:guid}/roles")]
    public async Task<ActionResult<IReadOnlyList<TenantRoleResponse>>> Roles(Guid tenantId, CancellationToken cancellationToken)
    {
        if (!Actor(out var actor))
        {
            return Unauthorized();
        }

        var result = await service.ListRolesAsync(actor, tenantId, cancellationToken);
        if (result.Status == QueryAccessStatus.Forbidden)
        {
            return Forbid();
        }

        return Ok(result.Value!.Select(x => new TenantRoleResponse(x.Id, x.Name, x.IsSystem, x.Permissions)).ToList());
    }

    [HttpPost("{tenantId:guid}/roles")]
    [Authorize(Policy = "RecentMfa")]
    public async Task<IActionResult> CreateRole(
        Guid tenantId,
        CreateTenantRoleRequest request,
        CancellationToken cancellationToken)
    {
        if (!Actor(out var actor))
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.Name) ||
            request.Name.Length > 100 ||
            request.Permissions.Length is 0 or > 64)
        {
            return this.Invalid("api.org.role_fields_invalid");
        }

        var role = await service.CreateRoleAsync(actor, tenantId, request.Name.Trim(), request.Permissions, cancellationToken);
        return role is null
            ? Forbid()
            : CreatedAtAction(nameof(Roles), new { tenantId }, new TenantRoleResponse(role.Id, role.Name, role.IsSystem, role.Permissions));
    }

    [HttpPut("{tenantId:guid}/roles/{roleId:guid}")]
    [Authorize(Policy = "RecentMfa")]
    public async Task<IActionResult> UpdateRole(
        Guid tenantId,
        Guid roleId,
        [FromBody] UpdateTenantRoleRequest request,
        CancellationToken cancellationToken)
    {
        if (!Actor(out var actor))
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.Name) ||
            request.Name.Length > 100 ||
            request.Permissions is null ||
            request.Permissions.Length is 0 or > 64)
        {
            return this.Invalid("api.org.role_fields_invalid");
        }

        var result = await service.UpdateRoleAsync(actor, tenantId, roleId, request.Name.Trim(), request.Permissions, cancellationToken);
        return result.Status switch
        {
            UpdateRolePermissionsStatus.Updated => Ok(new TenantRoleResponse(
                result.Role!.Id,
                result.Role.Name,
                result.Role.IsSystem,
                result.Role.Permissions)),
            UpdateRolePermissionsStatus.NotFound => NotFound(),
            UpdateRolePermissionsStatus.InvalidPermissions => this.Field("permissions", "api.org.permissions_not_assignable"),
            UpdateRolePermissionsStatus.Protected => Conflict(new ProblemDetails
            {
                Title = this.T("api.protected_role"),
                Detail = this.T("api.protected_role.detail"),
                Extensions = { ["code"] = "protected_role" }
            }),
            _ => Forbid()
        };
    }

    [HttpPut("{tenantId:guid}/roles/{roleId:guid}/permissions")]
    [Authorize(Policy = "RecentMfa")]
    public async Task<IActionResult> UpdateRolePermissions(
        Guid tenantId,
        Guid roleId,
        [FromBody] UpdateTenantRolePermissionsRequest request,
        CancellationToken cancellationToken)
    {
        if (!Actor(out var actor))
        {
            return Unauthorized();
        }

        if (request.Permissions is null || request.Permissions.Length is 0 or > 64)
        {
            return this.Invalid("api.org.permissions_list_invalid");
        }

        var result = await service.UpdateRolePermissionsAsync(actor, tenantId, roleId, request.Permissions, cancellationToken);
        return result.Status switch
        {
            UpdateRolePermissionsStatus.Updated => Ok(new UpdateTenantRolePermissionsResponse(
                result.Role!.Id,
                result.Role.Permissions,
                result.AffectedMemberCount)),
            UpdateRolePermissionsStatus.NotFound => NotFound(),
            UpdateRolePermissionsStatus.InvalidPermissions => this.Field("permissions", "api.org.permissions_not_assignable"),
            UpdateRolePermissionsStatus.Protected => Conflict(new ProblemDetails
            {
                Title = this.T("api.protected_role"),
                Detail = this.T("api.protected_role.detail"),
                Extensions = { ["code"] = "protected_role" }
            }),
            _ => Forbid()
        };
    }

    [HttpPost("{tenantId:guid}/administration/transfer")]
    [Authorize(Policy = "RecentMfa")]
    public async Task<IActionResult> TransferAdministration(
        Guid tenantId,
        [FromBody] TransferAdministrationRequest request,
        CancellationToken cancellationToken)
    {
        if (!Actor(out var actor))
        {
            return Unauthorized();
        }

        if (request.TargetUserId == Guid.Empty || string.IsNullOrWhiteSpace(request.Justification))
        {
            return this.Invalid("api.org.transfer_invalid");
        }

        var result = await service.TransferPrincipalAdministrationAsync(
            actor, tenantId, request.TargetUserId, request.Justification.Trim(), cancellationToken);
        return result switch
        {
            "transferred" => NoContent(),
            "not_found" => NotFound(),
            "forbidden" => Forbid(),
            "invalid" => this.Field("justification", "api.org.transfer_justification_invalid"),
            // Código wire dinâmico (result) preservado; textos por chave estável.
            _ => Conflict(new ProblemDetails
            {
                Title = this.T("api.org.transfer_failed"),
                Detail = this.T("api.org.transfer_failed.detail"),
                Extensions = { ["code"] = result }
            })
        };
    }

    [HttpGet("{tenantId:guid}/invitations")]
    public async Task<IActionResult> Invitations(
        Guid tenantId,
        [FromQuery] string? status,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
    {
        if (!Actor(out var actor))
        {
            return Unauthorized();
        }

        if (status is not null &&
            status is not ("pending" or "sent" or "failed" or "accepted" or "cancelled" or "expired"))
        {
            return this.Field("status", "api.org.invitation_status_invalid");
        }

        var result = await service.ListInvitationsAsync(actor, tenantId, status, page, pageSize, cancellationToken);
        if (result.Status == QueryAccessStatus.Forbidden)
        {
            return Forbid();
        }

        var pageResult = result.Value!;
        return Ok(new PaginatedResponse<InvitationListItemResponse>(
            pageResult.Items.Select(x => new InvitationListItemResponse(
                x.Id,
                x.Recipient,
                x.RoleName,
                x.RoleId,
                x.CreatedAt,
                x.ExpiresAt,
                x.Status,
                x.DeliveryStatus,
                x.DeliveryErrorCode)).ToList(),
            pageResult.TotalCount,
            pageResult.Page,
            pageResult.PageSize));
    }

    [HttpPost("{tenantId:guid}/invitations")]
    public async Task<IActionResult> Invite(
        Guid tenantId,
        CreateInvitationRequest request,
        CancellationToken cancellationToken)
    {
        if (!Actor(out var actor))
        {
            return Unauthorized();
        }

        if (!System.Net.Mail.MailAddress.TryCreate(request.Email, out _) ||
            string.IsNullOrWhiteSpace(request.IdempotencyKey) ||
            request.IdempotencyKey.Length > 100)
        {
            return this.Invalid("api.org.invite_invalid");
        }

        var result = await service.CreateInvitationAsync(
            actor,
            tenantId,
            request.Email.Trim(),
            request.RoleId,
            request.IdempotencyKey,
            cancellationToken);

        return result.Status switch
        {
            CreateInvitationResultStatus.Created or CreateInvitationResultStatus.Existing =>
                Ok(new InvitationResponse(
                    result.Invitation!.Id,
                    result.Invitation.Recipient,
                    result.Invitation.State,
                    result.Invitation.ExpiresAt)),
            CreateInvitationResultStatus.QuotaExceeded => Conflict(new ProblemDetails
            {
                Title = this.T("api.org.seats_limit_exceeded"),
                Detail = this.T("api.org.seats_limit_exceeded.detail"),
                Extensions = { ["code"] = "organization.seats_exceeded" }
            }),
            CreateInvitationResultStatus.InvalidRole => this.Field("roleId", "api.org.role_not_assignable"),
            CreateInvitationResultStatus.Conflict => Conflict(new ProblemDetails
            {
                Title = this.T("api.org.idempotency_conflict"),
                Detail = this.T("api.org.idempotency_conflict.detail"),
                Extensions = { ["code"] = "organization.idempotency_conflict" }
            }),
            _ => Forbid()
        };
    }

    [HttpPost("{tenantId:guid}/invitations/{invitationId:guid}/cancel")]
    public async Task<IActionResult> CancelInvitation(Guid tenantId, Guid invitationId, CancellationToken cancellationToken)
    {
        if (!Actor(out var actor))
        {
            return Unauthorized();
        }

        return MapInvitationMutation(await service.CancelInvitationAsync(actor, tenantId, invitationId, cancellationToken));
    }

    [HttpPost("{tenantId:guid}/invitations/{invitationId:guid}/resend")]
    public async Task<IActionResult> ResendInvitation(Guid tenantId, Guid invitationId, CancellationToken cancellationToken)
    {
        if (!Actor(out var actor))
        {
            return Unauthorized();
        }

        return MapInvitationMutation(await service.ResendInvitationAsync(actor, tenantId, invitationId, cancellationToken));
    }

    [AllowAnonymous]
    [HttpGet("invitations/preview")]
    public async Task<ActionResult<InvitationPreviewResponse>> PreviewInvitation(
        [FromQuery] Guid invitationId,
        [FromQuery] string token,
        CancellationToken cancellationToken)
    {
        if (invitationId == Guid.Empty || string.IsNullOrWhiteSpace(token) || token.Length is < 32 or > 256)
        {
            return NotFound();
        }

        var hash = HashToken(token);
        var preview = await service.GetInvitationPreviewAsync(invitationId, hash, cancellationToken);
        if (preview is null)
        {
            return NotFound();
        }

        return Ok(new InvitationPreviewResponse(
            preview.InvitationId,
            preview.OrganizationName,
            preview.RoleName,
            preview.RecipientEmail,
            preview.ExpiresAt,
            preview.Status));
    }

    [HttpPost("invitations/accept")]
    public async Task<IActionResult> Accept(AcceptInvitationRequest request, CancellationToken cancellationToken)
    {
        if (!Actor(out var actor))
        {
            return Unauthorized();
        }

        if (request.Token.Length is < 32 or > 256)
        {
            return this.Field("token", "api.org.invitation_token_invalid");
        }

        var accepted = await service.AcceptInvitationAsync(actor, request.InvitationId, HashToken(request.Token), cancellationToken);
        if (accepted)
        {
            return Ok(new { accepted = true });
        }

        return Conflict(new ProblemDetails
        {
            Title = this.T("api.invitation_not_acceptable"),
            Detail = this.T("api.invitation_not_acceptable.detail"),
            Extensions = { ["code"] = "invitation_not_acceptable" }
        });
    }

    private async Task<IActionResult> ChangeMemberStatus(
        Guid tenantId,
        Guid userId,
        string targetStatus,
        string? reason,
        CancellationToken cancellationToken)
    {
        if (!Actor(out var actor))
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length is < 3 or > 500)
        {
            return this.Field("reason", "api.org.reason_required");
        }

        var result = await service.ChangeMemberStatusAsync(actor, tenantId, userId, targetStatus, reason.Trim(), cancellationToken);
        return MapMemberAction(result);
    }

    private IActionResult MapMemberAction(MemberActionResult result)
        => result switch
        {
            MemberActionResult.Succeeded => NoContent(),
            MemberActionResult.NotFound => NotFound(),
            MemberActionResult.Conflict => Conflict(new ProblemDetails
            {
                Title = this.T("api.org.member_update_conflict"),
                Detail = this.T("api.org.member_update_conflict.detail"),
                Extensions = { ["code"] = "member_update_conflict" }
            }),
            MemberActionResult.LastAdminProtected => Conflict(new ProblemDetails
            {
                Title = this.T("api.last_admin_protected"),
                Detail = this.T("api.last_admin_protected.detail"),
                Extensions = { ["code"] = "last_admin_protected" }
            }),
            MemberActionResult.ElevationDenied => Conflict(new ProblemDetails
            {
                Title = this.T("api.elevation_denied"),
                Detail = this.T("api.elevation_denied.detail"),
                Extensions = { ["code"] = "elevation_denied" }
            }),
            _ => Forbid()
        };

    private IActionResult MapInvitationMutation(InvitationMutationResult result)
        => result switch
        {
            InvitationMutationResult.Succeeded => NoContent(),
            InvitationMutationResult.NotFound => NotFound(),
            InvitationMutationResult.Conflict => Conflict(new ProblemDetails
            {
                Title = this.T("api.org.invitation_cannot_modify"),
                Detail = this.T("api.org.invitation_cannot_modify.detail")
            }),
            _ => Forbid()
        };

    private static string HashToken(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    private bool Actor(out Guid actor) => Guid.TryParse(User.FindFirstValue("sub"), out actor);
}
