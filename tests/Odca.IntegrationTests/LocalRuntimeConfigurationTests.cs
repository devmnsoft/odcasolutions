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
    public void EmptyOverrideIsRejectedInDevelopment()
    {
        Environment.SetEnvironmentVariable(LocalRuntimeConfiguration.EnvironmentVariable, "   ");
        var exception = Assert.Throws<InvalidOperationException>(() =>
            LocalRuntimeConfiguration.Add(new ConfigurationManager(), EnvironmentNamed("Development"), []));
        Assert.Contains("caminho vazio", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostgresDevelopmentInitCreatesCompleteConfigurationWithoutChangingUserChoice()
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "runtime.json");
        Environment.SetEnvironmentVariable(LocalRuntimeConfiguration.EnvironmentVariable, path);
        Environment.SetEnvironmentVariable("ODCA_LOCAL_POSTGRES_PASSWORD", "local-test-secret");
        try
        {
            Assert.Equal(0, await BootstrapProgram.RunAsync(["init", "--postgres-development"]));
            var root = JsonNode.Parse(File.ReadAllText(path))!;
            var database = new Npgsql.NpgsqlConnectionStringBuilder(root["ConnectionStrings"]!["Database"]!.GetValue<string>());
            Assert.Equal("postgres", database.Database);
            Assert.Equal("postgres", database.Username);
            Assert.Equal(5432, database.Port);
            Assert.Equal("odca", database.SearchPath);
            Assert.Equal(50, database.MaxPoolSize);
            Assert.Equal(60, database.CommandTimeout);
            Assert.Equal(root["ConnectionStrings"]!["Database"]!.GetValue<string>(),
                root["ConnectionStrings"]!["DatabaseAdmin"]!.GetValue<string>());
            Assert.Equal(48, Convert.FromBase64String(root["Jwt"]!["SigningKey"]!.GetValue<string>()).Length);
            Assert.Equal("ODCA Solutions", root["DataProtection"]!["ApplicationName"]!.GetValue<string>());

            var original = File.ReadAllText(path);
            Environment.SetEnvironmentVariable("ODCA_LOCAL_POSTGRES_PASSWORD", "must-not-replace");
            Assert.Equal(0, await BootstrapProgram.RunAsync(["init", "--postgres-development"]));
            Assert.Equal(original, File.ReadAllText(path));
        }
        finally
        {
            Environment.SetEnvironmentVariable("ODCA_LOCAL_POSTGRES_PASSWORD", null);
        }
    }

    [Fact]
    public async Task ConcurrentInitializationsNeverReplaceTheWinningConfiguration()
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "runtime.json");
        Environment.SetEnvironmentVariable(LocalRuntimeConfiguration.EnvironmentVariable, path);
        Environment.SetEnvironmentVariable("ODCA_LOCAL_POSTGRES_PASSWORD", "concurrent-secret");
        try
        {
            var results = await Task.WhenAll(
                BootstrapProgram.RunAsync(["init", "--postgres-development"]),
                BootstrapProgram.RunAsync(["init", "--postgres-development"]));
            Assert.All(results, result => Assert.Equal(0, result));
            Assert.Equal(48, Convert.FromBase64String(
                JsonNode.Parse(File.ReadAllText(path))!["Jwt"]!["SigningKey"]!.GetValue<string>()).Length);
            Assert.Empty(Directory.GetFiles(directory, "*.tmp-*"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("ODCA_LOCAL_POSTGRES_PASSWORD", null);
        }
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

    [Fact]
    public void ApiWebWorkerAndBootstrapResolveTheSameProjectConfiguration()
    {
        var api = Path.Combine(directory, "src", "Odca.Api");
        var worker = Path.Combine(directory, "src", "Odca.Worker");
        var web = Path.Combine(directory, "src", "Odca.Web");
        var bootstrap = Path.Combine(directory, "src", "Odca.Bootstrap");
        Directory.CreateDirectory(api);
        Directory.CreateDirectory(worker);
        Directory.CreateDirectory(web);
        Directory.CreateDirectory(bootstrap);
        File.WriteAllText(Path.Combine(api, "Odca.Api.csproj"), "<Project />");
        var expected = Path.Combine(api, "development-runtime.json");

        Assert.Equal(expected, LocalRuntimeConfiguration.GetDefaultPath(api));
        Assert.Equal(expected, LocalRuntimeConfiguration.GetDefaultPath(worker));
        Assert.Equal(expected, LocalRuntimeConfiguration.GetDefaultPath(web));
        Assert.Equal(expected, LocalRuntimeConfiguration.GetDefaultPath(bootstrap));
        Assert.Equal(expected, LocalRuntimeConfiguration.GetDefaultPath(directory));
    }

    [Theory]
    [InlineData("Development", "postgres", false, false)]
    [InlineData("Development", "postgres", true, true)]
    [InlineData("Testing", "postgres", true, false)]
    [InlineData("Production", "postgres", true, false)]
    [InlineData("Development", "odca_test", false, true)]
    public void PostgresProvisioningRequiresExplicitDevelopmentException(
        string environment, string database, bool option, bool expected)
    {
        Assert.Equal(expected, BootstrapProgram.IsProvisionTargetAllowed(environment, database, option));
    }

    [Fact]
    public void SetupStopsDotNetPipelineOnNonZeroExitCode()
    {
        var root = LocalRuntimeConfiguration.GetDefaultPath(Directory.GetCurrentDirectory());
        var script = File.ReadAllText(Path.Combine(Directory.GetParent(Directory.GetParent(Directory.GetParent(root)!.FullName)!.FullName)!.FullName,
            "scripts", "setup-local.ps1"));
        Assert.Contains("if ($LASTEXITCODE -ne 0)", script, StringComparison.Ordinal);
        Assert.Contains("As etapas dependentes não foram executadas", script, StringComparison.Ordinal);
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
