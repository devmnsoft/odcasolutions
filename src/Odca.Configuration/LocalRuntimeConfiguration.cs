using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Odca.Configuration;

public sealed record LocalRuntimeConfigurationResult(string Environment, string? Path, bool Loaded);

public static class LocalRuntimeConfiguration
{
    public const string EnvironmentVariable = "ODCA_RUNTIME_CONFIG";

    public static string ResolvePath(string? startDirectory = null)
    {
        var configuredPath = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (configuredPath is not null && string.IsNullOrWhiteSpace(configuredPath))
        {
            throw new InvalidOperationException($"{EnvironmentVariable} foi definida com um caminho vazio.");
        }

        // Relative overrides are relative to the process working directory on every host and in the setup script.
        return Path.GetFullPath(configuredPath ?? GetDefaultPath(startDirectory));
    }

    public static string GetDefaultPath(string? startDirectory = null)
    {
        var current = new DirectoryInfo(startDirectory ?? Directory.GetCurrentDirectory());
        while (current is not null)
        {
            var apiProject = Path.Combine(current.FullName, "src", "Odca.Api", "Odca.Api.csproj");
            if (File.Exists(apiProject))
            {
                return Path.Combine(current.FullName, "src", "Odca.Api", "development-runtime.json");
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            "Projeto ODCA não encontrado. Execute dentro do repositório ou defina ODCA_RUNTIME_CONFIG.");
    }

    public static LocalRuntimeConfigurationResult Add(
        ConfigurationManager configuration, IHostEnvironment environment, string[] args)
    {
        if (!environment.IsDevelopment())
        {
            return new(environment.EnvironmentName, null, false);
        }

        var path = ResolvePath(environment.ContentRootPath);
        try
        {
            // The personal file supplies defaults. Explicit process configuration always wins.
            configuration.AddJsonFile(path, optional: false, reloadOnChange: false);
            configuration.AddEnvironmentVariables();
            configuration.AddCommandLine(args);
            return new(environment.EnvironmentName, Path.GetFullPath(path), true);
        }
        catch (FileNotFoundException exception)
        {
            throw new InvalidOperationException(
                $"Configuração local não encontrada. Ambiente={environment.EnvironmentName}; caminho={path}. " +
                "Na raiz do repositório, execute .\\scripts\\setup-local.ps1 e depois inicie novamente.", exception);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException or InvalidDataException)
        {
            throw new InvalidOperationException(
                $"Configuração local ilegível ou com JSON inválido. Ambiente={environment.EnvironmentName}; caminho={path}.", exception);
        }
    }
}
