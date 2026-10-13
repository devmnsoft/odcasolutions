# D-OC26 — Estabilização da suíte de integração (eliminação da linha de base de 54F)

Data: 13/10/2026 · ramo `codex/s00-foundation` (base `6a32a34` = D-OC25) · não pushado

## Objetivo

Tornar a suíte de integração **vermelho-zero e determinística** neste ambiente: os **54 falhas** que a linha de base de Bloco A→G carregou como "falhas ambientais conhecidas" nunca haviam sido dissecadas — eram tratadas como artefato de estado/ordem. Este incremento localiza e corrige as causas reais em banco recém-criado e entrega um runner reutilizável commitado para o gate.

## Método

- Drop + recreate de `odca_test_integration` e rotação sequencial da suíte completa (`DisableTestParallelization`; fixtures por classe em sequência), classificando cada falha individualmente sem favorecimento de ordem nem de estado prévio.
- Causa corrigida na própria camada (schema / DI / contrato de teste), com re-prova em nova rodada de banco zerado.
- **Runner commitado** em `scripts/qa/run-integration-tests.ps1`: variáveis do ambiente QA (com defaults documentados da instância local), kill de dotnets órfãos do repositório (lock de binário no Windows), build de `Odca.sln`, execução de `Odca.IntegrationTests` com log em `%TEMP%\odca-integration-tests.log`, e switch **`-FreshDb`** que zera o banco antes da rotação — o regime em que o defeito de bootstrap de fato aparece.

## Diagnóstico

Num banco zerado, as falhas colapsam para uma superfície pequena e real — **5 causas raiz** (F1–F5), que somadas à história de estado do banco antigo explicam a baseline de 54F. A composição exata dos 54 originais não é decompõevel (produto de acumulação de estado + mascaramento por ordem), mas o que estava oculto ficou exposto e corrigido: qualquer banco novo agora sobe limpo.

### F1 — migração v051 saiu com dollar-quoting corrompido por backslash

- O bloco 051 do canônico continha etiquetas escapadas: guard `DO … \\$;` (linha 5694) e corpo da função `AS \\$ … END \\$;` (linhas 5707/5745). Para o PostgreSQL, `\` fora de citação não é token válido → **qualquer banco novo morre na migração 51** (`syntax error at or near "\"`), derrubando todo fixture de migração.
- Evidência: bytes defeituosos preservados no release imutável `database/releases/odca-v051.sql` (sha256 `84aa7ceea3322742266b32790acfee1adec30fa2d498317f249dee8738fa7547`). Curiosidade registrada: o banco QA do dev (`odca_test_disposable`) carrega a linha 51 com o checksum declarado pelo pacote defeituoso (`d9951f97…`) embora a função publicada ali esteja livre de backslashes — o DDL efetivamente aplicado lá difere dos bytes do pacote.
- **Correção no canônico (in place)**: etiquetas certas — `$odca$` no guard, `$fn$` no corpo da função (o corpo não continha outro `$`; mudança segura). Novo checksum declarado **H = `1f8eaf7e802419d64ae1f45bc60b0031b7f600a9173db8cfb584a7c06241069a`** estampado em header/guard/INSERT; estabilidade do checksum recomputada OK; arquivo permanece CRLF sem BOM.
- **Releases**: `odca-v051.sql` **imutável** (pacote defeituoso preservado para auditoria); `odca-v052.sql` regenerada byte-idêntica ao canônico pós-reparo (sha256 `3a1b3bfa3cef9fe7c3f40b814267ed50183dc7580648ebe2e10eea4471601c0b`).
- **Migrator** (`DatabaseMigrator`): entrada `KnownDefectivePackage` para `V051SolicitationsOpenQuoting` (`Defective=d9951f97… → Repaired=H`) com **reafirmação idempotente** para bancos que gravaram o checksum defeituoso sem executar o bloco quebrado (caso do QA): `ALTER TABLE … ADD COLUMN IF NOT EXISTS contract_id` + `CREATE INDEX IF NOT EXISTS` + `DROP FUNCTION`(7 args)/`CREATE OR REPLACE`(8 args, corpo fiel ao publicado, tag `$fn$`) + `GRANT TO odca_app` (sem REVOKE, sem concessão além do fiel ao publicado) + restamp da linha de histórico para H.
- **Fatos novos**: `DefectivePackageRepairTests` +2 (histórico defeituoso é reparado ao pacote publicado — asserções `pronargs=8` e proargnames `p_contract_id`/`p_idempotency`; reparação idempotente uma vez que H está gravado); `MigrationChecksumTests` reescrita para a divergência v051 (release contém `ODCA-MIGRATION 051 CHECKSUM d9951f97…` e `"DO \\$"`, canônico declara H, bytes diferem; loop 1..50 idêntico entre release v051 e canônico).
- **Prova comportamental**: `dotnet run --project src/Odca.Bootstrap -- migrate` sobre o disposable (linha 51 presa em `d9951f97…`) → linha restampada para H, função 8-args íntegra, `/health/ready` **503 → 200** sem restart.

### F2 — connection string vazia gerava ANE do Npgsql antes da mensagem amigável

- `ConnectionStrings:Database` vazia/caixa fazia a factory `NpgsqlDataSource` lançar `ArgumentNullException('Parameter is null. (Parameter Host)')` durante a materialização de hosted service — **antes** de `StartupValidationService` poder produzir o erro amigável.
- Correção: guard `IsNullOrWhiteSpace` na factory de `DependencyInjection.cs` → mesmo `InvalidOperationException("ConnectionStrings:Database não foi configurada.")` do caso já tratado.

### F3 — `ApiHostStartupTests` não era hermética

