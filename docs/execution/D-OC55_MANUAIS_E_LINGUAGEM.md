# D-OC55 — Manual em todas as páginas + linguagem para leigos (Sprint 21)

Data: 20/10/2026 · Base: `codex/s00-foundation` @ `1d3b72f` (Sprint 20 / D-OC54) · Entregável da Sprint 21 do roadmap (`docs/execution/SPRINT-ROADMAP-AI.md`).

## 1. Escopo

1. Inventário de rotas SEM painel de ajuda/manual ("Como usar") → cada uma ganha manual contextual em 4 idiomas (chaves `*.manual_*`).
2. Revisão de todo texto da UI para linguagem simples e direta (compreensível por leigos): jargão técnico substituído por termos claros com o mesmo significado operacional.
3. Revisar mensagens de erro/status: **causa + efeito + próxima ação**, sempre em tom de produção.
4. Zero conotação de homologação/teste/dev nas mensagens varridas (scan contínuo no GATE).

Regras desta sprint: nenhuma classe CSS nova (reuso dos padrões existentes `details.help-card` / `.help-panel` / `aside.help-card` / `.compact-guide`); nenhuma chave fora do padrão `*.manual_*` para os manuais (o sumário reusa a chave existente `common.how_to_use`); sem mudança de API/Domain/Worker/SQL; páginas internas sem rota fixa de auditoria seguem a limitação já registrada nas sprints anteriores (cobertas por build estrito + suíte + scanner).

## 2. Inventário de rotas da auditoria (49) × painel de ajuda

Padrões aceitos como "manual presente": `common.how_to_use` em `details/summary`, seção `id=como-usar`, `.help-card`, `.help-panel`, `.compact-guide` ou equivalente fixo na página.

| Rota (d41) | View | Status pré-S21 | Ação |
|---|---|---|---|
| home | `Home/Index` | tem (`dash.howto`) | revisão linguagem |
| documentos | `Documents/Index` | tem (`documents.how_intro`) | revisão linguagem |
| documentos-novo | `Documents/NewDocument` | tem | revisão linguagem |
| estudio | `Studio/Index` | tem (`studio.cat_help_*`) | — |
| pacientes | `Patients/Index` | tem (`patient.help_body`) | revisão linguagem |
| central-solicitacoes | `Inbox/Index` | tem (`inbox.help_body*`) | revisão linguagem |
| obrigações | `Obligations/Index` | tem (`obl.how_1..4`) | — |
| obrigações-nova | `Obligations/Nova` | **SEM** | novo manual |
| notificações | `Account/Notifications` | **SEM** | novo manual |
| privacidade | `Privacy/Index` | **SEM** (legal) | bloco curto "como ler esta página" |
| atendimento-odca | `PlatformOperations/Index` | tem | revisão linguagem |
| suporte-sessoes | `SupportSessions/Index` | tem (`support.help_*`) | — |
| plataforma-notificacoes | `PlatformNotifications/Index` | tem (`platform.notif_help_*`) | revisão linguagem (jargão de filas) |
| auditoria | `PlatformAudit/Index` | tem | — |
| biblioteca | `PlatformLibrary/Index` | tem (`platform.lib_help_body`) | — |
| biblioteca-nova | `PlatformLibrary/Create` | **SEM** | novo manual |
| biblioteca-revisao | `PlatformLibrary/NewRevision` | **SEM** | novo manual |
| sla-configuracoes | `PlatformOperations` (action SLA) | tem (mesma view, tab) | — |
| diagnostico | `PlatformDiagnostics/Index` | tem intro, **SEM painel** | novo manual (como ler o diagnóstico) |
| modulo-* (6) | `Modules/Index` | tem | — |
| contratos | `Contracts/Index` | **SEM** | novo manual |
| ficha-contrato | `Contracts/Sheet` | **SEM** (só tips) | novo manual (estados/vigência/assinatura) |
| agenda | `Agenda/Index` | **SEM** | novo manual |
| renovacoes | `Renewals/Index` | tem (`ren.how_1..5`) | — |
| solicitacoes | `Solicitacoes/Index` | tem | — |
| acoes | `Actions/Index` | **SEM** | novo manual |
| org-picker | `Organizations/Index` | tem | — |
| equipe | `Organizations/Team` | tem | — |
| editar-org | `Organizations/Edit` | **SEM** | novo manual |
| visao-geral-cliente | `Onboarding/CustomerHome` | **SEM** | novo manual curto (ponteiro pro primeiro-passo) |
| primeiros-passos | `Onboarding/GettingStarted` | tem | — |
| aceitar-convite | `Organizations/Accept` | **SEM** (funil único) | parágrafo curto explicativo |
| contratar | `Onboarding/Register` (+plan) | **SEM** | novo manual curto (passos do cadastro) |
| confirmar-email | `Onboarding/ConfirmEmail` | **SEM** (funil único) | parágrafo curto explicativo |
| clientes | `Customers/Index` | tem | — |
| clientes-novo | `Customers/Create` | tem | — |
| clientes-usuarios | `Customers/Users` | **SEM** | novo manual |
| cliente-detalhe | `Customers/Details` | tem (`cust.help_detail`) | — |
| planos | `Plans/Index` | tem | — |
| plano-consumo | `Consumption/Index` | tem (`cons.help_1..4`) | — |
| caixa | `Inbox/Index` (rota de escopo do InboxController) | tem | — |
| login | `Account/Login` | **SEM** | dica curta (primeiro acesso/senha/MFA) |
| minha-conta | `Account/Conta` | tem | — |
| importacao | `Imports/Index` | tem | revisão linguagem |

