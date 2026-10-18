using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Odca.Configuration.Localization;

namespace Odca.Api;

/// <summary>
/// B.1 (D-OC31): enfileira o e-mail de um evento de negócio em
/// odca.notification_outbox dentro da transação do chamador (evento + outbox juntos),
/// respeitando as preferências por canal (enabled/channel_email, por tipo) e o e-mail
/// do usuário. Nunca lança: uma falha aqui é registrada como log e preserva a
/// consistência da operação de negócio (a outbox tem retry próprio). Quando o ponto
/// de chamada não mantém transação aberta (envelopes de função SQL executam em
/// autocommit), o overload sem NpgsqlTransaction abre uma transação curta com o GUC
/// do tenant definido para satisfazer o RLS da outbox.
/// </summary>
public static partial class NotificationEnqueue
{
    /// <summary>
    /// D-OC46 (Sprint 12/F-1): idioma preferido do destinatário nas notificações
    /// (odca.user_notification_settings), normalizado com fallback pt-BR. Na variante
    /// com transação o chamador já definiu o GUC do tenant; sem transação abre uma
    /// transação curta com o GUC (mesmo padrão dos enqueues sem NpgsqlTransaction).
    /// Nunca lança: resolução indisponível cai em pt-BR, que é a camada canônica.
    /// </summary>
    public static async Task<string> RecipientLanguageAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? tx,
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default)
    {
        try
        {
            var raw = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
                "SELECT preferred_language FROM odca.user_notification_settings WHERE tenant_id=@tenantId AND user_id=@userId",
                new { tenantId, userId }, transaction: tx, cancellationToken: ct));
            return LocalizationService.ResolveCultureCode(raw) ?? "pt-BR";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return "pt-BR"; }
    }

    public static async Task<string> RecipientLanguageAsync(
        NpgsqlConnection connection,
        Guid tenantId,
        Guid actorId,
        Guid userId,
        CancellationToken ct)
    {
        try
        {
            await using var tx = await connection.BeginTransactionAsync(ct);
            await connection.ExecuteAsync(new CommandDefinition(
                "SELECT set_config('odca.tenant_id',@tenant::text,true),set_config('odca.user_id',@actor::text,true)",
                new { tenant = tenantId, actor = actorId }, transaction: tx, cancellationToken: ct));
            var language = await RecipientLanguageAsync(connection, tx, tenantId, userId, ct);
            await tx.CommitAsync(ct);
            return language;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return "pt-BR"; }
    }

    public static async Task<bool> EnqueueEmailAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction tx,
        Guid tenantId,
        Guid recipientId,
        string kind,
        string title,
        IDataProtector protector,
        Guid? sourceId,
        ILogger logger,
        CancellationToken ct = default)
    {
        try
        {
            var state = await connection.QuerySingleOrDefaultAsync<ChannelState>(new CommandDefinition(
                "SELECT enabled AS Enabled, channel_in_app AS InApp, channel_email AS Email FROM odca.notification_channel_state(@tenantId,@userId,@kind)",
                new { tenantId, userId = recipientId, kind }, transaction: tx, cancellationToken: ct));
            if (state is null || !state.Enabled || !state.Email) return false;

            var email = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
                "SELECT email FROM odca.users WHERE id=@userId",
                new { userId = recipientId }, transaction: tx, cancellationToken: ct));
            if (string.IsNullOrWhiteSpace(email)) return false;

            // D-OC46 (Sprint 12/F-1): o idioma do destinatário viaja com a mensagem (v062) para
            // o transporte compor o corpo SMTP no idioma certo; o subject já chega localizado
            // no payload protegido quando o chamador usa as chaves do catálogo.
            var language = await RecipientLanguageAsync(connection, tx, tenantId, recipientId, ct);

            var payload = protector.Protect(title);
            if (sourceId is null)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO odca.notification_outbox(tenant_id,invitation_id,kind,destination,protected_payload,language)
                    VALUES(@tenantId,NULL,@kind,@email,@payload,@language)
                    """, new { tenantId, kind, email, payload, language }, transaction: tx, cancellationToken: ct));
            }
            else
            {
                // Identidade determinística opcional: reprocessamento do mesmo evento
                // nunca duplica a mensagem (ex.: replay idempotente do endpoint).
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO odca.notification_outbox(tenant_id,invitation_id,kind,destination,protected_payload,language,source_id)
                    VALUES(@tenantId,NULL,@kind,@email,@payload,@language,@sourceId)
                    ON CONFLICT(source_id) WHERE source_id IS NOT NULL DO NOTHING
                    """, new { tenantId, kind, email, payload, language, sourceId }, transaction: tx, cancellationToken: ct));
            }
            return true;
        }
        catch (Exception ex)
        {
            EnqueueFailure(logger, kind, recipientId, tenantId, ex.Message);
            return false;
        }
    }

    public static async Task<bool> EnqueueEmailAsync(
        NpgsqlConnection connection,
        Guid tenantId,
        Guid actorId,
        Guid recipientId,
        string kind,
        string title,
        IDataProtector protector,
        Guid? sourceId,
        ILogger logger,
        CancellationToken ct = default)
    {
        try
        {
            await using var tx = await connection.BeginTransactionAsync(ct);
            await connection.ExecuteAsync(new CommandDefinition(
                "SELECT set_config('odca.tenant_id',@tenant::text,true),set_config('odca.user_id',@actor::text,true)",
                new { tenant = tenantId, actor = actorId }, transaction: tx, cancellationToken: ct));
            var queued = await EnqueueEmailAsync(connection, tx, tenantId, recipientId, kind, title, protector, sourceId, logger, ct);
            await tx.CommitAsync(ct);
            return queued;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            TransactionFailure(logger, kind, recipientId, ex.Message);
            return false;
        }
    }

    /// <summary>
    /// D-OC38 (Sprint 4/F-1): materializa a linha do sino (odca.user_notifications) para um
    /// membro da organização dentro da transação do chamador, respeitando as preferências por
    /// canal (enabled/channel_in_app, por tipo). Deduplicação determinística por source_id
    /// (v059): replay ou concorrência do mesmo evento nunca duplica a linha. Nunca lança:
    /// falha é registrada como log e preserva a operação de negócio. Destinatários sem
    /// membership ativo não têm linha de sino para materializar (eventos de plataforma).
    /// </summary>
    public static async Task<bool> EnqueueInAppAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction tx,
        Guid tenantId,
        Guid recipientId,
        string kind,
        string title,
        string body,
        Guid? sourceId,
        ILogger logger,
        CancellationToken ct = default)
    {
        try
        {
            var isMember = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                "SELECT EXISTS(SELECT 1 FROM odca.memberships WHERE tenant_id=@tenantId AND user_id=@userId AND status='active')",
                new { tenantId, userId = recipientId }, transaction: tx, cancellationToken: ct));
            if (!isMember) return false;

            var state = await connection.QuerySingleOrDefaultAsync<ChannelState>(new CommandDefinition(
                "SELECT enabled AS Enabled, channel_in_app AS InApp, channel_email AS Email FROM odca.notification_channel_state(@tenantId,@userId,@kind)",
                new { tenantId, userId = recipientId, kind }, transaction: tx, cancellationToken: ct));
            if (state is null || !state.Enabled || !state.InApp) return false;

            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO odca.user_notifications(tenant_id,user_id,kind,title,body,channel,source_id)
                VALUES(@tenantId,@userId,@kind,@title,@body,'in_app',@sourceId)
                ON CONFLICT(tenant_id,source_id) WHERE source_id IS NOT NULL DO NOTHING
                """, new { tenantId, userId = recipientId, kind, title, body, sourceId }, transaction: tx, cancellationToken: ct));
            return true;
        }
        catch (Exception ex)
        {
            InAppEnqueueFailure(logger, kind, recipientId, tenantId, ex.Message);
            return false;
        }
    }

    public static async Task<bool> EnqueueInAppAsync(
        NpgsqlConnection connection,
        Guid tenantId,
        Guid actorId,
        Guid recipientId,
        string kind,
        string title,
        string body,
        Guid? sourceId,
        ILogger logger,
        CancellationToken ct = default)
    {
        try
        {
            await using var tx = await connection.BeginTransactionAsync(ct);
            await connection.ExecuteAsync(new CommandDefinition(
                "SELECT set_config('odca.tenant_id',@tenant::text,true),set_config('odca.user_id',@actor::text,true)",
                new { tenant = tenantId, actor = actorId }, transaction: tx, cancellationToken: ct));
            var queued = await EnqueueInAppAsync(connection, tx, tenantId, recipientId, kind, title, body, sourceId, logger, ct);
            await tx.CommitAsync(ct);
            return queued;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            InAppTransactionFailure(logger, kind, recipientId, ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Resolve o protetor de dados (compartilhado com o Worker pelo mesmo repositório de
    /// chaves) e um logger a partir do provedor de serviços do request. Centraliza a
    /// resolução para os controladores, que constroem o helper por chamada.
    /// </summary>
    public static (IDataProtector Protector, ILogger Logger) Resolve(IServiceProvider services) =>
        (services.GetRequiredService<IDataProtector>(),
         services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(NotificationEnqueue)));

    /// <summary>
    /// Identidade determinística de mensagem de outbox (SHA-256 truncado a 16 bytes):
    /// o mesmo evento produz sempre o mesmo source_id, o que torna a deduplicação por
    /// ON CONFLICT(source_id) válida mesmo em replay de endpoints.
    /// </summary>
    public static Guid DeterministicSource(string scope, params object[] parts)
    {
        var raw = string.Join('|', new[] { scope }.Concat(parts.Select(p => Convert.ToString(p, CultureInfo.InvariantCulture) ?? string.Empty)));
        return new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(raw)).AsSpan(0, 16).ToArray());
    }

    [LoggerMessage(EventId = 2401, Level = LogLevel.Warning, Message = "Falha ao enfileirar e-mail do tipo {Kind} para o usuário {UserId} na organização {TenantId}: {Reason}.", SkipEnabledCheck = true)]
    private static partial void EnqueueFailure(ILogger logger, string kind, Guid userId, Guid tenantId, string reason);

    [LoggerMessage(EventId = 2402, Level = LogLevel.Warning, Message = "Falha na transação de enfileiramento do e-mail do tipo {Kind} para o usuário {UserId}: {Reason}.", SkipEnabledCheck = true)]
    private static partial void TransactionFailure(ILogger logger, string kind, Guid userId, string reason);

    [LoggerMessage(EventId = 2403, Level = LogLevel.Warning, Message = "Falha ao materializar a notificação in-app do tipo {Kind} para o usuário {UserId} na organização {TenantId}: {Reason}.", SkipEnabledCheck = true)]
    private static partial void InAppEnqueueFailure(ILogger logger, string kind, Guid userId, Guid tenantId, string reason);

    [LoggerMessage(EventId = 2404, Level = LogLevel.Warning, Message = "Falha na transação da notificação in-app do tipo {Kind} para o usuário {UserId}: {Reason}.", SkipEnabledCheck = true)]
    private static partial void InAppTransactionFailure(ILogger logger, string kind, Guid userId, string reason);

    private sealed record ChannelState(bool Enabled, bool InApp, bool Email);
}
