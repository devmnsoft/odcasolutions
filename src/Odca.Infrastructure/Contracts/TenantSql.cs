using Dapper;
using Npgsql;

namespace Odca.Infrastructure.Contracts;

internal static class TenantSql
{
    public static async Task<NpgsqlTransaction?> BeginAuthorizedAsync(
        NpgsqlConnection connection,
        Guid actorId,
        Guid tenantId,
        string permission,
        CancellationToken cancellationToken)
    {
        var tx = await connection.BeginTransactionAsync(cancellationToken);
        var allowed = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT odca.tenant_actor_has_permission(@actorId, @tenantId, @permission);",
            new { actorId, tenantId, permission },
            tx,
            cancellationToken: cancellationToken));
        if (!allowed)
        {
            await tx.RollbackAsync(cancellationToken);
            await tx.DisposeAsync();
            return null;
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT set_config('odca.tenant_id', @tenantId, true);",
            new { tenantId = tenantId.ToString() },
            tx,
            cancellationToken: cancellationToken));
        return tx;
    }
}
