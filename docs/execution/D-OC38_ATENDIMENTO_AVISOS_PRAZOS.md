# Sprint 4 — atendimento, avisos e prazos: sino in-app com deduplicação, i18n do atendimento e dos avisos, centro de obrigações e prova ao vivo fails=0 (D-OC38) (18/10/2026)

Plano de seis sprints para candidata ao piloto (estreia 11/01/2027). Esta entrega fecha a Sprint 4.

## 1. Escopo e base

- **Base**: `codex/s00-foundation` @ `fe483da` (Sprint 3 / D-OC37). Migração nova neste incremento: **v059** (deduplicação determinística por origem nas notificações in-app — única mudança de contrato de dados).
- **F-1 (correção)**: notificação in-app ("sino") por evento de negócio com **deduplicação determinística por origem** — migração v059 (`user_notifications.source_id` + índice único parcial `(tenant_id,source_id) WHERE source_id IS NOT NULL`) e `NotificationEnqueue.EnqueueInAppAsync` **que nunca lança** (falha vira log EventId 2403/2404; `ON CONFLICT DO NOTHING`), guard de membership ativo no destinatário e `DeterministicSource` compartilhado com o canal de e-mail (mesmo evento = mesmo `source_id`). Oito pontos de chamada: `solicitation_opened` (→ responsável da plataforma), `solicitation_updated` (→ autor), `review_assigned` ×2 (→ revisor), `review_decided` (→ solicitante), `signature_pending` ×2 (envio + lembrete → participantes vinculados e ainda não assinantes), `signature_completed` (→ criador da preparação). Canais por tipo: in-app habilitado por padrão, e-mail desabilitado (padrão pré-existente preservado).
- **F-2 (correção)**: i18n em 4 idiomas (pt-BR/en-US/es-ES/fr-FR) do **atendimento** (central de solicitações: lista, nova, detalhe) e dos **avisos** (notificações + preferências) — **143 chaves novas por cultura** (`sol.*` ~95, `notif.*` ~48), total **1765 chaves/cultura, paridade 4/4**; cinco views reescritas no padrão canônico (`Culture` via `CultureMiddleware` + `L.T(key,_culture)`), controladores injetam `LocalizationService`; BOM dos catálogos preservado, sem nova linha final. **Dívida registrada**: o *conteúdo* das notificações (título/corpo armazenados) segue pt-BR — somente o chrome da superfície é localizado neste incremento.
- **F-3 (triagem — NÃO-defeito)**: suspeita "duplas notificações na triagem pela varredura de workers concorrentes da fila da plataforma (incluída a triagem de solicitações)". Encerrada como **não-defeito**, com evidência executável nova (`OutboxLeaseClaimTests`, 2 fatos verdes): `odca.claim_notification` é atômica (`UPDATE … WHERE id = (SELECT … FOR UPDATE SKIP LOCKED LIMIT 1)`), só elegge linhas `pending` ou `leased` com **lease expirado** e `available_at<=now()`, e cova novo `lease_token` por claim; `odca.complete_notification` só aplica com o token exato (conclusão de worker antigo = no-op); a linha volta a `pending` apenas com backoff `available_at`. O sino (F-1) tem dedup independente por `source_id`. Nenhuma mudança de produto atribuída ao item.
- **A-1 (avanço)**: centro de obrigações acessível no Web BFF — cadastro (única ou **série mensal** 1–120 ocorrências, parte financiada/recorrência XOR, evidência exigida, pós-vigência), leitura individual via API (`GET …/obligations/{id}`, nova rota), e **ações por status com versão otimista** (start/fulfill/cancel/reopen/reschedule/reassign) — um formulário por operação aplicável; no conflito **409** o BFF rele a versão fresca, **preserva os valores digitados** (motivo/nota/prazo/responsável/evidência) e atualiza a `expectedVersion`. Validação leve no BFF espelha a API; **a API continua autoridade** (permissão por operação, validação, versão, auditoria em `obligation_events`). Superfície **pt-first** (dívida i18n registrada, mesma política das superfícies operacionais anteriores à S2).
- **A-2 (avanço)**: prova ao vivo reproduzível `scripts/qa/validate-d38-s4-web.ps1` (31 checks definidos; rodada final fails=0) + capturas CDP de 6 telas × 4 idiomas (120 PNGs).
- **Defeito de produto descoberto e corrigido na prova**: a página de ações (e o re-render do 409) caía em `InvalidOperationException` (503 "Página indisponível") porque `View(vm)` usa a convenção de nome da action (`Actions`/`PerformAction`) e o arquivo da view é `Acoes.cshtml` — corrigido com `View("Acoes", vm)` explícito nos dois pontos; a rodada final da prova passa com o conflito 409 preservando valores (§4).
- **Fora de escopo registrado**: fila administrativa de atendimento da plataforma permanece pt-first; área de privacidade e sessão de suporte (MFA de sessão); série de obrigações "esta e futuras"; métricas de dashboard; localização do conteúdo das notificações (acima); SMTP real (e-mail segue desabilitado por padrão); botão "Agenda mensal" permanece disabled.

