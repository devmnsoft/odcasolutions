using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Odca.Api.Localization;

namespace Odca.IntegrationTests;

/// <summary>D-OC27: cobertura do catálogo de erros contra a fonte da API — todo literal
/// api.*, mensagem canônica, template e sufixo MFA precisa resolver para uma chave que
/// existe nos quatro catálogos (pt-BR/en-US/fr-FR/es-ES).</summary>
public sealed class ApiLocalizationCoverageTests
{
    private static readonly string[] Cultures = ["pt-BR", "en-US", "fr-FR", "es-ES"];

    /// <summary>Literals api.* que não são chaves de catálogo: exemplo em comentário
    /// de documentação e os dois prefixos MFA concatenados em tempo de execução com o
    /// sufixo concreto.</summary>
    private static readonly string[] NonCatalogLiterals =
    [
        "api.area.erro", "api.auth.mfa_confirm_", "api.auth.mfa_challenge_"
    ];

    [Fact]
    public void EveryApiLiteralInTheSourceExistsInAllFourCatalogs()
    {
        var catalogs = LoadCatalogs();
        var scanned = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var file in new DirectoryInfo(Path.Combine(FindRepoRoot(), "src", "Odca.Api"))
            .EnumerateFiles("*.cs", SearchOption.AllDirectories))
        {
            foreach (Match match in Regex.Matches(File.ReadAllText(file.FullName), "\"(api\\.[a-z0-9_.]+)\""))
            {
                scanned.Add(match.Groups[1].Value);
            }
        }

        // Piso protege contra regressão silenciosa do próprio scanner.
        Assert.True(scanned.Count >= 400, $"esperado >= 400 literais api.*, encontrado {scanned.Count}");

        var missing = new List<string>();
        foreach (var key in scanned.Where(key => NonCatalogLiterals.All(non => non != key)))
        {
            foreach (var culture in Cultures)
            {
                if (!catalogs[culture].ContainsKey(key))
                {
                    missing.Add($"{key} ausente de {culture}");
                }
            }
        }

        Assert.Empty(missing);
    }

    [Fact]
    public void EverySqlErrorEnvelopeMapsToACatalogKeyInAllFourCatalogs()
    {
        var sql = File.ReadAllText(Path.Combine(FindRepoRoot(), "database", "odca.sql"));
        // O envelope pode carregar argumentos extras depois do código (ex.: currentRowVersion).
        var pairs = Regex.Matches(sql, "jsonb_build_object\\('error','([^']+)','code','([^']+)'")
            .Select(match => (Message: match.Groups[1].Value, Code: match.Groups[2].Value))
            .Distinct()
            .ToList();

        Assert.True(pairs.Count >= 37, $"esperado >= 37 envelopes jsonb de erro no SQL, encontrado {pairs.Count}");

        var catalogs = LoadCatalogs();
        var problems = new List<string>();

        foreach (var (message, code) in pairs)
        {
            var key = ApiSqlMessages.KeyFor(code, message);
            if (key is null)
            {
                problems.Add($"par sem mapeamento: {code}: \"{message}\"");
                continue;
            }

            foreach (var culture in Cultures)
            {
                if (!catalogs[culture].ContainsKey(key))
                {
                    problems.Add($"{key} ausente de {culture}");
                }
            }

            // pt-BR é a camada canônica: o catálogo deve exibir exatamente o que o banco diz.
            if (!catalogs["pt-BR"].TryGetValue(key, out var pt) || pt != message)
            {
                problems.Add($"deriva pt-BR em {key}: \"{pt ?? "(ausente)"}\" vs SQL \"{message}\"");
            }
        }

        Assert.Empty(problems);
    }

    [Fact]
    public void ApiMessagesKeysTemplatesAndMfaSuffixesExistInAllFourCatalogs()
    {
        var catalogs = LoadCatalogs();
        var problems = new List<string>();

        void Require(string key, string origin)
        {
            foreach (var culture in Cultures)
            {
                if (!catalogs[culture].ContainsKey(key))
                {
                    problems.Add($"{origin}: {key} ausente de {culture}");
                }
            }
        }

        var knownKeys = typeof(ApiMessages).GetField("KnownKeys", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null) as IDictionary<string, string>;
        Assert.NotNull(knownKeys);
        foreach (var key in knownKeys!.Values.Distinct(StringComparer.Ordinal))
        {
            Require(key, "KnownKeys");
        }

        var templatePatterns = typeof(ApiMessages).GetField("TemplatePatterns", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null) as Regex[];
        var templateKeys = typeof(ApiMessages).GetField("TemplateKeys", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null) as string[];
        Assert.NotNull(templatePatterns);
        Assert.NotNull(templateKeys);
        Assert.Equal(templatePatterns!.Length, templateKeys!.Length);

        for (var i = 0; i < templateKeys.Length; i++)
        {
            Require(templateKeys[i], $"Template[{i}]");

            // Os placeholders {n} da exibição pt-BR devem espelhar exatamente os grupos
            // capturantes do padrão canônico.
            var placeholders = Regex.Matches(catalogs["pt-BR"][templateKeys[i]], @"\{\d+\}")
                .Select(match => int.Parse(match.Value.Trim('{', '}'), CultureInfo.InvariantCulture))
                .ToHashSet();
            var expected = Enumerable.Range(0, templatePatterns[i]!.GetGroupNames().Length - 1).ToHashSet();
            if (!placeholders.SetEquals(expected))
            {
                problems.Add($"Template[{i}] {templateKeys[i]}: placeholders {{{string.Join(",", placeholders)}}} != grupos {{{string.Join(",", expected)}}}");
            }
        }

        var mfaSuffixes = typeof(ApiMessages).GetField("MfaSuffixes", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null) as IDictionary<string, string>;
        Assert.NotNull(mfaSuffixes);
        foreach (var suffix in mfaSuffixes!.Values.Distinct(StringComparer.Ordinal))
        {
            Require($"api.auth.mfa_confirm_{suffix}", "MFA confirm");
            Require($"api.auth.mfa_challenge_{suffix}", "MFA challenge");
        }

        Assert.Empty(problems);
    }

    private static Dictionary<string, Dictionary<string, string>> LoadCatalogs()
    {
        var catalogs = new Dictionary<string, Dictionary<string, string>>();
        foreach (var culture in Cultures)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Localization", $"{culture}.json");
            catalogs[culture] = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))!;
        }

        return catalogs;
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "src", "Odca.Api")) &&
                File.Exists(Path.Combine(current.FullName, "database", "odca.sql")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("raiz do repositório não localizada a partir de " + AppContext.BaseDirectory);
    }
}
