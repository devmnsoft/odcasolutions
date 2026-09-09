using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Odca.Api;
using Odca.Infrastructure.Identity;

namespace Odca.IntegrationTests;

public sealed class StartupValidationTests
{
    [Fact]
    public void ValidIsolatedTestConfigurationIsAccepted()
    {
        var configuration = Configuration(
            ("ConnectionStrings:Database", "Host=localhost;Database=odca_test"),
            ("Security:MfaRequiredForSuperAdmin", "true"),
            ("Security:AllowDevelopmentBootstrap", "false"));

        StartupValidation.Validate(configuration, Environment("Testing"), ValidJwt());
    }

    [Theory]
    [InlineData("", "audience", "a-test-key-with-at-least-thirty-two-bytes", 15)]
    [InlineData("issuer", "", "a-test-key-with-at-least-thirty-two-bytes", 15)]
    [InlineData("issuer", "audience", "short", 15)]
    [InlineData("issuer", "audience", "a-test-key-with-at-least-thirty-two-bytes", 31)]
    public void InvalidJwtConfigurationIsRejected(
        string issuer,
        string audience,
        string signingKey,
        int accessTokenMinutes)
    {
        var configuration = Configuration(
            ("ConnectionStrings:Database", "Host=localhost;Database=odca_test"),
            ("Security:MfaRequiredForSuperAdmin", "true"));
        var jwt = new JwtOptions
        {
            Issuer = issuer,
            Audience = audience,
            SigningKey = signingKey,
            AccessTokenMinutes = accessTokenMinutes
        };

        Assert.Throws<InvalidOperationException>(
            () => StartupValidation.Validate(configuration, Environment("Production"), jwt));
    }

    [Fact]
    public void ProductionWithoutMfaRequirementIsRejected()
    {
        var configuration = Configuration(
            ("ConnectionStrings:Database", "Host=localhost;Database=odca_test"),
            ("Security:MfaRequiredForSuperAdmin", "false"));

        Assert.Throws<InvalidOperationException>(
            () => StartupValidation.Validate(configuration, Environment("Production"), ValidJwt()));
    }

    private static IConfiguration Configuration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(item => item.Key, item => (string?)item.Value))
            .Build();

    private static JwtOptions ValidJwt() => new()
    {
        Issuer = "odca-api-tests",
        Audience = "odca-api-tests",
        SigningKey = "a-test-key-with-at-least-thirty-two-bytes",
        AccessTokenMinutes = 15
    };

    private static TestHostEnvironment Environment(string name) => new(name);

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Odca.IntegrationTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
