using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Odca.AI;
using Odca.Api;

namespace Odca.IntegrationTests;

// D-OC52 (Sprint 18): unidades puras das fundações de IA — parser defensivo, provedores
// (null/http com handler falso), seleção de DI e resolução de caminho do gate de liberação.
// Nenhum teste toca rede nem banco: o provedor http conversa com um HttpMessageHandler falso.
public sealed class AiResponseParserTests
{
    [Fact]
    public async Task ParseShapesValidJsonIntoAnalysis()
    {
        const string raw = "{\"summary\":\"Resumo executivo.\",\"overall_risk\":\"alto\",\"categories\":[\"servicos\"],\"clauses\":[{\"code\":\"penalty\",\"risk\":\"baixo\",\"evidence\":\"multa de 2% ao mes\"}]}";

        var result = await Task.Run(() => AiResponseParser.Parse(raw, "modelo-teste"));

        Assert.NotNull(result);
        Assert.True(result!.Available);
        Assert.Null(result.Reason);
        Assert.Equal("Resumo executivo.", result.Summary);
        Assert.Equal("high", result.OverallRisk);
        Assert.Equal("servicos", Assert.Single(result.Categories));
        var finding = Assert.Single(result.Findings);
        Assert.Equal("penalty", finding.Code);
        Assert.Equal("low", finding.Risk);
        Assert.Equal("multa de 2% ao mes", finding.Evidence);
        Assert.Equal("modelo-teste", result.ProviderName);
    }

    [Fact]
    public void ParseStripsCodeFenceAroundJson()
    {
        var raw = "```json\n{\"summary\":\"Resumo\"}\n```";

        var result = AiResponseParser.Parse(raw, "p");

        Assert.NotNull(result);
        Assert.True(result!.Available);
        Assert.Equal("Resumo", result.Summary);
    }

    [Fact]
    public void ParseInvalidJsonReturnsNull() => Assert.Null(AiResponseParser.Parse("{isso nao eh json", "p"));

    [Fact]
    public void ParseNoUsableContentReturnsNull()
    {
        Assert.Null(AiResponseParser.Parse("{}", "p"));
        Assert.Null(AiResponseParser.Parse("{\"summary\":\"\",\"clauses\":[],\"categories\":[]}", "p"));
        Assert.Null(AiResponseParser.Parse(string.Empty, "p"));
    }

    [Fact]
    public void ParseClampsSummaryAndEvidenceToMaximumChars()
    {
        var summary = new string('a', 2_000);
        var evidence = new string('b', 900);
        var raw = JsonSerializer.Serialize(new { summary, overall_risk = (string?)null, categories = (string[])[], clauses = new[] { new { code = "penalty", risk = "low", evidence } } });

        var result = AiResponseParser.Parse(raw, "p");

        Assert.NotNull(result);
        Assert.Equal(AiResponseParser.MaximumSummaryChars, result!.Summary!.Length);
        Assert.Equal(AiResponseParser.MaximumEvidenceChars, Assert.Single(result.Findings).Evidence.Length);
    }

    [Fact]
    public void ParseLimitsFindingsToMaximumAndDedupesCategories()
    {
        var clauses = new StringBuilder();
        for (var i = 0; i < AiResponseParser.MaximumFindings + 3; i++)
        {
            if (i > 0) clauses.Append(',');
            clauses.Append("{\"code\":\"penalty\",\"risk\":\"low\",\"evidence\":\"e" + i + "\"}");
        }
        var categories = new StringBuilder();
        for (var i = 0; i < AiResponseParser.MaximumCategories + 2; i++)
        {
            if (i > 0) categories.Append(',');
            categories.Append("\"cat" + i + "\"");
        }
        var raw = $"{{\"summary\":\"s\",\"clauses\":[{clauses}],\"categories\":[{categories}]}}";

        var result = AiResponseParser.Parse(raw, "p");

        Assert.NotNull(result);
        Assert.Equal(AiResponseParser.MaximumFindings, result!.Findings.Count);
        Assert.Equal(AiResponseParser.MaximumCategories, result.Categories.Count);
    }

