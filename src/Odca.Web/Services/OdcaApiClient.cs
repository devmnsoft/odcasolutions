using System.Net.Http.Headers;
using System.Net.Http.Json;
using Odca.Contracts.Identity;
using Odca.Contracts.Onboarding;
using Odca.Contracts.Plans;
using Odca.Contracts.Privacy;

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

    private async Task<ApiCallResult<T>> SendAsync<T>(
        HttpRequestMessage message,
        bool invalidCredentialsOnUnauthorized,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await client.SendAsync(message, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var value = await response.Content.ReadFromJsonAsync<T>(cancellationToken);
                return value is null
                    ? new(ApiCallStatus.Unavailable)
                    : new(ApiCallStatus.Success, value);
            }

            return new(response.StatusCode switch
            {
                System.Net.HttpStatusCode.Unauthorized when invalidCredentialsOnUnauthorized => ApiCallStatus.InvalidCredentials,
                System.Net.HttpStatusCode.Unauthorized => ApiCallStatus.Unauthorized,
                System.Net.HttpStatusCode.Forbidden => ApiCallStatus.Forbidden,
                System.Net.HttpStatusCode.TooManyRequests => ApiCallStatus.RateLimited,
                _ when (int)response.StatusCode >= 500 => ApiCallStatus.Unavailable,
                _ => ApiCallStatus.InvalidRequest
            });
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

    private static HttpRequestMessage CreateAuthorized(HttpMethod method, string path, string token)
    {
        var message = new HttpRequestMessage(method, path);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return message;
    }
}
