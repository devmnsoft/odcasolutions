using Dapper;
using Microsoft.AspNetCore.DataProtection;
using Npgsql;
using Odca.Worker.Transports;

namespace Odca.Worker;

public sealed class Worker(NpgsqlDataSource dataSource, IDataProtectionProvider protectionProvider, INotificationTransport transport, IConfiguration configuration, ILogger<Worker> logger) : BackgroundService
{
    private readonly IDataProtector protector = protectionProvider.CreateProtector("ODCA.TenantInvitation.v1");
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        do
        {
            try
            {
                await ProcessOneAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falha transiente na comunicação com o banco de dados ou processamento.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
    private async Task ProcessOneAsync(CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        var item = await connection.QuerySingleOrDefaultAsync<OutboxItem>(new CommandDefinition("""SELECT id AS "Id",tenant_id AS "TenantId",invitation_id AS "InvitationId",destination AS "Destination",protected_payload AS "ProtectedPayload",lease_token AS "LeaseToken" FROM odca.claim_notification(@worker);""", new { worker = Environment.MachineName }, cancellationToken: ct));
        if (item is null) return;
        try
        {
            var origin = configuration["Notifications:PublicOrigin"];
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var baseUri)) throw new InvalidOperationException("notification_configuration_invalid");
            
            var token = protector.Unprotect(item.ProtectedPayload);
            var link = new Uri(baseUri, $"convites/aceitar?invitationId={item.InvitationId:D}&token={Uri.EscapeDataString(token)}");
            
            await transport.SendInvitationAsync(item.Destination, link, ct);
            
            await connection.ExecuteAsync(new CommandDefinition("SELECT odca.complete_notification(@id,@token,true,NULL);", new { id = item.Id, token = item.LeaseToken }, cancellationToken: ct));
        }
        catch (Exception ex)
        {
            logger.LogWarning("Falha controlada no envio {MessageId}: {ErrorCode}", item.Id, ex.Message);
            await connection.ExecuteAsync(new CommandDefinition("SELECT odca.complete_notification(@id,@token,false,@code);", new { id = item.Id, token = item.LeaseToken, code = ex.Message[..Math.Min(ex.Message.Length, 80)] }, cancellationToken: ct));
        }
    }
    private sealed record OutboxItem(Guid Id, Guid TenantId, Guid InvitationId, string Destination, string ProtectedPayload, Guid LeaseToken);
}
