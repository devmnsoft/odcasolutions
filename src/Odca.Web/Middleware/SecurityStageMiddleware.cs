using System.Security.Claims;

namespace Odca.Web.Middleware;

/// <summary>
/// Forces the session progression before the panel: mandatory password change first,
/// then MFA enrollment/challenge for the super administrator. An authenticated cookie
/// without those steps does not authorize administrative operations on the API.
/// </summary>
public sealed class SecurityStageMiddleware(RequestDelegate next)
{
    private static readonly string[] AccountPaths =
    [
        "/entrar",
        "/sair",
        "/alterar-senha",
        "/mfa/inscricao",
        "/mfa/inscricao/regenerar",
        "/mfa/desafio",
        "/acesso-negado"
    ];

    private static readonly string[] StaticPrefixes =
    [
        "/css/",
        "/js/",
        "/lib/",
        "/favicon.ico"
    ];

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            await next(context);
            return;
        }

        var path = context.Request.Path.Value ?? string.Empty;
        if (IsExempt(path))
        {
            await next(context);
            return;
        }

        if (context.User.FindFirstValue("must_change_password") == "true")
        {
            context.Response.Redirect("/alterar-senha");
            return;
        }

        if (context.User.IsInRole("SuperAdministrator") && context.User.FindFirstValue("mfa_verified") != "true")
        {
            context.Response.Redirect(
                context.User.FindFirstValue("requires_mfa_enrollment") == "true" ? "/mfa/inscricao" : "/mfa/desafio");
            return;
        }

        await next(context);
    }

    private static bool IsExempt(string path) =>
        AccountPaths.Contains(path, StringComparer.OrdinalIgnoreCase)
        || StaticPrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
}