Isentos com justificação registrada: `Shared/Error`, `Shared/ServiceUnavailable`, `Account/AccessDenied`, `Shared/AccessDenied` (páginas de estado, não de tarefa — mensagem já segue causa+efeito+ação na revisão de erros); `_Layout`/`_Toasts`/`_ConfirmDialog`/`_I18nBag`/partials (não são rotas); rotas internas de estúdio/ficha (`Studio/TemplateEdit`, `Studio/CompareVersions`, `Studio/TemplatePreview`, `Studio/Version`, `Impersonation/Cliente`, `Patient/Form`, detalhes de solicitação) não têm rota fixa na d41 — mesmíssima limitação declarada desde a S17 (cobertas por build estrito + suíte + scanner de chaves).

## 3. Implementação

**3.1 Manuais contextuais (objetivo 1)** — 17 pages sem painel de ajuda ganharam `<details class="help-card"><summary>@L.T("common.how_to_use")</summary>` com passos em 4 idiomas; nenhuma classe CSS nova (reuso do padrão existente do design system):

| Page | Chaves | Âncora |
|---|---|---|
| `Obligations/Nova` | `obl.manual_1..3` | após o header |
| `Account/Notifications` | `notif.manual_1..3` | após o header |
| `Privacy/Index` | `priv.manual_body` | após o intro |
| `PlatformLibrary/Create` | `platform.tplnew_manual_1..3` | após o header |
| `PlatformLibrary/NewRevision` | `platform.revnew_manual_1..4` | após o header |
| `PlatformDiagnostics/Index` | `platform.diag_manual_1..3` | antes da tabela |
| `Contracts/Index` | `contractlist.manual_1..3` | após o header |
| `Contracts/Sheet` | `sheet.manual_1..4` | após a subnav |
| `Agenda/Index` | `agenda.manual_1..3` | após os filtros |
| `Actions/Index` | `actions.manual_1..3` | após o header |
| `Organizations/Edit` | `org.manual_1..3` | após o header |
| `Onboarding/CustomerHome` | `onb.manual_1..3` | após o cabeçalho do painel |
| `Organizations/Accept` | `org.accept_manual_1..2` | após o h1 |
| `Onboarding/Register` | `reg.manual_1..3` | após a nota |
| `Onboarding/ConfirmEmail` | `conf.manual_body` | após o h1 |
| `Customers/Users` | `cust.manual_1..3` | após o header |
| `Account/Login` | `acct.login_manual_body` | após o sub do card |

**46 chaves novas × 4 idiomas = 184 entradas** no delta versionado `scripts/i18n/d-s21-manuais.json` → catálogos **4058→4104**, paridade 4/4. Tom: linguagem simples, cada passo diz o que fazer e o que acontece depois; fatos conferidos contra os textos existentes (`tpl_create_info_*`, `rev_effect_*`) para não divergir do produto.

**3.2 Linguagem para leigos (objetivos 2 e 4)** — varredura sistemática dos 4 catálogos por termo técnico e por conotação teste/dev/homologação, com revisão individual de cada acerto. Reescritas aplicadas (**59 valores — diff final por cultura: pt-BR 22, en-US 12, es-ES 12, fr-FR 13; a 23ª linha do diff de pt-BR é só vírgula obrigatória na antiga última chave após o acréscimo — + 1 chave nova**):

