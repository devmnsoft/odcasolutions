using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Odca.IntegrationTests;

public sealed class ApiHostStartupTests
{
    [Fact]
    public void ApiMarkerIdentifiesTheApiExecutableAssembly()
    {
        var assembly = typeof(global::Odca.Api.ApiAssemblyMarker).Assembly;

        Assert.Equal("Odca.Api", assembly.GetName().Name);
        Assert.NotNull(assembly.EntryPoint);
        Assert.NotEqual(typeof(global::BootstrapProgram).Assembly, assembly);
    }

    [Fact]
    public async Task IsolatedConfigurationStartsTheHostAndServesARequest()
    {
        await using var factory = new ConfiguredApiFactory("Testing", ValidConfiguration());
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        using var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CommentStateRoutesBuildAndOnlyAcceptTheTwoAuthorizedPosts()
    {
        await using var factory = new ConfiguredApiFactory("Testing", ValidConfiguration());
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var dataSources = factory.Services.GetRequiredService<IEnumerable<EndpointDataSource>>();
        var routes = dataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().Select(endpoint => endpoint.RoutePattern.RawText).ToArray();
        const string prefix = "api/v1/organizations/{tenantId:guid}/studio/drafts/{draftId:guid}/comments/{commentId:guid}/";
        Assert.Contains(prefix + "resolve", routes);
        Assert.Contains(prefix + "reopen", routes);
        Assert.DoesNotContain(routes, route => route?.Contains("{action", StringComparison.OrdinalIgnoreCase) == true);

        var tenant = Guid.NewGuid(); var draft = Guid.NewGuid(); var comment = Guid.NewGuid();
        using var resolve = await client.PostAsJsonAsync($"/api/v1/organizations/{tenant}/studio/drafts/{draft}/comments/{comment}/resolve", new { expectedResolved = false });
        using var reopen = await client.PostAsJsonAsync($"/api/v1/organizations/{tenant}/studio/drafts/{draft}/comments/{comment}/reopen", new { expectedResolved = true, observation = "Reavaliar cláusula." });
        using var unknown = await client.PostAsJsonAsync($"/api/v1/organizations/{tenant}/studio/drafts/{draft}/comments/{comment}/toggle", new { });
        using var invalid = await client.PostAsJsonAsync($"/api/v1/organizations/not-a-guid/studio/drafts/{draft}/comments/{comment}/resolve", new { expectedResolved = false });
        using var get = await client.GetAsync($"/api/v1/organizations/{tenant}/studio/drafts/{draft}/comments/{comment}/resolve");
        Assert.Equal(HttpStatusCode.Unauthorized, resolve.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, reopen.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, invalid.StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, get.StatusCode);
    }

    [Theory]
    [InlineData("ConnectionStrings:Database", "", "ConnectionStrings:Database")]
    [InlineData("Jwt:Issuer", "", "Jwt exige")]
    [InlineData("Jwt:Audience", "", "Jwt exige")]
    [InlineData("Jwt:SigningKey", "short", "Jwt exige")]
    [InlineData("Jwt:AccessTokenMinutes", "4", "Jwt:AccessTokenMinutes")]
    [InlineData("Jwt:AccessTokenMinutes", "31", "Jwt:AccessTokenMinutes")]
    public void InvalidProductionConfigurationPreventsHostStartup(
        string key,
        string value,
        string expectedMessage)
    {
        var configuration = ValidConfiguration();
        configuration[key] = value;
        using var factory = new ConfiguredApiFactory("Production", configuration);

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains(expectedMessage, exception.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string?> ValidConfiguration() => new()
    {
        ["ConnectionStrings:Database"] = "Host=localhost;Database=odca_test_startup",
        ["Jwt:Issuer"] = "odca-api-host-tests",
        ["Jwt:Audience"] = "odca-api-host-tests",
        ["Jwt:SigningKey"] = "host-test-key-with-at-least-thirty-two-bytes",
        ["Jwt:AccessTokenMinutes"] = "15",
        ["Security:MfaRequiredForSuperAdmin"] = "true",
        ["Security:AllowDevelopmentBootstrap"] = "false",
        ["DataProtection:ApplicationName"] = "ODCA startup isolation tests",
        ["DataProtection:KeysPath"] = Path.Combine(Path.GetTempPath(), "odca-startup-tests", Guid.NewGuid().ToString("N"))
    };

    private sealed class ConfiguredApiFactory(
        string environment,
        IReadOnlyDictionary<string, string?> settings)
        : WebApplicationFactory<global::Odca.Api.ApiAssemblyMarker>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(settings));
        }
    }
}
