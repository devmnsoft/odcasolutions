# Prompt codificado — ficha do contrato, agenda mensal e inbox unificada

Repositório: https://github.com/devmnsoft/odcasolutions  
Base: `codex/s00-foundation` após merge dos PRs #62 e #63  
Branch sugerida: `feat/contract-sheet-agenda-inbox`  
SDK: .NET 10.0.400 · PostgreSQL 18 · schema `odca` · RLS obrigatória

---

## 1. Análise do próximo recorte

O PR #63 entregou **regras de domínio**, não a jornada autenticada:

| Já existe | Ainda não existe |
|---|---|
| `OperationalInbox.Rank` / `Classify` / `IsVisibleTo` | `GET` HTTP da inbox |
| `MonthlyAgendaWindow.ForMonth` / `Includes` | `GET` da agenda só na janela visível |
| `ContractObligation` (mutações + versão otimista) | Formulários BFF das mutações sem UUID digitado |
| `ContractReview` + `RenewalRules` | Ficha única do contrato |
| Listagem paginada de obrigações + histórico | Projeção unificada revisão + obrigação + renovação |
| Vistas pessoais de obrigações | Reuso das vistas na inbox/ficha |

**Não é o próximo passo:** editor visual do Estúdio, PDF rastreável, OCR, série “esta e futuras”, cobrança, sessão de suporte.

**É o próximo passo:** uma **projeção de leitura** (sem tabela `odca.tasks`) + agenda limitada ao mês + ficha que reúne endpoints já existentes.

A inbox **não persiste tarefa**. Ela lê:

- `odca.contract_reviews` com status `in_review` / `changes_requested`
- `odca.contract_obligations` com status `open` / `in_progress`
- contratos com aviso de renovação até `RenewalRules.ThreeMonthWindowEnd(today)`

Autorização idêntica à lista de origem:

- `tenant.obligations.read` + dono, ou `tenant.obligations.read_all`
- permissão equivalente de revisão / renovação
- outro tenant = zero linhas (RLS + filtro de aplicação)

---

## 2. Prompt mestre (copiar para a próxima execução)

Implemente a fatia operacional autenticada do ODCA Solutions.

1. Expor `GET /api/v1/organizations/{tenantId}/inbox` com paginação `Pagination.Normalize`, urgência calculada por `OperationalInbox.Classify`, ordenação por `OperationalInbox.Rank`. Não criar tabela de tarefas.
2. Expor `GET /api/v1/organizations/{tenantId}/agenda?year=&month=` consultando somente `MonthlyAgendaWindow.ForMonth`. Recusar `from > to` com 400. Usar o fuso da organização (`tenants.timezone`), não `DateTime.UtcNow` solto.
3. Expor `GET /api/v1/organizations/{tenantId}/contracts/{contractId}/sheet` com contrato, documentos recentes, revisão corrente, obrigações abertas, contexto de renovação e atalhos. 404 se o contrato não existir no tenant.
4. No BFF: `/organizacoes/{tenantId}/caixa`, `/organizacoes/{tenantId}/agenda`, `/organizacoes/{tenantId}/contratos/{contractId}`. Token só no servidor. POST com antiforgery. Sem UUID digitado (seletores por nome).
5. Conectar mutações de obrigação já existentes no BFF (criar, iniciar, cumprir, cancelar, reabrir, reprogramar, reatribuir) com versão otimista e HTTP 409.
6. Testes de domínio + repositório/autorização. Se .NET 10.0.400 e Postgres 18 descartável existirem: restore `--locked-mode`, build Release, suíte, RLS A×B. Se não existirem, registrar a limitação. Não fabricar captura.

Regras que não podem quebrar:

1. Login consulta `odca.users.password_hash`. Falha genérica.
2. Senha: 12+, maiúscula, minúscula, dígito, símbolo.
3. Produção: `must_change_password` no primeiro acesso; MFA de superadmin se configurado.
4. `--allow-immediate-login` só em Development.
5. Sem senha em texto em `database/odca.sql`, controllers ou views.
6. Não relaxar RLS, antiforgery, nem mover bearer para o JavaScript.
7. Intenção de renovação ≠ extensão de vigência.
8. Não usar `development-runtime.json` como infra de teste.
9. Migration só se for aditiva; não alterar snapshots históricos.
10. Sem pragma para “resolver” CA1848/CA1859.

