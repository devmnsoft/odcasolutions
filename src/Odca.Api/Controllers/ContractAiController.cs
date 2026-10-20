using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Npgsql;
using Odca.AI;
using Odca.Api.Localization;
using Odca.Contracts.Ai;

namespace Odca.Api.Controllers;

// D-OC52 (Sprint 18) + D-OC53 (Sprint 19): análise IA de contrato por versão EXATA do documento
// (invariante assinatura↔versão). A fonte é o texto já extraído (camada de extração/OCR) da
// versão indicada; ContentHash liga a análise ao conteúdo analisado e AnalyzedChars declara o
// quanto do texto foi efetivamente enviado ao provedor (truncamento honesto).
//
// S19: as análises passam a ser PERSISTIDAS (odca.document_ai_analyses, append-only por versão):
// GET = leitura (devolve a última análise cujo hash bate com a extração atual — cache sem
// chamar o provedor — senão faz análise única ao vivo, sem gravação); POST = nova análise com
// persistência (idempotente por conteúdo: mesma extração já analisada não chama o provedor de
// novo). O módulo ia_analysis do plano é verificado antes de qualquer chamada de provedor;
// plano sem módulo ⇒ 200 available=false reason="module_not_enabled" (estado, não erro).
// Score 0–100 e delta contra as cláusulas padrão vêm de fórmulas determinísticas (Odca.AI).
[ApiController]
[Route("api/v1/organizations/{tenantId:guid}/contracts/{contractId:guid}/documents/{documentId:guid}/versions/{versionId:guid}/ai-analysis")]
[Authorize(Policy = "PasswordChanged")]
public sealed class ContractAiController(NpgsqlDataSource dataSource, IAiService ai, IOptions<AiOptions> aiOptions) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Analyze(Guid tenantId, Guid contractId, Guid documentId, Guid versionId, CancellationToken ct)
    {
        var (outcome, loaded) = await LoadAsync(tenantId, contractId, documentId, versionId, ct);
        if (outcome != LoadOutcome.Ok) return Map(outcome);
        var (cachedHit, cached) = await TryReadCachedAsync(tenantId, versionId!, loaded!.ContentHash, ct);
        if (cachedHit)
        {
            return Ok(cached!);
        }
        return Ok(await RunLiveAsync(loaded!, ct));
    }

    [HttpPost]
    public async Task<IActionResult> CreateAnalyze(Guid tenantId, Guid contractId, Guid documentId, Guid versionId, CancellationToken ct)
    {
        var (outcome, loaded) = await LoadAsync(tenantId, contractId, documentId, versionId, ct);
        if (outcome != LoadOutcome.Ok) return Map(outcome);
        // Idempotência por conteúdo: mesmo texto já analisado não gasta provedor de novo.
        var (cachedHit, cached) = await TryReadCachedAsync(tenantId, versionId!, loaded!.ContentHash, ct);
        if (cachedHit)
        {
            return Ok(cached!);
        }
        var live = await RunLiveAsync(loaded!, ct);
        if (live.Available)
        {
            await PersistAsync(loaded!, live, ct);
        }
        return Ok(live);
    }

    private enum LoadOutcome { Ok, Unauthorized, Forbid, NotFound, ModuleNotEnabled, NoText }

    private IActionResult Map(LoadOutcome outcome) => outcome switch
    {
        LoadOutcome.Unauthorized => Unauthorized(),
        LoadOutcome.Forbid => Forbid(),
        LoadOutcome.NotFound => NotFound(),
        LoadOutcome.ModuleNotEnabled => Ok(new AiAnalysisResponse(false, "module_not_enabled", null, null, [], [], "none", null, 0)),
        LoadOutcome.NoText => Ok(new AiAnalysisResponse(false, "no_text", null, null, [], [], "none", null, 0)),
        _ => throw new InvalidOperationException($"Resultado inesperado de carga: {outcome}"),
    };

    private async Task<(LoadOutcome, Source?)> LoadAsync(Guid tenantId, Guid contractId, Guid documentId, Guid versionId, CancellationToken ct)
    {
        var actor = Actor();
        if (actor is null) return (LoadOutcome.Unauthorized, null);
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await SetContext(connection, actor.Value, tenantId, transaction, ct);
        if (!await Allowed(connection, transaction, actor.Value, tenantId, "tenant.contracts.read", ct))
        {
            return (LoadOutcome.Forbid, null);
        }

        var row = await connection.QuerySingleOrDefaultAsync<SourceRow>(new CommandDefinition("""
            SELECT v.id AS VersionId, v.uploaded_by AS UploadedBy, COALESCE(r.raw_text, '') AS RawText
              FROM odca.document_versions v
              LEFT JOIN odca.extraction_results r
                ON r.tenant_id = v.tenant_id AND r.version_id = v.id
               AND r.id = (SELECT r2.id
                             FROM odca.extraction_results r2
                            WHERE r2.tenant_id = v.tenant_id AND r2.version_id = v.id
                            ORDER BY r2.created_at DESC, r2.id DESC
                            LIMIT 1)
             WHERE v.tenant_id = @tenantId AND v.contract_id = @contractId
               AND v.document_id = @documentId AND v.id = @versionId
            """, new { tenantId, contractId, documentId, versionId }, transaction, cancellationToken: ct));
        if (row is null)
        {
            await transaction.CommitAsync(ct);
            return (LoadOutcome.NotFound, null);
        }

        // Módulo do plano verificado ANTES de qualquer chamada de provedor (plano ≠ operação:
        // falha-fechada com estado honesto, não com incidente 503).
        var moduleState = await connection.ExecuteScalarAsync<string>(new CommandDefinition(
            "SELECT odca.organization_feature_state(@tenantId, 'ia_analysis')", new { tenantId }, transaction, cancellationToken: ct));
        await transaction.CommitAsync(ct);
        if (moduleState is not null and not "allowed")
        {
            return (LoadOutcome.ModuleNotEnabled, null);
        }
        if (string.IsNullOrWhiteSpace(row.RawText))
        {
            return (LoadOutcome.NoText, null);
        }

        var text = row.RawText.Trim();
        var maximumChars = Math.Max(1_000, aiOptions.Value.MaxInputChars);
        var contentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
        return (LoadOutcome.Ok, new Source(tenantId, contractId, documentId, versionId, row.UploadedBy, text, Math.Min(text.Length, maximumChars), contentHash));
    }

    private async Task<(bool, AiAnalysisResponse?)> TryReadCachedAsync(Guid tenantId, Guid versionId, string contentHash, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await SetTenant(connection, tenantId, ct);
        var row = await connection.QuerySingleOrDefaultAsync<CachedRow>(new CommandDefinition("""
            SELECT analysis::text AS Payload, provider AS Provider, score_value AS ScoreValue, analyzed_chars AS AnalyzedChars
              FROM odca.document_ai_analyses
             WHERE tenant_id = @tenantId AND document_version_id = @versionId
               AND content_hash = @contentHash
             ORDER BY created_at DESC, id DESC
             LIMIT 1
            """, new { tenantId, versionId, contentHash }, cancellationToken: ct));
        if (row is null || !TryBuildFromPayload(row, contentHash, out var response))
        {
            return (false, null);
        }
        return (true, response);
    }

    private static bool TryBuildFromPayload(CachedRow row, string contentHash, out AiAnalysisResponse response)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<AiAnalysisPayload>(row.Payload, JsonOptions);
            if (payload is null)
            {
                response = default!;
                return false;
            }
            response = new AiAnalysisResponse(true, null, payload.Summary, payload.OverallRisk,
                payload.Findings ?? [], payload.Categories ?? [], payload.ProviderName ?? "http", contentHash, row.AnalyzedChars,
                payload.Score, payload.TemplateComparison);
            return true;
        }
        catch (JsonException)
        {
            response = default!;
            return false;
        }
    }

    private Task<AiAnalysisResponse> RunLiveAsync(Source source, CancellationToken ct) =>
        AnalyzeAndShape(ai.AnalyzeContractAsync(new AiContractInput(source.Text, source.Text.Length, ApiCulture.For(HttpContext)), ct), source);

    internal static async Task<AiAnalysisResponse> AnalyzeAndShape(Task<AiAnalysis> pending, Source source)
    {
        var result = await pending;
        var score = RiskScorer.Score(result);
        var comparison = TemplateComparisonBuilder.Build(result);
        var findings = result.Findings.Select(f => new AiClauseFindingDto(f.Code, f.Risk, f.Evidence)).ToArray();
        return new AiAnalysisResponse(result.Available, result.Available ? null : result.Reason, result.Summary, result.OverallRisk,
            findings, [.. result.Categories], result.ProviderName, source.ContentHash, source.AnalyzedChars,
            result.Available ? ToScoreDto(score) : null,
            result.Available ? new AiTemplateComparisonDto(
                [.. comparison.Expected], [.. comparison.Detected], [.. comparison.Missing], [.. comparison.Unexpected]) : null);
    }

    private async Task PersistAsync(Source source, AiAnalysisResponse live, CancellationToken ct)
    {
        var payload = new AiAnalysisPayload(live.Summary, live.OverallRisk, live.Findings, live.Categories, live.Score, live.TemplateComparison, live.ProviderName,
            string.IsNullOrWhiteSpace(aiOptions.Value.Model) ? null : aiOptions.Value.Model?.Trim());
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await SetTenantTransaction(connection, source.TenantId, transaction, ct);
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO odca.document_ai_analyses(tenant_id, contract_id, document_id, document_version_id, content_hash, analyzed_chars, provider, model, score_value, analysis, created_by)
            VALUES(@tenantId, @contractId, @documentId, @versionId, @contentHash, @analyzedChars, @provider, @model, @scoreValue, @analysis::jsonb, @createdBy)
            """, new
            {
                source.TenantId,
                source.ContractId,
                source.DocumentId,
                versionId = source.VersionId,
                source.ContentHash,
                source.AnalyzedChars,
                provider = string.IsNullOrWhiteSpace(aiOptions.Value.Provider) ? "none" : aiOptions.Value.Provider.Trim().ToLowerInvariant(),
                payload.Model,
                scoreValue = live.Score?.Value ?? 0,
                analysis = JsonSerializer.Serialize(payload, JsonOptions),
                createdBy = source.UploadedBy,
            }, transaction, cancellationToken: ct));
        if (source.UploadedBy is not null)
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO odca.contract_events(tenant_id, contract_id, actor_id, event_type, details)
                VALUES(@tenantId, @contractId, @actorId, 'ai_analysis_completed', @details::jsonb)
                """, new
                {
                    source.TenantId,
                    source.ContractId,
                    actorId = source.UploadedBy.Value,
                    details = JsonSerializer.Serialize(new { provider = live.ProviderName, score = live.Score?.Value }, JsonOptions),
                }, transaction, cancellationToken: ct));
        }
        await transaction.CommitAsync(ct);
    }

    internal static AiRiskScoreDto? ToScoreDto(AiRiskScore score) =>
        score.Factors.Count == 0 && score.Value == 0 ? null : new AiRiskScoreDto(score.Value,
            score.Factors.Select(f => new AiRiskFactorDto(f.Code, f.Severity, f.Weight)).ToArray());

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private Guid? Actor() => Guid.TryParse(User.FindFirstValue("sub"), out var id) ? id : null;
    private static Task<bool> Allowed(NpgsqlConnection c, NpgsqlTransaction tx, Guid actor, Guid tenant, string permission, CancellationToken ct) => c.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT odca.tenant_actor_has_permission(@actor,@tenant,@permission)", new { actor, tenant, permission }, tx, cancellationToken: ct));
    private static Task<int> SetContext(NpgsqlConnection c, Guid actor, Guid tenant, NpgsqlTransaction tx, CancellationToken ct) => c.ExecuteAsync(new CommandDefinition("SELECT set_config('odca.user_id',@actor,true),set_config('odca.tenant_id',@tenant,true)", new { actor = actor.ToString(), tenant = tenant.ToString() }, tx, cancellationToken: ct));
    private static Task<int> SetTenant(NpgsqlConnection c, Guid tenant, CancellationToken ct) => c.ExecuteAsync(new CommandDefinition("SELECT set_config('odca.tenant_id',@tenant,false)", new { tenant = tenant.ToString() }, cancellationToken: ct));
    private static Task<int> SetTenantTransaction(NpgsqlConnection c, Guid tenant, NpgsqlTransaction tx, CancellationToken ct) => c.ExecuteAsync(new CommandDefinition("SELECT set_config('odca.tenant_id',@tenant,true)", new { tenant = tenant.ToString() }, tx, cancellationToken: ct));

    internal sealed record Source(Guid TenantId, Guid ContractId, Guid DocumentId, Guid VersionId, Guid? UploadedBy, string Text, int AnalyzedChars, string ContentHash);
    private sealed record SourceRow(Guid VersionId, Guid UploadedBy, string RawText);
    private sealed record CachedRow(string Payload, string Provider, int ScoreValue, int AnalyzedChars);

    /// <summary>Payload persistido em jsonb: o suficiente para reconstruir a resposta SEM novo provedor.</summary>
    private sealed record AiAnalysisPayload(
        string? Summary,
        string? OverallRisk,
        AiClauseFindingDto[]? Findings,
        string[]? Categories,
        AiRiskScoreDto? Score,
        AiTemplateComparisonDto? TemplateComparison,
        string? ProviderName,
        string? Model);
}