    [Fact]
    public void NormalizeRiskMapsDeclaredValuesAndPortuguese()
    {
        Assert.Equal("low", AiResponseParser.NormalizeRisk("LOW"));
        Assert.Equal("low", AiResponseParser.NormalizeRisk("baixo"));
        Assert.Equal("medium", AiResponseParser.NormalizeRisk("Médio"));
        Assert.Equal("medium", AiResponseParser.NormalizeRisk("media"));
        Assert.Equal("high", AiResponseParser.NormalizeRisk("Alto"));
        Assert.Null(AiResponseParser.NormalizeRisk(null));
        Assert.Null(AiResponseParser.NormalizeRisk("  "));
    }

    [Fact]
    public void NormalizeRiskUnknownValueNeverInflatesToHigh() => Assert.Equal("medium", AiResponseParser.NormalizeRisk("catastrofico"));

    [Fact]
    public void NormalizeCodeProducesStableSlug()
    {
        Assert.Equal("preo_alterao", AiResponseParser.NormalizeCode("Preço Alteração!"));
        Assert.Equal("data-protection.2", AiResponseParser.NormalizeCode("  DATA-protection.2 "));
        Assert.Null(AiResponseParser.NormalizeCode(null));
        Assert.Null(AiResponseParser.NormalizeCode("!!!"));
    }
}

public sealed class AiProviderUnitTests
{
    private const string Endpoint = "https://ai.test/v1/";
    private const string ApiKey = "chave-teste";

    [Fact]
    public async Task NullAiServiceDeclaresUnavailableWithoutSideEffects()
    {
        var result = await new NullAiService().AnalyzeContractAsync(new AiContractInput("texto", 5, "pt-BR"), CancellationToken.None);

        Assert.False(result.Available);
        Assert.Equal("provider_unavailable", result.Reason);
        Assert.Equal("none", result.ProviderName);
        Assert.Empty(result.Findings);
        Assert.Empty(result.Categories);
    }