Development: `admin@odca.local` / `OdcaAdmin#2026Local` via `scripts/provision-local-superadmin.ps1`.

---

## 3. Classes que JÁ EXISTEM — reutilizar, não duplicar

```
Odca.Application.Operations.OperationalWorkKind
Odca.Application.Operations.OperationalUrgency
Odca.Application.Operations.OperationalWorkItem
Odca.Application.Operations.OperationalInbox
Odca.Application.Operations.MonthlyAgendaWindow
Odca.Application.Obligations.ContractObligation
Odca.Application.Obligations.MonthlyRecurrence
Odca.Application.Obligations.ObligationRuleException
Odca.Application.Obligations.ObligationConflictException
Odca.Application.Reviews.ContractReview
Odca.Application.Reviews.ApprovedContentSnapshot
Odca.Application.Renewals.RenewalRules
Odca.Application.Tenancy.Pagination
Odca.Application.Common.IClock
Odca.Application.Identity.LoginLockoutPolicy
Odca.Api.Controllers.ObligationsController
Odca.Web.Controllers.ObligationsController
Odca.Web.Services.OdcaApiClient
```

Permissões de leitura:

- `tenant.obligations.read`
- `tenant.obligations.read_all`
- `tenant.reviews.read` / `tenant.reviews.read_all` (ou a permissão já catalogada em v012)
- `tenant.renewals.read`

Datas da organização: `(now() AT TIME ZONE t.timezone)::date`.

---

## 4. Classes NOVAS — implementar exatamente estes tipos

### 4.1 Contratos HTTP compartilhados

Arquivo: `src/Odca.Contracts/Operations/OperationalInboxContracts.cs`

```csharp
namespace Odca.Contracts.Operations;

public sealed record OperationalInboxQuery(
    string Scope = "mine",
    string? Kind = null,
    string? Urgency = null,
    Guid? ContractId = null,
    Guid? OwnerId = null,
    int Page = 1,
    int PageSize = 20);

public sealed record OperationalInboxItemDto(
    string Kind,
    Guid SourceId,
    Guid ContractId,
    string ContractTitle,
    string Title,
    Guid? OwnerId,
    string? OwnerName,
    DateOnly? DueOn,
    string Urgency,
    string Status,
    string OpenUrl);

public sealed record OperationalInboxPageDto(
    IReadOnlyList<OperationalInboxItemDto> Items,
    int Page,
    int PageSize,
    int Total,
    int Overdue,
    int DueToday,
    int DueThisWeek);

public sealed record MonthlyAgendaQuery(int Year, int Month, string Scope = "mine", Guid? OwnerId = null);

public sealed record MonthlyAgendaDayDto(
    DateOnly Day,
    IReadOnlyList<OperationalInboxItemDto> Items);

public sealed record MonthlyAgendaPageDto(
    int Year,
    int Month,
    DateOnly From,
    DateOnly To,
    IReadOnlyList<MonthlyAgendaDayDto> Days,
    int Total);

public sealed record ContractSheetDocumentDto(
    Guid DocumentId,
    Guid VersionId,
    string Name,
    string SafetyState,
    DateTimeOffset UpdatedAt);

public sealed record ContractSheetReviewDto(
    Guid ReviewId,
    string Status,
    Guid? CurrentReviewerId,
    string? CurrentReviewerName,
    DateTimeOffset? DueAt);

public sealed record ContractSheetRenewalDto(
    DateOnly? EndsOn,
    DateOnly? NoticeDueOn,
    bool InThreeMonthWindow,
    string? CommercialState);

public sealed record ContractSheetDto(
    Guid ContractId,
    string Title,
    string Status,
    DateOnly? StartsOn,
    DateOnly? EndsOn,
    IReadOnlyList<ContractSheetDocumentDto> Documents,
    ContractSheetReviewDto? CurrentReview,
    IReadOnlyList<OperationalInboxItemDto> OpenObligations,
    ContractSheetRenewalDto? Renewal,
    long Version);
```

