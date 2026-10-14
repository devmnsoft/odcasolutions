using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Odca.Configuration.Localization;

namespace Odca.Api.Localization;

/// <summary>
/// D-OC27: fábrica de erros da API com código estável no wire e texto localizado.
/// O código (chave api.* do catálogo) viaja em ProblemDetails.extensions["code"]
/// e NUNCA muda com o idioma; o título/valor exibido é resolvido na cultura ativa
/// (header X-Odca-Culture). Os 21 códigos jsonb emitidos pelas funções canônicas
/// do PostgreSQL (ex.: solicitations.permission) são preservados como estão —
/// apenas o texto "error" é substituído pela tradução (api.sql.&lt;código&gt;),
/// mantendo o corpo canônico em pt-BR intacto.
/// </summary>
public static class OdcaErrors
{
    /// <summary>Cultura ativa da requisição. Chamado como this.Culture() nos controladores.</summary>
    public static string Culture(this ControllerBase controller) => ApiCulture.For(controller.HttpContext);

    /// <summary>Texto da chave na cultura ativa. Chamado como this.T("api.area.erro") nos controladores.</summary>
    public static string T(this ControllerBase controller, string key, params string[] args)
        => controller.Localizer().T(key, controller.Culture(), args);

    private static LocalizationService Localizer(this ControllerBase controller)
        => controller.HttpContext.RequestServices.GetRequiredService<LocalizationService>();

    private static string Text(this ControllerBase controller, string key, string[] args)
        => controller.Localizer().T(key, controller.Culture(), args);

    /// <summary>400 com mensagem específica. Código estável = chave do catálogo.</summary>
    public static IActionResult Invalid(this ControllerBase controller, string key, params string[] args)
        => controller.ValidationProblem(new ValidationProblemDetails
        {
            Title = controller.Text(key, args),
            Extensions = { ["code"] = key }
        });

    /// <summary>400 de validação com um único campo apontado.</summary>
    public static IActionResult Field(this ControllerBase controller, string field, string key, params string[] args)
    {
        var message = controller.Text(key, args);
        return controller.ValidationProblem(new ValidationProblemDetails
        {
            Title = message,
            Extensions = { ["code"] = key },
            Errors = { [field] = [message] }
        });
    }

    /// <summary>400 de validação com múltiplos campos já mapeados (campo → mensagem). Título genérico localizado.</summary>
    public static IActionResult Fields(this ControllerBase controller, string titleKey, IDictionary<string, string[]> errors, string? codeKey = null)
        => controller.ValidationProblem(new ValidationProblemDetails
        {
            Title = controller.Text(titleKey, []),
            Extensions = { ["code"] = codeKey ?? titleKey },
            Errors = errors
        });

    /// <summary>
    /// 400 com texto canônico conhecido (política/serviço): resolvido por ApiMessages
    /// (mapa reverso pt-BR canônico → chave); texto sem mapeamento volta como veio.
    /// O código wire usa a chave mapeada (estável) ou o genérico quando não há.
    /// </summary>
    public static IActionResult CanonicalValidationProblem(this ControllerBase controller, string? canonicalText)
    {
        var key = ApiMessages.KeyFor(canonicalText);
        var message = key is null ? canonicalText : controller.Text(key, ApiMessages.ArgsFor(canonicalText));
        return controller.ValidationProblem(new ValidationProblemDetails
        {
            Title = message,
            Extensions = { ["code"] = key ?? "api.validation.invalid_fields" }
        });
    }

    /// <summary>
    /// 400 de validação apontando um campo com texto canônico conhecido (exceção/serviço):
    /// resolvido por ApiMessages; texto sem mapeamento volta como veio. Código wire =
    /// chave mapeada (estável) ou o genérico quando não há.
    /// </summary>
    public static IActionResult CanonicalField(this ControllerBase controller, string field, string? canonicalText)
    {
        var key = ApiMessages.KeyFor(canonicalText);
        var message = key is null ? canonicalText : controller.Text(key, ApiMessages.ArgsFor(canonicalText));
        return controller.ValidationProblem(new ValidationProblemDetails
        {
            Title = message,
            Extensions = { ["code"] = key ?? "api.validation.invalid_fields" },
            Errors = { [field] = [message ?? string.Empty] }
        });
    }

    /// <summary>400 com campos/mensagens já localizados pelo chamador; código wire aditivo.</summary>
    public static IActionResult Errors(this ControllerBase controller, IDictionary<string, string[]> errors, string? codeKey = null)
        => controller.ValidationProblem(new ValidationProblemDetails
        {
            Extensions = { ["code"] = codeKey ?? "api.validation.invalid_fields" },
            Errors = errors
        });

    /// <summary>409 com mensagem específica.</summary>
    public static IActionResult Conflict(this ControllerBase controller, string key, params string[] args)
        => controller.Conflict(new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = controller.Text(key, args),
            Extensions = { ["code"] = key }
        });

    /// <summary>ProblemDetails com status arbitrário (ex.: 503/422) e mensagem localizada.</summary>
    public static ObjectResult Status(this ControllerBase controller, int statusCode, string key, params string[] args)
        => controller.StatusCode(statusCode, new Microsoft.AspNetCore.Mvc.ProblemDetails
        {
            Status = statusCode,
            Title = controller.Text(key, args),
            Extensions = { ["code"] = key }
        });

    /// <summary>
    /// Localiza o envelope de erro canônico { error, code } devolvido pelas funções do PostgreSQL.
    /// O par (código, mensagem) resolve por ApiSqlMessages; sem mapeamento o payload original
    /// (pt-BR canônico) volta como veio — nunca se inventa texto.
    /// </summary>
    public static JsonElement WithLocalizedError(this JsonElement payload, ControllerBase controller)
    {
        if (payload.ValueKind != JsonValueKind.Object) return payload;
        if (!payload.TryGetProperty("error", out var errorEl) || errorEl.ValueKind != JsonValueKind.String) return payload;
        if (!payload.TryGetProperty("code", out var codeEl) || codeEl.ValueKind != JsonValueKind.String) return payload;

        var key = ApiSqlMessages.KeyFor(codeEl.GetString()!, errorEl.GetString()!);
        if (key is null) return payload; // sem tradução registrada: mantém o canônico
        var translated = controller.Localizer().T(key, controller.Culture());
        if (translated == key) return payload;

        using var document = JsonDocument.Parse(payload.GetRawText());
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Name == "error")
                    writer.WriteString(property.Name, translated);
                else
                    property.WriteTo(writer);
            }
            writer.WriteEndObject();
        }
        stream.Position = 0;
        using var rewritten = JsonDocument.Parse(stream);
        return rewritten.RootElement.Clone();
    }
}
