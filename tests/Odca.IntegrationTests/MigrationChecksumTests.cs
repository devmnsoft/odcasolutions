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
    [Fact]
    public void ReleaseV025PreservesItsHistoricalMigrations()
    {
        var canonical = File.ReadAllText(FindSql()).Replace("\r\n", "\n");
        var snapshot = File.ReadAllText(Path.Combine(Path.GetDirectoryName(FindSql())!, "releases", "odca-v025.sql")).Replace("\r\n", "\n");
        DatabaseMigrator.ValidateChecksums(snapshot);
        DatabaseMigrator.ValidateChecksums(canonical);
        for (var version = 1; version <= 25; version++)
            Assert.Equal(MigrationBlock(snapshot, version), MigrationBlock(canonical, version));
    }

    [Fact]
    public void ReleaseV026PreservesItsHistoricalMigrations()
    {
        var canonical = File.ReadAllText(FindSql()).Replace("\r\n", "\n");
        var snapshot = File.ReadAllText(Path.Combine(Path.GetDirectoryName(FindSql())!, "releases", "odca-v026.sql")).Replace("\r\n", "\n");
        DatabaseMigrator.ValidateChecksums(snapshot);
        DatabaseMigrator.ValidateChecksums(canonical);
        for (var version = 1; version <= 26; version++)
            Assert.Equal(MigrationBlock(snapshot, version), MigrationBlock(canonical, version));
    }

    [Fact]
    public void ReleaseV028PreservesItsHistoricalMigrations()
    {
        var canonical = File.ReadAllText(FindSql()).Replace("\r\n", "\n");
        var snapshot = File.ReadAllText(Path.Combine(Path.GetDirectoryName(FindSql())!, "releases", "odca-v028.sql")).Replace("\r\n", "\n");
        DatabaseMigrator.ValidateChecksums(snapshot);
        DatabaseMigrator.ValidateChecksums(canonical);
        for (var version = 1; version <= 28; version++)
            Assert.Equal(MigrationBlock(snapshot, version), MigrationBlock(canonical, version));
    }

    [Fact]
    public void ReleaseV029PreservesItsHistoricalMigrations()
    {
        var canonical = File.ReadAllText(FindSql()).Replace("\r\n", "\n");
        var snapshot = File.ReadAllText(Path.Combine(Path.GetDirectoryName(FindSql())!, "releases", "odca-v029.sql")).Replace("\r\n", "\n");
        DatabaseMigrator.ValidateChecksums(snapshot);
        DatabaseMigrator.ValidateChecksums(canonical);
        for (var version = 1; version <= 29; version++)
            Assert.Equal(MigrationBlock(snapshot, version), MigrationBlock(canonical, version));
    }

    [Fact]
    public void ReleaseV031PreservesItsHistoricalMigrations()
    {
        var canonical = File.ReadAllText(FindSql()).Replace("\r\n", "\n");
        var snapshot = File.ReadAllText(Path.Combine(Path.GetDirectoryName(FindSql())!, "releases", "odca-v031.sql")).Replace("\r\n", "\n");
        DatabaseMigrator.ValidateChecksums(snapshot);
        DatabaseMigrator.ValidateChecksums(canonical);
        for (var version = 1; version <= 31; version++)
            Assert.Equal(MigrationBlock(snapshot, version), MigrationBlock(canonical, version));
    }

    [Fact]
    public void ReleaseV032PreservesItsHistoricalMigrations()
    {
        var canonical = File.ReadAllText(FindSql()).Replace("\r\n", "\n");
        var snapshot = File.ReadAllText(Path.Combine(Path.GetDirectoryName(FindSql())!, "releases", "odca-v032.sql")).Replace("\r\n", "\n");
        DatabaseMigrator.ValidateChecksums(snapshot);
        DatabaseMigrator.ValidateChecksums(canonical);
        for (var version = 1; version <= 32; version++)
            Assert.Equal(MigrationBlock(snapshot, version), MigrationBlock(canonical, version));
    }

    [Fact]
    public void ReleaseV033AddsPlatformAuditPageAndMatchesCanonicalSql()
    {
        var canonical = File.ReadAllText(FindSql()).Replace("\r\n", "\n");
        var snapshot = File.ReadAllText(Path.Combine(Path.GetDirectoryName(FindSql())!, "releases", "odca-v033.sql")).Replace("\r\n", "\n");
        DatabaseMigrator.ValidateChecksums(snapshot);
        DatabaseMigrator.ValidateChecksums(canonical);
        for (var version = 1; version <= 33; version++)
            Assert.Equal(MigrationBlock(snapshot, version), MigrationBlock(canonical, version));
        Assert.Contains("CREATE OR REPLACE FUNCTION odca.platform_audit_page(", snapshot, StringComparison.Ordinal);
        Assert.Contains("CREATE OR REPLACE FUNCTION odca.platform_audit_count(", snapshot, StringComparison.Ordinal);
        Assert.Contains("GRANT EXECUTE ON FUNCTION odca.platform_audit_page(uuid, integer, integer, text, uuid) TO odca_app;", snapshot, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseV034AddsHierarchyAndPlanModulesWithoutRewritingEarlierMigrations()
    {
        var canonical = File.ReadAllText(FindSql()).Replace("\r\n", "\n");
        var previous = File.ReadAllText(Path.Combine(Path.GetDirectoryName(FindSql())!, "releases", "odca-v033.sql")).Replace("\r\n", "\n");
        var snapshot = File.ReadAllText(Path.Combine(Path.GetDirectoryName(FindSql())!, "releases", "odca-v034.sql")).Replace("\r\n", "\n");
        DatabaseMigrator.ValidateChecksums(snapshot);
        DatabaseMigrator.ValidateChecksums(canonical);
        for (var version = 1; version <= 33; version++)
            Assert.Equal(MigrationBlock(previous, version), MigrationBlock(canonical, version));
        Assert.Contains("CREATE OR REPLACE FUNCTION odca.ensure_tenant_standard_roles(target_tenant uuid)", snapshot, StringComparison.Ordinal);
        Assert.Contains("CREATE OR REPLACE FUNCTION odca.preview_organization_plan_change(actor uuid, requested_tenant uuid, requested_plan text)", snapshot, StringComparison.Ordinal);
        Assert.Contains("module.patients", snapshot, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseV035MetersMonthlyFranchisesWithoutRewritingEarlierMigrations()
    {
        var canonical = File.ReadAllText(FindSql()).Replace("\r\n", "\n");
        var previous = File.ReadAllText(Path.Combine(Path.GetDirectoryName(FindSql())!, "releases", "odca-v034.sql")).Replace("\r\n", "\n");
        var snapshot = File.ReadAllText(Path.Combine(Path.GetDirectoryName(FindSql())!, "releases", "odca-v035.sql")).Replace("\r\n", "\n");
        DatabaseMigrator.ValidateChecksums(snapshot);
        DatabaseMigrator.ValidateChecksums(canonical);
        for (var version = 1; version <= 34; version++)
            Assert.Equal(MigrationBlock(previous, version), MigrationBlock(canonical, version));
        Assert.Contains("CREATE OR REPLACE FUNCTION odca.consume_monthly_franchise(", snapshot, StringComparison.Ordinal);
        Assert.Contains("tenant.status <> 'suspended'", snapshot, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseV042PreservesTheDefectiveDeclaredChecksumForAudit()
    {
        var canonical = File.ReadAllText(FindSql()).Replace("\r\n", "\n");
        var previous = File.ReadAllText(Path.Combine(Path.GetDirectoryName(FindSql())!, "releases", "odca-v041.sql")).Replace("\r\n", "\n");
        var snapshot = File.ReadAllText(Path.Combine(Path.GetDirectoryName(FindSql())!, "releases", "odca-v042.sql")).Replace("\r\n", "\n");
        DatabaseMigrator.ValidateChecksums(previous);
        DatabaseMigrator.ValidateChecksums(canonical);
        for (var version = 1; version <= 41; version++)
            Assert.Equal(MigrationBlock(previous, version), MigrationBlock(canonical, version));
        // The distributed v042 package declared cfb7990e…, which did not match the
        // SHA-256 of its own normalized body (901a50fc…). The immutable snapshot keeps
        // the defective package for audit; the canonical installer carries the repaired
        // declaration, and the migrator re-stamps the stored history row.
        Assert.Contains("ODCA-MIGRATION 042 CHECKSUM cfb7990e2d98c35a3825857952b4489397c9c58c91150d2028a7c7b86880947f", snapshot, StringComparison.Ordinal);
        Assert.Contains("'cfb7990e2d98c35a3825857952b4489397c9c58c91150d2028a7c7b86880947f'", snapshot, StringComparison.Ordinal);
        Assert.DoesNotContain("ODCA-MIGRATION 043", snapshot, StringComparison.Ordinal);
        Assert.Contains("ODCA-MIGRATION 042 CHECKSUM 901a50fc9b4cf02bc18189b602df973102ae754f27926511700281eadb75ee9e", canonical, StringComparison.Ordinal);
        Assert.NotEqual(File.ReadAllBytes(FindSql()), File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(FindSql())!, "releases", "odca-v042.sql")));
    }

    [Fact]
    public void ReleaseV052AllowsInboxListingTypeWithoutRewritingEarlierMigrations()
    {
        var canonical = File.ReadAllText(FindSql()).Replace("\r\n", "\n");
        var previous = File.ReadAllText(Path.Combine(Path.GetDirectoryName(FindSql())!, "releases", "odca-v051.sql")).Replace("\r\n", "\n");
        var snapshot = File.ReadAllText(Path.Combine(Path.GetDirectoryName(FindSql())!, "releases", "odca-v052.sql")).Replace("\r\n", "\n");
        DatabaseMigrator.ValidateChecksums(previous);
        DatabaseMigrator.ValidateChecksums(snapshot);
        DatabaseMigrator.ValidateChecksums(canonical);
        for (var version = 1; version <= 50; version++)
            Assert.Equal(MigrationBlock(previous, version), MigrationBlock(canonical, version));
        // v051 shipped with malformed dollar quotes (double backslash before the quote
        // tag) that PostgreSQL rejects with 42601, so no fresh database could apply the
        // released file. The immutable snapshot keeps the defective package for audit;
        // the canonical installer carries the repaired body and the migrator re-stamps
        // databases still holding the defective checksum.
        Assert.Contains("ODCA-MIGRATION 051 CHECKSUM d9951f97d49f319c25120c18a801ca4d18732b6629841b055e48e970ebd4df28", previous, StringComparison.Ordinal);
        Assert.Contains("DO \\\\$", previous, StringComparison.Ordinal);
        Assert.DoesNotContain("ODCA-MIGRATION 052", previous, StringComparison.Ordinal);
        Assert.Contains("ODCA-MIGRATION 051 CHECKSUM 1f8eaf7e802419d64ae1f45bc60b0031b7f600a9173db8cfb584a7c06241069a", canonical, StringComparison.Ordinal);
        Assert.NotEqual(File.ReadAllBytes(FindSql()), File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(FindSql())!, "releases", "odca-v051.sql")));
        Assert.Contains("CHECK(listing_type IN('obligations','reviews','contracts','inbox'))", snapshot, StringComparison.Ordinal);
        Assert.DoesNotContain("ODCA-MIGRATION 053", snapshot, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseV053ExtendsPlatformAuditPolicyForOfficialCatalogSeed()
    {
        var canonical = File.ReadAllText(FindSql()).Replace("\r\n", "\n");
        var previous = File.ReadAllText(Path.Combine(Path.GetDirectoryName(FindSql())!, "releases", "odca-v052.sql")).Replace("\r\n", "\n");
        var snapshot = File.ReadAllText(Path.Combine(Path.GetDirectoryName(FindSql())!, "releases", "odca-v053.sql")).Replace("\r\n", "\n");
        DatabaseMigrator.ValidateChecksums(previous);
        DatabaseMigrator.ValidateChecksums(snapshot);
        DatabaseMigrator.ValidateChecksums(canonical);
        for (var version = 1; version <= 52; version++)
            Assert.Equal(MigrationBlock(previous, version), MigrationBlock(canonical, version));
        // D-OC26: o seeding do catálogo oficial grava 'template.official_catalog_seeded'
        // como auditoria de escopo platform; a policy anterior só admitia 'identity.%'
        // e o INSERT falhava com 42501 quando a API roda como a role da aplicação.
        Assert.Contains("action LIKE 'identity.%' OR action = 'template.official_catalog_seeded'", canonical, StringComparison.Ordinal);
        Assert.Contains("action LIKE 'identity.%' OR action = 'template.official_catalog_seeded'", snapshot, StringComparison.Ordinal);
        Assert.DoesNotContain("ODCA-MIGRATION 054", snapshot, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseV054AddsSharedStoresNotificationOpsRenewalRemindersAndEvidenceLink()
    {
        var canonical = File.ReadAllText(FindSql()).Replace("\r\n", "\n");
        var previous = File.ReadAllText(Path.Combine(Path.GetDirectoryName(FindSql())!, "releases", "odca-v053.sql")).Replace("\r\n", "\n");
        var snapshot = File.ReadAllText(Path.Combine(Path.GetDirectoryName(FindSql())!, "releases", "odca-v054.sql")).Replace("\r\n", "\n");
        DatabaseMigrator.ValidateChecksums(previous);
        DatabaseMigrator.ValidateChecksums(snapshot);
        DatabaseMigrator.ValidateChecksums(canonical);
        for (var version = 1; version <= 53; version++)
            Assert.Equal(MigrationBlock(previous, version), MigrationBlock(canonical, version));
        // D-OC31: stores compartilhados em PostgreSQL para sessao/tickets e chaves de
        // Data Protection (operacao multi-instancia); limite de tentativas da outbox
        // configuravel; operacao de notificacoes para superadministrador com retry
        // auditado; lembretes de renovacao pelos dias configuraveis do tenant; vinculo
        // opcional de documento do acervo ao registro de assinatura por evidencia.
        Assert.Contains("CREATE TABLE IF NOT EXISTS odca.distributed_cache_entries(", canonical, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS odca.data_protection_keys(", canonical, StringComparison.Ordinal);
        Assert.Contains("CREATE OR REPLACE FUNCTION odca.claim_notification(worker_id text, max_attempts integer DEFAULT 5)", canonical, StringComparison.Ordinal);
        Assert.Contains("CREATE OR REPLACE FUNCTION odca.notification_ops_page(p_actor uuid", canonical, StringComparison.Ordinal);
        Assert.Contains("CREATE OR REPLACE FUNCTION odca.retry_notification(p_message_id uuid, p_actor uuid)", canonical, StringComparison.Ordinal);
        Assert.Contains("action LIKE 'platform.notifications.%'", canonical, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS odca.renewal_reminders(", canonical, StringComparison.Ordinal);
        Assert.Contains("ADD COLUMN IF NOT EXISTS evidence_document_version_id uuid", canonical, StringComparison.Ordinal);
        Assert.DoesNotContain("ODCA-MIGRATION 055", snapshot, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseV055FixesRetryNotificationPlainUpdateReturningWithoutRewritingEarlierMigrations()
    {
        var canonical = File.ReadAllText(FindSql()).Replace("\r\n", "\n");
        var previous = File.ReadAllText(Path.Combine(Path.GetDirectoryName(FindSql())!, "releases", "odca-v054.sql")).Replace("\r\n", "\n");
        var snapshot = File.ReadAllText(Path.Combine(Path.GetDirectoryName(FindSql())!, "releases", "odca-v055.sql")).Replace("\r\n", "\n");
        DatabaseMigrator.ValidateChecksums(previous);
        DatabaseMigrator.ValidateChecksums(snapshot);
        DatabaseMigrator.ValidateChecksums(canonical);
        for (var version = 1; version <= 54; version++)
            Assert.Equal(MigrationBlock(previous, version), MigrationBlock(canonical, version));
        // D-OC31 (GATE): a v054 definiu retry_notification com 'UPDATE ... RETURNING 1;'
        // como instrucao simples em PL/pgSQL, construcao rejeitada pelo PostgreSQL com
        // 42601 ("query has no destination for result data") — todo retry auditado da
        // central respondia 500 antes de executar qualquer UPDATE. A correcao redefine a
        // funcao sem o RETURNING, mantendo o GET DIAGNOSTICS ROW_COUNT que decide o
        // resultado e a condicao da auditoria.
        Assert.DoesNotContain("ODCA-MIGRATION 056", snapshot, StringComparison.Ordinal);
        Assert.Contains("GET DIAGNOSTICS changed = ROW_COUNT;", MigrationBlock(canonical, 55), StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseV056AddsContentIndexReservationExpiryAndDraftExtractionReviewsWithoutRewritingEarlierMigrations()
    {
        var canonical = File.ReadAllText(FindSql()).Replace("\r\n", "\n");
        var previous = File.ReadAllText(Path.Combine(Path.GetDirectoryName(FindSql())!, "releases", "odca-v055.sql")).Replace("\r\n", "\n");
        var snapshot = File.ReadAllText(Path.Combine(Path.GetDirectoryName(FindSql())!, "releases", "odca-v056.sql")).Replace("\r\n", "\n");
        DatabaseMigrator.ValidateChecksums(previous);
        DatabaseMigrator.ValidateChecksums(snapshot);
        DatabaseMigrator.ValidateChecksums(canonical);
        for (var version = 1; version <= 55; version++)
            Assert.Equal(MigrationBlock(previous, version), MigrationBlock(canonical, version));
        // D-OC32: indice de consulta por hash de conteudo para o reupload idempotente por
        // conteudo; funcao do worker que expira reservas de armazenamento pendentes apos o
        // prazo e libera a cota reservada correspondente; registro idempotente de aplicacao
        // de extracao/OCR ao rascunho do estudio.
        Assert.Contains("CREATE INDEX IF NOT EXISTS ix_document_versions_content ON odca.document_versions(tenant_id,document_id,sha256);", canonical, StringComparison.Ordinal);
        Assert.Contains("CREATE OR REPLACE FUNCTION odca.expire_storage_reservations(p_limit integer DEFAULT 500)", canonical, StringComparison.Ordinal);
        Assert.Contains("GREATEST(0, u.reserved_bytes - agg.released)", canonical, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS odca.draft_extraction_reviews(", canonical, StringComparison.Ordinal);
        Assert.DoesNotContain("ODCA-MIGRATION 057", snapshot, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseV057AddsExtractionPageTransparencyTemplateProfileAndPlatformQueueSummaryWithoutRewritingEarlierMigrations()
    {
        var canonical = File.ReadAllText(FindSql()).Replace("\r\n", "\n");
        var previous = File.ReadAllText(Path.Combine(Path.GetDirectoryName(FindSql())!, "releases", "odca-v056.sql")).Replace("\r\n", "\n");
        var snapshot = File.ReadAllText(Path.Combine(Path.GetDirectoryName(FindSql())!, "releases", "odca-v057.sql")).Replace("\r\n", "\n");
        DatabaseMigrator.ValidateChecksums(previous);
        DatabaseMigrator.ValidateChecksums(snapshot);
        DatabaseMigrator.ValidateChecksums(canonical);
        for (var version = 1; version <= 56; version++)
            Assert.Equal(MigrationBlock(previous, version), MigrationBlock(canonical, version));
        // D-OC34: transparencia de paginas na extracao (total/processadas/truncado + metodo
        // 'pdf-mixed'), perfil de atividade opcional em templates e resumo somente leitura das
        // filas para o diagnostico operacional da plataforma.
        Assert.Contains("ADD COLUMN pages_total integer CHECK(pages_total IS NULL OR pages_total > 0)", canonical, StringComparison.Ordinal);
        Assert.Contains("ADD COLUMN pages_processed integer CHECK(pages_processed IS NULL OR pages_processed >= 0)", canonical, StringComparison.Ordinal);
        Assert.Contains("ADD COLUMN truncated boolean NOT NULL DEFAULT false", canonical, StringComparison.Ordinal);
        Assert.Contains("extraction_results_method_ck CHECK(method IN('pdf-native','pdf-mixed','ocr','docx-structure'))", canonical, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE odca.contract_templates ADD COLUMN required_activity_profile text", canonical, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE odca.contract_templates ADD COLUMN archive_reason text", canonical, StringComparison.Ordinal);
        Assert.Contains("CREATE OR REPLACE FUNCTION odca.platform_queue_summary()", canonical, StringComparison.Ordinal);
        Assert.Contains("GRANT EXECUTE ON FUNCTION odca.platform_queue_summary() TO odca_app", canonical, StringComparison.Ordinal);
        Assert.DoesNotContain("ODCA-MIGRATION 058", snapshot, StringComparison.Ordinal);
    }
}
