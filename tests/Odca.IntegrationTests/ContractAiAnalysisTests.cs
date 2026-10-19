using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Npgsql;
using Odca.AI;
using Odca.Api.Controllers;
using Odca.Contracts.Ai;

namespace Odca.IntegrationTests;

/// <summary>
/// D-OC52 (Sprint 18): análise IA por versão exata do documento — estados honestos
/// (sem texto extraído / provedor ausente), vínculo análise↔conteúdo (hash + chars),
/// isolamento por organização e permissão no servidor. Semeia diretamente a cadeia
/// contrato → documento → versão → extração com o padrão dos testes D-OC32.
/// </summary>
public sealed class ContractAiAnalysisTests(DatabaseFixture database) : IClassFixture<DatabaseFixture>
{
    private static readonly Guid TenantId = Guid.Parse("77000000-0000-0000-0000-000000000010");
    private static readonly Guid ActorId = Guid.Parse("77000000-0000-0000-0000-000000000001");
    private static readonly Guid OutsiderId = Guid.Parse("77000000-0000-0000-0000-000000000002");
    private static readonly Guid RoleId = Guid.Parse("77000000-0000-0000-0000-00000000000b");
    private static readonly Guid ContractId = Guid.Parse("77000000-0000-0000-0000-000000000021");
    private static readonly Guid DocumentId = Guid.Parse("77000000-0000-0000-0000-000000000022");
    private static readonly Guid VersionId = Guid.Parse("77000000-0000-0000-0000-000000000023");
    private static readonly Guid FirstJobId = Guid.Parse("77000000-0000-0000-0000-000000000024");
    private static readonly Guid FirstResultId = Guid.Parse("77000000-0000-0000-0000-000000000025");
    private static readonly Guid SecondJobId = Guid.Parse("77000000-0000-0000-0000-000000000026");
    private static readonly Guid SecondResultId = Guid.Parse("77000000-0000-0000-0000-000000000027");
    private const string Sha = "a1b2c3d4a1b2c3d4a1b2c3d4a1b2c3d4a1b2c3d4a1b2c3d4a1b2c3d4a1b2c3d4";
    private const string TextV1 = "Contrato de prestacao de servicos. Clausula 9: multa de 2% ao mes em caso de inadimplencia.";
    private const string TextV2 = "Contrato atualizado. Clausula 9 revogada: multa de 1% ao mes. Clausula 12: aviso previo de 60 dias.";

