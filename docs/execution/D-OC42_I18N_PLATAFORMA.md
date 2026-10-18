# Sprint 8 — superfícies de plataforma/operacional em 4 idiomas (D-OC42) (18/10/2026)

Plano pós-S7: **S8** leva a mesma operação i18n (catálogos + `L.T`/`L()`) às superfícies restantes carregadas das S3–S7: a **ficha do contrato detalhada** (cliente) e as áreas **pt-first de plataforma** (fila administrativa de atendimento, sessões de suporte, central de notificações da plataforma) + a **agenda mensal**. Nenhum invariante canônico é tocado — só apresentação (views, controllers Web, JS inline e catálogos).

## 1. Escopo e base

- **Base**: `codex/s00-foundation` @ `68a13b3` (Sprint 7 / D-OC41). Sem mudança de esquema nesta sprint (checksum v060 intacto); crescimento dos catálogos por deltas versionados + mesclador determinístico (`scripts/i18n/merge-i18n-delta.mjs`), padrão estabelecido na S7.
- **F-1 (ficha do contrato)**: `Contracts/Sheet.cshtml` (~1730 linhas — jornada contínua, próxima ação, pendências, renovações/aditivos, revisões, comentários, assinatura, obrigações, minutas sugeridas, documentos/derivados, solicitações, histórico com drawer JS) + `Contracts/Print.cshtml` (watermark/lang/barra de impressão) + `Contracts/Index.cshtml` (lista "Meus contratos") + mensagens remanescentes de `ContractsController` (BFF). Namespace: extensão de `sheet.*` (existente desde D-OC37) + reuso agressivo de `common.*`, `sol.*`, `obl.*`, `documents.*`, `inbox.*`, `studio.*` e chaves soltas (`meus-contratos`, `ficha-contrato`, `novo-contrato`, `agenda`). Deltas versionados: `scripts/i18n/d-s8-contracts.json`, `d-s8-contracts-ctl.json`, `d-s8-contracts-index.json`.
- **F-2 (fila administrativa de atendimento)**: `AdminSolicitacoes/Index.cshtml`, `Aprovacoes.cshtml`, `Detalhes.cshtml` + `AdminSolicitacoesController`. Namespace novo `admin.*`.
- **F-3 (sessões de suporte da plataforma)**: `SupportSessions/Index.cshtml` + `SupportSessionsController`. Namespace novo `support.*`.
- **F-4 (central de notificações da plataforma + agenda mensal)**: `PlatformNotifications/Index.cshtml` + controller e `Agenda/Index.cshtml` + `AgendaController` (o botão "Agenda mensal" da central de obrigações segue disabled nesta sprint — limitação registrada; a área Agenda já existente é localizada). Namespace novo `platform.*` + reuso de `common.*`.
- **Limitações registradas**: (a) o *conteúdo* das notificações do sino (título/corpo persistidos em pt-BR) permanece dívida — esta sprint localiza a *superfície* (rótulos, filtros, colunas), não o conteúdo armazenado; (b) botão "Agenda mensal" na central de obrigações continua disabled (habilitação funcional fora do escopo); (c) `ModulesController` (cards de módulos) fica para a próxima sprint.
- Sem push; commit único ao final.

## 2. GATE sobre a árvore final

Executado sobre a árvore final da sprint (todos os arquivos da seção 3):

| Verificação | Resultado |
|---|---|
| `dotnet build Odca.sln -c Release -warnaserror` | **0W/0E**, exit=0 |
| `dotnet build Odca.sln -c Debug -warnaserror` | **0W/0E**, exit=0 |
| `dotnet test tests/Odca.Domain.Tests -c Release` | **389/389 aprovados** (0 falhas) |
| Suíte de integração `scripts/qa/run-integration-tests.ps1 -FreshDb` (`odca_test_integration`) | FRESH-DB-OK → BUILD-OK → **231/231**, TEST-EXIT=0 |
| `node scripts/check-assets.mjs` | exit=0 — 10 assets web, 19 chaves i18n em 4 catálogos, round-trips decimal/template |
| Auditoria de design estendida `scripts/qa/audit-d41-design.ps1` | seção 4 (base ampliada nesta sprint) |
| Varredura de literais pt (caracteres acentuados) nas superfícies S8 | zero literal de interface remanescente fora de comentários e separadores neutros (`— – · ← → … ✕`) em `Sheet`, `Print`, `Contracts/Index`, `AdminSolicitacoes/*`, `SupportSessions/Index`, `PlatformNotifications/Index`, `Agenda/Index` e nos 5 controllers tocados |

