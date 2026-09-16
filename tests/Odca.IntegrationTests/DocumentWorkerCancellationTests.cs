using System.Diagnostics;
using Odca.Worker;

namespace Odca.IntegrationTests;

public sealed class DocumentWorkerCancellationTests
{
    [Fact]
    public async Task ExternalToolIsStoppedWhenTheHostCancels()
    {
        var windows = OperatingSystem.IsWindows();
        var executable = windows ? "cmd.exe" : "/bin/sh";
        IReadOnlyList<string> arguments = windows
            ? ["/c", "ping 127.0.0.1 -n 30 > nul"]
            : ["-c", "sleep 30"];
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
        var stopwatch = Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            DocumentWorker.Run(executable, arguments, TimeSpan.FromMinutes(1), cancellation.Token));

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task CancellationIsNotRetriedAsATransientFailure()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var attempts = 0;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            while (!cancellation.Token.IsCancellationRequested || attempts == 0)
            {
                attempts++;
                try
                {
                    await DocumentWorker.Run(
                        OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh",
                        OperatingSystem.IsWindows() ? ["/c", "ping 127.0.0.1 -n 30 > nul"] : ["-c", "sleep 30"],
                        TimeSpan.FromMinutes(1),
                        cancellation.Token);
                }
                catch (OperationCanceledException) when (cancellation.Token.IsCancellationRequested)
                {
                    throw;
                }
            }
        });

        Assert.Equal(1, attempts);
    }
}
