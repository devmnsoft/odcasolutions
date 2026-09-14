using System.Text.Json;

namespace Odca.Contracts.SavedViews;

public sealed record SavedViewItem(
    Guid Id,
    string Name,
    string ListingType,
    IReadOnlyDictionary<string, string> Filters,
    string Sort,
    bool IsDefault,
    long Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record SaveViewRequest(
    string Name,
    string ListingType,
    IReadOnlyDictionary<string, string> Filters,
    string? Sort);

public sealed record UpdateSavedViewRequest(
    string Name,
    bool IsDefault,
    long Version,
    IReadOnlyDictionary<string, string>? Filters = null,
    string? Sort = null);
