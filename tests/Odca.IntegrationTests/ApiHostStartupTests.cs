using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Odca.IntegrationTests;

public sealed class ApiHostStartupTests
{
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
        ["Security:AllowDevelopmentBootstrap"] = "false"
    };

    private sealed class ConfiguredApiFactory(
        string environment,
        IReadOnlyDictionary<string, string?> settings) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(settings));
        }
    }
}
