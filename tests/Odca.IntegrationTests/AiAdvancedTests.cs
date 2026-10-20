using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Npgsql;
using Odca.AI;
using Odca.Api.Controllers;
using Odca.Application.Contracts;
using Odca.Contracts.Ai;
using Odca.Contracts.Studio;
using Odca.Infrastructure.Signatures;

namespace Odca.IntegrationTests;

/// <summary>
/// D-OC53 (Sprint 19/F-A..F-D): núcleos determinísticos avançados de IA — score de risco,
/// delta contra as cláusulas padrão, sugestão de renovação e parser de busca natural.
/// Testes puros: mesma entrada produz sempre o mesmo resultado; nenhum veredito é inventado.
/// </summary>
public sealed class AiAdvancedDeterministicTests
{
    [Fact]
    public void RiskScorerIsZeroForUnavailableAnalysis()
    {
        var score = RiskScorer.Score(AiAnalysis.Unavailable("none"));

        Assert.Equal(0, score.Value);
        Assert.Empty(score.Factors);
    }

    [Fact]
    public void RiskScorerAppliesBaseByOverallRisk()
    {
        var low = RiskScorer.Score(new AiAnalysis(true, null, "s", "low", [], [], "fake"));
        var medium = RiskScorer.Score(new AiAnalysis(true, null, "s", "medium", [], [], "fake"));
        var high = RiskScorer.Score(new AiAnalysis(true, null, "s", "high", [], [], "fake"));

        Assert.Equal(20, low.Value);
        Assert.Equal(45, medium.Value);
        Assert.Equal(70, high.Value);
        Assert.Equal("overall_low", Assert.Single(low.Factors).Code);
        Assert.Equal("overall_high", Assert.Single(high.Factors).Code);
    }

    [Fact]
    public void RiskScorerAddsFindingWeightsAndAtypicalClauseBonus()
    {
        // medium(45) + penalty high(+12) + price_change low(+2) + auditoria_surpresa low(+2) + bonus atipica(+3) = 64
        var analysis = new AiAnalysis(true, null, "s", "medium",
            [
                new AiClauseFinding("penalty", "high", "multa de 2% ao mes"),
                new AiClauseFinding("price_change", "low", "reajuste anual"),
                new AiClauseFinding("auditoria_surpresa", "low", "clausula fora do padrao")
            ], ["servicos"], "fake");

        var score = RiskScorer.Score(analysis);

        Assert.Equal(64, score.Value);
        Assert.Contains(score.Factors, f => f.Code == "penalty" && f.Weight == 12);
        Assert.Contains(score.Factors, f => f.Code == "price_change" && f.Weight == 2);
        var atypicals = score.Factors.Where(f => f.Code == RiskScorer.AtypicalClauseCode).ToArray();
        Assert.Single(atypicals);
        Assert.Equal(3, atypicals[0].Weight);
    }

    [Fact]
    public void RiskScorerClampsToOneHundred()
    {
        var findings = Enumerable.Range(0, 8)
            .Select(i => new AiClauseFinding(AiResponseParser.ClauseCodes[i % AiResponseParser.ClauseCodes.Length], "high", "e"))
            .ToList();
        var analysis = new AiAnalysis(true, null, "s", "high", findings, [], "fake");

        Assert.Equal(100, RiskScorer.Score(analysis).Value);
    }

