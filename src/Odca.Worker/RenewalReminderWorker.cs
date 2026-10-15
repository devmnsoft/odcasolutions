using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.AspNetCore.DataProtection;
using Npgsql;

namespace Odca.Worker;

/// <summary>
/// B.1 (D-OC31): lembretes de renovação contratual. Varre os contratos dentro dos
/// dias configurados do tenant (tenants.renewal_reminder_days), conclui o lembrete na
/// mesma transação do enfileiramento do e-mail (sem duplicar mensagens confirmadas)
/// e respeita as preferências por canal (kind 'renewal_due'), como os lembretes de
/// obrigação.
/// </summary>
public sealed class RenewalReminderWorker(
    NpgsqlDataSource dataSource,
    IDataProtectionProvider protectionProvider,
    ILogger<RenewalReminderWorker> logger) : BackgroundService
{
    private readonly string workerName = $"renewal-reminders@{Environment.MachineName}";
    private readonly IDataProtector _protector = protectionProvider.CreateProtector("ODCA.UserNotification.v1");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        try { do { await ProcessAsync(stoppingToken); } while (await timer.WaitForNextTickAsync(stoppingToken)); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { logger.LogInformation("Worker de lembretes de renovação encerrado pelo host."); }
    }

    private async Task ProcessAsync(CancellationToken ct)
    {
        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(ct);
            var token = Guid.NewGuid();
            var item = await connection.QuerySingleOrDefaultAsync<Reminder>(new CommandDefinition(
                "SELECT id AS Id,tenant_id AS TenantId,contract_id AS ContractId FROM odca.claim_renewal_reminder(@worker,@token)",
                new { worker = workerName, token }, cancellationToken: ct));
            if (item is null) return;

            // Handoff atômico: complete + enfileiramento do e-mail na mesma transação.
            // Se qualquer passo falhar ou o processo cair, tudo volta e o lease expira,
            // permitindo novo claim no próximo tick sem duplicar o e-mail confirmado.
            await using var tx = await connection.BeginTransactionAsync(ct);
            var recipients = (await connection.QueryAsync<Recipient>(new CommandDefinition(
                "SELECT recipient_id AS RecipientId,title AS Title FROM odca.complete_renewal_reminder(@id,@token)",
                new { item.Id, token }, transaction: tx, cancellationToken: ct))).ToList();
            if (recipients.Count == 0)
            {
                await tx.RollbackAsync(ct);
                logger.LogWarning("Lease do lembrete de renovação {ReminderId} expirou antes da conclusão.", item.Id);
                return;
            }

            foreach (var recipient in recipients)
            {
                await MaybeEnqueueNotificationEmailAsync(connection, tx, item.TenantId, item.Id, recipient.RecipientId, recipient.Title, ct);
            }
            await tx.CommitAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception exception) { logger.LogError(exception, "Falha transitória no processamento de lembrete de renovação."); }
    }

    private async Task MaybeEnqueueNotificationEmailAsync(NpgsqlConnection connection, NpgsqlTransaction tx, Guid tenantId, Guid reminderId, Guid userId, string title, CancellationToken ct)
    {
        try
        {
            var state = await connection.QuerySingleAsync<ChannelState>(new CommandDefinition(
                "SELECT enabled AS Enabled, channel_in_app AS InApp, channel_email AS Email FROM odca.notification_channel_state(@tenantId,@userId,'renewal_due')",
                new { tenantId, userId }, transaction: tx, cancellationToken: ct));
            if (!state.Enabled || !state.Email) return;

            var email = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
                "SELECT email FROM odca.users WHERE id=@userId",
                new { userId }, transaction: tx, cancellationToken: ct));
            if (string.IsNullOrWhiteSpace(email)) return;

            // Identidade determinística (lembrete × destinatário): reprocessamento após
            // expiração de lease nunca duplica uma mensagem já aceita pela outbox.
            var sourceId = SourceIdFor(reminderId, userId);
            var protectedPayload = _protector.Protect(title);
            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO odca.notification_outbox(tenant_id,invitation_id,kind,destination,protected_payload,source_id) VALUES(@tenantId,NULL,'renewal_due',@email,@payload,@sourceId) ON CONFLICT(source_id) WHERE source_id IS NOT NULL DO NOTHING",
                new { tenantId, email, payload = protectedPayload, sourceId }, transaction: tx, cancellationToken: ct));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha ao enfileirar e-mail de renovação para {UserId} na organização {TenantId}.", userId, tenantId);
        }
    }

    private static Guid SourceIdFor(Guid reminderId, Guid recipientId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{reminderId:D}:{recipientId:D}"));
        return new Guid(hash.AsSpan(0, 16).ToArray());
    }

    private sealed record Reminder(Guid Id, Guid TenantId, Guid ContractId);
    private sealed record Recipient(Guid RecipientId, string Title);
    private sealed record ChannelState(bool Enabled, bool InApp, bool Email);
}
