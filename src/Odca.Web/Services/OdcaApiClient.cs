using System.Net.Http.Headers;
using System.Net.Http.Json;
using Odca.Contracts.Identity;
using Odca.Contracts.Plans;

namespace Odca.Web.Services;

public sealed class OdcaApiClient(HttpClient client)
{
    public async Task<ApiCallResult<LoginResponse>> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await client.PostAsJsonAsync("api/v1/auth/login", request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return new(ApiCallStatus.Success, await response.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken));
            }

            return new(response.StatusCode switch
            {
                System.Net.HttpStatusCode.Unauthorized => ApiCallStatus.InvalidCredentials,
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

    public async Task<(LoginResponse? Response, string[] Errors)> ChangePasswordAsync(
        string accessToken,
        ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        using var message = CreateAuthorized(HttpMethod.Post, "api/v1/auth/change-password", accessToken);
        message.Content = JsonContent.Create(request);
        using var response = await client.SendAsync(message, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return (await response.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken), []);
        }

        return (null, ["Não foi possível alterar a senha. Confira a senha atual e os requisitos informados."]);
    }

    public async Task<DashboardResponse?> GetDashboardAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var message = CreateAuthorized(HttpMethod.Get, "api/v1/platform/dashboard", accessToken);
        using var response = await client.SendAsync(message, cancellationToken);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<DashboardResponse>(cancellationToken)
            : null;
    }

    public async Task<IReadOnlyList<PlanCatalogResponse>?> GetPlansAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var message = CreateAuthorized(HttpMethod.Get, "api/v1/platform/plans", accessToken);
        using var response = await client.SendAsync(message, cancellationToken);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<PlanCatalogResponse[]>(cancellationToken)
            : null;
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
