using System.Globalization;
using Odca.Web.Localization;

namespace Odca.Web.Middleware;

/// <summary>
/// Resolve a cultura ativa a partir do cookie "odca_lang" e expõe em HttpContext.Items["culture_code"].
/// Fallback: pt-BR. Também aplica CultureInfo para formatação de datas/números nas views.
/// </summary>
public sealed class CultureMiddleware
{
    public const string CookieName = "odca_lang";
    public const string ItemsKey = "culture_code";

    private readonly RequestDelegate _next;
    private readonly LocalizationService _localizer;

    public CultureMiddleware(RequestDelegate next, LocalizationService localizer)
    {
        _next = next;
        _localizer = localizer;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var preferred = context.Request.Cookies[CookieName];
        var code = LocalizationService.ResolveCultureCode(preferred) ?? "pt-BR";
        context.Items[ItemsKey] = code;

        // Set thread culture for date/number formatting in views
        try
        {
            var ci = CultureInfo.GetCultureInfo(code);
            CultureInfo.CurrentCulture = ci;
            CultureInfo.CurrentUICulture = ci;
        }
        catch { /* fall through with default */ }

        await _next(context);
    }

    /// <summary>Gets the active culture code from HttpContext.</summary>
    public static string GetCulture(HttpContext context) =>
        (string?)context.Items[ItemsKey] ?? "pt-BR";
}
