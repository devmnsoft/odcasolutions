using Dapper;
using Npgsql;

namespace Odca.Worker;

public sealed class ObligationReminderWorker(NpgsqlDataSource dataSource,ILogger<ObligationReminderWorker> logger) : BackgroundService
{
    private readonly Guid owner=Guid.NewGuid();
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer=new PeriodicTimer(TimeSpan.FromSeconds(30));
        try { do { await ProcessAsync(stoppingToken); } while(await timer.WaitForNextTickAsync(stoppingToken)); }
        catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){logger.LogInformation("Worker de lembretes encerrado pelo host.");}
    }
    private async Task ProcessAsync(CancellationToken ct)
    {
        try
        {
            await using var connection=await dataSource.OpenConnectionAsync(ct);var token=Guid.NewGuid();
            var item=await connection.QuerySingleOrDefaultAsync<Reminder>(new CommandDefinition("SELECT id AS Id,tenant_id AS TenantId,obligation_id AS ObligationId,recipient_id AS RecipientId FROM odca.claim_obligation_reminder(@owner,@token)",new{owner,token},cancellationToken:ct));
            if(item is null)return;
            var completed=await connection.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT odca.complete_obligation_reminder(@id,@token)",new{item.Id,token},cancellationToken:ct));
            if(!completed)logger.LogWarning("Lease do lembrete {ReminderId} expirou antes da conclusão.",item.Id);
        }
        catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
        catch(Exception exception){logger.LogError(exception,"Falha transitória no processamento de lembrete interno.");}
    }
    private sealed record Reminder(Guid Id,Guid TenantId,Guid ObligationId,Guid RecipientId);
}
