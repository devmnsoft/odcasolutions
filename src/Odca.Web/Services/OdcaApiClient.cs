using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Odca.Contracts.Contracts;
using Odca.Contracts.Identity;
using Odca.Contracts.Onboarding;
using Odca.Contracts.Plans;
using Odca.Contracts.Privacy;
using Odca.Contracts.Tenancy;

namespace Odca.Web.Services;

public sealed class OdcaApiClient(HttpClient client)
{
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

    public Task<ApiCallResult<PaginatedResponse<ContractListItemResponse>>> GetContractsAsync(
        string token,
        Guid tenantId,
        string? title,
        Guid? counterpartyId,
        Guid? typeId,
        Guid? ownerUserId,
        string? operationalStatus,
        string? temporalStatus,
        DateOnly? endFrom,
        DateOnly? endTo,
        bool mine,
        bool approaching,
        bool unassigned,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query =
            $"title={Uri.EscapeDataString(title ?? string.Empty)}" +
            $"&counterpartyId={(counterpartyId is Guid c ? c.ToString() : string.Empty)}" +
            $"&typeId={(typeId is Guid t ? t.ToString() : string.Empty)}" +
            $"&ownerUserId={(ownerUserId is Guid o ? o.ToString() : string.Empty)}" +
            $"&operationalStatus={Uri.EscapeDataString(operationalStatus ?? string.Empty)}" +
            $"&temporalStatus={Uri.EscapeDataString(temporalStatus ?? string.Empty)}" +
            $"&endFrom={(endFrom is DateOnly ef ? ef.ToString("O", System.Globalization.CultureInfo.InvariantCulture) : string.Empty)}" +
            $"&endTo={(endTo is DateOnly et ? et.ToString("O", System.Globalization.CultureInfo.InvariantCulture) : string.Empty)}" +
            $"&mine={mine.ToString().ToLowerInvariant()}" +
            $"&approaching={approaching.ToString().ToLowerInvariant()}" +
            $"&unassigned={unassigned.ToString().ToLowerInvariant()}" +
            $"&page={page}&pageSize={pageSize}";
        return SendAsync<PaginatedResponse<ContractListItemResponse>>(
            CreateAuthorized(HttpMethod.Get, $"api/v1/organizations/{tenantId}/contracts?{query}", token),
            false,
            cancellationToken);
    }

    public Task<ApiCallResult<ContractOverviewResponse>> GetContractOverviewAsync(
        string token,
        Guid tenantId,
        CancellationToken cancellationToken)
        => SendAsync<ContractOverviewResponse>(
            CreateAuthorized(HttpMethod.Get, $"api/v1/organizations/{tenantId}/contracts/overview", token),
            false,
            cancellationToken);

    public Task<ApiCallResult<ContractDetailResponse>> GetContractAsync(
        string token,
        Guid tenantId,
        Guid id,
        CancellationToken cancellationToken)
        => SendAsync<ContractDetailResponse>(
            CreateAuthorized(HttpMethod.Get, $"api/v1/organizations/{tenantId}/contracts/{id}", token),
            false,
            cancellationToken);

    public async Task<ApiCallResult<ContractDetailResponse>> CreateContractAsync(
        string token,
        Guid tenantId,
        UpsertContractRequest request,
        CancellationToken cancellationToken)
    {
        using var message = CreateAuthorized(HttpMethod.Post, $"api/v1/organizations/{tenantId}/contracts", token);
        message.Content = JsonContent.Create(request);
        return await SendAsync<ContractDetailResponse>(message, false, cancellationToken);
    }

    public async Task<ApiCallResult<ContractDetailResponse>> UpdateContractAsync(
        string token,
        Guid tenantId,
        Guid id,
        UpsertContractRequest request,
        CancellationToken cancellationToken)
    {
        using var message = CreateAuthorized(HttpMethod.Put, $"api/v1/organizations/{tenantId}/contracts/{id}", token);
        message.Content = JsonContent.Create(request);
        return await SendAsync<ContractDetailResponse>(message, false, cancellationToken);
    }

