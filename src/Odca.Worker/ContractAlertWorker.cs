using Odca.Application.Contracts;

namespace Odca.Worker;

public sealed class ContractAlertWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<ContractAlertWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        try
        {
            do
            {
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var alerts = scope.ServiceProvider.GetRequiredService<IContractAlertRepository>();
                    await alerts.EvaluateDueAlertsAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Falha transiente na avaliação de alertas de contrato.");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Worker de alertas de contrato encerrado por cancelamento do host.");
        }
    }
}
