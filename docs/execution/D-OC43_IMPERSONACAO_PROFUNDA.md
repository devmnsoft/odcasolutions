# Sprint 9 — impersonação profunda na janela de suporte (D-OC43) (18/10/2026)

Plano pós-S8: **S9** implementa a dívida registrada desde a S5/S6 — o superadministrador deve poder *entrar na visão do cliente* dentro de uma sessão de suporte ativa, operando o portal **como um usuário específico daquela organização**, com tempo limitado, revogável e auditado. Hoje a janela de suporte (D-OC39) só dá visibilidade de plataforma (fila, eventos); não há como operar dentro da organização do cliente.

## 1. Escopo e base

- **Base**: `codex/s00-foundation` @ `e3df1d9` (Sprint 8 / D-OC42).
- **Migração v061** (única mudança de esquema): tabela `odca.support_impersonations` + funções governadas `start/end_support_impersonation`, `support_impersonation_is_valid`, `support_session_members` + extensão do enum de eventos da sessão (`impersonation_started/ended/revoked`). Checksum **`5e1e107223fcd55afb01025d214ffef61d154b1309685604e93c983ef3c0808e`**; `CurrentVersion=61`; release `database/releases/odca-v061.sql` byte-idêntica ao canônico. A regra de hash é a já implementada em `DatabaseMigrator.ParseAndValidate` (confirmada contra o bloco 060 nesta sprint): SHA-256 hex minúsculo do corpo entre a linha de cabeçalho e `-- ODCA-END NNN`, CRLF→LF, com cada ocorrência do checksum declarado substituído pelo literal `REPLACE_WITH_SHA256`. Blocos 1–60 imutáveis.
- **Invariantes mantidos/criados**:
  - Autorização no servidor: início/fim exigem superadministrador ativo (`SECURITY DEFINER` no SQL + política `PlatformAdministrator` na API); MFA recente para **começar** (política `RecentMfa`). O **encerramento da concessão não exige MFA recente por decisão**: ele precisa continuar funcionando mesmo após janelas longas e chega via token do próprio alvo (ator resolvido pelo vínculo `imp_op`).
  - Sessão de suporte **ativa** obrigatória (validada a cada request na API, não só no início).
  - Alvo: usuário **não excluído, sem troca de senha pendente, com membership ativa** no tenant da sessão (o operador entra exatamente naquela organização).
  - Uma impersonação ativa por sessão (índice parcial único); sem nesting (o alvo é cliente, nunca superadmin por papel — o token da impersonação carrega as claims do alvo).
  - Duración curta: 5–60 min, e **nunca além do fim da sessão de suporte** (`LEAST(now()+p_min, sessao.expires_at)`).
  - Revogação instantânea: o fim/revogação revoga a linha de sessão dedicada do alvo → tokens em uso deixam de validar imediatamente (mesmo mecanismo `sid`+`security_version` do login).
  - Auditoria: eventos `impersonation_started/ended/revoked` no histórico da sessão + `audit_events` platform com o **operador real**; durante a impersonação, o token carrega `imp_op` (operador) e `imp_grant` (concessão) além das claims do usuário-alvo.
  - Proteção de dados: o banner indica nome do usuário-alvo e do operador; sem novo dado pessoal armazenado além das chaves existentes.
- **F-1 (núcleo)**: migração v061 + repositório (`ISupportSessionRepository`: `StartImpersonationAsync`, `EndImpersonationAsync`, `IsImpersonationValidAsync`, `ListMembersAsync`) + emissão de JWT dedicado (`ITokenService.IssueImpersonation`) + validação por request no middleware do API + endpoints `GET /platform/support-sessions/{id}/members`, `POST /platform/support-sessions/{id}/impersonation`, `POST /platform/support-sessions/{id}/impersonation/{grantId}/end`.
- **F-2 (BFF/portal)**: "Entrar na visão do cliente" na área de sessões de suporte (lista de membros do tenant); re-signin com o token da impersonação preservando o token original do operador; **banner global** em todas as páginas ("Você está agindo como {usuário} — Suporte ODCA" + botão "Sair"); saída revoga a concessão e restaura o token do operador.
- **F-3 (i18n)**: novos rótulos/messages nos 4 catálogos (delta `d-s9-support-imp.json`): banner, ações, mensagens de erro da API e BFF.
- **Limitações registradas**: (a) durante a impersonação o operador opera com os papéis/permissões do usuário-alvo (visão exata do cliente) — áreas de plataforma exigem sair antes; (b) expiração por inatividade é a mesma do login (o prazo do grant é o teto); (c) o conteúdo persistido continua pt-BR onde aplicável (dívida anterior).
- Sem push; commit único ao final.

## 2. GATE sobre a árvore final

Executado localmente sobre `codex/s00-foundation` pós-S9, sem push (CI oficial "não verificada" neste SHA):

