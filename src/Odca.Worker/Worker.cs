using Dapper;
using Microsoft.AspNetCore.DataProtection;
using Npgsql;
using Odca.Worker.Transports;

namespace Odca.Worker;

public sealed class Worker(
    NpgsqlDataSource dataSource,
    IDataProtectionProvider protectionProvider,
    INotificationTransport transport,
    IConfiguration configuration,
    ILogger<Worker> logger) : BackgroundService
{
    private readonly IDataProtector protector = protectionProvider.CreateProtector("ODCA.TenantInvitation.v1");
    private readonly IDataProtector _notifProtector = protectionProvider.CreateProtector("ODCA.UserNotification.v1");
    // B.1 (D-OC31): limite de tentativas configurável por processo (Notifications:MaxAttempts);
    // o padrão 5 preserva o comportamento histórico (backoff quadrático, teto de 300s).
    private readonly int _maxAttempts = ResolveMaxAttempts(configuration["Notifications:MaxAttempts"]);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        try
        {
            do
            {
                try
                {
                    await ProcessOneAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Falha transiente na comunicação com o banco de dados ou processamento.");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Worker de notificações encerrado por cancelamento do host.");
        }
    }

    private async Task ProcessOneAsync(CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        // B.1 (D-OC31): limpeza periódica do store compartilhado de sessões/chaves (v054).
        // Operação barata e idempotente; o worker é o executor periódico designado.
        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT odca.purge_expired_cache_entries();", null, cancellationToken: ct));
        var item = await connection.QuerySingleOrDefaultAsync<OutboxItem>(new CommandDefinition(
            """
            SELECT id AS "Id",
                   tenant_id AS "TenantId",
                   invitation_id AS "InvitationId",
                   destination AS "Destination",
                   protected_payload AS "ProtectedPayload",
                   lease_token AS "LeaseToken"
               FROM odca.claim_notification(@worker, @maxAttempts);
            """,
            new { worker = Environment.MachineName, maxAttempts = _maxAttempts },
            cancellationToken: ct));
        if (item is null)
        {
            return;
        }

        try
        {
            var origin = configuration["Notifications:PublicOrigin"];
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var baseUri))
            {
                throw new InvalidOperationException("notification_configuration_invalid");
            }

            if (item.InvitationId.HasValue)
            {
                await DispatchInvitationAsync(baseUri, item, ct);
            }
            else
            {
                await DispatchUserNotificationAsync(baseUri, item, ct);
            }

            await connection.ExecuteAsync(new CommandDefinition(
                "SELECT odca.complete_notification(@id, @token, true, NULL, @maxAttempts);",
                new { id = item.Id, token = item.LeaseToken, maxAttempts = _maxAttempts },
                cancellationToken: ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var code = NormalizeErrorCode(ex);
            logger.LogWarning(
                "Falha controlada no envio {MessageId} correlation={Correlation}: {ErrorCode}",
                item.Id,
                item.TenantId,
                code);
            await connection.ExecuteAsync(new CommandDefinition(
                "SELECT odca.complete_notification(@id, @token, false, @code, @maxAttempts);",
                new { id = item.Id, token = item.LeaseToken, code, maxAttempts = _maxAttempts },
                cancellationToken: CancellationToken.None));
        }
    }

    private async Task DispatchInvitationAsync(Uri baseUri, OutboxItem item, CancellationToken ct)
    {
        var token = protector.Unprotect(item.ProtectedPayload);
        var link = new Uri(baseUri, $"convites/aceitar?invitationId={item.InvitationId:D}&token={Uri.EscapeDataString(token)}");
        await transport.SendInvitationAsync(item.Destination, link, ct);
    }

    private async Task DispatchUserNotificationAsync(Uri baseUri, OutboxItem item, CancellationToken ct)
    {
        var subject = _notifProtector.Unprotect(item.ProtectedPayload);
        var link = new Uri(baseUri, "/minha-conta/notificacoes");
        await transport.SendUserNotificationAsync(item.Destination, subject, link, ct);
    }

    private static string NormalizeErrorCode(Exception ex)
    {
        var raw = ex.Message;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "notification_delivery_failed";
        }

        var trimmed = raw.Trim();
        return trimmed.Length <= 80 ? trimmed : trimmed[..80];
    }

    private static int ResolveMaxAttempts(string? raw) =>
        int.TryParse(raw, out var value) && value >= 1 ? value : 5;

    private sealed record OutboxItem(
        Guid Id,
        Guid TenantId,
        Guid? InvitationId,
        string Destination,
        string ProtectedPayload,
        Guid LeaseToken);
}
