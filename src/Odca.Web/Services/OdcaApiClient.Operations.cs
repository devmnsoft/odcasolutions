using System.Globalization;
using Odca.Contracts.Operations;

namespace Odca.Web.Services;

// Cole estes métodos em OdcaApiClient (classe sealed existente).
// using Odca.Contracts.Operations;

public static class OdcaApiClientOperations
{
    public static string InboxPath(
        Guid tenantId,
        string scope,
        string? kind,
        string? urgency,
        Guid? contractId,
        Guid? ownerId,
        int page,
        int pageSize)
    {
        var query = new Dictionary<string, string?>
        {
            ["scope"] = scope,
            ["kind"] = kind,
            ["urgency"] = urgency,
            ["contractId"] = contractId?.ToString(),
            ["ownerId"] = ownerId?.ToString(),
            ["page"] = page.ToString(CultureInfo.InvariantCulture),
            ["pageSize"] = pageSize.ToString(CultureInfo.InvariantCulture)
        };
        var encoded = string.Join('&', query
            .Where(item => !string.IsNullOrWhiteSpace(item.Value))
            .Select(item => $"{Uri.EscapeDataString(item.Key)}={Uri.EscapeDataString(item.Value!)}"));
        return $"api/v1/organizations/{tenantId}/inbox?{encoded}";
    }

    public static string AgendaPath(Guid tenantId, int year, int month, string scope, Guid? ownerId)
    {
        var query = new Dictionary<string, string?>
        {
            ["year"] = year.ToString(CultureInfo.InvariantCulture),
            ["month"] = month.ToString(CultureInfo.InvariantCulture),
            ["scope"] = scope,
            ["ownerId"] = ownerId?.ToString()
        };
        var encoded = string.Join('&', query
            .Where(item => !string.IsNullOrWhiteSpace(item.Value))
            .Select(item => $"{Uri.EscapeDataString(item.Key)}={Uri.EscapeDataString(item.Value!)}"));
        return $"api/v1/organizations/{tenantId}/agenda?{encoded}";
    }

    public static string SheetPath(Guid tenantId, Guid contractId) =>
        $"api/v1/organizations/{tenantId}/contracts/{contractId}/sheet";
}
