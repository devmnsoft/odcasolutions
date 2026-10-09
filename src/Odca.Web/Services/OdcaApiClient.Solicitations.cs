using System.Net.Http.Json;
using System.Text.Json;
using Odca.Contracts.Solicitations;

namespace Odca.Web.Services;

/// <summary>Seção C: atende o portal da organização (Central de solicitações) e a fila da
/// plataforma ODCA (solicitações + aprovação de modelos cirúrgicos).</summary>
public sealed partial class OdcaApiClient
{
    private static string SolicitationQuery(string? service, string? status, string? search, Guid? tenantId = null)
    {
        var query = new Dictionary<string, string?>
        {
            ["service"] = service, ["status"] = status, ["search"] = search, ["tenantId"] = tenantId?.ToString()
        };
        return string.Join('&', query.Where(item => !string.IsNullOrWhiteSpace(item.Value))
            .Select(item => $"{Uri.EscapeDataString(item.Key)}={Uri.EscapeDataString(item.Value!)}"));
    }

    public Task<ApiCallResult<SolicitationListItem[]>> GetSolicitationsAsync(string token, Guid tenantId, string? service, string? status, string? search, CancellationToken ct) =>
        SendAsync<SolicitationListItem[]>(CreateAuthorized(HttpMethod.Get, $"api/v1/organizations/{tenantId}/solicitations?{SolicitationQuery(service, status, search)}", token), false, ct);

    public Task<ApiCallResult<JsonElement>> GetSolicitationAsync(string token, Guid tenantId, Guid solicitationId, CancellationToken ct) =>
        SendAsync<JsonElement>(CreateAuthorized(HttpMethod.Get, $"api/v1/organizations/{tenantId}/solicitations/{solicitationId}", token), false, ct);

    public async Task<ApiCallResult<JsonElement>> OpenSolicitationAsync(string token, Guid tenantId, OpenSolicitationRequest body, CancellationToken ct)
    { using var message = CreateAuthorized(HttpMethod.Post, $"api/v1/organizations/{tenantId}/solicitations", token); message.Content = JsonContent.Create(body); return await SendAsync<JsonElement>(message, false, ct); }

    public async Task<ApiCallResult<JsonElement>> SolicitationMessageAsync(string token, Guid tenantId, Guid solicitationId, SolicitationMessageRequest body, CancellationToken ct)
    { using var message = CreateAuthorized(HttpMethod.Post, $"api/v1/organizations/{tenantId}/solicitations/{solicitationId}/messages", token); message.Content = JsonContent.Create(body); return await SendAsync<JsonElement>(message, false, ct); }

    public async Task<ApiCallResult<JsonElement>> SolicitationActionAsync(string token, Guid tenantId, Guid solicitationId, SolicitationActionRequest body, CancellationToken ct)
    { using var message = CreateAuthorized(HttpMethod.Post, $"api/v1/organizations/{tenantId}/solicitations/{solicitationId}/actions", token); message.Content = JsonContent.Create(body); return await SendAsync<JsonElement>(message, false, ct); }

    // Fila da plataforma ODCA.

    public Task<ApiCallResult<SolicitationListItem[]>> GetPlatformSolicitationsAsync(string token, Guid? tenantId, string? service, string? status, string? search, CancellationToken ct) =>
        SendAsync<SolicitationListItem[]>(CreateAuthorized(HttpMethod.Get, $"api/v1/platform/solicitations?{SolicitationQuery(service, status, search, tenantId)}", token), false, ct);

    public Task<ApiCallResult<JsonElement>> GetPlatformSolicitationAsync(string token, Guid solicitationId, CancellationToken ct) =>
        SendAsync<JsonElement>(CreateAuthorized(HttpMethod.Get, $"api/v1/platform/solicitations/{solicitationId}", token), false, ct);

    public async Task<ApiCallResult<JsonElement>> PlatformSolicitationMessageAsync(string token, Guid solicitationId, SolicitationMessageRequest body, CancellationToken ct)
    { using var message = CreateAuthorized(HttpMethod.Post, $"api/v1/platform/solicitations/{solicitationId}/messages", token); message.Content = JsonContent.Create(body); return await SendAsync<JsonElement>(message, false, ct); }

    public async Task<ApiCallResult<JsonElement>> PlatformSolicitationActionAsync(string token, Guid solicitationId, SolicitationActionRequest body, CancellationToken ct)
    { using var message = CreateAuthorized(HttpMethod.Post, $"api/v1/platform/solicitations/{solicitationId}/actions", token); message.Content = JsonContent.Create(body); return await SendAsync<JsonElement>(message, false, ct); }

    public Task<ApiCallResult<ApprovalQueueRow[]>> GetTemplateApprovalsAsync(string token, CancellationToken ct) =>
        SendAsync<ApprovalQueueRow[]>(CreateAuthorized(HttpMethod.Get, "api/v1/platform/template-approvals", token), false, ct);

    public async Task<ApiCallResult<JsonElement>> DecideTemplateApprovalAsync(string token, Guid tenantId, string key, ApprovalDecisionRequest body, CancellationToken ct)
    { using var message = CreateAuthorized(HttpMethod.Post, $"api/v1/platform/template-approvals/{tenantId}/{Uri.EscapeDataString(key)}/decision", token); message.Content = JsonContent.Create(body); return await SendAsync<JsonElement>(message, false, ct); }
}