### 4.2 Consultas de aplicação

Arquivo: `src/Odca.Application/Operations/OperationalInboxService.cs`

```csharp
using Odca.Application.Common;
using Odca.Application.Renewals;
using Odca.Application.Tenancy;
using Odca.Contracts.Operations;

namespace Odca.Application.Operations;

public interface IOperationalInboxRepository
{
    Task<IReadOnlyList<OperationalInboxRow>> ListCandidatesAsync(
        Guid tenantId,
        Guid viewerId,
        bool canReadTenantObligations,
        bool canReadTenantReviews,
        bool canReadTenantRenewals,
        Guid? contractId,
        Guid? ownerId,
        DateOnly today,
        DateOnly renewalWindowEnd,
        CancellationToken cancellationToken);
}

public sealed record OperationalInboxRow(
    OperationalWorkKind Kind,
    Guid SourceId,
    Guid TenantId,
    Guid ContractId,
    string ContractTitle,
    string Title,
    Guid? OwnerId,
    string? OwnerName,
    DateOnly? DueOn,
    string Status);

public sealed class OperationalInboxService(
    IOperationalInboxRepository repository,
    IClock clock)
{
    public async Task<OperationalInboxPageDto> QueryAsync(
        Guid tenantId,
        Guid viewerId,
        bool canReadTenantObligations,
        bool canReadTenantReviews,
        bool canReadTenantRenewals,
        OperationalInboxQuery query,
        string timeZoneId,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(tenantId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(viewerId, Guid.Empty);

        var (page, pageSize) = Pagination.Normalize(query.Page, query.PageSize);
        var today = TimeZoneDate.Today(clock.UtcNow, timeZoneId);
        var windowEnd = RenewalRules.ThreeMonthWindowEnd(today);

        var rows = await repository.ListCandidatesAsync(
            tenantId, viewerId,
            canReadTenantObligations, canReadTenantReviews, canReadTenantRenewals,
            query.ContractId, query.OwnerId, today, windowEnd, cancellationToken);

        var kind = ParseKind(query.Kind);
        var urgency = ParseUrgency(query.Urgency);

        var work = rows.Select(row => new OperationalWorkItem(
            row.Kind, row.TenantId, row.SourceId, row.OwnerId, row.DueOn));

        var ranked = OperationalInbox.Rank(work, today, viewerId, canReadTenantObligations || canReadTenantReviews);
        var allowed = new HashSet<Guid>(ranked.Select(item => item.SourceId));

        var visible = rows
            .Where(row => allowed.Contains(row.SourceId))
            .Where(row => kind is null || row.Kind == kind)
            .Select(row => Map(row, today, tenantId))
            .Where(item => urgency is null || item.Urgency == urgency.ToString())
            .OrderBy(item => (int)Enum.Parse<OperationalUrgency>(item.Urgency))
            .ThenBy(item => item.DueOn ?? DateOnly.MaxValue)
            .ThenBy(item => item.SourceId)
            .ToArray();

        var pageItems = visible.Skip(Pagination.Offset(page, pageSize)).Take(pageSize).ToArray();
        return new OperationalInboxPageDto(
            pageItems, page, pageSize, visible.Length,
            visible.Count(item => item.Urgency == nameof(OperationalUrgency.Overdue)),
            visible.Count(item => item.Urgency == nameof(OperationalUrgency.DueToday)),
            visible.Count(item => item.Urgency == nameof(OperationalUrgency.DueThisWeek)));
    }

    private static OperationalInboxItemDto Map(OperationalInboxRow row, DateOnly today, Guid tenantId)
    {
        var urgency = OperationalInbox.Classify(row.DueOn, today);
        var path = row.Kind switch
        {
            OperationalWorkKind.Obligation => $"/organizacoes/{tenantId}/obrigacoes?obligationId={row.SourceId}",
            OperationalWorkKind.Review => $"/organizacoes/{tenantId}/contratos/{row.ContractId}#revisao",
            _ => $"/organizacoes/{tenantId}/contratos/{row.ContractId}#renovacao"
        };
        return new OperationalInboxItemDto(
            row.Kind.ToString(), row.SourceId, row.ContractId, row.ContractTitle,
            row.Title, row.OwnerId, row.OwnerName, row.DueOn, urgency.ToString(),
            row.Status, path);
    }

    private static OperationalWorkKind? ParseKind(string? value) =>
        Enum.TryParse<OperationalWorkKind>(value, true, out var kind) ? kind : null;

    private static OperationalUrgency? ParseUrgency(string? value) =>
        Enum.TryParse<OperationalUrgency>(value, true, out var urgency) ? urgency : null;
}

public static class TimeZoneDate
{
    public static DateOnly Today(DateTimeOffset utcNow, string timeZoneId)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utcNow, zone).DateTime);
    }
}
```