Catálogos: **2708 chaves/cultura, paridade 4/4** (baseline fim-S7 2161 → **+547**: deltas versionados d-s8-admin 131, d-s8-support 38, d-s8-platform 61, d-s8-contracts 239, d-s8-contracts-ctl 59, d-s8-contracts-index 18, mais 1 correção direta pela chave `obligacoes` ausente e usada em `Obligations/Acoes.cshtml` — bug detectado durante a verificação S7/S8). Mesclador idempotente revalidou paridade 4/4 em todas as corridas; nenhuma chave foi duplicada ou sobrescrita.

## 3. Alterações

| Arquivo | Mudança |
|---|---|
| `src/Odca.Configuration/Localization/{pt-BR,en-US,es-ES,fr-FR}.json` | crescimento 2161 → 2708 chaves/cultura via deltas versionados (paridade 4/4 validada pelo mesclador e por `check-assets`) |
| `scripts/i18n/d-s8-admin.json` (novo, 131), `d-s8-support.json` (novo, 38), `d-s8-platform.json` (novo, 61), `d-s8-contracts.json` (novo, 239), `d-s8-contracts-ctl.json` (novo, 59), `d-s8-contracts-index.json` (novo, 18) | deltas desta sprint — registro determinístico das traduções por namespace |
| `scripts/i18n/merge-i18n-delta.mjs` | generalizado para `d-sN-*.json` (antes apenas `d-s7-*`) e documentado como idempotente (chaves já presentes são puladas) |
| `Views/Contracts/Sheet.cshtml` | i18n integral da ficha: `@functions` helpers localizados (`StatusLabel`, `EventLabel`, novos `ObligationUrgency`/`ObligationStatus`/`SolStatus`/`SolService`/`SolPriority`/`DocState`) lendo `_culture` do middleware; chips, badges, empty states, formulários (proposta/revisão/comentários/obrigações/minutas), documentos/derivados/quarentena, solicitações, histórico, ações de ciclo (duplicar/arquivar/restaurar/encerrar com confirms) e `bag` `drawerI18n` para o JS inline (`toLocaleString(culture)` no lugar de `'pt-BR'`) |
| `Views/Contracts/Print.cshtml` | `lang` do documento via cultura do middleware, watermarks assinado/rascunho, barra de versão e botão de impressão |
| `Views/Contracts/Index.cshtml` | lista "Meus contratos": título/descrição, filtro (pesquisa/estado/botões), cabeçalhos e `data-label` das colunas, rótulos de vigência/contraparte, switch de situação de revisão (reuso `documents.review_*`/`stage_*`), paginação, empty state |
| `Controllers/ContractsController.cs` | ~60 mensagens BFF para `L("sheet.ct_*")` (padrão canônico já presente desde D-OC37); `OfficialTemplateNames` (Dictionary c/ nomes pt) substituído por `HashSet<string> OfficialTemplateKeys` — resolução exclusivamente por KEY, títulos vêm do catálogo do tenant; `ViewData["Title"]` via chaves existentes |
| `Controllers/AdminSolicitacoesController.cs`, `SupportSessionsController.cs`, `PlatformNotificationsController.cs`, `AgendaController.cs` | injeção de `LocalizationService` no primary constructor + `Culture`/`L()` canônicos e localização das mensagens BFF |
| `Views/AdminSolicitacoes/{Index,Aprovacoes,Detalhes}.cshtml` | fila administrativa, aprovações e detalhe do protocolo em 4 idiomas (`admin.*`) |
| `Views/SupportSessions/Index.cshtml` | sessões de suporte em 4 idiomas (`support.*`) |
| `Views/PlatformNotifications/Index.cshtml` | central de notificações da plataforma em 4 idiomas (`platform.*`) |
| `Views/Agenda/Index.cshtml` | agenda mensal em 4 idiomas + link "← Agenda" |
| `scripts/qa/audit-d41-design.ps1` | base ampliada: 13 → 17 rotas (+contratos, +ficha do contrato com contrato fixo da prova `76000000-…-000000000201`, +agenda em 4 idiomas cliente; +notificações da plataforma pt-BR superadmin); 152 → 204 medidas previstas |
| `docs/execution/{STATUS,BACKLOG}.md` | incremento S8 registrado |