    [Fact]
    public async Task VersionWithoutExtractedTextRespondsNoText()
    {
        await using var admin = new NpgsqlConnection(database.AdminConnectionString);
        await admin.OpenAsync();
        await ResetTenantDataAsync(admin);
        await SeedTenantAndRoleAsync(admin);
        await SeedChainAsync(admin, withResults: false);

        await using var dataSource = NpgsqlDataSource.Create(database.AppConnectionString);
        var controller = CreateController(dataSource, ActorId);

        var result = await controller.Analyze(TenantId, ContractId, DocumentId, VersionId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var value = Assert.IsType<AiAnalysisResponse>(ok.Value);
        Assert.False(value.Available);
        Assert.Equal("no_text", value.Reason);
        Assert.Equal("none", value.ProviderName);
        Assert.Null(value.ContentHash);
        Assert.Equal(0, value.AnalyzedChars);
        Assert.Empty(value.Findings);
    }

    [Fact]
    public async Task ProviderNoneDeclaresUnavailableWithContentHashAndAnalyzedChars()
    {
        await using var admin = new NpgsqlConnection(database.AdminConnectionString);
        await admin.OpenAsync();
        await ResetTenantDataAsync(admin);
        await SeedTenantAndRoleAsync(admin);
        await SeedChainAsync(admin, withResults: true);

        await using var dataSource = NpgsqlDataSource.Create(database.AppConnectionString);
        var controller = CreateController(dataSource, ActorId);

        var result = await controller.Analyze(TenantId, ContractId, DocumentId, VersionId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var value = Assert.IsType<AiAnalysisResponse>(ok.Value);
        Assert.False(value.Available);
        Assert.Equal("provider_unavailable", value.Reason);
        Assert.Equal("none", value.ProviderName);
        Assert.Equal(Sha256Hex(TextV2), value.ContentHash);
        Assert.Equal(TextV2.Length, value.AnalyzedChars);
    }

    [Fact]
    public async Task AnalysisIsBoundToTheLatestExtractionResultOfTheVersion()
    {
        await using var admin = new NpgsqlConnection(database.AdminConnectionString);
        await admin.OpenAsync();
        await ResetTenantDataAsync(admin);
        await SeedTenantAndRoleAsync(admin);
        await SeedChainAsync(admin, withResults: true);

        await using var dataSource = NpgsqlDataSource.Create(database.AppConnectionString);
        var controller = CreateController(dataSource, ActorId);

        var result = await controller.Analyze(TenantId, ContractId, DocumentId, VersionId, CancellationToken.None);

        // A segunda extração (mais recente por created_at) é a fonte da análise — nunca a primeira.
        var value = Assert.IsType<AiAnalysisResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(Sha256Hex(TextV2), value.ContentHash);
        Assert.NotEqual(Sha256Hex(TextV1), value.ContentHash);
        Assert.Equal(TextV2.Length, value.AnalyzedChars);
    }

    [Fact]
    public async Task UnknownVersionReturnsNotFound()
    {
        await using var admin = new NpgsqlConnection(database.AdminConnectionString);
        await admin.OpenAsync();
        await ResetTenantDataAsync(admin);
        await SeedTenantAndRoleAsync(admin);
        await SeedChainAsync(admin, withResults: false);

        await using var dataSource = NpgsqlDataSource.Create(database.AppConnectionString);
        var controller = CreateController(dataSource, ActorId);

        var result = await controller.Analyze(TenantId, ContractId, DocumentId, Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task MemberWithoutPermissionIsForbidden()
    {
        await using var admin = new NpgsqlConnection(database.AdminConnectionString);
        await admin.OpenAsync();
        await ResetTenantDataAsync(admin);
        await SeedTenantAndRoleAsync(admin);
        await SeedChainAsync(admin, withResults: false);

        await using var dataSource = NpgsqlDataSource.Create(database.AppConnectionString);
        var controller = CreateController(dataSource, OutsiderId);

        var result = await controller.Analyze(TenantId, ContractId, DocumentId, VersionId, CancellationToken.None);

        Assert.IsType<ForbidResult>(result);
    }

    private static ContractAiController CreateController(NpgsqlDataSource dataSource, Guid actorId)
    {
        var controller = new ContractAiController(dataSource, new NullAiService(), Options.Create(new AiOptions()));
        controller.ControllerContext = new ControllerContext { HttpContext = TestControllerContexts.Create(actorId) };
        return controller;
    }

    private static string Sha256Hex(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    /// <summary>Deixa o tenant do teste limpo para execuções repetidas no mesmo banco (sem -FreshDb).</summary>
    private static async Task ResetTenantDataAsync(NpgsqlConnection admin)
    {
        await admin.ExecuteAsync("""
            DELETE FROM odca.contract_events WHERE tenant_id=@TenantId;
            DELETE FROM odca.resource_movements WHERE tenant_id=@TenantId;
            DELETE FROM odca.storage_reservations WHERE tenant_id=@TenantId;
            DELETE FROM odca.tenant_storage_usage WHERE tenant_id=@TenantId;
            DELETE FROM odca.draft_extraction_reviews WHERE tenant_id=@TenantId;
            DELETE FROM odca.extraction_suggestions WHERE result_id IN (SELECT id FROM odca.extraction_results WHERE tenant_id=@TenantId);
            DELETE FROM odca.extraction_results WHERE tenant_id=@TenantId;
            DELETE FROM odca.extraction_jobs WHERE tenant_id=@TenantId;
            DELETE FROM odca.generated_contract_versions WHERE tenant_id=@TenantId;
            DELETE FROM odca.document_versions WHERE tenant_id=@TenantId;
            DELETE FROM odca.contract_documents WHERE tenant_id=@TenantId;
            DELETE FROM odca.contract_drafts WHERE tenant_id=@TenantId;
            DELETE FROM odca.contracts WHERE tenant_id=@TenantId;
            DELETE FROM odca.subscriptions WHERE tenant_id=@TenantId;
            DELETE FROM odca.member_roles WHERE tenant_id=@TenantId;
            """, new { TenantId });
    }

    private static async Task SeedTenantAndRoleAsync(NpgsqlConnection admin)
    {
        await admin.ExecuteAsync("""
            INSERT INTO odca.tenants(id, business_code, display_name, status, timezone)
            VALUES (@TenantId, '77000000000011', 'Tenant D-OC52', 'active', 'America/Sao_Paulo')
            ON CONFLICT (id) DO NOTHING;
            INSERT INTO odca.users(id, email, email_normalized, login_normalized, display_name, password_hash, is_platform_administrator)
            VALUES
                (@ActorId, 'actor.d52@odca.local', 'ACTOR.D52@ODCA.LOCAL', 'ACTOR.D52@ODCA.LOCAL', 'Ator D-OC52', 'hash', false),
                (@OutsiderId, 'outsider.d52@odca.local', 'OUTSIDER.D52@ODCA.LOCAL', 'OUTSIDER.D52@ODCA.LOCAL', 'Fora D-OC52', 'hash', false)
            ON CONFLICT (id) DO NOTHING;
            INSERT INTO odca.memberships(tenant_id, user_id, status)
            VALUES (@TenantId, @ActorId, 'active'), (@TenantId, @OutsiderId, 'active')
            ON CONFLICT (tenant_id, user_id) DO UPDATE SET status = 'active';
            INSERT INTO odca.roles(id, scope_type, tenant_id, code, display_name, is_system)
            VALUES (@RoleId, 'tenant', @TenantId, 'd52-analise-ia', 'D-OC52 Analise IA', true)
            ON CONFLICT (id) DO NOTHING;
            INSERT INTO odca.role_permissions(role_id, permission_code)
            SELECT @RoleId, code FROM odca.permissions
             WHERE code IN ('tenant.contracts.read')
            ON CONFLICT DO NOTHING;
            INSERT INTO odca.member_roles(tenant_id, user_id, role_id, assigned_by)
            VALUES (@TenantId, @ActorId, @RoleId, @ActorId)
            ON CONFLICT DO NOTHING;
            """, new { TenantId, ActorId, OutsiderId, RoleId });
    }

    private static async Task SeedChainAsync(NpgsqlConnection admin, bool withResults)
    {
        await admin.ExecuteAsync("""
            INSERT INTO odca.contracts(id, tenant_id, title, reference)
            VALUES (@ContractId, @TenantId, 'Contrato D-OC52', 'D52');
            INSERT INTO odca.contract_documents(id, tenant_id, contract_id, title, created_by)
            VALUES (@DocumentId, @TenantId, @ContractId, 'contrato.pdf', @ActorId);
            INSERT INTO odca.document_versions(id, tenant_id, contract_id, document_id, version_number, uploaded_by, display_name, detected_type, byte_size, sha256, storage_key, security_status, security_checked_at)
            VALUES (@VersionId, @TenantId, @ContractId, @DocumentId, 1, @ActorId, 'contrato.pdf', 'pdf', 2048, @sha, 'd52/' || CAST(@VersionId AS text), 'safe', now());
            """, new { ContractId, DocumentId, VersionId, TenantId, ActorId, sha = Sha });

        if (!withResults) return;

        await admin.ExecuteAsync("""
            INSERT INTO odca.extraction_jobs(id, tenant_id, contract_id, version_id, requested_by, status, completed_at)
            VALUES (@FirstJobId, @TenantId, @ContractId, @VersionId, @ActorId, 'ready_for_review', '2026-01-01T12:00:00Z'::timestamptz);
            INSERT INTO odca.extraction_results(id, tenant_id, job_id, version_id, method, raw_text, created_at)
            VALUES (@FirstResultId, @TenantId, @FirstJobId, @VersionId, 'docx-structure', @TextV1, '2026-01-01T12:00:00Z'::timestamptz);
            INSERT INTO odca.extraction_jobs(id, tenant_id, contract_id, version_id, requested_by, status, completed_at)
            VALUES (@SecondJobId, @TenantId, @ContractId, @VersionId, @ActorId, 'ready_for_review', '2026-01-02T12:00:00Z'::timestamptz);
            INSERT INTO odca.extraction_results(id, tenant_id, job_id, version_id, method, raw_text, created_at)
            VALUES (@SecondResultId, @TenantId, @SecondJobId, @VersionId, 'ocr', @TextV2, '2026-01-02T12:00:00Z'::timestamptz);
            """, new { FirstJobId, SecondJobId, FirstResultId, SecondResultId, TenantId, ContractId, VersionId, ActorId, TextV1, TextV2 });
    }
}
