using System.Security.Claims;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Odca.Api.Localization;
using Odca.Configuration.Localization;
using Odca.Contracts.Solicitations;

namespace Odca.Api.Controllers;

/// <summary>
/// Seção C: solicitações cliente -> ODCA (revisao/adaptacao/esclarecimento) com maquina de
/// estados e relogio de SLA. Toda a logica de negocio vive nas funcoes SECURITY DEFINER
/// odca.solicitations_*; o controlador so autentica, autoriza e mapeia envelopes de erro.
/// </summary>
[ApiController]
[Authorize(Policy = "PasswordChanged")]
[Route("api/v1/organizations/{tenantId:guid}/solicitations")]
public sealed class SolicitationsController(NpgsqlDataSource dataSource) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(Guid tenantId, [FromQuery] string? service, [FromQuery] string? status, [FromQuery] string? search, CancellationToken ct)
    {
        var actor = Actor(); if (actor is null) return Unauthorized();
        await using var c = await dataSource.OpenConnectionAsync(ct);
        if (!await Allowed(c, actor.Value, tenantId, "tenant.solicitations.read", ct)) return Forbid();
        var rows = await c.QueryAsync<SolicitationListRow>(new CommandDefinition(
            "SELECT * FROM odca.solicitations_list(@actor,@tenant,@service,@status,@search)",
            new { actor, tenant = tenantId, service = string.IsNullOrWhiteSpace(service) ? null : service.Trim(), status = string.IsNullOrWhiteSpace(status) ? null : status.Trim(), search = string.IsNullOrWhiteSpace(search) ? null : search.Trim() },
            cancellationToken: ct));
        return Ok(rows);
    }

    [HttpGet("{solicitationId:guid}")]
    public async Task<IActionResult> Detail(Guid tenantId, Guid solicitationId, CancellationToken ct)
    {
        var actor = Actor(); if (actor is null) return Unauthorized();
        await using var c = await dataSource.OpenConnectionAsync(ct);
        if (!await Allowed(c, actor.Value, tenantId, "tenant.solicitations.read", ct)) return Forbid();
        var owner = await c.QueryFirstOrDefaultAsync<Guid>(new CommandDefinition("SELECT tenant_id FROM odca.solicitations WHERE id=@solicitation", new { solicitation = solicitationId }, cancellationToken: ct));
        if (owner != tenantId) return NotFound();
        var outcome = await EnvelopeResult(c, "SELECT odca.solicitation_detail(@actor,@solicitation)::text", new { actor, solicitation = solicitationId }, HttpContext, ct);
        return outcome.Result;
    }

    [HttpPost]
    public async Task<IActionResult> Open(Guid tenantId, [FromBody] OpenSolicitationRequest request, CancellationToken ct)
    {
        var actor = Actor(); if (actor is null) return Unauthorized();
        // D-OC27: validações específicas com código estável e mensagem localizada.
        if (string.IsNullOrWhiteSpace(request.Service) || string.IsNullOrWhiteSpace(request.Subject) || string.IsNullOrWhiteSpace(request.Body) || request.Body.Trim().Length < 10 || request.IdempotencyKey == Guid.Empty) return this.Invalid("api.solicitations.open_invalid");
        await using var c = await dataSource.OpenConnectionAsync(ct);
        if (!await Allowed(c, actor.Value, tenantId, "tenant.solicitations.manage", ct)) return Forbid();
        var outcome = await EnvelopeResult(c, "SELECT odca.solicitations_open(@tenant,@actor,@service,@priority,@subject,@body,@key,@contractId)::text",
            new { tenant = tenantId, actor, service = request.Service.Trim(), priority = string.IsNullOrWhiteSpace(request.Priority) ? "normal" : request.Priority.Trim(), subject = request.Subject.Trim(), body = request.Body.Trim(), key = request.IdempotencyKey, contractId = request.ContractId }, HttpContext, ct);
        // D-OC31 B.1: abertura → e-mail aos administradores de plataforma ativos.
        // Transação própria (função definer executa em autocommit) com deduplicação por source_id determinístico.
        if (outcome.Data is { } data) await NotifyPlatformOpenAsync(c, tenantId, actor.Value, HttpContext, data, ct);
        return outcome.Result;
    }

    [HttpPost("{solicitationId:guid}/messages")]
    public async Task<IActionResult> Message(Guid tenantId, Guid solicitationId, [FromBody] SolicitationMessageRequest request, CancellationToken ct)
    {
        var actor = Actor(); if (actor is null) return Unauthorized();
        if (string.IsNullOrWhiteSpace(request.Body) || request.Body.Trim().Length < 2) return this.Invalid("api.solicitations.message_invalid");
        await using var c = await dataSource.OpenConnectionAsync(ct);
        if (!await Allowed(c, actor.Value, tenantId, "tenant.solicitations.manage", ct)) return Forbid();
        var outcome = await EnvelopeResult(c, "SELECT odca.solicitations_message(@tenant,@actor,@solicitation,@body,@version)::text",
            new { tenant = tenantId, actor, solicitation = solicitationId, body = request.Body.Trim(), version = request.RowVersion }, HttpContext, ct);
        return outcome.Result;
    }

    [HttpPost("{solicitationId:guid}/actions")]
    public async Task<IActionResult> Action(Guid tenantId, Guid solicitationId, [FromBody] SolicitationActionRequest request, CancellationToken ct)
    {
        var actor = Actor(); if (actor is null) return Unauthorized();
        if (string.IsNullOrWhiteSpace(request.Action)) return this.Invalid("api.solicitations.action_invalid");
        await using var c = await dataSource.OpenConnectionAsync(ct);
        if (!await Allowed(c, actor.Value, tenantId, "tenant.solicitations.manage", ct)) return Forbid();
        var outcome = await EnvelopeResult(c, "SELECT odca.solicitations_transition(@tenant,@actor,@solicitation,@action,@text,@priority,@assignee,@version)::text",
            new { tenant = tenantId, actor, solicitation = solicitationId, action = request.Action.Trim(), text = string.IsNullOrWhiteSpace(request.Text) ? null : request.Text.Trim(), priority = string.IsNullOrWhiteSpace(request.Priority) ? null : request.Priority.Trim(), assignee = request.AssigneeUserId, version = request.RowVersion }, HttpContext, ct);
        return outcome.Result;
    }

    /// <summary>Executa uma funcao definer que devolve payload OU {"error","code"} e mapeia o
    /// envelope para o status HTTP correspondente.</summary>
    /// <param name="context">D-OC27: cultura ativa (X-Odca-Culture) para localizar o título do erro.</param>
    /// <summary>D-OC31 B.1: resultado do envelope; em sucesso, Data é o payload JSON
    /// clonado para pós-processamento (notificação do evento na outbox).</summary>
    internal sealed record EnvelopeOutcome(IActionResult Result, JsonElement? Data);

    internal static async Task<EnvelopeOutcome> EnvelopeResult(NpgsqlConnection c, string sql, object parameters, Microsoft.AspNetCore.Http.HttpContext context, CancellationToken ct)
    {
        var localizer = context.RequestServices.GetRequiredService<LocalizationService>();
        var culture = ApiCulture.For(context);
        var payload = await c.ExecuteScalarAsync<string>(new CommandDefinition(sql + "::text", parameters, cancellationToken: ct));
        if (payload is null) return new EnvelopeOutcome(new ObjectResult(new ProblemDetails { Title = localizer.T("api.internal.no_result", culture), Status = 500, Extensions = { ["code"] = "api.internal.no_result" } }) { StatusCode = 500 }, null);
        using var doc = JsonDocument.Parse(payload);
        if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("code", out var codeEl))
        {
            var code = codeEl.GetString() ?? "solicitations.error";
            // D-OC27: (código, mensagem canônica PT) → chave estável api.sql.* no catálogo;
            // par sem mapeamento preserva o texto original. Detalhe permanece o código estável.
            // D-OC28: sem "error" no envelope, o fallback genérico é resolvido por catálogo.
            var errorText = doc.RootElement.TryGetProperty("error", out var errEl) ? errEl.GetString() : null;
            var sqlKey = ApiSqlMessages.KeyFor(code, errorText ?? "Operacao invalida.");
            string error;
            if (sqlKey is not null)
            {
                var translated = localizer.T(sqlKey, culture);
                error = translated == sqlKey ? (errorText ?? "Operacao invalida.") : translated;
            }
            else
            {
                error = errorText ?? localizer.T("api.solicitations.operation_invalid", culture);
            }
            long? current = doc.RootElement.TryGetProperty("currentRowVersion", out var cv) && cv.ValueKind == JsonValueKind.Number ? cv.GetInt64() : null;
            IActionResult Problem(int status) => new ObjectResult(new ProblemDetails { Title = error, Status = status, Detail = code, Extensions = { ["code"] = code } }) { StatusCode = status };
            return code switch
            {
                "solicitations.not_found" => new EnvelopeOutcome(Problem(StatusCodes.Status404NotFound), null),
                "solicitations.permission" or "solicitations.permission_odca" or "solicitations.permission_platform" => new EnvelopeOutcome(Problem(StatusCodes.Status403Forbidden), null),
                "solicitations.stale" => new EnvelopeOutcome(new ObjectResult(new ProblemDetails { Title = error, Status = StatusCodes.Status409Conflict, Detail = code, Extensions = { ["code"] = code, ["currentRowVersion"] = current } }) { StatusCode = StatusCodes.Status409Conflict }, null),
                "solicitations.plan.required" => new EnvelopeOutcome(Problem(StatusCodes.Status409Conflict), null),
                "solicitations.transition" => new EnvelopeOutcome(Problem(StatusCodes.Status409Conflict), null),
                _ => new EnvelopeOutcome(Problem(StatusCodes.Status400BadRequest), null)
            };
        }
        // Clone duplo: um para a resposta HTTP e outro para o chamador (o JsonDocument
        // é descartado ao final do método).
        return new EnvelopeOutcome(new OkObjectResult(doc.RootElement.Clone()), doc.RootElement.Clone());
    }

    internal static async Task<bool> Allowed(NpgsqlConnection c, Guid actor, Guid tenantId, string permission, CancellationToken ct) =>
        await c.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT odca.tenant_actor_has_permission(@actor,@tenantId,@permission)", new { actor, tenantId, permission }, cancellationToken: ct));

    private Guid? Actor() => Guid.TryParse(User.FindFirstValue("sub"), out var id) ? id : null;

    /// <summary>
    /// D-OC31 B.1: helpers de notificação de eventos de solicitação. As funções SQL
    /// executam em autocommit, então o enqueue da outbox ocorre em transação curta
    /// gerenciada pelo <see cref="NotificationEnqueue"/> (com GUC do tenant) e deduplicação
    /// por source_id determinístico do evento.
    /// </summary>
    internal static async Task NotifyPlatformOpenAsync(NpgsqlConnection c, Guid tenantId, Guid actor, Microsoft.AspNetCore.Http.HttpContext context, JsonElement data, CancellationToken ct)
    {
        if (!TryReadSolicitation(data, out var solId, out var protocol, out var subject)) return;
        var protector = context.RequestServices.GetRequiredService<IDataProtector>();
        var logger = context.RequestServices.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>().CreateLogger("solicitations");
        var admins = (await c.QueryAsync<Guid>(new CommandDefinition(
            "SELECT id FROM odca.users WHERE is_platform_administrator AND NOT is_deleted ORDER BY id",
            new { }, cancellationToken: ct))).ToList();
        foreach (var admin in admins)
        {
            // D-OC34 B.10: assunto genérico por tipo (protocolo/assunto livres não vão no subject).
            await NotificationEnqueue.EnqueueEmailAsync(c, tenantId, actor, admin, "solicitation_opened",
                "Nova solicitação registrada à plataforma", protector,
                NotificationEnqueue.DeterministicSource("solicitation.opened", solId, admin), logger, ct);
        }
    }

    /// <summary>D-OC31 B.1: resposta ou transição da plataforma → e-mail ao autor da solicitação.</summary>
    internal static async Task NotifyOpenedByAsync(NpgsqlConnection c, Guid tenantId, Guid actor, Microsoft.AspNetCore.Http.HttpContext context, JsonElement data, CancellationToken ct)
    {
        if (!TryReadSolicitation(data, out var solId, out var protocol, out var subject)) return;
        if (!data.TryGetProperty("openedBy", out var byEl) || byEl.ValueKind != JsonValueKind.String || !Guid.TryParse(byEl.GetString(), out var openedBy)) return;
        long version = data.TryGetProperty("rowVersion", out var rvEl) && rvEl.ValueKind == JsonValueKind.Number ? rvEl.GetInt64() : 0;
        var protector = context.RequestServices.GetRequiredService<IDataProtector>();
        var logger = context.RequestServices.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>().CreateLogger("solicitations");
        // D-OC34 B.10: assunto genérico por tipo (protocolo/assunto livres não vão no subject).
        await NotificationEnqueue.EnqueueEmailAsync(c, tenantId, actor, openedBy, "solicitation_updated",
            "Atualização da sua solicitação à plataforma", protector,
            NotificationEnqueue.DeterministicSource("solicitation.updated", solId, version), logger, ct);
    }

    private static bool TryReadSolicitation(JsonElement data, out Guid solId, out string protocol, out string subject)
    {
        solId = Guid.Empty; protocol = string.Empty; subject = string.Empty;
        if (data.ValueKind != JsonValueKind.Object
            || !data.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.String
            || !Guid.TryParse(idEl.GetString(), out solId))
        {
            return false;
        }
        if (data.TryGetProperty("protocol", out var p) && p.ValueKind == JsonValueKind.String) protocol = p.GetString() ?? string.Empty;
        if (data.TryGetProperty("subject", out var s) && s.ValueKind == JsonValueKind.String) subject = s.GetString() ?? string.Empty;
        return true;
    }

    internal sealed record SolicitationListRow(Guid Id, Guid TenantId, string OrganizationName, string Protocol, string Service, string Priority,
        string Status, string Subject, DateTime? OpenedAt, DateTime? UpdatedAt, string? AssigneeName, DateTime? FirstResponseDueAt,
        DateTime? ResolutionDueAt, DateTime? FirstResponseBreachAt, DateTime? ResolutionBreachAt, bool Paused, DateTime? ResolvedAt,
        DateTime? ClosedAt, DateTime? CancelledAt, string? CancellationReason);
}