## 2. GATE

Execução sobre a árvore final (após a correção do defeito de view do §1 e após os scripts da prova já versionados):

| Etapa | Resultado |
|---|---|
| Build Release estrito (`-warnaserror`) | exit=0 · **0 avisos / 0 erros** |
| Build Debug estrito (`-warnaserror`) | exit=0 · **0 avisos / 0 erros** |
| `Odca.Domain.Tests` | **367/367 PASS** (baseline 364 + 3 fatos do cliente de obrigações) |
| `scripts/check-assets.mjs` | exit=0 (assets JS + chaves i18n dos módulos × 4 catálogos) |
| Suíte de integração `-FreshDb` | **222/222 PASS** (FRESH-DB-OK → BUILD-OK → TEST-EXIT=0) = baseline S3 211 + 5 fatos do sino (F-1) + 3 fatos dos endpoints de obrigação (A-1) + 2 fatos do lease da outbox (F-3) + 1 fato de checksum da v059 |

Comandos reproduzíveis (orquestrador local `%TEMP%\opencode\d37-gate.ps1`, reaproveitado): `dotnet build .\Odca.sln -c Release -warnaserror`; `dotnet build .\Odca.sln -c Debug -warnaserror`; `dotnet test .\tests\Odca.Domain.Tests -c Debug --no-build`; `node .\scripts\check-assets.mjs`; `scripts\qa\run-integration-tests.ps1 -FreshDb`.

## 3. Mudanças

### Migração v059

- Checksum **`d742ce207f2618466726eeca7cefc7a5124d75069200a34fd9289462be1ca45e`**; `CurrentVersion=59`; release `database/releases/odca-v059.sql` byte-idêntica ao canônico (`database/odca.sql`).
- Conteúdo: coluna `source_id uuid` (NULL) em `odca.user_notifications` + índice único parcial `user_notifications_source_uq ON (tenant_id,source_id) WHERE source_id IS NOT NULL` — mesmo padrão do `notification_outbox.source_id` (v050). Linhas históricas (worker de lembretes) permanecem com `source_id NULL` e continuam protegidas pela máquina de leases; sem backfill.

### Catálogos

- **+143 chaves por cultura** em `src/Odca.Configuration/Localization/{pt-BR,en-US,fr-FR,es-ES}.json` (namespaces `sol.*` e `notif.*`) — total **1765 chaves/cultura**, paridade exata 4/4 (confere no GATE/check-assets). Textos puros do help-card via `sol.how_body_a/b` + `sol.how_paused`; BOM preservado, sem nova linha final.

### Código

