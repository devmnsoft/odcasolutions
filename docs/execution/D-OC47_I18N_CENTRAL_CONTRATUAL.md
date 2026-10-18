# Sprint 13 — central contratual do cliente em 4 idiomas: renovações + revisões + ações (D-OC47) (18/10/2026)

Entrega a localização completa da última área grande do cliente ainda pt-first medida por heurística: a **central contratual** em 4 idiomas (pt-BR/en-US/es-ES/fr-FR) — **renovações** (lista + detalhe da proposta), **revisões** (fila de solicitações + detalhe) e **ações** (central consolidada) — nas views e nas mensagens dos controllers BFF que as sustentam, com auditoria de design ao vivo estendida às três novas rotas.

## 1. Escopo e base

- **Base**: `codex/s00-foundation` @ `fd1b42d` (Sprint 12 / D-OC46).
- **Sem nova migração**: canônico v062 intacto (`DatabaseSchema.CurrentVersion = 62`); sem mudanças em API/Domain/Worker/assets/SQL.
- **235 chaves i18n × 4 culturas** via delta versionado `scripts/i18n/d-s13-contratos.json` (namespaces `ren.*` ~104, `rev.*` ~77, `actions.*` 13, `common.*` +21) + atualização da chave existente `ren.how_note` (separação em prefixo + corpo para preservar o `<strong>` do original) → catálogos em **2981 chaves/cultura, paridade 4/4**.
- **Áreas já localizadas e intactas**: Centro de Obrigações/Agenda (0 hits na heurística) e `Contracts\Sheet.cshtml` (linhas restantes são só comentários). Dívida restante (controllers Accounts/Customers/Organizations/Imports/Onboarding/Modules/Inbox/Consumption e áreas operacionais pt-first) registrada em §6.
- **Política de chaves**: status neutros compartilhados em `common.status_*` (`status_in_review`, `status_internally_approved`); status com gênero ficam por área (`ren.st_cancelled`="Cancelado" vs `rev.st_cancelled`="Cancelada"); `ren.st_not_renewing` ("Não renovar") serve a filtros/cards e `ren.st_cancelled` ao detalhe; ~30 chaves `common.*` existentes reutilizadas (situation, responsible, contract, due_date, next_action, priority, reason, history, select, prev/next, paginação, how_to_use, obligation, scope via `inbox.scope`, breadcrumb).

## 2. F-1 — 5 views localizadas (padrão canônico da casa)

Padrão idêntico ao das S7/S8: `_c=(string?)Context.Items[CultureMiddleware.ItemsKey]` + `L.T(key,_c,args)` (helper `L` já injetado no `_ViewImports`).

