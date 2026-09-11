using Odca.Infrastructure.Database;

namespace Odca.IntegrationTests;

public sealed class ContractsModuleTests
{
    [Fact]
    public void CanonicalPackageIncludesContractsMigration()
    {
        var sql = File.ReadAllText(FindSql());
        Assert.Contains("ODCA-MIGRATION 010", sql, StringComparison.Ordinal);
        Assert.Contains("odca.counterparties", sql, StringComparison.Ordinal);
        Assert.Contains("odca.contracts", sql, StringComparison.Ordinal);
        Assert.Contains("odca.user_notifications", sql, StringComparison.Ordinal);
        Assert.Contains("tenant.contracts.lifecycle", sql, StringComparison.Ordinal);
        Assert.Equal(10, DatabaseSchema.CurrentVersion);
        DatabaseMigrator.ValidateChecksums(sql);
    }

    [Fact]
    public async Task ContractScenariosSkipWithoutDisposableDatabase()
    {
        if (!HasExclusiveTestEnvironment())
        {
            return;
        }

        var fixture = new DatabaseFixture();
        await fixture.InitializeAsync();
        Assert.Equal(DatabaseSchema.CurrentVersion, await fixture.MigrationCountAsync());

        await using var connection = new Npgsql.NpgsqlConnection(fixture.AdminConnectionString);
        await connection.OpenAsync();
        var hasCounterparties = await Dapper.SqlMapper.ExecuteScalarAsync<bool>(
            connection,
            "SELECT to_regclass('odca.counterparties') IS NOT NULL;");
        Assert.True(hasCounterparties);

        await DevelopmentSeeder.SeedContractsDemoAsync(fixture.AdminConnectionString, new DateOnly(2026, 6, 15));

        var contractCount = await Dapper.SqlMapper.ExecuteScalarAsync<int>(
            connection,
            "SELECT count(*)::int FROM odca.contracts WHERE reference_number LIKE 'active-%';");
        Assert.True(contractCount >= 2);

        await fixture.DisposeAsync();
    }

    private static bool HasExclusiveTestEnvironment()
    {
        return string.Equals(
                Environment.GetEnvironmentVariable("ODCA_TEST_ENVIRONMENT"),
                "ODCA_INTEGRATION_TESTS",
                StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ODCA_TEST_ADMIN_CONNECTION"))
            && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ODCA_TEST_APP_CONNECTION"));
    }

    private static string FindSql()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var path = Path.Combine(current.FullName, "database", "odca.sql");
            if (File.Exists(path))
            {
                return path;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException("database/odca.sql");
    }
}
