# Sprint 5 — segurança e operacional: reautenticação MFA recente para operações sensíveis, sessões temporárias de suporte de plataforma com ator real e purga de retenção documental, com prova ao vivo fails=0 (D-OC39) (19/10/2026)

Plano de seis sprints para candidata ao piloto (estreia 11/01/2027). Esta entrega fecha a Sprint 5.

## 1. Escopo e base

- **Base**: `codex/s00-foundation` @ `ebbfbe1` (Sprint 4 / D-OC38). Migração nova neste incremento: **v060** (`extend_support_session` + `support_session_tenant_name`).
- **F-1 (avanço)**: reautenticação MFA **recente** para operações sensíveis. Os tokens de sessão passam a carregar os claims `mfa_enrolled` e `mfa_verified_at` (instante unix em segundos) quando o segundo fator foi verificado na sessão (`JwtTokenService`); nova política **`RecentMfa`** na API com janela configurável `Identity:RecentAuthMinutes` (padrão **15**, explícito nos `appsettings` da API e da Web). Semântica do handler (`RecentMfaHandler`): perfil não superadministrador nunca exige (nesta plataforma o segundo fator é requisito só de superadministradores — mesma regra canônica do `MfaVerifiedForSuperAdministratorRequirement`); fluxo de primeira troca de senha (`must_change_password=true`) é **isento** (a troca forçada precede o MFA, sem ciclo); superadmin **sem** MFA inscrito é isento (pré-inscrição); superadmin inscrito precisa de verificação dentro da janela. Aplicação: `change-password`, `mfa/enrollment`, `mfa/enrollment/regenerate` (AuthController) e nove escritas administrativas (bloquear/desbloquear/inativar/restaurar membro, atualizar papéis do membro, criar/atualizar papel, atualizar permissões do papel, transferir administração — OrganizationsController) e as escritas das sessões de suporte (F-2). Na Web, guard espelhado `RecentAuthGuard.NeedsFreshChallenge` (mesma regra sobre os claims do token) faz o BFF redirecionar para `/mfa/desafio?returnUrl=…` preservando o destino da ação; **a API devolve 403 puro** com código estável `api.auth.recent_auth_required` (sem redirect, sem mensagem de negócio).
- **F-2 (avanço)**: sessões temporárias de suporte de plataforma ("janela de atendimento") ponta a ponta. Funções SQL governadas v060: `extend_support_session` (1–480 min, evento `extended`, auditoria `superadmin.support_session.extended`, expiração avança por `GREATEST(now(), expires_at) + make_interval` — **nunca retrocede**) e `support_session_tenant_name` (`SECURITY DEFINER STABLE`; superadmin ativo → `COALESCE(display_name,'-')`; caso contrário NULL) — esta última é a leitura governada do nome da organização porque `odca.tenants` tem **RLS FORCADA** (`tenant_isolation`) e o JOIN direto do repositório via `odca_app` não enxerga a linha. API `api/v1/platform/support-sessions` (list/get/events/open/extend/end/revoke; escritas protegidas pela política `RecentMfa`; unicidade de sessão ativa por admin+organização → 23505 mapeado em **409 `support_session.active_exists`**; `not_found`/`not active` → **404 `support_session.not_found`**; `Valid` = `status='active' AND expires_at>now()` **virtual**, sem reaper; `end` só dono/admin, `revoke` qualquer superadmin — ambos terminais com `ended_by`; eventos `started`/`extended`/`ended`/`revoked`; auditoria `superadmin.support_session.{opened,extended,ended,revoked}` com **ator real**). Web BFF `/administracao/suporte-sessoes` pt-first (formulários abrir/estender/encerrar/revogar, anti-forgery, feedback via `TempData` na view — alertas adicionados a esta view nesta sprint), item de navegação `suporte-sessoes` nos 4 catálogos. **Fronteira honesta registrada**: a janela é *auditável* (quem abriu, por quê, quando, quanto tempo, quem encerrou/revogou); *impersonação profunda* (operar como o cliente dentro da organização) permanece dívida registrada no BACKLOG.
- **A-1 (avanço operacional)**: purga física após a retenção da área reconciliada — `DocumentWorker`: chave `Documents:ReconciledPurgeRetentionDays` (padrão **30 dias**), seleção testável `SelectReconciledPurgeCandidates` (estático; usa **horário de escrita** do arquivo mais velho que o corte, em todos os subdiretórios de mês — o mês apenas organiza a visão humana), remoção com contagem/bytes em log e falha isolada por arquivo sem expor caminho nem quebrar a reconciliação (log Warning).
- **A-2 (prova)**: prova ao vivo reproduzível `scripts/qa/validate-d39-s5-web.ps1` (23 checks definidos; rodada final fails=0) + capturas CDP da página em pt-BR (`capture-d39-screens.ps1`), com a limitação de idioma registrada (§5).
- **Defeito de produto descoberto e corrigido**: as leituras de plataforma do repositório JOINavam `odca.tenants` diretamente via `odca_app` — como a tabela tem **RLS FORCADA** (`tenant_isolation`: `id = current_setting('odca.tenant_id')`), o JOIN retornava zero linhas e `open` respondia 201 com o nome da organização vazio (e `GET` devolvia a linha sem nome). Correção canônica: leituras passam a usar `support_session_tenant_name` com `set_config('odca.user_id', @actor, true)` em transação — mesmo padrão dos SECURITY DEFINER de open/close/extend. Coberto pelos 8 fatos de integração (nomes presentes nas respostas) e pela prova ao vivo (linha com o nome real do tenant demo).
- **Fora de escopo registrado**: impersonação profunda dentro da janela de suporte; i18n da área de sessões de suporte (pt-first, mesma política das superfícies operacionais anteriores); deploy físico multi-instância; SMTP real (herdado, canal de e-mail segue desabilitado por padrão).

