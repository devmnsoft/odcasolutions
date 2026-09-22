using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Odca.Contracts.Identity;
using Odca.Contracts.Onboarding;
using Odca.Contracts.Plans;
using Odca.Contracts.Privacy;
using Odca.Contracts.Tenancy;
using Odca.Contracts.Obligations;
using Odca.Contracts.SavedViews;
using Odca.Contracts.Studio;
using Odca.Contracts.Renewals;
using Odca.Contracts.Consumption;
using Odca.Contracts.DocumentImports;
using Odca.Contracts.Reviews;

namespace Odca.Web.Services;

public sealed partial class OdcaApiClient(HttpClient client)
{
    public Task<ApiCallResult<ReviewQueuePage>> GetReviewsAsync(string token, Guid tenantId, string? status, bool mine, int page, CancellationToken ct) =>
        SendAsync<ReviewQueuePage>(CreateAuthorized(HttpMethod.Get, $"api/v1/organizations/{tenantId}/reviews?status={Uri.EscapeDataString(status ?? string.Empty)}&mine={mine.ToString().ToLowerInvariant()}&page={page}", token), false, ct);
    public Task<ApiCallResult<ReviewDetail>> GetReviewAsync(string token, Guid tenantId, Guid reviewId, CancellationToken ct) =>
        SendAsync<ReviewDetail>(CreateAuthorized(HttpMethod.Get, $"api/v1/organizations/{tenantId}/reviews/{reviewId}", token), false, ct);
    public async Task<ApiCallResult<JsonElement>> AddReviewMessageAsync(string token, Guid tenantId, Guid reviewId, AddReviewMessageRequest body, CancellationToken ct)
    {
        using var request = CreateAuthorized(HttpMethod.Post, $"api/v1/organizations/{tenantId}/reviews/{reviewId}/messages", token);
        request.Content = JsonContent.Create(body);
        return await SendAsync<JsonElement>(request, false, ct);
    }