    [Fact]
    public void TemplateComparisonSplitsDetectedMissingAndUnexpected()
    {
        var analysis = new AiAnalysis(true, null, "s", "medium",
            [
                new AiClauseFinding("penalty", "high", "e"),
                new AiClauseFinding("price_change", "low", "e"),
                new AiClauseFinding("penalty", "low", "duplicata — contada uma vez"),
                new AiClauseFinding("auditoria_surpresa", "low", "e")
            ], [], "fake");

        var comparison = TemplateComparisonBuilder.Build(analysis);

        Assert.Equal(AiResponseParser.ClauseCodes, comparison.Expected.ToArray());
        Assert.Equal(["penalty", "price_change"], comparison.Detected.ToArray());
        Assert.Equal(["auditoria_surpresa"], comparison.Unexpected.ToArray());
        var expectedMissing = AiResponseParser.ClauseCodes.Where(c => c is not ("penalty" or "price_change")).ToArray();
        Assert.Equal(expectedMissing, comparison.Missing.ToArray());
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("low", "renew")]
    [InlineData("medium", "review")]
    [InlineData("high", "renegotiate")]
    public void RenewalSuggestionMapsOverallRisk(string? risk, string? expectedAction)
    {
        var analysis = risk is null ? null : new AiAnalysis(true, null, "s", risk, [], [], "fake");

        var suggestion = RenewalSuggestionBuilder.FromAnalysis(analysis);

        if (expectedAction is null)
        {
            Assert.Null(suggestion);
            return;
        }
        Assert.NotNull(suggestion);
        Assert.Equal(expectedAction, suggestion!.Action);
        Assert.Equal(risk!.ToLowerInvariant(), suggestion.BasisRisk);
    }

    [Fact]
    public void RenewalSuggestionEscalatesToRenegotiateOnPenaltyOrPriceChangeMediumHigh()
    {
        var penaltyMedium = new AiAnalysis(true, null, "s", "low",
            [new AiClauseFinding("penalty", "medium", "e")], [], "fake");
        var priceHigh = new AiAnalysis(true, null, "s", "low",
            [new AiClauseFinding("price_change", "high", "e")], [], "fake");
        var priceLow = new AiAnalysis(true, null, "s", "low",
            [new AiClauseFinding("price_change", "low", "e")], [], "fake");
        var otherMedium = new AiAnalysis(true, null, "s", "low",
            [new AiClauseFinding("exclusivity", "medium", "e")], [], "fake");

        Assert.Equal(RenewalSuggestionBuilder.Renegotiate, RenewalSuggestionBuilder.FromAnalysis(penaltyMedium)?.Action);
        Assert.Equal(RenewalSuggestionBuilder.Renegotiate, RenewalSuggestionBuilder.FromAnalysis(priceHigh)?.Action);
        Assert.Equal(RenewalSuggestionBuilder.Renew, RenewalSuggestionBuilder.FromAnalysis(priceLow)?.Action);
        Assert.Equal(RenewalSuggestionBuilder.Renew, RenewalSuggestionBuilder.FromAnalysis(otherMedium)?.Action);
    }

    [Fact]
    public void RenewalSuggestionNeverInventsVerdictWhenUnavailable()
    {
        Assert.Null(RenewalSuggestionBuilder.FromAnalysis(AiAnalysis.Unavailable("none")));
        Assert.Null(RenewalSuggestionBuilder.FromAnalysis(new AiAnalysis(true, null, "s", "desconhecido", [], [], "fake")));
    }

    [Fact]
    public void SearchParserExtractsPenaltyPercentAndTokens()
    {
        var hints = SearchQueryParser.FromText("contratos com multa superior a 10%");

        Assert.Equal(10.0, hints.PenaltyMinPercent);
        Assert.Contains("multa", hints.Terms);
        Assert.Contains("superior", hints.Terms);
        Assert.DoesNotContain("10", hints.Terms);
        Assert.DoesNotContain("a", hints.Terms);
    }

    [Fact]
    public void SearchParserReadsDecimalPercentAndFallsBackWithoutPenalty()
    {
        Assert.Equal(7.5, SearchQueryParser.FromText("penalidade de 7,5% ou mais").PenaltyMinPercent);
        Assert.Null(SearchQueryParser.FromText("contratos recentes").PenaltyMinPercent);
        Assert.Null(SearchQueryParser.FromText("").PenaltyMinPercent);
        Assert.Empty(SearchQueryParser.FromText("").Terms);
    }

    [Fact]
    public void SearchParserHonorsMaximumTerms()
    {
        var query = string.Join(' ', Enumerable.Range(0, 30).Select(i => "termo" + i));

        Assert.True(SearchQueryParser.FromText(query).Terms.Count <= SearchQueryParser.MaxTerms);
    }

