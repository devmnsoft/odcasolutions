using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
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
