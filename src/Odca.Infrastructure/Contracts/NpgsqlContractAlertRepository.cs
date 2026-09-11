using Dapper;
using Npgsql;
using Odca.Application.Contracts;
using Odca.Domain.Contracts;

namespace Odca.Infrastructure.Contracts;

public sealed class NpgsqlContractAlertRepository(NpgsqlDataSource dataSource) : IContractAlertRepository
{
    public async Task EvaluateDueAlertsAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var tenants = (await connection.QueryAsync<TenantClock>(new CommandDefinition(
            """
            SELECT id AS "Id", timezone AS "Timezone"
              FROM odca.list_contract_alert_tenants();
            """,
            cancellationToken: cancellationToken))).AsList();

        foreach (var tenant in tenants)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await EvaluateTenantAsync(connection, tenant, cancellationToken);
        }
    }

    private static async Task EvaluateTenantAsync(
        NpgsqlConnection connection,
        TenantClock tenant,
        CancellationToken cancellationToken)
    {
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT set_config('odca.tenant_id', @tenantId, true);",
            new { tenantId = tenant.Id.ToString() },
            tx,
            cancellationToken: cancellationToken));

        var today = LocalToday(tenant.Timezone);
        var contracts = (await connection.QueryAsync<AlertContract>(new CommandDefinition(
            """
            SELECT id AS "Id",
                   title AS "Title",
                   owner_user_id AS "OwnerUserId",
                   end_date AS "EndDate",
                   is_indefinite AS "IsIndefinite",
                   term_cycle AS "TermCycle",
                   renewal_notice_days AS "RenewalNoticeDays",
                   operational_status AS "OperationalStatus"
              FROM odca.contracts
             WHERE tenant_id = @tenantId
               AND NOT is_deleted
               AND operational_status = 'active'
               AND NOT is_indefinite
               AND end_date IS NOT NULL;
            """,
            new { tenantId = tenant.Id },
            tx,
            cancellationToken: cancellationToken))).AsList();

        foreach (var contract in contracts)
        {
            if (contract.EndDate is null || contract.OwnerUserId is null)
            {
                continue;
            }

            var due = CalendarAlertSchedule.DueExpiryAlerts(contract.Id, contract.TermCycle, contract.EndDate.Value, today);
            foreach (var alert in due)
            {
                await UpsertNotificationAsync(
                    connection,
                    tx,
                    tenant.Id,
                    contract.OwnerUserId.Value,
                    category: "contract_expiry",
                    title: $"Contrato próximo do término: {contract.Title}",
                    body: $"O contrato \"{contract.Title}\" entra na janela de alerta {ContractAlertKindCodes.ToCode(alert.Kind)} (término {contract.EndDate:yyyy-MM-dd}).",
                    contract.Id,
                    alert.EventKey,
                    contract.EndDate,
                    cancellationToken);
            }

            var renewal = CalendarAlertSchedule.DueRenewalNotice(
                contract.Id,
                contract.TermCycle,
                contract.EndDate.Value,
                contract.RenewalNoticeDays,
                today);
            if (renewal is not null)
            {
                await UpsertNotificationAsync(
                    connection,
                    tx,
                    tenant.Id,
                    contract.OwnerUserId.Value,
                    category: "renewal_notice",
                    title: $"Aviso de renovação: {contract.Title}",
                    body: $"Chegou o prazo de aviso de renovação do contrato \"{contract.Title}\" (término {contract.EndDate:yyyy-MM-dd}).",
                    contract.Id,
                    renewal.EventKey,
                    contract.EndDate,
                    cancellationToken);
            }
        }

        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE odca.user_notifications n
               SET status = 'obsolete',
                   is_obsolete = true
              FROM odca.contracts c
             WHERE n.tenant_id = @tenantId
               AND n.resource_type = 'contract'
               AND n.resource_id = c.id
               AND c.tenant_id = n.tenant_id
               AND n.status = 'open'
               AND (
                    c.is_deleted
                    OR c.operational_status <> 'active'
                    OR split_part(n.event_key, ':', 2) <> c.term_cycle::text
               );
            """,
            new { tenantId = tenant.Id },
            tx,
            cancellationToken: cancellationToken));

        await tx.CommitAsync(cancellationToken);
    }

    private static async Task UpsertNotificationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction tx,
        Guid tenantId,
        Guid userId,
        string category,
        string title,
        string body,
        Guid contractId,
        string eventKey,
        DateOnly? relevantDate,
        CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO odca.user_notifications (
                tenant_id, user_id, category, title, body, resource_type, resource_id, event_key, relevant_date)
            VALUES (
                @tenantId, @userId, @category, @title, @body, 'contract', @contractId, @eventKey, @relevantDate)
            ON CONFLICT (tenant_id, user_id, event_key) DO NOTHING;
            """,
            new { tenantId, userId, category, title, body, contractId, eventKey, relevantDate },
            tx,
            cancellationToken: cancellationToken));
    }

    private static DateOnly LocalToday(string timezoneId)
    {
        TimeZoneInfo zone;
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
        }
        catch (InvalidTimeZoneException)
        {
            zone = TimeZoneInfo.Utc;
        }

        var local = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone);
        return DateOnly.FromDateTime(local.DateTime);
    }

    private sealed record TenantClock(Guid Id, string Timezone);

    private sealed record AlertContract(
        Guid Id,
        string Title,
        Guid? OwnerUserId,
        DateOnly? EndDate,
        bool IsIndefinite,
        int TermCycle,
        int? RenewalNoticeDays,
        string OperationalStatus);
}