    public async Task<ApiCallResult<bool>> ActivateContractAsync(
        string token,
        Guid tenantId,
        Guid id,
        ContractVersionRequest request,
        CancellationToken cancellationToken)
    {
        using var message = CreateAuthorized(HttpMethod.Post, $"api/v1/organizations/{tenantId}/contracts/{id}/activate", token);
        message.Content = JsonContent.Create(request);
        return await SendAsync<bool>(message, false, cancellationToken, emptyBodyIsSuccess: true);
    }

    public async Task<ApiCallResult<bool>> RenewContractAsync(
        string token,
        Guid tenantId,
        Guid id,
        RenewContractRequest request,
        CancellationToken cancellationToken)
    {
        using var message = CreateAuthorized(HttpMethod.Post, $"api/v1/organizations/{tenantId}/contracts/{id}/renew", token);
        message.Content = JsonContent.Create(request);
        return await SendAsync<bool>(message, false, cancellationToken, emptyBodyIsSuccess: true);
    }

    public async Task<ApiCallResult<bool>> CloseContractAsync(
        string token,
        Guid tenantId,
        Guid id,
        ContractVersionRequest request,
        CancellationToken cancellationToken)
    {
        using var message = CreateAuthorized(HttpMethod.Post, $"api/v1/organizations/{tenantId}/contracts/{id}/close", token);
        message.Content = JsonContent.Create(request);
        return await SendAsync<bool>(message, false, cancellationToken, emptyBodyIsSuccess: true);
    }

    public async Task<ApiCallResult<bool>> CancelContractAsync(
        string token,
        Guid tenantId,
        Guid id,
        ContractVersionRequest request,
        CancellationToken cancellationToken)
    {
        using var message = CreateAuthorized(HttpMethod.Post, $"api/v1/organizations/{tenantId}/contracts/{id}/cancel", token);
        message.Content = JsonContent.Create(request);
        return await SendAsync<bool>(message, false, cancellationToken, emptyBodyIsSuccess: true);
    }

    public async Task<ApiCallResult<bool>> SoftDeleteContractAsync(
        string token,
        Guid tenantId,
        Guid id,
        SoftDeleteContractRequest request,
        CancellationToken cancellationToken)
    {
        using var message = CreateAuthorized(HttpMethod.Post, $"api/v1/organizations/{tenantId}/contracts/{id}/delete", token);
        message.Content = JsonContent.Create(request);
        return await SendAsync<bool>(message, false, cancellationToken, emptyBodyIsSuccess: true);
    }

    public Task<ApiCallResult<PaginatedResponse<CounterpartyResponse>>> GetCounterpartiesAsync(
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
        return SendAsync<PaginatedResponse<CounterpartyResponse>>(
            CreateAuthorized(HttpMethod.Get, $"api/v1/organizations/{tenantId}/counterparties?{query}", token),
            false,
            cancellationToken);
    }

    public Task<ApiCallResult<CounterpartyResponse>> GetCounterpartyAsync(
        string token,
        Guid tenantId,
        Guid id,
        CancellationToken cancellationToken)
        => SendAsync<CounterpartyResponse>(
            CreateAuthorized(HttpMethod.Get, $"api/v1/organizations/{tenantId}/counterparties/{id}", token),
            false,
            cancellationToken);

    public async Task<ApiCallResult<CounterpartyResponse>> CreateCounterpartyAsync(
        string token,
        Guid tenantId,
        UpsertCounterpartyRequest request,
        CancellationToken cancellationToken)
    {
        using var message = CreateAuthorized(HttpMethod.Post, $"api/v1/organizations/{tenantId}/counterparties", token);
        message.Content = JsonContent.Create(request);
        return await SendAsync<CounterpartyResponse>(message, false, cancellationToken);
    }

    public async Task<ApiCallResult<CounterpartyResponse>> UpdateCounterpartyAsync(
        string token,
        Guid tenantId,
        Guid id,
        UpsertCounterpartyRequest request,
        CancellationToken cancellationToken)
    {
        using var message = CreateAuthorized(HttpMethod.Put, $"api/v1/organizations/{tenantId}/counterparties/{id}", token);
        message.Content = JsonContent.Create(request);
        return await SendAsync<CounterpartyResponse>(message, false, cancellationToken);
    }