    [Theory]
    [InlineData("""{"terms":["multa","renovacao"],"penaltyMinPercent":12}""", true)]
    [InlineData("""{"terms":[],"penaltyMinPercent":150}""", false)]
    [InlineData("""{"terms":["x"]}""", true)]
    [InlineData("{isso nao eh json", false)]
    [InlineData("[]", false)]
    [InlineData("```json\n{\"terms\":[\"a\"]}\n```", true)]
    public void ProviderJsonIsParsedDefensively(string raw, bool expectedUsable)
    {
        var hints = SearchQueryParser.ParseProviderJson(raw);

        if (!expectedUsable)
        {
            Assert.Null(hints);
            return;
        }
        Assert.NotNull(hints);
        Assert.All(hints!.Terms, term => Assert.False(term.Length > 60));
    }
}

/// <summary>
/// D-OC53 (Sprint 19/F-0..F-E): persistência da análise por versão exata — POST persiste e
/// grava o evento de auditoria; GET lê a linha persistida sem chamar o provedor de novo;
/// idempotência por hash de conteúdo; módulo do plano verificado antes do provedor; GET sem
/// análise persistida nunca grava. Provedor de IA falso (sem rede): conta chamadas.
/// </summary>
public sealed class AiAnalysisPersistenceTests(DatabaseFixture database) : IClassFixture<DatabaseFixture>
{
    private static readonly Guid TenantId = Guid.Parse("77100000-0000-0000-0000-000000000010");
    private static readonly Guid ActorId = Guid.Parse("77100000-0000-0000-0000-000000000001");
    private static readonly Guid RoleId = Guid.Parse("77100000-0000-0000-0000-00000000000b");
    private static readonly Guid ContractId = Guid.Parse("77100000-0000-0000-0000-000000000021");
    private static readonly Guid DocumentId = Guid.Parse("77100000-0000-0000-0000-000000000022");
    private static readonly Guid VersionId = Guid.Parse("77100000-0000-0000-0000-000000000023");
    private static readonly Guid JobId = Guid.Parse("77100000-0000-0000-0000-000000000024");
    private static readonly Guid ResultId = Guid.Parse("77100000-0000-0000-0000-000000000025");
    private const string Text = "Contrato de prestacao de servicos. Clausula 9: multa de 2% ao mes em caso de inadimplencia.";
    private const string Sha = "b2c3d4a1b2c3d4a1b2c3d4a1b2c3d4a1b2c3d4a1b2c3d4a1b2c3d4a1b2c3d4";

    [Fact]
    public async Task PostPersistsAnalysisWritesEventAndLaterReadsSkipTheProvider()
    {
        await using var admin = new NpgsqlConnection(database.AdminConnectionString);
        await admin.OpenAsync();
        await ResetTenantDataAsync(admin);
        await SeedTenantAndRoleAsync(admin);
        await SeedChainAsync(admin);

        var ai = new FakeAiService();
        await using var dataSource = NpgsqlDataSource.Create(database.AppConnectionString);
        var controller = CreateController(dataSource, ai, ActorId);

        var created = Assert.IsType<AiAnalysisResponse>(Assert.IsType<OkObjectResult>(
            await controller.CreateAnalyze(TenantId, ContractId, DocumentId, VersionId, CancellationToken.None)).Value);
        Assert.True(created.Available);
        Assert.Equal(Sha256Hex(Text), created.ContentHash);
        Assert.Equal(Text.Length, created.AnalyzedChars);
        Assert.NotNull(created.Score);
        // medium(45) + penalty high(+12) + price_change low(+2) = 59, todos codigos padrao
        Assert.Equal(59, created.Score!.Value);
        Assert.NotNull(created.TemplateComparison);
        Assert.Contains("penalty", created.TemplateComparison!.Detected);
        Assert.Contains("termination_notice", created.TemplateComparison.Missing);
        Assert.Empty(created.TemplateComparison.Unexpected);
        Assert.Equal(1, ai.AnalyzeCalls);

        // Linha persistida + evento de auditoria no contrato.
        var rows = await admin.ExecuteScalarAsync<int>("SELECT count(*)::int FROM odca.document_ai_analyses WHERE tenant_id=@t AND document_version_id=@v AND content_hash=@h",
            new { t = TenantId, v = VersionId, h = Sha256Hex(Text) });
        Assert.Equal(1, rows);
        var events = await admin.ExecuteScalarAsync<int>("SELECT count(*)::int FROM odca.contract_events WHERE tenant_id=@t AND contract_id=@c AND event_type='ai_analysis_completed'",
            new { t = TenantId, c = ContractId });
        Assert.Equal(1, events);

        // GET e novo POST batem no cache por conteúdo: o provedor não é chamado de novo.
        var read = Assert.IsType<AiAnalysisResponse>(Assert.IsType<OkObjectResult>(
            await controller.Analyze(TenantId, ContractId, DocumentId, VersionId, CancellationToken.None)).Value);
        Assert.True(read.Available);
        Assert.Equal(59, read.Score?.Value);
        Assert.Equal(Sha256Hex(Text), read.ContentHash);
        Assert.Equal(1, ai.AnalyzeCalls);

        await controller.CreateAnalyze(TenantId, ContractId, DocumentId, VersionId, CancellationToken.None);
        Assert.Equal(1, ai.AnalyzeCalls);
        var totalRows = await admin.ExecuteScalarAsync<int>("SELECT count(*)::int FROM odca.document_ai_analyses WHERE tenant_id=@t", new { t = TenantId });
        Assert.Equal(1, totalRows);
    }

