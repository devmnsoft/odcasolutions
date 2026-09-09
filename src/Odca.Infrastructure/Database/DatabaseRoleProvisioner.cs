using Npgsql;

namespace Odca.Infrastructure.Database;

public static class DatabaseRoleProvisioner
{
    public static async Task ProvisionApplicationLoginAsync(
        string adminConnectionString,
        string password,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            DO $odca$
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'odca_app_login') THEN
                    CREATE ROLE odca_app_login LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE INHERIT NOBYPASSRLS;
                END IF;
            END
            $odca$;
            SELECT set_config('odca.bootstrap_password', @password, false);
            DO $odca$
            BEGIN
                ALTER ROLE odca_app_login INHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;
                EXECUTE format('ALTER ROLE odca_app_login PASSWORD %L', current_setting('odca.bootstrap_password'));
                GRANT odca_app TO odca_app_login WITH INHERIT TRUE, SET FALSE;
            END
            $odca$;
            RESET odca.bootstrap_password;
            """;

        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("password", password);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