| Etapa | Comando | Resultado |
| --- | --- | --- |
| Build Debug estrito | `dotnet build Odca.sln -c Debug -warnaserror` | **0W/0E** |
| Build Release estrito | `dotnet build Odca.sln -c Release -warnaserror` | **0W/0E** |
| Domain | `dotnet test tests/Odca.Domain.Tests` | **389/389** |
| Integração em DB nova | `scripts/qa/run-integration-tests.ps1 -FreshDb` | **234/234, TEST-EXIT=0** (baseline 231 + 3 fatos novos; aplica v061 no bootstrap) |
| Fatos novos S9 (isolados) | `--filter SupportImpersonationEndpointTests \| ReleaseV061` | **3/3** em 14 s |
| Assets/i18n | `node scripts/check-assets.mjs` | **exit=0** — 10 assets, 19 novas chaves validadas em 4 catálogos, round trips decimal/template |
| Paridade de catálogos | mesclador `merge-i18n-delta.mjs` | **2727 chaves/cultura, paridade 4/4** (+19 sobre o baseline 2708) |
| Smoke SQL v061 | psql em `odca_test_disposable` (transações isoladas por teste) | verde — membros, start/end/revogação do alvo, índice único 23505, alvo inválido 02000, eventos e auditoria com o operador real |
| Auditoria de design d41 (regressão) | `scripts/qa/audit-d41-design.ps1` (navegador real, 17 rotas × idiomas × 4 viewports) | ver §4 |

O smoke SQL registrou uma correção real encontrada antes do GATE: as funções retornavam colunas que shadowavam variáveis PL/pgSQL internas (`expires_at`); renomeadas para a assinatura `RETURNS TABLE(imp_grant, imp_target_session, imp_expires)` e o bloco re-hashado (checksum final §1).

## 3. Alterações

| Arquivo | Mudança |
| --- | --- |
| `database/odca.sql` | Bloco v061: tabela `odca.support_impersonations` (+ índice parcial único de ativa por sessão), CHECK de eventos da sessão estendido (`impersonation_started/ended/revoked`), funções SECURITY DEFINER `start_support_impersonation` (5–60 min, `LEAST` com fim da janela, sessão dedicada do alvo, colheita de grant expirado), `end_support_impersonation` (revoga a sessão dedicada → tokens morrem via `sid`), `support_impersonation_is_valid` (validação por request) e `support_session_members`; REVOKE/GRANT no padrão v020/v060. Blocos 1–60 byte-imutáveis. |
| `database/releases/odca-v061.sql` | Release snapshot nova, byte-idêntica ao canônico. |
| `src/Odca.Infrastructure/Database/DatabaseSchema.cs` | `CurrentVersion` 60 → 61. |
| `src/Odca.Application/Support/ISupportSessionRepository.cs` | +`StartImpersonationAsync`, `EndImpersonationAsync`, `IsImpersonationValidAsync`, `ListMembersAsync`; records `SupportImpersonationStart`, `SupportSessionMemberInfo`; enum `SupportImpersonationStartError`. |
| `src/Odca.Application/Identity/ITokenService.cs` | +`IssueImpersonation(target, session, operatorUserId, grantId)`. |
| `src/Odca.Infrastructure/Support/NpgsqlSupportSessionRepository.cs` | Implementação dos 4 métodos sobre as funções DEFINER (set_config `odca.user_id` por transação; mapeamento de ERRCODEs 23505/02000/22023). |
| `src/Odca.Infrastructure/Identity/JwtTokenService.cs` | Refatoração para `BuildClaims`/`WriteToken`; `IssueImpersonation` emite claims do alvo na sessão dedicada + `imp_op`/`imp_grant`. |
| `src/Odca.Contracts/Support/SupportSessionContracts.cs` | +`StartSupportImpersonationRequest/Response`, `EndSupportImpersonationRequest`, `SupportSessionMemberResponse`. |
| `src/Odca.Api/Program.cs` | `OnTokenValidated`: com claim `imp_grant`, valida a concessão via `support_impersonation_is_valid` em cada request (falha → `api.impersonation.expired`) e expõe o operador em `HttpContext.Items["odca.impersonation.operator"]`. |
| `src/Odca.Api/Controllers/SupportSessionsController.cs` | Políticas movidas para por-ação (o encerrar da concessão aceita token do alvo); endpoints `GET {id}/members`, `POST {id}/impersonation` (RecentMfa), `POST {id}/impersonation/{grantId}/end` (ator por `imp_op` ?? `sub`). |
| `src/Odca.Web/Services/OdcaApiClient.cs` | +`GetSupportSessionMembersAsync`, `StartSupportImpersonationAsync`, `EndSupportImpersonationAsync`. |
| `src/Odca.Web/Services/ImpersonationAuth.cs` | Novo: claims/propriedades da visão, construção da identidade do alvo e restauração do operador a partir do seu JWT original. |
| `src/Odca.Web/Services/RecentAuthGuard.cs` | `DecodeClaims` tornou-se pública (reuso no fluxo de restauração). |
| `src/Odca.Web/Controllers/ImpersonationController.cs` | Novo: `Cliente` (seletor de membro), `Entrar` (re-signin preservando token do operador em property), `Sair` (encerra a concessão e restaura a identidade/token do operador). |
| `src/Odca.Web/Models/SupportSessionViewModels.cs` | +`SupportImpersonationViewModel`, `SupportImpersonationStartViewModel`. |
| `src/Odca.Web/Views/Impersonation/Cliente.cshtml` | Nova: janela + membros elegíveis + duração (5–60) + entrar. |
| `src/Odca.Web/Views/SupportSessions/Index.cshtml` | Botão "Ver como cliente" nas janelas ativas. |
| `src/Odca.Web/Views/Shared/_Layout.cshtml` | Banner global `role="status"` (alvo + operador + expiração) com botão "Sair da visão do cliente". |
| `scripts/i18n/d-s9-support-imp.json` | Delta novo: 19 chaves × 4 culturas (banner, ações, erros BFF/API). |
| `src/Odca.Configuration/Localization/{pt-BR,en-US,es-ES,fr-FR}.json` | Catálogos 2708 → 2727 chaves; paridade 4/4 verificada pelo mesclador idempotente. |
| `tests/Odca.IntegrationTests/SupportImpersonationEndpointTests.cs` | Novo: 2 fatos e2e sobre HTTP real (token do alvo validado por request; saída da própria visão; concorrência/validações/colheita de grant expirado; auditoria com ator real). |
| `tests/Odca.IntegrationTests/MigrationChecksumTests.cs` | Fato `ReleaseV061AddsAuditedDeepImpersonationWithoutRewritingEarlierMigrations` (blocos 1–60 intocados, snapshot byte-idêntica, checksum revalidado). |
| `tests/Odca.IntegrationTests/SupportSessionEndpointTests.cs` | Compatibilidade de construtor (fakes ausentes nos caminhos open/extend/end/revoke). |
| `tests/Odca.Domain.Tests/AuthenticationServiceTests.cs` | `FakeTokenService` implementa `IssueImpersonation`. |

