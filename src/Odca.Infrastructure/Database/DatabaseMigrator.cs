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
        ValidateChecksums(sql);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 120 };
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public static void ValidateChecksums(string sql)
    {
        var matches = MigrationPattern.Matches(sql);
        if (matches.Count == 0)
        {
            throw new InvalidDataException("Nenhum bloco de migração ODCA foi encontrado.");
        }

        foreach (Match match in matches)
        {
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
        }
    }
}