    [Fact]
    public async Task GetWithoutPersistedAnalysisRunsLiveOnceAndWritesNothing()
    {
        await using var admin = new NpgsqlConnection(database.AdminConnectionString);
        await admin.OpenAsync();
        await ResetTenantDataAsync(admin);
        await SeedTenantAndRoleAsync(admin);
        await SeedChainAsync(admin);

        await using var dataSource = NpgsqlDataSource.Create(database.AppConnectionString);
        var controller = CreateController(dataSource, new NullAiService(), ActorId);

        var result = Assert.IsType<AiAnalysisResponse>(Assert.IsType<OkObjectResult>(
            await controller.Analyze(TenantId, ContractId, DocumentId, VersionId, CancellationToken.None)).Value);

        Assert.False(result.Available);
        Assert.Equal("provider_unavailable", result.Reason);
        var rows = await admin.ExecuteScalarAsync<int>("SELECT count(*)::int FROM odca.document_ai_analyses WHERE tenant_id=@t", new { t = TenantId });
        Assert.Equal(0, rows);
        var events = await admin.ExecuteScalarAsync<int>("SELECT count(*)::int FROM odca.contract_events WHERE tenant_id=@t", new { t = TenantId });
        Assert.Equal(0, events);
    }

    [Fact]
    public async Task ModuleNotEnabledRespondsWithStateBeforeCallingProvider()
    {
        await using var admin = new NpgsqlConnection(database.AdminConnectionString);
        await admin.OpenAsync();
        await ResetTenantDataAsync(admin);
        await SeedTenantAndRoleAsync(admin);
        await SeedChainAsync(admin);
        await admin.ExecuteAsync("""
            INSERT INTO odca.organization_feature_blocks(tenant_id, feature_code, reason, actor_user_id)
            VALUES (@t, 'ia_analysis', 'Bloqueio administrativo para o teste D-OC53', @actor)
            ON CONFLICT DO NOTHING;
            """, new { t = TenantId, actor = ActorId });

        var ai = new FakeAiService();
        await using var dataSource = NpgsqlDataSource.Create(database.AppConnectionString);
        var controller = CreateController(dataSource, ai, ActorId);

        var get = Assert.IsType<AiAnalysisResponse>(Assert.IsType<OkObjectResult>(
            await controller.Analyze(TenantId, ContractId, DocumentId, VersionId, CancellationToken.None)).Value);
        var post = Assert.IsType<AiAnalysisResponse>(Assert.IsType<OkObjectResult>(
            await controller.CreateAnalyze(TenantId, ContractId, DocumentId, VersionId, CancellationToken.None)).Value);

        Assert.False(get.Available);
        Assert.Equal("module_not_enabled", get.Reason);
        Assert.Null(get.Score);
        Assert.False(post.Available);
        Assert.Equal("module_not_enabled", post.Reason);
        Assert.Equal(0, ai.AnalyzeCalls);
        var rows = await admin.ExecuteScalarAsync<int>("SELECT count(*)::int FROM odca.document_ai_analyses WHERE tenant_id=@t", new { t = TenantId });
        Assert.Equal(0, rows);
    }