- O teste partia de `Host=localhost;Database=odca_test_startup` hardcoded (banco que precisava existir). Correção: a classe agora recebe `DatabaseFixture` (`IClassFixture`) e `ValidConfiguration()` usa `database.AppConnectionString` — mesmo contrato `ODCA_TEST_*` do resto da suíte.

### F4 — pins obsoletos de catálogo (produto decidiu no v038; testes congelaram o pré-v038)

- v038 desabilitou deliberadamente `module.reviews` nos planos basic-v2 e intermediate-v2 — solicitações/revisões tornam-se capacidade Enterprise (decisão de produto coerente com o serviço de SLA doc também Enterprise-only). Estado canônico em banco novo: **6/6/7** módulos habilitados (basic/intermediate/enterprise).
- Dois testes pinavam `7/7/7` desde antes do v038: `S00FlowTests` (catálogo publicado) e `HierarchicalAdministrationTests` (tuplas). Decisão registrada: **canônico vence** — a decisão de produto permanece; corrigem-se os pins (não o dado). O drift runtime observado no disposable (reviews reabilitado no basic durante o dev) é ruído operacional, não intenção.
- Correção: pins atualizados para `6/6/7` com comentário citando o v038 em cada teste.

### F5 — seeding do catálogo oficial esbarrava em RLS em banco novo (42501)

- `OfficialCatalogSeedingService.SeedAsync` insere INSERT multi-estado (template + versão + auditoria) como a role da aplicação; o evento de auditoria tem escopo platform com `action='template.official_catalog_seeded'`, mas a policy `platform_security_insert` (`odca.sql`) só admitia `action LIKE 'identity.%'` → `42501 … política … "audit_events"`, retries 5×2s, e o 5º retry re-lança ⇒ boot da API falha em `CreateClient`.
- Dispara **somente** quando as linhas globais do catálogo ainda não existem (banco novo). Hosts que sobem após outros fluxos terem criado as linhas globais pulam o seed — o mascaramento por ordem que contribuía para a baseline parecer "ambiental". Dev não reproduzia (runtime dev conecta como superuser, que bypassa RLS — anotado, não alterado).
- **Correção: migração v053** estendendo a policy: `DROP POLICY IF EXISTS platform_security_insert` + `CREATE POLICY … FOR INSERT TO odca_app WITH CHECK (scope_type='platform' AND tenant_id IS NULL AND (action LIKE 'identity.%' OR action = 'template.official_catalog_seeded'))`. Checksum **K = `6aac915a94439996b51ae39c6855bc8bad52aaaffd589b8e6082d773cdc3d4a4`**; shape do guard DO idêntico aos blocos recentes (`$odca$`); `DatabaseSchema.CurrentVersion` 52 → 53; release `odca-v053.sql` byte-idêntica ao canônico.
- Alternativas rejeitadas: renomear a action para caber sob `identity.%` (semântica desonesta — seed não é identidade) e wrapper definer de auditoria (custo idêntico de migração, mais código; não há helper definer de auditoria platform existente para reaproveitar — todos os INSERTs em `audit_events` do canônico vivem dentro de funções definer ou são gravados com escopo tenant).
- **Fato novo**: `MigrationChecksumTests.ReleaseV053ExtendsPlatformAuditPolicyForOfficialCatalogSeed` (checksums válidos nos três arquivos; blocos 1..52 idênticos entre release v052 e canônico; predicado novo presente em canônico e snapshot; sem `ODCA-MIGRATION 054` no snapshot). `CurrentReleaseMatchesCanonicalSqlAndRuntimeVersion` passa a mirar `odca-v053.sql` automaticamente após o bump.
- **Prova comportamental**: `pg_policies` do disposable pós-bootstrap exibe a `with_check` estendida; API pronta em 200.

## Gates (todos re-executados após drop/recreate)

| Gate | Resultado |
|---|---|
| Build `Odca.sln` | **0W/0E** |
| Domain | **355/355 PASS** |
| Integração (banco zerado, via runner commitado `-FreshDb`) | **173/173 PASS** (baseline herdada: 54F/116P/170) |
| Comportamental no disposable | linha 51 `d9951f97… → H`; v053 aplicada (`K`); policy estendida; `/health/ready` 200 |

Total da suíte: 170 → **173** (+2 fatos de reparação + 1 fato de release). Sem resíduo conhecido: a "linha de base ambiental" está encerrada — em banco novo, tudo passa; no banco longevo, o runner `-FreshDb` reproduz o regime determinístico.

## Inventário final de hashes

| Artefato | sha256 |
|---|---|
| `database/odca.sql` (canônico atual, com v053) | `73df0018cfbb6828e228299df672f98b56c97e7a6f8126b8e516c2878539c9c9` |
| `database/releases/odca-v051.sql` (defeituoso, imutável) | `84aa7ceea3322742266b32790acfee1adec30fa2d498317f249dee8738fa7547` |
| `database/releases/odca-v052.sql` (canônico pré-v053, regenerada) | `3a1b3bfa3cef9fe7c3f40b814267ed50183dc7580648ebe2e10eea4471601c0b` |
| `database/releases/odca-v053.sql` (== canônico) | `73df0018cfbb6828e228299df672f98b56c97e7a6f8126b8e516c2878539c9c9` |
| Migração 51 (defeituosa → reparada) | `d9951f97…4df28` → `1f8eaf7e…1069a` (H) |
| Migração 53 (nova) | `6aac915a94439996b51ae39c6855bc8bad52aaaffd589b8e6082d773cdc3d4a4` (K) |

## Próximos passos

- Extensão natural (escolha do usuário): localização server-side da API (erros em PT hoje; códigos de erro estáveis) + bag i18n para áreas fora do Estúdio.
- Backlog cosmético: dedup das regras duplicadas de `.studio-grid` em max-width:1100px.
- Sem push/merge/deploy automático.
