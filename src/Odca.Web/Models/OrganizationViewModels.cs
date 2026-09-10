using System.ComponentModel.DataAnnotations;
using Odca.Contracts.Onboarding;
using Odca.Contracts.Tenancy;

namespace Odca.Web.Models;

public sealed class TeamPageViewModel
{
    public required OrganizationSummary Organization { get; set; }
    public OrganizationOverviewResponse? Overview { get; set; }
    public required string Tab { get; set; }
    public string? Search { get; set; }
    public string? Status { get; set; }
    public string? InvitationStatus { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public PaginatedResponse<TeamMemberResponse>? Members { get; set; }
    public PaginatedResponse<InvitationListItemResponse>? Invitations { get; set; }
    public TenantRoleResponse[] Roles { get; set; } = [];
    public InviteViewModel Invite { get; set; } = new();
    public RoleFormViewModel RoleForm { get; set; } = new();
    public string? OpenPanel { get; set; }
    public Guid? EditRoleId { get; set; }
    public Guid? EditMemberId { get; set; }
    public MemberRolesViewModel MemberRoles { get; set; } = new();
    public bool CanManageTeam { get; set; }
    public bool CanManageOrganization { get; set; }
}

public sealed class InviteViewModel
{
    [Required(ErrorMessage = "Informe o e-mail."), EmailAddress(ErrorMessage = "Informe um e-mail válido.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Selecione um perfil.")]
    public Guid RoleId { get; set; }

    [Required]
    public Guid TenantId { get; set; }

    [Required]
    public string IdempotencyKey { get; set; } = Guid.NewGuid().ToString("N");
}

public sealed class RoleFormViewModel
{
    public Guid? RoleId { get; set; }

    [Required(ErrorMessage = "Informe o nome do perfil."), StringLength(100)]
    public string Name { get; set; } = string.Empty;

    public string[] Permissions { get; set; } = [];

    public bool IsSystem { get; set; }

    public int AffectedMemberCount { get; set; }
}

public sealed class MemberRolesViewModel
{
    public Guid UserId { get; set; }
    public string MemberName { get; set; } = string.Empty;
    public Guid[] RoleIds { get; set; } = [];
}

public sealed class MemberActionViewModel
{
    [Required]
    public Guid UserId { get; set; }

    [StringLength(500)]
    public string? Reason { get; set; }
}

public sealed class EditOrganizationViewModel
{
    public Guid TenantId { get; set; }

    [Required(ErrorMessage = "Informe o nome."), StringLength(160, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe o fuso horário."), StringLength(80)]
    public string Timezone { get; set; } = "America/Sao_Paulo";

    public string Status { get; set; } = string.Empty;

    [Required]
    public long Version { get; set; }

    public bool VersionConflict { get; set; }
}

public sealed class AcceptInvitationViewModel
{
    public Guid InvitationId { get; set; }

    [Required]
    public string Token { get; set; } = string.Empty;

    public InvitationPreviewResponse? Preview { get; set; }

    public bool IsAuthenticated { get; set; }

    public string? ReturnUrl { get; set; }
}

public sealed class CustomerHomePageViewModel
{
    public required CustomerHomeResponse Home { get; init; }
    public OrganizationOverviewResponse? Overview { get; init; }
}