- **Conotação dev/teste removida**: `reg.dev_transport` ("Transporte local de desenvolvimento…concluir o teste" → "Nesta instalação, os e-mails de confirmação são entregues localmente. Use o código abaixo…"), `sheet.doc_simulated` ("ambiente de teste" → "provedor externo não configurado") + o tooltip `title="Engine: simulated-scanner-dev"` hardcoded em `Contracts/Sheet.cshtml` virou a chave nova `sheet.doc_simulated_engine` (delta `d-s21-linguagem.json`), `error.development_mode` ("Modo desenvolvimento" → "Ambiente de preparação — detalhes técnicos exibidos").
- **Jargão de infraestrutura**: `platform.notif_help_b1/b2` ("aguardando o worker", "reservado por um worker com lease de 2 minutos" → "na fila de processamento", "em processamento por uma estação de envio (reserva de 2 minutos)").
- **Jargão criptográfico**: `documents.new_how_generation_body` ("hash criptográfico SHA-256" → "impressão digital criptográfica (SHA-256) que comprova sua integridade").
- **Snapshot → instantâneo** em pt-BR (8 chaves: `studio.ver_prep_help`, `studio.ver_ptype_patient`, `studio.ver_ptype_representative`, `patient.deactivate_note`, `api.studio.patient_snapshot_mismatch`, `api.studio.representative_snapshot_mismatch`, `api.studio.snapshot_without_patient` + `platform.rev_effect_b`); en/es/fr mantêm seus termos nativos (snapshot/retrato/instantané).
- **"Chave de idempotência" → "identificador da requisição"** nas 4 mensagens onde o usuário enxerga o termo sem o produto nomear esse conceito por idempotência (`api.org.idempotency_conflict[.detail]`, `api.solicitations.open_invalid`).
- **Acentuação de 2 valores crus fora da família canônica**: `api.internal.no_result`, `api.solicitations.operation_invalid`.
- **Correção na ferramenta**: `merge-i18n-delta.mjs` deixava vírgula final inválida quando não havia chave nova para adicionar (não era idempotente nesse caso) — guard acrescentado; agora termina com `+0 chaves` sem tocar nos catálogos.

Catálogo final: **4105 chaves/cultura, paridade 4/4**, BOM+LF preservados.

**Decisão registrada (invariante D-OC27 descoberta pela suíte)**: a família `api.sql.*` em pt-BR deve ser **byte-exata** aos envelopes `jsonb_build_object('error', …)` de `database/odca.sql` (a camada SQL é canônica em pt-BR; os textos chegam sem diacríticos por construção). A primeira bateria da sprint acusou deriva exatamente aí (`EverySqlErrorEnvelopeMapsToACatalogKeyInAllFourCatalogs`); os 27 valores `api.sql.*` editados nesta sprint foram **restaurados ao texto canônico exato** (transcrito do mapa `ApiSqlMessages.cs`), enquanto en/es/fr receberam acentuação/termos corretos livres da restrição. Tornar esses textos pt com diacríticos exige mudança de envelope SQL (nova migração) — dívida registrada, fora do escopo da S21.

## 4. QA executado