    [Fact]
    public async Task HttpAiServicePostsToChatCompletionsAndParsesContent()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, ChatCompletions("{\"summary\":\"Resumo\",\"overall_risk\":\"low\",\"categories\":[\"seguro\"],\"clauses\":[]}"));
        var service = CreateService(handler, new AiOptions { Provider = "http", Endpoint = Endpoint, ApiKey = ApiKey, Model = "modelo-teste" });

        var result = await service.AnalyzeContractAsync(new AiContractInput("Contrato de teste.", 17, "pt-BR"), CancellationToken.None);

        Assert.True(result.Available);
        Assert.Equal("modelo-teste", result.ProviderName);
        Assert.Equal("Resumo", result.Summary);
        Assert.NotNull(handler.LastRequest);
        Assert.Equal(new Uri(Endpoint + "chat/completions"), handler.LastRequest!.RequestUri);
        Assert.Equal("Bearer", handler.LastRequest.Headers.Authorization?.Scheme);
        Assert.Equal(ApiKey, handler.LastRequest.Headers.Authorization?.Parameter);
        Assert.Contains("modelo-teste", handler.LastBody);
        Assert.Contains("Contrato de teste.", handler.LastBody);
    }

    [Fact]
    public async Task HttpAiServiceTruncatesInputAtMaxInputChars()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, ChatCompletions("{\"summary\":\"ok\"}"));
        var service = CreateService(handler, new AiOptions { Provider = "http", Endpoint = Endpoint, ApiKey = ApiKey, MaxInputChars = 1_000 });
        var text = new string('a', 2_997) + "ZZZ";

        await service.AnalyzeContractAsync(new AiContractInput(text, text.Length, "pt-BR"), CancellationToken.None);

        Assert.NotNull(handler.LastBody);
        Assert.Contains(new string('a', 100), handler.LastBody!);
        Assert.DoesNotContain("ZZZ", handler.LastBody);
    }

    [Fact]
    public async Task HttpAiServiceDegradesToUnavailableOnHttpError()
    {
        var service = CreateService(new FakeHandler(HttpStatusCode.InternalServerError, "{}"), new AiOptions { Provider = "http", Endpoint = Endpoint, ApiKey = ApiKey, Model = "m" });

        var result = await service.AnalyzeContractAsync(new AiContractInput("texto", 5, "pt-BR"), CancellationToken.None);

        Assert.False(result.Available);
        Assert.Equal("provider_unavailable", result.Reason);
    }

    [Fact]
    public async Task HttpAiServiceDegradesToUnavailableOnUnparseableContent()
    {
        var service = CreateService(new FakeHandler(HttpStatusCode.OK, ChatCompletions("isso nao e json")), new AiOptions { Provider = "http", Endpoint = Endpoint, ApiKey = ApiKey });

        var result = await service.AnalyzeContractAsync(new AiContractInput("texto", 5, "pt-BR"), CancellationToken.None);

        Assert.False(result.Available);
        Assert.Equal("provider_unavailable", result.Reason);
    }

    [Fact]
    public async Task HttpAiServiceDegradesToUnavailableOnTimeout()
    {
        var client = new HttpClient(new SlowHandler()) { BaseAddress = new Uri(Endpoint), Timeout = TimeSpan.FromMilliseconds(300) };
        var service = new HttpAiService(client, Options.Create(new AiOptions { Provider = "http", Endpoint = Endpoint, ApiKey = ApiKey }));

        var result = await service.AnalyzeContractAsync(new AiContractInput("texto", 5, "pt-BR"), CancellationToken.None);

        Assert.False(result.Available);
        Assert.Equal("provider_unavailable", result.Reason);
    }

    [Fact]
    public void SystemPromptNamesTargetLanguagePerCulture()
    {
        Assert.Contains("português do Brasil", HttpAiService.SystemPrompt("pt-BR"));
        Assert.Contains("inglês", HttpAiService.SystemPrompt("en-US"));
        Assert.Contains("espanhol", HttpAiService.SystemPrompt("es-ES"));
        Assert.Contains("francês", HttpAiService.SystemPrompt("fr-FR"));
        Assert.Contains("price_change", HttpAiService.SystemPrompt("pt-BR"));
    }

    [Fact]
    public void ExtractMessageContentReadsChatPayloadAndToleratesMalformedBodies()
    {
        Assert.Equal("conteudo", HttpAiService.ExtractMessageContent(ChatCompletions("conteudo")));
        Assert.Null(HttpAiService.ExtractMessageContent("{garbage"));
        Assert.Null(HttpAiService.ExtractMessageContent("{\"choices\":[]}"));
        Assert.Null(HttpAiService.ExtractMessageContent("[]"));
    }

    private static HttpAiService CreateService(HttpMessageHandler handler, AiOptions options) =>
        new(new HttpClient(handler) { BaseAddress = new Uri(Endpoint) }, Options.Create(options));

    private static string ChatCompletions(string content) =>
        JsonSerializer.Serialize(new { choices = new[] { new { message = new { content } } } });

    private sealed class FakeHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastBody = request.Content is null ? null : request.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8) });
        }
    }

    // Ignora o cancellation internamente: quem dispara é o Timeout do HttpClient, que converte em TaskCanceledException.
    private sealed class SlowHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        }
    }
}

