using Dapper;
using Npgsql;
using Odca.Application.Signatures;

namespace Odca.Worker;

/// <summary>
/// D-OC37 (Sprint 3/A-1): reconcilia envelopes externos de assinatura — consulta o provedor
/// sobre o estado de envelopes "sent" e aplica a transição com evento deduplicado (a tabela de
/// eventos é única por source_event_id). No-op enquanto o provedor for "none". As funções
/// varridas são SECURITY DEFINER (padrão claim_notification), mantendo a varredura cross-tenant
/// compatível com a RLS da aplicação.
/// </summary>
public sealed class SignatureEnvelopeWorker(
    NpgsqlDataSource dataSource,
    IConfiguration configuration,
    ISignatureEnvelopeProvider provider,
    ILogger<SignatureEnvelopeWorker> logger) : BackgroundService
{
    private DateTime lastSweep = DateTime.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        try { do { await SweepOnceAsync(stoppingToken); } while (await timer.WaitForNextTickAsync(stoppingToken)); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { logger.LogInformation("Worker de reconciliação de envelopes de assinatura encerrado pelo host."); }
    }

    internal async Task SweepOnceAsync(CancellationToken ct)
    {
        if (string.Equals(provider.Code, "none", StringComparison.Ordinal)) return;
        var interval = TimeSpan.FromSeconds(configuration.GetValue("Signatures:ReconcileIntervalSeconds", 30));
        if (DateTime.UtcNow - lastSweep < interval) return;
        lastSweep = DateTime.UtcNow;

        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(ct);
            var pending = (await connection.QueryAsync<PendingEnvelope>(new CommandDefinition(
                "SELECT tenant_id AS TenantId,envelope_id AS EnvelopeId,envelope_ref AS EnvelopeRef FROM odca.signature_envelope_pending_list(20)", cancellationToken: ct))).AsList();
            foreach (var item in pending)
            {
                try
                {
                    var state = await provider.GetStateAsync(item.TenantId, item.EnvelopeRef, ct);
                    if (state is null) continue;
                    var result = await connection.ExecuteScalarAsync<string>(new CommandDefinition(
                        "SELECT odca.signature_envelope_apply_state(@tenantId,@envelopeId,@status,@source,@occurredAt)",
                        new
                        {
                            item.TenantId,
                            item.EnvelopeId,
                            status = state.Status,
                            source = $"{item.EnvelopeRef}:{state.Status}@{state.OccurredAt:O}",
                            state.OccurredAt,
                        }, cancellationToken: ct));
                    if (result == "transitioned")
                        logger.LogInformation("Envelope {Ref} transitou para {Status}.", item.EnvelopeRef, state.Status);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception exception)
                {
                    logger.LogWarning(exception, "Falha ao reconciliar o envelope {Ref}.", item.EnvelopeRef);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger.LogError(exception, "Falha transitória na varredura de envelopes de assinatura.");
        }
    }

    private sealed record PendingEnvelope(Guid TenantId, Guid EnvelopeId, string EnvelopeRef);
}
