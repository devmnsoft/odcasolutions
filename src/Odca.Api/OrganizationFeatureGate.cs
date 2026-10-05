using System.Security.Claims;
using Dapper;
using Npgsql;

namespace Odca.Api;

public sealed class OrganizationFeatureGate(NpgsqlDataSource dataSource) : IMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
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
                "A consulta de acesso não foi concluída.",
                "Não foi possível confirmar suspensão, bloqueio administrativo ou restrição de plano. A operação não foi executada.");
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
                    "A consulta de acesso não foi concluída.",
                    "Não foi possível confirmar a permissão da operação. A operação não foi executada.");
                return;
            }

            if (!permitted.Value)
            {
                await WriteProblem(
                    context,
                    "permission_denied",
                    "Permissão insuficiente.",
                    "O vínculo não possui permissão para esta ação. Ocultar o menu não substitui esta verificação.");
                return;
            }
        }

        if (gate.State is "allowed")
        {
            await next(context);
            return;
        }

        var name = feature is null
            ? "a operação"
            : Odca.Application.Administration.OrganizationFeatureCatalog.DisplayName(feature);
        switch (gate.State)
        {
            case "organization_suspended":
                await WriteProblem(
                    context,
                    "organization_suspended",
                    "A organização está suspensa.",
                    "Cadastros e documentos permanecem armazenados. As operações desta organização ficam indisponíveis até o superadministrador da plataforma restaurar o acesso. Este bloqueio é independente de permissão, vínculo e funcionalidade.");
                return;
            case "membership_blocked":
                await WriteProblem(
                    context,
                    "membership_blocked",
                    "O vínculo está bloqueado.",
                    "O acesso desta pessoa a esta organização está suspenso. Vínculos com outras organizações não são alterados.");
                return;
            case "administratively_blocked":
                await WriteProblem(
                    context,
                    "feature_blocked",
                    $"A funcionalidade {name} está bloqueada pela administração.",
                    $"Os dados permanecem preservados. A liberação é feita pelo superadministrador, com justificativa, e não altera plano, permissões nem outros bloqueios. Motivo registrado: {gate.Reason}");
                return;
            case "plan_restricted":
                await WriteProblem(
                    context,
                    "plan_restricted",
                    $"O módulo {name} não está contratado.",
                    "A habilitação do módulo é distinta da franquia quantitativa, do bloqueio administrativo e da permissão do perfil. A consulta não registra pagamento, cobrança ou quitação.");
                return;
            case "quota_exhausted":
                await WriteProblem(
                    context,
                    "quota_exhausted",
                    $"O limite de {name} foi atingido.",
                    "O módulo está contratado, mas a franquia vigente não permite esta operação. Pessoas, documentos e histórico não são apagados.");
                return;
            default:
                await next(context);
                return;
        }
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