## 2. GATE

Execução sobre a árvore final (após a correção do RLS e os scripts da prova já versionados):

| Etapa | Resultado |
|---|---|
| Build Release estrito (`-warnaserror`) | exit=0 · **0 avisos / 0 erros** |
| Build Debug estrito (`-warnaserror`) | exit=0 · **0 avisos / 0 erros** |
| `Odca.Domain.Tests` | **389/389 PASS** (baseline 367 + 22 novos: handler 8 + claims JWT 2 + guard da Web 6 fatos e 1 teoria c/ 4 casos + seleção de purga do worker 2) |
| `scripts/check-assets.mjs` | exit=0 (assets JS + chaves i18n dos módulos × 4 catálogos + round trips decimal/editor) |
| Suíte de integração `-FreshDb` | **231/231 PASS** (FRESH-DB-OK → BUILD-OK → TEST-EXIT=0) = baseline S4 222 + 8 fatos dos endpoints de sessão de suporte (incl. 2 e2e HTTP com stale/fresco) + 1 fato de checksum da v060 |

Comandos reproduzíveis (orquestrador local `%TEMP%\opencode\d37-gate.ps1`, reaproveitado): `dotnet build .\Odca.sln -c Release -warnaserror`; `dotnet build .\Odca.sln -c Debug -warnaserror`; `dotnet test .\tests\Odca.Domain.Tests -c Debug --no-build`; `node .\scripts\check-assets.mjs`; `scripts\qa\run-integration-tests.ps1 -FreshDb`.

## 3. Mudanças

### Migração v060

