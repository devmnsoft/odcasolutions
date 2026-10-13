# D-OC20 — Correção do 500 nas Saved Views da Caixa Operacional (migração v052)

Data: 2026-10-13 · Branch: codex/s00-foundation · Base: `dd62d67`

## Contexto

Defeito pré-existente descoberto no item 19 (D-OC18): salvar uma vista pessoal a partir da Caixa Operacional (`/organizacoes/{t}/caixa`) devolvia **500** (PostgreSQL 23514, check constraint violada). O BFF `InboxController.SaveView` envia `listingType="inbox"` para `POST api/v1/organizations/{t}/saved-views`, mas a constraint canônica de `odca.saved_work_views` só admitia `('obligations','reviews','contracts')`. Consequência colateral: `saved_work_views` estava vazia em todos os tenants — a funcionalidade nunca chegou a gravar.

## Decisão

**Alargar o schema (migração v052)** em vez de cortar a funcionalidade do BFF: toda a camada de aplicação já implementa e valida vistas de `inbox` desde o surgimento da Caixa — `SavedViewFilterPolicy` tem whitelist própria de filtros (`scope`,`kind`,`urgency`,`contractId`,`ownerId`,`relativeDate`), de ordenação (`due_date`) e de valores (incluindo `kind=Exception` do item 20), e `SavedViewsController` é genérico por `listingType`. O único ponto de divergência era o CHECK do banco.

Detalhes da migração:

- **`DROP CONSTRAINT IF EXISTS` + `ADD CONSTRAINT` com o mesmo nome** (`saved_work_views_listing_type_check` — nome automático do PostgreSQL para CHECK inline de coluna) agora com os 4 valores. O par é idempotente e seguro sob o `pg_advisory_xact_lock` dos demais blocos.
- **DDL histórico não reescrito**: o `CREATE TABLE` (odca.sql:1925) permanece com os 3 valores originais; bancos novos atravessam o bloco v052 assim como os antigos (padrão da casa: só se apendem blocos).
- Guard DO de checksum + linha em `schema_migrations` com o formato exato dos blocos v050/v051.
- Os índices únicos já incluem `listing_type` (`saved_work_views_active_name_uq`, `saved_work_views_default_uq`) — nomes iguais em tipos diferentes coexistem e o "padrão" segue por `(tenant, dono, listing_type)`; nada a alterar.
- `DatabaseSchema.CurrentVersion` 51 → 52. O health gate (`PostgresReadinessHealthCheck`) exige `max(version)=CurrentVersion` — por isso `/health/ready` ficou 503 após o build e voltou a 200 após `dotnet run --project src/Odca.Bootstrap -- migrate`, **sem restart da aplicação**.

## Mudanças

| Arquivo | Alteração |
|---------|-----------|
| `database/odca.sql` | bloco v052 anexado (checksum `bb9690bb14f4773fcf9cecfd049a089aa93baaff2c56766023ca9afe1dcc86cd`): DROP/ADD de `saved_work_views_listing_type_check` com `('obligations','reviews','contracts','inbox')` + registro em `schema_migrations` |
| `database/releases/odca-v052.sql` | snapshot **byte-idêntico** ao canônico (exigido por `CurrentReleaseMatchesCanonicalSqlAndRuntimeVersion`) |
| `src/Odca.Infrastructure/Database/DatabaseSchema.cs` | `CurrentVersion = 52` |
| `tests/Odca.IntegrationTests/MigrationChecksumTests.cs` | `ReleaseV052AllowsInboxListingTypeWithoutRewritingEarlierMigrations` — checksums válidos (snapshot/anterior/canônico), blocos 001–051 inalterados vs. `odca-v051.sql`, novo CHECK presente, sem bloco 053 |

Zero mudança de código de aplicação — o defeito era só do contrato de schema.

## Evidência comportamental (aplicação real, API `:7143` + Web `:7144`, banco `odca_test_disposable`)

Build da solution **0 avisos / 0 erros**. Domain **302/302 PASS**. `MigrationChecksumTests` **36/36 PASS** (35 anteriores + 1 novo). Suíte de integração em `odca_test_local`: **54 falhas / 112 aprovados / 166** — idêntica à linha de base ambiental (54/111/165) mais o novo teste aprovado.

A migração foi aplicada pelo runner real (`dotnet run --project src/Odca.Bootstrap -- migrate` → "Migração aplicada e seed de Development verificado"); confirmado `version=52` em `odca.schema_migrations` e `pg_get_constraintdef` = `CHECK ((listing_type = ANY (ARRAY['obligations','reviews','contracts','inbox'])))`.

Sessões MFA consumidas nesta homologação: `REV20A` (admin/API) e `REV20B` (admin/Web). Nota operacional: cada desafio MFA consome um código — a primeira execução falhou por reaproveitar o código já consumido (registrar: um código por fluxo).

| Teste | Cenário | Resultado observado |
|-------|---------|---------------------|
| T1 | Baseline (zero vistas `inbox` ativas) + login real admin (API, token+MFA) + `GET inbox?scope=mine` | `200` — ambiente limpo e autenticado antes do teste |
| T2 | **Reprodução exata do defeito pelo formulário Web**: login + MFA via cookies → `POST /organizacoes/{t}/caixa/vistas` (nome `Excecoes hoje`, `scope=mine`, `kind=Exception`, `urgency=DueToday` + antiforgery) | **302** → `/organizacoes/{t}/caixa?scope=mine&kind=Exception&urgency=DueToday&viewId={id}` (antes: 500); página de retorno renderiza o aviso TempData **"Vista pessoal salva para a caixa operacional."** e a vista salva aparece na lista |
| T3 | CRUD pela API com o token real | `List?listingType=inbox` → 1 item com `filters={scope:mine,kind:Exception,urgency:DueToday}` e `sort=due_date` (byte-exatos); `Get {id}` 200; `PUT` rename → 204 (`row_version` 1→2); `PUT` com versão obsoleta → **409** (lock otimista íntegro); `PUT isDefault=true` → 204; **aplicação automática do padrão**: `GET /caixa` sem filtros → 302 para `/caixa/vistas/{id}` (OpenView) |
| T4 | Negativos | `POST saved-views` com `listingType="caixa"` → **400** (validação do policy intacta); vistas `obligations` e `inbox` com **o mesmo nome** criam ambas sem colisão (unique index já isola por `listing_type`) |
| T5 | Limpeza lógica + estado final | `DELETE` das 3 vistas de teste → 204; `List?listingType=inbox` → 0; linhas de teste removidas fisicamente do banco QA; **zero resíduo** |

Classificação: comportamento provado por comandos reais da aplicação (formulário Web + HTTP + estado do banco + health gate). SQL usado apenas para prover/consumir códigos MFA, asserções de observação e limpeza das linhas de teste.

## Limitações

- Defeito histórico: vistas `inbox` nunca persistiram antes deste incremento — não havia dado antigo a migrar (a coluna só passa a receber `'inbox'`).
- O bloco substitui a constraint por nome; um banco com o nome auto-gerado divergente (impossível via `CREATE TABLE` atual) não seria atingido pelo `DROP ... IF EXISTS` — registrado para conhecimento.
- Sem push/merge/deploy automático.
