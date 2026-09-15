using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace Odca.Configuration;

/// <summary>Runs the repository setup once for explicitly enabled local Windows launches.</summary>
internal static class DevelopmentRuntimeSetup
{
    public static void EnsureCreated(string path)
    {
        if (File.Exists(path) ||
            !OperatingSystem.IsWindows() ||
            !Environment.UserInteractive ||
            !string.Equals(Environment.GetEnvironmentVariable("ODCA_SETUP_ON_START"), "true", StringComparison.OrdinalIgnoreCase) ||
            Environment.GetEnvironmentVariable(LocalRuntimeConfiguration.EnvironmentVariable) is not null ||
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI")))
        {
            return;
        }

        var repository = Directory.GetParent(path)?.Parent?.Parent?.FullName
            ?? throw new InvalidOperationException("Não foi possível localizar a raiz do projeto ODCA.");
        var script = Path.Combine(repository, "scripts", "setup-local.ps1");
        if (!File.Exists(script))
        {
            throw new InvalidOperationException($"Assistente local não encontrado: {script}. Atualize o checkout completo do projeto.");
        }

        var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant())));
        using var mutex = new Mutex(false, @"Local\Odca.RuntimeSetup." + identity);
        var acquired = false;
        try
        {
            try
            {
                acquired = mutex.WaitOne(TimeSpan.FromMinutes(10));
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }

            if (!acquired)
            {
                throw new InvalidOperationException("Outro processo está configurando o ODCA. Conclua o assistente aberto e inicie novamente.");
            }

            if (File.Exists(path))
            {
                return;
            }

            var start = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                    "WindowsPowerShell", "v1.0", "powershell.exe"),
                WorkingDirectory = repository,
                UseShellExecute = true
            };
            start.ArgumentList.Add("-NoProfile");
            start.ArgumentList.Add("-ExecutionPolicy");
            start.ArgumentList.Add("Bypass");
            start.ArgumentList.Add("-File");
            start.ArgumentList.Add(script);

            using var process = Process.Start(start)
                ?? throw new InvalidOperationException("Não foi possível abrir o assistente de configuração local.");

            process.WaitForExit();
            if (process.ExitCode != 0 || !File.Exists(path))
            {
                throw new InvalidOperationException(
                    "A configuração inicial não foi concluída. Execute .\\scripts\\setup-local.ps1 em um PowerShell na raiz do projeto para visualizar o diagnóstico.");
            }
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException(
                "Não foi possível iniciar o PowerShell. Execute .\\scripts\\setup-local.ps1 manualmente na raiz do projeto.", exception);
        }
        finally
        {
            if (acquired)
            {
                mutex.ReleaseMutex();
            }
        }
    }
}