- `Renewals\Index.cshtml`: header, cards de resumo, lembretes (labels + copy com os 3 dias), painéis de filtro (período, tipo, contraparte, situação, prioridade, meus/sem responsável), linhas (status/kind/prioridade, datas, próximos passos), confirm de aplicação, paginação, guia "como usar".
- `Renewals\Detail.cshtml`: cabeçalho (eyebrow/subtítulo/back), dados da proposta (maps `StLabel`/`AppLabel`/`PrioLabel`/`EvLabel`/kind), tabela vigência atual × proposta, formalização registrada, ações condicionais (enviar/cancelar/formalizar/aplicar) com confirms, linha do tempo de eventos.
- `Reviews\Index.cshtml`: breadcrumb, header, help (split antes/**negrito**depois para preservar o `<strong>`), filtros (situação/escopo/responsável/busca/período), contagem, empty/error state, tabela (6 colunas) e paginação.
- `Reviews\Detail.cshtml`: resumo (responsável/próxima ação/versão com nota de escopo), bloco aprovado-internamente (próxima etapa), mensagens e notas (visibilidade), histórico, reatribuição, decisão (solicitar ajustes/aprovar) e cancelamento — tudo em catálogo.
- `Actions\Index.cshtml`: eyebrow, aria do resumo, filtros (escopo/fonte/aplicar/limpar), sourceLabel por tipo, prazo, ação, responsável inline e paginação.
- **Datas e moeda (invariante D-OC22)**: `.ToString("dd/MM/yyyy"/"MM/dd/yyyy")` fixos saem das UIs — `CulturedTime.Date/DateTime(value,_c)` (ordem da data por língua ativa, 24h; fallback pt-BR); valores monetários `ToString("N2", CultureInfo.GetCultureInfo(_c ?? "pt-BR"))`. O formato canônico dd/MM/yyyy do **documento** (PDF/papel) permanece intacto.
- **Melhoria registrada**: o badge de status em `Reviews\Detail` exibia o código cru (`@Model.Status`); agora exibe o label localizado — a classe CSS `status-@Model.Status` permanece inalterada.

## 3. F-1 — 3 controllers BFF em 4 idiomas

Injeção canônica de `LocalizationService` (padrão S8: propriedade `Culture` do `CultureMiddleware` + helper privado `L(key, args)`):

| Controller | Strings localizadas | Observações |
| --- | --- | --- |
| `RenewalsController` | 17 | ModelState de período, not-found, conflitos 409, sucessos/falhas de submit/cancel/formalize/reminders/apply; `msg_applied_until` recebe a data já formatada culturalmente |
| `ReviewsController` | 9 | `ViewData["Title"]` ×2 + load error + mensagens de decisão/cancelamento/atribuição/mensagem |
| `ActionsController` | 2 | `ViewData["Title"]` (`actions.title` reutilizado) + falha de carga |

## 4. F-2 — auditoria de design estendida (d41)

`scripts/qa/audit-d41-design.ps1`: tabela de rotas 17 → **20** (+`renovacoes`, +`solicitacoes`, +`acoes`, sessão cliente, ×4 idiomas) — 63 pares rota×idioma × 4 viewports (360/768/1280/1440) = **252 medidas** (era 51 pares / 204). Comentários e asserts atualizados junto. Execução ao vivo sobre o banco descartável: **`AUDIT-DONE checks=252 ok=252 overflow=0 authfail=0 httpfail=0` (AUDIT-EXIT=0)**; transcript commitado `evidence/d41-design-audit-s13-rodada-final.txt`; 0 PNGs de falha.

## 5. Evidência

### GATE sobre a árvore final (executado localmente)

| Etapa | Comando | Resultado |
| --- | --- | --- |
| Build Debug estrito | `dotnet build Odca.sln -c Debug -warnaserror` | **0W/0E** |
| Build Release estrito | `dotnet build Odca.sln -c Release -warnaserror` | **0W/0E** |
| Domain | `dotnet test tests/Odca.Domain.Tests` | **389/389** |
| Integração em DB nova | `scripts/qa/run-integration-tests.ps1 -FreshDb` | **236/236, TEST-EXIT=0** (baseline S12 preservado; nenhum teste novo) |
| Assets/i18n | `node scripts/check-assets.mjs` | **exit=0** — 10 assets, 19 chaves do conjunto d41, 4 catálogos (**2981 chaves/cultura, paridade 4/4**) |
| Paridade delta↔catálogo | script efêmero (removido antes do commit) | 1210 chaves de todos os deltas × 4 = 4840 comparações, **0 divergências** |
| Auditoria de design d41 | `scripts/qa/audit-d41-design.ps1` | **252/252 · overflow=0 · authfail=0 · httpfail=0 · exit=0** (transcript §4) |
| SMTP / backup | — | **não reexecutados de propósito**: sem diff em transporte, outbox, worker nem scripts desses domínios (rodadas S12 seguem válidas) |

### Verificação byte-a-byte dos valores i18n (lição S12 aplicada)

Delta escrito 100% ASCII com `\uXXXX` para todo não-ascii. Amostras decodificadas conferidas por bytes no catálogo `pt-BR.json`: "Substituída" termina `…75 69 c3 ad 64 61` (í = U+00ED), "Renovações e prazos" contém `c3 a7 c3 b5` (ç õ), "Nenhuma renovação é automática." contém `c3 a7 c3 a3 … c3 a9 … c3 a1` (ç ã é á).

### Classificação QA (não é do produto)

O diff byte-a-byte do delta capturou, **antes** do GATE: (1) typo de escape "Substitída"/"Substitïda" → "Substituída"; (2) "Alterção" → "Alteração" (3 ocorrências, letra "a" suprimida); (3) ES "vencimiento e inicio" → "y inicio" (eufonia); (4) FR concordância "Message enregistrée" → "enregistré"; (5) EN maiúscula indevida "; The" → "; the". E o confronto com o código-fonte original revelou **6 chaves ausentes** adicionadas ao delta (e re-mergiadas): `ren.st_awaiting_formalization`, `ren.cards_aria`, `ren.how_note_prefix`, `ren.msg_cancelled_ok`, `ren.msg_reminders_saved`, `ren.msg_record_changed`.

### Confirmação de preservação estrutural

Dif de esqueleto (sequência tag+nomes de atributos, versão HEAD × nova) nos 5 views: **0 remoções / 0 adições** em todos. Varredura de caracteres não-ascii residual nos 8 arquivos: somente separadores neutros originais (`·`, `—`, `→`, `⚠`) — nenhum literal de UI pt remanescente.

## 6. Cobertura honesta e dívidas mantidas

- **Coberto**: as 3 superfícies de lista (rotas d41, 4 idiomas, 4 viewports — overflow/auth/HTTP medidos ao vivo) + todas as strings das 5 views e 3 controllers 100% em catálogo (zero literais de UI verificado por varredura). Estrutura das views comprovadamente inalterada (§5).
- **Não coberto pela rota fixa da auditoria**: as páginas de **detalhe** (renovação individual / revisão individual) não têm rota fixa na d41 (dependem de id de registro) — cobertas por compilação estrita, varredura de literals e dif de esqueleto; risco residual avaliado baixo. Aceite humano das 4 línguas pendente (como S7/S8/S12).
- **Dívida i18n pt-first restante** (fora de escopo decidido desta sprint): controllers `Accounts`, `Customers`, `Organizations`, `Imports`, `Onboarding`, `Modules`, `Inbox`, `Consumption` e áreas operacionais pt-first (registros D-OC42/43).
- **Bloqueios externos mantidos (registrados, nenhuma inventada)**: CI oficial nunca verde publicada (branch sem push) → `.odca/progress.json` permanece **40%**; provedor de assinatura, canal SMTP real, usuários convidados + aprovação da versão, política de sessão 15 min e retenção de backups seguem abertos (D-OC45 §4). Nenhum marco credenciável nesta rodada.

## 7. Separação de evidências e portal

- **Teste local**: GATE §5 + execução d41 ao vivo + transcript versionado + amostras byte-a-byte.
- **CI oficial**: **não verificada** — sem push nesta rodada; nenhum ID de execução inventado.
- **Aceite humano**: pendente (4 idiomas da central contratual + itens D-OC45).
- **Portal / `.odca/progress.json`**: **inalterado** — baseline 40%; IDs e pesos intactos; sem CI oficial verde do SHA nem aceite real, nenhum marco sai de `pending`.
