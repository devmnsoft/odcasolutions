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
        var canonical = File.ReadAllText(FindSql()).Replace("\r\n", "\n");
        var snapshot = File.ReadAllText(Path.Combine(Path.GetDirectoryName(FindSql())!, "releases", "odca-v002.sql")).Replace("\r\n", "\n");

        DatabaseMigrator.ValidateChecksums(snapshot);
        Assert.DoesNotContain("ODCA-MIGRATION 003", snapshot, StringComparison.Ordinal);
        Assert.StartsWith(snapshot.TrimEnd(), canonical, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseV003IsAnImmutablePrefixOfCanonicalSql()
    {
        var canonical = File.ReadAllText(FindSql()).Replace("\r\n", "\n");
        var snapshot = File.ReadAllText(Path.Combine(Path.GetDirectoryName(FindSql())!, "releases", "odca-v003.sql")).Replace("\r\n", "\n");

        DatabaseMigrator.ValidateChecksums(snapshot);
        Assert.DoesNotContain("ODCA-MIGRATION 004", snapshot, StringComparison.Ordinal);
        Assert.StartsWith(snapshot.TrimEnd(), canonical, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseV004IsAnImmutablePrefixOfCanonicalSql()
    {
        var canonical = File.ReadAllText(FindSql()).Replace("\r\n", "\n");
        var snapshot = File.ReadAllText(Path.Combine(Path.GetDirectoryName(FindSql())!, "releases", "odca-v004.sql")).Replace("\r\n", "\n");

        DatabaseMigrator.ValidateChecksums(snapshot);
        Assert.DoesNotContain("ODCA-MIGRATION 005", snapshot, StringComparison.Ordinal);
        Assert.StartsWith(snapshot.TrimEnd(), canonical, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseV005IsAnImmutablePrefixOfCanonicalSql()
    {
        var canonical = File.ReadAllText(FindSql()).Replace("\r\n", "\n");
        var snapshot = File.ReadAllText(Path.Combine(Path.GetDirectoryName(FindSql())!, "releases", "odca-v005.sql")).Replace("\r\n", "\n");

        DatabaseMigrator.ValidateChecksums(snapshot);
        Assert.DoesNotContain("ODCA-MIGRATION 006", snapshot, StringComparison.Ordinal);
        Assert.StartsWith(snapshot.TrimEnd(), canonical, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseV006MatchesCanonicalSql()
    {
        var canonical = File.ReadAllText(FindSql()).Replace("\r\n", "\n");
        var snapshot = File.ReadAllText(Path.Combine(Path.GetDirectoryName(FindSql())!, "releases", "odca-v006.sql")).Replace("\r\n", "\n");

        Assert.StartsWith(snapshot.TrimEnd(), canonical, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseV007IsAnImmutablePrefixOfCanonicalSql()
    {
        var canonical = File.ReadAllText(FindSql()).Replace("\r\n", "\n");
        var snapshot = File.ReadAllText(Path.Combine(Path.GetDirectoryName(FindSql())!, "releases", "odca-v007.sql")).Replace("\r\n", "\n");
        Assert.StartsWith(snapshot, canonical);
    }

    [Fact]
    public void ReleaseV008IsAnImmutablePrefixOfCanonicalSql()
    {
        var canonical = File.ReadAllText(FindSql()).Replace("\r\n", "\n");
        var snapshot = File.ReadAllText(Path.Combine(Path.GetDirectoryName(FindSql())!, "releases", "odca-v008.sql")).Replace("\r\n", "\n");

        DatabaseMigrator.ValidateChecksums(snapshot);
        Assert.Contains("ODCA-MIGRATION 008", snapshot, StringComparison.Ordinal);
        Assert.DoesNotContain("ODCA-MIGRATION 009", snapshot, StringComparison.Ordinal);
        Assert.StartsWith(snapshot.TrimEnd(), canonical, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseV009PreservesTheDefectiveDistributedPackageForAudit()
    {
        var canonical = FindSql();
        var snapshot = Path.Combine(Path.GetDirectoryName(canonical)!, "releases", "odca-v009.sql");
        Assert.True(File.Exists(snapshot));
        var snapshotSql = File.ReadAllText(snapshot);
        Assert.Contains("ODCA-MIGRATION 009", snapshotSql, StringComparison.Ordinal);
        DatabaseMigrator.ValidateChecksums(snapshotSql);
        Assert.Contains("preview_tenant_invitation(invitation_id uuid", snapshotSql, StringComparison.Ordinal);
        Assert.Contains("preview_tenant_invitation(p_invitation_id uuid", File.ReadAllText(canonical), StringComparison.Ordinal);
        Assert.NotEqual(File.ReadAllBytes(canonical), File.ReadAllBytes(snapshot));
    }

    [Fact]
    public void ReleaseV010IsAnImmutablePrefixOfCanonicalSql()
    {
        var canonical = FindSql();
        var snapshot = Path.Combine(Path.GetDirectoryName(canonical)!, "releases", "odca-v010.sql");
        Assert.StartsWith(File.ReadAllText(snapshot).TrimEnd(), File.ReadAllText(canonical), StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseV011IsAnImmutablePrefixOfCanonicalSql()
    {
        var canonical = FindSql();
        var snapshot = Path.Combine(Path.GetDirectoryName(canonical)!, "releases", "odca-v011.sql");
        DatabaseMigrator.ValidateChecksums(File.ReadAllText(snapshot));
        Assert.StartsWith(File.ReadAllText(snapshot).TrimEnd(), File.ReadAllText(canonical), StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseV012IsAnImmutablePrefixOfCanonicalSql()
    {
        var canonical = FindSql();
        var snapshot = Path.Combine(Path.GetDirectoryName(canonical)!, "releases", "odca-v012.sql");
        DatabaseMigrator.ValidateChecksums(File.ReadAllText(snapshot));
        Assert.StartsWith(File.ReadAllText(snapshot).TrimEnd(), File.ReadAllText(canonical), StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseV013IsAnImmutablePrefixOfCanonicalSql()
    {
        var canonical = FindSql();
        var snapshot = Path.Combine(Path.GetDirectoryName(canonical)!, "releases", "odca-v013.sql");
        DatabaseMigrator.ValidateChecksums(File.ReadAllText(snapshot));
        Assert.StartsWith(File.ReadAllText(snapshot).TrimEnd(), File.ReadAllText(canonical), StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseV014IsAnImmutablePrefixOfCanonicalSql()
    {
        var canonical = FindSql();
        var snapshot = Path.Combine(Path.GetDirectoryName(canonical)!, "releases", "odca-v014.sql");
        DatabaseMigrator.ValidateChecksums(File.ReadAllText(snapshot));
        Assert.Contains(File.ReadAllText(snapshot).TrimEnd(), File.ReadAllText(canonical), StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseV015IsAnImmutablePrefixOfCanonicalSql()
    {
        var canonical = FindSql();
        var snapshot = Path.Combine(Path.GetDirectoryName(canonical)!, "releases", "odca-v015.sql");
        DatabaseMigrator.ValidateChecksums(File.ReadAllText(snapshot));
        Assert.StartsWith(File.ReadAllText(snapshot).TrimEnd(), File.ReadAllText(canonical), StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseV016IsAnImmutablePrefixOfCanonicalSql()
    {
        var canonical = FindSql();
        var snapshot = Path.Combine(Path.GetDirectoryName(canonical)!, "releases", "odca-v016.sql");
        DatabaseMigrator.ValidateChecksums(File.ReadAllText(snapshot));
        Assert.StartsWith(File.ReadAllText(snapshot).TrimEnd(), File.ReadAllText(canonical), StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseV017IsAnImmutablePrefixOfCanonicalSql()
    {
        var canonical = FindSql();
        var snapshot = Path.Combine(Path.GetDirectoryName(canonical)!, "releases", "odca-v017.sql");
        DatabaseMigrator.ValidateChecksums(File.ReadAllText(snapshot));
        Assert.StartsWith(File.ReadAllText(snapshot).TrimEnd(), File.ReadAllText(canonical), StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseV018IsAnImmutablePrefixOfCanonicalSql()
    {
        var canonical = FindSql();
        var snapshot = Path.Combine(Path.GetDirectoryName(canonical)!, "releases", "odca-v018.sql");
        DatabaseMigrator.ValidateChecksums(File.ReadAllText(snapshot));
        Assert.StartsWith(File.ReadAllText(snapshot).TrimEnd(), File.ReadAllText(canonical), StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseV019IsAnImmutablePrefixOfCanonicalSql()
    {
        var canonical = FindSql();
        var snapshot = Path.Combine(Path.GetDirectoryName(canonical)!, "releases", "odca-v019.sql");
        DatabaseMigrator.ValidateChecksums(File.ReadAllText(snapshot));
        Assert.StartsWith(
            File.ReadAllText(snapshot).TrimEnd(),
            File.ReadAllText(canonical),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseV020IsAnImmutablePrefixOfCanonicalSql()
    {
        var canonical = FindSql();
        var snapshot = Path.Combine(Path.GetDirectoryName(canonical)!, "releases", "odca-v020.sql");

        var packaged = File.ReadAllText(snapshot);
        var canonicalSql = File.ReadAllText(canonical);
        DatabaseMigrator.ValidateChecksums(packaged);
        // v020 was deliberately packaged as a single migration (and with a UTF-8
        // BOM), rather than as the historical prefix used by the other releases.
        // Compare the exact bytes that the migrator hashes after ReadAllText: no
        // newline or SQL normalization is allowed here.
        Assert.Equal(MigrationBlock(canonicalSql, 20), MigrationBlock(packaged, 20));
        Assert.DoesNotContain("ODCA-MIGRATION 019", packaged, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseV021IsAnImmutablePrefixOfCanonicalSql()
    {
        var canonical = FindSql();
        var snapshot = Path.Combine(Path.GetDirectoryName(canonical)!, "releases", "odca-v021.sql");
        DatabaseMigrator.ValidateChecksums(File.ReadAllText(snapshot));
        Assert.StartsWith(File.ReadAllText(snapshot).TrimEnd(), File.ReadAllText(canonical), StringComparison.Ordinal);
    }

    [Fact]
    public void CurrentReleaseMatchesCanonicalSqlAndRuntimeVersion()
    {
        var canonical = FindSql();
        var snapshot = Path.Combine(Path.GetDirectoryName(canonical)!, "releases", $"odca-v{DatabaseSchema.CurrentVersion:000}.sql");

        Assert.True(File.Exists(snapshot));
        DatabaseMigrator.ValidateChecksums(File.ReadAllText(snapshot));
        Assert.Equal(File.ReadAllText(canonical), File.ReadAllText(snapshot));
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

    private static string MigrationBlock(string sql, int version)
    {
        var marker = $"-- ODCA-MIGRATION {version:000} ";
        var endMarker = $"-- ODCA-END {version:000}";
        var start = sql.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Marcador {marker} ausente.");
        var end = sql.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(end >= 0, $"Marcador {endMarker} ausente.");
        return sql[start..(end + endMarker.Length)];
    }
}
