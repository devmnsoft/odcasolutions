using Odca.Contracts.Operations;

namespace Odca.Web.Services;

public sealed partial class OdcaApiClient
{
    public Task<ApiCallResult<OperationalInboxPageDto>> GetInboxAsync(
        string token,
        Guid tenantId,
        string scope,
        string? kind,
        string? urgency,
        Guid? contractId,
        Guid? ownerId,
        int page,
        int pageSize,
        CancellationToken ct) =>
        SendAsync<OperationalInboxPageDto>(
            CreateAuthorized(
                HttpMethod.Get,
                InboxPath(tenantId, scope, kind, urgency, contractId, ownerId, page, pageSize),
                token),
            false,
            ct);

    public Task<ApiCallResult<MonthlyAgendaPageDto>> GetAgendaAsync(
        string token,
        Guid tenantId,
        int year,
        int month,
        string scope,
        Guid? ownerId,
        string? kind,
        CancellationToken ct) =>
        SendAsync<MonthlyAgendaPageDto>(
            CreateAuthorized(HttpMethod.Get, AgendaPath(tenantId, year, month, scope, ownerId, kind), token),
            false,
            ct);

    public Task<ApiCallResult<ActionCenterResponse>> GetActionCenterAsync(
        string token, Guid tenantId, string scope, string? source, int page, int pageSize, CancellationToken ct) =>
        SendAsync<ActionCenterResponse>(
            CreateAuthorized(HttpMethod.Get, ActionCenterPath(tenantId, scope, source, page, pageSize), token),
            false, ct);

    private static string ActionCenterPath(Guid tenantId, string scope, string? source, int page, int pageSize)
    {
        var query = new Dictionary<string, string?>
        {
            ["scope"] = scope,
            ["source"] = source,
            ["page"] = page.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["pageSize"] = pageSize.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        return $"api/v1/organizations/{tenantId}/operations/action-center?{JoinQuery(query)}";
    }

    public Task<ApiCallResult<ContractSheetDto>> GetContractSheetAsync(
        string token,
        Guid tenantId,
        Guid contractId,
        CancellationToken ct) =>
        SendAsync<ContractSheetDto>(
            CreateAuthorized(HttpMethod.Get, $"api/v1/organizations/{tenantId}/contracts/{contractId}/sheet", token),
            false,
            ct);

    public Task<ApiCallResult<Odca.Contracts.Studio.OfficialTemplateInstallResponse>> InstallOfficialStudioTemplatesAsync(
        string token, Guid tenantId, CancellationToken ct) =>
        SendAsync<Odca.Contracts.Studio.OfficialTemplateInstallResponse>(
            CreateAuthorized(HttpMethod.Post, $"api/v1/organizations/{tenantId}/studio/templates/official", token),
            true, ct);

    public async Task<ApiCallResult<object>> CreateObligationAsync(
        string token,
        Guid tenantId,
        Guid contractId,
        Odca.Contracts.Obligations.CreateObligationRequest body,
        CancellationToken ct)
    {
        using var message = CreateAuthorized(HttpMethod.Post, $"api/v1/organizations/{tenantId}/obligations/contracts/{contractId}", token);
        message.Content = System.Net.Http.Json.JsonContent.Create(body);
        return await SendAsync<object>(message, false, ct);
    }

    public async Task<ApiCallResult<object>> PerformObligationActionAsync(
        string token,
        Guid tenantId,
        Guid obligationId,
        string operation,
        Odca.Contracts.Obligations.ObligationActionRequest body,
        CancellationToken ct)
    {
        using var message = CreateAuthorized(HttpMethod.Post, $"api/v1/organizations/{tenantId}/obligations/{obligationId}/{operation}", token);
        message.Content = System.Net.Http.Json.JsonContent.Create(body);
        return await SendAsync<object>(message, false, ct);
    }

    /// <summary>D-OC38 (Sprint 4/A-1): lê uma obrigação individual com versão atual — base do
    /// formulário de ações, que re-carrega o estado após conflito 409 para preservar valores.</summary>
    public Task<ApiCallResult<Odca.Contracts.Obligations.ObligationItem>> GetObligationAsync(
        string token, Guid tenantId, Guid obligationId, CancellationToken ct) =>
        SendAsync<Odca.Contracts.Obligations.ObligationItem>(
            CreateAuthorized(HttpMethod.Get, $"api/v1/organizations/{tenantId}/obligations/{obligationId}", token),
            false,
            ct);

    /// <summary>D-OC38 (Sprint 4/A-1): executa ação preservando o <c>currentVersion</c> do 409,
    /// para que o formulário atualize a versão esperada e repita sem perder os valores digitados.</summary>
    public async Task<(ApiCallStatus Status, string? Title, string? Code, long? CurrentVersion)> PerformObligationActionDetailedAsync(
        string token,
        Guid tenantId,
        Guid obligationId,
        string operation,
        Odca.Contracts.Obligations.ObligationActionRequest body,
        CancellationToken ct)
    {
        using var message = CreateAuthorized(HttpMethod.Post, $"api/v1/organizations/{tenantId}/obligations/{obligationId}/{operation}", token);
        message.Content = System.Net.Http.Json.JsonContent.Create(body);
        try
        {
            using var response = await client.SendAsync(message, ct);
            if (response.IsSuccessStatusCode) return (ApiCallStatus.Success, null, null, null);
            string? title = null, code = null; long? currentVersion = null;
            var json = await response.Content.ReadAsStringAsync(ct);
            if (!string.IsNullOrWhiteSpace(json))
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("title", out var t) && t.ValueKind == System.Text.Json.JsonValueKind.String) title = t.GetString();
                if (doc.RootElement.TryGetProperty("code", out var c) && c.ValueKind == System.Text.Json.JsonValueKind.String) code = c.GetString();
                if (doc.RootElement.TryGetProperty("currentVersion", out var v) && v.ValueKind == System.Text.Json.JsonValueKind.Number) currentVersion = v.GetInt64();
            }
            return (MapStatus(response.StatusCode, false), title, code, currentVersion);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return (ApiCallStatus.Timeout, null, null, null);
        }
        catch (System.IO.IOException)
        {
            return (ApiCallStatus.Unavailable, null, null, null);
        }
    }

    public async Task<ApiCallResult<object>> CreateRenewalProposalAsync(
        string token,
        Guid tenantId,
        Guid contractId,
        Odca.Contracts.Renewals.CreateRenewalRequest body,
        CancellationToken ct)
    {
        using var message = CreateAuthorized(HttpMethod.Post, $"api/v1/organizations/{tenantId}/renewals/contracts/{contractId}", token);
        message.Content = System.Net.Http.Json.JsonContent.Create(body);
        return await SendAsync<object>(message, false, ct);
    }

    private static string InboxPath(
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
            ["page"] = page.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["pageSize"] = pageSize.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        return $"api/v1/organizations/{tenantId}/inbox?{JoinQuery(query)}";
    }

    private static string AgendaPath(Guid tenantId, int year, int month, string scope, Guid? ownerId, string? kind)
    {
        var query = new Dictionary<string, string?>
        {
            ["year"] = year.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["month"] = month.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["scope"] = scope,
            ["ownerId"] = ownerId?.ToString(),
            ["kind"] = kind
        };
        return $"api/v1/organizations/{tenantId}/agenda?{JoinQuery(query)}";
    }

    private static string JoinQuery(Dictionary<string, string?> query) =>
        string.Join('&', query
            .Where(item => !string.IsNullOrWhiteSpace(item.Value))
            .Select(item => $"{Uri.EscapeDataString(item.Key)}={Uri.EscapeDataString(item.Value!)}"));
}
