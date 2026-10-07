using Odca.Contracts.Contracts;
using Odca.Contracts.Operations;
using Odca.Contracts.Tenancy;
using Odca.Contracts.SavedViews;

namespace Odca.Web.Models;

public sealed class InboxWorkspaceViewModel
{
    public OperationalInboxPageDto Page { get; init; } = new([], 1, 20, 0, 0, 0, 0);
    public string Scope { get; init; } = "mine";
    public string? Kind { get; init; }
    public string? Urgency { get; init; }
    public string? Error { get; init; }
    public IReadOnlyList<SavedViewItem> Views { get; init; } = [];
    public SavedViewItem? CurrentView { get; init; }
}

public sealed class AgendaWorkspaceViewModel
{
    public MonthlyAgendaPageDto Page { get; init; } = new(1, 1, DateOnly.MinValue, DateOnly.MinValue, DateOnly.MinValue, [], 0);
    public string Scope { get; init; } = "mine";
    public string? Kind { get; init; }
    public int? Day { get; init; }
    public string? Error { get; init; }
}

public sealed class ContractListViewModel
{
    public Guid TenantId { get; init; }
    public IReadOnlyList<ContractListItem> Contracts { get; init; } = [];
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
    public int Total { get; init; }
    public string? Search { get; init; }
    public string Status { get; init; } = "active";
    public bool CanCreateDocument { get; init; }
    public bool CanManageContracts { get; init; }
    public bool CanReadHistory { get; init; }
    public string? Error { get; init; }
}

public sealed class ContractSheetViewModel
{
    public ContractSheetDto? Sheet { get; init; }
    public string? Error { get; init; }
    public IReadOnlyList<TeamMemberResponse> ActiveMembers { get; init; } = [];
    public Guid? SelectedObligationId { get; init; }
    public string? FromSource { get; init; }
    public int? Year { get; init; }
    public int? Month { get; init; }
    public string? Scope { get; init; }
    public string? Kind { get; init; }
    public string? Urgency { get; init; }
    public Guid? ViewId { get; init; }
    public IReadOnlyList<Odca.Contracts.Studio.StudioCommentItem> Comments { get; init; } = [];
    public IReadOnlyList<Odca.Contracts.Studio.StudioVersionItem> Versions { get; init; } = [];
    public IReadOnlyList<Odca.Contracts.Studio.StudioReviewerItem> Reviewers { get; init; } = [];
    // B.3.2c — jornada completa na ficha (assinatura, ações de ciclo de vida, histórico)
    public IReadOnlyList<Odca.Contracts.Contracts.ContractEventItem> Events { get; init; } = [];
    public int EventTotal { get; init; }
    public int EventPage { get; init; } = 1;
    public bool CanManageContracts { get; init; }
    public bool CanReadHistory { get; init; }
    public bool CanSignDocuments { get; init; }
    public bool CanReadDocuments { get; init; }
}
