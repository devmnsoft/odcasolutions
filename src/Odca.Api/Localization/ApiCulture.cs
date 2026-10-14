namespace Odca.Api.Localization;

/// <summary>
/// D-OC27: resolve a cultura ativa de uma requisição de API a partir do header
/// X-Odca-Culture, adiantado pelo BFF Web a partir do cookie odca_lang
/// (mesma normalização usada pelo CultureMiddleware, via LocalizationService).
/// Fallback: pt-BR — a camada canônica permanece pt-BR; isto é apenas apresentação.
/// </summary>
public static class ApiCulture
{
    public const string HeaderName = "X-Odca-Culture";

    public static string For(Microsoft.AspNetCore.Http.HttpContext context) =>
        Odca.Configuration.Localization.LocalizationService.ResolveCultureCode(
            context.Request.Headers[HeaderName].ToString()) ?? "pt-BR";
}