    [Fact]
    public async Task DocumentSearchReachesExtractedTextAndFiltersByDeclaredPenalty()
    {
        await using var admin = new NpgsqlConnection(database.AdminConnectionString);
        await admin.OpenAsync();
        await ResetTenantDataAsync(admin);
        await SeedTenantAndRoleAsync(admin, allPermissions: true);
        await admin.ExecuteAsync($"""
            INSERT INTO odca.contracts(id, tenant_id, title, reference)
            VALUES ('{ContractHighId:N}', @t, 'Contrato Multa Alta', 'REF-H19'),
                   ('{ContractLowId:N}', @t, 'Contrato Multa Baixa', 'REF-L19');
            INSERT INTO odca.contract_documents(id, tenant_id, contract_id, title, created_by)
            VALUES ('{DocumentHighId:N}', @t, '{ContractHighId:N}', 'alto.pdf', @actor),
                   ('{DocumentLowId:N}', @t, '{ContractLowId:N}', 'baixo.pdf', @actor);
            INSERT INTO odca.document_versions(id, tenant_id, contract_id, document_id, version_number, uploaded_by, display_name, detected_type, byte_size, sha256, storage_key, security_status, security_checked_at)
            VALUES ('{VersionHighId:N}', @t, '{ContractHighId:N}', '{DocumentHighId:N}', 1, @actor, 'alto.pdf', 'pdf', 1024, @sha, 'd53/alto.pdf', 'safe', now()),
                   ('{VersionLowId:N}', @t, '{ContractLowId:N}', '{DocumentLowId:N}', 1, @actor, 'baixo.pdf', 'pdf', 1024, @sha, 'd53/baixo.pdf', 'safe', now());
            INSERT INTO odca.extraction_jobs(id, tenant_id, contract_id, version_id, requested_by, status, completed_at)
            VALUES ('{JobHighId:N}', @t, '{ContractHighId:N}', '{VersionHighId:N}', @actor, 'ready_for_review', now()),
                   ('{JobLowId:N}', @t, '{ContractLowId:N}', '{VersionLowId:N}', @actor, 'ready_for_review', now());
            INSERT INTO odca.extraction_results(id, tenant_id, job_id, version_id, method, raw_text, created_at)
            VALUES ('{ResultHighId:N}', @t, '{JobHighId:N}', '{VersionHighId:N}', 'docx-structure', 'Clausula 9: multa de 12% ao mes em caso de atraso.', now()),
                   ('{ResultLowId:N}', @t, '{JobLowId:N}', '{VersionLowId:N}', 'docx-structure', 'Clausula 9: multa de 5% ao mes em caso de atraso.', now());
            """, new { t = TenantId, actor = ActorId, sha = Sha });

        await using var dataSource = NpgsqlDataSource.Create(database.AppConnectionString);
        var controller = CreateStudioController(dataSource, new NullAiService(), ActorId);

        // Busca por frase em linguagem natural (provedor ausente ⇒ modo palavras-chave):
        // a camada de texto pega os dois contratos pela palavra "multa"; o limite declarado
        // (>10%) filtra numericamente e só o contrato de 12% sobrevive.
        var page = Assert.IsType<StudioDocumentPage>(Assert.IsType<OkObjectResult>(await controller.ListDocuments(
            TenantId, search: "contratos com multa superior a 10%", type: null, stage: null, patientId: null, page: 1, pageSize: 20, ct: CancellationToken.None)).Value);
        Assert.Equal(1, page.Total);
        Assert.Equal(ContractHighId, Assert.Single(page.Items).ContractId);
        Assert.NotNull(page.Semantic);
        Assert.Equal("keywords", page.Semantic.Engine);
        Assert.Contains("multa", page.Semantic.Terms);

        // Controle negativo: sem limite declarado os dois contratos casam.
        var both = Assert.IsType<StudioDocumentPage>(Assert.IsType<OkObjectResult>(await controller.ListDocuments(
            TenantId, search: "multa", type: null, stage: null, patientId: null, page: 1, pageSize: 20, ct: CancellationToken.None)).Value);
        Assert.Equal(2, both.Total);
    }

