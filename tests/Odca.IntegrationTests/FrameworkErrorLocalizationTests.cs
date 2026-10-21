using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Odca.Configuration;

namespace Odca.IntegrationTests;

/// <summary>D-OC30: os erros emitidos pelo próprio framework — 429 do rate limiter e
/// 500 de exceção não tratada — seguem o catálogo via X-Odca-Culture com código estável
/// no wire. O 500 usa o caminho instalado pelo D-OC27 (CustomizeProblemDetails no
/// handler de exceção); este teste o fixa como contrato. O 429 tem, a partir deste
/// incremento, título/detalhe localizados + código estável (o .NET 10 gera o
/// problem+json pela fábrica registrada — o hook do D-OC27 alcança os dois status).
/// Coleção "Environment": o host Development define/restaura ODCA_RUNTIME_CONFIG
/// (mesma variável usada por LocalRuntimeConfigurationTests) — serializa com ela
/// pelo mesmo padrão da casa.</summary>
[Collection("Environment")]
public sealed class FrameworkErrorLocalizationTests(DatabaseFixture database) : IClassFixture<DatabaseFixture>
{
    [Theory]
    [InlineData("pt-BR", "Muitas requisições.", "O limite de requisições para este endereço foi atingido. Tente novamente em instantes.")]
    [InlineData("en-US", "Too many requests.", "The request limit for this address was reached. Please try again shortly.")]
    public async Task RateLimitedRequestIsPresentedInTheRequestedCulture(
        string culture, string expectedTitle, string expectedDetail)
    {
        await using var factory = database.CreateApi();
        using var client = factory.CreateClient();

        // A política login permite 5 requisições/minuto por IP: 5 logins inválidos
        // (401, já localizados pelo D-OC27) consomem a janela e a 6ª cai no 429.
        for (var i = 0; i < 5; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/auth/login")
            {
                Content = JsonContent.Create(new { login = $"d30-consume-{Guid.NewGuid():N}@odca.local", password = "Errada#D30" })
            };
            request.Headers.Add("X-Odca-Culture", culture);
            using var consumed = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Unauthorized, consumed.StatusCode);
        }

        using var limited = new HttpRequestMessage(HttpMethod.Post, "api/v1/auth/login")
        {
            Content = JsonContent.Create(new { login = $"d30-limited-{Guid.NewGuid():N}@odca.local", password = "Errada#D30" })
        };
        limited.Headers.Add("X-Odca-Culture", culture);
        using var response = await client.SendAsync(limited);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Contains("application/problem+json", response.Content.Headers.ContentType?.MediaType ?? string.Empty);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal(expectedTitle, root.GetProperty("title").GetString());
        Assert.Equal(expectedDetail, root.GetProperty("detail").GetString());
        Assert.Equal(429, root.GetProperty("status").GetInt32());
        Assert.Equal("rate_limited", root.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("pt-BR", "Erro interno do servidor.")]
    [InlineData("en-US", "Internal server error.")]
    public async Task UnhandledExceptionIsPresentedInTheRequestedCulture(
        string culture, string expectedTitle)
    {
        // Ambiente não-Testing: o Program.cs registra UseExceptionHandler() apenas fora
        // do Testing, e é esse caminho que produz o problem+json do 500. O endpoint de
        // diagnóstico /api/v1/diagnostics/fault (Development) lança exceção determinística.
        await using var factory = new DevelopmentFaultApiFactory(database);
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/diagnostics/fault");
        request.Headers.Add("X-Odca-Culture", culture);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal(expectedTitle, root.GetProperty("title").GetString());
        Assert.Equal(500, root.GetProperty("status").GetInt32());
        Assert.True(root.TryGetProperty("code", out var codeElement), "sem 'code' no wire");
        Assert.Equal("api.internal.error", codeElement.GetString());
    }

    /// <summary>Host em ambiente não-Testing (mesmo contrato ODCA_TEST_* do fixture) para
    /// exercitar o caminho real do 500 (UseExceptionHandler + CustomizeProblemDetails).
    /// D-OC58: hermeticidade em checkout limpo — o ambiente Development do Program exige o
    /// development-runtime.json (arquivo pessoal, gitignored: existe na máquina de
    /// desenvolvimento, ausente no runner da CI). O escape previsto pelo próprio produto é a
    /// variável ODCA_RUNTIME_CONFIG (LocalRuntimeConfiguration.ResolvePath); este host a aponta
    /// para um JSON temporário vazio, pois toda a configuração do host de teste já vem da
    /// coleção in-memory abaixo (que vence o arquivo, como no fluxo real). A variável é
    /// restaurada e o arquivo removido no Dispose.</summary>
    private sealed class DevelopmentFaultApiFactory : WebApplicationFactory<global::Odca.Api.ApiAssemblyMarker>
    {
        private readonly DatabaseFixture _fixture;
        private readonly string _runtimeConfigPath;
        private readonly string? _previousRuntimeConfig;

        public DevelopmentFaultApiFactory(DatabaseFixture fixture)
        {
            _fixture = fixture;
            _runtimeConfigPath = Path.Combine(
                Path.GetTempPath(), "odca-framework-error-tests", $"development-runtime-{Guid.NewGuid():N}.json");
            _previousRuntimeConfig = Environment.GetEnvironmentVariable(LocalRuntimeConfiguration.EnvironmentVariable);
            Directory.CreateDirectory(Path.GetDirectoryName(_runtimeConfigPath)!);
            File.WriteAllText(_runtimeConfigPath, "{}");
            Environment.SetEnvironmentVariable(LocalRuntimeConfiguration.EnvironmentVariable, _runtimeConfigPath);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            var settings = new Dictionary<string, string?>
            {
                ["ConnectionStrings:Database"] = _fixture.AppConnectionString,
                ["Jwt:Issuer"] = "odca-api-fault-tests",
                ["Jwt:Audience"] = "odca-api-fault-tests",
                ["Jwt:SigningKey"] = "fault-test-key-with-at-least-thirty-two-bytes",
                ["Jwt:AccessTokenMinutes"] = "15",
                ["Security:MfaRequiredForSuperAdmin"] = "true",
                ["Security:AllowDevelopmentBootstrap"] = "false",
                ["DataProtection:ApplicationName"] = "ODCA framework error tests",
                ["DataProtection:KeysPath"] = Path.Combine(Path.GetTempPath(), "odca-framework-error-tests", Guid.NewGuid().ToString("N"))
            };
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (!disposing) return;

            var variable = LocalRuntimeConfiguration.EnvironmentVariable;
            Environment.SetEnvironmentVariable(variable, _previousRuntimeConfig);
            try { File.Delete(_runtimeConfigPath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { } // limpeza best-effort do artefato temporário
        }
    }
}
