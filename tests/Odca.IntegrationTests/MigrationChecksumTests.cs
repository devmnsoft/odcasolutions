using Odca.Infrastructure.Database;

namespace Odca.IntegrationTests;

public sealed class MigrationChecksumTests
{
    [Fact]
    public void CanonicalSqlHasValidChecksum()
    {
        DatabaseMigrator.ValidateChecksums(File.ReadAllText(FindSql()));
    }

    [Fact]
    public void CanonicalSqlDetectsTampering()
    {
        var tampered = File.ReadAllText(FindSql())
            .Replace("S00 foundation", "tampered", StringComparison.Ordinal);

        Assert.Throws<InvalidDataException>(() => DatabaseMigrator.ValidateChecksums(tampered));
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
