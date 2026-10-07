namespace Odca.Api;

/// <summary>
/// Bloqueia no servidor os recursos que não devem estar disponíveis neste ciclo de liberação
/// (jornada documental MVP): importações de contratos, envio de arquivos para contratos e a
/// extração automática de dados (OCR). O bloqueio é por versão, não por organização, plano ou
/// permissão, e é controlado pela seção "Features" da configuração da API. Falha fechado: sem
/// habilitação explícita em "true", o recurso responde 503 problem+json antes de qualquer regra
/// de suspensão, módulo, franquia ou permissão. Consultas de leitura fora dessas superfícies
/// (listas, download de versões já aprovadas) permanecem disponíveis.
/// </summary>
public sealed class ReleaseFeatureGate(IConfiguration configuration) : IMiddleware
{
    public const string ContractImports = "ContractImports";
    public const string DocumentUpload = "DocumentUpload";
    public const string OcrExtraction = "OcrExtraction";

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (!TryResolveFeature(context.Request.Path.Value, context.Request.Method, out var feature))
        {
            await next(context);
            return;
        }

        if (configuration.GetValue($"Features:{feature}", false))
        {
            await next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(new
        {
            type = "https://odca.local/problems/release-feature-disabled",
            title = feature switch
            {
                ContractImports => "A Central de importações não está disponível nesta versão.",
                DocumentUpload => "O envio de arquivos para contratos não está disponível nesta versão.",
                _ => "A extração automática de dados não está disponível nesta versão."
            },
            status = StatusCodes.Status503ServiceUnavailable,
            detail = "O recurso será habilitado em uma versão futura. Documentos, dados e histórico já existentes permanecem preservados.",
            code = "release_feature_disabled",
            feature
        });
    }

    private static bool TryResolveFeature(string? path, string method, out string feature)
    {
        feature = string.Empty;
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
        var tenantToken = slash < 0 ? rest : rest[..slash];
        if (!Guid.TryParse(tenantToken, out _))
        {
            return false;
        }

        var remainder = slash < 0 ? string.Empty : rest[(slash + 1)..];
        var segments = remainder.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            return false;
        }

        if (segments[0] == "contract-imports")
        {
            feature = ContractImports;
            return true;
        }

        if (segments.Length >= 3 && segments[0] == "contracts" && segments[2] == "documents")
        {
            if (segments.Any(segment => segment == "extractions"))
            {
                feature = OcrExtraction;
                return true;
            }

            if (segments.Length == 3 && HttpMethods.IsPost(method))
            {
                feature = DocumentUpload;
                return true;
            }
        }

        return false;
    }
}