### 4.3 Agenda mensal

Arquivo: `src/Odca.Application/Operations/MonthlyAgendaService.cs`

```csharp
using Odca.Application.Common;
using Odca.Contracts.Operations;

namespace Odca.Application.Operations;

public interface IMonthlyAgendaRepository
{
    Task<IReadOnlyList<OperationalInboxRow>> ListWindowAsync(
        Guid tenantId,
        Guid viewerId,
        bool canReadTenant,
        Guid? ownerId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken);
}

public sealed class MonthlyAgendaService(
    IMonthlyAgendaRepository repository,
    IClock clock)
{
    public async Task<MonthlyAgendaPageDto> QueryAsync(
        Guid tenantId,
        Guid viewerId,
        bool canReadTenant,
        MonthlyAgendaQuery query,
        string timeZoneId,
        CancellationToken cancellationToken)
    {
        var (from, to) = MonthlyAgendaWindow.ForMonth(query.Year, query.Month);
        MonthlyAgendaWindow.EnsureInclusiveRange(from, to);
        var today = TimeZoneDate.Today(clock.UtcNow, timeZoneId);

        var rows = await repository.ListWindowAsync(
            tenantId, viewerId, canReadTenant, query.OwnerId, from, to, cancellationToken);

        var ranked = OperationalInbox.Rank(
            rows.Select(row => new OperationalWorkItem(row.Kind, row.TenantId, row.SourceId, row.OwnerId, row.DueOn)),
            today, viewerId, canReadTenant);

        var allowed = ranked.Select(item => item.SourceId).ToHashSet();
        var items = rows
            .Where(row => allowed.Contains(row.SourceId) && row.DueOn is not null && MonthlyAgendaWindow.Includes(row.DueOn.Value, from, to))
            .Select(row => new
            {
                Day = row.DueOn!.Value,
                Item = new OperationalInboxItemDto(
                    row.Kind.ToString(), row.SourceId, row.ContractId, row.ContractTitle,
                    row.Title, row.OwnerId, row.OwnerName, row.DueOn,
                    OperationalInbox.Classify(row.DueOn, today).ToString(),
                    row.Status, $"/organizacoes/{tenantId}/contratos/{row.ContractId}")
            })
            .GroupBy(x => x.Day)
            .OrderBy(g => g.Key)
            .Select(g => new MonthlyAgendaDayDto(g.Key, g.Select(x => x.Item).ToArray()))
            .ToArray();

        return new MonthlyAgendaPageDto(query.Year, query.Month, from, to, items, items.Sum(day => day.Items.Count));
    }
}
```

### 4.4 Ficha do contrato

Arquivo: `src/Odca.Application/Operations/ContractSheetService.cs`

```csharp
using Odca.Contracts.Operations;

namespace Odca.Application.Operations;

public interface IContractSheetRepository
{
    Task<ContractSheetDto?> GetAsync(
        Guid tenantId,
        Guid contractId,
        Guid viewerId,
        bool canReadTenant,
        DateOnly today,
        CancellationToken cancellationToken);
}

public sealed class ContractSheetService(IContractSheetRepository repository)
{
    public async Task<ContractSheetDto?> GetAsync(
        Guid tenantId,
        Guid contractId,
        Guid viewerId,
        bool canReadTenant,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty || contractId == Guid.Empty || viewerId == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(contractId));

        return await repository.GetAsync(tenantId, contractId, viewerId, canReadTenant, today, cancellationToken);
    }
}
```