    private static ContractAiController CreateController(NpgsqlDataSource dataSource, IAiService ai, Guid actorId)
    {
        var controller = new ContractAiController(dataSource, ai, Options.Create(new AiOptions()));
        controller.ControllerContext = new ControllerContext { HttpContext = TestControllerContexts.Create(actorId) };
        return controller;
    }

    private static ContractStudioController CreateStudioController(NpgsqlDataSource dataSource, IAiService ai, Guid actorId)
    {
        var config = new ConfigurationBuilder().Build();
        var catalog = new StubTemplateCatalogRepository();
        var controller = new ContractStudioController(dataSource, config, catalog, new DisabledSignatureEnvelopeProvider(), ai, Options.Create(new AiOptions()));
        controller.ControllerContext = new ControllerContext { HttpContext = TestControllerContexts.Create(actorId) };
        return controller;
    }

    private sealed class StubTemplateCatalogRepository : ITemplateCatalogRepository
    {
        public Task<TemplateCatalogPage> ListAsync(Guid tenantId, string? search, string? contractType, string? scope, string? documentPurpose, int page, int pageSize, CancellationToken cancellationToken) =>
            Task.FromResult(new TemplateCatalogPage([], page, pageSize, 0));
    }

    private sealed class FakeAiService : IAiService
    {
        public int AnalyzeCalls { get; private set; }

        public Task<AiAnalysis> AnalyzeContractAsync(AiContractInput input, CancellationToken ct)
        {
            AnalyzeCalls++;
            var analysis = new AiAnalysis(true, null, "Resumo deterministico do contrato de teste.", "medium",
                [
                    new AiClauseFinding("penalty", "high", "multa de 2% ao mes"),
                    new AiClauseFinding("price_change", "low", "reajuste anual vinculado")
                ], ["servicos"], "fake");
            return Task.FromResult(analysis);
        }

        public Task<AiInterpretation> InterpretSearchQueryAsync(string query, CancellationToken ct) =>
            Task.FromResult(new AiInterpretation(SearchQueryParser.FromText(query), false));
    }

