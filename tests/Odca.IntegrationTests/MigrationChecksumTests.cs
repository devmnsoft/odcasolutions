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

    [Fact]
    public void CanonicalSqlRejectsExecutableTextOutsideTrackedBlocks()
    {
        var sql = File.ReadAllText(FindSql()) + Environment.NewLine + "SELECT 1;";

        Assert.Throws<InvalidDataException>(() => DatabaseMigrator.ValidateChecksums(sql));
    }

    [Fact]
    public void CanonicalSqlRejectsDuplicateVersions()
    {
        var sql = File.ReadAllText(FindSql());
        var firstBlockStart = sql.IndexOf("-- ODCA-MIGRATION", StringComparison.Ordinal);
        var firstBlockEnd = sql.IndexOf("-- ODCA-END 001", StringComparison.Ordinal) + "-- ODCA-END 001".Length;
        var duplicate = sql + Environment.NewLine + sql[firstBlockStart..firstBlockEnd];

        Assert.Throws<InvalidDataException>(() => DatabaseMigrator.ValidateChecksums(duplicate));
    }

    [Fact]
    public void ReleaseV002IsAnImmutablePrefixOfCanonicalSql()
    {
        var canonical = FindSql();
        var snapshot = Path.Combine(Path.GetDirectoryName(canonical)!, "releases", "odca-v002.sql");

        var snapshotSql = File.ReadAllText(snapshot);
        DatabaseMigrator.ValidateChecksums(snapshotSql);
        Assert.DoesNotContain("ODCA-MIGRATION 003", snapshotSql, StringComparison.Ordinal);
        Assert.StartsWith(snapshotSql.TrimEnd(), File.ReadAllText(canonical), StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseV003IsAnImmutablePrefixOfCanonicalSql()
    {
        var canonical = FindSql();
        var snapshot = Path.Combine(Path.GetDirectoryName(canonical)!, "releases", "odca-v003.sql");

        var snapshotSql = File.ReadAllText(snapshot);
        DatabaseMigrator.ValidateChecksums(snapshotSql);
        Assert.DoesNotContain("ODCA-MIGRATION 004", snapshotSql, StringComparison.Ordinal);
        Assert.StartsWith(snapshotSql.TrimEnd(), File.ReadAllText(canonical), StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseV004IsAnImmutablePrefixOfCanonicalSql()
    {
        var canonical = FindSql();
        var snapshot = Path.Combine(Path.GetDirectoryName(canonical)!, "releases", "odca-v004.sql");

        var snapshotSql = File.ReadAllText(snapshot);
        DatabaseMigrator.ValidateChecksums(snapshotSql);
        Assert.DoesNotContain("ODCA-MIGRATION 005", snapshotSql, StringComparison.Ordinal);
        Assert.StartsWith(snapshotSql.TrimEnd(), File.ReadAllText(canonical), StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseV005MatchesCanonicalSql()
    {
        var canonical = FindSql();
        var snapshot = Path.Combine(Path.GetDirectoryName(canonical)!, "releases", "odca-v005.sql");

        Assert.Equal(File.ReadAllBytes(canonical), File.ReadAllBytes(snapshot));
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