### 4.5 Repositório Dapper (Infrastructure)

Arquivo: `src/Odca.Infrastructure/Operations/OperationalInboxRepository.cs`

Regras SQL:

- Sempre `SET LOCAL odca.tenant_id` e `odca.actor_id` na **mesma** transação da consulta.
- Sem `BYPASSRLS`.
- Inbox: `UNION ALL` das três origens, **sem** `INSERT`.
- Obrigações: `status IN ('open','in_progress') AND deleted_at IS NULL`.
- Revisões: `status IN ('in_review','changes_requested')`.
- Renovações: `ends_on IS NOT NULL AND ends_on <= @renewalWindowEnd AND ends_on >= @today` **ou** `notice_due_on` no intervalo — o que o schema v017 já persistir. Não inventar coluna.
- `DueOn` de revisão = `due_at::date` no fuso da organização.
- `DueOn` de renovação = `RenewalRules.NoticeDueOn` já persistido ou calculado no SELECT.

Esqueleto:

```csharp
using Dapper;
using Npgsql;
using Odca.Application.Operations;

namespace Odca.Infrastructure.Operations;

public sealed class OperationalInboxRepository(NpgsqlDataSource dataSource) : IOperationalInboxRepository
{
    public async Task<IReadOnlyList<OperationalInboxRow>> ListCandidatesAsync(
        Guid tenantId,
        Guid viewerId,
        bool canReadTenantObligations,
        bool canReadTenantReviews,
        bool canReadTenantRenewals,
        Guid? contractId,
        Guid? ownerId,
        DateOnly today,
        DateOnly renewalWindowEnd,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT set_config('odca.tenant_id', @tenantId, true), set_config('odca.actor_id', @actorId, true);",
            new { tenantId = tenantId.ToString(), actorId = viewerId.ToString() },
            transaction, cancellationToken: cancellationToken));

        const string sql = """
            SELECT kind AS Kind, source_id AS SourceId, tenant_id AS TenantId, contract_id AS ContractId,
                   contract_title AS ContractTitle, title AS Title, owner_id AS OwnerId,
                   owner_name AS OwnerName, due_on AS DueOn, status AS Status
              FROM (
                SELECT 'Obligation'::text AS kind, o.id AS source_id, o.tenant_id, o.contract_id,
                       c.title AS contract_title, o.title, o.owner_id, u.display_name AS owner_name,
                       o.due_date AS due_on, o.status
                  FROM odca.contract_obligations o
                  JOIN odca.contracts c ON c.id = o.contract_id AND c.tenant_id = o.tenant_id
                  JOIN odca.users u ON u.id = o.owner_id
                 WHERE o.tenant_id = @tenantId AND o.deleted_at IS NULL
                   AND o.status IN ('open','in_progress')
                   AND (@canReadObligations OR o.owner_id = @viewerId)
                   AND (@contractId IS NULL OR o.contract_id = @contractId)
                   AND (@ownerId IS NULL OR o.owner_id = @ownerId)
                UNION ALL
                SELECT 'Review', r.id, r.tenant_id, r.contract_id, c.title,
                       COALESCE(r.instructions, c.title), s.reviewer_id, ru.display_name,
                       (r.due_at AT TIME ZONE t.timezone)::date, r.status
                  FROM odca.contract_reviews r
                  JOIN odca.contracts c ON c.id = r.contract_id AND c.tenant_id = r.tenant_id
                  JOIN odca.tenants t ON t.id = r.tenant_id
                  JOIN odca.contract_review_steps s ON s.review_id = r.id AND s.tenant_id = r.tenant_id AND s.status = 'current'
                  JOIN odca.users ru ON ru.id = s.reviewer_id
                 WHERE r.tenant_id = @tenantId
                   AND r.status IN ('in_review','changes_requested')
                   AND (@canReadReviews OR s.reviewer_id = @viewerId OR r.requested_by = @viewerId)
                   AND (@contractId IS NULL OR r.contract_id = @contractId)
                   AND (@ownerId IS NULL OR s.reviewer_id = @ownerId)
                UNION ALL
                SELECT 'Renewal', c.id, c.tenant_id, c.id, c.title,
                       c.title, c.owner_id, ou.display_name,
                       COALESCE(c.notice_due_on, c.ends_on), 'active'
                  FROM odca.contracts c
                  JOIN odca.users ou ON ou.id = c.owner_id
                 WHERE c.tenant_id = @tenantId AND c.deleted_at IS NULL
                   AND c.ends_on IS NOT NULL
                   AND c.ends_on <= @renewalWindowEnd
                   AND (@canReadRenewals OR c.owner_id = @viewerId)
                   AND (@contractId IS NULL OR c.id = @contractId)
                   AND (@ownerId IS NULL OR c.owner_id = @ownerId)
              ) inbox
            """;

        var rows = await connection.QueryAsync<OperationalInboxRow>(new CommandDefinition(
            sql,
            new
            {
                tenantId, viewerId, contractId, ownerId, today, renewalWindowEnd,
                canReadObligations = canReadTenantObligations,
                canReadReviews = canReadTenantReviews,
                canReadRenewals = canReadTenantRenewals
            },
            transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return rows.AsList();
    }
}
```

