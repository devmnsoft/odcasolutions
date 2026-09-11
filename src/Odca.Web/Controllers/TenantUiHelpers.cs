using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Odca.Contracts.Tenancy;
using Odca.Web.Services;

namespace Odca.Web.Controllers;

internal static class TenantUiHelpers
{
    public static Task<string?> AccessTokenAsync(HttpContext httpContext)
        => httpContext.GetTokenAsync("access_token");

    public static async Task<OrganizationSummary?> RequireOrganizationAsync(
        OdcaApiClient api,
        string token,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var orgs = await api.GetOrganizationsAsync(token, cancellationToken);
        if (!orgs.Succeeded)
        {
            return null;
        }

        return orgs.Value!.SingleOrDefault(x => x.Id == tenantId);
    }

    public static async Task<Guid?> ResolveDefaultTenantIdAsync(
        OdcaApiClient api,
        string token,
        CancellationToken cancellationToken)
    {
        var home = await api.GetCustomerHomeAsync(token, cancellationToken);
        if (home.Succeeded)
        {
            return home.Value!.TenantId;
        }

        var orgs = await api.GetOrganizationsAsync(token, cancellationToken);
        if (orgs.Succeeded && orgs.Value is { Length: 1 })
        {
            return orgs.Value[0].Id;
        }

        return null;
    }

    public static IActionResult MapReadFailure(Controller controller, ApiCallStatus status) => status switch
    {
        ApiCallStatus.Forbidden => controller.Forbid(),
        ApiCallStatus.Unauthorized => controller.Challenge(),
        _ => controller.View("~/Views/Shared/ServiceUnavailable.cshtml")
    };

    public static string UserFacingError<T>(ApiCallResult<T> result, string fallback) => result.Status switch
    {
        ApiCallStatus.Unauthorized => "Sua sessão expirou. Entre novamente.",
        ApiCallStatus.Forbidden => result.UserMessage("Você não tem permissão para esta ação."),
        ApiCallStatus.Conflict => result.UserMessage("A operação conflitou com outro estado. Recarregue e tente novamente."),
        ApiCallStatus.RateLimited => "Muitas tentativas. Aguarde e tente novamente.",
        ApiCallStatus.Timeout or ApiCallStatus.Unavailable => "O serviço está temporariamente indisponível.",
        ApiCallStatus.InvalidRequest => result.UserMessage(fallback),
        _ => result.UserMessage(fallback)
    };

    public static bool HasPermission(OrganizationSummary org, string permission)
        => org.Permissions.Contains(permission, StringComparer.Ordinal);

    public static void SetTenantContext(Controller controller, OrganizationSummary org, int? unreadCount = null)
    {
        controller.ViewData["OrganizationName"] = org.Name;
        controller.ViewData["TenantId"] = org.Id;
        if (unreadCount is int count)
        {
            controller.ViewData["UnreadNotifications"] = count;
        }
    }
}
