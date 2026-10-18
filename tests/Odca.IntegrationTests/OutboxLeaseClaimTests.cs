using Dapper;
using Npgsql;

namespace Odca.IntegrationTests;

/// <summary>
/// D-OC38 (Sprint 4/F-3): triagem da suspeita de notificações duplicadas pela varredura de
/// workers concorrentes na fila da plataforma (triagem de solicitações à plataforma incluída).
/// Evidência executável do mecanismo de lease, que fundamenta a conclusão NÃO-defeito:
/// <c>claim_notification</c> só elegge linhas <c>pending</c> ou <c>leased</c> com lease expirado e
/// é atômica (<c>FOR UPDATE SKIP LOCKED LIMIT 1</c> por linha); <c>complete_notification</c> só
/// aplica com o token exato do lease ativo. Dados sintéticos, linhas removidas no final.
/// </summary>
public sealed class OutboxLeaseClaimTests(DatabaseFixture database) : IClassFixture<DatabaseFixture>
{
    private sealed record Claimed(Guid Id, Guid LeaseToken);

    private sealed record RowState(string Status, int Attempts, Guid? Token);

    [Fact]
    public async Task ConcurrentClaimsHandTheRowToExactlyOneWorker()
    {
        var (row, tenant) = await SeedPendingRowAsync();
        try
        {
            var workerA = ClaimSingleAsync("d38-f3-worker-a");
            var workerB = ClaimSingleAsync("d38-f3-worker-b");
            var (a, b) = (await workerA, await workerB);

            var claimed = new[] { a, b }.Where(x => x is not null).ToList();
            Assert.Single(claimed);
            Assert.Equal(row, claimed[0]!.Id);

            var state = await RowStateAsync(row);
            Assert.Equal("leased", state.Status);
            Assert.Equal(1, state.Attempts);
        }
        finally
        {
            await CleanupAsync(row, tenant);
        }
    }

    [Fact]
    public async Task ExpiredLeaseIsReclaimedAndStaleTokenCompletionIsIgnored()
    {
        var (row, tenant) = await SeedPendingRowAsync();
        try
        {
            var first = await ClaimSingleAsync("d38-f3-worker-a");
            Assert.NotNull(first);

            // Worker A morre sem completar: o lease expira e a linha volta a ser elegível.
            await using (var admin = await OpenAdminAsync())
                await admin.ExecuteAsync("UPDATE odca.notification_outbox SET lease_until=now()-interval '1 second' WHERE id=@id", new { id = row });

            var second = await ClaimSingleAsync("d38-f3-worker-b");
            Assert.Equal(row, second!.Id);
            Assert.NotEqual(first!.LeaseToken, second.LeaseToken);

            // O worker antigo tenta concluir com o token vencido: operação sem efeito.
            await using (var conn = await OpenAppAsync())
                await conn.ExecuteAsync("SELECT odca.complete_notification(@id,@token,false,'stale_worker',5)", new { id = row, token = first.LeaseToken });
            var afterStale = await RowStateAsync(row);
            Assert.Equal("leased", afterStale.Status);
            Assert.Equal(second.LeaseToken, afterStale.Token);

            // O detentor atual conclui com o próprio token: a linha vira sent uma única vez.
            await using (var conn2 = await OpenAppAsync())
                await conn2.ExecuteAsync("SELECT odca.complete_notification(@id,@token,true,NULL,5)", new { id = row, token = second.LeaseToken });
            var final = await RowStateAsync(row);
            Assert.Equal("sent", final.Status);
            Assert.Null(final.Token);
        }
        finally
        {
            await CleanupAsync(row, tenant);
        }
    }

    private async Task<(Guid Row, Guid Tenant)> SeedPendingRowAsync()
    {
        var tenant = Guid.NewGuid();
        var row = Guid.NewGuid();
        await using var admin = await OpenAdminAsync();
        await admin.ExecuteAsync("""
            INSERT INTO odca.tenants(id, business_code, display_name, status, timezone)
            VALUES (@tenant, @businessCode, 'F3-Lease ' || @marker, 'active', 'America/Sao_Paulo');
            INSERT INTO odca.notification_outbox(id, tenant_id, kind, destination, protected_payload, status)
            VALUES (@row, @tenant, 'user_notification', 'triagem-teste@odca.local', 'payload-sintetico', 'pending');
            """, new { tenant, row, businessCode = tenant.ToString("N")[..20], marker = tenant.ToString("N")[..6] });
        return (row, tenant);
    }

    private async Task CleanupAsync(Guid row, Guid tenant)
    {
        await using var admin = await OpenAdminAsync();
        await admin.ExecuteAsync("DELETE FROM odca.notification_outbox WHERE id=@id", new { id = row });
        await admin.ExecuteAsync("DELETE FROM odca.tenants WHERE id=@tenant AND display_name LIKE 'F3-Lease-%'", new { tenant });
    }

    private async Task<NpgsqlConnection> OpenAdminAsync()
    {
        var conn = new NpgsqlConnection(database.AdminConnectionString);
        await conn.OpenAsync();
        return conn;
    }

    private async Task<NpgsqlConnection> OpenAppAsync()
    {
        var conn = new NpgsqlConnection(database.AppConnectionString);
        await conn.OpenAsync();
        return conn;
    }

    private async Task<Claimed?> ClaimSingleAsync(string worker) => await Task.Run(async () =>
    {
        await using var conn = await OpenAppAsync();
        return await conn.QuerySingleOrDefaultAsync<Claimed>("SELECT id AS Id, lease_token AS LeaseToken FROM odca.claim_notification(@worker,5)", new { worker });
    });

    private async Task<RowState> RowStateAsync(Guid row)
    {
        await using var admin = await OpenAdminAsync();
        return await admin.QuerySingleAsync<RowState>(
            "SELECT status AS Status, attempt_count AS Attempts, lease_token AS Token FROM odca.notification_outbox WHERE id=@id", new { id = row });
    }
}