- Checksum **`1562ceacb177ea199cfeaf6eaf281af3e8052f704c570a3373f7a8e4bf5169ca`**; `CurrentVersion=60`; release `database/releases/odca-v060.sql` byte-idêntica ao canônico (`database/odca.sql`). Blocos 1–59 imutáveis (verificado pelo fato `ReleaseV060AddsAuditedSupportSessionExtensionWithoutRewritingEarlierMigrations`, que também assera as duas funções do bloco 60 e o grant restrito ao `odca_app`).
- Conteúdo: `odca.extend_support_session(p_actor_id, p_session_id, p_minutes DEFAULT 30) RETURNS timestamptz` (`SECURITY DEFINER`, guarda de superadmin ativo, duração 1–480, trava `FOR UPDATE`, só sessões ativas, insere evento `extended` com `details` jsonb `{minutes,new_expires_at}` e audit `superadmin.support_session.extended`, expiração por `GREATEST(now(), expires_at)`) + `odca.support_session_tenant_name(p_actor_id, p_tenant_id) RETURNS text` (`SECURITY DEFINER STABLE`, NULL se ator não é superadmin ativo) + grants EXECUTE restritos ao `odca_app` (REVOKE PUBLIC).

### Catálogos

- **+5 chaves por cultura** em `src/Odca.Configuration/Localization/{pt-BR,en-US,fr-FR,es-ES}.json`: rótulo de navegação `suporte-sessoes` + 4 mensagens de API (`api.auth.recent_auth_required`, `api.support_session.validation`, `api.support_session.active_exists`, `api.support_session.not_found`) — total **1770 chaves/cultura**, paridade exata 4/4 (confere no GATE/check-assets). BOM preservado, sem nova linha final.

### Código

