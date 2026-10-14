using System.Security.Claims;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Odca.Api.Localization;
using Odca.Configuration.Localization;

namespace Odca.Api;

public sealed class OrganizationFeatureGate(NpgsqlDataSource dataSource) : IMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        // D-OC28: título/detalhe dos gates resolvidos por catálogo na cultura do usuário.
        var localizer = context.RequestServices.GetRequiredService<LocalizationService>();
        var culture = ApiCulture.For(context);
        var path = context.Request.Path.Value;
        var operations = context.GetEndpoint()?.Metadata.GetOrderedMetadata<TenantOperationAttribute>()
            ?? Array.Empty<TenantOperationAttribute>();
        var feature = operations.Select(item => item.Feature).LastOrDefault(item => !string.IsNullOrWhiteSpace(item))
            ?? Odca.Application.Administration.OrganizationFeatureCatalog.ResolveApiPath(path);
        var permission = operations.Select(item => item.Permission).LastOrDefault(item => !string.IsNullOrWhiteSpace(item));
        if (!TryReadTenant(path, out var tenantId) || !Guid.TryParse(context.User.FindFirstValue("sub"), out var actorId))
        {
            await next(context);
            return;
        }

        if (IsFeatureCatalog(path))
        {
            await next(context);
            return;
        }

        GateRow? gate;
        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(context.RequestAborted);
            gate = await connection.QuerySingleOrDefaultAsync<GateRow>(new CommandDefinition(
                """
                SELECT state AS State, reason AS Reason
                  FROM odca.organization_operation_gate(@actor, @tenant, @feature)
                """,
                new { actor = actorId, tenant = tenantId, feature },
                cancellationToken: context.RequestAborted));
        }
        catch (NpgsqlException)
        {
            await WriteProblem(
                context,
                "feature_check_failed",
                localizer.T("api.gate.access_incomplete", culture),
                localizer.T("api.gate.access_incomplete_suspension", culture));
            return;
        }

        if (gate is null || gate.State is "skip")
        {
            await next(context);
            return;
        }

        if (gate.State is "allowed" && !string.IsNullOrWhiteSpace(permission))
        {
            var permitted = await HasPermissionAsync(dataSource, context, actorId, tenantId, permission);
            if (permitted is null)
            {
                await WriteProblem(
                    context,
                    "feature_check_failed",
                    localizer.T("api.gate.access_incomplete", culture),
                    localizer.T("api.gate.access_incomplete_permission", culture));
                return;
            }

            if (!permitted.Value)
            {
                await WriteProblem(
                    context,
                    "permission_denied",
                    localizer.T("api.gate.permission_title", culture),
                    localizer.T("api.gate.permission_detail", culture));
                return;
            }
        }

        if (gate.State is "allowed")
        {
            await next(context);
            return;
        }

        var name = feature is null
            ? localizer.T("api.gate.feature_name_generic", culture)
            : LocalizeFeatureName(localizer, culture, feature);
        switch (gate.State)
        {
            case "organization_suspended":
                await WriteProblem(
                    context,
                    "organization_suspended",
                    localizer.T("api.gate.suspended_title", culture),
                    localizer.T("api.gate.suspended_detail", culture));
                return;
            case "membership_blocked":
                await WriteProblem(
                    context,
                    "membership_blocked",
                    localizer.T("api.gate.membership_blocked_title", culture),
                    localizer.T("api.gate.membership_blocked_detail", culture));
                return;
            case "administratively_blocked":
                await WriteProblem(
                    context,
                    "feature_blocked",
                    localizer.T("api.gate.feature_blocked_title", culture, name),
                    localizer.T("api.gate.feature_blocked_detail", culture, gate.Reason ?? string.Empty));
                return;
            case "plan_restricted":
                await WriteProblem(
                    context,
                    "plan_restricted",
                    localizer.T("api.gate.plan_restricted_title", culture, name),
                    localizer.T("api.gate.plan_restricted_detail", culture));
                return;
            case "quota_exhausted":
                await WriteProblem(
                    context,
                    "quota_exhausted",
                    localizer.T("api.gate.quota_exhausted_title", culture, name),
                    localizer.T("api.gate.quota_exhausted_detail", culture));
                return;
            default:
                await next(context);
                return;
        }
    }

    // D-OC28: rótulo da feature resolvido por catálogo (api.gate.feature_<código>);
    // código desconhecido volta ao DisplayName canônico PT (nada é inventado).
    private static string LocalizeFeatureName(LocalizationService localizer, string culture, string feature)
    {
        var key = "api.gate.feature_" + feature;
        var label = localizer.T(key, culture);
        return label == key ? Odca.Application.Administration.OrganizationFeatureCatalog.DisplayName(feature) : label;
    }

    private static bool TryReadTenant(string? path, out Guid tenantId)
    {
        tenantId = Guid.Empty;
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        const string marker = "/api/v1/organizations/";
        var index = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return false;
        }

        var rest = path[(index + marker.Length)..];
        var slash = rest.IndexOf('/');
        var token = slash < 0 ? rest : rest[..slash];
        return Guid.TryParse(token, out tenantId);
    }

    private static async Task<bool?> HasPermissionAsync(
        NpgsqlDataSource dataSource,
        HttpContext context,
        Guid actorId,
        Guid tenantId,
        string permission)
    {
        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(context.RequestAborted);
            return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                "SELECT odca.tenant_actor_has_permission(@actor, @tenant, @permission)",
                new { actor = actorId, tenant = tenantId, permission },
                cancellationToken: context.RequestAborted));
        }
        catch (NpgsqlException)
        {
            return null;
        }
    }

    private static bool IsFeatureCatalog(string? path) =>
        path is not null &&
        path.Contains("/features", StringComparison.OrdinalIgnoreCase) &&
        path.TrimEnd('/').EndsWith("/features", StringComparison.OrdinalIgnoreCase);

    private static async Task WriteProblem(HttpContext context, string code, string title, string detail)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(new
        {
            type = $"https://odca.local/problems/{code}",
            title,
            status = StatusCodes.Status403Forbidden,
            detail,
            code
        });
    }

    private sealed class GateRow
    {
        public string State { get; set; } = string.Empty;
        public string? Reason { get; set; }
    }
}