    public async Task<ApiCallResult<JsonElement>> DecideReviewAsync(string token, Guid tenantId, Guid reviewId, DecideReviewRequest body, CancellationToken ct)
    {
        using var message = CreateAuthorized(HttpMethod.Post, $"api/v1/organizations/{tenantId}/reviews/{reviewId}/decision", token);
        message.Content = JsonContent.Create(body);
        return await SendAsync<JsonElement>(message, false, ct);
    }
    public async Task<(HttpStatusCode Status, byte[]? Content, string? ContentType)> GetDocumentPreviewAsync(string token, Guid tenantId, Guid contractId, Guid documentId, Guid versionId, CancellationToken ct)
    {
        using var request = CreateAuthorized(HttpMethod.Get, $"api/v1/organizations/{tenantId}/contracts/{contractId}/documents/{documentId}/versions/{versionId}/content", token);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode) return (response.StatusCode, null, null);
        return (response.StatusCode, await response.Content.ReadAsByteArrayAsync(ct), response.Content.Headers.ContentType?.MediaType);
    }
    public Task<ApiCallResult<ContractImportPage>> GetContractImportsAsync(string token, Guid tenantId, string? status, bool mine, bool awaitingReview, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var query = new Dictionary<string, string?>
        {
            ["status"] = status,
            ["mine"] = mine ? "true" : null,
            ["awaitingReview"] = awaitingReview ? "true" : null,
            ["from"] = from?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["to"] = to?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        };
        var encoded = string.Join('&', query.Where(item => !string.IsNullOrWhiteSpace(item.Value))
            .Select(item => $"{Uri.EscapeDataString(item.Key)}={Uri.EscapeDataString(item.Value!)}"));
        return SendAsync<ContractImportPage>(CreateAuthorized(HttpMethod.Get, $"api/v1/organizations/{tenantId}/contract-imports?{encoded}", token), false, ct);
    }

    public Task<ApiCallResult<JsonElement>> GetContractImportAsync(string token, Guid tenantId, Guid importId, CancellationToken ct) =>
        SendAsync<JsonElement>(CreateAuthorized(HttpMethod.Get, $"api/v1/organizations/{tenantId}/contract-imports/{importId}", token), false, ct);

    public async Task<ApiCallResult<JsonElement>> SaveImportReviewAsync(string token, Guid tenantId, Guid contractId, Guid extractionId, SaveImportReview body, CancellationToken ct)
    {
        using var request = CreateAuthorized(HttpMethod.Post, $"api/v1/organizations/{tenantId}/contracts/{contractId}/documents/extractions/{extractionId}/apply", token);
        request.Content = JsonContent.Create(body);
        return await SendAsync<JsonElement>(request, false, ct);
    }
    public Task<ApiCallResult<ConsumptionSummary>> GetConsumptionAsync(string token,Guid tenantId,CancellationToken ct)=>SendAsync<ConsumptionSummary>(CreateAuthorized(HttpMethod.Get,$"api/v1/organizations/{tenantId}/consumption",token),false,ct);
    public Task<ApiCallResult<AdditionalStorageRequest[]>> GetStorageRequestsAsync(string token,Guid tenantId,CancellationToken ct)=>SendAsync<AdditionalStorageRequest[]>(CreateAuthorized(HttpMethod.Get,$"api/v1/organizations/{tenantId}/storage-requests",token),false,ct);
    public Task<ApiCallResult<StoragePackage[]>> GetStoragePackagesAsync(string token,CancellationToken ct)=>SendAsync<StoragePackage[]>(CreateAuthorized(HttpMethod.Get,"api/v1/storage-packages",token),false,ct);
    public async Task<ApiCallResult<AdditionalStorageRequest>> RequestStorageAsync(string token,Guid tenantId,CreateStorageRequest body,CancellationToken ct){using var m=CreateAuthorized(HttpMethod.Post,$"api/v1/organizations/{tenantId}/storage-requests",token);m.Content=JsonContent.Create(body);return await SendAsync<AdditionalStorageRequest>(m,false,ct);}
    public Task<ApiCallResult<PlatformCustomer[]>> GetCustomersAsync(string token,string? search,CancellationToken ct)=>SendAsync<PlatformCustomer[]>(CreateAuthorized(HttpMethod.Get,$"api/v1/platform/customers?search={Uri.EscapeDataString(search??string.Empty)}",token),false,ct);
    public Task<ApiCallResult<PlatformCustomerDetail>> GetCustomerAsync(string token,Guid tenantId,CancellationToken ct)=>SendAsync<PlatformCustomerDetail>(CreateAuthorized(HttpMethod.Get,$"api/v1/platform/customers/{tenantId}",token),false,ct);
    public async Task<ApiCallResult<bool>> DecideStorageAsync(string token,Guid tenantId,Guid requestId,DecideStorageRequest body,CancellationToken ct){using var m=CreateAuthorized(HttpMethod.Post,$"api/v1/platform/customers/{tenantId}/storage-requests/{requestId}/decision",token);m.Content=JsonContent.Create(body);return await SendAsync<bool>(m,false,ct,true);}
    public async Task<ApiCallResult<bool>> GrantStorageAsync(string token,Guid tenantId,ManualStorageGrant body,CancellationToken ct){using var m=CreateAuthorized(HttpMethod.Post,$"api/v1/platform/customers/{tenantId}/storage-grants",token);m.Content=JsonContent.Create(body);return await SendAsync<bool>(m,false,ct,true);}
    public Task<ApiCallResult<RenewalPage>> GetRenewalsAsync(string token,Guid tenantId,DateOnly? from,DateOnly? to,Guid? ownerId,string? contractType,string? counterparty,string? status,bool mine,bool withoutOwner,int page,int pageSize,CancellationToken cancellationToken)
    {
        var query=new Dictionary<string,string?> { ["from"]=from?.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),["to"]=to?.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),["ownerId"]=ownerId?.ToString(),["contractType"]=contractType,["counterparty"]=counterparty,["status"]=status,["mine"]=mine?"true":null,["withoutOwner"]=withoutOwner?"true":null,["page"]=page.ToString(CultureInfo.InvariantCulture),["pageSize"]=pageSize.ToString(CultureInfo.InvariantCulture)};
        var encoded=string.Join('&',query.Where(x=>!string.IsNullOrWhiteSpace(x.Value)).Select(x=>$"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value!)}"));
        return SendAsync<RenewalPage>(CreateAuthorized(HttpMethod.Get,$"api/v1/organizations/{tenantId}/renewals?{encoded}",token),false,cancellationToken);
    }

    public async Task<ApiCallResult<ApplyRenewalResponse>> ApplyRenewalAsync(string token, Guid tenantId, Guid requestId, ApplyRenewalRequest request, CancellationToken ct)
    {
        using var message = CreateAuthorized(HttpMethod.Post, $"api/v1/organizations/{tenantId}/renewals/{requestId}/apply", token);
        message.Content = JsonContent.Create(request);
        return await SendAsync<ApplyRenewalResponse>(message, false, ct);
    }

    public Task<ApiCallResult<SavedViewItem[]>> GetSavedViewsAsync(string token, Guid tenantId, string listingType, CancellationToken cancellationToken) =>
        SendAsync<SavedViewItem[]>(CreateAuthorized(HttpMethod.Get, $"api/v1/organizations/{tenantId}/saved-views?listingType={Uri.EscapeDataString(listingType)}", token), false, cancellationToken);

    public Task<ApiCallResult<SavedViewItem>> GetSavedViewAsync(string token, Guid tenantId, Guid id, CancellationToken cancellationToken) =>
        SendAsync<SavedViewItem>(CreateAuthorized(HttpMethod.Get, $"api/v1/organizations/{tenantId}/saved-views/{id}", token), false, cancellationToken);

    public async Task<ApiCallResult<SavedViewItem>> CreateSavedViewAsync(string token, Guid tenantId, SaveViewRequest body, CancellationToken cancellationToken)
    {
        using var message = CreateAuthorized(HttpMethod.Post, $"api/v1/organizations/{tenantId}/saved-views", token);
        message.Content = JsonContent.Create(body);
        return await SendAsync<SavedViewItem>(message, false, cancellationToken);
    }

    public async Task<ApiCallResult<bool>> UpdateSavedViewAsync(string token, Guid tenantId, Guid id, UpdateSavedViewRequest body, CancellationToken cancellationToken)
    {
        using var message = CreateAuthorized(HttpMethod.Put, $"api/v1/organizations/{tenantId}/saved-views/{id}", token);
        message.Content = JsonContent.Create(body);
        return await SendAsync<bool>(message, false, cancellationToken, emptyBodyIsSuccess: true);
    }

    public Task<ApiCallResult<bool>> DeactivateSavedViewAsync(string token, Guid tenantId, Guid id, CancellationToken cancellationToken) =>
        SendAsync<bool>(CreateAuthorized(HttpMethod.Delete, $"api/v1/organizations/{tenantId}/saved-views/{id}", token), false, cancellationToken, emptyBodyIsSuccess: true);

    public Task<ApiCallResult<ObligationHistoryResponse>> GetObligationHistoryAsync(
        string token,
        Guid tenantId,
        Guid obligationId,
        CancellationToken cancellationToken) =>
        SendAsync<ObligationHistoryResponse>(
            CreateAuthorized(HttpMethod.Get, $"api/v1/organizations/{tenantId}/obligations/{obligationId}/history", token),
            false,
            cancellationToken);

    public Task<ApiCallResult<ObligationPage>> GetObligationsAsync(
        string token,
        Guid tenantId,
        string scope,
        Guid? contractId,
        Guid? ownerId,
        string? category,
        string? status,
        DateOnly? from,
        DateOnly? to,
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = new Dictionary<string, string?>
        {
            ["scope"] = scope,
            ["contractId"] = contractId?.ToString(),
            ["ownerId"] = ownerId?.ToString(),
            ["category"] = category,
            ["status"] = status,
            ["from"] = from?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["to"] = to?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["search"] = search,
            ["page"] = page.ToString(CultureInfo.InvariantCulture),
            ["pageSize"] = pageSize.ToString(CultureInfo.InvariantCulture)
        };
        var encoded = string.Join('&', query
            .Where(item => !string.IsNullOrWhiteSpace(item.Value))
            .Select(item => $"{Uri.EscapeDataString(item.Key)}={Uri.EscapeDataString(item.Value ?? string.Empty)}"));
        var request = CreateAuthorized(
            HttpMethod.Get,
            $"api/v1/organizations/{tenantId}/obligations?{encoded}",
            token);

        return SendAsync<ObligationPage>(request, false, cancellationToken);
    }
    public async Task<ApiCallResult<LoginResponse>> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "api/v1/auth/login")
        {
            Content = JsonContent.Create(request)
        };
        return await SendAsync<LoginResponse>(message, invalidCredentialsOnUnauthorized: true, cancellationToken);
    }

    public async Task<ApiCallResult<LoginResponse>> ChangePasswordAsync(
        string accessToken,
        ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        using var message = CreateAuthorized(HttpMethod.Post, "api/v1/auth/change-password", accessToken);
        message.Content = JsonContent.Create(request);
        return await SendAsync<LoginResponse>(message, invalidCredentialsOnUnauthorized: false, cancellationToken);
    }

    public async Task<ApiCallResult<MfaEnrollmentResponse>> StartMfaEnrollmentAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var message = CreateAuthorized(HttpMethod.Post, "api/v1/auth/mfa/enrollment", accessToken);
        return await SendAsync<MfaEnrollmentResponse>(message, invalidCredentialsOnUnauthorized: false, cancellationToken);
    }

    public async Task<ApiCallResult<MfaVerificationResponse>> ConfirmMfaEnrollmentAsync(
        string accessToken,
        ConfirmMfaEnrollmentRequest request,
        CancellationToken cancellationToken)
    {
        using var message = CreateAuthorized(HttpMethod.Post, "api/v1/auth/mfa/enrollment/confirm", accessToken);
        message.Content = JsonContent.Create(request);
        return await SendAsync<MfaVerificationResponse>(message, invalidCredentialsOnUnauthorized: false, cancellationToken);
    }

    public async Task<ApiCallResult<MfaVerificationResponse>> VerifyMfaChallengeAsync(
        string accessToken,
        MfaChallengeRequest request,
        CancellationToken cancellationToken)
    {
        using var message = CreateAuthorized(HttpMethod.Post, "api/v1/auth/mfa/challenge", accessToken);
        message.Content = JsonContent.Create(request);
        return await SendAsync<MfaVerificationResponse>(message, invalidCredentialsOnUnauthorized: false, cancellationToken);
    }

    public async Task<ApiCallResult<DashboardResponse>> GetDashboardAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var message = CreateAuthorized(HttpMethod.Get, "api/v1/platform/dashboard", accessToken);
        return await SendAsync<DashboardResponse>(message, invalidCredentialsOnUnauthorized: false, cancellationToken);
    }

    public async Task<ApiCallResult<PlanCatalogResponse[]>> GetPlansAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var message = CreateAuthorized(HttpMethod.Get, "api/v1/platform/plans", accessToken);
        return await SendAsync<PlanCatalogResponse[]>(message, invalidCredentialsOnUnauthorized: false, cancellationToken);
    }

    public async Task<ApiCallResult<PlanCatalogResponse[]>> GetPublicPlansAsync(CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, "api/v1/catalog/plans");
        return await SendAsync<PlanCatalogResponse[]>(message, invalidCredentialsOnUnauthorized: false, cancellationToken);
    }

    public async Task<ApiCallResult<PrivacyRequestCreated>> SubmitPrivacyRequestAsync(
        CreatePrivacyRequest request,
        CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "api/v1/privacy/requests")
        {
            Content = JsonContent.Create(request)
        };
        return await SendAsync<PrivacyRequestCreated>(message, invalidCredentialsOnUnauthorized: false, cancellationToken);
    }

    public async Task<ApiCallResult<StartCustomerRegistrationResponse>> StartCustomerRegistrationAsync(
        StartCustomerRegistrationRequest request,
        CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "api/v1/onboarding/registrations")
        {
            Content = JsonContent.Create(request)
        };
        return await SendAsync<StartCustomerRegistrationResponse>(message, invalidCredentialsOnUnauthorized: false, cancellationToken);
    }

    public async Task<ApiCallResult<ConfirmCustomerRegistrationResponse>> ConfirmCustomerRegistrationAsync(
        ConfirmCustomerRegistrationRequest request,
        CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "api/v1/onboarding/confirm-email")
        {
            Content = JsonContent.Create(request)
        };
        return await SendAsync<ConfirmCustomerRegistrationResponse>(message, invalidCredentialsOnUnauthorized: false, cancellationToken);
    }

    public async Task<ApiCallResult<CustomerHomeResponse>> GetCustomerHomeAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var message = CreateAuthorized(HttpMethod.Get, "api/v1/onboarding/customer-home", accessToken);
        return await SendAsync<CustomerHomeResponse>(message, invalidCredentialsOnUnauthorized: false, cancellationToken);
    }

    public Task<ApiCallResult<OrganizationSummary[]>> GetOrganizationsAsync(string token, CancellationToken cancellationToken)
        => SendAsync<OrganizationSummary[]>(CreateAuthorized(HttpMethod.Get, "api/v1/organizations", token), false, cancellationToken);

    public Task<ApiCallResult<OrganizationDetails>> GetOrganizationAsync(string token, Guid tenantId, CancellationToken cancellationToken)
        => SendAsync<OrganizationDetails>(CreateAuthorized(HttpMethod.Get, $"api/v1/organizations/{tenantId}", token), false, cancellationToken);

    public async Task<ApiCallResult<bool>> UpdateOrganizationAsync(
        string token,
        Guid tenantId,
        UpdateOrganizationRequest request,
        CancellationToken cancellationToken)
    {
        using var message = CreateAuthorized(HttpMethod.Put, $"api/v1/organizations/{tenantId}", token);
        message.Content = JsonContent.Create(request);
        return await SendAsync<bool>(message, false, cancellationToken, emptyBodyIsSuccess: true);
    }

    public Task<ApiCallResult<OrganizationOverviewResponse>> GetOrganizationOverviewAsync(
        string token,
        Guid tenantId,
        CancellationToken cancellationToken)
        => SendAsync<OrganizationOverviewResponse>(
            CreateAuthorized(HttpMethod.Get, $"api/v1/organizations/{tenantId}/overview", token),
            false,
            cancellationToken);

    public Task<ApiCallResult<PaginatedResponse<TeamMemberResponse>>> GetMembersAsync(
        string token,
        Guid tenantId,
        string? search,
        string? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query =
            $"search={Uri.EscapeDataString(search ?? string.Empty)}" +
            $"&status={Uri.EscapeDataString(status ?? string.Empty)}" +
            $"&page={page}&pageSize={pageSize}";
        return SendAsync<PaginatedResponse<TeamMemberResponse>>(
            CreateAuthorized(HttpMethod.Get, $"api/v1/organizations/{tenantId}/members?{query}", token),
            false,
            cancellationToken);
    }

    public Task<ApiCallResult<TeamMemberDetailResponse>> GetMemberAsync(
        string token,
        Guid tenantId,
        Guid userId,
        CancellationToken cancellationToken)
        => SendAsync<TeamMemberDetailResponse>(
            CreateAuthorized(HttpMethod.Get, $"api/v1/organizations/{tenantId}/members/{userId}", token),
            false,
            cancellationToken);

    public async Task<ApiCallResult<bool>> MemberActionAsync(
        string token,
        Guid tenantId,
        Guid userId,
        string action,
        MemberStatusChangeRequest request,
        CancellationToken cancellationToken)
    {
        using var message = CreateAuthorized(
            HttpMethod.Post,
            $"api/v1/organizations/{tenantId}/members/{userId}/{action}",
            token);
        message.Content = JsonContent.Create(request);
        return await SendAsync<bool>(message, false, cancellationToken, emptyBodyIsSuccess: true);
    }

    public async Task<ApiCallResult<bool>> UpdateMemberRolesAsync(
        string token,
        Guid tenantId,
        Guid userId,
        UpdateMemberRolesRequest request,
        CancellationToken cancellationToken)
    {
        using var message = CreateAuthorized(
            HttpMethod.Put,
            $"api/v1/organizations/{tenantId}/members/{userId}/roles",
            token);
        message.Content = JsonContent.Create(request);
        return await SendAsync<bool>(message, false, cancellationToken, emptyBodyIsSuccess: true);
    }

    public Task<ApiCallResult<TenantRoleResponse[]>> GetRolesAsync(string token, Guid tenantId, CancellationToken cancellationToken)
        => SendAsync<TenantRoleResponse[]>(
            CreateAuthorized(HttpMethod.Get, $"api/v1/organizations/{tenantId}/roles", token),
            false,
            cancellationToken);

    public async Task<ApiCallResult<TenantRoleResponse>> CreateRoleAsync(
        string token,
        Guid tenantId,
        CreateTenantRoleRequest request,
        CancellationToken cancellationToken)
    {
        using var message = CreateAuthorized(HttpMethod.Post, $"api/v1/organizations/{tenantId}/roles", token);
        message.Content = JsonContent.Create(request);
        return await SendAsync<TenantRoleResponse>(message, false, cancellationToken);
    }

    public async Task<ApiCallResult<UpdateTenantRolePermissionsResponse>> UpdateRolePermissionsAsync(
        string token,
        Guid tenantId,
        Guid roleId,
        UpdateTenantRolePermissionsRequest request,
        CancellationToken cancellationToken)
    {
        using var message = CreateAuthorized(
            HttpMethod.Put,
            $"api/v1/organizations/{tenantId}/roles/{roleId}/permissions",
            token);
        message.Content = JsonContent.Create(request);
        return await SendAsync<UpdateTenantRolePermissionsResponse>(message, false, cancellationToken);
    }

    public Task<ApiCallResult<PaginatedResponse<InvitationListItemResponse>>> GetInvitationsAsync(
        string token,
        Guid tenantId,
        string? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query =
            $"status={Uri.EscapeDataString(status ?? string.Empty)}" +
            $"&page={page}&pageSize={pageSize}";
        return SendAsync<PaginatedResponse<InvitationListItemResponse>>(
            CreateAuthorized(HttpMethod.Get, $"api/v1/organizations/{tenantId}/invitations?{query}", token),
            false,
            cancellationToken);
    }

    public async Task<ApiCallResult<InvitationResponse>> InviteAsync(
        string token,
        Guid tenantId,
        CreateInvitationRequest request,
        CancellationToken cancellationToken)
    {
        using var message = CreateAuthorized(HttpMethod.Post, $"api/v1/organizations/{tenantId}/invitations", token);
        message.Content = JsonContent.Create(request);
        return await SendAsync<InvitationResponse>(message, false, cancellationToken);
    }

    public async Task<ApiCallResult<bool>> CancelInvitationAsync(
        string token,
        Guid tenantId,
        Guid invitationId,
        CancellationToken cancellationToken)
    {
        using var message = CreateAuthorized(
            HttpMethod.Post,
            $"api/v1/organizations/{tenantId}/invitations/{invitationId}/cancel",
            token);
        return await SendAsync<bool>(message, false, cancellationToken, emptyBodyIsSuccess: true);
    }

    public async Task<ApiCallResult<bool>> ResendInvitationAsync(
        string token,
        Guid tenantId,
        Guid invitationId,
        CancellationToken cancellationToken)
    {
        using var message = CreateAuthorized(
            HttpMethod.Post,
            $"api/v1/organizations/{tenantId}/invitations/{invitationId}/resend",
            token);
        return await SendAsync<bool>(message, false, cancellationToken, emptyBodyIsSuccess: true);
    }

    public async Task<ApiCallResult<InvitationPreviewResponse>> GetInvitationPreviewAsync(
        Guid invitationId,
        string inviteToken,
        CancellationToken cancellationToken)
    {
        var query =
            $"invitationId={invitationId}&token={Uri.EscapeDataString(inviteToken)}";
        using var message = new HttpRequestMessage(HttpMethod.Get, $"api/v1/organizations/invitations/preview?{query}");
        return await SendAsync<InvitationPreviewResponse>(message, false, cancellationToken);
    }

    public async Task<ApiCallResult<object>> AcceptInvitationAsync(
        string accessToken,
        AcceptInvitationRequest request,
        CancellationToken cancellationToken)
    {
        using var message = CreateAuthorized(HttpMethod.Post, "api/v1/organizations/invitations/accept", accessToken);
        message.Content = JsonContent.Create(request);
        return await SendAsync<object>(message, false, cancellationToken);
    }

    public async Task<bool> LogoutAsync(string accessToken, CancellationToken cancellationToken)
    {
        try
        {
            using var message = CreateAuthorized(HttpMethod.Post, "api/v1/auth/logout", accessToken);
            using var response = await client.SendAsync(message, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    public Task<ApiCallResult<TemplateCatalogPage>> GetStudioTemplatesAsync(string token, Guid tenantId, string? search, int page, CancellationToken ct) =>
        SendAsync<TemplateCatalogPage>(CreateAuthorized(HttpMethod.Get, $"api/v1/organizations/{tenantId}/studio/templates?search={Uri.EscapeDataString(search ?? string.Empty)}&page={page}", token), false, ct);

    public Task<ApiCallResult<TemplatePreview>> GetOfficialStudioTemplateByKeyAsync(string token, Guid tenantId, string key, CancellationToken ct) =>
        SendAsync<TemplatePreview>(CreateAuthorized(HttpMethod.Get, $"api/v1/organizations/{tenantId}/studio/templates/official/{Uri.EscapeDataString(key)}", token), false, ct);

    public async Task<ApiCallResult<JsonElement>> CreateStudioDraftAsync(string token, Guid tenantId, CreateDraftRequest request, CancellationToken ct)
    {
        using var message = CreateAuthorized(HttpMethod.Post, $"api/v1/organizations/{tenantId}/studio/drafts", token);
        message.Content = JsonContent.Create(request);
        return await SendAsync<JsonElement>(message, false, ct);
    }

    public Task<ApiCallResult<DraftResponse>> GetStudioDraftAsync(string token, Guid tenantId, Guid draftId, CancellationToken ct) =>
        SendAsync<DraftResponse>(CreateAuthorized(HttpMethod.Get, $"api/v1/organizations/{tenantId}/studio/drafts/{draftId}", token), false, ct);

    public async Task<ApiCallResult<SaveDraftResponse>> SaveStudioDraftAsync(string token, Guid tenantId, Guid draftId, SaveDraftRequest request, CancellationToken ct)
    {
        using var message = CreateAuthorized(HttpMethod.Put, $"api/v1/organizations/{tenantId}/studio/drafts/{draftId}", token);
        message.Content = JsonContent.Create(request);
        return await SendAsync<SaveDraftResponse>(message, false, ct);
    }

    public async Task<ApiCallResult<GeneratedVersionResponse>> GenerateStudioVersionAsync(string token, Guid tenantId, Guid draftId, CancellationToken ct)
    {
        using var message = CreateAuthorized(HttpMethod.Post, $"api/v1/organizations/{tenantId}/studio/drafts/{draftId}/versions", token);
        message.Content = JsonContent.Create(new GenerateVersionRequest(Guid.NewGuid()));
        return await SendAsync<GeneratedVersionResponse>(message, false, ct);
    }

    public async Task<ApiCallResult<GeneratedVersionResponse>> GenerateStudioVersionAsync(string token, Guid tenantId, Guid draftId, GenerateVersionRequest request, CancellationToken ct)
    {
        using var message = CreateAuthorized(HttpMethod.Post, $"api/v1/organizations/{tenantId}/studio/drafts/{draftId}/versions", token);
        message.Content = JsonContent.Create(request); return await SendAsync<GeneratedVersionResponse>(message, false, ct);
    }

    public Task<ApiCallResult<StudioReviewerItem[]>> GetStudioReviewersAsync(string token, Guid tenantId, CancellationToken ct) =>
        SendAsync<StudioReviewerItem[]>(CreateAuthorized(HttpMethod.Get, $"api/v1/organizations/{tenantId}/studio/reviewers", token), false, ct);

    public async Task<ApiCallResult<ReviewSubmittedResponse>> SubmitStudioReviewAsync(string token, Guid tenantId, SubmitReviewRequest request, CancellationToken ct)
    {
        using var message = CreateAuthorized(HttpMethod.Post, $"api/v1/organizations/{tenantId}/studio/reviews", token);
        message.Content = JsonContent.Create(request); return await SendAsync<ReviewSubmittedResponse>(message, false, ct);
    }

    public Task<ApiCallResult<StudioVersionItem[]>> GetStudioVersionsAsync(string token, Guid tenantId, Guid draftId, CancellationToken ct) =>
        SendAsync<StudioVersionItem[]>(CreateAuthorized(HttpMethod.Get, $"api/v1/organizations/{tenantId}/studio/drafts/{draftId}/versions", token), false, ct);

    public Task<ApiCallResult<VersionComparisonResponse>> CompareStudioVersionsAsync(string token, Guid tenantId, Guid before, Guid after, CancellationToken ct) =>
        SendAsync<VersionComparisonResponse>(CreateAuthorized(HttpMethod.Get, $"api/v1/organizations/{tenantId}/studio/versions/compare?before={before}&after={after}", token), false, ct);

    public Task<ApiCallResult<ChecklistResponse>> GetStudioChecklistAsync(string token, Guid tenantId, Guid draftId, long version, CancellationToken ct) =>
        SendAsync<ChecklistResponse>(CreateAuthorized(HttpMethod.Get, $"api/v1/organizations/{tenantId}/studio/drafts/{draftId}/checklist?expectedVersion={version}", token), false, ct);

    public Task<ApiCallResult<StudioCommentItem[]>> GetStudioCommentsAsync(string token, Guid tenantId, Guid draftId, bool resolved, CancellationToken ct) =>
        SendAsync<StudioCommentItem[]>(CreateAuthorized(HttpMethod.Get, $"api/v1/organizations/{tenantId}/studio/drafts/{draftId}/comments?includeResolved={resolved}", token), false, ct);

    public async Task<ApiCallResult<JsonElement>> AddStudioCommentAsync(string token, Guid tenantId, Guid draftId, CreateStudioCommentRequest request, CancellationToken ct)
    {
        using var message = CreateAuthorized(HttpMethod.Post, $"api/v1/organizations/{tenantId}/studio/drafts/{draftId}/comments", token);
        message.Content = JsonContent.Create(request); return await SendAsync<JsonElement>(message, false, ct);
    }

    public async Task<ApiCallResult<bool>> SetStudioCommentStateAsync(string token, Guid tenantId, Guid draftId, Guid commentId, string operation, ChangeStudioCommentStateRequest request, CancellationToken ct)
    {
        if (operation is not ("resolve" or "reopen")) throw new ArgumentOutOfRangeException(nameof(operation));
        using var message = CreateAuthorized(HttpMethod.Post, $"api/v1/organizations/{tenantId}/studio/drafts/{draftId}/comments/{commentId}/{operation}", token);
        message.Content = JsonContent.Create(request);
        return await SendAsync<bool>(message, false, ct, emptyBodyIsSuccess: true);
    }

    private async Task<ApiCallResult<T>> SendAsync<T>(
        HttpRequestMessage message,
        bool invalidCredentialsOnUnauthorized,
        CancellationToken cancellationToken,
        bool emptyBodyIsSuccess = false)
    {
        try
        {
            using var response = await client.SendAsync(message, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.NoContent ||
                    (emptyBodyIsSuccess && response.Content.Headers.ContentLength == 0))
                {
                    return EmptySuccess<T>();
                }

                var value = await response.Content.ReadFromJsonAsync<T>(cancellationToken);
                if (value is null)
                {
                    return emptyBodyIsSuccess ? EmptySuccess<T>() : new(ApiCallStatus.Unavailable);
                }

                return new(ApiCallStatus.Success, value);
            }

            var (title, detail) = await ReadProblemAsync(response, cancellationToken);
            return new(MapStatus(response.StatusCode, invalidCredentialsOnUnauthorized), default, title, detail);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(ApiCallStatus.Timeout);
        }
        catch (HttpRequestException)
        {
            return new(ApiCallStatus.Unavailable);
        }
    }

    private static ApiCallResult<T> EmptySuccess<T>()
    {
        if (typeof(T) == typeof(bool))
        {
            object boxed = true;
            return new(ApiCallStatus.Success, (T)boxed);
        }

        return new(ApiCallStatus.Success, default);
    }

    private static ApiCallStatus MapStatus(HttpStatusCode statusCode, bool invalidCredentialsOnUnauthorized) =>
        statusCode switch
        {
            HttpStatusCode.Unauthorized when invalidCredentialsOnUnauthorized => ApiCallStatus.InvalidCredentials,
            HttpStatusCode.Unauthorized => ApiCallStatus.Unauthorized,
            HttpStatusCode.Forbidden => ApiCallStatus.Forbidden,
            HttpStatusCode.NotFound => ApiCallStatus.NotFound,
            HttpStatusCode.TooManyRequests => ApiCallStatus.RateLimited,
            HttpStatusCode.Conflict => ApiCallStatus.Conflict,
            _ when (int)statusCode >= 500 => ApiCallStatus.Unavailable,
            _ => ApiCallStatus.InvalidRequest
        };

    private static async Task<(string? Title, string? Detail)> ReadProblemAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            if (stream.CanSeek && stream.Length == 0)
            {
                return (null, null);
            }

            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            string? title = null;
            string? detail = null;
            if (document.RootElement.TryGetProperty("title", out var titleNode) && titleNode.ValueKind == JsonValueKind.String)
            {
                title = titleNode.GetString();
            }

            if (document.RootElement.TryGetProperty("detail", out var detailNode) && detailNode.ValueKind == JsonValueKind.String)
            {
                detail = detailNode.GetString();
            }

            return (title, detail);
        }
        catch (JsonException)
        {
            return (null, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (IOException)
        {
            return (null, null);
        }
    }

    private static HttpRequestMessage CreateAuthorized(HttpMethod method, string path, string token)
    {
        var message = new HttpRequestMessage(method, path);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return message;
    }
}