| Arquivo | Mudança |
|---|---|
| `src/Odca.Infrastructure/Identity/JwtTokenService.cs` | **F-1**: claims `mfa_enrolled` e `mfa_verified_at` (instante unix) emitidos quando o MFA foi verificado na sessão |
| `src/Odca.Api/RecentMfaRequirement.cs` (novo) | **F-1**: `RecentMfaOptions` (seção `Identity`, `RecentAuthMinutes` padrão 15), `RecentMfaRequirement`, `RecentMfaHandler` (regras do §1; falha com código estável `api.auth.recent_auth_required`) |
| `src/Odca.Api/Program.cs` | **F-1**: binding das options + registro do handler + política `RecentMfa` |
| `src/Odca.Api/Controllers/AuthController.cs` | **F-1**: `[Authorize(Policy="RecentMfa")]` em `change-password`, `mfa/enrollment`, `mfa/enrollment/regenerate` |
| `src/Odca.Api/Controllers/OrganizationsController.cs` | **F-1**: `RecentMfa` nas 9 escritas administrativas (membros, papéis, permissões, transferência) |
| `src/Odca.Api/Controllers/SupportSessionsController.cs` (novo) | **F-2**: endpoints list/get/events/open/extend/end/revoke; escritas com `RecentMfa`; 409/404 com códigos estáveis; ator propagado a todas as operações |
| `src/Odca.Application/Support/ISupportSessionRepository.cs` (novo) | **F-2**: contrato do repositório com `actorId` em todas as leituras/escritas |
| `src/Odca.Contracts/Support/SupportSessionContracts.cs` (novo) | **F-2**: DTOs de request/response (incl. eventos) |
| `src/Odca.Infrastructure/Support/NpgsqlSupportSessionRepository.cs` (novo) | **F-2**: implementação sobre funções governadas; leitura de nomes via `support_session_tenant_name` + `set_config('odca.user_id', @actor, true)`; parâmetro `actorId` sempre declarado nos `DynamicParameters` (quirk Npgsql/Dapper registrado); `ExtendAsync` converte o escalar timestamptz com TZ UTC explícito |
| `src/Odca.Infrastructure/DependencyInjection.cs` | **F-2**: registro do repositório |
| `src/Odca.Infrastructure/Database/DatabaseSchema.cs` | `CurrentVersion` 59 → **60** |
| `src/Odca.Web/Services/RecentAuthGuard.cs` (novo) | **F-1**: guard da Web espelhando a regra da política sobre os claims do token (token inválido nunca exige desafio) |
| `src/Odca.Web/Controllers/AccountController.cs` | **F-1**: escrita bloqueada por `RecentMfa` redireciona para `/mfa/desafio?returnUrl=` preservando o destino |
| `src/Odca.Web/Controllers/SupportSessionsController.cs` (novo) | **F-2**: BFF pt-first — Index + ações abrir/estender/encerrar/revogar (anti-forgery, validação leve espelhando a API, `TempData` de feedback, redirect de reautenticação em stale) |
| `src/Odca.Web/Controllers/OrganizationsController.cs` | **F-2**: `GetCustomersAsync` no cliente BFF (lista de organizações para o seletor do formulário) |
| `src/Odca.Web/Models/SupportSessionViewModels.cs` (novo) | **F-2**: modelos bindable + view model da página |
| `src/Odca.Web/Navigation/NavigationRegistry.cs` | item `suporte-sessoes` na navegação de plataforma |
| `src/Odca.Web/Services/OdcaApiClient.cs` | **F-2**: métodos da client BFF para os endpoints de sessão de suporte |
| `src/Odca.Web/Views/SupportSessions/Index.cshtml` (novo) | página pt-BR: formulário de abertura, tabela de sessões recentes com ações, alertas de `TempData` (sucesso/erro) |
| `src/Odca.Api/appsettings.json`, `src/Odca.Web/appsettings.json` | **F-1**: `Identity:RecentAuthMinutes: 15` explícito nos dois lados |
| `src/Odca.Worker/DocumentWorker.cs` | **A-1**: purga física de `reconciled/` por idade de escrita (chave `Documents:ReconciledPurgeRetentionDays`, padrão 30) + `SelectReconciledPurgeCandidates` estático testável |
| `tests/Odca.Domain.Tests/RecentMfaAndSupportSessionTests.cs` (novo) | **F-1/A-1** · 22 testes: `RecentMfaHandlerTests` (8: não-superadmin, isenção de troca forçada, isenção pré-inscrição, sem verificação falha, fresco passa, limite da janela passa, obsoleto falha com código estável, menor janela configurável respeitada), `JwtTokenServiceRecentAuthClaimsTests` (2), `RecentAuthGuardTests` (6 fatos + 1 teoria c/ 4 casos de token inválido), `DocumentWorkerPurgeSelectionTests` (2) |
| `tests/Odca.Domain.Tests/Odca.Domain.Tests.csproj` | referência a `Odca.Worker` (testabilidade da seleção de purga) |
| `tests/Odca.IntegrationTests/SupportSessionEndpointTests.cs` (novo) | **F-2/F-1** · 8 fatos: open auditado com ator real; segundo ativo conflita (409) e reabre após encerramento; extend soma minutos com evento/audit sem reescrever histórico (+ invalid 400 + extend de expirada 404); end do dono/revoke de qualquer admin terminais; expirada reportada inválida e não estendível; usuário comum bloqueado pelo **guard SQL** mesmo sem camada de política; stale `RecentMfa` bloqueia escrita enquanto sessão fresca abre via HTTP; usuário comum 403 no HTTP (inclui seed com MFA inscrito/confirmado e membro com membership ativa) |
| `tests/Odca.IntegrationTests/MigrationChecksumTests.cs` | fato `ReleaseV060AddsAuditedSupportSessionExtensionWithoutRewritingEarlierMigrations` (checksums 1–59 imutáveis + assert das duas funções e grants do bloco 60) |
| `database/odca.sql` + `database/releases/odca-v060.sql` | bloco 060 canônico + snapshot byte-idêntico |
| `scripts/qa/validate-d39-s5-web.ps1` (novo) | prova ao vivo (A-2) — um comando, base descartável, dados sintéticos |
| `scripts/qa/capture-d39-screens.ps1` (novo) | capturas CDP da página de suporte (A-2) |

## 4. Prova ao vivo (A-2)

