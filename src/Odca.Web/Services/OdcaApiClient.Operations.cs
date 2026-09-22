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
