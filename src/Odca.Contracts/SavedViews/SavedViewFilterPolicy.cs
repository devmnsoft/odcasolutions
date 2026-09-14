using System.Globalization;

namespace Odca.Contracts.SavedViews;

/// <summary>Canonical validation policy for persisted, user-controlled query definitions.</summary>
public static class SavedViewFilterPolicy
{
    private static readonly Dictionary<string, HashSet<string>> Filters = new(StringComparer.Ordinal)
    {
        ["obligations"] = ["scope", "contractId", "ownerId", "category", "status", "from", "to", "search"],
        ["reviews"] = ["scope", "contractId", "reviewerId", "requesterId", "status", "from", "to", "search"],
        ["contracts"] = ["ownerId", "status", "renewalFrom", "renewalTo", "search"]
    };

    private static readonly Dictionary<string, HashSet<string>> Sorts = new(StringComparer.Ordinal)
    {
        ["obligations"] = ["due_date", "-due_date", "title", "-title"],
        ["reviews"] = ["due_date", "-due_date", "updated_at", "-updated_at"],
        ["contracts"] = ["title", "-title", "end_date", "-end_date"]
    };

    private static readonly HashSet<string> ReservedRouteKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "action", "area", "controller", "tenantId", "viewId", "returnUrl", "page"
    };

    private static readonly HashSet<string> ObligationCategories =
        ["delivery", "document", "renewal", "communication", "financial", "other"];
    private static readonly HashSet<string> ObligationStatuses = ["open", "in_progress", "fulfilled", "cancelled"];

    public static bool IsListingType(string listingType) => Filters.ContainsKey(listingType);

    public static bool IsAllowedRouteKey(string listingType, string key) =>
        !ReservedRouteKeys.Contains(key) && Filters.TryGetValue(listingType, out var allowed) && allowed.Contains(key);

    public static bool TryNormalize(
        string listingType,
        IReadOnlyDictionary<string, string> input,
        string? sort,
        out Dictionary<string, string> normalized,
        out string normalizedSort,
        out string? error)
    {
        normalized = new(StringComparer.Ordinal);
        normalizedSort = sort ?? DefaultSort(listingType);
        error = null;
        if (!Filters.TryGetValue(listingType, out var allowed) ||
            !Sorts.TryGetValue(listingType, out var sorts))
        {
            error = "Tipo de listagem inválido.";
            return false;
        }
        if (!sorts.Contains(normalizedSort))
        {
            error = "Ordenação não permitida.";
            return false;
        }

        foreach (var (key, rawValue) in input)
        {
            if (!allowed.Contains(key) || ReservedRouteKeys.Contains(key) || rawValue.Length > 200)
            {
                error = "A vista contém filtros não permitidos.";
                return false;
            }
            var value = rawValue.Trim();
            if (key.EndsWith("Id", StringComparison.Ordinal) && !Guid.TryParse(value, out _))
            {
                error = "A vista contém um identificador inválido.";
                return false;
            }
            if (key is "from" or "to" or "renewalFrom" or "renewalTo" &&
                !DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            {
                error = "As datas devem usar o formato AAAA-MM-DD.";
                return false;
            }
            if ((listingType == "obligations" && key == "scope" && value is not ("mine" or "organization")) ||
                (listingType == "obligations" && key == "category" && !ObligationCategories.Contains(value)) ||
                (listingType == "obligations" && key == "status" && !ObligationStatuses.Contains(value)))
            {
                error = "A vista contém um valor não permitido.";
                return false;
            }
            normalized[key] = value;
        }

        if ((TryDate(normalized, "from", out var from) && TryDate(normalized, "to", out var to) && from > to) ||
            (TryDate(normalized, "renewalFrom", out from) && TryDate(normalized, "renewalTo", out to) && from > to))
        {
            error = "A data final deve ser igual ou posterior à data inicial.";
            return false;
        }
        return true;
    }

    private static string DefaultSort(string listingType) => listingType == "contracts" ? "title" : "due_date";

    private static bool TryDate(Dictionary<string, string> filters, string key, out DateOnly value)
    {
        value = default;
        return filters.TryGetValue(key, out var text) && DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
    }
}