1. **Varredura de jargão**: scan automatizado (24 termos) nos 4 catálogos — contagens brutas pós-reescrita: pt-BR 53+6, en-US 95+3, es-ES 28+8, fr-FR 14+4 acertos; cada um revisado individualmente: os restantes são termos corretos ou falsos positivos — `hash` exibido junto do próprio valor hex, `fila/queue/fila` (comum em todos os idiomas), `sonda` (comum em pt/es), `retry` (comum em en), `lease` = tipo legítimo de contrato (ou substring de *release*), `token` coerente com o label visível "Token" (dívida `[Display(Name)]` herdada), `cron` = substring de *síncrono*, `artificial` = substring de *inteligência artificial*. Nenhum termo técnico ficou sem tratamento ou justificativa.
2. **Mensagens de erro/status (causa + efeito + próxima ação)**: amostragem determinística (1 em 7) das 481 chaves de erro/status do catálogo + revisão integral das famílias `api.sql.*` (37) e `api.org.solicitations.*`; padrão já consistente de sprints anteriores confirmado; corrigidas as pontas encontradas nesta varredura (item 3.2) e a acentuação dos 2 valores fora da família canônica.
3. **Scan de conotação teste/dev/homologação** (GATE contínuo): 3 casos reais encontrados e corrigidos (item 3.2); o restante revisado e justificado — `error.dev_p1/p2` são os parágrafos padrão do .NET que só aparecem em instâncias com `ASPNETCORE_ENVIRONMENT=Development` (referenciam o identificador real do ambiente); `api.studio.reminder_local_only` descreve estado honesto de configuração (canal externo ausente); `cons.sub` "sem cobrança simulada" significa "sem cobrança efetivada" (sentido revisado, mantido).
4. **Rodadas de auditoria pós-mudança** (texto novo pode estourar overflow): d54 UX **verde na primeira volta** — `UX-DONE checks=382 ok=382 uxfail=0 authfail=0 httpfail=0 overflowTotal=0 unlabeledTotal=0 emptyNoCtaTotal=0 imgsNoAltTotal=0 statusElTotal=112` (exit 0), pior contraste medido 4.7:1 — o painel de ajuda novo (`details.help-card` fechado por padrão) não gerou overflow nem falha de contraste em nenhuma das 49 rotas × 4 idiomas × 2 viewports; transcript `evidence/d54-ux-s21-rodada-final.txt`. d41 design: a r1 foi interrompida por suspensão da máquina ao fim de 628/764 medidas (**0 falhas até a interrupção**; transcript preservado em `evidence/d41-design-audit-s21-r1-interrupcao-ambiental.txt`) — as passadas completas seguintes morreram no mesmo evento de ambiente à ~60 min da medição (r2 em 635/764 e r3 em 617/764; ambas com **0 falhas** e sem marcador de saída — morte da árvore de processos por evento de sessão; o evento das 08:07 derrubou também conexões MariaDB de outra aplicação da máquina, corroborando origem fora do pipeline). Como a passada única completa (~73 min) excede a janela do ambiente, a rodada final foi executada em **2 partes dentro da janela**, cobrindo 100% das medidas: parte 1 = prefixo da r3 (**617 medidas** do início da lista; transcript `evidence/d41-design-audit-s21-rodada-final-part1-r3-prefixo.txt`) e parte 2 = rotas restantes clientes→importacao (`AUDIT-DONE checks=160 ok=160 overflow=0 authfail=0 httpfail=0`, exit 0, com 13 medidas de sobreposição de segurança na primeira rota; transcript `evidence/d41-design-audit-s21-rodada-final-part2-tail.txt`). A união cobre as **764 medidas únicas da árvore S21 com 0 falhas** — somando 2.040 medições reais nas 4 rodadas (628+635+617+160), nenhuma falha em nenhuma.

## 5. Limitações registradas

- Rotas internas de estúdio/ficha sem rota fixa na auditoria seguem a limitação herdada (S17 em diante).
- O *conteúdo* armazenado em pt-BR (ex.: motivo de histórico, corpo de notificação gravada) continua dívida de dados, não de superfície — revisada a superfície.

## 6. GATE da entrega

| Item | Resultado |
|---|---|
| Build sln Debug+Release (`-warnaserror`, 13 projetos) | 0 avisos / 0 erros nos dois modos (bateria final, após a restauração da família `api.sql.*`) |
| Suíte Domain | 389/389 aprovados |
| Suíte Integration (`run-integration-tests.ps1 -FreshDb`) | 293/293 aprovados, TEST-EXIT=0 (inclui `EverySqlErrorEnvelopeMapsToACatalogKeyInAllFourCatalogs` byte-exato) |
| Scanner de chaves (`scan-i18n-keys.mjs`) | KEYUSE-OK: 2746 chaves usadas em 92 arquivos, 0 ausentes nos 4 catálogos; `check-assets.mjs` verde (10 assets, 19 chaves, round trips ok) |
| Catálogos | 4105 chaves/cultura, paridade 4/4, BOM+LF preservados |
| Scan de conotação teste/dev/homologação | 3 casos reais corrigidos; restante revisado individualmente e justificado (item 4.3) |
| d54 UX (382 medidas) pós-textos | VERDE `UX-DONE checks=382 ok=382 uxfail=0 authfail=0 httpfail=0 overflowTotal=0 unlabeledTotal=0 emptyNoCtaTotal=0 imgsNoAltTotal=0 statusElTotal=112` (exit 0), pior contraste 4.7:1 — transcript `evidence/d54-ux-s21-rodada-final.txt` |
| d41 (764 medidas) pós-textos | **VERDE** — rodada final em 2 partes por janela de ambiente (~60 min): 617 (prefixo r3) + 160 (tail c/ 13 de sobreposição) = 764 únicas / 0 falhas — ver QA item 4 |