Ajuste nomes de coluna ao schema real (v012/v017). Se `notice_due_on` ou `contracts.owner_id` não existirem, use a coluna canônica já versionada. Não inventar schema.

### 4.6 API

Arquivo: `src/Odca.Api/Controllers/OperationalInboxController.cs`

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odca.Application.Operations;
using Odca.Contracts.Operations;

namespace Odca.Api.Controllers;

[ApiController]
[Authorize(Policy = "PasswordChanged")]
[Route("api/v1/organizations/{tenantId:guid}")]
public sealed class OperationalInboxController(
    OperationalInboxService inbox,
    MonthlyAgendaService agenda,
    ContractSheetService sheet) : ControllerBase
{
    [HttpGet("inbox")]
    public async Task<IActionResult> Inbox(Guid tenantId, [FromQuery] OperationalInboxQuery query, CancellationToken ct)
    {
        var actor = Actor();
        if (actor is null) return Unauthorized();
        // Allowed(tenant.obligations.read) obrigatório para abrir a caixa.
        // canRead* = Allowed das permissões *read_all correspondentes.
        return Ok(await inbox.QueryAsync(tenantId, actor.Value, canReadObligations: false, canReadReviews: false, canReadRenewals: false, query, timeZoneId: "America/Sao_Paulo", ct));
    }

    [HttpGet("agenda")]
    public async Task<IActionResult> Agenda(Guid tenantId, [FromQuery] int year, [FromQuery] int month, [FromQuery] string scope = "mine", [FromQuery] Guid? ownerId = null, CancellationToken ct = default)
    {
        var actor = Actor();
        if (actor is null) return Unauthorized();
        try
        {
            var page = await agenda.QueryAsync(tenantId, actor.Value, canReadTenant: false, new MonthlyAgendaQuery(year, month, scope, ownerId), "America/Sao_Paulo", ct);
            return Ok(page);
        }
        catch (ArgumentOutOfRangeException)
        {
            return ValidationProblem("Informe ano e mês civis válidos.");
        }
        catch (ArgumentException ex)
        {
            return ValidationProblem(ex.Message);
        }
    }

    [HttpGet("contracts/{contractId:guid}/sheet")]
    public async Task<IActionResult> Sheet(Guid tenantId, Guid contractId, CancellationToken ct)
    {
        var actor = Actor();
        if (actor is null) return Unauthorized();
        var dto = await sheet.GetAsync(tenantId, contractId, actor.Value, canReadTenant: false, today: DateOnly.FromDateTime(DateTime.UtcNow), ct);
        return dto is null ? NotFound() : Ok(dto);
    }

    private Guid? Actor()
    {
        var value = User.FindFirst("sub")?.Value ?? User.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value;
        return Guid.TryParse(value, out var id) ? id : null;
    }
}
```

Substitua o `timeZoneId` hardcoded pela leitura de `odca.tenants.timezone` já usada em `ObligationsController`. Replique o helper `Allowed` / `SetTenant` do controller de obrigações — não crie um segundo mecanismo de RLS.

### 4.7 BFF

Arquivos:

- `src/Odca.Web/Controllers/InboxController.cs` — rota `organizacoes/{tenantId:guid}/caixa`
- `src/Odca.Web/Controllers/AgendaController.cs` — rota `organizacoes/{tenantId:guid}/agenda`
- `src/Odca.Web/Controllers/ContractsController.cs` — rota `organizacoes/{tenantId:guid}/contratos/{contractId:guid}`

Padrão obrigatório (igual `ObligationsController` do Web):

```csharp
var token = await HttpContext.GetTokenAsync("access_token");
if (token is null) return Challenge();
var result = await api.GetInboxAsync(token, tenantId, query, ct);
if (result.Status == ApiCallStatus.Unauthorized) return Challenge();
if (result.Status == ApiCallStatus.Forbidden) return Forbid();
```

Views:

- `Views/Inbox/Index.cshtml`
- `Views/Agenda/Index.cshtml`
- `Views/Contracts/Sheet.cshtml`

Requisitos de UI:

- Filtros na URL (`kind`, `urgency`, `page`, `year`, `month`, `viewId`).
- Sem `localStorage` do conteúdo.
- Sem UUID em input livre.
- Estados: carregando, vazio, erro, 409.
- Responsivo 360 / 768 / 1280 / 1440.

Estender `OdcaApiClient` com:

```csharp
Task<ApiCallResult<OperationalInboxPageDto>> GetInboxAsync(...);
Task<ApiCallResult<MonthlyAgendaPageDto>> GetAgendaAsync(...);
Task<ApiCallResult<ContractSheetDto>> GetContractSheetAsync(...);
```

### 4.8 Testes

Arquivo: `tests/Odca.Domain.Tests/OperationalInboxServiceTests.cs`

Cobrir:

- leitor próprio não vê obrigação de outro `OwnerId`
- `read_all` vê as três origens
- `kind=Review` descarta obrigação
- página 2 não repete a 1
- agenda de fevereiro em ano bissexto termina dia 29
- `ForMonth(2026, 13)` lança
- ficha de outro tenant retorna `null`

---

## 5. Rotas finais

| Camada | Método | Rota |
|---|---|---|
| API | GET | `/api/v1/organizations/{tenantId}/inbox` |
| API | GET | `/api/v1/organizations/{tenantId}/agenda?year=&month=` |
| API | GET | `/api/v1/organizations/{tenantId}/contracts/{contractId}/sheet` |
| BFF | GET | `/organizacoes/{tenantId}/caixa` |
| BFF | GET | `/organizacoes/{tenantId}/agenda` |
| BFF | GET | `/organizacoes/{tenantId}/contratos/{contractId}` |
| BFF | POST | mutações já existentes de obrigações, com antiforgery |

---

## 6. Sequência de implementação

1. Contratos (`Odca.Contracts.Operations`) + testes do serviço com repositório fake.
2. Repositório Dapper + `SET LOCAL` na mesma transação.
3. Controller API reutilizando `Allowed` / `SetTenant`.
4. `OdcaApiClient` + controllers BFF + views.
5. Mutações de obrigação no BFF (409 / justificativa / evidência `safe`).
6. `STATUS.md` e `NEXT_PROMPT.md`.
7. PR único contra `codex/s00-foundation`.

Fora desta fatia: editor visual, PDF, OCR, série prospectiva, dashboard consolidado, cache distribuído.

---

## 7. Credenciais de Development (não vão no código da API)

- Superadmin: `admin@odca.local` / `OdcaAdmin#2026Local`
- Cliente: `cliente.teste@odca.local` / `OdcaCliente#2026Local`
- Provisionar: `.\scripts\provision-local-superadmin.ps1`
- Entrar: `https://localhost:7144/entrar`
