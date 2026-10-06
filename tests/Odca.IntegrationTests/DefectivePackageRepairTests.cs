using Dapper;
using Npgsql;
using Odca.Infrastructure.Database;

namespace Odca.IntegrationTests;

public sealed class DefectivePackageRepairTests(DatabaseFixture database) : IClassFixture<DatabaseFixture>
{
    private const string V035Defective = "4f0db0a1a6d35529677816e5df0a28d3655027eff9302b379599fa9661d7240c";
    private const string V035Repaired = "35164be04924df0aa9f98c1d3f4c032e18621331893d6e0d4115c0193888ac03";

    [Fact]
    public async Task PreReleaseV035HistoryIsRepairedToTheReleasedPackage()
    {
        await SetV035ChecksumAsync(V035Defective);

        await ApplyMigrationsAsync();

        Assert.Equal(V035Repaired, await GetV035ChecksumAsync());
        var argumentNames = await GetConsumeArgumentNamesAsync();
        Assert.Contains("requested_quantity", argumentNames);
        Assert.Contains("requested_key", argumentNames);
        Assert.Contains("requested_actor", argumentNames);
    }

    [Fact]
    public async Task RepairIsIdempotentOnceTheReleasedChecksumIsStored()
    {
        await SetV035ChecksumAsync(V035Defective);
        await ApplyMigrationsAsync();
        await ApplyMigrationsAsync();

        Assert.Equal(V035Repaired, await GetV035ChecksumAsync());
    }

    private async Task SetV035ChecksumAsync(string checksum)
    {
        await using var connection = new NpgsqlConnection(database.AdminConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            "UPDATE odca.schema_migrations SET checksum = @checksum WHERE version = 35;",
            new { checksum });
    }

    private async Task<string> GetV035ChecksumAsync()
    {
        await using var connection = new NpgsqlConnection(database.AdminConnectionString);
        await connection.OpenAsync();
        return (await connection.ExecuteScalarAsync<string>(
            "SELECT checksum FROM odca.schema_migrations WHERE version = 35;"))!;
    }

    private async Task<string[]> GetConsumeArgumentNamesAsync()
    {
        await using var connection = new NpgsqlConnection(database.AdminConnectionString);
        await connection.OpenAsync();
        return (string[])(await connection.ExecuteScalarAsync<object>(
            """
            SELECT p.proargnames
              FROM pg_proc AS p
              JOIN pg_namespace AS n ON n.oid = p.pronamespace
             WHERE n.nspname = 'odca' AND p.proname = 'consume_monthly_franchise';
            """))!;
    }

    private Task ApplyMigrationsAsync()
    {
        var sqlPath = FindFile("database", "odca.sql");
        return new DatabaseMigrator(sqlPath).ApplyAsync(database.AdminConnectionString);
    }

    private static string FindFile(params string[] segments)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine([current.FullName, .. segments]);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, segments));
    }
}
