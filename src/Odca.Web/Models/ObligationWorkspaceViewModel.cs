using Odca.Contracts.Obligations;
using Odca.Contracts.SavedViews;

namespace Odca.Web.Models;

public sealed record ObligationWorkspaceViewModel(
    ObligationPage Results,
    IReadOnlyList<SavedViewItem> SavedViews,
    SavedViewItem? CurrentView = null,
    bool IsCurrentViewModified = false);
