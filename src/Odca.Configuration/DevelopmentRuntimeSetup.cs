using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Hosting;

namespace Odca.Configuration;

public static class DevelopmentRuntimeSetup
{
    public const string EnableEnvironmentVariable = "ODCA_SETUP_ON_START";
    private static readonly TimeSpan SetupTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan LockRetryInterval = TimeSpan.FromMilliseconds(200);

    public static void PrepareIfEnabled(IHostEnvironment environment, string runtimePath)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimePath);

        if (!environment.IsDevelopment() ||
            !string.Equals(Environment.GetEnvironmentVariable(EnableEnvironmentVariable), "true", StringComparison.OrdinalIgnoreCase) ||
            File.Exists(runtimePath))
        {
            return;
        }

        if (!OperatingSystem.IsWindows() || !Environment.UserInteractive)
        {
            throw new InvalidOperationException(
                $"A configuração local está ausente em '{runtimePath}', mas o assistente automático só pode ser aberto " +
                "em uma sessão interativa do Windows. Execute scripts/setup-local.ps1 manualmente ou desabilite " +
                $"{EnableEnvironmentVariable}.");
        }

        CoordinateCreation(runtimePath, SetupTimeout, _ => RunPowerShellSetup(environment.ContentRootPath));
    }

    internal static void CoordinateCreation(
        string runtimePath,
        TimeSpan timeout,
        Func<string, int> setup)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimePath);
        ArgumentNullException.ThrowIfNull(setup);
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));

        var fullPath = Path.GetFullPath(runtimePath);
        var lockPath = GetLockPath(fullPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException($"O caminho da configuração não possui diretório: '{fullPath}'."));

        var started = Stopwatch.StartNew();
        using var coordination = AcquireLock(lockPath, timeout, started);
        if (File.Exists(fullPath)) return;

        var exitCode = setup(fullPath);
        if (exitCode != 0)
        {
            throw new InvalidOperationException(
                $"O assistente de configuração foi cancelado ou falhou (código {exitCode}). " +
                $"O arquivo esperado era '{fullPath}'. Execute scripts/setup-local.ps1 manualmente para ver o diagnóstico completo.");
        }

        if (!File.Exists(fullPath))
        {
            throw new InvalidOperationException(
                $"O assistente terminou com sucesso, mas não criou '{fullPath}'. " +
                $"Verifique {LocalRuntimeConfiguration.EnvironmentVariable} e execute scripts/setup-local.ps1 manualmente.");
        }
    }

    private static FileStream AcquireLock(string lockPath, TimeSpan timeout, Stopwatch started)
    {
        while (true)
        {
            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (started.Elapsed < timeout)
            {
                Thread.Sleep(LockRetryInterval);
            }

            if (started.Elapsed >= timeout)
            {
                throw new TimeoutException(
                    $"Tempo esgotado aguardando outro processo concluir a configuração local em '{lockPath}'. " +
                    "Encerre assistentes abandonados e tente novamente.");
            }
        }
    }

    private static int RunPowerShellSetup(string contentRootPath)
    {
        var repositoryRoot = FindRepositoryRoot(contentRootPath);
        var scriptPath = Path.Combine(repositoryRoot, "scripts", "setup-local.ps1");
        if (!File.Exists(scriptPath))
        {
            throw new FileNotFoundException("O script de configuração local não foi encontrado.", scriptPath);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            WorkingDirectory = repositoryRoot,
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Normal
        };
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("O Windows não conseguiu iniciar powershell.exe para a configuração local.");
        if (process.WaitForExit((int)SetupTimeout.TotalMilliseconds)) return process.ExitCode;

        try { process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        throw new TimeoutException(
            $"O assistente não terminou em {SetupTimeout.TotalMinutes:0} minutos e foi encerrado. " +
            "Execute scripts/setup-local.ps1 manualmente para tentar novamente.");
    }

    private static string FindRepositoryRoot(string startPath)
    {
        var current = new DirectoryInfo(Path.GetFullPath(startPath));
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "scripts", "setup-local.ps1"))) return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            $"A raiz do repositório não foi encontrada a partir de '{startPath}'. Execute scripts/setup-local.ps1 manualmente.");
    }

    private static string GetLockPath(string runtimePath)
    {
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(runtimePath)))[..16];
        return Path.Combine(Path.GetTempPath(), $"odca-development-runtime-{digest}.lock");
    }
}
