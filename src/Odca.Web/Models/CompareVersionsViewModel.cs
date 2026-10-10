using Odca.Contracts.Studio;

namespace Odca.Web.Models;

public sealed record CompareVersionsViewModel(VersionComparisonResponse? Comparison, IReadOnlyList<StudioVersionItem> Versions, Guid? BeforeId = null, Guid? AfterId = null);