public sealed class OdcaAiRegistrationTests
{
    [Fact]
    public void AddOdcaAiDefaultsToNullProviderWithoutConfiguration()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>());

        Assert.IsType<NullAiService>(provider.GetRequiredService<IAiService>());
    }

    [Fact]
    public void AddOdcaAiSelectsHttpProviderOnlyWhenFullyConfigured()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Ai:Provider"] = "http",
            ["Ai:Endpoint"] = "https://ai.test/v1",
            ["Ai:ApiKey"] = "chave",
            ["Ai:Model"] = "modelo"
        });

        Assert.IsType<HttpAiService>(provider.GetRequiredService<IAiService>());
    }

    [Fact]
    public void AddOdcaAiFallsBackToNullWhenEndpointOrKeyMissing()
    {
        using var missingKey = BuildProvider(new Dictionary<string, string?> { ["Ai:Provider"] = "http", ["Ai:Endpoint"] = "https://ai.test/v1" });
        using var missingEndpoint = BuildProvider(new Dictionary<string, string?> { ["Ai:Provider"] = "http", ["Ai:ApiKey"] = "chave" });
        using var invalidEndpoint = BuildProvider(new Dictionary<string, string?> { ["Ai:Provider"] = "http", ["Ai:Endpoint"] = "https://", ["Ai:ApiKey"] = "chave" });

        Assert.IsType<NullAiService>(missingKey.GetRequiredService<IAiService>());
        Assert.IsType<NullAiService>(missingEndpoint.GetRequiredService<IAiService>());
        Assert.IsType<NullAiService>(invalidEndpoint.GetRequiredService<IAiService>());
    }

    [Fact]
    public void AddOdcaAiBindsOptionsFromSection()
    {
        using var provider = BuildProvider(new Dictionary<string, string?> { ["Ai:TimeoutSeconds"] = "45", ["Ai:MaxInputChars"] = "12000" });

        var options = provider.GetRequiredService<IOptions<AiOptions>>().Value;

        Assert.Equal(45, options.TimeoutSeconds);
        Assert.Equal(12_000, options.MaxInputChars);
    }

    private static ServiceProvider BuildProvider(Dictionary<string, string?> configuration)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOdcaAi(new ConfigurationBuilder().AddInMemoryCollection(configuration).Build());
        return services.BuildServiceProvider();
    }
}

public sealed class ReleaseFeatureGatePathTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Contract = Guid.NewGuid();
    private static readonly Guid Document = Guid.NewGuid();
    private static readonly Guid Version = Guid.NewGuid();

    [Fact]
    public void GateResolvesAiAnalysisForVersionScopedRoute()
    {
        var resolved = ReleaseFeatureGate.TryResolveFeature(
            $"/api/v1/organizations/{Tenant}/contracts/{Contract}/documents/{Document}/versions/{Version}/ai-analysis",
            HttpMethods.Get, out var feature);

        Assert.True(resolved);
        Assert.Equal(ReleaseFeatureGate.AiAnalysis, feature);
    }

    [Fact]
    public void GateKeepsExtractionsResolutionPriority()
    {
        var resolved = ReleaseFeatureGate.TryResolveFeature(
            $"/api/v1/organizations/{Tenant}/contracts/{Contract}/documents/extractions/{Guid.NewGuid()}",
            HttpMethods.Get, out var feature);

        Assert.True(resolved);
        Assert.Equal(ReleaseFeatureGate.OcrExtraction, feature);
    }

    [Fact]
    public void GateKeepsDocumentUploadResolution()
    {
        var resolved = ReleaseFeatureGate.TryResolveFeature(
            $"/api/v1/organizations/{Tenant}/contracts/{Contract}/documents",
            HttpMethods.Post, out var feature);

        Assert.True(resolved);
        Assert.Equal(ReleaseFeatureGate.DocumentUpload, feature);
    }

    [Fact]
    public void GateIgnoresOtherContractsRoutesAndNonApiPaths()
    {
        Assert.False(ReleaseFeatureGate.TryResolveFeature($"/api/v1/organizations/{Tenant}/contracts/{Contract}/events", HttpMethods.Get, out _));
        Assert.False(ReleaseFeatureGate.TryResolveFeature($"organizações/{Tenant}/contratos/{Contract}", HttpMethods.Get, out _));
        Assert.False(ReleaseFeatureGate.TryResolveFeature("/api/v1/organizations/{Tenant}/contracts/{Contract}/documents/{Document}/versions/{Version}", HttpMethods.Get, out _));
    }
}
