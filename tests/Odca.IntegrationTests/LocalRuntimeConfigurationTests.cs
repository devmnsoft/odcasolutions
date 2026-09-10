using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Odca.Configuration;
using System.Text.Json.Nodes;

namespace Odca.IntegrationTests;

[CollectionDefinition("Environment", DisableParallelization = true)]
public sealed class EnvironmentCollection { }

[Collection("Environment")]
public sealed class LocalRuntimeConfigurationTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "odca-config-" + Guid.NewGuid());
    private readonly string? previousPath = Environment.GetEnvironmentVariable(LocalRuntimeConfiguration.EnvironmentVariable);

    [Fact]
    public void DevelopmentLoadsCompleteFileAndEnvironmentWins()
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "runtime.json");
        File.WriteAllText(path, """{"Value":"file","Jwt":{"Issuer":"issuer"}}""");
        Environment.SetEnvironmentVariable(LocalRuntimeConfiguration.EnvironmentVariable, path);
        Environment.SetEnvironmentVariable("Value", "environment");
        try
        {
            var configuration = new ConfigurationManager();
            var result = LocalRuntimeConfiguration.Add(configuration, EnvironmentNamed("Development"), []);
            Assert.True(result.Loaded);
            Assert.Equal("environment", configuration["Value"]);
            Assert.Equal("issuer", configuration["Jwt:Issuer"]);
        }
        finally { Environment.SetEnvironmentVariable("Value", null); }
    }

    [Fact]
    public void ArgumentsWinOverEnvironmentAndFile()
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "runtime.json");
        File.WriteAllText(path, """{"Value":"file"}""");
        Environment.SetEnvironmentVariable(LocalRuntimeConfiguration.EnvironmentVariable, path);
        Environment.SetEnvironmentVariable("Value", "environment");
        try
        {
            var configuration = new ConfigurationManager();
            LocalRuntimeConfiguration.Add(configuration, EnvironmentNamed("Development"), ["--Value=argument"]);
            Assert.Equal("argument", configuration["Value"]);
        }
        finally { Environment.SetEnvironmentVariable("Value", null); }
    }

    [Theory]
    [InlineData("Testing")]
    [InlineData("Production")]
    public void NonDevelopmentNeverLoadsPersonalFile(string environment)
    {
        Environment.SetEnvironmentVariable(LocalRuntimeConfiguration.EnvironmentVariable, Path.Combine(directory, "missing.json"));
        var configuration = new ConfigurationManager();
        var result = LocalRuntimeConfiguration.Add(configuration, EnvironmentNamed(environment), []);
        Assert.False(result.Loaded);
        Assert.Null(result.Path);
    }

    [Fact]
    public void MissingAndInvalidFilesHaveDistinctSafeDiagnostics()
    {
        var path = Path.Combine(directory, "missing.json");
        Environment.SetEnvironmentVariable(LocalRuntimeConfiguration.EnvironmentVariable, path);
        var missing = Assert.Throws<InvalidOperationException>(() =>
            LocalRuntimeConfiguration.Add(new ConfigurationManager(), EnvironmentNamed("Development"), []));
        Assert.Contains("não encontrada", missing.Message);

        Directory.CreateDirectory(directory);
        File.WriteAllText(path, "{ invalid");
        var invalid = Assert.Throws<InvalidOperationException>(() =>
            LocalRuntimeConfiguration.Add(new ConfigurationManager(), EnvironmentNamed("Development"), []));
        Assert.Contains("JSON inválido", invalid.Message);
    }

    [Fact]
    public async Task RepairIsIdempotentAndPreservesValidAndUnknownValues()
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "runtime.json");
        const string validKey = "this-existing-signing-key-is-long-enough-to-preserve";
        File.WriteAllText(path, $$"""{"ConnectionStrings":{"Database":"Host=private;Password=secret"},"Jwt":{"SigningKey":"{{validKey}}"},"Unknown":{"Keep":true}}""");
        Environment.SetEnvironmentVariable(LocalRuntimeConfiguration.EnvironmentVariable, path);

        Assert.Equal(0, await BootstrapProgram.RunAsync(["repair"]));
        var once = File.ReadAllText(path);
        Assert.Equal(0, await BootstrapProgram.RunAsync(["repair"]));
        Assert.Equal(once, File.ReadAllText(path));
        var root = JsonNode.Parse(once)!;
        Assert.Equal(validKey, root["Jwt"]!["SigningKey"]!.GetValue<string>());
        Assert.True(root["Unknown"]!["Keep"]!.GetValue<bool>());
        Assert.Equal("odca-api", root["Jwt"]!["Issuer"]!.GetValue<string>());
        Assert.NotEmpty(Directory.GetFiles(directory, "runtime.json.backup-*"));
    }

    [Fact]
    public async Task RepairRefusesInvalidExistingKeyWithoutExplicitReplacement()
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "runtime.json");
        File.WriteAllText(path, """{"Jwt":{"SigningKey":"short"}}""");
        Environment.SetEnvironmentVariable(LocalRuntimeConfiguration.EnvironmentVariable, path);
        Assert.Equal(1, await BootstrapProgram.RunAsync(["repair"]));
        Assert.Equal("short", JsonNode.Parse(File.ReadAllText(path))!["Jwt"]!["SigningKey"]!.GetValue<string>());
        Assert.Empty(Directory.GetFiles(directory, "*.backup-*"));
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(LocalRuntimeConfiguration.EnvironmentVariable, previousPath);
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }

    private static IHostEnvironment EnvironmentNamed(string name) => new FakeEnvironment { EnvironmentName = name };

    private sealed class FakeEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "";
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
