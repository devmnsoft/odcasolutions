using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Npgsql;
using Odca.AI;
using Odca.Api.Localization;
using Odca.Contracts.Ai;

namespace Odca.Api.Controllers;

// D-OC52 (Sprint 18): análise IA de contrato por versão EXATA do documento (invariante
// assinatura↔versão). A fonte é o texto já extraído (camada de extração/OCR) da versão
// indicada; ContentHash liga a análise ao conteúdo analisado e AnalyzedChars declara o quanto
// do texto foi efetivamente enviado ao provedor (truncamento honesto). Sem texto extraído o
// recurso responde 200 com available=false e reason="no_text" — estado, não erro.
[ApiController]
[Route("api/v1/organizations/{tenantId:guid}/contracts/{contractId:guid}/documents/{documentId:guid}/versions/{versionId:guid}/ai-analysis")]
[Authorize(Policy = "PasswordChanged")]
public sealed class ContractAiController(NpgsqlDataSource dataSource, IAiService ai, IOptions<AiOptions> aiOptions) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Analyze(Guid tenantId, Guid contractId, Guid documentId, Guid versionId, CancellationToken ct)
    {
        var actor = Actor(); if (actor is null) return Unauthorized();
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await SetContext(connection, actor.Value, tenantId, transaction, ct);
        if (!await Allowed(connection, transaction, actor.Value, tenantId, "tenant.contracts.read", ct)) return Forbid();
        var row = await connection.QuerySingleOrDefaultAsync<ExtractionSource>(new CommandDefinition("""
            SELECT v.id AS VersionId, COALESCE(r.raw_text, '') AS RawText
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
        await transaction.CommitAsync(ct);
        if (row is null) return NotFound();
        if (string.IsNullOrWhiteSpace(row.RawText))
        {
            return Ok(new AiAnalysisResponse(false, "no_text", null, null, [], [], "none", null, 0));
        }

        var text = row.RawText.Trim();
        var maximumChars = Math.Max(1_000, aiOptions.Value.MaxInputChars);
        var analyzedChars = Math.Min(text.Length, maximumChars);
        var contentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
        var result = await ai.AnalyzeContractAsync(new AiContractInput(text, text.Length, ApiCulture.For(HttpContext)), ct);
        var findings = result.Findings.Select(f => new AiClauseFindingDto(f.Code, f.Risk, f.Evidence)).ToArray();
        return Ok(new AiAnalysisResponse(result.Available, result.Available ? null : result.Reason, result.Summary, result.OverallRisk,
            findings, [.. result.Categories], result.ProviderName, contentHash, analyzedChars));
    }

    private Guid? Actor() => Guid.TryParse(User.FindFirstValue("sub"), out var id) ? id : null;
    private static Task<bool> Allowed(NpgsqlConnection c, NpgsqlTransaction tx, Guid actor, Guid tenant, string permission, CancellationToken ct) => c.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT odca.tenant_actor_has_permission(@actor,@tenant,@permission)", new { actor, tenant, permission }, tx, cancellationToken: ct));
    private static Task<int> SetContext(NpgsqlConnection c, Guid actor, Guid tenant, NpgsqlTransaction tx, CancellationToken ct) => c.ExecuteAsync(new CommandDefinition("SELECT set_config('odca.user_id',@actor,true),set_config('odca.tenant_id',@tenant,true)", new { actor = actor.ToString(), tenant = tenant.ToString() }, tx, cancellationToken: ct));

    private sealed record ExtractionSource(Guid VersionId, string RawText);
}
