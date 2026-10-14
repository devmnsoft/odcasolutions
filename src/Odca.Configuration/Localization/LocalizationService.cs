using System.Globalization;
using System.Text.Json;

namespace Odca.Configuration.Localization;

/// <summary>Carrega os catálogos pt-BR/en-US/fr-FR/es-ES que viajam com cada app
/// (cópia de conteúdo em Localization/ na raiz do output) e resolve chaves com
/// fallback pt-BR. Camada canônica permanece pt-BR; isto é apenas apresentação.</summary>
public sealed class LocalizationService
{
    private static readonly string[] _supportedLanguages = { "pt-BR", "en-US", "fr-FR", "es-ES" };
    private static readonly CultureInfo FallbackCulture = CultureInfo.GetCultureInfo("pt-BR");
    private readonly Dictionary<string, Dictionary<string, string>> _catalogs = new();

    public LocalizationService()
    {
        var baseDir = Path.Combine(AppContext.BaseDirectory, "Localization");
        foreach (var lang in _supportedLanguages)
        {
            var path = Path.Combine(baseDir, $"{lang}.json");
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                _catalogs[lang] = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
            }
        }
    }

    /// <summary>Returns the whole resolved catalog for a supported culture (no fallback merge needed: catalogs are parity-checked).</summary>
    public IReadOnlyDictionary<string, string> Catalog(string? culture)
    {
        var code = ResolveCultureCode(culture) ?? "pt-BR";
        return _catalogs.TryGetValue(code, out var catalog) ? catalog : _catalogs["pt-BR"];
    }

    public string T(string key, string? culture = null, params string[] args)
    {
        var code = ResolveCultureCode(culture) ?? "pt-BR";
        if (_catalogs.TryGetValue(code, out var catalog) && catalog.TryGetValue(key, out var value))
            return ApplyArgs(value, args);
        // Fallback to pt-BR
        if (_catalogs.TryGetValue("pt-BR", out var fallback) && fallback.TryGetValue(key, out var fallbackValue))
            return ApplyArgs(fallbackValue, args);
        return key;
    }

    private static string ApplyArgs(string value, string[] args)
        => args.Length == 0 ? value : string.Format(CultureInfo.InvariantCulture, value, args);

    public static string? ResolveCultureCode(string? preferred)
    {
        if (string.IsNullOrWhiteSpace(preferred)) return "pt-BR";
        // Normalize: "en" → "en-US", "fr" → "fr-FR", "es" → "es-ES", "pt" → "pt-BR"
        var normalized = preferred.Trim().ToLowerInvariant().Replace('_', '-');
        if (normalized is "pt" or "pt-br") return "pt-BR";
        if (normalized is "en" or "en-us" or "en-gb") return "en-US";
        if (normalized is "fr" or "fr-fr") return "fr-FR";
        if (normalized is "es" or "es-es" or "es-mx") return "es-ES";
        return "pt-BR";
    }

    public static CultureInfo GetCulture(string? preferred)
    {
        try { return CultureInfo.GetCultureInfo(ResolveCultureCode(preferred) ?? "pt-BR"); }
        catch { return FallbackCulture; }
    }
}
