

using Odca.Contracts.Operations;
using Odca.Web.Services;

public Task<ApiCallResult<OperationalInboxPageDto>> GetInboxAsync(
    string token, Guid tenantId, string scope, string? kind, string? urgency,
    Guid? contractId, Guid? ownerId, int page, int pageSize, CancellationToken ct) =>
    SendAsync<OperationalInboxPageDto>(
        CreateAuthorized(HttpMethod.Get, OdcaApiClientOperations.InboxPath(tenantId, scope, kind, urgency, contractId, ownerId, page, pageSize), token),
        false, ct);

public Task<ApiCallResult<MonthlyAgendaPageDto>> GetAgendaAsync(
    string token, Guid tenantId, int year, int month, string scope, Guid? ownerId, CancellationToken ct) =>
    SendAsync<MonthlyAgendaPageDto>(
        CreateAuthorized(HttpMethod.Get, OdcaApiClientOperations.AgendaPath(tenantId, year, month, scope, ownerId), token),
        false, ct);

public Task<ApiCallResult<ContractSheetDto>> GetContractSheetAsync(
    string token, Guid tenantId, Guid contractId, CancellationToken ct) =>
    SendAsync<ContractSheetDto>(
        CreateAuthorized(HttpMethod.Get, OdcaApiClientOperations.SheetPath(tenantId, contractId), token),
        false, ct);

public Task<ApiCallResult<Odca.Contracts.Studio.OfficialTemplateInstallResponse>> InstallOfficialStudioTemplatesAsync(
    string token, Guid tenantId, CancellationToken ct) =>
    SendAsync<Odca.Contracts.Studio.OfficialTemplateInstallResponse>(
        CreateAuthorized(HttpMethod.Post, $"api/v1/organizations/{tenantId}/studio/templates/official", token),
        true, ct);
