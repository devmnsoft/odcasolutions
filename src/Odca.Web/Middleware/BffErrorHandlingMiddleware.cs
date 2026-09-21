using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Odca.Web.Middleware;

public sealed partial class BffErrorHandlingMiddleware(
    RequestDelegate next,
    ILogger<BffErrorHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);

            if (!context.Response.HasStarted &&
                (context.Response.StatusCode == StatusCodes.Status500InternalServerError ||
                 context.Response.StatusCode == StatusCodes.Status503ServiceUnavailable))
            {
                LogStatusError(context, context.Response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            LogExceptionError(context, ex);

            if (!context.Response.HasStarted)
            {
                await HandleErrorResponseAsync(context);
            }
        }
    }

    private async Task HandleErrorResponseAsync(HttpContext context)
    {
        var tenantId = ExtractTenantId(context);

        if (IsJsonRequest(context))
        {
            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsJsonAsync(new
            {
                title = "Página indisponível",
                detail = "Não foi possível abrir este recurso agora. O detalhe técnico foi registrado."
            });
            return;
        }

        try
        {
            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.SetEndpoint(null);
            context.Request.RouteValues.Clear();
            context.Request.Path = "/erro-indisponivel";
            if (tenantId.HasValue)
            {
                context.Request.QueryString = new QueryString($"?tenantId={tenantId.Value}");
            }

            await next(context);
        }
        catch (Exception renderingEx)
        {
            LogRenderError(logger, renderingEx, context.Request.Path.Value, tenantId, context.TraceIdentifier);

            if (!context.Response.HasStarted)
            {
                await WriteFallbackHtmlAsync(context, tenantId);
            }
        }
    }

    private static async Task WriteFallbackHtmlAsync(HttpContext context, Guid? tenantId)
    {
        context.Response.Clear();
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        context.Response.ContentType = "text/html; charset=utf-8";

        var tenantButton = tenantId.HasValue
            ? $"<a class=\"button button-primary\" href=\"/organizacoes/{tenantId.Value}/caixa\">Voltar à caixa</a>"
            : string.Empty;

        var html = $$"""
            <!DOCTYPE html>
            <html lang="pt-BR">
            <head>
                <meta charset="utf-8" />
                <meta name="viewport" content="width=device-width, initial-scale=1" />
                <title>Página indisponível — ODCA Solutions</title>
                <link rel="stylesheet" href="/css/site.css" />
            </head>
            <body class="public">
                <main class="page-shell">
                    <dialog id="unavailable-dialog" class="unavailable-dialog" role="alertdialog" open aria-labelledby="dialog-title" aria-describedby="dialog-desc">
                        <div class="dialog-content card">
                            <h2 id="dialog-title">Página indisponível</h2>
                            <p id="dialog-desc">Não foi possível abrir este recurso agora. O detalhe técnico foi registrado.</p>
                            <div class="dialog-actions">
                                <form method="dialog" style="display:inline;">
                                    <button type="submit" class="button button-quiet" id="btn-close-dialog">Fechar</button>
                                </form>
                                {{tenantButton}}
                            </div>
                        </div>
                    </dialog>
                </main>
            </body>
            </html>
            """;

        await context.Response.WriteAsync(html);
    }

    private void LogExceptionError(HttpContext context, Exception ex)
    {
        var exceptionType = ex.GetType().FullName;
        var message = ex.Message;
        var innerMessage = ex.InnerException?.Message;
        var path = context.Request.Path.Value;
        var tenantId = ExtractTenantId(context);
        var sub = ExtractSub(context);
        var requestId = context.TraceIdentifier;

        LogExceptionDetails(logger, ex, exceptionType, message, innerMessage, path, tenantId, sub, requestId);
    }

    private void LogStatusError(HttpContext context, int statusCode)
    {
        var path = context.Request.Path.Value;
        var tenantId = ExtractTenantId(context);
        var sub = ExtractSub(context);
        var requestId = context.TraceIdentifier;

        LogStatusDetails(logger, statusCode, path, tenantId, sub, requestId);
    }

    [LoggerMessage(
        EventId = 3001,
        Level = LogLevel.Error,
        Message = "Erro ao renderizar ServiceUnavailable (503): Path={Path}, TenantId={TenantId}, RequestId={RequestId}")]
    private static partial void LogRenderError(
        ILogger logger,
        Exception exception,
        string? path,
        Guid? tenantId,
        string requestId);

    [LoggerMessage(
        EventId = 3002,
        Level = LogLevel.Error,
        Message = "Erro de indisponibilidade (503): Tipo={ExceptionType}, Mensagem={Message}, Inner={InnerMessage}, Path={Path}, TenantId={TenantId}, Sub={Sub}, RequestId={RequestId}")]
    private static partial void LogExceptionDetails(
        ILogger logger,
        Exception exception,
        string? exceptionType,
        string message,
        string? innerMessage,
        string? path,
        Guid? tenantId,
        string? sub,
        string requestId);

    [LoggerMessage(
        EventId = 3003,
        Level = LogLevel.Error,
        Message = "Página indisponível ({StatusCode}): Path={Path}, TenantId={TenantId}, Sub={Sub}, RequestId={RequestId}")]
    private static partial void LogStatusDetails(
        ILogger logger,
        int statusCode,
        string? path,
        Guid? tenantId,
        string? sub,
        string requestId);

    public static Guid? ExtractTenantId(HttpContext context)
    {
        var routeValue = context.GetRouteValue("tenantId")?.ToString();
        if (Guid.TryParse(routeValue, out var fromRoute))
        {
            return fromRoute;
        }

        var queryValue = context.Request.Query["tenantId"].ToString();
        if (Guid.TryParse(queryValue, out var fromQuery))
        {
            return fromQuery;
        }

        var path = context.Request.Path.Value;
        if (!string.IsNullOrEmpty(path))
        {
            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < segments.Length - 1; i++)
            {
                if (string.Equals(segments[i], "organizacoes", StringComparison.OrdinalIgnoreCase) &&
                    Guid.TryParse(segments[i + 1], out var fromPath))
                {
                    return fromPath;
                }
            }
        }

        return null;
    }

    public static string? ExtractSub(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        return context.User.FindFirst("sub")?.Value
            ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    }

    private static bool IsJsonRequest(HttpContext context)
    {
        var accept = context.Request.Headers.Accept.ToString();
        var xRequestedWith = context.Request.Headers["X-Requested-With"].ToString();

        return accept.Contains("application/json", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(xRequestedWith, "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);
    }
}