    public async Task<ApiCallResult<bool>> InactivateCounterpartyAsync(
        string token,
        Guid tenantId,
        Guid id,
        ContractVersionRequest request,
        CancellationToken cancellationToken)
    {
        using var message = CreateAuthorized(
            HttpMethod.Post,
            $"api/v1/organizations/{tenantId}/counterparties/{id}/inactivate",
            token);
        message.Content = JsonContent.Create(request);
        return await SendAsync<bool>(message, false, cancellationToken, emptyBodyIsSuccess: true);
    }

    public Task<ApiCallResult<ContractTypeResponse[]>> GetContractTypesAsync(
        string token,
        Guid tenantId,
        string? status,
        CancellationToken cancellationToken)
    {
        var query = $"status={Uri.EscapeDataString(status ?? string.Empty)}";
        return SendAsync<ContractTypeResponse[]>(
            CreateAuthorized(HttpMethod.Get, $"api/v1/organizations/{tenantId}/contract-types?{query}", token),
            false,
            cancellationToken);
    }

    public Task<ApiCallResult<ContractTypeResponse>> GetContractTypeAsync(
        string token,
        Guid tenantId,
        Guid id,
        CancellationToken cancellationToken)
        => SendAsync<ContractTypeResponse>(
            CreateAuthorized(HttpMethod.Get, $"api/v1/organizations/{tenantId}/contract-types/{id}", token),
            false,
            cancellationToken);

    public async Task<ApiCallResult<ContractTypeResponse>> CreateContractTypeAsync(
        string token,
        Guid tenantId,
        UpsertContractTypeRequest request,
        CancellationToken cancellationToken)
    {
        using var message = CreateAuthorized(HttpMethod.Post, $"api/v1/organizations/{tenantId}/contract-types", token);
        message.Content = JsonContent.Create(request);
        return await SendAsync<ContractTypeResponse>(message, false, cancellationToken);
    }

    public async Task<ApiCallResult<ContractTypeResponse>> UpdateContractTypeAsync(
        string token,
        Guid tenantId,
        Guid id,
        UpsertContractTypeRequest request,
        CancellationToken cancellationToken)
    {
        using var message = CreateAuthorized(HttpMethod.Put, $"api/v1/organizations/{tenantId}/contract-types/{id}", token);
        message.Content = JsonContent.Create(request);
        return await SendAsync<ContractTypeResponse>(message, false, cancellationToken);
    }

    public async Task<ApiCallResult<bool>> SetContractTypeStatusAsync(
        string token,
        Guid tenantId,
        Guid id,
        SetContractTypeStatusRequest request,
        CancellationToken cancellationToken)
    {
        using var message = CreateAuthorized(
            HttpMethod.Post,
            $"api/v1/organizations/{tenantId}/contract-types/{id}/status",
            token);
        message.Content = JsonContent.Create(request);
        return await SendAsync<bool>(message, false, cancellationToken, emptyBodyIsSuccess: true);
    }

    public Task<ApiCallResult<PaginatedResponse<UserNotificationResponse>>> GetNotificationsAsync(
        string token,
        Guid tenantId,
        bool unreadOnly,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = $"unreadOnly={unreadOnly.ToString().ToLowerInvariant()}&page={page}&pageSize={pageSize}";
        return SendAsync<PaginatedResponse<UserNotificationResponse>>(
            CreateAuthorized(HttpMethod.Get, $"api/v1/organizations/{tenantId}/notifications?{query}", token),
            false,
            cancellationToken);
    }

    public Task<ApiCallResult<UnreadNotificationsResponse>> GetUnreadNotificationCountAsync(
        string token,
        Guid tenantId,
        CancellationToken cancellationToken)
        => SendAsync<UnreadNotificationsResponse>(
            CreateAuthorized(HttpMethod.Get, $"api/v1/organizations/{tenantId}/notifications/unread-count", token),
            false,
            cancellationToken);

    public async Task<ApiCallResult<bool>> MarkNotificationReadAsync(
        string token,
        Guid tenantId,
        Guid id,
        CancellationToken cancellationToken)
    {
        using var message = CreateAuthorized(
            HttpMethod.Post,
            $"api/v1/organizations/{tenantId}/notifications/{id}/read",
            token);
        return await SendAsync<bool>(message, false, cancellationToken, emptyBodyIsSuccess: true);
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
