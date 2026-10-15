using System.Net.Http.Json;
using System.Text.Json;

namespace Odca.Web.Services;

/// <summary>B.1 (D-OC31): central de notificações do superadministrador (outbox da
/// plataforma): listagem com filtros, detalhe seguro e retry de falhas definitivas.</summary>
public sealed partial class OdcaApiClient
{
    public Task<ApiCallResult<JsonElement>> GetPlatformNotificationsAsync(string token, string? status, string? kind, string? search, int page, int pageSize, CancellationToken ct)
    {
        var query = new Dictionary<string, string?> { ["status"] = status, ["kind"] = kind, ["search"] = search };
        var qs = string.Join('&', query.Where(item => !string.IsNullOrWhiteSpace(item.Value))
            .Select(item => $"{Uri.EscapeDataString(item.Key)}={Uri.EscapeDataString(item.Value!)}"));
        var url = $"api/v1/platform/notifications?page={page}&pageSize={pageSize}" + (string.IsNullOrEmpty(qs) ? string.Empty : $"&{qs}");
        return SendAsync<JsonElement>(CreateAuthorized(HttpMethod.Get, url, token), false, ct);
    }

    public Task<ApiCallResult<JsonElement>> RetryPlatformNotificationAsync(string token, Guid messageId, CancellationToken ct) =>
        SendAsync<JsonElement>(CreateAuthorized(HttpMethod.Post, $"api/v1/platform/notifications/{messageId}/retry", token), false, ct);
}