## 4. Prova ao vivo e auditoria de design

- **Smoke SQL v061** (psql em `odca_test_disposable`, transações isoladas por teste, antes do GATE): membros listados (`MEMBROS:1`); T1 start → `support_impersonation_is_valid` true → end → válida=false e sessão dedicada do alvo revogada (`T1_ALVO_REVOGADO:SIM`) → eventos `started,impersonation_started,impersonation_ended`; T2 segunda concessão ativa na mesma janela → `23505` (índice parcial único); T3 alvo sem membership ativa → `02000`.
- **E2E sobre HTTP real** (fatos novos da suíte, revalidados no `-FreshDb`): o bearer emitido é do **alvo** (sub/sid/mfa do alvo + `imp_op`/`imp_grant`, sem papel de plataforma); middleware aceita enquanto o grant vive (**403 puro** em rota de plataforma, não 401) e passa a rejeitar com **401** após o encerramento; a saída **vinda da própria visão do cliente** atribui o ato ao operador real (eventos + `audit_events` platform com `actor_user_id`=operador); concorrência → **409** `support_impersonation.active_exists`; duração fora de 5–60 e alvo estranho → **400**; grant expirado naturalmente é colhido no próximo start (revogando a sessão dedicada antiga) sem bloquear a janela.
- **Auditoria de design d41 (regressão)** em navegador real sobre `odca_test_disposable`: `AUDIT-DONE checks=204 ok=204 overflow=0 authfail=0 httpfail=0`, exit=0 — mesmo resultado da S8 (as 17 rotas do piloto seguem íntegras após o banner/layout e as mudanças de i18n). Transcript versionado em `evidence/d41-design-audit-s9-rodada-final.txt`.
- **Limitação registrada**: a visão do cliente exige seleção interativa (membro + duração), portanto não é uma rota fixa da auditoria de overflow; ela é coberta pelos fatos e2e de HTTP. Sem captura nova de tela desta superfície neste incremento — o aceite humano do fluxo completo segue pendente (§5), como os demais fluxos de plataforma das S7/S8.

## 5. Separação de evidências e portal

- **Teste local**: GATE completo verde sobre este SHA (builds estritos 0W/0E, Domain 389/389, suíte `-FreshDb` 234/234, assets exit=0, auditoria d41 204/204, smoke SQL). Logs desta rodada mantidos em `C:\Users\NCELL-DEV-020\AppData\Local\Temp\opencode` (`s9-freshdb.log`, `s9-audit-d41.log`).
- **CI oficial**: **não verificada** — branch `codex/s00-foundation` sem push nesta rodada; nenhum ID de execução inventado. O push (e o job `build-test`/passo `Tests` do SHA) é decisão externa a esta sprint.
- **Aceite humano**: **pendente** — fluxo de impersonação profunda (entrar/banner/sair) e os 4 idiomas das superfícies de plataforma das S7/S8 ainda não foram aceitos por pessoa designada.
- **Portal / `.odca/progress.json`**: **inalterado** — baseline 40% preservado; sem CI oficial verde deste SHA nem aceite real não há marco creditável nesta sprint (nenhum `completed_commit`/`ci_run_id`/`approved_by` registrado, nenhum peso movido, IDs/pesos/baseline intactos).
- **Segredos/dados**: todos os dados de prova são sintéticos (tenants/usuários GUIDs efêmeros, e-mails `@*.test`/`integration@odca.local`); nenhuma credencial aparece nos relatórios ou evidências.