| Arquivo | Mudança |
|---|---|
| `src/Odca.Api/NotificationEnqueue.cs` | **F-1**: `EnqueueInAppAsync` (overloads com/sem transação do chamador; nunca lança — EventIds 2403/2404; guard de membership ativo; `DeterministicSource` compartilhado com o e-mail; `ON CONFLICT (tenant_id,source_id) DO NOTHING`) |
| `src/Odca.Api/Controllers/SolicitationsController.cs` | F-1: pontos de chamada `solicitation_opened`/`solicitation_updated` dentro da transação do fluxo |
| `src/Odca.Api/Controllers/ReviewRequestsController.cs` | F-1: `review_assigned` (atribuição) / `review_decided` (decisão) |
| `src/Odca.Api/Controllers/ContractStudioController.cs` | F-1: `signature_pending` (envio + lembrete, destinatários = participantes vinculados não assinantes) / `signature_completed` |
| `src/Odca.Contracts/Obligations/ObligationContracts.cs` | **A-1**: `ObligationItem` ganha `bool EvidenceRequired` (fim do registro posicional) |
| `src/Odca.Api/Controllers/ObligationsController.cs` | **A-1**: `GET {id}` individual (read ou read_all/dono) expondo `evidenceRequired`; lista expõe `o.evidence_required`; registro privado `ObligationRow` no padrão da casa |
| `src/Odca.Web/Services/OdcaApiClient.Operations.cs` | **A-1**: `GetObligationAsync` + `PerformObligationActionDetailedAsync` (retorna status + `title`/`code`/`currentVersion` do 409) |
| `src/Odca.Web/Controllers/ObligationsController.cs` | **A-1**: `Nova`/`Create` (validação leve espelhando a API; recorrência mensal), `DocumentOptions` (JSON para não perder campos digitados), `Actions`/`PerformAction` (form por operação aplicável ao status; 409 → re-read + `RebuildActionsViewAsync` preservando valores), helpers de catálogo/reconstrução; **correção §1**: `View("Acoes", vm)` explícito |
| `src/Odca.Web/Models/ObligationFormModels.cs` (novo) | modelos bindable de criação/ação + opções (contrato/membro/documento) |
| `src/Odca.Web/Views/Obligations/Index.cshtml` | **A-1**: botão "Nova obrigação" habilitado (link), link "Ações" por linha, notices via `TempData` |
| `src/Odca.Web/Views/Obligations/Nova.cshtml` (novo) | formulário de cadastro (contrato ativo, responsável, prazo, categoria/prioridade, recorrência mensal com toggle, evidência, pós-vigência; fetch de documentos ao trocar contrato) |
| `src/Odca.Web/Views/Obligations/Acoes.cshtml` (novo) | um form por operação aplicável ao status; `expectedVersion` em hidden; aviso de conflito com valores preservados |
| `src/Odca.Web/Controllers/SolicitacoesController.cs`, `AccountController.cs` | **F-2**: injeção de `LocalizationService` + padrão `Culture`/`L()` |
| `src/Odca.Web/Views/Solicitacoes/{Index,Nova,Detalhes}.cshtml`, `Views/Account/{Notifications,NotificationPreferences}.cshtml` | **F-2**: reescritas pelo catálogo (`sol.*`/`notif.*`) |
| `tests/Odca.IntegrationTests/NotificationFeedTests.cs` (novo) | **F-1** · 5 fatos: sino exato por evento; replay do ponto de chamada não duplica (dedup por `source_id`); destinatário sem membership ativo não recebe linha; falha de enfileiramento não quebra o fluxo de negócio; e-mail segue respeitando o canal desabilitado |
| `tests/Odca.IntegrationTests/ObligationEndpointTests.cs` (novo) | **A-1** · 3 fatos: lista expõe `evidenceRequired`; GET individual (200 dono / 200 read_all / 403 estranho / 404 inexistente); ação com versão obsoleta → 409 `obligation.version.conflict` + `currentVersion` |
| `tests/Odca.IntegrationTests/OutboxLeaseClaimTests.cs` (novo) | **F-3** · 2 fatos: claims concorrentes entregam a linha a exatamente um worker (atomicidade `SKIP LOCKED`); lease expirado é re-claimado e conclusão com token antigo é no-op (linha vai a `sent` uma única vez) |
| `tests/Odca.IntegrationTests/MigrationChecksumTests.cs` | fato de checksum da v059 (ReleaseV059 + blocos ≤58 imutáveis) |
| `tests/Odca.Domain.Tests/OdcaApiClientTests.cs` | **+3 fatos** do cliente: rota do GET individual + parse de `EvidenceRequired`; 409 mapeado com `currentVersion`; sucesso sem payload |
| `database/odca.sql` + `database/releases/odca-v059.sql` | bloco 059 canônico + snapshot byte-idêntico |
| `scripts/qa/validate-d38-s4-web.ps1` (novo) | prova ao vivo (A-2) — um comando, base descartável, dados sintéticos |
| `scripts/qa/capture-d38-screens.ps1` (novo) | capturas CDP das 6 telas da sprint (A-2) |

## 4. Prova ao vivo (A-2)

