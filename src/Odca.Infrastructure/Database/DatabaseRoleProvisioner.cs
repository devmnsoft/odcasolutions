using Npgsql;

namespace Odca.Infrastructure.Database;

public static class DatabaseRoleProvisioner
{
    private const string DefaultApplicationLogin = "odca_app_login";

    public static async Task ProvisionApplicationLoginAsync(
        string adminConnectionString,
        string password,
        string loginRoleName = DefaultApplicationLogin,
        CancellationToken cancellationToken = default)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(loginRoleName, "^[a-z][a-z0-9_]{0,62}$"))
        {
            throw new ArgumentException("Nome da role de aplicação inválido.", nameof(loginRoleName));
        }

        const string sql = """
            DO $odca$
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = current_setting('odca.bootstrap_role')) THEN
                    EXECUTE format(
                        'CREATE ROLE %I LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE INHERIT NOBYPASSRLS',
                        current_setting('odca.bootstrap_role'));
                END IF;
            END
            $odca$;
            DO $odca$
            BEGIN
                EXECUTE format(
                    'ALTER ROLE %I INHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS',
                    current_setting('odca.bootstrap_role'));
                EXECUTE format(
                    'ALTER ROLE %I PASSWORD %L',
                    current_setting('odca.bootstrap_role'),
                    current_setting('odca.bootstrap_password'));
                EXECUTE format(
                    'GRANT odca_app TO %I WITH INHERIT TRUE, SET FALSE',
                    current_setting('odca.bootstrap_role'));
            END
            $odca$;
            """;

        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var roleCommand = new NpgsqlCommand(
            "SELECT set_config('odca.bootstrap_role', @roleName, true), set_config('odca.bootstrap_password', @password, true);",
            connection,
            transaction))
        {
            roleCommand.Parameters.AddWithValue("roleName", loginRoleName);
            roleCommand.Parameters.AddWithValue("password", password);
            await roleCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
