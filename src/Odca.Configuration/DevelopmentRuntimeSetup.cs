using System.ComponentModel;
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

        CoordinateCreation(runtimePath, SetupTimeout, target => RunPowerShellSetup(environment.ContentRootPath, target));
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
        var failurePath = lockPath + ".failed";
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException($"O caminho da configuração não possui diretório: '{fullPath}'."));

        var attemptStartedAt = DateTimeOffset.UtcNow;
        var started = Stopwatch.StartNew();
        using var coordination = AcquireLock(lockPath, timeout, started);
        if (File.Exists(fullPath)) return;

        // A caller which was already waiting when the winning assistant failed must observe that
        // same result instead of opening another password prompt. A later, deliberate restart can retry.
        if (TryReadConcurrentFailure(failurePath, attemptStartedAt, out var concurrentFailure))
        {
            throw new InvalidOperationException(concurrentFailure);
        }

        try
        {
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

            File.Delete(failurePath);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or
            UnauthorizedAccessException or TimeoutException or Win32Exception)
        {
            WriteFailure(failurePath, exception.Message);
            throw;
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

    private static int RunPowerShellSetup(string contentRootPath, string runtimePath)
    {
        var repositoryRoot = FindRepositoryRoot(contentRootPath);
        var scriptPath = Path.Combine(repositoryRoot, "scripts", "setup-local.ps1");
        if (!File.Exists(scriptPath))
        {
            throw new FileNotFoundException("O script de configuração local não foi encontrado.", scriptPath);
        }

        var startInfo = CreatePowerShellStartInfo(repositoryRoot, scriptPath, runtimePath);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("O Windows não conseguiu iniciar powershell.exe para a configuração local.");
        if (process.WaitForExit((int)SetupTimeout.TotalMilliseconds)) return process.ExitCode;

        TerminateTimedOutProcess(process);
        throw new TimeoutException(
            $"O assistente não terminou em {SetupTimeout.TotalMinutes:0} minutos e foi encerrado. " +
            "Execute scripts/setup-local.ps1 manualmente para tentar novamente.");
    }

    internal static void TerminateTimedOutProcess(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) when (process.HasExited)
        {
            return;
        }

        // Kill is asynchronous. Reap the process before releasing the cross-host lock so a timed-out
        // PowerShell cannot remain alive, prompt later, and race a subsequent deliberate restart.
        if (!process.WaitForExit(TimeSpan.FromSeconds(30)))
        {
            throw new TimeoutException(
                $"O processo do assistente (PID {process.Id}) não encerrou após o cancelamento forçado. " +
                "Encerre powershell.exe manualmente antes de tentar novamente.");
        }
    }

    internal static ProcessStartInfo CreatePowerShellStartInfo(
        string repositoryRoot,
        string scriptPath,
        string runtimePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimePath);

        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            WorkingDirectory = Path.GetFullPath(repositoryRoot),
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Normal
        };
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(Path.GetFullPath(scriptPath));
        startInfo.ArgumentList.Add("-RuntimePath");
        startInfo.ArgumentList.Add(Path.GetFullPath(runtimePath));
        return startInfo;
    }

    private static bool TryReadConcurrentFailure(string failurePath, DateTimeOffset attemptStartedAt, out string message)
    {
        message = string.Empty;
        if (!File.Exists(failurePath) || File.GetLastWriteTimeUtc(failurePath) < attemptStartedAt.UtcDateTime)
        {
            return false;
        }

        message = File.ReadAllText(failurePath);
        return !string.IsNullOrWhiteSpace(message);
    }

    private static void WriteFailure(string failurePath, string message)
    {
        var temporary = failurePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, message);
            File.Move(temporary, failurePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
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