Sem mudança de esquema (checksum v060 intacto) e sem mudança de comportamento: apenas apresentação. `Odca.Infrastructure` intocado — decisão registrada: os labels pt hardcoded em `ContractSheetRepository.StatusLabel/ServiceLabel` (L287-288) não foram alterados; a view mapeia `sol.Status/ServiceType/Priority` pelos próprios helpers localizados. Valores aceitos em URL (ex.: filtro `status=arquivados|encerrados|todos` em `ContractsController.List`) foram mantidos como constantes de entrada, não como texto de interface.

## 4. Prova ao vivo e auditoria de design

`scripts/qa/audit-d41-design.ps1` (navegador real Edge/CDP, logins reais com fixture sintética — cliente sem MFA e superadmin da prova com MFA reinscrita por chave conhecida) sobre a stack própria no banco descartável `odca_test_disposable`, **base ampliada nesta sprint**: 13 → 17 rotas (+`contratos` em 4 idiomas, +`ficha-contrato` em 4 idiomas com o contrato fixo da prova `76000000-0000-0000-0000-000000000201` "Contrato Prova D-OC37", +`agenda` em 4 idiomas, +`plataforma-notificacoes` pt-BR) e 152 → 204 medidas.

**Resultado: `AUDIT-DONE checks=204 ok=204 overflow=0 authfail=0 httpfail=0`, AUDIT-EXIT=0**, 0 PNGs de falha; um `RELOGIN` de sessão expirada em `contratos/pt-BR` foi recuperado automaticamente pelo driver (não é falha de produto). Nenhuma correção de produto foi necessária nesta rodada.

Transcript versionado: `evidence/d41-design-audit-s8-rodada-final.txt`. Limitação declarada: a auditoria mede overflow horizontal (360/768/1280/1440) e autenticção/HTTP por rota×idioma — não é diff visual pixel a pixel entre idiomas.

## 5. Separação de evidências e portal

- **Testes locais (esta árvore, sem push)**: builds estritos Release+Debug 0W/0E; Domain 389/389; suíte `-FreshDb` 231/231 TEST-EXIT=0; `check-assets` exit=0; auditoria de design de seção 4; varreduras de literais pt das superfícies (seção 2). Nada disso executa fora da máquina local.
- **CI oficial**: **não verificada** — nenhum push autorizado para este SHA; a CI oficial do portal ainda nunca publicou verde. Por regra do acompanhamento rastreável, isto não credencia marco algum.
- **Aceite humano**: não houve aceite humano dos 4 idiomas nas superfícies desta sprint (nem jurídica, que exige fornecedor de assinatura — proposto, não escolhido). Mantém-se pendente o aceite real pertinente.
- **Portal `.odca/progress.json`**: mantido em **40%**. Nenhum dos seis marcos se tornou inteiramente concluído nesta sprint; baseline, IDs e pesos imutáveis não foram alterados.
- **Dados**: apenas fixtures sintéticos (tenant demo, contas sintéticas de teste e contrato de prova do D-OC37); sem credenciais ou dados pessoais nestes relatórios.