    private static string Sha256Hex(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static readonly Guid ContractHighId = Guid.Parse("77100000-0000-0000-0000-000000000031");
    private static readonly Guid ContractLowId = Guid.Parse("77100000-0000-0000-0000-000000000032");
    private static readonly Guid DocumentHighId = Guid.Parse("77100000-0000-0000-0000-000000000033");
    private static readonly Guid DocumentLowId = Guid.Parse("77100000-0000-0000-0000-000000000034");
    private static readonly Guid VersionHighId = Guid.Parse("77100000-0000-0000-0000-000000000035");
    private static readonly Guid VersionLowId = Guid.Parse("77100000-0000-0000-0000-000000000036");
    private static readonly Guid JobHighId = Guid.Parse("77100000-0000-0000-0000-000000000037");
    private static readonly Guid JobLowId = Guid.Parse("77100000-0000-0000-0000-000000000038");
    private static readonly Guid ResultHighId = Guid.Parse("77100000-0000-0000-0000-000000000039");
    private static readonly Guid ResultLowId = Guid.Parse("77100000-0000-0000-0000-00000000003a");

    /// <summary>Deixa o tenant do teste limpo para execuções repetidas no mesmo banco (sem -FreshDb).</summary>
    private static async Task ResetTenantDataAsync(NpgsqlConnection admin)
    {
        await admin.ExecuteAsync("""
            DELETE FROM odca.contract_events WHERE tenant_id=@TenantId;
            DELETE FROM odca.document_ai_analyses WHERE tenant_id=@TenantId;
            DELETE FROM odca.organization_feature_blocks WHERE tenant_id=@TenantId;
            DELETE FROM odca.extraction_suggestions WHERE result_id IN (SELECT id FROM odca.extraction_results WHERE tenant_id=@TenantId);
            DELETE FROM odca.extraction_results WHERE tenant_id=@TenantId;
            DELETE FROM odca.extraction_jobs WHERE tenant_id=@TenantId;
            DELETE FROM odca.document_versions WHERE tenant_id=@TenantId;
            DELETE FROM odca.contract_documents WHERE tenant_id=@TenantId;
            DELETE FROM odca.contracts WHERE tenant_id=@TenantId;
            DELETE FROM odca.subscriptions WHERE tenant_id=@TenantId;
            DELETE FROM odca.member_roles WHERE tenant_id=@TenantId;
            """, new { TenantId });
    }

    private static async Task SeedTenantAndRoleAsync(NpgsqlConnection admin, bool allPermissions = false)
    {
        await admin.ExecuteAsync("""
            INSERT INTO odca.tenants(id, business_code, display_name, status, timezone)
            VALUES (@TenantId, '77100000000010', 'Tenant D-OC53', 'active', 'America/Sao_Paulo')
            ON CONFLICT (id) DO NOTHING;
            INSERT INTO odca.users(id, email, email_normalized, login_normalized, display_name, password_hash, is_platform_administrator)
            VALUES (@ActorId, 'actor.d53@odca.local', 'ACTOR.D53@ODCA.LOCAL', 'ACTOR.D53@ODCA.LOCAL', 'Ator D-OC53', 'hash', false)
            ON CONFLICT (id) DO NOTHING;
            INSERT INTO odca.memberships(tenant_id, user_id, status)
            VALUES (@TenantId, @ActorId, 'active')
            ON CONFLICT (tenant_id, user_id) DO UPDATE SET status = 'active';
            INSERT INTO odca.roles(id, scope_type, tenant_id, code, display_name, is_system)
            VALUES (@RoleId, 'tenant', @TenantId, 'd53-analise-ia', 'D-OC53 Analise IA', true)
            ON CONFLICT (id) DO NOTHING;
            """, new { TenantId, ActorId, RoleId });

        await admin.ExecuteAsync(allPermissions
            ? """INSERT INTO odca.role_permissions(role_id, permission_code) SELECT @RoleId, code FROM odca.permissions WHERE code LIKE 'tenant.%' ON CONFLICT DO NOTHING;"""
            : """INSERT INTO odca.role_permissions(role_id, permission_code) SELECT @RoleId, code FROM odca.permissions WHERE code IN ('tenant.contracts.read','tenant.contract_drafts.read','tenant.patients.documents.read','tenant.documents.download') ON CONFLICT DO NOTHING;""",
            new { RoleId });

        await admin.ExecuteAsync("""
            INSERT INTO odca.member_roles(tenant_id, user_id, role_id, assigned_by)
            VALUES (@TenantId, @ActorId, @RoleId, @ActorId)
            ON CONFLICT DO NOTHING;
            """, new { TenantId, ActorId, RoleId });
    }

    private static async Task SeedChainAsync(NpgsqlConnection admin)
    {
        await admin.ExecuteAsync("""
            INSERT INTO odca.contracts(id, tenant_id, title, reference)
            VALUES (@ContractId, @TenantId, 'Contrato D-OC53', 'D53');
            INSERT INTO odca.contract_documents(id, tenant_id, contract_id, title, created_by)
            VALUES (@DocumentId, @TenantId, @ContractId, 'contrato.pdf', @ActorId);
            INSERT INTO odca.document_versions(id, tenant_id, contract_id, document_id, version_number, uploaded_by, display_name, detected_type, byte_size, sha256, storage_key, security_status, security_checked_at)
            VALUES (@VersionId, @TenantId, @ContractId, @DocumentId, 1, @ActorId, 'contrato.pdf', 'pdf', 2048, @sha, 'd53/contrato.pdf', 'safe', now());
            INSERT INTO odca.extraction_jobs(id, tenant_id, contract_id, version_id, requested_by, status, completed_at)
            VALUES (@JobId, @TenantId, @ContractId, @VersionId, @ActorId, 'ready_for_review', now());
            INSERT INTO odca.extraction_results(id, tenant_id, job_id, version_id, method, raw_text, created_at)
            VALUES (@ResultId, @TenantId, @JobId, @VersionId, 'docx-structure', @text, now());
            """, new { ContractId, DocumentId, VersionId, JobId, ResultId, TenantId, ActorId, sha = Sha, text = Text });
    }
}