- Script reproduzível: **`scripts/qa/validate-d39-s5-web.ps1`** — base descartável `odca_test_disposable` (aplica o bloco v060 se ausente), seed idem-potente do superadministrador sintético `suporte.prova@odca.local` (hash clonado do operador fixture; estado MFA/sessões/audits limpo a cada rodada; sem segredos novos — senha sintética declarada nos scripts da casa), api `:5143` + web `:5144` sobidos pelo próprio script com **`Identity__RecentAuthMinutes=1`** (janela determinística para validar o stale em ~70 s em vez de 15 min).
- Fluxo executado: login API + inscrição/confirmação MFA com chave manual determinística (TOTP local) → login web com **desafio MFA obrigatório** (redirecionamento `/mfa/desafio` + resolução com retry de timestep por anti-replay) → página pt-BR carregando com nav de plataforma → ciclo **abrir → estender → encerrar** na janela fresca (feedback de `TempData` na página + estado no banco: linha ativa, eventos, auditorias com ator real; estender avança `expires_at` ~15 min sem retroceder) → envelhecimento de 70 s → escrita stale na Web → **302 para `/mfa/desafio?returnUrl=%2Fadministracao%2Fsuporte-sessoes%2Fabrir`** → desafio resolvido → reenvio com sucesso → escrita stale na API → **403 puro** (e leitura continua 200, pois somente as escritas exigem MFA recente).
- **Rodada final fails=0 — 22/22 checks executados** (transcript `evidence/d39-proof.txt`; o script define 23 checks e o pré-requisito P0a-aplicação roda apenas em base que ainda não tem a v060 — na rodada final a base já estava migrada). Convergência registrada: **fails 2 → 0**.
- **Classificação das falhas da rodada anterior (zero falha de produto)**: asserção de feedback conferindo o corpo do POST 302 (curl sem `-L` — o alerta `TempData` só aparece no GET seguinte; asserção movida para a página pós-redirect) e `[Net.WebUtility]::UrlUnescape` inexistente no PowerShell 5.1 (substituído por `[System.Uri]::UnescapeDataString`; erro era não-fatal). Ambas de harness, corrigidas no script antes da rodada final.

## 5. Capturas em navegador (CDP)

- `scripts/qa/capture-d39-screens.ps1` + driver `scripts/qa/cdp-capture-v2.mjs`: **1 tela** (área de sessões de suporte com uma janela ativa de 60 min montada de forma determinística pelo próprio script — superadmin sintético com MFA reinscrita de chave conhecida) × **pt-BR** × 360/768/1280/1440px + zoom 200% = **5 PNG** (transcript `evidence/d39-captures.txt`).
- PNGs em `%TEMP%\opencode\evidence\captures-d39\`, fora do repositório por tamanho (convenção das capturas anteriores); disponíveis para revisão humana.
- **Limitação registrada**: a área é **pt-first** (dívida i18n do §1) — capturas somente em pt-BR até a tradução da superfície; asserções negativas de idioma aplicadas nas demais superfícies (S2–S4) voltam quando a área entrar no catálogo.

## 6. Separação dos tipos de evidência

- **Teste local**: builds estritos, Domain 389/389, assets, suíte `-FreshDb` 231/231 (§2).
- **Prova ao vivo**: §4 (script reproduzível em um comando, fails=0).
- **Prova em navegador**: §5 (capturas para revisão humana).
- **CI oficial**: **não verificada** — branch sem push oficial; nenhum ID de execução inventado (vale também para os SHAs das S1–S4).
- **Aceite humano**: pendente — nenhum responsável ou aprovação inventada.
- **Portal**: `.odca/progress.json` **inalterado** (baseline 40%, IDs/pesos imutáveis; crédito de marco exige CI oficial do SHA funcional + aceite real pertinentes).

## 7. Riscos e próximo passo

- Bloqueio externo registrado: fornecedor de envio externo indefinido (decisão "proposto, não escolhido" preservada — nenhuma integração simulada como concluída); SMTP real fora de escopo.
- Dívidas registradas: impersonação profunda dentro da janela de suporte (fronteira honesta do F-2); i18n da área de sessões de suporte (pt-first); fila admin de atendimento, privacidade, conteúdo das notificações e `aria-label`s da navegação global (carreados das S3–S4) — itens pendentes no BACKLOG.
- CI oficial deste SHA: pendente (push quando solicitado). Aceite humano: pendente (capturas disponíveis).
- Sprint 6 (piloto/design/matriz de prontidão) inicia-se sobre este commit.
