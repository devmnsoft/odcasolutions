using Dapper;
using Npgsql;

namespace Odca.Worker;

/// <summary>
/// Seção C: varre o relógio de SLA das solicitações ativas e registra violações de primeira
/// resposta/resolução (uma vez cada) direto na tabela e na timeline via odca.sla_scan_violations().
/// A função usa FOR UPDATE SKIP LOCKED, então múltiplas instâncias do worker não competem.
/// </summary>
public sealed class SlaViolationScanWorker(NpgsqlDataSource dataSource, ILogger<SlaViolationScanWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(60));
        try { do { await ScanAsync(stoppingToken); } while (await timer.WaitForNextTickAsync(stoppingToken)); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { logger.LogInformation("Worker de varredura de SLA encerrado pelo host."); }
    }
    private async Task ScanAsync(CancellationToken ct)
    {
        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(ct);
            var violations = await connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT odca.sla_scan_violations()", cancellationToken: ct));
            if (violations > 0) logger.LogInformation("Varredura de SLA registrou {Count} violação(ões).", violations);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception exception) { logger.LogError(exception, "Falha transitória na varredura de violações de SLA."); }
    }
}
