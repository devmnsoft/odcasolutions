using Odca.Infrastructure.Database;

namespace Odca.IntegrationTests;

public sealed class TenancyInvitationTests
{
    [Fact]
    public void CanonicalPackageIncludesInvitationLifecycleMigration()
    {
        var sql = File.ReadAllText(FindSql());
        Assert.Contains("ODCA-MIGRATION 009", sql, StringComparison.Ordinal);
        Assert.Contains("expire_tenant_invitations", sql, StringComparison.Ordinal);
        Assert.Contains("ensure_not_removing_last_admin", sql, StringComparison.Ordinal);
        Assert.Contains("'expired'", sql, StringComparison.Ordinal);
        Assert.True(DatabaseSchema.CurrentVersion >= 16);
        DatabaseMigrator.ValidateChecksums(sql);
    }

    [Fact]
    public async Task InvitationQuotaAndAcceptScenariosRequireDisposableDatabase()
    {
        Assert.True(HasExclusiveTestEnvironment(),
            "Teste PostgreSQL essencial não foi executado. Defina ODCA_TEST_ENVIRONMENT=ODCA_INTEGRATION_TESTS, ODCA_TEST_ADMIN_CONNECTION e ODCA_TEST_APP_CONNECTION para um banco odca_test* descartável.");

        var fixture = new DatabaseFixture();
        await fixture.InitializeAsync();
        Assert.Equal(DatabaseSchema.CurrentVersion, await fixture.MigrationCountAsync());
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
