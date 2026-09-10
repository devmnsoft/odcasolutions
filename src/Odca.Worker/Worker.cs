using Dapper;
using Microsoft.AspNetCore.DataProtection;
using Npgsql;

namespace Odca.Worker;

public sealed class Worker(NpgsqlDataSource dataSource,IDataProtectionProvider protectionProvider,IHostEnvironment environment,IConfiguration configuration,ILogger<Worker> logger):BackgroundService
{
    private readonly IDataProtector protector=protectionProvider.CreateProtector("ODCA.TenantInvitation.v1");
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer=new PeriodicTimer(TimeSpan.FromSeconds(5));
        do { await ProcessOneAsync(stoppingToken); } while(await timer.WaitForNextTickAsync(stoppingToken));
    }
    private async Task ProcessOneAsync(CancellationToken ct)
    {
        await using var connection=await dataSource.OpenConnectionAsync(ct);
        var item=await connection.QuerySingleOrDefaultAsync<OutboxItem>(new CommandDefinition("""SELECT id AS "Id",tenant_id AS "TenantId",invitation_id AS "InvitationId",destination AS "Destination",protected_payload AS "ProtectedPayload" FROM odca.claim_notification(@worker);""",new{worker=Environment.MachineName},cancellationToken:ct));
        if(item is null)return;
        try
        {
            if(!environment.IsDevelopment())throw new InvalidOperationException("notification_provider_missing");
            var origin=configuration["Notifications:PublicOrigin"];
            var pickup=configuration["Notifications:DevelopmentPickupDirectory"];
            if(!Uri.TryCreate(origin,UriKind.Absolute,out var baseUri)||string.IsNullOrWhiteSpace(pickup))throw new InvalidOperationException("notification_configuration_invalid");
            var token=protector.Unprotect(item.ProtectedPayload); Directory.CreateDirectory(pickup);
            var link=new Uri(baseUri,$"convites/aceitar?invitationId={item.InvitationId:D}&token={Uri.EscapeDataString(token)}");
            await File.WriteAllTextAsync(Path.Combine(pickup,$"{item.Id:N}.txt"),$"Destinatário sintético/local: {item.Destination}\n{link}\n",ct);
            await connection.ExecuteAsync(new CommandDefinition("SELECT odca.complete_notification(@id,true,NULL);",new{id=item.Id},cancellationToken:ct));
        }
        catch(Exception ex)
        {
            logger.LogWarning("Falha controlada no envio {MessageId}: {ErrorCode}",item.Id,ex.Message);
            await connection.ExecuteAsync(new CommandDefinition("SELECT odca.complete_notification(@id,false,@code);",new{id=item.Id,code=ex.Message[..Math.Min(ex.Message.Length,80)]},cancellationToken:ct));
        }
    }
    private sealed record OutboxItem(Guid Id,Guid TenantId,Guid InvitationId,string Destination,string ProtectedPayload);
}
