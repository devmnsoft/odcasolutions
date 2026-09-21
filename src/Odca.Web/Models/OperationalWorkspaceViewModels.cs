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
    public MonthlyAgendaPageDto Page { get; init; } = new(1, 1, DateOnly.MinValue, DateOnly.MinValue, [], 0);
    public string Scope { get; init; } = "mine";
    public int? Day { get; init; }
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
}