- Script reproduzível: **`scripts/qa/validate-d38-s4-web.ps1`** — base descartável `odca_test_disposable` (aplica o bloco v059 se ausente), seed determinístico com GUIDs fixos no tenant demo (contrato `Contrato Prova D-OC38`, preparação confirmada, participante vinculado ao membro do cliente, papel sintético `prova-obligacoes-d38` com as permissões de obrigações — fixture declarada, sem segredos), api `:5143` + web `:5144`; worker ausente (varredura coberta pelos fatos de integração). Fase do sino com `Signatures__Provider=local-file` via env (transporte de referência da S3). Sessão `cliente.teste@odca.local` (sem MFA).
- **Rodada final fails=0 — 30/30 checks executados** (transcript `evidence/d38-proof.txt`; o script define 31 checks e o pré-requisito P0a é executado apenas em base que ainda não tem a v059 — na rodada final a base já estava migrada); convergência registrada em `evidence/d38-proof-fim.txt`: **fails 10 → 2 → 0**. Conteúdo da rodada final:
  - pré-condições determinísticas (binários, purga idem-potente, seed válido `p=1 part=1 role≥1`);
  - **F-2** central de solicitações nos **4 idiomas** (positivos completos + negativos anti-vazamento pt-BR), página de notificações nos **4 idiomas** (idem), formulário de nova solicitação pt/en (spot-check do texto real do catálogo);
  - **F-1 sino** B1–B5: envio do envelope → 200 `sent`; **exatamente 1 linha** `signature_pending` para o cliente vinculado e outbox de e-mail vazia (canal desabilitado por padrão); replay → `replayed=true` **sem duplicar**; página em pt-BR exibe título+corpo do evento; em en-US o chrome fica traduzido e o conteúdo segue pt-BR (dívida registrada);
  - **A-1 obrigações** O1–O8: botão Nova habilitado (placeholder antigo ausente); formulário com contrato ativo listado; POST cria **série mensal de 3 prazos** (15/nov–15/jan) + 3 eventos `created`; API GET individual → 200 `evidenceRequired=false`/`status=open`; página de ações com `expectedVersion=1`; ação `start` → 302 + `in_progress|v2`; **conflito 409** (cancel com `expectedVersion=1` obsoleta) → página 200 com aviso, **motivo preservado no formulário**, versão atualizada para 2, status intocado; resubmit `cancel` v2 → 302 + `cancelled|v3` + trilha `start`/`cancel` na auditoria.
- **Classificação das falhas das rodadas intermediárias**:
  1. **Defeito de produto (único)**: `View(vm)` por convenção de action vs. arquivo `Acoes.cshtml` → 503 na página de ações e no re-render do conflito (log: "The view 'Actions'/'PerformAction' was not found") — corrigido com nome explícito (§1) e coberto pelos checks O5–O8 da rodada final.
  2. **Harness**: literais acentuadas corrompidas pela leitura ANSI de `.ps1` sem BOM no PowerShell 5.1 (assíncrona dos checks pt-BR) — BOM adicionado ao script; asserções ajustadas ao texto real dos catálogos (`Descreva a demanda…`, rótulo `Serviço` sem espaço final); estado idem-potente do seed (limpeza de `role_permissions`); rótulo de tipo inexistente no feed de notificações (o feed exibe título+corpo; o rótulo vive no cartão de preferências).

## 5. Capturas em navegador (CDP)

- `scripts/qa/capture-d38-screens.ps1` + driver `scripts/qa/cdp-capture-v2.mjs`: **6 telas** (central de solicitações, nova solicitação, notificações com o aviso do sino, centro de obrigações, nova obrigação, ações da obrigação em `cancelled` → form de reabertura) × **4 idiomas** × 360/768/1280/1440px + zoom 200% @1280 = **`CAPTURES-DONE ok=120 fail=0`** (transcript `evidence/d38-captures.txt`).
- PNGs em `%TEMP%\opencode\evidence\captures-d38\`, fora do repositório por tamanho (convenção das capturas anteriores); disponíveis para revisão humana. Sessão cliente (sem MFA); estado determinístico montado pelo próprio script (cadeia de assinatura, papel de obrigações e obrigação de demonstração idem-potente).

## 6. Separação dos tipos de evidência

- **Teste local**: builds estritos, Domain 367/367, assets, suíte `-FreshDb` 222/222 (§2).
- **Prova ao vivo**: §4 (script reproduzível em um comando, fails=0).
- **Prova em navegador**: §5 (120 capturas para revisão).
- **CI oficial**: **não verificada** — branch sem push oficial; nenhum ID de execução inventado (vale também para os SHAs das S1–S3).
- **Aceite humano**: pendente — nenhum responsável ou aprovação inventada.
- **Portal**: `.odca/progress.json` **inalterado** (baseline 40%, IDs/pesos imutáveis; crédito de marco exige CI oficial do SHA funcional + aceite real pertinentes).

## 7. Riscos e próximo passo

- Bloqueio externo registrado: fornecedor de envio externo indefinido (decisão "proposto, não escolhido" preservada — nenhuma integração simulada como concluída); SMTP real fora de escopo.
- Dívidas i18n registradas: fila admin de atendimento, área de privacidade, sessão de suporte, conteúdo das notificações, `aria-label` da navegação global (pré-S3) e o restante da área Documentos (carreado da S2) — itens pendentes no BACKLOG.
- CI oficial deste SHA: pendente (push quando solicitado). Aceite humano: pendente (capturas disponíveis).
- Sprint 5 inicia-se sobre este commit.
