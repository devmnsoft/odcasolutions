using Dapper;
using Microsoft.AspNetCore.DataProtection;
using Npgsql;

namespace Odca.Worker;

public sealed class ObligationReminderWorker(
    NpgsqlDataSource dataSource,
    IDataProtectionProvider protectionProvider,
    ILogger<ObligationReminderWorker> logger) : BackgroundService
{
    private readonly Guid owner = Guid.NewGuid();
    private readonly IDataProtector _protector = protectionProvider.CreateProtector("ODCA.UserNotification.v1");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        try { do { await ProcessAsync(stoppingToken); } while (await timer.WaitForNextTickAsync(stoppingToken)); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { logger.LogInformation("Worker de lembretes encerrado pelo host."); }
    }

    private async Task ProcessAsync(CancellationToken ct)
    {
        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(ct);
            var token = Guid.NewGuid();
            var item = await connection.QuerySingleOrDefaultAsync<Reminder>(new CommandDefinition(
                "SELECT id AS Id,tenant_id AS TenantId,obligation_id AS ObligationId,recipient_id AS RecipientId FROM odca.claim_obligation_reminder(@owner,@token)",
                new { owner, token }, cancellationToken: ct));
            if (item is null) return;

            // Atomic handoff: complete + outbox enqueue in the same transaction.
            // If either fails or the process crashes, both roll back and the lease
            // will expire, allowing re-claim on the next tick.
            await using var tx = await connection.BeginTransactionAsync(ct);
            var completed = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                "SELECT odca.complete_obligation_reminder(@id,@token)", new { item.Id, token }, transaction: tx, cancellationToken: ct));
            if (!completed)
            {
                await tx.RollbackAsync(ct);
                logger.LogWarning("Lease do lembrete {ReminderId} expirou antes da conclusão.", item.Id);
                return;
            }

            await MaybeEnqueueNotificationEmailAsync(connection, tx, item.TenantId, item.RecipientId, item.Id, ct);
            await tx.CommitAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception exception) { logger.LogError(exception, "Falha transitória no processamento de lembrete interno."); }
    }

    private async Task MaybeEnqueueNotificationEmailAsync(NpgsqlConnection connection, NpgsqlTransaction tx, Guid tenantId, Guid userId, Guid sourceId, CancellationToken ct)
    {
        try
        {
            var state = await connection.QuerySingleAsync<ChannelState>(new CommandDefinition(
                "SELECT enabled AS Enabled, channel_in_app AS InApp, channel_email AS Email FROM odca.notification_channel_state(@tenantId,@userId,'obligation_due')",
                new { tenantId, userId }, transaction: tx, cancellationToken: ct));
            if (!state.Enabled || !state.Email) return;

            var email = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
                "SELECT email FROM odca.users WHERE id=@userId",
                new { userId }, transaction: tx, cancellationToken: ct));
            if (string.IsNullOrWhiteSpace(email)) return;

            var title = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
                "SELECT title FROM odca.user_notifications WHERE tenant_id=@tenantId AND user_id=@userId AND kind='obligation_due' ORDER BY created_at DESC LIMIT 1",
                new { tenantId, userId }, transaction: tx, cancellationToken: ct));
            if (string.IsNullOrWhiteSpace(title)) return;

            var protectedPayload = _protector.Protect(title);
            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO odca.notification_outbox(tenant_id,invitation_id,kind,destination,protected_payload,source_id) VALUES(@tenantId,NULL,'obligation_due',@email,@payload,@sourceId) ON CONFLICT(source_id) WHERE source_id IS NOT NULL DO NOTHING",
                new { tenantId, email, payload = protectedPayload, sourceId }, transaction: tx, cancellationToken: ct));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha ao enfileirar e-mail de notificação para {UserId} na organização {TenantId}.", userId, tenantId);
        }
    }

    private sealed record Reminder(Guid Id, Guid TenantId, Guid ObligationId, Guid RecipientId);
    private sealed record ChannelState(bool Enabled, bool InApp, bool Email);
}
