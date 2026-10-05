using Dapper;
using Npgsql;
using Odca.Worker;

namespace Odca.IntegrationTests;

public sealed class FranchiseMeteringTests(DatabaseFixture database) : IClassFixture<DatabaseFixture>
{
    [Fact]
    public void OcrPageCountUsesFormFeedsFromTheExtractedText()
    {
        Assert.Equal(1, DocumentWorker.CountOcrPages(""));
        Assert.Equal(1, DocumentWorker.CountOcrPages("uma página"));
        Assert.Equal(2, DocumentWorker.CountOcrPages("primeira\fsegunda"));
        Assert.Equal(2, DocumentWorker.CountOcrPages("primeira\fsegunda\f"));
    }

    [Fact]
    public async Task MonthlyFranchiseIsIdempotentAndBlocksWhenTheContractedPagesAreUsed()
    {
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var planId = Guid.Parse("30000000-0000-0000-0000-000000000011");
        await using var admin = new NpgsqlConnection(database.AdminConnectionString);
        await admin.OpenAsync();
        await using var tx = await admin.BeginTransactionAsync();
        await admin.ExecuteAsync("""
            INSERT INTO odca.tenants(id, business_code, display_name, status, timezone)
            VALUES (@tenantId, @code, 'Organização da franquia', 'active', 'America/Sao_Paulo');
            INSERT INTO odca.users(id, email, email_normalized, login_normalized, display_name, password_hash, is_platform_administrator)
            VALUES (@actorId, @email, @emailUpper, @emailUpper, 'Ator da franquia', 'hash', false);
            INSERT INTO odca.subscriptions(id, tenant_id, plan_version_id, commercial_state, status, created_by)
            VALUES (@subscriptionId, @tenantId, @planId, 'active', 'active', @actorId);
            """, new
        {
            tenantId,
            actorId,
            planId,
            subscriptionId = Guid.NewGuid(),
            code = tenantId.ToString("N")[..20],
            email = $"{tenantId:N}@franchise.test",
            emailUpper = $"{tenantId:N}@franchise.test".ToUpperInvariant()
        }, tx);

        var first = await admin.ExecuteScalarAsync<string>("""
            SELECT odca.consume_monthly_franchise(@tenantId, 'ocr_credit', 300, @key, 'extraction_job', @source, 'document-worker')
            """, new { tenantId, key = "ocr-job:first", source = Guid.NewGuid() }, tx);
        var replay = await admin.ExecuteScalarAsync<string>("""
            SELECT odca.consume_monthly_franchise(@tenantId, 'ocr_credit', 300, @key, 'extraction_job', @source, 'document-worker')
            """, new { tenantId, key = "ocr-job:first", source = Guid.NewGuid() }, tx);
        var overflow = await admin.ExecuteScalarAsync<string>("""
            SELECT odca.consume_monthly_franchise(@tenantId, 'ocr_credit', 1, @key, 'extraction_job', @source, 'document-worker')
            """, new { tenantId, key = "ocr-job:second", source = Guid.NewGuid() }, tx);
        var signature = await admin.ExecuteScalarAsync<string>("""
            SELECT odca.consume_monthly_franchise(@tenantId, 'signature_credit', 1, @key, 'signature_envelope', @source, 'signature-provider')
            """, new { tenantId, key = "envelope:one", source = Guid.NewGuid() }, tx);
        var signatureReplay = await admin.ExecuteScalarAsync<string>("""
            SELECT odca.consume_monthly_franchise(@tenantId, 'signature_credit', 1, @key, 'signature_envelope', @source, 'signature-provider')
            """, new { tenantId, key = "envelope:one", source = Guid.NewGuid() }, tx);

        Assert.Equal("consumed", first);
        Assert.Equal("duplicate", replay);
        Assert.Equal("exhausted", overflow);
        Assert.Equal("consumed", signature);
        Assert.Equal("duplicate", signatureReplay);
        Assert.Equal(300, await admin.ExecuteScalarAsync<long>("""
            SELECT COALESCE(sum(quantity), 0) FROM odca.resource_movements
             WHERE tenant_id=@tenantId AND resource_type='ocr_credit' AND movement_type='consume'
            """, new { tenantId }, tx));
        Assert.Equal(1, await admin.ExecuteScalarAsync<int>("""
            SELECT count(*)::int FROM odca.resource_movements
             WHERE tenant_id=@tenantId AND resource_type='ocr_credit'
            """, new { tenantId }, tx));
        Assert.Equal("quota_exhausted", await admin.ExecuteScalarAsync<string>(
            "SELECT odca.organization_feature_state(@tenantId, 'imports')", new { tenantId }, tx));
        Assert.Equal("allowed", await admin.ExecuteScalarAsync<string>(
            "SELECT odca.organization_feature_state(@tenantId, 'signatures')", new { tenantId }, tx));
        Assert.Equal("allowed", await admin.ExecuteScalarAsync<string>(
            "SELECT odca.organization_feature_state(@tenantId, 'patients')", new { tenantId }, tx));

        var suspendedTenant = Guid.NewGuid();
        var suspendedVersion = Guid.NewGuid();
        var activeTenant = Guid.NewGuid();
        var activeVersion = Guid.NewGuid();
        await admin.ExecuteAsync("""
            INSERT INTO odca.tenants(id, business_code, display_name, status, timezone) VALUES
                (@suspendedTenant, @suspendedCode, 'Organização suspensa da varredura', 'suspended', 'America/Sao_Paulo'),
                (@activeTenant, @activeCode, 'Organização ativa da varredura', 'active', 'America/Sao_Paulo');
            INSERT INTO odca.subscriptions(id, tenant_id, plan_version_id, commercial_state, status, created_by) VALUES
                (@suspendedSubscription, @suspendedTenant, @planId, 'active', 'active', @actorId),
                (@activeSubscription, @activeTenant, @planId, 'active', 'active', @actorId);
            INSERT INTO odca.contracts(id, tenant_id, title) VALUES
                (@suspendedContract, @suspendedTenant, 'Contrato suspenso'),
                (@activeContract, @activeTenant, 'Contrato ativo');
            INSERT INTO odca.contract_documents(id, tenant_id, contract_id, title, created_by) VALUES
                (@suspendedDocument, @suspendedTenant, @suspendedContract, 'Documento suspenso', @actorId),
                (@activeDocument, @activeTenant, @activeContract, 'Documento ativo', @actorId);
            INSERT INTO odca.document_versions(
                id, tenant_id, contract_id, document_id, version_number, uploaded_by, uploaded_at,
                display_name, detected_type, byte_size, sha256, storage_key, security_status)
            VALUES
                (@suspendedVersion, @suspendedTenant, @suspendedContract, @suspendedDocument, 1, @actorId, '-infinity',
                 'suspenso.pdf', 'pdf', 10, repeat('a', 64), @suspendedKey, 'pending'),
                (@activeVersion, @activeTenant, @activeContract, @activeDocument, 1, @actorId, '1970-01-01',
                 'ativo.pdf', 'pdf', 10, repeat('b', 64), @activeKey, 'pending');
            """, new
        {
            actorId,
            planId,
            suspendedTenant,
            activeTenant,
            suspendedVersion,
            activeVersion,
            suspendedSubscription = Guid.NewGuid(),
            activeSubscription = Guid.NewGuid(),
            suspendedContract = Guid.NewGuid(),
            activeContract = Guid.NewGuid(),
            suspendedDocument = Guid.NewGuid(),
            activeDocument = Guid.NewGuid(),
            suspendedCode = suspendedTenant.ToString("N")[..20],
            activeCode = activeTenant.ToString("N")[..20],
            suspendedKey = "scan-" + suspendedVersion.ToString("N"),
            activeKey = "scan-" + activeVersion.ToString("N")
        }, tx);

        Guid? claimedTenant = null;
        for (var attempt = 0; attempt < 30 && claimedTenant != activeTenant; attempt++)
        {
            claimedTenant = await admin.ExecuteScalarAsync<Guid?>(
                "SELECT tenant_id FROM odca.claim_document_scan()", transaction: tx);
            Assert.NotEqual(suspendedTenant, claimedTenant);
        }

        Assert.Equal(activeTenant, claimedTenant);
        Assert.Equal("pending", await admin.ExecuteScalarAsync<string>(
            "SELECT security_status FROM odca.document_versions WHERE id=@suspendedVersion", new { suspendedVersion }, tx));
        await tx.RollbackAsync();
    }
}
