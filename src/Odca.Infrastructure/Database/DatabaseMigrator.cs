using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Npgsql;

namespace Odca.Infrastructure.Database;

public sealed class DatabaseMigrator(string sqlPath)
{
    private static readonly Regex MigrationPattern = new(
        @"-- ODCA-MIGRATION (?<version>\d{3}) CHECKSUM (?<checksum>[a-f0-9]{64})\r?\n(?<body>.*?)-- ODCA-END \k<version>",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.CultureInvariant);

    public async Task ApplyAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(sqlPath))
        {
            throw new FileNotFoundException("O SQL canônico não foi encontrado.", sqlPath);
        }

        var sql = await File.ReadAllTextAsync(sqlPath, cancellationToken);
        var migrations = ParseAndValidate(sql);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await ExecuteAsync(connection, "SELECT pg_advisory_lock(hashtext('odca.schema.migrations'));", cancellationToken);

        Exception? primaryFailure = null;
        try
        {
            var historyExists = await ScalarAsync<bool>(
                connection,
                "SELECT to_regclass('odca.schema_migrations') IS NOT NULL;",
                cancellationToken);
            var history = historyExists
                ? await LoadHistoryAsync(connection, cancellationToken)
                : [];
            await RepairKnownDefectivePackageAsync(connection, migrations, history, cancellationToken);
            ValidateHistory(migrations, history);

            foreach (var migration in migrations)
            {
                if (history.TryGetValue(migration.Version, out _))
                {
                    continue;
                }

                await ExecuteAsync(connection, migration.Body, cancellationToken);

                var stored = await ScalarAsync<string?>(
                    connection,
                    $"SELECT checksum FROM odca.schema_migrations WHERE version = {migration.Version};",
                    cancellationToken);
                if (!string.Equals(stored?.Trim(), migration.Checksum, StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        $"A migração {migration.Version:000} não registrou o checksum esperado.");
                }

                history[migration.Version] = migration.Checksum;
            }
        }
        catch (Exception exception)
        {
            primaryFailure = exception;
            if (connection.FullState.HasFlag(System.Data.ConnectionState.Open))
            {
                try
                {
                    await ExecuteAsync(connection, "ROLLBACK;", CancellationToken.None);
                }
                catch (Exception rollbackException)
                {
                    exception.Data["OdcaMigrationRollbackFailure"] = rollbackException.Message;
                }
            }

            throw;
        }
        finally
        {
            if (connection.FullState.HasFlag(System.Data.ConnectionState.Open))
            {
                try
                {
                    await ExecuteAsync(connection, "SELECT pg_advisory_unlock(hashtext('odca.schema.migrations'));", CancellationToken.None);
                }
                catch (Exception unlockException) when (primaryFailure is not null)
                {
                    primaryFailure.Data["OdcaMigrationUnlockFailure"] = unlockException.Message;
                }
            }
        }
    }

    // v009 was distributed with a PostgreSQL 42P13 error: the input and an OUT
    // column of preview_tenant_invitation were both named invitation_id.  This
    // is the only accepted checksum transition; arbitrary divergence still
    // fails in ValidateHistory.  The immutable release snapshot remains the
    // record of the defective package, while the canonical installer contains
    // the corrected parameter name.
    private static async Task RepairKnownDefectivePackageAsync(
        NpgsqlConnection connection,
        IReadOnlyList<MigrationBlock> migrations,
        IDictionary<int, string> history,
        CancellationToken cancellationToken)
    {
        const int version = 9;
        const string defective = "8ea94f7cafca871d9a1923a8383eaf89a7535f2749efa550e6930bfcf7a7982b";
        const string repaired = "f2d7bc3a2ed44f1e0601b29d911cd4af4fba774a4d47a2020962519d094209f2";
        if (!history.TryGetValue(version, out var stored) ||
            !string.Equals(stored.Trim(), defective, StringComparison.Ordinal) ||
            !migrations.Any(x => x.Version == version && x.Checksum == repaired))
        {
            return;
        }

        await ExecuteAsync(connection, $"""
            BEGIN;
            DROP FUNCTION IF EXISTS odca.preview_tenant_invitation(uuid,text);
            CREATE OR REPLACE FUNCTION odca.preview_tenant_invitation(p_invitation_id uuid, presented_hash text)
            RETURNS TABLE(invitation_id uuid, organization_name text, role_name text, recipient_email text, expires_at timestamptz, status text)
            LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, odca AS $function$
            DECLARE locked_tenant_id uuid;
            BEGIN
              SELECT i.tenant_id INTO locked_tenant_id FROM odca.tenant_invitations i WHERE i.id = p_invitation_id;
              IF FOUND THEN PERFORM odca.expire_tenant_invitations(locked_tenant_id); END IF;
              RETURN QUERY SELECT i.id, t.display_name, r.display_name, i.recipient_email, i.expires_at, i.status
                FROM odca.tenant_invitations i JOIN odca.tenants t ON t.id = i.tenant_id
                JOIN odca.roles r ON r.id = i.role_id AND r.tenant_id = i.tenant_id
               WHERE i.id = p_invitation_id AND i.token_hash = presented_hash
                 AND i.status IN ('pending','sent') AND i.expires_at > now();
            END; $function$;
            REVOKE ALL ON FUNCTION odca.preview_tenant_invitation(uuid,text) FROM PUBLIC;
            GRANT EXECUTE ON FUNCTION odca.preview_tenant_invitation(uuid,text) TO odca_app;
            UPDATE odca.schema_migrations SET checksum = '{repaired}'
             WHERE version = {version} AND checksum = '{defective}';
            COMMIT;
            """, cancellationToken);
        history[version] = repaired;
    }

    public static void ValidateChecksums(string sql)
        => _ = ParseAndValidate(sql);

    private static void ValidateHistory(
        IReadOnlyList<MigrationBlock> migrations,
        IReadOnlyDictionary<int, string> history)
    {
        var known = migrations.ToDictionary(migration => migration.Version);
        foreach (var applied in history)
        {
            if (!known.TryGetValue(applied.Key, out var migration))
            {
                throw new InvalidDataException(
                    $"O banco contém a migração desconhecida {applied.Key:000}; o código pode estar desatualizado.");
            }

            if (!string.Equals(applied.Value.Trim(), migration.Checksum, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Histórico divergente na migração {applied.Key:000}: banco {applied.Value.Trim()}, arquivo {migration.Checksum}.");
            }
        }

        var highestApplied = history.Keys.DefaultIfEmpty(0).Max();
        var missing = migrations.FirstOrDefault(migration =>
            migration.Version <= highestApplied && !history.ContainsKey(migration.Version));
        if (missing is not null)
        {
            throw new InvalidDataException(
                $"O histórico do banco tem uma lacuna na migração {missing.Version:000}.");
        }
    }

    private static async Task<Dictionary<int, string>> LoadHistoryAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT version, checksum FROM odca.schema_migrations ORDER BY version;",
            connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new Dictionary<int, string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(reader.GetInt32(0), reader.GetString(1));
        }

        return result;
    }

    private static List<MigrationBlock> ParseAndValidate(string sql)
    {
        var matches = MigrationPattern.Matches(sql);
        if (matches.Count == 0)
        {
            throw new InvalidDataException("Nenhum bloco de migração ODCA foi encontrado.");
        }

        var migrations = new List<MigrationBlock>(matches.Count);
        var versions = new HashSet<int>();
        var previousVersion = 0;
        var cursor = 0;
        foreach (Match match in matches)
        {
            EnsureOnlyCommentsOutsideBlocks(sql[cursor..match.Index]);
            cursor = match.Index + match.Length;

            var version = int.Parse(match.Groups["version"].Value, System.Globalization.CultureInfo.InvariantCulture);
            if (!versions.Add(version))
            {
                throw new InvalidDataException($"A versão de migração {version:000} está duplicada.");
            }

            if (version <= previousVersion)
            {
                throw new InvalidDataException("Os blocos de migração devem estar em ordem crescente.");
            }

            previousVersion = version;
            var declared = match.Groups["checksum"].Value;
            var normalizedBody = match.Groups["body"].Value
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace(declared, "REPLACE_WITH_SHA256", StringComparison.Ordinal);
            var actual = Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(normalizedBody)))
                .ToLowerInvariant();

            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(actual),
                    Encoding.ASCII.GetBytes(declared)))
            {
                throw new InvalidDataException(
                    $"Checksum inválido na migração {match.Groups["version"].Value}: esperado {declared}, obtido {actual}.");
            }

            migrations.Add(new MigrationBlock(version, declared, match.Groups["body"].Value));
        }

        EnsureOnlyCommentsOutsideBlocks(sql[cursor..]);
        return migrations;
    }

    private static void EnsureOnlyCommentsOutsideBlocks(string value)
    {
        var lines = value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n');
        if (lines.Any(line => line.Contains("ODCA-MIGRATION", StringComparison.Ordinal) ||
                              line.Contains("ODCA-END", StringComparison.Ordinal)))
        {
            throw new InvalidDataException("Há um marcador de migração inválido ou incompleto.");
        }

        var executable = string.Join('\n', lines.Where(line =>
            !string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith("--", StringComparison.Ordinal)));
        if (!string.IsNullOrWhiteSpace(executable))
        {
            throw new InvalidDataException("Há SQL executável fora de um bloco de migração rastreado.");
        }
    }

    private static async Task ExecuteAsync(
        NpgsqlConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 120 };
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<T> ScalarAsync<T>(
        NpgsqlConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 30 };
        return (T)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private sealed record MigrationBlock(int Version, string Checksum, string Body);
}
