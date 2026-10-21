# Status de execução

## Incremento: Sprint 22 — dress rehearsal final da candidata sobre a árvore atual + veredito go/no-go atualizado + pacote do dia 1 (D-OC56) (21/10/2026)

- **Base**: `codex/s00-foundation` @ `abf2da7` (Sprint 21 / D-OC55). Sem nova migração (v063 intacto). Mudança de produto somente na `Odca.Worker` (corrigindo a regressão S19 pega no ensaio). GATE sobre a árvore final: builds Release+Debug estritos (**sln `-warnaserror`, 13 projetos**) **0W/0E**, Domain **389/389**, suíte `-FreshDb` **295/295 TEST-EXIT=0** (baseline 293 + 2 novos guardas de regressão), assets **exit=0** (10 ativos / round trips), scanner **KEYUSE-OK 0 ausentes** (2746 chaves / 92 arquivos). Commit único ao final, **sem push**.
- **F-1 (dress rehearsal sobre a árvore S21)**: primeira reexecução completa da cadeia desde S11 — a candidata nunca tinha sido re-ensaiada sobre a própria árvore após as mudanças das S12–S21. Rodada r1 (foto de 8 estágios, ~27 min): 6 verdes (**C-SLA 98/0**, **ACEITE 15/0/0**, **D37 fails=0**, **D38 fails=0**, **S10-BACKUP 26/26**, **S10-SMTP 17/17**) + 2 falhas classificadas: **(a) regressão real de produto** — D34-JORNADA com 8/61 checks vermelhos porque a Sprint 19 (`1f94693`) estendeu o record `VersionItem` de 4→6 parâmetros sem atualizar a projeção do claim de scan (`ScanOne`), e **toda varredura documental lançava exceção Dapper em runtime** (log preservado `evidence/d56-d34-r1-worker-erro.txt`; mesma classe de bug que a própria S19 corrigiu no `RenewalListItem`, o primo do worker passou pelas auditorias porque suíte/d41/d54 não executam scans); **(b) deriva QA** — D39-SEGURANCA com 1 check falho (script QA congelado em v061; canônico v063; comportamento até W1b 100% verde). Correções: construtor secundário de 4 parâmetros ancorando a projeção do claim + **teste comportamental** (claim real `odca.claim_document_scan()` com linha semeadas pela role da aplicação, mesmo SQL/tipo de produção) + **guard de fonte**; asserção do script QA atualizada para v063. Rodada r2 (`-Only 'D34-JORNADA;D39-SEGURANCA'`): **ambos verdes** (D34 **61/0** `TODOS OS CHECKS PASSARAM` em 773 s; D39 **fails=0** em 113 s — validou pela primeira vez nesta árvore W2–W5, F1a–F1c e F2/F2b: página/ciclo de sessões de suporte, stale→redirect MFA com returnUrl, 403 puro na API) → **dress rehearsal consolidado 8/8 estágios verdes na árvore final** (transcripts r1/r2 separados em `evidence/d56-*`).
- **F-2 (veredito go/no-go atualizado)**: **NO-GO formal — capacidade técnica de GO reforçada sobre a S11**: as únicas pendências para o GO seguem sendo decisões externas não simuladas (push + CI oficial do SHA, transporte de assinatura, canal de aviso, usuários convidados + cenários, aprovação explícita da versão); tudo que depende do repositório está comprovado localmente nesta árvore, incluindo a captura+correção da única regressão real introduzida por S12–S21. Itens ordenados "para virar GO" no D-OC56 §4.
- **F-3 (pacote do dia 1)**: checklist versionada no D-OC56 §5 — pré-condições antes do convite (6 itens com estado), ordem exata do dia 1 (backup pré-piloto c/ restore validado como pré-condição de rollback), cenários por perfil (cliente / superadministrador / titular de dados) e suporte/monitoramento/rollback/saída, todos ancorados em provisioners/scripts versionados desta árvore; adendo datado anexado ao D-OC40 corrigindo referências defasadas (§7.5 v060→v063; contagens da época da S6 marcadas como snapshot; dívida i18n já resolvida) sem reescrever o histórico.
- **Limitações mantidas**: CI oficial nunca verde publicada (sem push); provedor real de IA pendente (`none`); provedores de assinatura/SMTP pendentes como decisão; sessão absoluta de 15 min sem sliding pendente como decisão de produto; dívida de impersonação profunda (D-OC39); rotas internas sem rota fixa na d41; `api.sql.*` pt-BR sem diacríticos (dívida de migração). Detalhamento no D-OC56.
- **Portal**: `.odca/progress.json` inalterado (40%, baseline/IDs/pesos intactos). Sem push → CI oficial não verificada; aceite humano pendente.

## Incremento: Sprint 21 — manuais contextuais em todas as páginas + linguagem para leigos (D-OC55) (20/10/2026)

- **Base**: `codex/s00-foundation` @ `1d3b72f` (Sprint 20 / D-OC54). Sem nova migração (v063 intacto) e sem mudança em API/Domain/Worker/SQL. GATE sobre a árvore final: builds Release+Debug estritos (**sln `-warnaserror`, 13 projetos**) **0W/0E**, Domain **389/389**, suíte `-FreshDb` **293/293 TEST-EXIT=0**, assets **exit=0**; catálogos **4105 chaves/cultura, paridade 4/4** (+47×4: 46 `*.manual_*` via delta versionado `scripts/i18n/d-s21-manuais.json` + 1 `sheet.doc_simulated_engine` via `scripts/i18n/d-s21-linguagem.json`); scanner permanente **KEYUSE-OK 0 ausentes** (2746 chaves / 92 arquivos); codepoints **0 problemas** (16.420 valores). Commit único ao final, **sem push**.
- **F-1 (manuais contextuais)**: as 17 páginas da auditoria (inventário de 49 rotas d41 × painel de ajuda — D-OC55 §2) que estavam SEM "Como usar" ganharam `<details class="help-card">` com o sumário reusando a chave canônica `common.how_to_use` e passos em 4 idiomas (46 chaves `*.manual_*`) — nenhuma classe CSS nova (reuso do padrão existente `details.help-card`). Páginas de funil/estado receberam parágrafo curto explicativo (privacidade, aceitar-convite, confirmar-email, login); isenções registradas no D-OC55 §2 (páginas de estado, partials, rotas internas sem rota fixa — limitação herdada da S17).
- **F-2 (linguagem para leigos)**: varredura sistemática dos 4 catálogos por jargão técnico e por conotação teste/dev/homologação, com revisão individual de cada acerto — **59 valores reescritos** (pt-BR 22, en-US 12, es-ES 12, fr-FR 13) + 1 chave nova: conotação dev/teste removida (`reg.dev_transport`, `sheet.doc_simulated`, `error.development_mode`; o tooltip hardcoded `title="Engine: simulated-scanner-dev"` da ficha de contrato virou a chave de catálogo `sheet.doc_simulated_engine`), "worker/lease" → "fila de processamento/estação de envio", "hash criptográfico SHA-256" → "impressão digital criptográfica (SHA-256) que comprova sua integridade", snapshot→instantâneo em pt-BR (8 chaves; en/es/fr mantêm snapshot/retrato/instantané), "chave de idempotência" → "identificador da requisição" (4 mensagens) e acentuação de 2 valores crus. Os demais acertos do scan foram justificados individualmente (D-OC55 §4 item 1). Mensagens de erro/status revisadas quanto a causa+efeito+próxima ação (amostragem determinística 1-em-7 das 481 chaves + revisão integral das famílias `api.sql.*` e solicitações).
- **QA — classificação**: a 1ª bateria do GATE acusou deriva real — a reescrita de acentuação da família `api.sql.*` em pt-BR violava a invariante D-OC27 (camada SQL é canônica em pt-BR: valores byte-exatos dos envelopes, imposta por `EverySqlErrorEnvelopeMapsToACatalogKeyInAllFourCatalogs`); os 27 valores afetados foram **restaurados ao texto canônico exato** (canonizar com diacríticos exige nova migração — dívida registrada, fora do escopo); en/es/fr, livres da restrição, receberam termos e acentuação corretos. Efeito colateral positivo: `merge-i18n-delta.mjs` passou a ser idempotente no caso "0 chaves novas" (antes deixava vírgula final inválida).
- **Auditorias pós-textos**: d54 UX (382 medidas) **verde na primeira volta** — `UX-DONE checks=382 ok=382 uxfail=0 authfail=0 httpfail=0 overflowTotal=0 unlabeledTotal=0 emptyNoCtaTotal=0 imgsNoAltTotal=0 statusElTotal=112` (exit 0), pior contraste 4.7:1; os 17 painéis de ajuda novos (fechados por padrão) não geraram overflow nem falha de contraste em nenhuma rota — transcript `evidence/d54-ux-s21-rodada-final.txt`; d41 design (764 medidas): a 1ª volta foi interrompida por suspensão da máquina em 628/764 (0 falhas até aí; transcript preservado `evidence/d41-design-audit-s21-r1-interrupcao-ambiental.txt`) — as passadas completas seguintes morreram no mesmo evento de ambiente à ~60 min da medição (r2 em 635/764 e r3 em 617/764, ambas com 0 falhas e sem marcador de saída — morte da árvore de processos); a passada única completa (~73 min) excede a janela do ambiente, então a volta final foi executada em 2 partes dentro da janela cobrindo 100% das medidas: prefixo da r3 (**617 medidas** do início da lista; transcript `evidence/d41-design-audit-s21-rodada-final-part1-r3-prefixo.txt`) + rodízio das rotas restantes clientes→importacao (`AUDIT-DONE checks=160 ok=160 overflow=0 authfail=0 httpfail=0`, exit 0, com 13 medidas de sobreposição de segurança na primeira rota; transcript `evidence/d41-design-audit-s21-rodada-final-part2-tail.txt`) — união = **764 medidas únicas verdes na árvore S21, 0 falhas** (2.040 medições reais nas 4 rodadas, nenhuma falha em nenhuma).
- **Limitações mantidas**: rotas internas de estúdio/ficha sem rota fixa na d41 (limitação herdada da S17); família `api.sql.*` sem diacríticos em pt-BR por construção da camada canônica (dívida de migração registrada); conteúdo armazenado em pt-BR é dívida de dados, não de superfície; provedor real de IA pendente (`none`); CI oficial nunca verde publicada; aceite humano pendente.
- **Portal**: `.odca/progress.json` inalterado (40%, baseline/IDs/pesos intactos). Sem push → CI oficial não verificada; aceite humano pendente.

## Incremento: Sprint 20 — QA de design + polimento de UX: dívida CSS, auditoria UX d54 e d41 pós-CSS (D-OC54) (20/10/2026)

- **Base**: `codex/s00-foundation` @ `1f94693` (Sprint 19 / D-OC53). Sem nova migração (v063 intacto) e sem mudança em API/Domain/Worker/SQL. GATE sobre a árvore final: builds Release+Debug estritos (**sln `-warnaserror`, 13 projetos**) **0W/0E**, Domain **389/389**, suíte `-FreshDb` **293/293 TEST-EXIT=0**, assets **exit=0**; catálogos **4058 chaves/cultura, paridade 4/4** (+6×4 via delta versionado `scripts/i18n/d-s20-ux-audit.json`); scanner permanente de chaves **KEYUSE-OK 0 ausentes** (2699 chaves / 92 arquivos); codepoints **0 problemas** (16.232 valores). Commit único ao final, **sem push**.
- **F-1 (dívida CSS zerada)**: inventário exato e reprodutível (`inventory-css-classes.mjs` + snapshot versionado): dos **47 nomes reais** usados, **0 sem definição**. 58 linhas anexadas a `site.css` (chaves 752/752): as 46 classes do inventário + **2 classes runtime que o escaneador estático não captura** (`.comment` gerada por `studio.js`; `.sec-scan_failed` do CHECK `security_status`); `field-search` já existia no fim do arquivo (redefinir sobrescreveria) e `form-actions` foi resolvido por reuso de `form-actions-row` no view — sem classe nova além das necessárias.
- **F-2 (auditoria UX nova d54)**: driver próprio (`cdp-ux-audit.mjs` + `audit-d54-ux.ps1`) medindo 49 rotas × 4 idiomas × (360×780, 1280×800) = **382 medidas**: contraste WCAG AA por elemento com fundo composto alfa subindo a árvore (gradiente ⇒ branco opaco), rótulo acessível visível em todo field, tabela vazia exige estado vazio com CTA (4 níveis de ancestor), presença de `[role=status]`, overflow `sw>iw` e imagem sem alt. **4 rodadas**: r1 abortada por bug no próprio probe (driver, não produto — transcript preservado); r2 `ok=48 uxfail=334` → 5 causas raiz corrigidas (`.button-quiet` branco-sobre-branco 812 falhas, `.menu-caption` em tom navy sobre topbar branca 334, vermelho `#ef4444` inline 32, verde com fallback claro 8, 56 fields sem rótulo associável); r3 `uxfail=342` → colisão de superfície da mesma classe entre sidebar navy e topbar branco (1754 falhas), **6 chaves `layout.*` ausentes dos 4 catálogos renderizadas como texto cru no sidebar — bug de produto pré-S20, o usuário via "LAYOUT.ORG_ACTIVE"** — e botão publicar branco sobre `#059669`; **r4 final VERDE: `UX-DONE checks=382 ok=382 uxfail=0 overflowTotal=0 unlabeledTotal=0 emptyNoCtaTotal=0 imgsNoAltTotal=0`, pior contraste medido 4.7:1 (exit 0)** — transcript `evidence/d54-ux-s20-rodada-final.txt`.
- **F-3 (regressão visual d41 pós-CSS)**: 1ª volta `checks=764 ok=760 overflow=4` — o `white-space:nowrap` novo (classe antes indefinida) estourou a coluna Motivo de `suporte-sessoes @ 768x1024` em 4 idiomas (deltas 77–105px = comprimento do texto); a tabela nunca tinha o wrapper canônico `.table-wrap{overflow-x:auto}` — corrigido e volta final **`AUDIT-DONE checks=764 ok=764 overflow=0 authfail=0 httpfail=0` (exit 0)**, 0 PNGs novos — transcript `evidence/d41-design-audit-s20-postcss-rodada-final.txt`.
- **Scanner permanente de i18n**: `scripts/qa/scan-i18n-keys.mjs` varre todo o Web (Razor `L.T("...")` com **qualquer** segundo argumento, JS `t("...")`, registro de navegação C# `Key/AriaKey="..."`) contra os 4 catálogos — fecha o gap dos keyuse ad-hoc por sprint que variavam escopo e deixaram as 6 chaves `layout.*` renderizarem cru por várias sprints (antes do merge acusava exatamente elas; depois, 0 ausentes).
- **Limitações mantidas**: pseudo-elementos/SVG fora da medição de contraste; estados vazios medidos só para `<table>`; placeholder-vs-instrução por amostragem visual + varredura estática; provedor real de IA pendente (`none`); dívida pt-first herdada cross-camada; CI oficial nunca verde publicada; aceite humano pendente. Detalhe item a item em D-OC54.
- **Portal**: `.odca/progress.json` inalterado (40%, baseline/IDs/pesos intactos). Sem push → CI oficial não verificada; aceite humano pendente.

## Incremento: Sprint 19 — IA avançada: análise inteligente de contratos (D-OC53) (19/10/2026)

- **Base**: `codex/s00-foundation` @ `b428a6a` (Sprint 18 / D-OC52). Nova migração **v063** (canônico + snapshot byte-idêntico; `DatabaseSchema.CurrentVersion` 62→63) importada sem erro em banco fresco na suíte. GATE sobre a árvore final: builds Release+Debug estritos (**sln `-warnaserror`, 13 projetos**) **0W/0E**, Domain **389/389**, suíte `-FreshDb` **291/291 TEST-EXIT=0** (baseline 266 + 24 novos em `AiAdvancedTests.cs` + 1 novo fato `MigrationChecksumTests.ReleaseV063...`), assets **exit=0**; catálogos **4052 chaves/cultura, paridade 4/4** (+17×4 via delta versionado `scripts/i18n/d-s19-ia-avancada.json`, incluindo a correção da chave pré-existente `documents.stage_all`); keyuse **0 ausentes**; scan de codepoint limpo (só `œ` francês legítimo pré-existente). Commit único ao final, **sem push**.
- **F-0 (persistência + módulo)**: tabela `odca.document_ai_analyses` (append-only por versão imutável; `content_hash char(64)` liga análise↔extração; payload jsonb c/ score+fatores+delta; RLS tenant; GRANT SELECT/INSERT ao `odca_app`) + entitlement `module.ia_analysis` nos 3 planos publicados v2 (catálogo passa a 7/7/8 módulos — testes S00/PlanGate/Hierarchical atualizados) + `organization_feature_state` recriada com whitelist de 8 módulos e CHECK de bloqueios estendido. Endpoints evoluem: **GET** lê a linha persistida (cache, zero chamadas ao provedor) senão analisa ao vivo sem gravar; **POST** gera+persiste com idempotência por hash de conteúdo e evento `ai_analysis_completed`; módulo fora do plano ⇒ 200 `module_not_enabled` antes do provedor (gate global `Features:AiAnalysis` segue sendo o kill-switch 503).
- **F-A/F-B/F-C (determinístico em C#)**: `RiskScorer` 0–100 (low=20/medium=45/high=70 base; achado +2/+6/+12; cláusula atípica +3; clamp — fórmula única registrada no D-OC53), `TemplateComparisonBuilder` vs. as 8 cláusulas padrão de `ClauseCodes` (presente ✓/ausente ○/fora do padrão !), `RenewalSuggestionBuilder` (high ou penalty/price_change medium-high ⇒ `renegotiate`; medium ⇒ `review`; low ⇒ `renew`; sem análise ⇒ null) exibido apenas na **List** das renovações com "a decisão é sua". A IA descreve, o código decide os números — mesma entrada dá o mesmo resultado em qualquer provedor.
- **F-D (busca semântica)**: a busca da central de documentos chega ao `raw_text` extraído (termos OR, jamais restringe o que casava antes) em dois estágios: palavras-chave determinístico sempre ativo; interpretação por frase via provedor (`SearchQueryParser`/`AiInterpretation`) só quando gate+módulo+`http` ativos, com modo exibido honesto (`semantic.engine` = `"ai"` só se o provedor interpretou de fato). Filtro numérico de multa declarada ("multa … X %", >mínimo) aplica-se nos dois modos; nota de modo na UI nas 4 línguas.
- **F-E (Worker)**: auto-análise pós-extração no `DocumentWorker` (gate + módulo + cultura do tenant; dedup por hash; fronteira tolerante — falha loga e nunca quebra o pipeline documental; evento auditável). Sem e2e na suíte (sem fixture de extração executada) — registrado como limitação.
- **Web**: botão do painel passa a **POST** (gera+persiste) com cache client-side por versão e dedup server-side; blocos novos de risco (0–100 + fatores explicáveis) e delta vs. padrão; `module_not_enabled` declarado; BFF com action POST espelhando os estados 404/403/503/422; cartão de sugestão nas renovações; nota de modo na busca. Zero literal na JS (bolsa i18n `studio.*`).
- **QA classificado**: 2 lacunas do bloco 063 inicial (GRANT ausente; CHECK de bloqueios sem `ia_analysis`) corrigidas dentro do próprio bloco com checksum final `23559ecd…ffd0f` (blocos 1..62 byte-idênticos, validado por teste); parêntese ausente no filtro de busca pego pela suíte; chave `documents.stage_all` pré-existente sem catálogo corrigida no delta; 1 flake de timing em OutboxLease (sem reprodução); **1 bug real de produto pego pela auditoria ao vivo (r6)** — `AiSuggestion` no construtor do `RenewalListItem` fora da projeção Dapper da lista (503 em `/renovacoes` com dados) — corrigido na árvore e blindado por 2 testes de regressão. Detalhe item a item em D-OC53 §9.
- **Auditoria de design (d41)**: `AUDIT-DONE checks=764 ok=764 overflow=0 authfail=0 httpfail=0` (exit 0), 0 PNGs de falha novos — transcript `evidence/d41-design-audit-s19-rodada-final.txt` (rodada **r8** completa sobre a árvore corrigida; a r6 foi interrompida por **503 real em `/renovacoes`** — bug de produto da S19: `AiSuggestion` no construtor do `RenewalListItem` fora da projeção Dapper, D-OC53 §9 item 8, corrigido na árvore + 2 testes de regressão; a r7 percorreu 657 medidas OK com `/renovacoes` verde e foi interrompida por esgotamento de RAM — item 9).
- **Limitações mantidas**: sugestão não deriva dos ciclos registrados (S22) nem altera prazos/multas; multa por regex na formulação canônica; sem embeddings; diff literal contra modelos oficiais futura; auto-análise sem e2e na suíte; sugestão só na List; provedor real pendente de decisão externa (`none` até lá — a UI declara o modo); dívida pt-first cross-camada (~54 `[Required]`, mensagens da API, valores crus); 64 classes CSS → S20; CI oficial nunca verde publicada; aceite humano pendente.
- **Portal**: `.odca/progress.json` inalterado (40%, baseline/IDs/pesos intactos). Sem push → CI oficial não verificada; aceite humano pendente.

## Incremento: Sprint 18 — fundações de IA: análise com provedor configurável + fallback honesto (D-OC52) (19/10/2026)

- **Base**: `codex/s00-foundation` @ `0d75eb8` (Sprint 17 / D-OC51). Sem nova migração (canônico v062 intacto); a análise consome o `extraction_results.raw_text` já persistido por versão. Novo projeto `Odca.AI` (`IAiService`/`NullAiService`/`HttpAiService` compatível com chat `/chat/completions`/parser defensivo/DI) + contrato de wire `Odca.Contracts.Ai`. GATE sobre a árvore final: builds Release+Debug estritos (**sln `-warnaserror`, 13 projetos**) **0W/0E**, Domain **389/389**, suíte `-FreshDb` **266/266** (baseline 236 + 30 novos TEST-EXIT=0), assets **exit=0**; catálogos **4035 chaves/cultura, paridade 4/4** (+21×4 via delta versionado `scripts/i18n/d-s18-ia.json`; 0 colisões). Commit único ao final, **sem push**.
- **F-1 (fundação)**: provedor configurável por operação (seção `Ai:`; `http` só com Endpoint absoluto + ApiKey, senão `none` por construção), system prompt cultural (pt-BR/en-US/es-ES/fr-FR), truncamento declarado em `MaxInputChars`, qualquer falha degrada para `provider_unavailable` com reason estável — análise IA nunca quebra a jornada documental.
- **F-2 (API)**: `ContractAiController` por versão exata do documento (RLS + `tenant.contracts.read` no servidor; fonte = extração mais recente da versão; sem texto ⇒ 200 `no_text`; **SHA-256 do texto analisado + AnalyzedChars** — vínculo análise↔versão), gate falha-fechada `Features:AiAnalysis` (503 problem+json c/ `api.gate.release_ai` localizado) e dependência `ai.provider` no diagnóstico de plataforma.
- **F-3 (Web)**: BFF `DocumentAiAnalysis` (contractId resolvido do rascunho no servidor) + botão "Analisar com IA" por versão no estúdio c/ estados honestos (carregando/indisponível/sem texto) e resultado (resumo, achados c/ risco + evidência literal, categorias, rodapé modelo+caracteres+hash) — zero literal de UI na JS (bolsa `_I18nBag` prefixo `studio.*`) e nenhuma classe CSS nova.
- **F-4 (testes)**: 30 novos na suíte de integração (25 unidades puras — parser/provedores/DI/caminho do gate — + 5 controller-level em banco — estados, hash conferido, extração mais recente, 404, 403).
- **Limitações mantidas**: provedor real pendente de decisão externa (`none` até lá — fallback transparente na UI); análise stateless em S18 (cache/versionamento → S19); cenários avançados (scoring/comparação/preditiva/busca semântica) → S19; dívida pt-first herdada cross-camada (`[Required]` ~54, mensagens da API, valores crus); 64 classes CSS → S20; CI oficial nunca verde publicada; aceite humano pendente.
- **Portal**: `.odca/progress.json` inalterado (40%, baseline/IDs/pesos intactos). Sem push → CI oficial não verificada; aceite humano pendente.

## Incremento: Sprint 17 — conta e resíduo em 4 idiomas: Account + MFA + navegação + importações + fallback global (D-OC51) (19/10/2026)

- **Base**: `codex/s00-foundation` @ `2880156` (Sprint 16 / D-OC50). Sem nova migração (canônico v062 intacto) e sem mudanças em API/Domain/Worker/assets/SQL. GATE sobre a árvore final: builds Release+Debug estritos (**sln `-warnaserror`**) **0W/0E**, Domain **389/389**, suíte `-FreshDb` **236/236 TEST-EXIT=0**, assets **exit=0**; catálogos **4014 chaves/cultura, paridade 4/4** (+163 via delta versionado `scripts/i18n/d-s17-conta-residuo.json`: `acct.*` 72, `imp_rev.*` 45, `imp.*` 35, `bff.*` 4, `group_*` 3, `sol.*` 2, `cons.plan_summary_aria` 1, `central-notificacoes` 1; 652 entradas, 0 colisões; 820 chaves únicas usadas em 22 arquivos / 0 ausentes). Commit único ao final, **sem push**.
- **F-1 (conta)**: as 7 views de conta reescritas no padrão canônico (`_culture` do CultureMiddleware + `L.T`): `Login` (toggle de senha via `data-toggle-show/hide` + `data-processing-label`), `Conta` (senha/MFA com 2 estados/notificações/help), `ChangePassword`, `MfaEnrollment` (QR/chave manual/URI TOTP/ajuda), `MfaChallenge`, `RecoveryCodes`, `AccessDenied` — **62 call sites**. `AccountController`: **19 chaves** (4 blocos `switch` de `ApiCallStatus` + sucesso de troca de senha + falha de regeneração + aviso de logout sem revogação remota confirmada).
- **F-2 (importações)**: `Imports/{Index,Review}` + `ImportsController` — **90 call sites**: filtros completos com reuso de 6 chaves canônicas, 6 diagnósticos localizados (função local capturando a cultura), lista com data culturalizada (`CulturedTime.DateTime`, substituindo o formato fixo `dd/MM/yyyy HH:mm`), barra de progresso 5 passos + tabs mobile, formulário de conferência manual, sugestões da extração com decisões aceitar/editar/rejeitar, confirms finais com chaves dedicadas (FR com apóstrofo U+2019 por decisão registrada).
- **F-3 (fallback global + navegação + resíduo)**: `BffErrorHandlingMiddleware` com `LocalizationService` (fallback JSON+HTML `bff.*`, `<html lang>` dinâmico `culture ?? pt-BR` — middleware roda antes da CultureMiddleware, fallback correto por construção); navegação **66/66 chaves** verificadas por codepoint (4 novas: 3 grupos acentuados + `central-notificacoes`); 13 aria-labels residuais em 10 views via reuso (`common.breadcrumb_aria` ×8, `admin.summary_aria`, `sheet.details_aria`, `cons.plan_summary_aria`); `forms.js` toggle lendo data-attributes com fallback pt-first. **Scan global final: 0 literal pt-BR visível em toda a UI Razor** (52 linhas acentuadas = 49 comentários + 3 endônimos do seletor de idioma).
- **F-4 (auditoria de design estendida 46→49 rotas)**: +`login` (`/entrar`, anônima), +`minha-conta` (cliente), +`importacao` (cliente) ×4 idiomas ×4 viewports = **191 pares rota×idioma × 4 viewports = 764 medidas** (era 179/716) → execução ao vivo na rodada **r4b**: `AUDIT-DONE checks=764 ok=764 overflow=0 authfail=0 httpfail=0` (exit 0), 0 PNGs de falha (a r4 anterior foi classificada como falha ambiental — D-OC51 §7 item 7; transcript em `evidence/d41-design-audit-s17-r4-falha-ambiental.txt`); transcript final commitado em `evidence/d41-design-audit-s17-rodada-final.txt`.
- **Limitações mantidas**: GETs internos sem rota fixa na d41 (`importacao/{id}/revisao`, `mfa/inscricao`, `mfa/desafio`, `recuperacao-codigos`, `alterar-senha`, `acesso-negado` — cobertos por build estrito + varredura + suíte); dívida pt-first restante reduzida a cross-camada (`[Required(ErrorMessage)]` ~54, mensagens de domínio da API em pt, valores de domínio crus); 64 classes CSS sem definição em `site.css` (inventariadas → S20); CI oficial nunca verde publicada; aceite humano pendente.
- **Portal**: `.odca/progress.json` inalterado (40%, baseline/IDs/pesos intactos). Sem push → CI oficial não verificada; aceite humano pendente.

## Incremento: Sprint 16 — gestão de clientes em 4 idiomas: Customers + consumo + planos + caixa (D-OC50) (19/10/2026)

- **Base**: `codex/s00-foundation` @ `1534bc7` (Sprint 15 / D-OC49). Sem nova migração (canônico v062 intacto) e sem mudanças em API/Domain/Worker/assets/SQL. GATE sobre a árvore final: builds Release+Debug estritos (**sln `-warnaserror`**) **0W/0E**, Domain **389/389**, suíte `-FreshDb` **236/236 TEST-EXIT=0**, assets **exit=0**; catálogos **3851 chaves/cultura, paridade 4/4** (+231 via delta versionado `scripts/i18n/d-s16-clientes.json`: `cust.*` 131, `cons.*` 41, `usage.*` 17, `plans.*` 19, `inbox.*` 17, `status.*` 6; delta↔catálogo 924 comparações / 0 divergências; 308 chaves usadas × 4 catálogos = 1232 / 0 ausentes). Commit único ao final, **sem push**.
- **F-1 (views)**: as 7 views da área passam ao padrão canônico (`_c` do CultureMiddleware + `L.T`): `Customers/{Index,Users,Create,Details}` (listagem com dual Status×consumo OCR/assinatura via `cell_usage` 4 args InvariantCulture; busca de usuários plataforma c/ empty states parametrizados; cadastro org c/ 3 seções e notas split em `<strong>`/`<em>`; **ficha completa do tenant** — preview de mudança de plano c/ 4+4+2 args, métricas, funcionalidades c/ estado switch→4 chaves e bloqueio/liberação, situação admin+comercial via StatusLabels overloads, solicitações approve/reject, concessão manual, franquias credit_line, histórico timeline), `Consumption\Index` (5 cards métricos c/ `CulturedTime.Date(DateTimeOffset)`, barra progresso, breakdown 5 itens, **solicitar armazenamento em 3 passos** com `step3_note` em quotes curly, créditos `credit_line_unit` 5 args c/ unidade, módulos help longo, histórico), `Plans\Index` (cards c/ 7 limites, **módulos via `ModuleName` reutilizando `api.gate.feature_*`** — unificação registrada) e `Inbox\Index` (resíduos: vistas pessoais + DueOn → `CulturedTime.Date(DateOnly)`). Datas: `"dd/MM/yyyy"` fixo sai das UIs → `CulturedTime.Date`/`DateTime` preservando relógio 24h; numéricos → `ToString(InvariantCulture)`.
- **F-1 (controllers BFF)**: injeção canônica de `LocalizationService` em 3 controllers (`CustomersController` **28 call sites** — títulos, validações plan/justify/grant, sucessos incluindo `ok_create {0}` com nome da org, falhas `UserMessage` com fallback localizado; `PlanItems` static→instância para usar `L("cust.plan_item",…)`; `ConsumptionController` 2; `InboxController` 10 — vistas salvar/padrão/inativar) + `StatusLabels.cs` sobrecargas `Status(v,t)`+`Commercial(v,t)` no padrão TenancyDisplay (legado pt preservado, estados desconhecidos exibidos como gravados) — **377 call sites** nos 11 arquivos. Dívidas registradas: aria-label "Resumo do plano" (chrome não visível), `[Required(ErrorMessage)]` pt-first (cross-área), valores de domínio crus (`@r.Status`, `@e.Type`, RoleCode).
- **F-2 (auditoria de design)**: `audit-d41-design.ps1` estendido 39 → **46 rotas** — entram 7 rotas novas ×4 idiomas: superadmin `clientes`/`clientes-novo`/`clientes-usuarios`/`cliente-detalhe` (fixture tenant) + cliente `planos`/`plano-e-consumo`/`caixa` = 179 pares rota×idioma × 4 viewports = **716 medidas** (era 151/604) → execução ao vivo **`AUDIT-DONE checks=716 ok=716 overflow=0 authfail=0 httpfail=0`** (transcript `evidence/d41-design-audit-s16-rodada-final.txt`; QA: URL da rota de catálogo no script corrigida antes da rodada final). Preservação estrutural: dif de multiset de tags HEAD×trabalho **0/0** nas 7 views; varredura não-ASCII = apenas glifos neutros (`—`, `·`, `✓`) e comentários C#.

## Incremento: Sprint 15 — área de administração de plataforma em 4 idiomas (D-OC49) (19/10/2026)

- **Base**: `codex/s00-foundation` @ `69e45c0` (Sprint 14 / D-OC48). Sem nova migração (canônico v062 intacto) e sem mudanças em API/Domain/Worker/assets/SQL. GATE sobre a árvore final: builds Release+Debug estritos (**sln `-warnaserror`**) **0W/0E**, Domain **389/389**, suíte `-FreshDb` **236/236 TEST-EXIT=0**, assets **exit=0**; catálogos **3620 chaves/cultura, paridade 4/4** (+216 via delta versionado `scripts/i18n/d-s15-plataforma.json`: `platform.lib.*` 34, `platform.rev.*` 45, `platform.rev_js.*/rev_prop.*` 31, `module.*` 43, `platform.diag.*` 25, `platform.audit.*` 16, `platform.sla.*` 18, `platform.tpl.*` 4; delta↔catálogo 864 comparações / 0 divergências). Commit único ao final, **sem push**.
- **F-1 (views)**: as 7 views da área passam ao padrão canônico (`_c` do CultureMiddleware + `L.T`): `PlatformLibrary/{Index,Create,NewRevision}` (catálogo oficial global com publicar/retirar justificados e linhas de versão, resíduo do novo modelo, e a nova revisão com editor de campos/propriedades/condições + **bolsa i18n do script no padrão do Studio** — `Html.Raw` com `UnsafeRelaxedJsonEscaping`, `window.OdcaI18n.t(key,…args)` e `confirm()` do publish localizado), `PlatformAudit\Index` (busca/filtros/paginação/metadados preservados), `PlatformDiagnostics\Index` (8 dependências + corrente honesta configurado≠acessível≠comprovado + filas), `PlatformOperations\Index` (matriz SLA vigente — Basic/Enterprise, h/min e Legal Design literais por decisão registrada) e `Modules\Index` (estados controlados por perfil tenant×plataforma). Datas: `"dd/MM/yyyy"` fixo sai das UIs → `CulturedTime.Date(value.ToLocalTime(),_c)` / `CulturedTime.DateTime(occurredAt.ToLocalTime(),_c)` preservando o relógio exibido no HEAD; numéricos/paginação → `InvariantCulture`.
- **F-1 (controllers BFF)**: injeção canônica de `LocalizationService` em 4 controllers (`PlatformLibraryController` 16 call sites — títulos, validações, sucessos incluindo "Revisão {0} publicada…" com invariante de versões imutáveis e falhas `UserMessage` com fallback localizado; `ModulesController` com catálogo estático slug→chaves resolvido por request mantendo o isolamento por perfil (o slug `contratos` existe nos dois perfis com títulos distintos) e `OpenHref` só na auditoria; `PlatformDiagnosticsController`/`PlatformOperationsController` com títulos localizados) — **310 call sites** nos 11 arquivos. Teste de domínio que instanciava o `ModulesController` passou a injetar o serviço (cultura ausente → fallback pt-BR; nenhuma asserção mudada). Dívidas registradas no escopo: `[Required(ErrorMessage)]` pt-first (política cross-área), JSON de conteúdo inicial do template em pt (conteúdo de documento de domínio), `d.Detail` do diagnóstico em pt (mensagem da API) e aria-label global de breadcrumb (S17).
- **F-2 (auditoria de design)**: `audit-d41-design.ps1` estendido 28 → **39 rotas** — as 4 superfícies superadmin legadas passam a medir 4 idiomas (+12 pares) e entram 11 rotas novas ×4: superadmin `biblioteca`/`biblioteca-novo`/`biblioteca-revisao` (fixture `services-agreement` published rev 1)/`sla-configuracoes`/`diagnostico` e módulos `modulo-contratos`/`modulo-documentos`/`modulo-ajuda` (cliente) + `modulo-usuarios`/`modulo-auditoria`/`modulo-biblioteca` (superadmin) = 151 pares rota×idioma × 4 viewports = **604 medidas** (era 380) → execução ao vivo **`AUDIT-DONE checks=604 ok=604 · overflow=0 · authfail=0 · httpfail=0` (exit=0)**, transcript `evidence/d41-design-audit-s15-rodada-final.txt`. Preservação estrutural: dif de multiset de tags HEAD×trabalho 0/0 nos 6 views restantes (NewRevision +2 `<span aria-hidden>` +2 `<script>` da bolsa i18n, proposital).
- **Classificação QA**: antes do GATE — (1) o merge pós-`git checkout` encontrou CRLF do smudge onde o formato canônico é BOM+LF → normalização `\r\n`→`\n` + revalidação de formato antes de escrever; (2) checagem verbatim de integridade de acentos relida contra `git show HEAD:` (o working tree já estava editado); (3) cross-check de chaves revelou 2 chaves ausentes do delta (`platform.rev_js_origin_contractor`, `platform.sla_th_priority`) → delta regenerado 214→216, remergiado e paridade 3620 ×4/4 revalidada; (4) transcrição `splice(index,index)`→`splice(index,1)` do `removeField` corrigida no review. Amostras byte-a-byte (ç=c3a7, ã=c3a3, õ=c3b5, í=c3ad) e varredura de marcas de double-encoding = 0 nos 4 catálogos.
- **Limitações mantidas**: POSTs e estados internos dos fluxos (criação de modelo, publicação de revisão com confirmação, retirar/publicar com justificativa, filtros da auditoria aplicados) sem rota fixa na d41 (cobertos por build estrito + varredura + dif estrutural + suíte inalterada); dívida i18n pt-first restante particionada em **S16 — gestão de clientes** (Customers ×4 + controller, Consumption + VMs, Plans, Inbox + controller, usos de StatusLabels) e **S17 — conta e resíduo** (Account ×7 + controller + VMs MFA/ChangePassword, resíduo Solicitações, `BffErrorHandlingMiddleware`, endônimo `_Layout`, `NavigationRegistry` + renderer + aria-label de breadcrumb); CI oficial nunca verde publicada; aceitações humanas e decisões externas pendentes (D-OC45 §4).
- **Separação de evidências**: teste local (GATE completo + d41 ao vivo + transcript + checagem das 241 chaves usadas×catálogo + amostras byte) · **CI não verificada** (branch sem push; nenhum ID de execução inventado) · **aceite humano pendente** (área de administração de plataforma em 4 idiomas) · `.odca/progress.json` **inalterado** em 40% (baseline/IDs/pesos intactos).
- **Próximos passos**: S16 segue a sequência registrada (gestão de clientes) mantendo os bloqueios externos de D-OC45 §4 — push → CI oficial segue pré-condição para qualquer credenciamento de marco.

## Incremento: Sprint 14 — central de organização + visão geral do cliente + funil anônimo de onboarding em 4 idiomas (D-OC48) (18/10/2026)

- **Base**: `codex/s00-foundation` @ `3345ea0` (Sprint 13 / D-OC47). Sem nova migração (canônico v062 intacto) e sem mudanças em API/Domain/Worker/assets/SQL. GATE sobre a árvore final: builds Release+Debug estritos **0W/0E**, Domain **389/389**, suíte `-FreshDb` **236/236 TEST-EXIT=0**, assets **exit=0**; catálogos **3404 chaves/cultura, paridade 4/4** (+423 via delta versionado `scripts/i18n/d-s14-organizacao.json`: `org.*` 213, `perm.*` 50, `onb.*` 50, `dash.*` 36, `gs.*` 35, `reg.*` 21, `permg.*` 13, `conf.*` 3, `api.*` 2; delta↔catálogo 1692 comparações / 0 divergências). Commit único ao final, **sem push**.
- **F-1 (views)**: as 10 views da central de organização, visão geral do cliente, funil anônimo e visão global passam ao padrão canônico (`_c` do CultureMiddleware + `L.T`): `Organizations/{Team,Edit,Index,Accept}` (abas pessoas/convites/perfis, matriz de permissões com estados por plano/bloqueio/franquia, cadastro com controle de versão e 409, picker de organização, aceite de convite anônimo), `Onboarding/{CustomerHome,GettingStarted,Register,ConfirmEmail,RegistrationStarted}` (documentos recentes/revisões atribuídas no fuso do tenant, progresso real, registro com rótulos explícitos substitutos dos `DisplayNameFor`, confirmação) e `Home\Index` (14 indicadores, ações rápidas, eventos de auditoria com `CulturedTime`). Datas: `ToString("dd/MM/yyyy HH:mm")`/`"g"` fixos saem das UIs → `CulturedTime.DateTime(value,_c)` preservando o relógio exibido (`.ToLocalTime()` onde o original convertia zona — bug detectado e corrigido antes do GATE); formato canônico dd/MM/yyyy do documento preservado (D-OC22).
- **F-1 (controllers BFF + helper)**: injeção canônica de `LocalizationService` em `OrganizationsController` (41 call sites) e `OnboardingController` (17); mensagens comuns de erro de API unificadas nas chaves compartilhadas `api.too_many_attempts`/`api.service_unavailable`; `TenancyDisplay` ganha overloads cientes de cultura (`MemberStatus`/`InvitationStatus`/`DeliveryStatus`/`OrganizationStatus`/`DeliveryErrorMessage`/`RolePresentation`/`PermissionLabel`/`PermissionGroup`) resolvendo `perm.<código>`/`permg.<slug>` com fallback para o valor legado quando `T` devolve a própria chave (o nome canônico "Administrador da organização" segue como marcador determinístico → `org.role_superadmin`). Dívida registrada: `[Required(ErrorMessage)]` segue pt-first (política cross-área) e mensagens de domínio vindas da API permanecem em pt.
- **F-2 (auditoria de design)**: `audit-d41-design.ps1` estendido 20 → **28 rotas** (+`org-picker`/`equipe`/`editar-org`/`visao-geral-cliente`/`primeiros-passos` sessão cliente; +`aceitar-convite`/`contratar`/`confirmar-email` sessão anônima — caminhos conferidos contra os `[HttpGet]` reais) — 95 pares rota×idioma × 4 viewports = **380 medidas** (era 252) → execução ao vivo **`AUDIT-DONE checks=380 ok=380 · overflow=0 · authfail=0 · httpfail=0` (exit=0)**, 0 PNGs de falha; transcript `evidence/d41-design-audit-s14-rodada-final.txt`. Preservação estrutural: dif de multiset de tags HEAD×trabalho 0/0 nos 10 views (CustomerHome +2 `<span aria-hidden>` proposital; Register envolve o controle no label — acessibilidade).
- **Limitações mantidas**: estados internos dos painéis da equipe e POSTs do funil anônimo sem rota fixa na auditoria (cobertos por build estrito + varredura + dif estrutural + suíte inalterada); dívida i18n pt-first restante (~370 linhas heurísticas: PlatformLibrary/Customers/Imports/Consumption/Plans-Modules/Account/Inbox/PlatformAudit-Diagnostics-Operations); CI oficial nunca verde publicada; aceitações humanas e decisões externas pendentes (D-OC45 §4).
- **Separação de evidências**: teste local (GATE completo + d41 ao vivo + transcript + checagem das 430 chaves usadas×catálogo + amostras byte-a-byte) · **CI não verificada** (branch sem push; nenhum ID de execução inventado) · **aceite humano pendente** (central de organização + visão geral + funil anônimo em 4 idiomas) · `.odca/progress.json` **inalterado** em 40% (baseline/IDs/pesos intactos).
- **Próximos passos**: S15 segue a sequência registrada (próxima fatia da dívida i18n pt-first: PlatformLibrary/Customers/Imports/Consumption/Modules/Inbox/PlatformAudit-Diagnostics-Operations) mantendo os bloqueios externos de D-OC45 §4 — push → CI oficial segue pré-condição para qualquer credenciamento de marco.

## Incremento: Sprint 13 — central contratual do cliente em 4 idiomas: renovações + revisões + ações (D-OC47) (18/10/2026)

- **Base**: `codex/s00-foundation` @ `fd1b42d` (Sprint 12 / D-OC46). Sem nova migração (canônico v062 intacto) e sem mudanças em API/Domain/Worker/assets/SQL. GATE sobre a árvore final: builds Release+Debug estritos **0W/0E**, Domain **389/389**, suíte `-FreshDb` **236/236 TEST-EXIT=0**, assets **exit=0**; catálogos **2981 chaves/cultura, paridade 4/4** (+235 via delta versionado `scripts/i18n/d-s13-contratos.json`, namespaces `ren.*`/`rev.*`/`actions.*`/`common.*`). Commit único ao final, **sem push**.
- **F-1 (views)**: as 5 views da central contratual passam ao padrão canônico (`_c` do CultureMiddleware + `L.T`): `Renewals/{Index,Detail}` (cards, lembretes, filtros, linhas, confirmações, vigência × proposta, formalização, linha do tempo), `Reviews/{Index,Detail}` (filtros, fila, resumo, mensagens/notas, histórico, reatribuição, decisão, cancelamento) e `Actions\Index` (resumo, filtros por fonte, itens, paginação). Datas/moeda: `CulturedTime.Date/DateTime(value,_c)` e `N2` com cultura ativa no lugar de `dd/MM/yyyy`/`C` fixos (UI na ordem da língua ativa; formato canônico dd/MM/yyyy do documento preservado). Melhoria registrada: badge de status em `Reviews\Detail` exibía→exibia o código cru; agora label localizado (classe CSS inalterada).
- **F-1 (controllers BFF)**: injeção canônica de `LocalizationService` em `RenewalsController` (17 strings), `ReviewsController` (9) e `ActionsController` (2) — ModelState, conflitos 409, sucessos/falhas e `ViewData["Title"]` em 4 idiomas; `msg_applied_until` recebe data já formatada culturalmente.
- **F-2 (auditoria de design)**: `audit-d41-design.ps1` estendido 17 → **20 rotas** (+`renovacoes`, +`solicitacoes`, +`acoes` do cliente ×4 idiomas; 63 pares × 4 viewports = **252 medidas**) → execução ao vivo **`AUDIT-DONE checks=252 ok=252 · overflow=0 · authfail=0 · httpfail=0` (exit=0)**, 0 PNGs de falha; transcript `evidence/d41-design-audit-s13-rodada-final.txt`. Confirmação estrutural: dif de esqueleto (tag+atributos HEAD×nova) 0/0 nos 5 views; varredura de não-ascii residual = só separadores neutros originais.
- **Classificação QA**: byte-a-byte do delta capturou antes do GATE 5 typos (pt "Substitída/Substitïda"→Substituída, pt "Alterção"→Alteração ×3, ES "e inicio"→"y inicio", FR "enregistrée"→"enregistré", EN "; The"→"; the") e o confronto com o código-fonte revelou 6 chaves ausentes adicionadas ao delta — corrigidos com revalidação de paridade 1210 chaves × 4 = 4840/0 e amostras de bytes no catálogo.
- **Limitações mantidas**: páginas de detalhe individuais de renovação/revisão não têm rota fixa na d41 (cobertas por build estrito + varredura + dif de esqueleto); dívida i18n pt-first restante (Accounts/Customers/Organizations/Imports/Onboarding/Modules/Inbox/Consumption + áreas operacionais); CI oficial nunca verde publicada; aceitações humanas e decisões externas pendentes (D-OC45 §4).
- **Separação de evidências**: teste local (GATE completo + d41 ao vivo + transcript + amostras byte) · **CI não verificada** (branch sem push; nenhum ID de execução inventado) · **aceite humano pendente** (4 idiomas da central contratual) · `.odca/progress.json` **inalterado** em 40% (baseline/IDs/pesos intactos).
- **Próximos passos**: S14 segue a sequência registrada (próxima fatia da dívida i18n pt-first das áreas operacionais/contábeis) mantendo os bloqueios externos de D-OC45 §4 — push → CI oficial segue pré-condição para qualquer credenciamento de marco.

## Incremento: Sprint 12 — conteúdo das notificações no idioma do destinatário + backup pré-piloto com retenção (D-OC46) (18/10/2026)

- **Base**: `codex/s00-foundation` @ `cef18e0` (Sprint 11 / D-OC45). Nova migração **v062** (coluna `language varchar(16) NOT NULL DEFAULT 'pt-BR'` + CHECK das 4 culturas em `odca.notification_outbox`; `claim_notification` passa a retornar o idioma; `complete_obligation_reminder`/`complete_renewal_reminder` reescritas com CASE por idioma; checksum `11168e2a…380`, 62 blocos, snapshot `database/releases/odca-v062.sql` byte-idêntico) + **19 chaves i18n × 4** (`notif.title.*` ×10, `notif.body.*` ×9, delta `scripts/i18n/d-s12-notifications.json`) + atualização de `notif.lang_help` → catálogos em **2746 chaves/cultura, paridade 4/4**.
- **F-1 (i18n do conteúdo de notificações)**: idioma resolvido **na origem do evento** — call sites chamam `NotificationEnqueue.RecipientLanguageAsync` (overloads com/sem transação; nunca lança → fallback pt-BR), e a mesma string alimenta título in-app e a coluna `outbox.language` (paridade por construção); os 8 call sites foram migrados (ContractStudio ×4, ReviewRequests ×2, Solicitations ×2), overloads legados preservados para testes e convites seguem pt-BR default (fora de escopo, registrado). Workers ganham o idioma do dispatch (`OutboxItem.Language`, vazio → pt-BR) e `INotificationTransport.SendUserNotificationAsync` agora carrega `language` nos 4 transports (SMTP localiza via `LocalizationService` injetada; FilePickup registra "Idioma:" no artefato; Stub inalterado no comportamento). Datas: en-US `MM/DD/YYYY`, demais `DD/MM/YYYY`.
- **F-2 (backup pré-piloto com retenção)**: `run-backup-restore.ps1 -KeepDumpDir <dir>` retém cópia carimbada (`yyyy-MM-dd_HHmmss`) do dump **somente após todos os checks de paridade**, com bytes+sha256 no log; sem o parâmetro o comportamento é idêntico ao S10.
- **Prova local**: v062 aplicada em `odca_test_disposable` (exit=0) + smoke das funções SQL (en-US `Contract obligation due: Obrigacao S12` / `Due on 01/05/2027.` vs pt-BR `Prazo contratual: Obrigacao S12` / `Vencimento em 05/01/2027.`); backup/restore **27/27 EXIT=0** com dump retido (989512 B, sha256 `f0ec4fbc…400e7`) — transcript `evidence/d44-backup-restore-rodada-final.txt` regenerado; ensaio SMTP com worker real: pt-BR **17/17** (baseline S10 preservado), en-US **20/20** e fr-FR **20/20** via extensão opcional `-RecipientLanguage` (acento sobrevivendo) — transcript commitado = en-US 20/20. GATE sobre a árvore: builds Release+Debug estritos **0W/0E**, Domain **389/389**, suíte `-FreshDb` **236/236 TEST-EXIT=0** (+2 fatos: título salvo no idioma + checksum/release v062), assets **exit=0**; d41 não reexecutada de propósito (nenhuma rota auditada renderiza chave nova/alterada; 204/204 das rondas anteriores segue válido).
- **Classificação QA**: a rodada fr-FR expôs um typo no catálogo francês desta sprint ("plateforme" sem o "e"); corrigido nos catálogos e no delta com revalidação byte-a-byte dos 80 valores S12 e rodadas refeitas verdes — conteúdo novo é prova nova.
- **Pendências externas mantidas (registradas, nenhuma inventada)**: CI oficial do SHA (branch sem push) → `.odca/progress.json` permanece **40%**; provedor SMTP real ainda decisão; janela legal de retenção de backups "a definir"; aceite humano dos 4 idiomas das notificações pendente. Nenhum marco credenciável nesta rodada.

## Incremento: Sprint 11 — dress rehearsal do piloto e veredito go/no-go (D-OC45) (18/10/2026)

- **Base**: `codex/s00-foundation` @ `42ca1b1` (Sprint 10 / D-OC44). Sem nova migração (canônico v061 intacto) e sem mudança de produto: o diff é o orquestrador QA, uma asserção obsoleta corrigida numa prova ao vivo e docs/evidências. GATE sobre a árvore final: builds Release+Debug estritos **0W/0E**, Domain **389/389**, suíte `-FreshDb` **234/234 TEST-EXIT=0**, assets **exit=0**. Commit único ao final, **sem push**.
- **F-1 (dress rehearsal consolidado)**: `run-pilot-rehearsal.ps1` ganhou os estágios autocontidos `S10-BACKUP` e `S10-SMTP` — **um único comando reproduz toda a cadeia de prova atual** (`C-SLA → ACEITE → D34-JORNADA → D37-ASSINATURA → D38-ATENDIMENTO → D39-SEGURANCA → S10-BACKUP → S10-SMTP`). Rodada completa (~18 min em máquina compartilhada): 7/8 estágios verdes; a única falha (D39) foi classificada como asserção QA obsoleta (`P0a schema_migrations em v060`, anterior à v061 da S9 — o checkpoint de segurança do script abortou a prova após W1b), corrigida e reexecutada verde (`fails=0`). Transcripts versionados: `evidence/d45-dress-rehearsal-rodada-final.txt`, `...-d39-falha-classificada.txt`, `...-d39-rerun.txt`.
- **F-2 (go/no-go formal)**: veredito item a item contra as pré-condições do runbook D-OC40 §7.1 — atendidas localmente: backup/restauração ensaiada (26/26), ensaio + auditoria de design verdes (204/204 desde a S9); pendentes externas: **CI oficial do SHA**, decisão do **transporte de assinatura**, decisão do **canal de aviso**, **usuários convidados + aprovação da versão** e política de sessão de 15 min. **Veredito: NO-GO formal com capacidade técnica de GO** + checklist de 6 passos para virar GO (D-OC45 §4–§5).
- **Limitações mantidas**: CI oficial nunca verde publicada; provedores externos de assinatura e SMTP ainda decisão (mecanismos comprovados); deploy físico multi-instância e backup offsite; janela legal de retenção "a definir"; dívida i18n pt-first das áreas operacionais.
- **Separação de evidências**: teste local (GATE completo + dress rehearsal versionado) · **CI não verificada** (branch sem push; nenhum ID de execução inventado) · **aceite humano pendente** (convite + aprovação da versão) · `.odca/progress.json` **inalterado** em 40% (baseline/IDs/pesos intactos).
- **Próximos passos**: encerra o plano explícito de sprints (S11). Daqui à estreia 11/01/2027 (85 dias): push → CI oficial; decisões de assinatura/canal/sessão; usuários convidados; preparação do ambiente do piloto (MFA dos superadministradores + backup pré-piloto com restore validado) — sequência em D-OC45 §5.

## Incremento: Sprint 10 — backup/restore real ensaiado e transporte SMTP por protocolo real (D-OC44) (18/10/2026)

- **Base**: `codex/s00-foundation` @ `f7810a1` (Sprint 9 / D-OC43). Sem nova migração (canônico v061 intacto) e sem mudanças em portal/i18n/API/Web. GATE sobre a árvore final: builds Release+Debug estritos **0W/0E**, Domain **389/389**, suíte de integração `-FreshDb` **234/234 TEST-EXIT=0**, assets **exit=0**. Commit único ao final, **sem push**.
- **F-1 (backup/restauração real ensaiada)**: script versionado `scripts/qa/run-backup-restore.ps1` — `pg_dump -Fc` do banco de prova do piloto → `pg_restore --no-owner --exit-on-error` em banco novo → **26 checks comparando fonte × restaurado** (versão de migração 61=61, paridade de contagem em 11 tabelas-chave, md5 por id ordenado de users/contracts/audit_events, superfície de leitura sob RLS como `odca_app` sem contexto de tenant em paridade, função `SECURITY DEFINER` executável após o restore) → derruba o banco restaurado; transcript versionado `evidence/d44-backup-restore-rodada-final.txt` (**26/26, EXIT=0**).
- **F-2 (transporte SMTP por protocolo real)**: sink MTA local versionado (`scripts/qa/smtp-sink.mjs`) + orquestrador (`scripts/qa/rehearse-smtp-transport.ps1`) que roda o **worker real** contra banco dedicado com cenário sintético (obrigação contratual com lembrete + canal e-mail habilitado): fase positiva (mensagem **aceita pelo sink** com `.eml` validado — destinatário, remetente, `charset=utf-8`, corpo base64 decodificado com o link da plataforma e acentos intactos, outbox `sent`, in-app criada) e fase negativa (MTA parado → falha **controlada** com código de erro, sem perda, in-app continua). Ajuste de produto exposto pelo ensaio: `SmtpNotificationTransport` passou a usar `BodyEncoding=UTF8` (o pipeline US-ASCII padrão trocava acentos por `?`). Transcript versionado `evidence/d44-smtp-transport-rodada-final.txt` (**17/17, EXIT=0**).
- **Auditoria de design (d41)**: **não reexecutada de propósito** — o diff da sprint não toca `src/Odca.Web`, catálogos i18n nem assets; as superfícies auditadas são byte-idênticas às da S9 (`AUDIT-DONE checks=204 ok=204 overflow=0`, transcript `evidence/d41-design-audit-s9-rodada-final.txt`).
- **Limitações registradas**: provedor SMTP real segue decisão externa (o mecanismo está comprovado contra MTA local); a janela de retenção legal de backups permanece "a definir" na RETENTION_POLICY (cronograma diário/30 dias proposto para aprovação, não definido); deploy físico multi-instância e backup offsite de produção seguem como itens de infra externos.
- **Separação de evidências**: teste local (GATE completo acima + transcripts versionados dos dois ensaios) · **CI não verificada** (branch sem push; nenhum ID de execução inventado) · **aceite humano pendente** (decisão de canal e-mail vs. sino, provedor, infraestrutura de backup de produção) · `.odca/progress.json` **inalterado** em 40% (nenhum marco inteiramente concluído nesta sprint; baseline/IDs/pesos intactos).
- **Próximos passos**: S11 = D-OC45 (dress rehearsal + go/no-go para 11/01/2027). Bloqueios externos registrados e mantidos: CI oficial nunca verde publicada, fornecedor de assinatura "proposto, não escolhido", SMTP real ainda decisão externa, deploy físico multi-instância.

## Incremento: Sprint 9 — impersonação profunda na janela de suporte (D-OC43) (18/10/2026)

- **Base**: `codex/s00-foundation` @ `e3df1d9` (Sprint 8 / D-OC42). Nova migração **v061** (única do incremento): tabela `odca.support_impersonations` + índice parcial único de ativa por janela + funções SECURITY DEFINER `start/end_support_impersonation`, `support_impersonation_is_valid`, `support_session_members` + eventos `impersonation_started/ended/revoked`; checksum **`5e1e107223fcd55afb01025d214ffef61d154b1309685604e93c983ef3c0808e`**, blocos 1–60 byte-imutáveis, release `odca-v061.sql` byte-idêntica ao canônico. GATE sobre a árvore final: builds Release+Debug estritos **0W/0E**, Domain **389/389**, suíte de integração `-FreshDb` **234/234 TEST-EXIT=0** (baseline 231 + 3 fatos novos; aplica v061 no bootstrap), assets **exit=0**; catálogos **2727 chaves/cultura, paridade 4/4** (+19 via delta versionado `d-s9-support-imp.json`). Commit único ao final, **sem push**.
- **F-1 (núcleo de autorização)**: o superadministrador entra na visão exata de um usuário específico da organização, dentro de uma sessão de suporte ativa. Token da visão = JWT do **usuário-alvo** em sessão dedicada (`sub`/`sid`/nível MFA) + claims `imp_op`/`imp_grant`; revalidação da concessão **a cada request** no middleware da API (grant encerrado/expirado ou janela fora do ar → 401 `api.impersonation.expired`); duração 5–60 min e **nunca além do fim da janela** (`LEAST`); alvo exige membership ativa, sem troca de senha pendente e não excluído; uma concessão ativa por janela (concorrência → 409); revogação instantânea (o fim revoga a sessão dedicada do alvo, matando tokens em uso via `sid`); endpoints `GET {id}/members`, `POST {id}/impersonation` (RecentMfa) e `POST {id}/impersonation/{grantId}/end` (204; ator resolvido por `imp_op` ?? `sub`).
- **F-2 (BFF/portal)**: botão "Ver como cliente" nas janelas ativas; página de seleção de membro + duração (5/10/15/30/60); re-signin que troca o cookie pela identidade do alvo preservando o token original do operador em auth property; **banner global** em todas as páginas ("Você está agindo como {alvo}… expira às {t}" + botão "Sair da visão do cliente", `role=status`); Sair encerra a concessão (se ainda ativa) e restaura a identidade/token do operador decodificando seu JWT original (fallback: login).
- **Auditoria de design (regressão)**: `scripts/qa/audit-d41-design.ps1` em navegador real sobre o banco descartável — as mesmas 17 rotas × idiomas × 4 viewports da S8 → **`AUDIT-DONE checks=204 ok=204 · overflow=0 · authfail=0 · httpfail=0` (exit=0)**, 0 PNGs de falha; transcript em `evidence/d41-design-audit-s9-rodada-final.txt`; nenhuma correção de produto nesta rodada.
- **Limitações registradas**: durante a visão o operador opera com as permissões do alvo (áreas de plataforma exigem sair antes); o encerrar da concessão não exige MFA recente por decisão (saída robusta após janelas longas); a superfície interativa da visão não é rota fixa da auditoria de overflow (coberta pelos fatos e2e de HTTP); smoke SQL registrou uma correção real pré-GATE (colunas que shadowavam variáveis PL/pgSQL renomeadas para `imp_grant/imp_target_session/imp_expires`, bloco re-hashado).
- **Separação de evidências**: teste local (GATE acima + smoke SQL v061 no banco descartável) · navegador (regressão d41) · **CI não verificada** (branch sem push; nenhum ID de execução inventado) · **aceite humano pendente** (fluxo entrar/banner/sair + idiomas das superfícies S7/S8) · `.odca/progress.json` **inalterado** em 40% (nenhum marco inteiramente concluído nesta sprint; baseline/IDs/pesos intactos).
- **Próximos passos**: S10 = D-OC44 (backup real + SMTP); S11 = D-OC45 (dress rehearsal + go/no-go para 11/01/2027). Bloqueios externos registrados e mantidos: CI oficial nunca verde publicada, fornecedor de assinatura "proposto, não escolhido", SMTP real indefinido, backup/restore ainda não ensaiado.

## Incremento: Sprint 8 — superfícies de plataforma/operacional em 4 idiomas (D-OC42) (18/10/2026)

- **Base**: `codex/s00-foundation` @ `68a13b3` (Sprint 7 / D-OC41). Sem nova migração neste incremento (checksum canônico v060 intacto). GATE sobre a árvore final: builds Release+Debug estritos **0W/0E**, Domain **389/389**, assets exit=0, suíte de integração `-FreshDb` **231/231 TEST-EXIT=0**; catálogos **2708 chaves/cultura, paridade 4/4** (+547 sobre o baseline 2161: deltas versionados d-s8-admin 131, d-s8-support 38, d-s8-platform 61, d-s8-contracts 239, d-s8-contracts-ctl 59, d-s8-contracts-index 18 + correção direta da chave `obligacoes`). Commit único ao final, **sem push**.
- **F-1 (ficha do contrato)**: `Contracts/Sheet.cshtml` integral em 4 idiomas — jornada contínua, próxima ação, propostas/aditivos com janela de renovação, revisão por versão, comentários (resolver/reabrir), obrigações com cinco ações e conflito 409, minutas oficiais, documentos/quarentena, derivados, solicitações e histórico com drawer JS (`bag` `drawerI18n`, `toLocaleString(culture)`); helpers localizados nos `@functions` (`StatusLabel`, `EventLabel`, `ObligationUrgency/ObligationStatus/SolStatus/SolService/SolPriority/DocState`). `Contracts/Print.cshtml`: `lang` via middleware, watermarks assinado/rascunho, barra de versão. `Contracts/Index.cshtml`: lista "Meus contratos" (filtro, colunas + `data-label`, situação de revisão, paginação, empty state). `ContractsController`: ~60 mensagens BFF para `sheet.ct_*` e `OfficialTemplateNames` substituído por `HashSet<string> OfficialTemplateKeys` (resolução exclusivamente por KEY; títulos vêm do catálogo do tenant).
- **F-2 (fila administrativa de atendimento)**: `AdminSolicitacoes/{Index,Aprovacoes,Detalhes}.cshtml` + controller em 4 idiomas; namespace novo `admin.*`.
- **F-3 (sessões de suporte da plataforma)**: `SupportSessions/Index.cshtml` + controller em 4 idiomas; namespace novo `support.*`.
- **F-4 (notificações da plataforma + agenda mensal)**: `PlatformNotifications/Index.cshtml` + controller (namespace novo `platform.*`) e `Agenda/Index.cshtml` + controller (reuso `common.*`) em 4 idiomas; padrão canônico (CultureMiddleware + `L()`) injetado nos controllers faltantes.
- **Auditoria de design estendida**: `scripts/qa/audit-d41-design.ps1` ampliado 13 → 17 rotas — contratos/ficha do contrato/agenda em 4 idiomas (cliente) e notificações da plataforma pt-BR (superadmin); ficha audita o contrato fixo da prova do D-OC37 no banco descartável → **`AUDIT-DONE checks=204 ok=204 · overflow=0 · authfail=0 · httpfail=0` (exit=0)**, 0 PNGs de falha; transcript em `evidence/d41-design-audit-s8-rodada-final.txt`; nenhuma correção de produto nesta rodada.
- **Limitações registradas**: conteúdo (título/corpo) das notificações permanece pt-BR no armazenamento (dívida); botão "Agenda mensal" na central de obrigações segue disabled; `ModulesController` fica para a próxima sprint; labels pt hardcoded em `ContractSheetRepository` (Infrastructure) mantidos por decisão — a view mapeia `sol.*` pelos helpers localizados.
- **Separação de evidências**: teste local (GATE acima + varredura de literais pt) · navegador (auditoria de design) · **CI não verificada** (branch sem push; nenhum ID de execução inventado) · **aceite humano pendente** · `.odca/progress.json` **inalterado** em 40% (nenhum dos seis marcos inteiramente concluído nesta sprint; baseline/IDs/pesos intactos).
- **Próximos passos**: S9 = D-OC43 (impersonação profunda na janela de suporte); S10 = D-OC44 (backup real + SMTP); S11 = D-OC45 (dress rehearsal + go/no-go para 11/01/2027).

## Incremento: Sprint 7 — jornada do cliente do piloto em 4 idiomas e acessibilidade de navegação global (D-OC41) (18/10/2026)

- **Base**: `codex/s00-foundation` @ `d5759f8` (Sprint 6 / D-OC40). Sem nova migração neste incremento (checksum canônico v060 intacto). GATE sobre a árvore final: builds Release+Debug estritos **0W/0E**, Domain **389/389**, assets exit=0, suíte de integração `-FreshDb` **231/231 TEST-EXIT=0**; catálogos **2161 chaves/cultura, paridade 4/4** (+391 via deltas versionados). Commit único ao final, **sem push**.
- **F-1 (hub de documentos)**: `Documents/Index.cshtml`, `Documents/NewDocument.cshtml` e mensagens remanescentes do `DocumentsController` (títulos, `UserMessage` de falha de carga, permissão) passam ao catálogo `documents.*` — filtros, tabela, badges de estágio/minuta/revisão/PDF, próximas ações, paginação, empty state, jornada de criação em 5 etapas e busca/cadastro rápido de paciente; zero literal pt (verificado por varredura de caracteres acentuados).
- **F-2 (central de obrigações)**: namespace novo `obl.*` — `Obligations/Index.cshtml` (header, escopo meu/organização, vistas salvas com renomear/inativar, filtros, tabela, painel lateral, guia "como usar"), `Nova.cshtml` (recorrência mensal, lembretes 7/1/0, seletor de cláusula com fallback JS localizado), `Acoes.cshtml` (seis operações + conflito 409); `ObligationsController` ganha injeção de `LocalizationService` (padrão da casa) e todos os `TempData`/notificações/erros de campo viram chaves.
- **F-3 (central de privacidade)**: canal público anônimo em 4 idiomas (`priv.*`) — formulário, tipos de solicitação, confirmação com protocolo e falhas de rate-limit/timeout/indisponibilidade do `PrivacyController`.
- **F-4 (acessibilidade/design global)**: `NavItem.AriaKey` no mecanismo de navegação — aria-labels distintos do rótulo visível traduzidos nos 4 catálogos (`nav.aria_*`) resolvidos pelo localizer; aria-labels idênticos ao rótulo removidos (WCAG 2.5.3); `_Layout` localizado (sufixos de estado de funcionalidade, "Administração ODCA", "Escolha uma organização"); páginas compartilhadas (`Error` antes todo em inglês, `AccessDenied`, `ServiceUnavailable`, `_ConfirmDialog`, `_Toasts`) em 4 idiomas; `navigation.js` lê os rótulos expandir/recolher do bag `window.odcaNav` injetado pelo layout; fallbacks do `dialogs.js` herdam o texto renderizado pelo servidor.
- **Auditoria de design estendida**: `scripts/qa/audit-d41-design.ps1` (mesma base CDP do D-OC40, driver inalterado) — 13 rotas × idiomas × 360/768/1280/1440 = **152 medidas** (documentos/novo e obrigações/nova em 4 idiomas; home/solicitações/notificações/privacidade estendidas aos 4) → **152/152 OK · overflow=0 · authfail=0 · httpfail=0 (exit=0)**; transcript em `evidence/d41-design-audit-rodada-final.txt`; nenhuma correção de produto nesta rodada; áreas de plataforma seguem pt-BR (dívida S8).
- **Separação de evidências**: teste local (GATE acima) · navegador (auditoria de design em 4 idiomas) · **CI não verificada** (branch sem push; nenhum ID de execução inventado) · **aceite humano pendente** · `.odca/progress.json` **inalterado** (nenhum marco creditável sem CI oficial do SHA funcional + aceite real).
- **Próximos passos**:
  - Push da branch quando solicitado (passa a verificar a CI oficial dos SHAs).
  - Iniciar a Sprint 8 (D-OC42): levar a mesma operação i18n às superfícies de plataforma/operacional (ficha do contrato, fila administrativa de atendimento, sessões de suporte, agenda mensal, conteúdo das notificações).
  - Fechar pendências externas para a estreia 11/01/2027 (provedor de envelope, SMTP real, backup/restauração ensaiada).

## Incremento: Sprint 6 — candidata ao piloto: ensaio de piloto reproduzível, design das superfícies e matriz de prontidão (D-OC40) (18/10/2026)

- **Base**: `codex/s00-foundation` @ `196bcdc` (Sprint 5 / D-OC39). Sem nova migração neste incremento. GATE sobre a árvore final: builds Release+Debug estritos **0W/0E**, Domain **389/389**, assets exit=0, suíte de integração `-FreshDb` **231/231 TEST-EXIT=0**. Commit único ao final, **sem push**.
- **F-1 (piloto)**: ensaio de piloto reproduzível (`scripts/qa/run-pilot-rehearsal.ps1`) reexecuta, sobre a mesma base descartável, as provas ao vivo versionadas que definem a candidata — C-SLA, matriz de aceite comportamental, jornada documental A–N (D-OC34), assinatura com envelope (S3), atendimento/avisos/prazos (S4) e reautenticação/suporte (S5) — com transcript consolidado por estágio e provisionamento automático da stack dev (https :7143/:7144) para os estágios legados. Rodada final (08:28): **5/6 verdes no transcript consolidado** — C-SLA 98/0 · ACEITE 15/15 · D34 **61/61 checks** (892 s) · D38 fails=0 · D39 fails=0; o único fail (D37 em `P0b`) foi classificado **ambiente** (stall transitório do Postgres sob pressão de memória da máquina compartilhada) e limpo na reexecução pontual `-Only 'D37-ASSINATURA'` verde na mesma base → composto, **seis estágios verdes**; convergência de todas as runs do dia (com 3 bugs de harness corrigidos no caminho: poll sem `-w`, probe TLS legada e contador `$t` esmagando `$T`) está registrada em §4–§5 do D-OC40. Evidências: `evidence/d40-rehearsal.txt` + `d40-rehearsal-d37-rerun-0905.txt` + transcript por estágio + runs anteriores arquivadas.
- **F-2 (design)**: auditoria de design das superfícies do piloto em navegador real (`audit-d40-design.ps1` + `cdp-design-audit.mjs`): 10 rotas × idiomas × 360/768/1280/1440 medindo **overflow horizontal**, **autenticação quebrada** e **HTTP 4xx/5xx** por medida, com PNG apenas das falhas para triagem visual. A primeira execução foi invalidada por bug do próprio harness (contador `$t` esmagando o tenant `$T` → rotas organizacionais 404 mascaradas como "overflow" — run arquivada e causa registrada em §5) e a rodada final veio com o harness corrigido: **72/72 medidas OK — overflow=0 · authfail=0 · httpfail=0 (exit=0)** (nenhuma correção de produto necessária; limitação registrada: áreas pt-first auditadas só em pt-BR).
- **F-3 (matriz)**: **matriz de prontidão do piloto** (§6 do D-OC40) mapeando os 6 marcos do portal (critério → implementação → evidência → responsável → pendência) e **runbook do piloto** (§7): pré-condições (incluindo backup/restore real como pré-condição explícita), contas sintéticas pelos provisioners versionados, cenários por perfil, suporte via sessões de plataforma auditadas (S5), monitoramento/rollback e critério de saída.
- **Separação de evidências**: teste local (GATE acima) · prova ao vivo (ensaio consolidado + provas individuais versionadas) · navegador (auditoria de design) · **CI não verificada** (branch sem push; nenhum ID de execução inventado) · **aceite humano pendente** · `.odca/progress.json` **inalterado** (nenhum marco creditável sem CI oficial do SHA funcional + aceite real).
- **Próximos passos**:
  - Push da branch quando solicitado (passa a verificar a CI oficial dos SHAs).
  - Fechar pendências externas para a estreia 11/01/2027: provedor de envelope externo, SMTP real, backup/restauração real ensaiado, usuários convidados e aprovação da versão (§6/§7 do D-OC40).

## Incremento: Sprint 5 — segurança e operacional: reautenticação MFA recente para operações sensíveis, sessões temporárias de suporte de plataforma com ator real e purga de retenção documental, com prova ao vivo fails=0 (D-OC39) (19/10/2026)

- **Base**: `codex/s00-foundation` @ `ebbfbe1` (Sprint 4 / D-OC38). Migração **v060** (`extend_support_session` + `support_session_tenant_name`; release byte-idêntica ao canônico). **Defeito de produto descoberto e corrigido**: `odca.tenants` tem RLS FORCADA (`tenant_isolation`) — o JOIN direto do repositório via `odca_app` retornava zero linhas e o `open` respondia 201 sem nome da organização; leituras de plataforma agora passam pela função governada `support_session_tenant_name` (SECURITY DEFINER) com `set_config('odca.user_id')` em transação. GATE sobre a árvore final: builds Release+Debug estritos **0W/0E**, Domain **389/389** (baseline 367 + 22 novos), assets exit=0, suíte de integração `-FreshDb` **231/231 TEST-EXIT=0** (baseline S4 222 + 8 fatos dos endpoints de sessão de suporte + 1 fato de checksum da v060); catálogos **1770 chaves/cultura, paridade 4/4**. Commit único ao final, **sem push**.
- **F-1 (avanço)**: reautenticação MFA **recente** para operações sensíveis — claims `mfa_enrolled`/`mfa_verified_at` no token de sessão, política `RecentMfa` com janela `Identity:RecentAuthMinutes` (padrão 15, explícito nos appsettings API e Web); handler isenta não-superadmin (sem 2F nesta plataforma), troca forçada de senha (precede o MFA, sem ciclo) e pré-inscrição; aplicada em `change-password`/`mfa/enrollment`/`regenerate` + 9 escritas administrativas (membros, papéis, permissões, transferência) + escritas de suporte. API: **403 puro** com código estável `api.auth.recent_auth_required`; Web: guard `RecentAuthGuard` → **302 para `/mfa/desafio?returnUrl=`** preservando o destino.
- **F-2 (avanço)**: sessões temporárias de suporte de plataforma ponta a ponta — API `/api/v1/platform/support-sessions` (list/get/events/open/extend/end/revoke; 409 `active_exists` por unicidade ativa admin+organização; expiração virtual sem reaper; extend avança por `GREATEST(now(), expires_at)` — nunca retrocede; `end` dono/admin, `revoke` qualquer superadmin; eventos `started/extended/ended/revoked` + auditoria `superadmin.support_session.*` com **ator real**); Web BFF `/administracao/suporte-sessoes` pt-first (formulários, anti-forgery, feedback `TempData`), item de navegação nos 4 catálogos (**+5 chaves/cultura**). Fronteira honesta registrada: janela auditável — **impersonação profunda permanece dívida**.
- **A-1 (avanço)**: purga física da área reconciliada no worker por idade de escrita (`Documents:ReconciledPurgeRetentionDays`, padrão 30 dias) — seletores estáticos testáveis, falha isolada por arquivo sem expor caminho.
- **Prova ao vivo**: `scripts/qa/validate-d39-s5-web.ps1` sobre base descartável (janela determinística `Identity__RecentAuthMinutes=1`) — rodada final **fails=0 (22/22 executados; 23 definidos, P0a só em base sem v060)**; convergência 2→0 registrada (`evidence/d39-proof.txt`); falhas intermediárias 100% classificadas como harness (corpo do POST 302 sem `-L`; método inexistente no PS 5.1) — **zero falha de produto**.
- **Capturas CDP**: `scripts/qa/capture-d39-screens.ps1` — 1 tela × pt-BR × 360/768/1280/1440 + zoom 200% = **ok=5 fail=0** (PNGs em `%TEMP%\opencode\evidence\captures-d39\`, fora do repo por tamanho). Limitação registrada: área pt-first (dívida i18n).
- **Fora de escopo registrado**: impersonação profunda na janela de suporte; i18n da área de sessões de suporte (pt-first); SMTP real (herdado, e-mail desabilitado por padrão); deploy físico multi-instância (itens no BACKLOG).
- **Separação de evidências**: teste local (GATE acima) · prova ao vivo (script reproduzível) · navegador (capturas para revisão) · **CI não verificada** (branch sem push; nenhum ID de execução inventado) · **aceite humano pendente** · `.odca/progress.json` inalterado.
- **Próximos passos**:
  - Push da branch quando solicitado (passa a verificar a CI oficial dos SHAs).
  - Iniciar a Sprint 6 (piloto/design/matriz de prontidão) sobre este commit.

## Incremento: Sprint 4 — atendimento, avisos e prazos: sino in-app com deduplicação, i18n do atendimento e dos avisos, centro de obrigações e prova ao vivo fails=0 (D-OC38) (18/10/2026)

- **Base**: `codex/s00-foundation` @ `fe483da` (Sprint 3). Migração **v059** (`user_notifications.source_id` + índice único parcial — deduplicação determinística do sino; release byte-idêntica ao canônico). GATE sobre a árvore final após o defeito de view descoberto na prova: builds Release+Debug estritos **0W/0E**, Domain **367/367**, assets exit=0, suíte de integração `-FreshDb` **222/222 TEST-EXIT=0** (baseline S3 211 + 5 fatos do sino + 3 fatos dos endpoints de obrigação + 2 fatos do lease da outbox + 1 fato de checksum da v059); catálogos **1765 chaves/cultura, paridade 4/4**. Commit único ao final, **sem push**.
- **F-1 (correção)**: notificação in-app por evento de negócio que **nunca lança** (falha vira log 2403/2404), com dedup determinística por `source_id` compartilhado com o e-mail e guard de membership ativo; 8 pontos de chamada (solicitações, revisões, assinatura). **F-3 (triagem → NÃO-defeito)**: claims concorrentes da fila são atômicos (`FOR UPDATE SKIP LOCKED`), só re-pendem pending/leased expirado, conclusão exige o token exato do lease — evidência executável nova (2 fatos verdes), sem mudança de produto.
- **Defeito de produto descoberto e corrigido**: página de ações de obrigação dava 503 ("The view 'Actions' was not found") — `View(vm)` por convenção de action vs. arquivo `Acoes.cshtml` → `View("Acoes", vm)` explícito nos dois pontos; coberto pelos checks O5–O8 da prova.
- **F-2 (correção)**: atendimento (central/nova/detalhe de solicitações) e avisos (notificações + preferências) em 4 idiomas — **+143 chaves/cultura** (total 1765), cinco views reescritas no padrão canônico. Dívida registrada: conteúdo das notificações segue pt-BR.
- **A-1 (avanço)**: centro de obrigações no Web BFF — cadastro (única ou série mensal), leitura individual via API (nova rota GET), ações por status com versão otimista; conflito 409 **preserva os valores digitados** e atualiza a versão esperada. API continua autoridade (permissão/validação/versão/auditoria).
- **Prova ao vivo**: `scripts/qa/validate-d38-s4-web.ps1` sobre base descartável — rodada final **fails=0 (30/30 executados; 31 definidos, P0a só em base sem v059)**; convergência 10→2→0 registrada (`evidence/d38-proof.txt`/`d38-proof-fim.txt`); única falha de produto entre as rodadas = defeito de view acima (corrigida); demais falhas de harness classificadas na doc (BOM PS 5.1, asserções vs. texto real dos catálogos, seed idem-potente).
- **Capturas CDP**: `scripts/qa/capture-d38-screens.ps1` — 6 telas × 4 idiomas × 360/768/1280/1440 + zoom 200% = **ok=120 fail=0** (PNGs em `%TEMP%\opencode\evidence\captures-d38\`, fora do repo por tamanho).
- **Fora de escopo registrado**: fila admin de atendimento pt-first, privacidade e sessão de suporte (MFA), série "esta e futuras", métricas de dashboard, localização do conteúdo das notificações, SMTP real, botão "Agenda mensal" permanece disabled (itens no BACKLOG).
- **Separação de evidências**: teste local (GATE acima) · prova ao vivo (script reproduzível) · navegador (capturas para revisão) · **CI não verificada** (branch sem push; nenhum ID de execução inventado) · **aceite humano pendente** · `.odca/progress.json` inalterado.
- **Próximos passos**:
  - Push da branch quando solicitado (passa a verificar a CI oficial dos SHAs).
  - Iniciar a Sprint 5 sobre este commit.

## Incremento: Sprint 3 — assinatura: contrato plugável de provedor de envelope externo, consistência de status, superfície em 4 idiomas e prova ao vivo fails=0 (D-OC37) (17/10/2026)

- **Base**: `codex/s00-foundation` @ `528eff4` (Sprint 2). Migração **v058** (envelope externo + eventos, tenant-scoped com RLS; release byte-idêntica ao canônico). GATE reexecutado sobre a árvore final após correções descobertas na prova (execução inicial 210/210 invalidada — raciocínio na doc): builds Release+Debug estritos **0W/0E**, Domain **364/364**, assets exit=0, suíte de integração `-FreshDb` **211/211 TEST-EXIT=0** (baseline 202 + 8 fatos do envelope + 1 fato de checksum da v058); catálogos **1622 chaves/cultura, paridade 4/4**. Commit único ao final, **sem push**.
- **F-1 (correção)**: `signatureStatus` no detalhe da versão (`GET .../studio/versions/{id}`) agora **derivado da preparação real** (`not_started|preparing|confirmed`) e o readiness de preparação mantém/remove a advertência `integration.unavailable` conforme o transporte externo configurado. **F-3 (triagem → NÃO-defeito)**: advertência de readiness com provider `none` é fiel à decisão "proposto, não escolhido" e o envio falha explicitamente (`409 provider_unconfigured`) — comportamento documentado, sem mudança de código.
- **Defeito de produto descoberto e corrigido**: `GET versions/{id}` dava 500 com participante (materialização Dapper de registro público com parâmetro default) → `ParticipantReadRow` privado no controller + projeção `.Select()` (padrão da casa); cobertura por fato de integração e pela prova ao vivo.
- **F-2 (correção)**: superfície de assinatura em 4 idiomas — seção de assinatura da ficha (incl. `aria-label`), mensagens das ações assinar/vincular/lembrete no `ContractsController` (web) e rótulos de estágio em `Documents/Index`; **+89 chaves/cultura** (total 1622), cookie canônico `odca_lang`.
- **A-1 (avanço)**: contrato plugável `ISignatureEnvelopeProvider` (código `none` = decisão preservada; `local-file` = transporte de referência de drop), migração v058, endpoints send/sync/GET idempotentes (uma franquia consumida por envelope; replay = 0), reconciliador periódico cross-tenant no worker, 8 fatos de integração.
- **Prova ao vivo**: `scripts/qa/validate-d37-signature-web.ps1` sobre base descartável — rodada final **fails=0 (24 checks)**; convergência 8→4→3→0 registrada (`evidence/d37-proof.txt`/`d37-proof-fim.txt`); única falha de produto entre as rodadas = defeito Dapper acima (corrigida); demais falhas de harness classificadas na doc (rota da ficha, asserção de página inteira vs. seção, BOM PS 5.1).
- **Capturas CDP**: `scripts/qa/capture-d37-screens.ps1` — 1 tela × 4 idiomas × 360/768/1280/1440 + zoom 200% = **ok=20 fail=0** (PNGs em `%TEMP%\opencode\evidence\captures-d37\`, fora do repo por tamanho).
- **Fora de escopo registrado**: fornecedor real de envio indefinido (decisão no DECISIONS-LOG — "proposto, não escolhido"); restante da ficha do contrato e `aria-label`s da navegação global permanecem pt-first/dívida pré-S3 (itens no BACKLOG).
- **Separação de evidências**: teste local (GATE acima) · prova ao vivo (script reproduzível) · navegador (capturas para revisão) · **CI não verificada** (branch sem push; nenhum ID de execução inventado) · **aceite humano pendente** · `.odca/progress.json` inalterado.
- **Próximos passos**:
  - Push da branch quando solicitado (passa a verificar a CI oficial dos SHAs).
  - Iniciar a Sprint 4 (atendimento, avisos e prazos) sobre este commit.

## Incremento: Sprint 2 — área de paciente em 4 idiomas, prova ao vivo fails=0 e 60 capturas CDP (D-OC36) (17/10/2026)

- **Base**: `codex/s00-foundation` @ `158a907` (Sprint 1). GATE **único sobre a árvore final** (raciocínio registrado na doc: baseline S1 verificado na mesma sessão; mudanças aditivas, sem migração): builds Release+Debug estritos **0W/0E**, Domain **364/364**, assets verdes, suíte de integração `-FreshDb` **202/202 TEST-EXIT=0** (inclui paridade 4/4 dos catálogos com as 123 chaves `patient.*` novas — total 1533/cultura). Commit único ao final, **sem push**.
- **Correção (P1) — área de paciente em 4 idiomas**: views Index/Form/Details via `L.T`/`CulturedTime` (novo overload `Date(DateOnly)`), `PatientsController` reescrito (propriedade-shadow `Culture` no padrão `StudioController` + `L()` + validação manual pelo catálogo — atributos `[Required]`/`[EmailAddress]` retirados de `FullName`/`Email` para eliminar mensagens padrão em inglês duplicadas), 8 mensagens de paciente no `DocumentsController` e bloco de cadastro rápido em `NewDocument`. Chaves existentes reutilizadas antes de criar novas; caminho canônico de idioma = cookie `odca_lang`; datas localizadas de fato (prova `17/10/2026` pt vs `10/17/2026` en).
- **Fora de escopo registrado**: restante da área Documentos permanece **pt-first** (literals não-paciente) → item pendente no BACKLOG. Higiene documental: seção obsoleta "Estúdio de contratos" do BACKLOG remapeada com as entregas reais apontadas.
- **Prova ao vivo**: `scripts/qa/validate-d35-patient-i18n-web.ps1` sobre a base descartável — rodada final **fails=0** (27 checks; transcript `evidence/d35-proof-rodada-final.txt` + trace): telas nos 4 idiomas com asserções negativas pt-BR, criação real via web, validação server-side, cadastro rápido, ciclo inativar/restaurar. Falhas das rodadas 1–3 **100% classificadas como de harness** (CPF sintético c/ dígito inválido, regex capturando GUID do tenant, `.ps1` UTF-8 sem BOM sob PS 5.1, `Location` relativa) — **zero falhas de produto**.
- **Capturas CDP**: `scripts/qa/capture-d35-patient-screens.ps1` — 3 telas × 4 idiomas × 360/768/1280/1440 + zoom 200% = **ok=60 fail=0** (PNGs em `%TEMP%\opencode\evidence\captures-d35\`, fora do repo por tamanho). Driver `cdp-capture-v2.mjs` melhorado: sessões eagress opcionais quando ausentes do cfg (comportamento idêntico quando presentes).
- **Separação de evidências**: teste local (GATE acima) · prova ao vivo (script reproduzível) · navegador (capturas para revisão) · **CI não verificada** (branch sem push; nenhum ID de execução inventado) · **aceite humano pendente**.
- **Portal**: `.odca/progress.json` inalterado (nenhum marco credenciável neste incremento).
- **Próximos passos**:
  - Iniciar a Sprint 3 (assinatura) sobre este commit; bloqueio conhecido: fornecedor de envio externo indefinido (decisão registrada no D-OC34).
  - Push da branch quando o usuário pedir (a CI oficial passará a verificar os SHAs).

## Incremento: Sprint 1 — automação confiável e consolidação das provas ao vivo do D-OC34 (D-OC35) (17/10/2026)

- **Base**: `codex/s00-foundation` @ `27b19b6` (inclui o D-OC34, commitado em `aecfca7`). Builds estritos (Release+Debug, `-warnaserror`) **0W/0E**. Domain **364/364 PASS**. Suíte de integração via `scripts/qa/run-integration-tests.ps1 -FreshDb`: **202/202 PASS — zero falhas** (FRESH-DB-OK → BUILD-OK → TEST-EXIT=0; log `%TEMP%\opencode\d34-suite-freshdb-r2.log`). Paridade de catálogos **4/4** pelo gate determinístico `scripts/check-assets.mjs` (19 chaves i18n × 4 catálogos + 10 assets + round trips decimal/editor). Commit único ao final, **sem push**.
- **Correções (P0/P1) de confiabilidade da automação, antes dos avanços**: `studio-decimal.js` e `template-editor-document.js` desacoplados do `window` em tempo de avaliação de módulo (`t` resolvido no momento da chamada — módulos puros ficam importáveis em Node sem `window`, ordem de carga irrelevante); novo gate i18n no `check-assets.mjs` (extrai os `t('chave')` dos dois módulos e exige presença não vazia nos 4 catálogos, com strip de BOM). Zero mudança de comportamento de produto (mesmo wire/UI).
- **Reconfirmação dos gates locais na base**: builds estritos 0W/0E, Domain 364/364, suíte `-FreshDb` 202/202 (o número "201" do GATE do D-OC34 é histórico de 16/10 — a suíte cresceu +1 com o caso de arquivamento com motivo) e assets verdes.
- **Prova ao vivo A–N (D-OC34 §D) consolidada e reproduzível**: `scripts/qa/validate-d34-an-web.ps1` (purga + fixtures determinísticas por rodada, stack própria, marker `PROVA-FIM`). As 31 falhas da rodada 10 foram 100% classificadas por causa raiz (`evidence/d34-proof-an-rodada10-falhas-classificadas.md`) — **zero falhas de produto**: purga de tabelas sem `tenant_id`, quoting de argumento nativo PS 5.1 p/ psql, assembly base do `ZipArchiveMode`, dígito verificador CPF/CNPJ em campo Tipo 5, suíte paralela derrubando a prova. Convergência FINAL-3 (15) → FINAL-4 (12) → FINAL-5 (5) → **FINAL-6 fails=0**, com todos os segmentos A/W/F/B/J/X/T/N verdes: jornada de assinatura completa com revisão real + modalidade evidence (nenhum envelope, franquia intacta, versão `externally_signed`), escopo OCR honesto (25 páginas → 20 processadas, `truncated=true`, banco/API concordando), códigos de falha estáveis e distintos (`extraction-timeout` ≠ `pdf-no-text-layer` ≠ `extractor-not-configured`) e exatamente **uma** notificação por ato (assunto genérico fixo, dedup por `source_id` determinística, destinatário = criador, sem segredos no conteúdo).
- **Achados de fixture/prova documentados na doc (nenhum é defeito de produto)**: módulo de revisões habilitado só no enterprise v2 (fixture de plano determinística; ativação ≠ pagamento); contato obrigatório no participante de assinatura; canal de e-mail por tipo é opt-in (padrão `channel_email=false` deixa a outbox vazia sem erro); diretório do FilePickup vem da runtime config; atribuição de linha da outbox ao ato por `source_id` (payload protegido); anti-replay TOTP do MFA rejeita janela igual à confirmação da inscrição (retry em janela fresca).
- **Separação explícita dos tipos de evidência**: teste local (suítes acima) · prova ao vivo (script reproduzível) · prova em navegador (capturas CDP `scripts/qa/capture-d34-screens.ps1` + `cdp-capture-v2.mjs`) · **CI não verificada** (branch sem push oficial; nenhum ID de execução inventado) · **aceite humano pendente** (nenhum responsável ou aprovação inventada).
- **Fora de escopo / próximo backlog**: herança do D-OC34 (purga física pós-retenção dos órfãos, idiomas de OCR além de `por`, homologação real de envio externo, deploy físico multi-instância) + continuação do plano de seis sprints — S2 modelos/paciente/editor, S3 assinatura, S4 atendimento/avisos/prazos, S5 segurança/operacional, S6 piloto/design/matriz de prontidão. Sem push/merge/deploy automático.
- **Próximos passos**:
  - Iniciar a Sprint 2 (modelos/paciente/editor) sobre este commit.
  - Push da branch quando o usuário pedir (a CI oficial passará a verificar o SHA).

## Incremento: Piloto da jornada documental para a estreia — escopo OCR honesto, assinatura real sem fornecedor inventado e diagnóstico operacional (D-OC34) (16/10/2026)

- **Base**: `codex/s00-foundation` @ `8095bc8` (= D-OC33). Builds estritos (Release+Debug, `-warnaserror`) **0W/0E**. Domain **364/364 PASS**. Suíte de integração via `scripts/qa/run-integration-tests.ps1 -FreshDb`: **201/201 PASS — zero falhas** (novo fato de arquivamento c/ motivo em `TemplateCatalogPersistenceTests.cs` + checksum v057 estendido em `MigrationChecksumTests.cs`; FRESH-DB-OK → BUILD-OK → TEST-EXIT=0; log `%TEMP%\opencode\d34-suite-freshdb.log`). Commit único ao final, **sem push**.
- **Entrega**: `docs/execution/D-OC34_PILOTO_JORNADA_DOCUMENTAL_ESTREIA.md`. **F1**: plano fantasma `odca-pro` eliminado — catálogo único com os planos canônicos v2 (`basic`/`intermediate`/`enterprise`). **F2**: escopo OCR honesto — v057 estendido e ancorado (`extraction_results` +`pages_total`/`pages_processed`/`truncated` +`method_ck`; `contract_templates` +`required_activity_profile`/`archive_reason`; `odca.platform_queue_summary()`): análise parcial nunca aparece como integral (páginas processadas, limite, o que ficou fora, próxima ação), páginas brancas contam, docx/imagem não fabricam paginação, `pdfinfo` ausente declara total desconhecido, códigos de falha distintos e estáveis (`extraction-timeout` ≠ `extractor-failed` ≠ `pdf-no-text-layer`). **B.5**: arquivar modelo exige motivo (5..500) persistido; versões publicadas imutáveis; gate opcional por perfil de atuação (409 `draft.profile.template_mismatch`). **B.6/B.7**: vincular/desvincular paciente na minuta (row_version otimista, snapshot imutável, autofill só de campos não confirmados, evento `patient.linked`, botões explícitos de conflito) e cadastro rápido inline em novo documento. **B.8/B.9**: banner de modalidades reais de assinatura nas fichas (nenhum envelope externo é enviado; franquia não consumida) e próxima ação final "Concluído — disponível para download". **B.10**: assuntos SMTP genéricos e fixos por kind (sem dados livres) + novo `signature_completed` (destinatário = criador da preparação, dedup determinística preparação+versão, só outbox). **A.3**: diagnóstico operacional do superadministrador com estados em quatro níveis (`unavailable < configured < accessible < verified` + `not_applicable`), valores configurados nunca exibidos e resumo de filas da plataforma. Decisão registrada no DECISIONS-LOG: envelope externo de assinatura **"proposto, não escolhido"** para a estreia.
- **Provas ao vivo**: consolidadas em 17/10 na Sprint 1 (D-OC35) — prova A–N reproduzível `scripts/qa/validate-d34-an-web.ps1` contra a base descartável, rodada final **fails=0** (transcript em `evidence/d34-proof-an-rodada-final.txt`; convergência rodadas 10→6 com causas raiz em `evidence/d34-proof-an-rodada10-falhas-classificadas.md`), capturas CDP via `scripts/qa/capture-d34-screens.ps1`. Detalhe por item e separação teste local / prova ao vivo / navegador / CI (não verificada, sem push) / aceite humano (pendente) na doc de entrega (§D/§E).
- **E**: matriz de estreia na doc de entrega — cada item com evidência apontada, sem percentuais inventados.
- **Fora de escopo / próximo backlog**: purga física após a retenção dos órfãos (`reconciled/{yyyy-MM}/`, aberta desde o D-OC32); idiomas de OCR além de `por`; homologação real de envio externo (SMTP/envelope comercial); deploy físico multi-instância. Sem push/merge/deploy automático.
- **Próximos passos**:
  - Push da branch quando o usuário pedir.

## Incremento: OCR real para imagens rasterizadas e PDF escaneado + paginação nas sugestões (D-OC33) (16/10/2026)

- **Base**: `codex/s00-foundation` @ `8d802bd` (= D-OC32). Builds estritos (Release+Debug, `-warnaserror`) **0W/0E**. Domain **362/362 PASS** (+4 fatos em `DocumentContentTests.cs`). Suíte de integração zerada via `scripts/qa/run-integration-tests.ps1 -FreshDb`: **199/199 PASS — zero falhas** (+2 fatos novos em `FranchiseMeteringTests.cs`; FRESH-DB-OK → BUILD-OK → TEST-EXIT=0; log final `%TEMP%\opencode\evidence\d33-freshdb-final.log`). Commit único ao final, **sem push**.
- **Entrega**: `docs/execution/D-OC33_OCR_REAL_IMAGENS_PDF_ESCANEADO.md`. **A**: OCR real para PNG/JPEG — Tesseract com linguagem configurável `Documents:OcrLanguage` (padrão `por`; antes hardcoded) e semântica honesta de saída: página em branco gera texto vazio e job **`ready_for_review` com zero sugestões** (falha não é o caminho), exit 1 do Tesseract também é vazio; método persistido `ocr`; franquia `ocr_credit` 1 página por imagem via `CountOcrPages` existente. **B**: paginação nas sugestões — `DocumentContent.Suggest` divide por form feed (`\f`) e atribui `page_number` (1-based) **somente quando o texto tem separador** (DOCX/imagem única mantêm página nula, contrato da UI `studio.x_page` preservado); página branca entre separadores conta na numeração sem gerar sugestão fantasma. **C**: PDF escaneado — detecção `PdfHasUsableTextLayer` (< 20 caracteres úteis → digitalizado) + fallback `RenderScannedPagesToOcrText` (`pdftoppm -png -r <dpi> -f 1 -l <max>`, até `Documents:OcrMaxPages` 20, `Documents:OcrDpi` 300 clamp 72–600, texto final junta com `\n\f\n`, método vira `ocr`, franquia consome as páginas efetivamente renderizadas); sem renderer/OCR configurados → `NoPdfTextLayerException` (singleton) → `failure_code='pdf-no-text-layer'` estável, retratável até `max_attempts`. Sem migração, sem chave de catálogo, sem mudança de API/BFF/UI (executáveis externos no padrão do scanner).
- **Provas ao vivo**: **33/33 checks PASS** na rodada final (`%TEMP%\opencode\evidence\d33-ocr-a.txt`) — cadeias C1–C4 com upload real pelo painel web (PNG → 7 sugestões c/ evidência e referência exata lida pelo OCR; JPEG branco → 0 sugestões honesto; PDF 2 págs → `pdf-native` c/ `page_number` 1 e 2 por segmento; PDF só de imagem → fallback OCR das 2 págs c/ form feeds persistidos), apply ao rascunho c/ campo confirmado intocado (`already_confirmed`) e reversão completa, e fase negativa determinística com worker alternativo sem executáveis (mesmo binário, config sem as chaves): N1 `pdf-no-text-layer`, N2 `extractor-failed`, worker principal de volta. Smoke pré-codificação verde. Bugs registrados na doc: Poppler 25.07 removeu `-scale-y` (DPI por `-r`), wkhtmltopdf exige div de quebra explícita entre imagens, Tesseract em página branca = exit 0 + vazio.
- **E**: matriz de aceite de dez itens no documento — todos **aprovados** com evidência apontada; nenhum falhou/bloqueado/não executado nesta fatia.
- **Fora de escopo / próximo backlog**: purga física após a retenção dos órfãos (`reconciled/{yyyy-MM}/`) segue aberta desde o D-OC32; idiomas de OCR além de `por` (basta traineddata + `Documents:OcrLanguage`). Sem push/merge/deploy automático.
- **Próximos passos**:
  - Push da branch quando o usuário pedir.

## Incremento: Reupload idempotente por conteúdo + reconciliação de armazenamento + extração para o estúdio (D-OC32) (16/10/2026)

- **Base**: `codex/s00-foundation` @ `2af1c37` (= D-OC31). Builds estritos (Release+Debug, `-warnaserror`) **0W/0E**. Suíte de integração zerada via `scripts/qa/run-integration-tests.ps1 -FreshDb`: **197/197 PASS — zero falhas** (+6 casos: 5 novos em `DocumentReuploadAndDraftExtractionTests.cs` + 1 em `MigrationChecksumTests.cs`; log final `%TEMP%\opencode\evidence\d32-freshdb-final2.log`). Paridade de catálogos **4/4** (1391 chaves por idioma: 1353 + 38 novos). Commit único ao final, **sem push**.
- **Entrega**: `docs/execution/D-OC32_DOCUMENTOS_E_EXTRACAO.md`. **B.1**: reupload por hash de conteúdo — mesmo sha256 ao mesmo documento é replay honesto (200 `replayed=true`, mesma versão, sem cota dupla, reserva devolvida, evento `document_reuploaded`); 201 ganha `replayed=false`; dedup não adivinha documento novo. **B.2**: reconciliador — `odca.expire_storage_reservations(p_limit DEFAULT 500)` SECURITY DEFINER (v056) finaliza reservas vencidas em lote; órfãos (tmp de quarentena e finais sem referência) mais velhos que `Documents:ReconcileOrphanRetentionHours` (24h) são **movidos** para `reconciled/{yyyy-MM}/`, nunca excluídos; logs só com contagens. **B.3**: aplicar extração ao rascunho (`POST studio/drafts/{draftId}/apply-extraction`) com mapeamento FECHADO conceito→campo (`ExtractionDraftMapping`: match exato + alias auditável, monetário só com candidato único, documento de parte 11/14 dígitos e candidato único), valores com `source="extraction"`/`confirmed=false`, nunca sobre confirmado/calculado, row_version otimista (409 `draft.save.conflict` c/ review `conflicting`), idempotência por `odca.draft_extraction_reviews` (replay `repeated=true`; payload diferente 409 `idempotency.payload.conflict`), evento `draft.extraction_applied` e sugestões revisadas. **C**: painel Documentos no editor (proxies BFF ancorados no draftId — JS nunca envia contractId; antiforgery por header `RequestVerificationToken`; lista snake_case, upload multipart 200 novo/replay, extrair 202 só para versão `safe`, sugestões com evidência aceitar/editar/rejeitar, aplicação explícita); +38 chaves ×4 idiomas. **D**: release v056 (índice `(tenant_id,document_id,sha256)`, função de expiração, tabela de reviews c/ RLS+GRANTs, CurrentVersion 56, checksum canônico, prefixo 1..55 imutável).
- **Provas ao vivo**: B.1/B.2/B.3 pela API **46/46 PASS** (`%TEMP%\opencode\evidence\d32-b1b2b3.txt`) e cadeia BFF/Web **W1–W17 todos PASS** (`d32-web-c.txt`: login → aba → list/upload/replay → scan até `safe` → extrair 202 → `ready_for_review` → 7 sugestões c/ evidência → apply 200 v2→v3 com skips estáveis → reversão). Bugs registrados na doc: MVC puro executa a action com corpo nulo quando o parse falha (NRE mascarado em 405 pela reentrada do middleware de erro — remediado com 422 `studio.request_invalid`) e PS 5.1+curl raspa aspas duplas de argumentos (provas agora enviam JSON por `-d @file`).
- **E**: matriz de aceite de dez itens no documento — todos **aprovados** com evidência apontada; nenhum falhou/bloqueado/não executado nesta fatia.
- **Fora de escopo / próximo backlog**: OCR real para imagens/PDF escaneado (extração atual opera sobre texto), purga física após retenção (`reconciled/{yyyy-MM}` preserva). Sem push/merge/deploy automático.
- **Próximos passos**:
  - Push da branch quando o usuário pedir.

## Incremento: Operação documental integrada e preparação comercial (D-OC31) (15/10/2026)

- **Base**: `codex/s00-foundation` @ `b8206bb` (= D-OC30). Build estrito (Release+Debug) **0W/0E**. Suíte de integração zerada via `scripts/qa/run-integration-tests.ps1 -FreshDb`: **191/191 PASS — zero falhas** (FRESH-DB-OK → BUILD-OK → TEST-EXIT=0; log em `%TEMP%\opencode\d31-freshdb-run.log`). Paridade de catálogos **4/4** (1353 chaves por idioma: 1319 + 34 novos). Commit único ao final, **sem push**.
- **Entrega**: `docs/execution/D-OC31_OPERACAO_DOCUMENTAL_COMERCIAL.md`. GATE provado antes dos avanços dependentes (baseline 0W/0E, `-FreshDb` limpo, regressões críticas corrigidas — `42P10` no `ON CONFLICT(source_id)` e `42601` no `retry_notification` na **v055** sem reescrever a v054, regra canônica de planos reconciliada — base fresca **6/6/7**, gate 409 `solicitations.plan.required`, os quatro serviços diferenciados — e isolamento entre dois tenants em API + RLS).
- **B.1/B.2**: evento + outbox na mesma transação com retry/backoff/limite configurável, lease recuperável e reprocessamento sem duplicar confirmações; SMTP desativado até configuração válida (stub honesto `notification_provider_missing`); central do superadmin com retry auditado. Assinatura vinculada à versão exata com hash do PDF confirmado no servidor, evidência ligando documento do acervo, correção por evento complementar, idempotência/replay.
- **B.3/B.4/B.5** (prova integrada final **47/47 PASS**, `%TEMP%\opencode\evidence\d31-b345-final.txt`): jornada contínua na ficha (próxima ação priorizada, pendências ancoradas, bloqueio em arquivado); editor com busca/grupos/preservação pós-falha; restaurar versão = **nova revisão** com evento `draft.restored` e 409 `draft.save.conflict`; assinatura não altera vigência/obrigações; encerradas saem das projeções; aditivo = nova versão + vínculo `contract_change_applications` + obrigações preservadas sem cópia implícita; histórico consultável; ficha administrativa diferenciando situação administrativa/comercial (ativação ≠ pagamento recebido) com preview de mudança de plano e auditoria `organization.plan_changed` (`payment_recorded=false`).
- **A.4/D**: `PostgresDistributedCache` + chaves compartilhadas do Data Protection (**v054**) com seleção condicional (`Cache:Store`/`DataProtection:KeysStore` = `postgres` em produção; dev permanece memória/filesystem); prova duas instâncias **17/17** — login numa, sessão continua na outra, logout revoga nas duas. Detalhe net10: o repositório exige `AddKeyManagementOptions(o => o.XmlRepository = ...)` para as chaves não ficarem efêmeras.
- **C**: 7 telas × 4 idiomas × 360/768/1280/1440px + zoom 200% = **140 capturas full-page** headless (msedge CDP) das jornadas autenticadas em `%TEMP%\opencode\evidence\captures\`. Correção registrada: cookie `__Host-odca-session` exige `secure:true` no CDP `Network.setCookies` e login fresco por passo TOTP (replay protection).
- **E**: matriz de aceite de dez itens no documento — todos **aprovados** com evidência apontada; nenhum falhou/bloqueado/não executado nesta fatia.
- **Fora de escopo / próximo backlog**: decisão comercial de planos (regra canônica mantém-se até ela), OCR/upload, integração real de pagamento, credenciais SMTP reais, deploy físico multi-instância. Sem push/merge/deploy automático.
- **Próximos passos**:
  - Push da branch quando o usuário pedir.

## Incremento: Localização dos erros emitidos pelo framework — 429 rate limiting + 500 não tratado (D-OC30) (14/10/2026)

- **Base**: `codex/s00-foundation` @ `ea3ead3` (= D-OC29). Build da solution (`Odca.sln`) **0W/0E**. Domain **358/358 PASS**. Suíte de integração em `odca_test_integration` zerado via `scripts/qa/run-integration-tests.ps1 -FreshDb`: **186/186 PASS — zero falhas** (baseline 182 + 4 novos; FRESH-DB-OK → BUILD-OK → TEST-EXIT=0). Paridade de catálogos **4/4** (1319 chaves por idioma: 1317 + 2 `api.rate_limited.*`).
- **Entrega**: `docs/execution/D-OC30_LOCALIZACAO_ERROS_FRAMEWORK.md`. Último item do backlog: os textos gerados **pelo próprio framework**. **429**: o `RateLimitingMiddleware` do .NET 10 já emitia problem+json pela fábrica registrada (o `traceId` no corpo confirma que passa pelo hook do D-OC27), mas com title hardcoded `"Too Many Requests"` (en) nas 4 culturas e sem detalhe/código estável → o mesmo `CustomizeProblemDetails` do D-OC27 foi estendido para 429: título/detalhe por catálogo na cultura do usuário + `code = rate_limited` (convenção curta dos gates de transporte). **500**: caminho já instalado pelo D-OC27 (título localizado + `code = api.internal.error`) — fixado agora como contrato em teste, com novo endpoint diagnóstico `/api/v1/diagnostics/fault` (somente Development, anônimo, exceção não tratada determinística; ausente em Production/Testing).
- **Prova HTTP real em 4 idiomas** contra a API Development: 429 (5 logins consomem a janela `login` por IP + 4 rejeições nas 4 culturas, corpos completos na doc) e 500 (`GET /api/v1/diagnostics/fault` ×4 culturas com título localizado, `type`/`traceId` preservados).
- **Estabilidade de wire**: status, `Content-Type: application/problem+json`, `traceId`, códigos, políticas e particionamento por IP inalterados; 429 do framework sem `Retry-After` antes e depois (comportamento do middleware, fora de escopo).
- **Testes novos**: `FrameworkErrorLocalizationTests` ×4 casos (429 pt-BR/en-US assercionando title/detail/status/code; 500 pt-BR/en-US com host não-Testing + endpoint diagnóstico). Detalhe empírico registrado: `UseExceptionHandler()` sem argumento é a variante rethrow e o TestServer retorna a resposta completa quando a exceção é lançada após o início da resposta.
- **Fora de escopo / backlog zerado**: 413 do Kestrel (sem corpo) e 404/405 (sem texto de usuário) não têm texto para localizar. Sem push/merge/deploy automático.
- **Próximos passos**:
  - Push da branch quando o usuário pedir.

## Incremento: Dedup cosmética das regras `.studio-grid` em max-width:1100px (D-OC29) (14/10/2026)

- **Base**: `codex/s00-foundation` @ `76d2705` (= D-OC28). Build da solution (`Odca.sln`) **0W/0E**. Domain **358/358 PASS**. Suíte de integração em `odca_test_integration` zerado via `scripts/qa/run-integration-tests.ps1 -FreshDb`: **182/182 PASS — zero falhas** (FRESH-DB-OK → BUILD-OK → TEST-EXIT=0).
- **Entrega**: `docs/execution/D-OC29_DEDUP_STUDIO_GRID.md`. Item cosmético do backlog desde o D-OC24: o `site.css` declarava **dois** blocos `@media(max-width:1100px)` para o Estúdio (um por seção, o segundo vindo de `7d2f20d`) com `grid-template-columns:minmax(0,1fr)` do `.studio-grid` e `min-width:0` do `.paper` duplicados, e um conflito em `.studio-document` (`overflow-x:auto` vs. shorthand `overflow:visible` do bloco posterior) resolvido só pela ordem da cascata — o `overflow-x:auto` era **código morto**. Consolidação num **único** bloco na posição do segundo (posição obrigatória: o override `.reading-mode .studio-grid` precisa permanecer após a regra base da linha anterior, ou o modo leitura quebraria em ≤1100px), com comentário marcando o local do bloco removido. Valor vencedor de cada propriedade preservado exato.
- **Prova de equivalência renderizada**: harness CDP headless (msedge `--headless=new` + Node 24) com página-probe carregando a stylesheet real via query string → `getComputedStyle` de 7 grupos de seletores nas larguras exatas 1101/1100/900/768/600/360px (breakpoint contornado pelos dois lados) contra o CSS antigo (cópia pré-edito) e o novo → **COMPARISON=IDENTICAL (6 larguras × 2 css)**, zero diferença de pixel.
- **Fora de escopo**: textos 429/500 genéricos do framework (exclusão desde D-OC27). Sem push/merge/deploy automático.
- **Próximos passos**:
  - Push da branch quando o usuário pedir.
  - Backlog restante (único item): textos 429/500 do framework, se um dia entrar em escopo.

## Incremento: Localização total das respostas de sucesso + erros remanescentes (D-OC28) (14/10/2026)

- **Base**: `codex/s00-foundation` @ `006f185` (= D-OC27). Build da solution (`Odca.sln`) **0W/0E**. Domain **358/358 PASS**. Suíte de integração em `odca_test_integration` zerado via `scripts/qa/run-integration-tests.ps1 -FreshDb`: **182/182 PASS — zero falhas** (linha de base D-OC27: 178; +4 casos e2e de duas culturas). Paridade de catálogos **4/4** (1317 chaves por idioma: 1186 + 131 `api.*`).
- **Entrega**: `docs/execution/D-OC28_LOCALIZACAO_RESPOSTAS_API.md`. Todo texto de usuário PT em **respostas** da API sai pela cultura do usuário: (1) todos os payloads de sucesso/informativos (~90 textos distintos em 12 controllers + Program) e (2) os gaps de erro do D-OC27 (~25: JWT 401 ×2, ReleaseFeatureGate 503 title/detail, OrganizationFeatureGate 403 nos 8 códigos, ReviewRequests 400 datas, fallbacks genéricos dos envelopes SQL de Solicitations/PlatformOps). Sem nova infraestrutura: reaproveita `this.T(key,args)` / `ApiMessages.Localize` / `LocalizationService`+`ApiCulture.For` do D-OC27; literais emitidos por SQL são localizados por pós-processamento exato das linhas Dapper (queries inline intactas). Camada de armazenamento continua canônica pt-BR (`odca.sql` inalterado).
- **Estabilidade de wire**: códigos dos gates preservados (`release_feature_disabled`, `feature_check_failed`, `permission_denied`, `organization_suspended`, `membership_blocked`, `feature_blocked`, `plan_restricted`, `quota_exhausted`) — só title/detail localizados; `{name}` resolve `api.gate.feature_<código>` com fallback ao `DisplayName` canônico; conteúdo de usuário e justificativas `{reason}` passam como vieram. Regra da casa mantida: texto desconhecido volta como veio; pt-BR byte-idêntico à fonte.
- **Exclusões documentadas (valores de dados)**: sufixos `(cópia)`/`— cópia` de cópias geradas, fallbacks `"Documento"`/`"documento"` de nome de arquivo/título, fallback `'Documento gerado'` de contexto.
- **Prova HTTP real em 4 idiomas** contra a API Development: registro válido + confirmação de e-mail (202/200 com `message` localizado) e problem 503 do gate de liberação (title/detail localizados + `code` estável) em pt-BR/en-US/fr-FR/es-ES (corpos completos na doc). Nota operacional: `POST /onboarding/registrations` tem a política `privacy-intake` (5 req/10 min/IP) — a primeira captura bateu em 429 no meio da janela (probes somadas); restart da API zerou o limitador em memória e as 4 culturas completaram em uma passada.
- **Fora de escopo**: textos de 429/500 genéricos do framework (comportamento padrão desde D-OC27); dedup cosmética das regras `.studio-grid` em max-width:1100px (backlog). Sem push/merge/deploy automático.
- **Próximos passos**:
  - Push da branch quando o usuário pedir.
  - Backlog cosmético: dedup das regras duplicadas de `.studio-grid`.

## Incremento: Localização total da API (D-OC27) (14/10/2026)

- **Base**: `codex/s00-foundation` @ `9eda889` (= D-OC26). Build da solution (`Odca.sln`) **0W/0E**. Domain **358/358 PASS** (355 de base + 3 novos de cultura BFF→API). Suíte de integração em `odca_test_integration` zerado via `scripts/qa/run-integration-tests.ps1 -FreshDb`: **178/178 PASS — zero falhas** (linha de base D-OC26: 173; +3 fatos de cobertura sem DB + 2 e2e de duas culturas). Paridade de catálogos **4/4** (1186 chaves por idioma: 749 BFF + 437 `api.*`).
- **Entrega**: `docs/execution/D-OC27_LOCALIZACAO_API.md`. Toda mensagem de erro da API sai no idioma do usuário com **código estável no wire** e texto resolvido por catálogo; a cultura viaja BFF→API no header `X-Odca-Culture`. Os 27 controllers da API + o handler de 500 foram varridos. A camada SQL permanece **canônica em pt-BR** (`odca.sql` inalterado; os 37 envelopes jsonb são mapeados `(código, mensagem)` → chave apenas na fronteira da API). Regra da casa mantida: **texto desconhecido volta como veio** — nada inventado.
- **Três defeitos reais achados pela cobertura nova (e corrigidos)**:
  - **Regex malformado em `ApiMessages.TemplatePatterns`** (`api.studio.formula_terms_only_calculated`): parêntese de fechamento sem escape → `TypeInitializationException` em todo caminho que tocasse `ApiMessages`. Invisível ao build; corrigido com `\)` no padrão.
  - **Shadowing do extension `OdcaErrors.Conflict`** (27 sites em 9 controllers): `this.Conflict("api.…")` ligava ao método de instância `ControllerBase.Conflict(object?)`, vazando a **chave crua do catálogo** como body 409 — regressão de forma de wire vs. 9eda889. Corrigido qualificando `OdcaErrors.Conflict(this, "api.…")`.
  - **Invocação direta de controllers sem `RequestServices`**: a varredura passou a resolver `LocalizationService` via `HttpContext.RequestServices`, nula nos contextos hand-built de teste (×4 Domain + 4 arquivos de Integração). Correção: helper compartilhado `TestControllerContexts.Create(actorId, extraClaims)`. Detalhe: no .NET 10 `ProblemDetailsFactory` é **classe abstrata** (não interface) e `MvcProblemDetailsFactory` deixou de existir; o concreto público é `DefaultProblemDetailsFactory(IOptions<ApiBehaviorOptions>, IOptions<ProblemDetailsOptions>?)`.
- **Prova HTTP real em 4 idiomas**: mesmo registro inválido (`POST /api/v1/onboarding/registrations`) com `X-Odca-Culture` pt-BR/en-US/fr-FR/es-ES contra a API rodando → 400 em todos, `code = api.onboarding.registration_invalid` estável, título + erros de campo localizados (corpos completos na doc).
- **Fora de escopo (backlog)**: i18n das mensagens de sucesso/resposta além do Estúdio; dedup cosmética das regras `.studio-grid` em max-width:1100px. Sem push/merge/deploy automático.

## Incremento: Estabilização da suíte de integração (D-OC26) (13/10/2026)

- **Base**: `codex/s00-foundation` @ `6a32a34`. Build da solution (`Odca.sln`) **0W/0E**. Domain **355/355 PASS**. Suíte de integração em `odca_test_integration` zerado: **173/173 PASS — zero falhas** (linha de base herdada: 54F/116P/170; total 170→173 com +3 fatos novos). Runner reutilizável commitado: `scripts/qa/run-integration-tests.ps1 [-FreshDb]`.
- **Entrega**: `docs/execution/D-OC26_ESTABILIZACAO_INTEGRACAO.md`. Os 54F nunca haviam sido dissecados (banco de teste longevo + mascaramento por ordem de execução); em banco zerado as falhas colapsam em **5 causas raiz reais**, todas corrigidas e provadas:
  - **F1 — v051 saiu com dollar-quoting corrompido** (`DO … \\$;` / `AS \\$ … END \\$;`, sintaxe inválida): qualquer banco novo morria na migração 51. Canônico reparado in place com checksum novo **H = `1f8eaf7e802419d64ae1f45bc60b0031b7f600a9173db8cfb584a7c06241069a`**; `odca-v051.sql` imutável (pacote defeituoso preservado para auditoria); `odca-v052.sql` regenerada byte-idêntica ao canônico pós-reparo; migrator ganha `KnownDefectivePackage` (`d9951f97… → H`) com reafirmação idempotente (DROP fn 7-args / CREATE OR REPLACE 8-args fiel ao corpo publicado + restamp). Prova comportamental: disposable com linha 51 presa em `d9951f97…` reconciliado no `Bootstrap migrate` → restamp para H, função 8-args íntegra, `/health/ready` 503→200 sem restart.
  - **F2** — connection string vazia/caixa gerava ANE do Npgsql na materialização de hosted service antes da mensagem amigável → guard `IsNullOrWhiteSpace` na factory DI.
  - **F3** — `ApiHostStartupTests` partia de DB hardcodado (`odca_test_startup`) → hermetizado com o `DatabaseFixture` (mesmo contrato `ODCA_TEST_*`).
  - **F4** — pins pré-v038 de catálogos (7/7/7 módulos): v038 tornou `module.reviews` exclusivo do Enterprise; decisão de produto mantida (canônico = 6/6/7) → pins corrigidos com comentário citando o v038 (`S00FlowTests`, `HierarchicalAdministrationTests`).
  - **F5 — seeding do catálogo oficial esbarrava em RLS** (42501) só em banco novo: policy `platform_security_insert` admitia apenas `action LIKE 'identity.%'` e o seeder grava `'template.official_catalog_seeded'` como app-role (5 retries → boot falha). Migração **v053** estende a policy (checksum **K = `6aac915a94439996b51ae39c6855bc8bad52aaaffd589b8e6082d773cdc3d4a4`**), `CurrentVersion` 53, release `odca-v053.sql` byte-idêntica; alternativas rejeitadas documentadas (renomear action sob `identity.%`; wrapper definer).
- **Gates**: build **0W/0E** · Domain **355/355** · Integração **173/173** em banco zerado via runner commitado (`FRESH-DB-OK → BUILD-OK → TEST-EXIT=0`) · disposable reconciliado (v051=H, v053 aplicada, policy estendida visível em `pg_policies`).
- **Próximos passos**:
  - Extensão natural (escolha do usuário): localização server-side da API (erros em PT hoje; códigos estáveis) + bag i18n além do Estúdio.
  - Backlog cosmético: dedup das regras duplicadas de `.studio-grid` em max-width:1100px.
  - Sem push/merge/deploy automático.

## Incremento: Bloco F — homologação visual de PDFs (D-OC25) (13/10/2026)

- **Base**: `codex/s00-foundation` @ `08d14d5`. Build da solution (`Odca.sln`) **0W/0E**. Domain **355/355 PASS** (353 + 2 novos: separador final de tabela e quebra de token longo). Suíte de integração: **54 falhas / 116 aprovados / 170 — idêntica à linha de base ambiental** (zero nova regressão).
- **Entrega**: `docs/execution/D-OC25_HOMOLOGACAO_PDF.md`. Homologação visual da camada de impressão pelos caminhos reais (login+MFA na API, minuta rica criada/salva/generada via endpoints, PDF baixado e inspecionado página a página via Ghostscript, overflow medido por pixels): **aprovada com 5 defeitos reais corrigidos** — (1) "Versao" sem acento e separador divergente na capa do preview; (2) título em maiúsculas só na capa (corpo/preview no caso autoral); (3) "SUMARIO" sem acento; (4) URL longa de 108 chars estourando a margem direita (medido x=1133 > margem 1127) → `Wrap()` divide tokens indivisíveis; (5) pipe fantasma após a última cela de cada linha de tabela. Renderer `odca-pdf-2` → **`odca-pdf-3`** (convenção D-A4: saída mudou ⇒ versão nova; artefatos antigos imutáveis e baixáveis). Integridade revalidada pós-fix: sha256 do download == banco, replay idêntico em bytes, `/Producer` certo. Achados operacionais documentados (413 de cota ao regenerar sem creditar o movimento `pdf:<versionId>`; gs no Windows exige barra direta em `-sOutputFile`).
- **Bloco F encerrado** (QA+MFA, decisões versionadas/hashes, papéis/assinantes, formatos canônicos, i18n fim-a-fim, responsivo do sistema inteiro e agora a homologação visual de PDF). Próximos passos:
  - Extensão natural: localização server-side da API (erros em PT hoje; códigos de erro estáveis) + bag para áreas fora do Estúdio.
  - Backlog separado: incremento dedicado às 54 falhas pré-existentes da suíte de integração; dedup cosmética das regras duplicadas de `.studio-grid` em max-width:1100px.
  - Sem push/merge/deploy automático.

## Incremento: Bloco F — responsivo 360/768/1440px do sistema inteiro (D-OC24) (13/10/2026)

- **Base**: `codex/s00-foundation` @ `598413e`. Build da solution (`Odca.sln`) **0W/0E**. Domain **353/353 PASS** · paridade de catálogos **4/4 PASS**. Suíte de integração: **54 falhas / 116 aprovados / 170 — idêntica à linha de base ambiental** (zero nova regressão).
- **Entrega**: `docs/execution/D-OC24_RESPONSIVO_SISTEMA.md`. Matriz do sistema inteiro — **57 páginas reais** (Estúdio ×11, organizacional ×22, conta ×5, plataforma ×10, públicas ×3 na sessão + 4 anônimas sem sessão) × viewports exatos 360/768/1440px = **171 combinações** com o mesmo harness CDP do D-OC23 (msedge headless, sessão MFA real, screenshots full-page): **171/171 `overflow=0px · elements=0 · jsErrors=0` → RESPONSE-CLEAN**.
- **4 causas raiz de layout corrigidas** (combos quebrados na pré-auditoria: ficha@360 +153px; central-solicitações @360/@768 +461/+53px; atendimento ODCA @360/@768 +609/+201px; aprovações @360 +213px; documentos @360/@768 +528/+356px; `/cliente`@360 +207px): (1) track `1fr` de `.renewal-row` inchada por token SHA-256 inquebrável (medido 471.625px num container de 305px) → `min-width:0` nos filhos + `overflow-wrap`; (2) o padrão cards ≤768 esconde o `thead` com absolute+clip, mas a tabela anônima shrink-to-fit dentro dele (ths ainda `table-cell`) inflamava a área rolável do documento — o overflow era exatamente borda-direita do thead − viewport → `position:relative` no scroller + thead 1×1px `overflow:hidden`; (3) classe `.table-responsive` usada em `Documents/Index` e `Onboarding/CustomerHome` **não existe no site.css** → substituída pela convenção `.table-scroll` (a página `/cliente` não era coberta antes → adicionada com `/primeiros-passos`); (4) `.studio-filter` sem wrap transbordando o form em 768px (9–11 campos) → `flex-wrap:wrap`.
- **Bug JS pré-existente corrigido**: o `_ValidationScriptsPartial` carregava jquery.validate/unobtrusive **sem que nada no app carregasse o jQuery core** → 2× `ReferenceError: jQuery is not defined` em toda carga de `/privacidade` (afetava também MFA ×2, registro e confirmação de e-mail). Adicionado `lib/jquery/dist/jquery.min.js` ao partial; jsErrors 2→0 em todas as larguras nas duas rodadas.
- **Lição de processo**: views Razor são pré-compiladas no assembly — edição de `.cshtml` exige `dotnet build` antes da verificação em runtime (na primeira re-roda só o CSS surtiu efeito).
- **Próximos passos**:
  - Bloco F restante: homologação visual de PDF.
  - Extensão natural: localização server-side da API (erros em PT hoje; códigos de erro estáveis) + áreas fora do Estúdio (bag e regra de prefixo já prontos).
  - Backlog separado: as 54 falhas pré-existentes da suíte de integração (ambiente/estado); dedup cosmética das regras duplicadas de `.studio-grid` em max-width:1100px.
  - Sem push/merge/deploy automático.

## Incremento: Bloco F — responsivo 360/768/1440px do Estúdio (D-OC23) (13/10/2026)

- **Base**: `codex/s00-foundation` @ `386edc2`. Build da solution **0W/0E**. Domain **353/353 PASS** · paridade de catálogos **4/4 PASS**. Suíte de integração: **54 falhas / 116 aprovados / 170 — idêntica à linha de base ambiental** (zero nova regressão).
- **Entrega**: `docs/execution/D-OC23_RESPONSIVO_ESTUDIO.md`. Matriz das 6 páginas do Estúdio × viewports exatos 360/768/1440px (18 combinações) auditada com harness próprio (msedge headless + CDP via Node 24, `Emulation.setDeviceMetricsOverride`, sessão MFA real, screenshots full-page): **18/18 `overflow=0px · elements=0 · jsErrors=0` → RESPONSE-CLEAN**.
- **4 defeitos de layout corrigidos**: (1) model-editar@360 53px — grids inline imunes a media queries + `min-width:auto` dos itens → classes `.form-grid.tpl-meta`/`.tpl-pair` com colapso ≤768 e `minmax(0,1fr)`; (2) version@360 56px — hash SHA-256 em `<code>` não quebrava → `overflow-wrap:anywhere`; (3) edit@1440 16px — coluna central `minmax(35rem,1fr)` excedia o disponível (~1072px) → `minmax(0,1fr)`; (4) edit@360 4px — margem negativa `clamp` (24px) ≠ padding do shell (20px) → `-1.25rem`.
- **Bug pré-existente descoberto e corrigido**: o bag inline `_I18nBag.cshtml` (Edit/TemplateEdit/Version) gerava JS quebrado em todas as cargas — o JSON saía HTML-encodado (`&quot;`) e no ambiente as referências **não são decodificadas dentro de `<script>`** (demonstrado por teste controlado), além de a IIFE ter syntax error próprio (string não terminada). `window.odcaI18n`/`OdcaI18n` nunca existiam → strings dinâmicos exibiam chaves cruas. Correção: `UnsafeRelaxedJsonEscaping` + `Html.Raw` (padrão canônico, robusto aos dois comportamentos de decodificação) + fix do tail da IIFE. Evidência pós-fix: `t('studio.preview_title')="Prévia do modelo"` na página real, bag sem entidades (45.599→31.318 chars pt-BR), jsErrors 12/18 → 0/18.
- **Próximos passos**:
  - Bloco F restante: homologação visual de PDF.
  - Extensão natural: localização server-side da API (erros em PT hoje; códigos de erro estáveis) + áreas fora do Estúdio (bag e regra de prefixo já prontos).
  - Backlog separado: as 54 falhas pré-existentes da suíte de integração (ambiente/estado).
  - Sem push/merge/deploy automático.

## Incremento: Bloco F — validação de formatos (data/moeda) do Estúdio (D-OC22) (13/10/2026)

- **Base**: `codex/s00-foundation` @ `0dbb640`. Build da solution **0W/0E**. Domain **353/353 PASS** (antes 302; +51 novos). Paridade de catálogos **4/4 PASS**. Suíte de integração em `odca_test_local`: **54 falhas / 116 aprovados / 170 — idêntica à linha de base ambiental** (zero nova regressão).
- **Entrega**: `docs/execution/D-OC22_FORMATOS_DATA_MOEDA.md`. Formatos canônicos inalterados (data ISO `yyyy-MM-dd` com dia real de calendário; moeda não-negativa até 2 casas; número assinado até 6 casas; entrada pt-BR/invariante normalizada); o que mudou foi a **cobertura, a exibição de metadados e o encadeamento das mensagens**:
  - **Matriz de testes** nova (`DateCurrencyValidationMatrixTests`, 51 fatos): datas bissextas/extremos, dias inexistentes (2026-02-31, 1900-02-29), formatos não-ISO, número (±, 6 casas, ambiguidade, leading zero), normalização pt-BR×invariante (`R$` sem espaço, NBSP, `+`, precisão), opcional malformado falha em Structural, mensagem do save com label do campo.
  - **Timestamps de UI seguem a língua ativa** (helper novo `Odca.Web.Common.CulturedTime` = `ShortDatePattern` da cultura + 24h; fallback pt-BR): Index/Edit/Version/CompareVersions + chrome do `studio.js` (savedAt/histórico/comentários via `window.OdcaI18n.culture`). Camada de documento (papel/PDF) segue canônica pt-BR — preview == saída (D-OC5). Bônus: `ToLocalTime()` removido no Edit (divergia das demais views fora de UTC).
  - **Mensagem por campo chega ao Estúdio**: `StudioController.Save` propaga `ValidationErrors` para `detail` (padrão do Generate) — antes o save devolvia `{title:null,detail:null}` e mostrava falha genérica. API: `JsonException` no save vira 400 `ValidationProblem` (antes 500 não mapeado).
  - **Evidência fim-a-fim na aplicação real** (Web `:7144`, login + MFA REV21A, catálogo oficial instalado pelo endpoint real, minuta real): data `2026-02-31` → 422 "O valor de 'Início' é inválido."; moeda `1.250` → 422 "…ambíguo…"; `1.250,50`+`2026-12-31` → 200 com `1250.50`/`2026-12-31` persistidos. Nas 4 línguas: chrome inverte dia/mês só em en-US (`13/10` ↔ `10/13`, fr/es em ordem dia/mes) e o papel em en-US manteve `1.234,56` (pt-BR canônico; `1,234.56` ausente). Evidências em `%TEMP%\sv-format-evidence\`.
- **Próximos passos**:
  - Bloco F restante: responsivo 360/768/1440px, homologação visual de PDF.
  - Extensão natural: localização server-side da API (erros em PT hoje; códigos de erro estáveis) + áreas fora do Estúdio (bag e regra de prefixo já prontos).
  - Backlog separado: as 54 falhas pré-existentes da suíte de integração (ambiente/estado).
  - Sem push/merge/deploy automático.

## Incremento: Bloco F — i18n fim-a-fim do Estúdio em 4 idiomas (D-OC21) (13/10/2026)

- **Base**: `codex/s00-foundation` @ `4b14f4a`. Build da solution **0W/0E**. Domain **302/302 PASS** · paridade de catálogos **4/4 PASS** (novo teste, sem banco). Suíte de integração em `odca_test_local`: **54 falhas / 116 aprovados / 170** — os 54 são a linha de base ambiental conhecida; os 116 = 112 prévios + 4 novos de paridade (zero nova regressão).
- **Entrega**: `docs/execution/D-OC21_I18N_ESTUDIO_FIM_A_FIM.md`. Área do Estúdio (6 views + 3 JS + `StudioController`) 100% localizada em pt-BR/en-US/fr-FR/es-ES:
  - Catálogos 223 → **749 chaves × 4 idiomas** (526 novas, todas `studio.*`/`common.*`), paridade exata de conjunto coberta por `LocalizationCatalogParityTests` (parse achatado não-vazio, sentinelas do bag por cultura, whitespace intencional preservado).
  - `LocalizationService`: `T(key, culture, params args)` + `Catalog(culture)`; novo partial `_I18nBag.cshtml` emite o bag inline por página (`window.odcaI18n`, filtrado por prefixo `studio.`/`common.`) — zero request extra; JS consome via alias `window.OdcaI18n.t` com fallback de identidade (páginas fora do escopo, ex.: `PlatformLibrary/NewRevision`, seguem em PT de forma consistente).
  - Conversão das 6 views (incluindo JS inline) e dos 3 scripts (`studio.js` ~50 strings, `studio-decimal.js`, `template-editor-document.js`); reuso de chaves existentes antes de criar novas; bugs de chave existentes corrigidos no caminho (`nav.templates`→`studio.nav_catalog`, h1 da comparação `action.filter`→`studio.cmpv_title`).
  - **`StudioController`**: ~50 strings visíveis ao usuário (títulos `ViewData["Title"]` que alimentam `<title>`/topbar, validações, conflitos de publicação, TempData de sucesso/falha, geração/PDF/preparação/revisão) localizadas via cultura da própria request (`CultureMiddleware.ItemsKey`) — sem isso o `<title>` vazava PT em todas as culturas.
  - Dados ≠ UI: conteúdo-semente documental do novo modelo e nomes/sufixos persistidos permanecem como conteúdo; sufixo "— cópia editável" aplicado e checado pela mesma chave traduzida.
  - **Evidência fim-a-fim na aplicação real** (Web `:7144`, login admin + MFA REV21A, cookie `odca_lang` trocado na sessão): para cada uma das 4 culturas, Index e Modelos/novo renderizam os bytes traduzidos esperados, as negativas PT estão ausentes em en/fr/es (o check en-US falhava antes da conversão do controller e passou depois) e o bag inline nasce com a cultura certa (pt 34 929 / en 30 562 / fr 37 336 / es 33 766 chars); evidências HTML em `%TEMP%\sv-i18n-evidence\`. Grep final: resíduo PT apenas em comentários de código e conteúdo-semente documental.
- **Próximos passos**:
  - ~~Bloco F restante: validação de formatos (data/moeda)~~ — entregue em D-OC22. Restante: responsivo 360/768/1440px, homologação visual de PDF.
  - Extensão natural: localização server-side da API (erros em PT hoje) + áreas fora do Estúdio (bag e regra de prefixo já prontos).
  - Backlog separado: as 54 falhas pré-existentes da suíte de integração (ambiente/estado).
  - Sem push/merge/deploy automático.

## Incremento: Correção do 500 nas saved-views da Caixa — migração v052 (13/10/2026)

- **Base**: `codex/s00-foundation` @ `dd62d67`. Build da solution **0W/0E**. Domain **302/302 PASS** · `MigrationChecksumTests` **36/36 PASS** (35 anteriores + 1 novo). Suíte de integração em `odca_test_local`: **54 falhas / 112 aprovados / 166 — idêntica à linha de base ambiental** (54/111/165) mais o novo teste aprovado; zero nova regressão.
- **Entrega**: `docs/execution/D-OC20_SAVED_VIEWS_INBOX_V052.md`. Fecha a descoberta do item 19 (D-OC18): salvar vista pessoal na Caixa devolvia 500 — o BFF envia `listingType="inbox"` mas a constraint canônica de `saved_work_views` só admitia `('obligations','reviews','contracts')` (por isso a tabela estava vazia em todos os tenants). Zero mudança de código de aplicação — a camada já era inbox-aware (`SavedViewFilterPolicy`); o gap era só do schema:
  - Migração **v052** (canônico + release byte-idêntica, padrão dos blocos v050/v051): DROP/ADD idempotente de `saved_work_views_listing_type_check` com os 4 valores + registro em `schema_migrations`; `DatabaseSchema.CurrentVersion` 51 → 52. DDL histórico não reescrito.
  - Aplicada pelo runner real (`Bootstrap migrate`); o health gate exigiu a nova versão (`ready` 503 → 200 após migrar, sem restart).
  - Evidência T1–T5 por HTTP real (apps reais, sessões MFA): **reprodução exata do defeito pelo formulário Web agora devolve 302** com `viewId` + aviso TempData renderizado; CRUD completo pela API (list/get/rename `row_version`→2/409 com versão obsoleta/`isDefault`); **vista padrão se aplica sozinha** (GET `/caixa` sem filtros → 302 OpenView); negativos: tipo inválido 400 e mesmo nome coexistindo entre `obligations`/`inbox` (uq já isola por `listing_type`); limpeza com zero resíduo.
  - Nota operacional: cada desafio MFA consome um código de recuperação — registrar "um código por fluxo" (a primeira execução da suíte falhou por reuso).
- **Próximos passos**:
  - Bloco F: i18n 4 idiomas fim-a-fim (homologação), validação de formatos (data/moeda), responsivo 360/768/1440px, homologação visual de PDF.
  - Backlog separado: as 54 falhas pré-existentes da suíte de integração (ambiente/estado).
  - Sem push/merge/deploy automático.

## Incremento: Bloco E item 20 — fila de exceções na Caixa Operacional (13/10/2026)

- **Base**: `codex/s00-foundation` @ `090439a`. Build da solution **0W/0E**. Domain **302/302 PASS** (incluindo 2 fatos novos). Suíte de integração em `odca_test_local`: **idêntica à linha de base** — 111 aprovados / 54 falhas pré-existentes de ambiente (zero nova regressão).
- **Entrega**: `docs/execution/D-OC19_FILA_DE_EXCECOES.md`. Comportamento provado contra a aplicação real (API + Web, sessões MFA, banco QA):
  - Novo kind **`Exception`** na Caixa Operacional — **sem migração, sem endpoint novo e sem permissão própria** (projeção de leitura; a visibilidade é decidida pela branch de origem com a porta do kind de origem): (a) change request em `conflict`/aplicação falhada → gate `tenant.renewals.read`, urgência `DueToday`, abre `…?from=caixa#renovacao`; (b) solicitação ativa com brecha de SLA registrada → gate `tenant.solicitations.read`, urgência `Overdue` (data da primeira brecha), abre `#solicitacoes` ou raiz do tenant quando sem contrato.
  - Regras: solicitações **com** brecha saem da lista nativa `Solicitation` e vivem só na fila (anti-duplicação); a agenda mensal não contém a fila (sentinel de window idêntico ao do kind `Signature`); vistas salvas aceitam `kind=Exception`; filtro/rótulos da UI nos 4 idiomas.
  - Evidência T1–T7 por HTTP real: baseline zerada → fixtures reversíveis → 2 itens com campos byte-exatos (urgências, âncoras, fallback órfão) → portes por dono/permissão (admin mine=2, operador mine=0, operador org-scope=2) → renderização web (label `Exceções · Atrasada`, âncora `#renovacao`, opção no filtro) → restore com fila de volta a zero.
- **Fora por critério pré-definido**: `notification_outbox.status='failed'` não alimenta a fila (sem dono nem permissão limpa) — registrada como fonte candidata futura.
- **Próximos passos**:
  - Correção do 500 das saved-views `inbox` (migração v052 ou alinhamento de contrato BFF/API) — defeito pré-existente descoberto no item 19.
  - Bloco F: i18n 4 idiomas (homologação fim-a-fim), validação de formatos (data/moeda), responsivo 360/768/1440px, homologação visual de PDF.
  - Backlog separado: as 54 falhas pré-existentes da suíte de integração (ambiente/estado).
  - Sem push/merge/deploy automático.

## Incremento: Bloco E item 19 — recuperação de divergência + expiração de sessão na Caixa/Ficha (12/10/2026)

- **Base**: `codex/s00-foundation` @ `27e2574`. Build 0W/0E (warnings-as-errors). Suíte de integração em `odca_test_local` reconstruído do zero: **idêntica à linha de base** — 111 aprovados / 54 falhas pré-existentes de ambiente (zero nova regressão deste incremento).
- **Entrega**: `docs/execution/D-OC18_RECURPERACAO_DIVERGENCIA_SESSAO.md`. Comportamento provado contra a aplicação real (API + Web, sessões MFA, banco QA):
  - **Expiração de sessão**: invalidação real de sessões no banco (`odca.sessions.revoked_at`, mesmo caminho de validação da expiração) → GET devolve 302 para `/entrar?ReturnUrl=<página>`; o destino é preservado em três saltos (login → `mfa/desafio?returnUrl=` → hidden input do desafio) e, após o MFA, o redirecionamento volta **exatamente** para a página de origem (reaberta com 200). O middleware de estágio de segurança agora envia super-admin sem MFA para `/mfa/desafio?returnUrl=<caminho+query>`.
  - **Divergência (409)**: mensagens dedicadas com instrução de recarga na ação de obrigação (novo código de API `obligation.version.conflict`) e na proposta de renovação/aditivo (código `contract.version.conflict`); "definir vista padrão" da Caixa deixa de devolver 409 vazio (redireciona ao índice com mensagem). Em todos os casos: 302 + mensagem renderizada + banco intacto.
  - **Fetch**: as quatro frentes (`studio.js`, `imports.js`, `obligations.js`, drawer da Ficha) detectam `redirected`/401 antes de parsear JSON — mensagem clara de "Sessão expirada" no lugar de SyntaxError e redirect para `/entrar?ReturnUrl=<página atual>`.
- **Classificação**: provado por comandos reais da aplicação (HTTP + renderização + estado do banco); SQL usado apenas para forçar a invalidação das sessões (teste A) e criar a linha-fixture da vista (teste D).
- **Descoberta (pré-existente, agendada separadamente)**: salvar vista da Caixa devolve 500 — o BFF envia `listingType="inbox"` mas a constraint canônica só permite `('obligations','reviews','contracts')`; por isso `saved_work_views` estava vazia em todos os tenants. Requer migração ou alinhamento de contrato.
- **Próximos passos**:
  - Item 20: fila de exceções (Bloco E).
  - Correção do 500 das saved-views `inbox` (migração v052 ou alinhamento de contrato).
  - Bloco F: i18n (4 idiomas), validação de formatos, design 360/768/1440px, homologação visual de PDF.
  - Backlog separado: as 54 falhas pré-existentes da suíte de integração (ambiente/estado).
  - Sem push/merge/deploy automático.

## Incremento: Estúdio item 7 — homologação formal de salvamento de minuta (12 linhas) (12/10/2026)

- **Base**: `codex/s00-foundation` @ `4b92c54`. Incremento somente de evidência/documentação — zero mudança de código (nenhum projeto recompilado; build/testes não se aplicam).
- **Entrega**: tabela de 12 cenários em `docs/execution/CONTRACT_STUDIO.md` (§ Homologação de salvamento de minuta), cada linha executada contra a aplicação real (API + Web, sessões MFA) sobre a minuta `46665c7b…` criada por API:
  - Sucesso: PUT 200 avança `row_version` uma vez, grava valores/`last_client_revision`, 1 recibo + 1 evento `draft.saved`; barra `Alterações não salvas → Salvando → Salvo` (`studio.js`).
  - Idempotência: replay pela `clientRevision` devolve a resposta original persistida **mesmo com corpo divergente** (o recibo do servidor vence) — duplicata jamais grava duas vezes.
  - Falhas: 400 de tipo (mensagem por campo, `O valor de 'Início' é inválido.`) e 400 estrutural (campo definido sem ocorrência) sem tocar no banco; 409 de conflito com `currentVersion` no corpo + banner de recuperação na UI; o loop recuperar-e-repetir fechou em 200.
  - Fora de ordem / duplo submit: "Salvo" apenas quando `localRevision===sentRevision` (resposta atrasada volta a sujo e re-salva em 150 ms; single-flight bloqueia PUT paralelo); corrida real de dois PUTs concorrentes resolveu 200/409 com um único incremento de versão.
  - Permissão/isolamento: remoção reversível do papel em `odca.member_roles` → GET/PUT **403** por requisição (a checagem lê o banco, não o claim do token — descoberta colateral útil); 404 para draft inexistente e cross-tenant (RLS).
  - Verdade da interface: carga inicial e reload pós-salvamento refletem o servidor — `data-version` = `row_version` e carimbo renderizado = `updated_at` no fuso local.
- **Classificação**: comportamento provado por comandos reais da aplicação (HTTP + renderização + estado do banco); SQL usado apenas para a fixture reversível da linha 11 e para asserções de observação.
- **Próximos passos**:
  - Itens 19–20: recuperação de divergência + fila de exceções.
  - Bloco F: i18n do Estúdio/plataforma (4 idiomas), validação de formatos (data/moeda), design 360/768/1440px, homologação visual de PDF.
  - Backlog separado: as 54 falhas pré-existentes da suíte de integração (ambiente/estado) merecem incremento dedicado.
  - Sem push/merge/deploy automático.

## Incremento: Estúdio item 11 — publicar nova revisão de modelo oficial preservando documentos existentes + fix de checksum v051 (12/10/2026)

- **Base**: `codex/s00-foundation` @ `0ff937c` (início). Commit único deste incremento (ver abaixo).
- **Build**: solution 0 avisos / 0 erros · Domain **300/300 PASS** · `MigrationChecksumTests` **35/35 PASS** (após o fix abaixo; falhava no HEAD).
- **Item 11**: o superadmin agora publica **nova revisão (N+1)** de um modelo oficial pela UI, com a garantia explícita de que documentos emitidos de revisões anteriores não mudam (minutas/versões geradas ficam pinadas por snapshot + `source_template_version_id`):
  - API: `GET api/v1/platform/template-catalog/{key}/revision/current` (conteúdo/campos da revisão atual para edição) e `POST api/v1/platform/template-catalog/{key}/revisions` — transação única com `FOR UPDATE`, exigindo modelo publicado, resumo obrigatório (5–500, vai para auditoria), JSON validado server-side (objeto/array) e concorrência otimista por `expectedRevision` (409 `template.revision_conflict`). Grava: INSERT em `contract_template_versions` (N+1), UPDATE `current_version`/`official_revision`/`row_version` e evento `platform.template.revision_published`.
  - Web: botão "Nova revisão" na biblioteca da plataforma (somente publicados), página `/administracao/biblioteca/{key}/nova-revisao` com o mesmo editor do Estúdio (campos variáveis + texto simples com marcadores + banner de perda de estrutura), caixa de efeito da publicação e confirmação no submit. Erro/conflito recarrega o estado do servidor. Rótulos PT-hardcoded conforme convenção das views de plataforma (i18n do Estúdio segue no Bloco F).
  - DTOs: `TemplateRevisionCurrent` e `TemplateRevisionPublishRequest` em `Odca.Contracts.Administration`; 2 métodos no `OdcaApiClient`.
  - **Evidência comportamental** (apps reais, sessão MFA admin, modelo global `nda-mutual`, tenant 0001): minuta criada **antes** via `POST studio/drafts` fixou a linha global v1 (`4817b61e…`, snapshot byte-idêntico — SHA `bd695afc…`); publicação da revisão 2 pelo **formulário Web** (302 + alerta "Revisão 2 publicada" no index); após: v1 imutável (SHA idêntico antes/depois), `current_version=official_revision=2`, v2 contém a edição do título, auditoria gravada; a minuta pré permaneceu pinada em v1 com hash intacto; nova minuta criada **depois** fixou v2 (`8e2fde44…`); POST concorrente com `expectedRevision=1` → 409; página reaberta mostra "revisão atual 2" e botão "Publicar revisão 3".
- **Fix lateral (pré-existente no HEAD, descoberto na validação)**: o canonical `database/odca.sql` terminava em **v050** enquanto `DatabaseSchema.CurrentVersion=51` — o bloco v051 (link solicitação→contrato do item 13/17) só existia no release `odca-v051.sql`, com checksum placeholder (`a3f8c1d2…`) inconsistente com o corpo (`d9951f97…`). Corrigido: bloco v051 anexado ao canonical (EOL CRLF preservado), release regenerada **byte-idêntica** ao canonical, os 3 checksums do bloco atualizados (header/DO/INSERT) — verificado por `MigrationChecksumTests` 35/35. Histórico dos bancos locais ajustado (`odca_test_disposable.schema_migrations` v051 → novo checksum). Sem mudança de comportamento da aplicação.
- **Classificação da suíte de integração**: com e sem este incremento (A/B, banco fresco `odca_test_local` em cada execução), **mesma lista exata de 54 falhas** (111/165 passam) — todas pré-existentes/ambientais no HEAD (ex.: `ApiHostStartupTests` usa connection string sem senha contra um PG com SCRAM; classes com estado acumulado na ordem serial de execução). Zero regressão deste incremento.
- **Próximos passos**:
  - Item 7: tabela formal de homologação de salvamento (12 linhas).
  - Itens 19–20: recuperação de divergência + fila de exceções.
  - Bloco F: i18n do Estúdio/plataforma (4 idiomas), validação de formatos (data/moeda), design 360/768/1440px, homologação visual de PDF.
  - Backlog separado: as 54 falhas pré-existentes da suíte de integração (ambiente/estado) merecem um incremento dedicado.
  - Sem push/merge/deploy automático.

## Incremento: Item 3 — categorias de descoberta na biblioteca do Estúdio (12/10/2026)

- **Base**: `codex/s00-foundation` @ `6667f62` (início). Commit único deste incremento (ver abaixo).
- **Build**: solution 0 avisos / 0 erros · IntegrationTests `TemplateCatalogPersistenceTests` **3/3 PASS** (banco dedicado `odca_test_local`, fixture `ODCA_INTEGRATION_TESTS`).
- **Item 3**: a biblioteca oficial de modelos agora é descobrível por **finalidade documental** (a coluna `document_purpose` já existia desde a migração 031 com CHECK de 7 valores; o formulário de criação de modelo oficial já a gravava — faltava expô-la na descoberta):
  - API: `GET studio/templates` aceita `?purpose=` (parâmetro validado por allow-list de domínio no SQL, Dapper parametrizado) e devolve `documentPurpose` no DTO `TemplateCatalogItem` (novo campo opcional).
  - View `Studio/Index.cshtml`: select "Finalidade" na barra de filtros (7 categorias), **badge de categoria** no card quando preenchida, filtro preservado em paginação (`asp-route-purpose`) e no "Duplicar como particular" (hidden input). Rótulos seguem a convenção PT-hardcoded deste arquivo (i18n completo do Estúdio segue no Bloco F).
  - Testes: `TemplateCatalogPersistenceTests` ganhou cobertura real do filtro (template publicado com `confidentiality` aparece só no filtro correspondente; rascunho sem finalidade nunca aparece em nenhum filtro de finalidade); stubs dos demais testes atualizados para a nova assinatura.
  - **Evidência comportamental** (API real, sessão MFA admin, tenant 0001): catálogo sem filtro `total=12` (item 0 já traz `documentPurpose='confidentiality'`) → `?purpose=lease` `total=1` (único valor distinto = `lease`) → `?purpose=xyz` `total=0`. Renderização Web confirmada: 12 cards sem filtro → 1 card com `purpose=lease`, com badge de categoria "Locação" dentro do card e select com opção selecionada preservada.
- **Próximos passos**:
  - Item 7: tabela formal de homologação de salvamento (12 linhas).
  - Estúdio item 11: publicar nova revisão preservando documentos existentes (UI).
  - Itens 19–20: recuperação de divergência + fila de exceções.
  - Bloco F: i18n do Estúdio (4 idiomas), validação de formatos (data/moeda), design 360/768/1440px, homologação visual de PDF.
  - Sem push/merge/deploy automático.

## Incremento: Item 12 — sequência de documentos derivados na ficha (aditivo/renovação/revisão) + fix de transação latente na Sheet (12/10/2026)

- **Base**: `codex/s00-foundation` @ `2fd53c8` (início). Commit único deste incremento (ver abaixo).
- **Build**: solution 0 avisos / 0 erros · Domain tests **300/300 PASS** · catálogos i18n (4 idiomas) parseando limpo.
- **Item 12**: nova seção `#derivados` na FICHA do contrato exibe a sequência completa de versões geradas do documento, em ordem, com origem quando derivada de alteração:
  - DTO `ContractSheetDerivedDocumentDto` (versão, data de geração, review_status, prefixo SHA-256 canônico, kind/effective_on/razão de origem, quem/quando aplicou, delta before→after de vigência e valor).
  - Query no `ContractSheetRepository` (mesma transação de leitura da ficha): `generated_contract_versions` LEFT JOIN LATERAL em `contract_change_requests` por `generated_version_id` (+ `contract_change_applications` para before/after e aplicador). Sem nova tabela/migração — dados já existiam.
  - View `Sheet.cshtml`: link na subnav condicional + seção com badge por versão, rótulo de status de revisão, "Geração direta (minuta ou modelo)" quando sem origem, motivo/data de efeito/"Aplicada em … por …" e linhas "Vigência: antes → depois" / "Valor: antes → depois".
  - i18n: 16 chaves novas × 4 idiomas (pt/en/fr/es), reutilizando `sheet.chg_*` existente onde fazia sentido.
  - **Bug latente corrigido (descoberto na validação)**: `ContractSheetRepository.GetAsync` tinha `CommitAsync` no meio do método (linha 226 em HEAD) — as queries seguintes (solicitações, alterações pendentes) rodavam sobre transação já finalizada → Npgsql `Transaction is already completed` → **500 em TODA ficha**. Commit movido para o fim da coleta de dados. Corroborado: antes da correção a ficha real retornava 500; depois, 200 com as seções.
  - **Evidência comportamental** (API real, sessão MFA admin): caso direto (tenant 0001/contrato 421d0ecd → v1 "gerado", sem origem, sha prefix); caso derivado (tenant e5f78b3c/contrato ed122873 → v2 "Derivado de Renovação", effective 06/10/2026, aplicada em 06/10/2026 por "Admin C Auditoria", vigência 06/10/2026–31/03/2027 → 01/04/2027–31/03/2028, valor 15.000,50 BRL → 18.000,00 BRL). Renderização Web (login+MFA via cookie, GET `/organizacoes/{tenant}/contratos/{contract}`) confirmou seção, subnav, rótulos e deltas nos dois casos (pt-BR).
- **Mudança de fixture documentada** (base de teste descartável): `admin@odca.local` ganhou membership ativa + role `tenant-administrator` no tenant `e5f78b3c…` para validar a ficha cross-tenant do caso derivado.
- **Próximos passos**:
  - Item 3: categorias de descoberta do Estúdio.
  - Item 7: tabela formal de homologação de salvamento (12 linhas).
  - Estúdio item 11: publicar nova revisão preservando documentos existentes (UI).
  - Itens 19–20: recuperação de divergência + fila de exceções.
  - Bloco F: validação de formatos (data/moeda), design 360/768/1440px, homologação visual de PDF.
  - Sem push/merge/deploy automático.

## Incremento: Fontes de Solicitação e Assinatura no Inbox operacional (D-OC17) + auditoria scanner do auto-approve (D-OC16) + prévia do modelo no Estúdio (12/10/2026)

- **Base**: `codex/s00-foundation` @ `87c47b0`/`8b24769` (início) → `77c666a` (fim). 3 commits nesta janela (`b47a970`, `c785bef`, `77c666a`).
- **Build**: solution 0 avisos / 0 erros · Domain tests **300/300 PASS** · health schema 51/51 · **Matriz de aceite 15/15 PASS (0 BLOCKED)**.
- **Estúdio item 4** (`b47a970`): prévia do modelo antes de criar minuta — `GET studio/templates/{id}/preview` renderiza HTML com valores vazios via `ContractDocumentRenderer` (não cria contrato, não consome franquia, não usa dados de outro cliente); botão "Ver prévia" por modelo publicado + disclaimer claro.
- **Bloco A item 4** (`c785bef`, D-OC16): auditoria do auto-approve do scanner — 4 estados distintos (verificado/ameaça/indisponível/simulado); indisponibilidade **nunca** produz `safe`; simulação só em Development com `Documents:SimulateScanner != false` + guarda dura em código; selo visível na ficha para verificação simulada; verificado com worker real 3/3 PASS.
- **Item 17** (`77c666a`, D-OC17): fontes **Solicitação** e **Assinatura** integradas ao endpoint e à página **existentes** do Inbox operacional (mesma rota, mesma view, âncoras `#solicitacoes`/`#assinatura` na Sheet). Portão por fonte via permissão real: Solicitação = `tenant.solicitations.read` (mine = `opened_by`), Assinatura = `tenant.contracts.read` (mine = participação em `composition_revision`). `/acoes` permanece como visão complementar de ações agrupadas.
  - Bugs latentes corrigidos na verificação (só aparecem com a query rodando): status EN dentro de CHECK PT (inbox + action center); `partially_signed` fantasma (domínio canônico `draft|confirmed`; estado parcial deriva de `signature_participants.signed_at`); negação C# `!@param` no SQL (42883); 5 CTEs do action center sem parêntese de fechamento (42601) + mapeamento Dapper snake_case; nome do documento = `COALESCE(contracts.title,'Documento gerado')` nas duas views; cauda v051 órfã dentro do bloco 051 + `DROP FUNCTION` do defasado + REVOKE obsoleto; `validTypes` aceita `services-multiple-therapies`; catálogos i18n Web (4 idiomas) com vírgulas reparados (crash de startup oculto).
  - **Evidência comportamental** (`scripts/qa/validate-inbox-sources.ps1`, curl https:7143, MFA real via `/mfa/challenge` + código de recuperação): restrito (operador, `contracts.read=f`) → inbox org=10 só Solicitation, filtro Signature=0, action-center sem fonte signature (14); completo (admin, ambos reads) → inbox org=19 (Sol 10 + Sig 9 com título do contrato), filtro Signature=9, action-center 23. Raiz das 401 genéricas anteriores documentada: cliente PS 5.1 remove `Authorization` no redirect 307 http→https; chamada direta https resolve.
- **Mudança de fixture documentada** (base de teste descartável): senha de `admin@odca.local` = `OdcaAdmin#Qa2026` (hash gerado pelo próprio `AspNetPasswordService`); códigos de recuperação MFA conhecidos inseridos (`ODCA-*-QA-*`, uso único).
- **Matriz reexecutada e completada (15/15)**: `validate-acceptance-matrix.ps1` agora usa `curl.exe` sobre HTTPS (root cause das 401s) + parâmetro `ODCA_QA_MFA_CODE` para sessão MFA real. **S11** PASS (action-center mine com token MFA-verificado); **S12** PASS executado (conta restrita: banco tem 9 preparações de assinatura, inbox/filtro/action-center exibem 0 — fonte proibida inteiramente ausente); **S13** PASS com ciclo real via API como admin: `POST renewals/contracts/{id}` → `/submit` → `/formalization` (evidência `document_versions` safe, justificação, idempotency) → `/apply` = `{"applied":true,"contractVersion":2,"obligationsPreserved":true}`; request final `formalized|applied`, fora da view pendente, com linha em `contract_change_applications`. Fixture: evidência `bdfdc315…` ("Val S13 evidence") no contrato `cfd0bf87…`.
- **Próximos passos**:
  - Item 12: sequência de documentos derivados na ficha (aditivo, renovação, revisão).
  - Item 3: categorias de descoberta do Estúdio.
  - Item 7: tabela formal de homologação de salvamento (12 linhas).
  - Estúdio item 11: publicar nova revisão preservando documentos existentes (UI).
  - Itens 19–20: recuperação de divergência + fila de exceções.
  - Bloco F: validação de formatos (data/moeda), design 360/768/1440px, homologação visual de PDF.
  - Matriz fechada 15/15 nesta janela (S11–S13 via sessão MFA real + ciclo apply real).
  - Sem push/merge/deploy automático.

## Incremento: Governança contratual — aprovações, condições vigentes, worker canônico, modelo oficial (12/10/2026)

- **Base**: `codex/s00-foundation` @ `3c944b7` (início) → `8b24769` (fim). 5 commits nesta sessão.
- **Build**: 0W/0E. Matriz: **12 PASS, 0 FAIL, 3 BLOCKED**.
- **Estúdio item 2** (`4fac709`): Superadmin cria modelo oficial via interface.
  - `POST api/v1/platform/templates`: cria global draft com official_key auto-gerada, validação de tipo, uniqueness check.
  - `GET/POST administracao/biblioteca/novo`: form nome/tipo/descricao/finalidade.
  - Botão "+ Novo modelo" na biblioteca; 13 keys i18n × 4 idiomas.
- **Bloco B/7-B/9** (`97f3b9f`): Documento D-OC15 — vinculação aprovação→versão + critérios.
  - Formalização exige `evidence_version_id` (document_version com sha256 + security_status='safe').
  - Apply valida `base_contract_version == current_version`; CAS row_version; idempotency key.
  - Revisão administrativa: delta comercial bloqueado na aplicação (revStart/End/Value vs Old).
  - Permissões granulares: prepare/read/decide/formalize/apply/cancel (segregação por função).
- **Item 10** (`0db80a8`): Condições vigentes vs propostas na ficha do contrato.
  - `ContractSheetChangeDto`: kind, status, application_status, effective_on, proposed values.
  - Repository consulta change_requests ativas (não cancelled/applied) LIMIT 10.
  - View: seção entre detalhes e renovação com badge por estado, data de efeito, nova vigência.
  - Antes da aplicação: condições atuais visíveis; depois: alteração sai e fica no histórico.
- **Item 11** (`2081924`): Worker aplica mesma regra canônica que ApplyCore.
  - Gap encontrado: worker NÃO checar delta comercial em kind=revision.
  - Corrigido: revStart/revEnd/revValue comparados com OldStart/OldEnd/OldValue.
  - Se divergem: status=conflict (mesmo comportamento do manual).
- **Matriz S2 fix** (`8b24769`): Handler para unique constraint `(tenant_id, contract_id)`.
  - Se draft já existe → verifica persistência do conteúdo (cenário real de retomada).
- **Próximos passos**:
  - Item 12: sequência de documentos derivados na ficha (aditivo, renovação, revisão).
  - Item 14/15: completar papéis dos participantes + identidade signatário na UI.
  - Item 17: integrar fontes novas (Signature, Solicitation) no OperationalInboxRepository.
  - Item 23: validação visual PDFs + design em 360/768/1440px.
  - Estúdio item 4: prévia do modelo antes de criar minuta.
  - Estúdio item 11: criação de nova revisão sem alterar documentos existentes (UI para publicar revisão).

## Incremento: Central operacional corrigida + vínculo solicitação-contrato + matriz comportamental (12/10/2026)

- **Base**: `codex/s00-foundation` @ `a2ccb42` (início) → `324cd00` (fim). 3 commits nesta sessão.
- **Build**: 0W/0E (Odca.Api, Odca.Web, Odca.Infrastructure).
- **Bloco B/7-10 — OperationsCenterController REESCRITO**:
  - Autorização por fonte separada: cada CTE (renewal, signature, obligation, solicitation, contract) condicional ao seu respectivo permission (`tenant.renewals.read`, `tenant.contracts.read`, `tenant.obligations.read/read_all`, `tenant.solicitations.read`).
  - "Minhas" vs "Organização": obrigações filtram por `owner_id`; renovações por `responsible_id`; solicitações por `opened_by`; assinaturas por participação real (membership → signature_participants). Anteriormente "mine" = toda a organização para solicitações.
  - Categorias corrigidas: solicitação pausada → `info` (não mais `upcoming`); data de efeito ≠ prazo de decisão; prioridade crítica para atrasos.
  - Paginação: `page`/`pageSize` parâmetros + `COUNT(*)` do conjunto autorizado + `OFFSET/LIMIT`. Ordenação determinística (priority → deadline → created_at DESC).
- **Bloco C/13 — Vínculo persistente solicitação→contrato**:
  - Migration v051: `solicitations.contract_id uuid REFERENCES odca.contracts(id)` + índice parcial.
  - Função `odca.solicitations_open` atualizada (8 params, `p_contract_id DEFAULT NULL`) com validação: contrato deve pertencer ao tenant.
  - API controller passa `contractId`; Web controller aceita `contractContext`; formulário na ficha envia hidden field.
  - Repository da ficha agora filtra `WHERE s.contract_id=@contractId` (não mais tenant genérico).
- **Item 3 — Matriz de aceite reescrita (comportamental)**:
  - Anterior: 15 cenários estruturais (coluna existe, contagem > 0).
  - Nova: fixture própria (INSERT), ação executada (UPDATE/função), releitura, verificação de conflito/hash/idempotência, limpeza implícita.
  - Resultado: **12 PASS, 0 FAIL, 3 BLOCKED** (S11/S12/S13 com causa: API fora do ar, conta sem permissão parcial, sem alteração aplicada ainda).
  - Credenciais via `$env:` (sem fixas no script); PS 5.1 compatível.
- **Próximos passos**:
  - Item 12: integrar novas fontes (Signature, Solicitation, Contract) na view do Inbox existente.
  - Item 4: auditoria do auto-approve em Development no DocumentWorker.
  - Item 22: reconciliar direitos comerciais (OCR/Reviews na matriz).
  - P0 (complemento): aprovações vinculadas à versão + condições vigentes + identidade signatários.

## Evolução completa da operação contratual — incremento integrado: integridade + jornada + SaaS + localização + matriz de aceite 15/15 (11–12/10/2026)

- **Base**: `codex/s00-foundation` @ `818f039` (início) → `fe92857` (fim). 9 commits, árvore limpa, sem push/merge/deploy.
- **Baseline validado**: Build 3 projects 0W/0E · Domain 300/0 · Data validation 17/17 · **Matriz de aceite 15/15 PASS**.
- **Bloco A (corrections)**: 4 fixes em `RenewalCenterController` — (1) remoção de OR confuso entre versão solicitação × versão-base; (2) validação de estado resultante em Create; (3) moeda correta no INSERT de histórico; (4) revalidação defensiva de revisão administrativa. Estado D-OC14 documentado (`ESTADOS_DEFICAO_CONSISTENTE.md`).
- **Bloco B (jornada)**: (B/7) `OperationsCenterController` — endpoint action-center agregando 5 fontes em 5 categorias com próxima ação; (B/9) Ficha do contrato ampliada com seção "Solicitações ODCA" (top 10 ativas por tenant + DTO + query) e formulário "Abrir solicitação ODCA" com contexto do contrato; (B/11) Comparação de versões reescrita com 5 seções categorizadas, CSS diff visual, disclaimer jurídico.
- **Bloco C/D (SaaS)**: Endpoint de ativação de tenant (`POST /customers/{id}/activate`); Matriz de planos/benefícios documentada (`MATRIZ_PLANOS_BENEFICIOS.md`) com 3 planos × módulos × limites × SLA × operações × upgrade/downgrade.
- **Bloco D (localização)**: Infraestrutura i18n completa — `LocalizationService` singleton, `CultureMiddleware` (cookie `odca_lang`), `NavContext.Localizer` delegate, `_Layout` traduzido, 4 catálogos JSON (pt-BR/en-US/fr-FR/es-ES, ~100 keys cada), `AccountController.SetLanguage`. Sheet.cshtml: headers (9 keys) + subnav (12 keys) localizados nos 4 idiomas.
- **Bloco E/F (validação)**: `validate-e-data.ps1` 17/17 PASS (integridade referencial, idempotência, consistência); `validate-acceptance-matrix.ps1` 15/15 PASS (cenários do prompt: cliente novo, rascunho, concorrência, idempotência, prazo indeterminado, snapshot, template versioning, responsável desativado, permissão, isolamento, SLA, idiomas, auditoria, PDF, worker restart).
- **Entrega**: `EVIDENCIAS_ENTREGA_20261012.md` com os 9 itens exigidos pelo prompt item 28.
- **Próximos passos**: C/14 (estender `@L.T()` para as 60+ views restantes), C/23 (revisão editorial/termos), C/16 (design responsivo 360/768/1440), E/24 (homologação PDF visual). Sem push/merge/deploy automático.

## Fase de aprofundamento sobre `2a6fc43` — reauditoria da entrega recente (Blocos A+B+C) + início da integridade contratual (Bloco A′) (10/10/2026)

- **Base reconfirmada**: `codex/s00-foundation` @ `2a6fc43` (árvore limpa). O commit já incorpora Blocos A (`62ba38f`), B (`2412875`) e C (`4875c67`+`2a6fc43`, v046). Os dois commits finais ("novos ajustes") contêm exatamente o Bloco C (24 arquivos; o volume é o snapshot `odca-v046.sql`). Dados, snapshots e histórico preservados.
- **Reauditoria (§12 da auditoria)**: classificação dos nove itens — **comprovado**: navegação centralizada, busca administrativa de usuários, suporte técnico por plano, conferência por severidade (código), **biblioteca oficial global** (retirada preservada após reinício + resolução sem troca silenciosa de versão — A′/3/D-OC12), **publicação/retirada de modelos**, **revisão administrativa** (regra de servidor — A′/2/D-OC11), **notificações** (paginação/leitura individual/preferências/canal e-mail/link seguro — A′/5/D-OC13). **Parcial**: comparação de versões (categorização administrativo×jurídico + polimento UI).
- **Baseline validado nesta sessão**: **E 140/0** · **C 98/0** · **D 116/0** · **b34 109/0** — 0 falhas; **fingerprint dos seeds idêntico** ao baseline (`9b641fa0c67d2bc840054c24776c9feb`) e **0 `qa-%` residuais** (isolamento). Único defeito real já corrigido+commitado: `C.setup` da b34 usava `email_normalized` em minúsculas (armazenado em maiúsculas) → `upper(...)`. Nota ambiental: a suíte **D** falha na cadeia quando o 1º pedido excede 60 s (caixa quente pós-E); isolada → 116/0.
- **Bloco A′ / item 2 — revisão administrativa (COMPROVADO)**: regra de servidor — `kind='revision'` **rejeita** payload com delta comercial (valor/vigência/moeda) no `Create`, comparando o efeito aplicado ao contrato atual; correção não comercial preserva valores, vigência, objeto e partes; alterações materiais seguem aditivo/nova versão. Evidência de live: suíte **D 118/0** (D12b: reajuste→400, revisão pura→201); build `Odca.Api` 0W/0E.
- **Bloco A′ / item 3 — biblioteca/retirada (COMPROVADO, D-OC12)**: (a) reinício não republique modelo retirado — seeder consulta `EXISTS` sem filtro de status (linha global arquivada é pulada, não reseeded); (b) atualização de linha global não troca silenciosamente a versão fixada — migração **v047** faz `odca.resolve_official_template` rank 1 retornar a versão fixada. Evidência: simulação em banco do seeder + probe resolver antes/depois; v047 aplicada (`maxv=47`, checksum 47/47, snapshot byte-igual); homologação live S8 (publicar→encontrar→retirar→bloquear→republicar→reconectar) + S6 (decisão presa à versão). Suítes **E 143/0** e **D 118/0**; seed intacto; build `Odca.Api` 0W/0E.
- **Bloco A′ / item 5 — notificações completas (COMPROVADO, D-OC13)**: paginação (`page`/`pageSize` cap 100, `total`, `hasMore`), leitura individual (`POST {id}/ler`), preferências por tipo/canal/idioma (allowlist pt‑BR/en‑US/fr‑FR/es‑ES, per‑kind enabled/in_app/email), link seguro de destino (`target_type`+`target_id` → `odca.notification_target_authorized` fail‑closed → `CanOpen=false` quando sem permissão), canal e‑mail via outbox generalizado (`invitation_id` NULL → Worker dispatch a `SendUserNotificationAsync`, gate `notification_channel_state`). Migração **v048** aplicada (`CurrentVersion=48`). Evidência: **S9 27/0** (paginação 30 seeds, individual read isola, prefs GET/PUT + linguagem cultural `10/11/2026 12:30 PM`, gate function, marcar‑lidas) + **E 143/0** + **D 118/0**; build 3 projects 0W/0E.
- **Próximos passos**: B′ (4 idiomas pt/en/fr/es ponta-a-ponta com `.resx` + seletor persistente + formatos culturais) → C′ (design + PDF 360/768/1440 + matriz plano×perfil×ação×menu + homologação superadmin). Sem push/merge/deploy automático.

## Operação contratual — Bloco B implementado: registro central de navegação (D-OC4) + menus cliente/plataforma com itens reais + suporte técnico em todos os planos (D-OC7) + administração global (catálogo global, busca de usuários, filtros de clientes); migração v045; cadeia E→C→D→b34 sem falhas; seed intacto (10/10/2026)

- **Base**: `codex/s00-foundation` @ `62ba38f` (Bloco A) + Estabilização A; auditoria e matriz em `docs/execution/AUDITORIA_OPERACAO_CONTRATUAL_20261010.md` (**§10 implementação revalidada**).
- **D-OC4 · Registro central de navegação**: `src/Odca.Web/Navigation/` (NavModels/NavigationRegistry/NavigationRenderer) tornou-se a fonte única dos menus — ordem, condições de disponibilidade/ativo e captions do `_Layout.cshtml` antigo preservados item a item; única normalização: `aria-current="page"` em todo item ativo (antes inconsistente). Itens novos com páginas reais: cliente — **Arquivados** (`Contracts/List?status=arquivados`), **Suporte técnico** (`?service=suporte_tecnico`) e **Minha conta** (`/minha-conta`: senha + MFA em autoatendimento, sem placeholder); plataforma — **Localizar usuários** (busca global com vínculos), **Biblioteca de modelos** (`administracao/biblioteca`: catálogo global com versões, publicar/retirar com justificativa) e **Configurações operacionais** (matriz SLA somente-leitura). Removido o item morto "Publicação e versões".
- **D-OC7 · Suporte técnico e recuperação de acesso em qualquer plano vigente**: quarto serviço `suporte_tecnico` nos CHECKs + 12 políticas de SLA (base do `esclarecimento` × multiplicador do plano); `solicitations_open` exige assinatura ativa para o serviço, mantém o gate Enterprise byte-idêntico (`solicitations.plan.required`) para os documentais; recuperação de acesso = ticket de suporte técnico (sem fluxo próprio inventado). Superfície Web completa (Nova com pré-seleção, rótulos em listas/detalhes).
- **Administração global completa**: `PlatformOperationsController` (API, policy `PlatformAdministrator` + sessão MFA) — catálogo oficial global com versões e publicar/retirar auditados (`platform_catalog_set_status`, NOOP honesto), busca global de usuários (`platform_user_search`, vínculos por organização) e filtros de clientes por plano/situação (`platform_consumption_customers` de 4 parâmetros). Web: filtros na lista de clientes, página de busca de usuários e as duas páginas administrativas novas. A leitura PARCIAL da auditoria foi corrigida: mudança de plano, bloqueio de recurso e suspensão/restauração **já existiam** com justificativa — os gaps reais eram filtros e busca global, ambos implementados.
- **Migração v045** aplicada em `odca_test_disposable` (`CurrentVersion=45`). O bloco canônico foi re-hash para `3ced09a7…bef0b` após o reparo do defeito de `platform_catalog_set_status` (parâmetro `official_key` colidia com a coluna homônima → Postgres 42702, ver DECISIONS-LOG); `KnownDefectivePackage V045CatalogSetStatus` recria a função e re-carimba `schema_migrations` automaticamente no bootstrap de bancos gravados com o checksum original `ecfc3004…d57c`; `database/releases/odca-v045.sql` permanece imutável como registro do pacote como publicado.
- **Cadeia pós-mudança (E→C→D→b34)**: **E 140/140** · **C 99/99** · **D 116/116** · **b34 100/100** — 0 falhas; logs `baseline-{e,c,d,b34}-20261010-08{4538,4717,4838,4934}.log` em `%TEMP%\opencode\`. Novos asserts: **S8 da suíte E** (+20: catálogo 9, retirada/publicação da linha global com auditoria+row_version+visibilidade, justificativa curta 400, busca de usuário, filtros de clientes, suporte técnico abre em basic e documental segue Enterprise) e **T2.9 da b34** (+5: itens novos no menu HTML + página Minha conta renderiza + item removido ausente). Fingerprint dos seeds **idêntico** ao baseline (`9b641fa0c67d2bc840054c24776c9feb`), 0 `qa-%` residuais. Probe dedicado das páginas de plataforma (usuário descartável SuperAdministrator + cliente TC vinculado): **19/19** — biblioteca com 9 modelos + chaves `surgical-consent`/`nda-mutual`; matriz SLA 48 linhas = 36 documental + 12 suporte técnico; busca global encontra `admin@odca.local`/"Administrador da plataforma"; cliente TC autenticado → `/acesso-negado` nas rotas de plataforma; cleanup sem resíduo.
- **Git**: Bloco B será publicado como commit único em `codex/s00-foundation` — push somente quando pedido. Decisões D-OC4/D-OC7 em `DECISIONS-LOG.md`.
- Próximos passos: **Bloco C** (conferência documental antes da emissão, comparação de versões/histórico, renovações e alterações, painel de pendências/prazos, notificações + SLA Enterprise — D-OC6) → **Bloco D** (4 idiomas pt/en/fr/es D-OC5, linguagem simples, design consistente, PDF renderizado validado, 360/768/1440) → documento de entrega com matriz plan×profile×action×menu e script de homologação.

## Operação contratual — Bloco A (gate) implementado e revalidado: catálogo oficial global sob gestão da plataforma (D-OC1) + canal de envio honesto (D-OC3); migração v044; cadeia E 120/120 · C 99/99 · D 116/116 · b34 95/95 sem falhas; seed intacto (10/10/2026)

- **Base**: `codex/s00-foundation` @ `786694c` + Estabilização A; auditoria e matriz em `docs/execution/AUDITORIA_OPERACAO_CONTRATUAL_20261010.md` (§1–§5 auditoria completa dos planos publicados, §4 matriz funcionalidade→plano→perfil→menu→endpoint→persistência, §8 baseline pré-Bloco-A, **§9 implementação revalidada**).
- **D-OC1 · Biblioteca oficial sob gestão da plataforma** (cliente consulta publicado e cria minutas/copias; não carrega modelos): novo `OfficialCatalogSeedingService` (`IHostedService` da API) publica os 9 modelos oficiais como linhas `scope='global'` (owner NULL, revisão 1) em cada start — idempotente por `official_key`, 5 tentativas (2 s), não-fatal, autor fixo do admin de plataforma, evento `template.official_catalog_seeded`; `VisibleFilter` faz dedupe (tenant dono de cópia não arquivada da mesma `official_key` não vê a linha global duplicada); `POST studio/templates/official` mantém compatibilidade com exists-check apenas sobre cópias próprias (arquivar→reinstalar do E-S6 restaurado); instalar/importar saíram da apresentação do cliente (Studio, ficha, novo documento), endpoints mantidos; migração **v044** adiciona `odca.resolve_official_template(tenant,key)` (ordem: último usado → cópia própria → linha global, sempre a versão publicada corrente), reescreve `template_approval_decide` (aprovação presa à versão aprovada mesmo via linha global) e `template_approvals_queue` (uso = cópias próprias ∪ versões geradas da linha global). Probes diretos: 9 linhas globais · dedupe demo=9 · tenant sem cópia decide preso à versão global rev 1 · fila 1 tenant/linha.
- **D-OC3 · Canal externo declarado não configurado** (sem entrega falsa): `RemindPreparation` carrega `delivery={channel:"not_configured",sent:false,message}` na resposta, no evento de preparação e na auditoria; Web exibe "Lembrete registrado…" e o diálogo de confirmação avisa que o envio por canal externo ainda não está configurado. Conclusão de assinatura permanece restrita às modalidades comprovadas (session/evidence — v043).
- **Migração v044** aplicada em `odca_test_disposable` (`CurrentVersion=44`, checksum `108c7c5e…10fc5` validado pelo migrador, snapshot `database/releases/odca-v044.sql`); health `Schema de banco incompatível` alinhado ao subir a versão canônica.
- **Cadeia pós-mudança (E→C→D→b34)**: **E 120/120** (novo S7: recovery code em reinício de sessão, consumo único, reuso rejeitado, sessão reiniciada autorizada; S6 ganha decisão via linha global) · **C 99/99** · **D 116/116** (D02: catálogo publicado visível sem instalação no tenant novo) · **b34 95/95** (T4.4: dedupe sem duplicata) — 0 falhas; logs `baseline-{e,c,d,b34}-20261010-03{22,24,24,26}*.log` em `%TEMP%\opencode\`. Fingerprint dos seeds **idêntico** ao baseline (`9b641fa0…`); 0 `qa-%` residuais.
- **Correções de suíte registradas**: ordem de cleanup da E (recovery codes antes de sessions — FK nova do S7) e `SqlBest` com stderr para arquivo (com `$ErrorActionPreference='Stop'`, `2>&1` virava erro terminante e matava o best-effort do cleanup). Nenhuma expectativa anterior foi alterada — só assertes novos (E 109→120, D 115→116, b34 93→95).
- **Git**: Bloco A publicado como commit único em `codex/s00-foundation` (seeder + Program, dedupe do VisibleFilter, exists-check da instalação, v044 + snapshot + bump do schema, lembrete honesto, views do cliente, DECISIONS-LOG D-OC1…D-OC6, auditoria, suítes) — push somente quando pedido. Decisões D-OC1…D-OC6 em `DECISIONS-LOG.md`.
- Próximos passos: **Bloco B** (registro central de navegação D-OC4 + menus cliente/plataforma completos + benefícios de plano ponta-a-ponta + administração global) → **Bloco C** (conferência documental, comparação, renovações/alterações, painel de pendências, notificações + SLA enterprise — D-OC6) → **Bloco D** (4 idiomas D-OC5, linguagem simples, design consistente, PDF renderizado validado, 360/768/1440) → documento de entrega com matriz plan×profile×action×menu e script de homologação.

## Estabilização — Bloco A fechado: auditoria aplicada em migração única v043 (assinatura rastreável por modalidade, aprovação vinculada à revisão, reviews.read) + reparo do pacote v042 + PDF por snapshot/WinAnsi//Info + metodologia D-A5 das suítes QA (identidades próprias, guarda de banco, try/finally, seed intacto): E 109/109 × 3, C 99/99, D 115/115, regressão B.3.4 93/93 (10/10/2026)

- **Auditoria estabilização A aplicada** (`docs/execution/AUDITORIA_ESTABILIZACAO_A.md`, decisões D-A1…D-A5): migração **v043** (`CurrentVersion=43`, checksum `b879b4e9c174…fe79245`, snapshot `database/releases/odca-v043.sql`) — assinatura registra **modalidade** (`session` com participante vinculado à própria conta e sessão em nível MFA / `evidence` com descrição), replay devolve contagem real (antes hardcoded), permissões separadas `sign_self`/`record` (preparar ≠ declarar terceiro assinado), **vinculação de identidade é ato próprio** (endpoint dedicado auditável; parâmetro `@membership` corrigido), aprovação cirúrgica **vinculada à revisão aprovada** (versão instalada + sha256) validada no readiness e `tenant.reviews.read` concedida no trio e tenants existentes.
- **Reparo do pacote v042 (defeito de empacotamento)**: bloco consolidado rehashado para o canônico `901a50fc9b4cf02bc18189b602df973102ae754f27926511700281eadb75ee9e`; `database/releases/odca-v042.sql` preservado como registro imutável do estado original — o migrador continua validando o checksum registrado.
- **PDF imutável e fiel**: geração usa exclusivamente o snapshot da versão (`created_at`/`emission_metadata` com CNPJ congelados — reimprimir não muda a data do documento); `renderer_version odca-pdf-2`; Unicode→WinAnsi correto (travessão/bullet não viravam `?`); dicionário `/Info`; estilos de capa/sumário/callout; numeração de página fora do bloco de texto.
- **Metodologia D-A5 das suítes QA (referência: suíte E)**: cada suita cria identidade própria (`qa-c-op`, `qa-d-op`, `qa-e-signer/recorder/reviewer/admin` @odca.local — senha = hash clonado do operador, seeds intocados), recusa banco cujo `current_database()` ≠ `odca_test_disposable`, executa o corpo em `try` com cleanup completo em `finally` e **asser ao final que os três usuários-semente permanecem intactos** (fingerprint: hash, estado MFA, recovery codes, `failed_login_count`, etc.).
- **Validação na stack real**: suíte **E 109/109 em 3 execuções consecutivas** · suíte **C reescrita 99/99** · suíte **D reescrita 115/115** (`qa-d-op` = platform-admin + `tenant-operator` nos fixtures) · regressão **`validate-b34-web.ps1` 93/93** (metodologia inalterada). `cleanup.seed_intacto` verde em todas as execuções.
- **Achados que guiaram a reescrita** (detalhados em `DECISIONS-LOG.md`): PowerShell 5.1 emite o array do `ConvertFrom-Json` como item único do pipeline (`@(...)` gerava wrapper falso — causa raiz de `S6.fila` na E); endpoints de escopo da organização exigem membership (platform-admin sem ela recebe 403); enrollment MFA restrito a platform-administrators; cleanup da E remove os modelos cirúrgicos do TC (dependência entre suítes coberta pelo install idempotente da biblioteca oficial no setup da C).
- **Git**: increment publicado como commit único em `codex/s00-foundation` (v043 + snapshot, reparo do bloco v042 consolidado, controllers/renderer/web das assinaturas, suítes C/D/E reescritas, auditoria e docs) — push somente quando pedido.

## D fechado — Design e homologação ponta a ponta; suíte web ALL PASS 111/111 + 2 bugs reais corrigidos (apply de renovação 500, vazamento multi-tenant); regressões B.3.4 e C 93/93 sem falhas (09/10/2026)

- **D fechado** — Identidade visual consolidada na paleta da SPEC §8 (`site.css` token-sync: fundo `#F4F7FB`, campo pendente `#FFF3BF`, foco AA via `--focus-ring`, zero hexos avulsos fora do `:root`); documento gerado com **capa + sumário automáticos** quando adequado (≥ 2 seções nível 1 ou quebras de página) e nó **`callout`** de primeira classe (`attention|info`) na tela e no documento; prova de que **documento final jamais contém variável não resolvida** (barreira 400 na geração apontando o campo; campo opcional vazio sai como travessão). CTA "Novo contrato" homologado; tabelas convertem ao padrão `.responsive-table` real (Bootstrap inerte removido).
- **Validação na stack real: ALL PASS 111/111** (`validate-d-web.ps1` versionado em `scripts/qa/`, UTF-8+BOM): D00 fixtures `Val D:` (2 organizações × 4 perfis) · D01–D20 cobrindo os **19 cenários mínimos** do plano (assert UI+API+banco cada), tokens/contraste WCAG calculados na suíte, classes `document-cover`/`document-toc`/`document-callout`, matriz ações×perfil×plano provada linha a linha, cleanup idempotente D99. Caderno: `docs/execution/RESULTADO_SECAO_D_V42_20261009.md`; matriz: `docs/execution/MATRIZ_ACOES_PERFIL_PLANO.md`; decisões D-D1…D-D6 em `DECISIONS-LOG.md`.
- **Duas falhas reais registradas e corrigidas**: **aplicar renovação de prazo indeterminado → 500 `42P08`** — `jsonb_build_object` com parâmetros sem tipo (`$6` ambíguo) no INSERT de auditoria de `RenewalCenterController.ApplyCore`: casts `::date/::numeric/::text` explícitos; **leak multi-tenant** — `SolicitationsController.Detail` respondia solicitação de outro tenant por ID direto: guarda de propriedade adicionada (404 fora do tenant). (Além de `document_versions.detected_type` = enum `pdf|png|jpeg|docx`, registrado como decisão D-D6.)
- **Homologação responsiva/acessibilidade**: trilha formal em **360/768/1440 px** de todas as telas canônicas + operação por teclado com foco sempre visível + contraste WCAG AA calculado nos pares reais — evidências PNG em pasta externa de trabalho (`%TEMP%\opencode\evidencias-d\`, convenção A–C: nunca commitadas), nomes citados no caderno D.
- **Regressões**: `validate-b34-web.ps1` **93/93** e `validate-c-web.ps1` **93/93** após TODAS as mudanças de D (incl. os 2 fixes de produto); ambiente restaurado pós-suíte (fixtures `Val D:` = 0, plano TC BASIC, operador rebaixado/MFA limpo).
- **Git**: Seção D publicada como commit único em `codex/s00-foundation` (renderer, templates, controllers, visões, site.css, docs e suíte) + push solicitado. Com D, **o MVP (seções A–D) está completo**.

## C fechado — Solicitações/SLA + aprovação ODCA dos modelos (migração v042); suíte web ALL PASS 93/93 × 3 execuções + 3 bugs reais corrigidos; regressão B.3.4 93/93 sem falhas (09/10/2026)

- **C fechado** — Solicitações (revisão/adaptação/esclarecimento) com máquina de estados `aberta→triagem→em_atendimento→aguardando_cliente→resolvida→encerrada` (+`cancelada`), SLA por plano/serviço/prioridade com snapshot/fuso/calendário no abrir, pausa/retomada justificada deslocando prazos, reatribuição sem reiniciar relógio, violações únicas via `SlaViolationScanWorker` (`violacao_*`), fila da Central ODCA e aprovação formal dos modelos cirúrgicos que libera (de forma reversível) o blocker `approval.odca.required`. Migração **v042** aplicada (`CurrentVersion=42`, checksum `cfb7990e…0947f`).
- **Validação na stack real: ALL PASS 93/93** (`validate-c-web.ps1` versionado em `scripts/qa/`, UTF-8+BOM, 3 execuções consecutivas): S1 gate Enterprise · S2 abertura/protocolo/SLA · S3 idempotência · S4–S7 triagem/início/stale-409+`currentRowVersion` · S8 pausa/retomada · S9 aguardar cliente/auto-retomada/`retorno_cliente` · S9b resposta ODCA · S10 403 `permission_odca` · S11 resolver/encerrar/auditoria · S12 cancelamento · S13 listas+fila plataforma · S14 violações · S15 calendário comercial · S16 aprovação ODCA (nota mínima, decisor, auditoria) · S17 ciclo do blocker de readiness. Caderno: `docs/execution/RESULTADO_SECAO_C_V42_20261009.md`.
- **Três falhas reais registradas e corrigidas**: **lista 500** — `solicitations_list` devolvia `varchar(200)` onde `RETURNS TABLE` declara `text` (PL/pgSQL não coerciona → 42804): casts `::text` em `subject`/`cancellation_reason` (v042 + snapshot + função viva); **fila de aprovações 500** — `set_config('odca.user_id', uuid)` sem resolução (42883) e GUC transacional invisível à query seguinte (42501): `@actor::text` + escopo de sessão em `EnsurePlatformGuc`; **`currentRowVersion` nulo no 409** — `EnvelopeResult` lia a chave errada `"currentVersion"` (corrigido no início do ciclo). Suíte também registrou correções próprias (TOTP base32 `PadLeft`, reset MFA de 4 colunas, refresh de `rowVersion` entre blocos).
- **Regressão**: `validate-b34-web.ps1` reexecutada **93/93**; ambiente restaurado e conferido pós-suíte (fixtures `Val C:` = 0, tenant BASIC, operador rebaixado/MFA limpo). Decisões D-C1…D-C11 em `DECISIONS-LOG.md`; entrega completa em `docs/ENTREGA-CONCLUSAO-SISTEMA.md`.
- **Git**: Seção C publicada como commit único em `codex/s00-foundation` (controllers API/Web, contracts, visões, Worker, v042 + snapshot, docs e suítes) — push somente quando pedido.
- Próximos passos: **seção D** (design + homologação: 360/768/1440 px, teclado/foco/contraste, matriz ações×perfil×plano, 19 cenários mínimos com evidências UI).

## B.3.4 fechado — campos por perfil de atuação (overlay + fórmula + gate cirúrgico); suíte web ALL PASS 93/93 + 3 bugs reais corrigidos (form Web, CPF de teste, Dapper ReadinessRow); migração v041 (08/10/2026)

- **B.3.4 fechado** (subplano B.3): regras por campo por perfil de atuação — `odca.tenants.activity_profile` (`general`/`therapy_clinic`/`plastic_surgery`, migração **v041**, editável na tela da organização via Web e `PUT /api/v1/organizations/{t}` com gate de plano (409 `organization.profile.plan_required` para `plastic_surgery` sem Enterprise; 400 `activityProfile` para valor inválido; conflito de versão segue 409 sem `code`).
- **Overlay + fórmula explícita (D2)**: na criação de minuta nova a partir do template oficial escolhido, perfil `therapy_clinic` × `multiple-therapies` injeta cláusula 2ª-A de sessões + MENSALIDADE TOTAL (`ActivityProfileOverlays.cs`, idempotente); novos campos `sessions_*` (Number, RequiredWhen `therapy_*=Sim`) + `monthly_fee_total` (tipo somente-leitura `Formula`, Required=false) com `monthly_fee_total = Σ(sessões × valor)`; servidor recalcula em salvar/preparar/gerar — vazio é preenchido com o canônico (`FillFormulaValues`), divergente é rejeitado em todos os modos com a mensagem `O valor de '{Label}' não confere com a fórmula declarada (esperado: …)`; cliente (`studio.js`) faz a mesma conta em BigInt escala 10^8 (half-up).
- **Gate cirúrgico (D3)**: template oficial mínimo `surgical-consent` (purpose `consent`); `plastic_surgery` exige plano Enterprise; minutas cirúrgicas ganham estado visual pendente (banner `data-pending-odca-approval` + blocker `approval.odca.required` em `ContractStudioController.CalculateReadiness`) que trava o preparativo de assinatura até a seção C liberar o modelo; criação de draft cirúrgico fora do perfil → 409 `draft.profile.surgical_required`.
- **Validação na stack real: ALL PASS 93/93** (`validate-b34-web.ps1` em Temp, UTF-8+BOM, duas execuções estáveis): T0 gate/baseline/login · T1 regras de perfil API (400/204/409+code/versão obsoleta) · T2 tela Web (select, gate Enterprise no form, toast de sucesso) · T3 `studio.js` (máscara BR, BigInt, rejeição) · T4 instalação dos 9 modelos oficiais + catálogo · T5 gate cirúrgico (409/201, tipos dos campos, responsável legal condicional, banner) · T6 validação de servidor (CPF inválido/válido, data inválída, moeda pt-BR canônica, texto em moeda) · T7 overlay + fórmula (4 termos, semeado `0.00`, autofill de organização, divergente→400 com esperado, vazio→autofill, obsoleto→400, perfil geral sem overlay como controle) · T8 bloqueio ODCA (geração 201, readiness 200 com blocker + `canConfirm=false`, confirmação 400 citando aprovação, versão não-cirúrgica sem blocker).
- **Três falhas reais registradas e corrigidas**: **form Web de perfil** — `Esc [string]$org.version` em posição de argumento de comando vira string literal (`[string]@{version=42}.version`) → `Version` inválido no POST, ModelState rejeitava sem chamar a API (bug da suíte, corrigido com cast prévio em variável); **T6.1/T8.1** — CPFs de teste `52998224725` (aceito: dígitos válidos) e `52998224720` (usado como "válido" quando é inválido) invertidos nos asserts (dados de teste corrigidos, não bug de produto); **Detail-500 readiness** — `ReadinessRow` ganhou `OfficialKey` no final do record mas o SELECT coloca a coluna na posição 5 → Dapper não achava construtor correspondente → reordenado o record para espelhar o SELECT (`ContractStudioController.cs:1456`).
- **Banco/migrações vigentes**: schema **v041** (`CurrentVersion=41`, checksum `ef78cb21…5e411d` conferido com a receita do migrador, snapshot `database/releases/odca-v041.sql` byte-idêntico ao consolidado); `bootstrap migrate` re-executado e `/health/ready` healthy (41|41|41).
- **Git**: publicado como commit único da etapa em `codex/s00-foundation` (base `e1ee6b0` de B.3.3), incluindo v041 SQL, tenancy, Formula, overlays, template cirúrgico, controller/visões, studio.js, docs e suíte — push somente quando pedido. Detalhamento: seção B.3.4 de `ENTREGA_JORNADA_MVP_20261006.md`.
- Próximos passos: **seção C Enterprise ODCA/SLA** (aprovação de modelos cirúrgicos — libera o blocker `approval.odca.required`) → D design + homologação (19 cenários, incluindo screenshots em pixels).

## B.3.3 fechado — Central de Renovações e Prazos; suíte web ALL PASS 94/94 + 3 bugs reais corrigidos (detail Dapper, cancelled_at); commit único da jornada (07/10/2026)

- **B.3.3 fechado** (subplano B.3): central de renovações e prazos ponta-a-ponta — índice com filtros/perfis e prioridades baixa/normal/alta/crítica, detalhe do request (condições atuais vs. propostas, candidatos de evidência safe, timeline de eventos), submeter (+conflito de versão), cancelar com motivo, formalização manual com justificativa, aplicação restrita à central (apenas formalized + vencimento da vigência), lembretes 30/15/7 configuráveis por tenant (`renewal_reminder_days`), auditoria e cleanup; sem renovação automática e prazo indeterminado aceito.
- **Validação na stack real: ALL PASS 94/94** (`validate-b33-web.ps1` em Temp, UTF-8+BOM): fases T0 (fixtures idempotentes), I (índice/filtros), S/D (detalhe por perfil), C (cancelar + `cancelled_at` + evento com motivo), F (formalização manual + evidência), A (aplicar + linha de aplicação), CFG/R (lembretes), H (timeline de auditoria), CU (cleanup conferido no banco) — via Web :7144 + API :7143 + SQL direto.
- **Três falhas reais registradas e corrigidas** (API `RenewalCenterController.cs`): **Detail-503 (quebrado desde o nascimento)** — Dapper não materializava `RenewalRequestDetails` (25 parâmetros) a partir de query de 23 colunas → row privado `RenewalRequestRow` (23 parâmetros espelhando os aliases) + construção explícita do DTO público (padrão `ReviewRequestsController`/`ApplyRow`); **DateTimeOffset × DateTime** — Npgsql lê timestamptz como `DateTime` e a seleção de construtor do Dapper exige tipo exato (`DateTimeOffset?` quebrava o match na row, nos eventos e nas evidências) → row records com `DateTime` + helper `ToUtcOffset` por controller (overload nullable); **Cancel sem timestamp** — UPDATE passou a setar `cancelled_at=now()`.
- **Asserções de suíte ajustadas (não bugs de produto)**: T0 esperava 3 evidências safe (fixtures têm 2 safe + 1 pending; contagem escopada por ID dos fixtures + coluna total de dvs → `2|4|2|3`); C1/CU comparavam booleano canônico `'t'` — o PG v18 imprime `'true'/'false'`.
- **Banco/migrações vigentes**: schema **v040** (`CurrentVersion`), snapshots `odca-v038/039/040.sql` em `database/releases/`; os 94 asserts executaram sobre `odca_test_disposable`; fixtures removidos pelo cleanup da suíte.
- **Git**: commit único contendo toda a jornada MVP desde `8ac10dd` (B.1 bloqueio OCR/importação/upload · B.3.1 menus + permissões do cliente · B.3.2c ações de contrato na ficha + impressão RASCUNHO + histórico · B.3.3 central de renovações) + docs/releases.
- Próximos passos: **B.3.4 campos por perfil de atuação** → C Enterprise ODCA/SLA → D design + homologação (19 cenários, incluindo screenshots em pixels).

## B.3.2c fechado — ações de contrato na ficha + impressão "RASCUNHO" + histórico; suíte web ALL PASS + migração v039 (07/10/2026)

- **B.3.2c fechado** (subplano B.3, item 4): cada ação de contrato exposta na ficha `/organizacoes/{tenantId}/contratos/{contractId}` com permissão/estado/validação/feedback exato/auditoria, em camadas A–D (DTOs em `src/Odca.Contracts/Contracts/` (novo) · repositório `ContractSheetRepository.cs` sob `FOR UPDATE` com eventos `contract_events`+`audit_events` · controller de contrato na API (novo) + ações BFF no `ContractsController` Web + `OdcaApiClient` · telas `Sheet.cshtml` + `Print.cshtml` (novo)); estados preservados: elaboração ≠ assinatura ≠ vigência, duplicação não copia assinaturas, arquivar ≠ rescindir, sem exclusão física, concorrência por `?version=N`.
- **Validação na stack real: ALL PASS** (execução #8 de fechamento, `validate-b32c-web.ps1` em Temp, após reset limpo): fases T0–T8 (detalhes/atualizar v2→v3/arquivar v4+replay/restaurar v5+replay/duplicar/validação de encerramento), B1–B6 (draft→confirm→versão 201→PDF→preparação→detalhe), W1–W8 (lembrete/assinatura/replay/all-signed/too_soon/PDF 1618 B `%PDF`/marca d'água ASSINADO sem RASCUNHO), C1–C3 (encerrar closed+archived/badge/duplicar-encerrado 409), H1–H2 (histórico), O1–O3 (operador sem `contracts.*`: ficha via drafts.read, forms ausentes, POST arquivar negado e canônico segue ativo), R1–R2 (lista com exatamente 1 ativo); persistência confirmada no banco em cada fase mutacional.
- **Três falhas reais registradas e corrigidas** (ação/expected/observed/causa raiz/correção/regressão na entrega): **B6** materialização do Dapper por construtor (`SignatureParticipantInput.SignedAt` de `DateTimeOffset?`→`DateTime?`, `StudioContracts.cs:89`; repro isolado `b6-probe2` OK); **W1** AnchorTagHelper 503 por `href` literal + `asp-route-*` juntos (helper estático `BuildReturnUrlQuery` no controller + `pdfBaixarQuery` no bloco `@` da view); **v039** grants m038 derivavam `tenant.contracts.read/manage` de `contract_drafts.*` e o operador arquivava (O2×3/O3/R2 falharam na execução #5) — migração v039 DELETE dos dois grants no `tenant-operator` (matriz aprovada, opção B), `CurrentVersion` 38→39, snapshot `database/releases/odca-v039.sql` byte-idêntico ao consolidado; aplicada e validada em banco (MIG 39 checksum `d2df475b…fe7496`, operador sem nenhum `tenant.contracts.%`, admin/cliente intactos, readiness OK, serviços reiniciados).
- **Contrato Web de 403 descoberto (execução #6)**: a API responde 403 problem+json (prova direta com token do operador); a Web mapeia via `Forbid()` → 302 `/acesso-negado` ("Acesso negado") e o follow termina em 200 — assert da suíte alinhado ao contrato real (não é bug de produto). `reset-b32c.ps1` reforçado (limpa `archived_*`/`closed_*` no canônico + version=1).
- **Banco/migrações vigentes**: schema **v039** no banco `odca_test_disposable` e no consolidado `database/odca.sql` (hash verificado); snapshot de release `odca-v039.sql`. Git `codex/s00-foundation` @ `8ac10dd` sujo (uncommitted — publicar somente quando pedido). Detalhamento: seção B.3.2c de `ENTREGA_JORNADA_MVP_20261006.md`.
- Próximos passos: **B.3.3 central de renovações e prazos** (prioridades baixa/normal/alta/crítica, datas separadas, prazo indeterminado, lembretes 30/15/7 configuráveis, sem renovação automática, aplicação restrita à central) → B.3.4 campos por perfil de atuação → C Enterprise ODCA/SLA → D design + homologação.

## B.3.1 fechado — menu do cliente + permissões na Web; suíte 35/35 ALL PASS (07/10/2026)

- **B.3.1 fechado** (subplano B.3, item 1): menus do cliente e da plataforma completos em `_Layout.cshtml` (cliente: Início · Pacientes · Contratos {Modelos disponíveis, Meus contratos, Minhas minutas, Novo contrato} · Assinaturas · Renovações e prazos · Operações {Caixa/Obrigações/Filtros salvos} · Equipe e permissões · Minha organização e plano · Suporte); **"Solicitações ODCA" escondido quando o plano restringe a feature `reviews`** nos dois blocos; renomeações "Equipe e perfis"→"Equipe e permissões" e "Renovações e aditivos"→"Renovações e prazos"; nova rota `GET /organizacoes/{tenantId}/contratos` (rota absoluta `~`, pois o template do controller carrega `{contractId}`) com lista, busca, filtro de status, badges e paginação (`Views/Contracts/Index.cshtml`); 10 métodos de contrato no `OdcaApiClient.cs` sobre os endpoints já validados em B.3.2b.
- **Validação na stack real: ALL PASS 35/35** (`validate-b31-web.ps1`, Temp): login do cliente (`/entrar` c/ antiforgery global), menu íntegro com ausência efetiva do item Enterprise, contagens conferidas com o banco (active=1 canônico `b0139659-…` · all=11 · closed=8 · archived=2 · bogus→active), linha canônica com badge "Ativo" e link para a ficha, botão "Novo contrato" → `/documentos/novo`, regressões limpas (ficha/Inbox/Documentos/modulos 200). Asserts com acento passando com script UTF-8+BOM confirmam HTML servido correto (os `?` do console eram artefato de codepage PT-BR/CP1252).
- **Falha real encontrada e corrigida (A.6)**: 503 na lista — a ação chamava `View(model)` e procurava `List.cshtml` (o arquivo é `Index.cshtml`); correção `View("Index", …)` + rebuild/restart. Tooling registrado em A.7: rota do login é `/entrar` (não `/account/entrar`), antiforgery global exige token no POST, `.ps1` sem BOM lido como CP1252 pelo PS 5.1 (falsos FAILs de acentos) e probe de readiness esperando `"status":"ready"` (o endpoint responde `"healthy"`).
- Próximos passos: **B.3.2c** (ações sobre os endpoints B.3.2b na ficha + impressão "RASCUNHO" + histórico via `GetContractEventsAsync`) → B.3.3 central de renovações → B.3.4 campos por perfil de atuação. Menu da plataforma (admin c/ MFA) exercitado ponta-a-ponta na matriz da seção D (2 organizações × 4 perfis); screenshots em pixels também pendem lá. Git `codex/s00-foundation` @ `8ac10dd` sujo, sem commit/push. Detalhamento: seção B.3.1 + A.6/A.7 de `ENTREGA_JORNADA_MVP_20261006.md`.

## B.1 fechado + colateral da suíte recuperado: cenário canônico recriado com novos IDs (06/10/2026)

- **B.1 (bloqueio OCR/importação/upload) fechado**: middleware `ReleaseFeatureGate : IMiddleware` (config-driven, fail-closed) + flags `Features:ContractImports`/`DocumentUpload`/`OcrExtraction=false` no `development-runtime.json`; resposta problem+json `release_feature_disabled` (503) **antes** do auth. Menu do cliente sem itens de importações (`_Layout.cshtml`), card "em breve" em `NewDocument.cshtml`, texto do módulo documentos reescrito (`ModulesController`), banner em `Views/Imports/Index.cshtml`. Build da solução 0/0. Bateria HTTP aprovada (`gate-b1-check.ps1`) e re-confirmada no smoke pós-restauração: contract-imports GET/POST 503, upload multipart 503, extractions POST/GET/apply 503, anônimo 503, negativos intactos (GET documents 200). Suíte de integração: **164/164** (1m09s). UI verificada por snapshot de DOM logado como cliente (menu limpo, banner da rota direta, card em breve, módulo documentos); screenshots em pixels pendentes — janela do browser desktop oculta e tool de screenshot exige janela visível (fica para a seção D). Detalhamento completo: seção B (B.1/B.2) de `ENTREGA_JORNADA_MVP_20261006.md`.
- **Bug DI corrigido neste item**: `ReleaseFeatureGate` não estava registrado no container → 500 em todo request; correção `builder.Services.AddTransient<ReleaseFeatureGate>();` em `Program.cs` (~linha 29). Registro ação/expected/observed/causa/correção/regressão na entrega B.1.3.
- **Efeito colateral da suíte identificado e recuperado**: `DevelopmentAccessProvisioningTests.ResetScenarioAsync()` faz wipe da closure transitiva de FKs dos tenants demo a cada execução da classe (por design, isolamento dos testes de provisioning) — apagou as linhas canônicas dos GATES 2/3 e resetau senha/MFA dos 3 usuários dev. Restauração: snapshot MFA pré-suíte (`mfa-snapshot-b1`) + `odcatools restore` + UPDATE `security_version=8`/`must_change_password=false` + `provision-test-access --rotate-passwords --allow-immediate-login`; smoke 3 contas verde (admin com desafio MFA 200). Regra operacional para os próximos ciclos: antes de suíte, snapshotar também as linhas de negócio do cenário ativo ou planejar recriação (scripts reaproveitáveis em `Temp\opencode\restore-gates*.ps1`; payloads PT-BR fora do PowerShell — PS 5.1 lê `.ps1` sem BOM como CP1252 e corrompe acentos; recidiva do splat `-H @auth` contra curl nativo documentada em B.2.4).
- **Novos IDs canônicos vigentes** (recriados via API, persistência confirmada API + banco, auditoria `template.created`/`template.published` success): template publicado `261516a2-02ef-43bb-abdc-8bcf48701d5a` (row_version=3), paciente `75c87013-4595-41dd-93d9-ac2e9c1f8fc4`, rascunho `35cfbd2f-433b-4398-9064-723dafb1c3d6`, contrato `b0139659-75b7-4782-a250-ae41d29490e1` (nasce junto com o draft). Primeiro template recriado (`a56ea697-…`, mojibake) arquivado via API; par v1 (draft/contrato) removido por SQL pela closure de FK mapeada.
- Ambiente: stack no ar (API :7143, Web :7144 HTTPS, banco `odca_test_disposable`, schema v037); admin `secver=8`, `mfaConfirmed=true`, 8 recovery codes; git `codex/s00-foundation` suja com todos os arquivos do B.1, sem commit/push. Abertos: B.3 (menus/propostas + permissões, ações completas de contrato, renovações, campos por perfil de atuação), C (Enterprise ODCA/SLA), D (design + homologação 19 cenários, incluindo screenshots).

## Jornada documental MVP — gates 1–3 aprovados e seção A registrada (06/10/2026)

- Estado: branch `codex/s00-foundation` @ `8ac10dd`; stack local no ar (API :7143, Web :7144, banco `odca_test_disposable`, schema v037); detalhamento e evidências em `docs/execution/ENTREGA_JORNADA_MVP_20261006.md`.
- GATE 1 (login + MFA) aprovado, incluindo regressão pós-restart: login 200 `requiresMfaChallenge=true` (`security_version=7`) e desafio TOTP 200 `mfaVerified=true` com chaves de Data Protection persistidas. As falhas intermediárias do teste foram de tooling (curl.exe ausente no temp; quoting PowerShell 5.1 em `-Command`; CLI do odcatools alterada), não do servidor.
- GATE 2 (superadmin cria/salva/publica modelo) aprovado: template `b4f11df0-…` criado (201), atualizado com `expectedVersion=1` (200, `rowVersion=2`) e publicado (`?expectedVersion=2` → 200, `status=published`, `rowVersion=3`); persistência conferida em `contract_templates`/`contract_template_versions` e auditoria `template.created` + `template.published` no banco.
- GATE 3 (cliente cadastra paciente e cria rascunho persistido) aprovado: paciente 201 (`96f2f892-…`, CPF normalizado) e rascunho 201 (`bb16de93-…`/contrato `6cc5408e-…`) a partir do template publicado; linhas em `odca.patients`, `odca.contract_drafts` e `odca.contracts` verificadas.
- Correções do ciclo registradas na seção A da entrega: 401 de login por desincronização do arquivo local de credenciais × hash no banco (corrigida com `provision-test-access --rotate-passwords --allow-immediate-login`); 403 de MFA `regenerate` em conta confirmada (por design); falhas falsas de tooling de teste; splat `-H @auth` contra curl nativo (401 + saída 3). Sem OCR/importação/upload neste ciclo (fora do menu do cliente e bloqueados no servidor quando os menus forem implementados).
- Abertos: B (jornada documental completa do cliente, menus e permissões, ações de contrato, renovação, campos por perfil de atuação), C (Enterprise ODCA/SLA) e D (design e homologação 19 cenários).

## Auditoria obrigatória do MVP no checkout `17762a9` (21/09/2026)

### Baseline executado antes de alterações

- A execução começou limpa na branch `work`, em
  `17762a90a60709a7d5ee3d171a7ec5ac7ba495ef`. Foram lidos o README, a
  especificação, o status, backlog, próximo prompt, documentos de privacidade,
  snapshots SQL 001–019, composição dos hosts e os controllers/services dos
  domínios pedidos. A existência desses artefatos não foi considerada evidência
  de funcionamento.
- `npm run build` terminou com código zero e validou os sete assets. O runtime
  disponível, entretanto, é Node.js 20.20.2, abaixo do Node.js 24+ exigido em
  `package.json`; por isso o resultado é evidência estática, não aprovação do
  ambiente suportado.
- `dotnet restore Odca.sln --locked-mode`, `dotnet build Odca.sln
  --configuration Release --no-restore` e `dotnet test Odca.sln --configuration
  Release --no-build` foram realmente iniciados e terminaram com código 127:
  não há executável `dotnet`, logo o SDK fixado 10.0.400 não pôde ser validado.
- Também não existem `psql`, `pg_isready` ou `docker`. Não foi possível criar
  um banco descartável `odca_test*` no PostgreSQL 18 com a role restrita
  `odca_test_app_login`, aplicar/reaplicar migrations, validar checksums em banco,
  provar RLS A × B, reuso do pool ou executar os testes de integração.

### Decisão do gate

- O gate obrigatório do Bloco A permanece **bloqueado por infraestrutura
  externa**. Em particular, login/MFA, Web/API/Worker, autorização real e QA nos
  breakpoints não foram executados nem inferidos da implementação existente.
- Em cumprimento à ordem explícita do ciclo, nenhuma migration histórica foi
  editada, nenhuma migration v020 foi necessária e nenhuma funcionalidade dos
  blocos B.1–B.6 foi iniciada. O próximo executor deve fornecer exatamente .NET
  SDK 10.0.400, Node.js 24+ e PostgreSQL 18 (ou engine de contêiner) e repetir o
  baseline antes de qualquer avanço funcional.

## Revalidação do gate no checkout `3c2d0ee` (21/09/2026)

### Auditoria e sincronização

- A execução começou sem alterações locais, na branch de trabalho `work`, exatamente
  no commit solicitado `3c2d0ee9250afc3ee732316d3155fd9a82bdff3d`.
- O checkout não possui remoto Git configurado nem referência local denominada
  `codex/s00-foundation`. Por isso, `git fetch origin codex/s00-foundation` não pôde
  sincronizar conteúdo. Como o `HEAD` já coincide com o commit indicado pelo prompt,
  nenhum merge, reset ou descarte de alterações foi realizado.

### Mapa do estado real

- **Pronto com evidência neste ambiente:** `npm run build` validou os sete assets
  Web e terminou com código zero.
- **Parcial:** há implementação e testes versionados para autenticação/MFA,
  permissões, documentos, contratos, consumo, privacidade, migrations e isolamento,
  mas sua presença no repositório não foi tratada como prova de funcionamento.
- **Quebrado:** não foi identificada uma regressão executável do produto neste ciclo;
  o gate, contudo, permanece reprovado por não ser possível executar três dos quatro
  comandos obrigatórios.
- **Não validado por ambiente:** restore, build e testes .NET; PostgreSQL 18
  descartável; aplicação/reaplicação e checksums das migrations 001–019; RLS A × B;
  isolamento do pool; login HTTP dos três perfis; MFA; autorização; inicialização de
  API, Web e Worker; e QA visual nos breakpoints/zoom exigidos.

### Evidência e causa raiz do bloqueio

- `dotnet restore Odca.sln --locked-mode`, `dotnet build Odca.sln --configuration
  Release --no-restore` e `dotnet test Odca.sln --configuration Release --no-build`
  terminaram com código 127 porque `dotnet` não existe no contêiner. Assim, a versão
  obrigatória 10.0.400 também não pôde ser confirmada.
- Não há `psql`, `postgres`, `pg_ctl`, `initdb`, `docker` ou `podman`. Não existe,
  portanto, um mecanismo local disponível para criar o banco `odca_test*` em
  PostgreSQL 18 e a role restrita `odca_test_app_login`.
- O Node disponível é 20.20.2, abaixo do requisito `>=24` declarado pelo projeto.
  Mesmo assim, o verificador de assets executou com sucesso; isso não substitui o
  gate no runtime requerido.
- Causa raiz: a imagem de execução não fornece os runtimes de .NET e PostgreSQL nem
  um engine de contêiner. Em respeito ao gate entre blocos, nenhuma migration foi
  alterada e nenhuma funcionalidade dos blocos B.1–B.6 foi iniciada.

## Revalidação do gate no checkout entregue (21/09/2026)

- O checkout recebido está na branch de trabalho `work`, no commit solicitado
  `68e04bf950251bb53e9e9960e021eda3d1f434c9`, sem alterações locais no início da
  execução. Esse commit coincide com a referência fornecida, embora o nome da
  branch ativa não seja `codex/s00-foundation`.
- `npm run build` foi reexecutado e validou os sete assets Web.
- Os gates `dotnet restore Odca.sln --locked-mode`, `dotnet build Odca.sln
  --configuration Release --no-restore` e `dotnet test Odca.sln --configuration
  Release --no-build` foram iniciados, mas o contêiner não possui o executável
  `dotnet`. A tentativa de obter o SDK 10.0.400 nos endereços oficiais foi
  bloqueada pelo proxy do ambiente com HTTP 403.
- O contêiner também não fornece `docker`, `podman` ou `psql`; portanto não foi
  possível criar o PostgreSQL 18 descartável nem produzir evidência nova de
  migrations, checksum, RLS A × B, isolamento do pool, login/MFA ou HTTP real.
- Em cumprimento à ordem dos gates, nenhuma funcionalidade dependente foi
  avançada e nenhuma homologação anterior foi promovida. O próximo incremento
  deve começar em um executor com .NET 10.0.400 e PostgreSQL 18 (ou Docker),
  repetir toda a matriz e somente então tratar as lacunas funcionais.

## Gate do MVP operacional — contexto documental e auditoria local (21/09/2026)

- Branch de trabalho: `feat/mvp-operacional-validacao-superadmin`, criada sobre o estado integrado de `codex/s00-foundation` (`01295a1`). O checkout fornecido não possui remoto configurado; portanto, pull/rebase remoto não pôde ser executado.
- Auditoria do ambiente: os snapshots imutáveis 001–019, a solution e os lockfiles estão presentes. O contêiner não possui .NET 10.0.400, servidor PostgreSQL 18 nem Docker (somente `pg_config` 16), impedindo restore/build/testes .NET, banco descartável, login HTTP e QA autenticado. `npm run build` validou os sete assets Web.
- O fluxo documental agora configura `odca.user_id` e `odca.tenant_id` com `set_config(..., true)` dentro da mesma transação usada pela autorização, leitura, upload, extração e aplicação. A consulta de limites do upload também passou a usar contexto e transação próprios, evitando depender de estado residual do pool.
- O download pelo BFF mantém a dupla verificação de documento `safe` e autorização da API, mas passa a responder com `Content-Disposition: inline`; o nome visível é codificado e nenhum bearer token é enviado ao navegador.
- Este incremento **não declara o MVP concluído**: Central completa do Superadministrador, suporte temporário, operação autenticada LGPD, prova PostgreSQL/RLS/login e capturas responsivas continuam bloqueados pelos gates descritos acima e permanecem na próxima execução.

## Documentos, importação, confirmações e agenda (21/09/2026)

- Estado da entrega: branch `feat/documentos-importacao-confirmacoes` a partir de `codex/s00-foundation` @ `a4ca12c`.
- Gates: `dotnet restore --locked-mode` ✔ · `dotnet build -c Release` ✔ · `dotnet test` 184/184 ✔.
- Template oficial por Key canônica (type + scope), nunca por name nem audit_events:
  - `ContractSheetRepository.cs`: `publishedCount` conta por `CASE WHEN contract_type IN (...) ... THEN 'key'` sem `AND t.name = ANY(...)`.
  - `ContractStudioCatalogController.cs` (PR anterior): lookup usa `contract_type` e campo NDA discriminante; sem referência a `audit_events`.
- ImportId resolvido no servidor:
  - `ContractSheetRepository.cs`: query agora retorna `i.id` (`Guid?`) em vez de `EXISTS`; `HasImportAwaitingReview` derivado de `importId.HasValue`.
  - `ContractSheetDto`: campo `ImportId: importId` passado ao DTO.
  - `Sheet.cshtml`: chip navega para `/importacoes/{ImportId.Value}` (Review) quando `ImportId` existe, fallback para lista `?awaitingReview=true`.
- _ConfirmDialog ligado em todas as ações de mutação na ficha:
  - `start`, `reschedule`, `reassign`: botões trocados de `type=submit` para `type=button` com `data-confirm-open`; forms com `id` explícito.
  - `CreateObligation`: form recebeu `id="create-obligation-form"`, botão com confirmação explícita.
  - `StartOfficialDraft`: form com `id="draft-form-{key}"`, botão com confirmação e nome do template.
  - `InstallOfficialLibrary`: form com `id="install-library-form"`, botão com confirmação.
- #documentos — download seguro:
  - `ContractsController.DownloadDocument` (GET): verifica `SafetyState == "safe"` via ficha; se quarentena, `TempData["ContractSheetError"]` e redirect `#documentos`. Se safe, retorna `File(bytes, contentType, fileName)`.
  - `Sheet.cshtml`: seção #documentos renderiza botão "Baixar" apenas se `SafetyState == "safe"`; caso contrário, label "Quarentena".
- Agenda — `?day=` e empty-state:
  - `AgendaController`: aceita `int? day`; filtra `Days` por `d.Day.Day == day`; se `Total == 0`, seta `TempData["AgendaEmptyNotice"]`.
  - `AgendaWorkspaceViewModel`: novo campo `int? Day`.
  - `Agenda/Index.cshtml`: exibe banner `banner-notice-warning` quando mês vazio; chip de filtro de dia ativo com botão remover; empty-state com link para caixa.
- Caixa — faixa role=status para atrasadas:
  - `Inbox/Index.cshtml`: renderiza `<div class="banner-notice-warning" role="status">` com contagem e link `urgency=Overdue&viewId=...` quando `Model.Page.Overdue > 0`.
  - `site.css`: `.banner-notice-warning` adicionado com cor âmbar (#fff3cd / #856404), flex.

## Avisos, confirmação e formalização/aplicação de renovação (21/09/2026)

- Estado da entrega: branch `feat/avisos-confirmacao-renovacao` a partir de `codex/s00-foundation` @ `3bc6f58`.
- Separação estrita de erros e ausência de captura 4xx no middleware:
  - `BffErrorHandlingMiddleware` monitora estritamente exceções e status 500/503; códigos 400 (Bad Request) e 409 (Conflict) nunca são interceptados como indisponibilidade.
  - 400 Bad Request exibe mensagens e validações diretamente no formulário (`asp-validation-summary`).
  - 409 Conflict exibe diálogo modal com mensagem amigável: "O registro mudou. Recarregue." com botão de recarregar (`location.reload()`) e fechar.
- Sistema de notificações e toasts (`_Toasts.cshtml`):
  - Integrado ao `_Layout.cshtml` consumindo `TempData` de sucesso, erro, aviso e conflito.
  - Sucesso (`role="status"`): auto-dismiss após 8s (8000ms) ou via botão de fechar.
  - Erro (`role="alert"`): persiste na tela até ação explícita no botão "Fechar".
  - Layout responsivo em `site.css`: fixo no canto inferior direito para desktop (>=768px), largura total na base da tela para mobile (<768px). Não obstrui a subnav sticky na ficha do contrato. Compatível com zoom 200%.
  - Tecla Escape fecha notificações e diálogos.
- Catálogo de modelos oficiais por chave:
  - Consulta do catálogo oficial (`ContractStudioCatalogController.GetOfficial`) busca diretamente em `OfficialContractTemplates.All` por `key` e resolve o modelo em `odca.contract_templates` por `name`, eliminando dependência de `odca.audit_events`.
- Diálogo nativo de confirmação (`_ConfirmDialog.cshtml`):
  - `<dialog id="confirm-modal" data-confirm-dialog aria-modal="true">` reutilizado em todo o sistema via delegação de eventos em `dialogs.js` com suporte a `data-confirm-open`, `data-confirm-form`, `data-confirm-title`, `data-confirm-body` e `data-confirm-action`.
  - Confirmação com nome visível do item implementada para:
    - Cumprimento de obrigação (`fulfill`) com nome da obrigação.
    - Cancelamento de obrigação (`cancel`) com nome da obrigação.
    - Reabertura de obrigação/apontamento (`reopen`) com nome do apontamento/obrigação.
    - Registro de proposta de renovação/aditivo na ficha com título do contrato e aviso de que a proposta deixa de ser apenas intenção e tem efeito em vigência somente após formalização e aplicação.
  - Tecla Escape fecha o diálogo e restaura o foco ao botão de origem.
- Formalização e aplicação exclusiva na Central de Renovações (`/renovacoes`):
  - Proibido `/apply` na ficha do contrato; aplicação restrita a `/organizacoes/{tenantId}/renovacoes/{requestId}/aplicar`.
  - Diálogo de confirmação com texto estrito: *"Isto altera a vigência. A proposta deixa de ser só intenção."*.
  - Toast de sucesso exibe o `ends_on` resultante da nova vigência.
  - Aplicação transacional com bloqueio pessimista `FOR UPDATE OF r,c` em `ApplyCore`: em caso de conflito de versão (409), aborta a operação sem aplicação parcial.
- Menu do cliente de demonstração restrito:
  - Usuário demo (`cliente.teste@odca.local`, role `tenant-client`) não visualiza menu operacional (Caixa, Agenda, Obrigações, Renovações e aditivos, Modelos e minutas, Importações, Ficha do contrato).
- Validação e testes:
  - `dotnet build Odca.sln -c Release`: 0 avisos, 0 erros.
  - `dotnet test tests/Odca.Domain.Tests/Odca.Domain.Tests.csproj -c Release`: 184/184 testes aprovados (100% passando).
  - Novos testes unitários em `AvisosEConfirmacaoRenovacaoTests.cs` cobrindo separação de erro 400/409, `ApplyRenewalResponse` com `EndsOn`, busca por chave do catálogo e restrições de menu do cliente demo.

## Acesso local de desenvolvimento, perfil de operador, landing e página indisponível (21/09/2026)

- Estado da entrega: branch `feat/acesso-local-operador-indisponivel` a partir de `codex/s00-foundation` @ `89fbbb8`.
- Provisionamento e usuários de desenvolvimento:
  - Suporte completo às 3 identidades reservadas: `admin@odca.local`, `operador@odca.local` e `cliente.teste@odca.local`.
  - Seed SQL (`database/development/seed-test-access.sql`) e `TestAccessProvisioner`:
    - Role `tenant-operator` criada com o conjunto completo de permissões operacionais (`tenant.obligations.read|read_all|manage|fulfill|reopen|cancel|assign`, `tenant.documents.download`, `tenant.renewals.read|prepare`, `tenant.templates.read`, `tenant.contract_drafts.read`, `tenant.reviews.read`, `tenant.imports.read`, `tenant.saved_views.manage`).
    - Role `tenant-client` com permissões não-operacionais (`tenant.organization.read`, `tenant.billing.read`).
    - Membership ativo do superadministrador vinculado ao tenant de demonstração (sem criar segundo tenant), viabilizando `activeTenantId` para navegação.
  - Scripts `scripts/provision-local-superadmin.ps1` e `.sh`:
    - Validação de identidades reservadas: bloqueio contra e-mails divergentes.
    - Suporte a `--operator-password`, `$env:ODCA_DEV_OPERATOR_PASSWORD` e `--allow-immediate-login`.
    - Exibição de senhas locais confirmadas no banco via `show-login` (nenhuma senha mantida em código ou controllers).
- Landing no BFF (`HomeController`):
  - Operador autenticado com `tenant.obligations.read` é redirecionado automaticamente para `/organizacoes/{tenantId}/caixa`.
  - Cliente autenticado sem permissão operacional é redirecionado para `/cliente` (`CustomerHome`).
- Navegação e menu de permissões (`_Layout.cshtml` + `IUserTenantContext`):
  - `UserTenantContext` injetado como serviço com cache por requisição em `HttpContext.Items`.
  - Menu operacional condicionado a `membership active` no tenant.
  - Cada link operacional é filtrado estritamente por permissão específica (`obligations.read`, `renewals.read`, `templates.read`, `contract_drafts.read`, `imports.read`, `organization.read/manage`).
  - Item "Ficha" é exibido no menu exclusivamente quando a rota possui `contractId`.
  - Cliente não visualiza Caixa, Agenda, Obrigações, Renovações, Minutas, Importações ou Ficha.
- Tratamento de indisponibilidade e auditoria técnica (`BffErrorHandlingMiddleware` + `ServiceUnavailable.cshtml`):
  - Middleware intercepta exceções e respostas 500/503 no BFF.
  - Registro estruturado no `ILogger.LogError` via `[LoggerMessage]` (CA1848 compliant) com tipo da exceção, mensagem, inner exception, path, tenantId, sub do JWT e request id (zero senhas em log).
  - Diálogo `<dialog class="unavailable-dialog" role="alertdialog" open aria-labelledby="dialog-title" aria-describedby="dialog-desc">`:
    - Título: `Página indisponível`.
    - Texto: `Não foi possível abrir este recurso agora. O detalhe técnico foi registrado.`
    - Botões: `Fechar` (nativo via `<form method="dialog">` e script) e `Voltar à caixa` (quando `tenantId` está disponível na rota, query ou sessão).
    - UI limpa e estéril: sem stack trace, SQL, connection string, token ou hash.
  - 401 preserva Challenge, 403 preserva Forbid, e 404 preserva página de não encontrado (sem disparar o diálogo 5xx).
- Build e testes:
  - `dotnet build Odca.sln -c Release`: 0 erros, 0 avisos.
  - `dotnet test tests/Odca.Domain.Tests/Odca.Domain.Tests.csproj -c Release`: 179/179 testes aprovados.
  - Novos testes unitários em `BffAccessAndIndisponibilidadeTests.cs` e integração em `DevelopmentAccessProvisioningTests.cs`.

## Vistas da caixa, datas relativas e refinamento de UI da ficha (19/09/2026)


- Estado da entrega: branch `feat/workspace-ui-vistas-datas` a partir de `codex/s00-foundation` @ `0c1ef9c6`.
- Vistas da caixa no módulo v014:
  - Salvar vista da caixa operacional (`POST /vistas/salvar` com `listingType=inbox`).
  - Abrir vista (`GET /vistas/{viewId}`), alternar com chip ativo.
  - Tornar padrão (`POST /vistas/{viewId}/padrao`) com checagem de concorrência (`RowVersion`) e isolamento tenant + dono (retorna 404 para vista de outro usuário/tenant e 409 em conflito de versão).
  - Inativar vista (`POST /vistas/{viewId}/inativar`).
  - Indicador textual explícito `(Padrão)` além de estrelas nos chips de vistas.
  - Paginação e cards de urgência levam `viewId` preservado na URL; `page` nunca é gravado no JSON da vista.
  - Allowlist server-side em `SavedViewFilterPolicy` para `listingType=inbox`: `scope`, `kind`, `urgency`, `contractId`, `ownerId`, `relativeDate`.
- Datas relativas dinâmicas:
  - Tokens suportados: `today`, `overdue`, `dueToday`, `dueThisWeek`, `nextDays:N` (0 <= N <= 365).
  - Resolução no servidor em `RelativeDateResolver` utilizando `TimeProvider` e fuso horário civil do tenant (`tenants.timezone`).
  - Proibido persistir `DateOnly` absoluto no JSON quando data relativa for utilizada; proibido uso concomitante de data relativa com datas absolutas (`from`/`to`).
  - `dueThisWeek` mapeia para a urgência `DueThisWeek` e avança na virada de meia-noite civil do tenant (`TimeProvider`).
- Data default de proposta e "hoje" na ficha e renovações:
  - Ficha do contrato expõe `sheet.Today` derivado de `(now() AT TIME ZONE tenants.timezone)::date` do banco.
  - Proposta de alteração/renovação utiliza `sheet.Today` como valor inicial padrão (sem `DateTime.UtcNow`).
  - Central de renovações (`/renovacoes`) utiliza data civil do tenant retornada pela API (`Today`).
  - Link de proposta na ficha direciona para `/renovacoes` filtrado por título (formalização/aplicação exclusivamente lá; sem `/apply` na ficha).
  - Regra de negócio: 403 em `scope=organization` sem `tenant.obligations.read_all` mantido intacto.
- Acessibilidade e Estilo Semântico (sem inline `style=`):
  - Removidos todos os atributos inline `style=` de `Views/Inbox/Index.cshtml`, `Views/Agenda/Index.cshtml` e `Views/Contracts/Sheet.cshtml`.
  - Subnav da ficha (`#obrigacoes #renovacao #revisao #minutas`) sticky com tracking de `aria-current="true"` baseado na hash via JavaScript e suporte a teclado.
  - Chip de importação com texto + status-label sem depender apenas de emojis.
  - Grid responsivo: stack < 768px; Inbox com 1 coluna < 768px, 2 colunas 768–1279px, 4 colunas >= 1280px.
  - Focus visível e sequência de tabulação consistente.
- Build e testes:
  - `dotnet build Odca.sln -c Release`: 0 avisos, 0 erros.
  - `dotnet test tests/Odca.Domain.Tests/Odca.Domain.Tests.csproj -c Release`: 175/175 testes aprovados.

## Ficha acionável: revisão, comentários, histórico seguro e resolução por chave (18/09/2026)

- Estado da entrega: branch `feat/ficha-revisao-historico-chave` a partir de `codex/s00-foundation` @ `600915e7`.
- Drawer de histórico de obrigação:
  - Consumo via `GET /organizacoes/{tenantId}/obrigacoes/{id}/historico`.
  - Renderização estritamente segura usando DOM APIs (`createElement`, `textContent`), sem `innerHTML`.
  - Controle de acesso com dono da obrigação ou permissão `tenant.obligations.read_all` (403 restrito no drawer sem quebrar a ficha).
- Painel `#revisao`:
  - Link direto para o rascunho no Estúdio preservando `returnUrl` integral.
  - Submissão de versão para revisão reutilizando `api.SubmitStudioReviewAsync` (POST `revisao/submeter`).
  - Adição e alteração de estado (resolve/reopen) de comentários contextuais no rascunho com aviso explícito "comentário ≠ aprovação".
  - Tratamento de colisão/conflito 409 recarregando a ficha com mensagem de erro amigável sem perder o contexto.
  - Atalhos diretos para Checklist e Comparação de versões.
- Resolução de minutas oficiais por Key:
  - Lookup resiliente a renomeações de título via `odca.audit_events.metadata->>'key' = @key` através do endpoint `GET /api/v1/organizations/{tenantId}/studio/templates/official/{key}`.
  - Proibido `FirstOrDefault(x => x.Name == título)`.
  - Retry de instalação sob demanda caso `CanInstallOfficialLibrary` seja verdadeiro.
  - Bloqueio 400 BadRequest para minutas primárias (NDA, serviços, fornecimento, locação) se houver revisão aberta (`in_review` ou `changes_requested`).
  - Termo aditivo (`contract-amendment`) permitido apenas se `RequiresAmendment` (janela de renovação ou alteração de prazo).
- Data civil do fuso horário no cumprimento de obrigação:
  - Quando `EffectiveAt` for omitido em `fulfill`, calcula a data civil do tenant via `(now() AT TIME ZONE timezone)::date` com fallback seguro `TimeZonePolicy.Resolve(tenantInfo.Timezone, ...)`. Proibido `DateTimeOffset.UtcNow`.
- Preservação da query de retorno:
  - `from`, `year`, `month`, `scope`, `kind`, `urgency`, `viewId`, `obrigacao` preservados em formulários POST e links da ficha ("Voltar à caixa" / "Voltar à agenda").
- Build e testes:
  - `dotnet build Odca.sln -c Release`: 0 erros, 0 avisos.
  - `dotnet test tests/Odca.Domain.Tests/Odca.Domain.Tests.csproj -c Release`: 136/136 testes aprovados.

## Ficha acionável do contrato, biblioteca de minutas oficiais e contexto operacional (18/09/2026)

- Estado da entrega: branch `feat/ficha-acionavel-minuta-contexto` criada a partir de `codex/s00-foundation`.
- Catálogo canônico de 6 minutas oficiais consolidado em `OfficialContractTemplates`:
  1. `nda-unilateral` ("Acordo de Confidencialidade (NDA) — Unilateral")
  2. `nda-mutual` ("Acordo de Confidencialidade (NDA) — Mútuo")
  3. `services-agreement` ("Contrato de Prestação de Serviços Técnicos")
  4. `contract-amendment` ("Termo Aditivo Contratual")
  5. `supply-agreement` ("Contrato de Fornecimento de Bens")
  6. `lease-agreement` ("Contrato de Locação de Imóvel Comercial")
- Ficha acionável do contrato (`/sheet`):
  - Ações diretas em obrigações: iniciar (`start`), cumprir (`fulfill`), cancelar (`cancel`), reabrir (`reopen`), reprogramar (`reschedule`), reatribuir (`reassign`) e criar obrigação manual com validação de versão otimista e redirecionamento para âncora `#obrigacoes`.
  - Proposta de alteração/renovação (`POST /proposta-renovacao`): registro sem efeito direto na vigência até formalização, obrigatoriedade de aditivo (`amendment`) na janela de 3 meses ou alteração de data final, validação de responsável e idempotência.
  - Rascunhos de minutas oficiais (`POST /rascunho-oficial`): criação direta de rascunho de minuta oficial via API de estúdio com base no catálogo oficial do tenant (com instalação automática se permitido quando catálogo incompleto).
  - Instalação sob demanda da biblioteca oficial (`POST /biblioteca-oficial`) autorizada por permissão.
  - Contexto de importação aguardando revisão destacado na ficha via chip de alerta direto para importações.
- Visibilidade e RLS:
  - `scope=organization` na caixa operacional exige permissão estrita `tenant.obligations.read_all` retornando 403 `Forbid()` (sem degradação silenciosa para `mine`). Contratos de outros tenants retornam 404 `NotFound`.
  - Links profundos canônicos com âncora (`?obrigacao={id}&from=caixa#obrigacoes`, `#revisao`, `#renovacao`).
- Separação de camadas:
  - `Odca.Web` estritamente desacoplado de `Odca.Application` (usings removidos, regras de roteamento e catálogo mapeados localmente).
- Testes e compilação:
  - `dotnet build Odca.sln -c Release`: 0 erros, 0 avisos.
  - `dotnet test tests/Odca.Domain.Tests/Odca.Domain.Tests.csproj -c Release`: 132/132 testes aprovados.
- Fora de escopo preservado: sem editor visual, sem PDF rastreável, sem OCR, sem séries recorrentes ("esta e futuras"), sem dashboard consolidado de tarefas, sem billing/PIX, sem impersonation, sem sessão de suporte, sem migração de banco alterando snapshots 001–019.


- Estado inicial desta entrega: branch `work`, SHA `b3b3d8c2b452254797384a8781cc4e54bbfcb35c`, árvore limpa e nenhum `AGENTS.md` presente no checkout ou diretório pai.
- As actions de solicitação agora possuem nomes de negócio sem ocultar `ControllerBase.Request`/`Controller.Request`; as URLs públicas permanecem explícitas e o POST do BFF exige antiforgery.
- Pacotes sem preço e moeda válidos não são oferecidos nem aceitos. A idempotência compara pacote e quantidade no mesmo tenant, impedindo que a mesma chave seja reutilizada silenciosamente com outro payload; o snapshot comercial continua calculado no servidor.
- O repositório alinha parâmetros públicos à interface, preserva nomes SQL explícitos, propaga cancelamento até delegates e commit/rollback e usa o retorno concreto `Task<int>` do helper Dapper.
- O worker usa lock transacional entre instâncias, verifica versão, tenant, request, linhas afetadas e registro único da aplicação antes do commit. Conflitos são persistidos como tal e nenhuma aplicação parcial é confirmada.
- O SDK .NET 10.0.400 e PostgreSQL descartável não estão disponíveis neste contêiner; a tentativa de obter o instalador oficial retornou HTTP 403. Builds/testes .NET/PostgreSQL e captura autenticada permanecem gates não executados localmente.

## Política canônica e jornada de vistas pessoais (14/09/2026)

- Estado inicial registrado: branch `work`, SHA `1e0a470ed78f6151da7e284474f8e99d395ba58f`; árvore inicialmente limpa.

### Implementado

- Os alvos `CA1859` em `SavedViewsController` agora expõem os tipos concretos realmente usados nos detalhes privados; o helper de contexto continua retornando `Task<int>`. O parâmetro privado `Add` do BFF recebe `Dictionary<string,string>`.
- A construção de rota deixou de usar o construtor incompatível de `RouteValueDictionary`: filtros passam pela política canônica, são inseridos individualmente e somente depois recebem `tenantId`, `viewId` e página inicial confiáveis. Chaves reservadas são recusadas.
- Salvar, atualizar e aplicar reutilizam a mesma normalização de chaves, enumerações, UUIDs, datas ISO, intervalo, ordenação e limites. Atualização de filtros é explícita e preserva a versão otimista.
- Acesso sem filtro/vista abre a vista padrão; filtros explícitos (inclusive dashboard) prevalecem. A central identifica vista atual e alterações não salvas, oferece chips, limpeza e gerenciamento responsivo.

### Validado nesta execução

- `npm run build` e `git diff --check` aprovados. Foram adicionados testes comportamentais para rota confiável, caracteres especiais, chaves reservadas, datas e exclusão de página da persistência.

### Pendente por limitação do ambiente

- O container não contém `dotnet`; restore bloqueado, builds Debug/Release e testes .NET não puderam ser executados. Não há PostgreSQL descartável/role restrita nem aplicação executável, portanto HTTP/RLS, concorrência real, jornada autenticada, capturas e QA 360/768/1280/1440 permanecem não validados.
- Datas relativas dependentes do fuso da organização, ficha/formulários de tratamento completos e criação idempotente com chave de requisição permanecem pendentes; não são declarados concluídos.

## Evolução — factory inequívoca e vistas pessoais (14/09/2026)

- Estado inicial registrado: branch `work`, SHA `4a38ba0f368e0fc9dfc3c89b6238da707b920315`; árvore de trabalho inicialmente limpa.

### Implementado

- A ambiguidade `CS0433` vinha das referências simultâneas a `Odca.Api` e `Odca.Bootstrap`, cujos entrypoints declaram `Program` no namespace global. As factories agora usam o marcador público exclusivo `Odca.Api.ApiAssemblyMarker`; a referência ao Bootstrap e seu comportamento foram preservados.
- A migration v014 adiciona vistas pessoais de trabalho, isoladas por tenant **e proprietário** via RLS, com definição JSON de filtros, ordenação allowlisted pela API, inativação lógica, versão otimista e unicidade de nome/padrão.
- A central de obrigações permite salvar os filtros correntes e reabrir uma vista. A API revalida permissão, tenant, contrato e responsável a cada abertura; somente a definição é persistida e a consulta operacional continua paginada no servidor.
- O schema canônico passa a declarar v014. A divergência preexistente entre a migration v013 presente e `DatabaseSchema.CurrentVersion` foi corrigida.

### Validado nesta execução

- `npm run build`, validação independente dos checksums 001–014 e `git diff --check` foram aprovados.
- A ausência de ocorrências restantes de `WebApplicationFactory<Program>`/`typeof(Program)` nos testes foi verificada por busca estática.

### Pendente por limitação do ambiente

- O container não possui `dotnet`; restore, builds Debug/Release e testes .NET não puderam ser executados localmente.
- Não há PostgreSQL descartável nem as credenciais exclusivas `ODCA_TEST_*`; migration real, RLS A × B e jornada autenticada permanecem gates de CI/QA.
- A ficha completa de contrato, agenda mensal e jornada HTTP integral de revisões permanecem no backlog; esta entrega não os declara concluídos.

## Obrigações: compilação e integridade das ações (14/09/2026)

### Implementado

- O alvo de `CA1859` em `ObligationsController.Create` era a variável local `IReadOnlyList<DateOnly> dates`; ela agora declara o `List<DateOnly>` efetivamente produzido pelos dois ramos, sem materialização adicional nem mudança no contrato HTTP.
- Reprogramações e reatribuições sem mudança real são rejeitadas antes da gravação, e a reatribuição exige justificativa tanto na regra de aplicação quanto na API.
- Evidências de cumprimento exigem permissão de download, versão do mesmo tenant e contrato, documento lógico ativo e estado de segurança `safe`; anexar não altera quarentena.
- A recorrência mensal limita a janela no extremo de `DateOnly`, evitando overflow, e continua preservando o dia-base nos meses curtos.
- A leitura de histórico agora executa a segunda consulta de permissão na transação ativa da conexão.

### Validado neste ambiente

- `npm run build` e verificações de diff/whitespace foram executados localmente.
- Foram adicionados testes comportamentais de reprogramação, reatribuição e limite superior da recorrência.

### Pendente / limitação verificada

- O SDK .NET 10.0.400 e o PostgreSQL descartável não estão disponíveis neste contêiner; restore, builds Debug/Release, testes .NET/HTTP/RLS e jornada autenticada no navegador não são declarados aprovados.
- Agenda mensal, formulários BFF das mutações, edição prospectiva de séries e dashboard consolidado permanecem incrementos posteriores; esta entrega não os apresenta como concluídos.

## Histórico autorizado de obrigações (14/09/2026)

### Implementado

- A API expõe o histórico cronológico da obrigação somente após validar vínculo, permissão de leitura, tenant RLS e o escopo do responsável; quem possui apenas leitura própria não consulta o histórico de outro responsável.
- O BFF mantém o token fora do JavaScript, distingue ausência, proibição e indisponibilidade e entrega à interface apenas a projeção autorizada.
- O painel de detalhes de “Minhas pendências” carrega autor, data, tipo, motivo, observação, mudança de prazo/responsável e presença de evidência, com texto seguro criado via DOM e estados de carregamento, vazio e erro.

### Validado neste ambiente

- `npm run build` validou os seis assets web e `git diff --check` não encontrou erros.

### Pendente / limitação verificada

- O SDK .NET 10.0.400 e o PostgreSQL descartável continuam indisponíveis; restore, builds .NET, testes, HTTP autenticado, RLS real e jornada no navegador não são declarados aprovados.
- As ações de mutação e a jornada completa de revisões ainda precisam ser conectadas no BFF. A consulta de histórico entregue não é apresentada como conclusão integral desses fluxos.

## Correções de analisadores e refinamento de Minhas pendências (14/09/2026)

### Implementado

- `CA1859` corrigido usando `Dictionary` no mapa privado de transições, sem alterar a exposição somente leitura das etapas e do histórico.
- Reatribuição rejeita motivo nulo, vazio ou em branco antes de qualquer mutação; normaliza com `Trim`, preserva a política de truncamento em 1.000 caracteres e mantém separados ator, revisor anterior e novo revisor na etapa e no evento.
- `CA1512` corrigido com `ArgumentOutOfRangeException.ThrowIfNegative(offset)`, preservando zero como ocorrência-base válida.
- Datas opcionais da consulta de obrigações usam `InvariantCulture` no formato ISO e continuam omitidas quando ausentes. Intervalo invertido retorna validação antes de chamar a API.
- A superfície existente foi renomeada para “Minhas pendências”, ganhou linguagem de central operacional, labels explícitos, estado de validação e paginação que preserva todos os filtros da URL.

### Validado neste ambiente

- `npm run build` validou os assets e `git diff --check` não encontrou erros de whitespace.
- Testes foram ampliados para justificativas, ausência de mutação parcial, auditoria de reatribuição, limite, offset negativo/zero e serialização ISO sob `pt-BR` e `ar-SA`, com restauração das culturas no `finally`.

### Pendente / limitação verificada

- O contêiner não possui o SDK .NET 10.0.400; por isso restore, build Release, testes .NET, PostgreSQL descartável, HTTP e jornada autenticada não são declarados aprovados.
- Sem aplicação .NET executável, não houve captura honesta nem QA em navegador nas quatro larguras. Integração completa de revisões e renovações na central, ficha do contrato e ações BFF continuam pendentes.
- A consulta da CI remota não pôde ser autenticada: `gh` não possui sessão e a API pública respondeu HTTP 403. O workflow versionado executa restore bloqueado, build, assets, testes com PostgreSQL 18 e secret scan.

## Incremento de revisão contratual interna (14/09/2026)

### Implementado

- O `CS9007` do teste de reparo foi removido na origem: o cenário de JSON válido agora serializa um objeto com `System.Text.Json`, preservando conexão, chave e propriedade desconhecida verificadas pelo teste. Os valores globais de ambiente usados pelos testes são restaurados ao valor anterior em `finally`, e a coleção continua serializada.
- A camada de aplicação recebeu o agregado `ContractReview`, separado do processamento do arquivo e do estado operacional do contrato. Ele prende a solicitação a uma versão imutável e a um snapshot de hash, partes, valor, moeda e vigência; aplica fluxo sequencial, confirmação explícita, justificativas, tabela de transições, revisão otimista, reatribuição histórica e comentários resolvíveis sem convertê-los em aprovação.
- A migration aditiva v012 cria solicitações, etapas, comentários, eventos e outbox interna com FKs compostas por tenant, RLS, índices de pendências, unicidade da etapa corrente/idempotência e catálogo de seis permissões de revisão. `database/odca.sql` e o snapshot de release foram consolidados com o mesmo checksum.
- O procedimento documentado de `ENC0097` agora explicita parar somente a sessão, corrigir erros reais, recompilar e reiniciar; não orienta alterar configuração nem finalizar todos os processos `dotnet`.

### Validado neste ambiente

- Checksum de todos os 12 blocos do SQL e igualdade do snapshot v012 foram verificados por script local; `npm run build` e verificações textuais são registradas na entrega.
- A tentativa real de `dotnet restore Odca.sln --locked-mode` foi feita, mas o executável `dotnet` não está instalado neste contêiner. Por isso build/testes .NET, PostgreSQL descartável e jornada autenticada não são declarados aprovados nesta execução.

### Pendente

- A v012 é a base transacional e de domínio, não a jornada HTTP/BFF completa. Repositório Dapper, casos de uso autorizados, endpoints, Worker da outbox, ficha split-view, central “Minhas pendências”, comparação e dashboard ainda precisam ser conectados antes de declarar o critério de conclusão do fluxo autenticado.
- Também permanecem pendentes a prova PostgreSQL de decisão/cancelamento concorrentes e isolamento A × B, e QA real por navegador/teclado em 360/768/1280/1440 e zoom 200%. Nenhuma captura foi fabricada sem uma aplicação executável.

## Correção do controller de documentos e contexto RLS (14/09/2026)

- **Implementado:** os streams e caminhos do upload têm nomes sem conflito; operações físicas usam o alias explícito de `System.IO.File`, enquanto respostas continuam usando os helpers do MVC. O arquivo temporário é descartado após a gravação e removido no `finally`; falha do commit remove o objeto que ainda não se tornou válido no banco.
- **Implementado:** `SetTenant` expõe o `Task<int>` realmente devolvido pelo Dapper. Listagem, conteúdo e revisão de extração agora definem `odca.tenant_id` localmente em uma transação e consultam na mesma conexão, eliminando o contexto de sessão que sobrevivia no pool. Nova versão bloqueia e valida o documento lógico no tenant/contrato antes de calcular seu número.
- **Validado neste ambiente:** `npm run build` e `git diff --check`. O teste PostgreSQL foi ampliado para alternância A → B e limpeza do contexto após commit e rollback usando a role restrita.
- **Não validado neste ambiente:** restore, build e testes .NET, PostgreSQL descartável, HTTP autenticado, navegador e capturas; o executável `dotnet` não está instalado e a obtenção do instalador oficial retornou HTTP 403. Essas verificações não são declaradas aprovadas.
- **Pendente:** BFF da área de contratos/documentos/revisão, inativação, reconciliação automática de órfãos/reservas, idempotência de upload, indicadores do dashboard e QA visual. A jornada integral solicitada continua explicitamente não concluída.

## Evolução do estúdio estruturado — fundação validável (14/09/2026)

- **Implementado:** CA1859 no migrador corrigido no parâmetro privado com o tipo concreto efetivamente produzido por `LoadHistoryAsync`, sem materialização, mudança de contrato público, ordem, checksum, transação ou cancelamento.
- **Implementado:** formato canônico JSON único para documentos, com nós/atributos/marcas permitidos, limites, campos por identificador estável e recusa de HTML, handlers e recursos externos. Validação tipada de campos, confirmação obrigatória na publicação, snapshot imutável, conflito otimista e restauração como novo rascunho foram adicionados à camada Application.
- **Validado neste ambiente:** assets e integridade do diff. O SDK .NET 10.0.400 não está instalado, portanto restore, build e testes .NET não foram declarados aprovados. A CLI do GitHub não está autenticada, logo o resultado remoto atual da CI não pôde ser consultado.
- **Documentado:** ENC0097 como limitação de Hot Reload e sequência segura de reinicialização do perfil `ODCA local`, sem modificar ou excluir configuração pessoal.
- **Pendente:** persistência/API/BFF do estúdio, modelos e cláusulas, PDF rastreável, migration v012, navegador autenticado, PostgreSQL descartável e QA visual. A fundação não é apresentada como jornada completa.

## Incremento S02 recuperado sobre o checkout disponível (14/09/2026)

| Funcionalidade | Implementada | Validada | Pendência | Ação desta entrega |
|---|---|---|---|---|
| Equipe e convites | sim | testes existentes; banco não executado aqui | prova PostgreSQL | preservada; schema avançado para v011 |
| Contratos | núcleo SQL | checksum estático | telas/casos completos da referência ausente | modelo mínimo tenant-aware criado |
| Upload e versões | API e armazenamento privado | testes de detecção/limites | reconciliação automática | streaming, hash, cota transacional e imutabilidade |
| Segurança | Worker local | fail-closed por código | fixture ClamAV | estados separados; sem liberação implícita |
| Extração | PDF nativo, imagem OCR e DOCX | DOCX/sugestões unitários | OCR de PDF digitalizado e QA executável | job com lease, retry e resultado rastreável |
| Revisão/aplicação | schema de decisões | checksum | API/BFF transacional e tela split-view | persistência preparada, sem alegar jornada completa |

O SHA histórico `183aa2b5` continua ausente do pack local e o fetch do GitHub foi
recusado pelo proxy (HTTP 403). O estado real foi reavaliado em `f6954f3`: este
checkout continha S01 e apenas o reparo v010, não os módulos S02 mencionados.
Esta entrega implementa uma primeira fatia executável em vez de declarar como
existente o código inacessível. SDK .NET, PostgreSQL descartável e navegador não
estão disponíveis neste ambiente; esses gates permanecem explicitamente não
aprovados.

## Reparo do pacote S01/S02 (14/09/2026)

- O checkout disponível está em `596ffe6` (S01), e não contém o SHA S02 informado (`183aa2b5`); o acesso ao remoto foi recusado pelo proxy HTTP 403. Assim, a central de contratos/documentos não foi recriada sobre uma base antiga nem declarada concluída.
- Foi reproduzido por inspeção o bloqueio PostgreSQL 42P13 da migração 009: parâmetro de entrada e coluna `RETURNS TABLE` usavam `invitation_id`. O instalador canônico agora usa `p_invitation_id`, sem alterar a assinatura por tipos, permissões ou o snapshot distribuído v009.
- O release v009 defeituoso permanece imutável para auditoria. O migrador aceita **somente** a transição dos checksums conhecidos v009 defeituoso → reparado, recria a função sem `DROP CASCADE` e atualiza o registro; qualquer outro checksum continua sendo recusado. O v010 registra o reparo e é o pacote canônico integral para instalação nova, atualização completa ou histórico parcialmente aplicado.
- Os testes de host agora isolam também Data Protection e cada caso mantém válidos os campos alheios ao alvo. As mensagens JWT identificam a própria validação, sem reduzir os requisitos de produção.
- Não verificado neste ambiente: restore/build/testes .NET (SDK ausente), PostgreSQL descartável, HTTP autenticado, navegador e larguras visuais. O build de assets e as verificações estáticas executadas estão registrados no PR.

## Configuração local consolidada (10/09/2026)

- O setup oficial passou a ser `.\scripts\setup-local.ps1`; ele mantém `Database=postgres`, schema `odca` e separa configuração/conectividade de migration e provisionamento.
- A exceção para provisionar acesso de demonstração na base `postgres` requer simultaneamente `--environment Development` e `--allow-postgres-development`; as recusas históricas abaixo descrevem versões anteriores.
- Neste ambiente de revisão o executável `dotnet` não está instalado e não há conexão autorizada ao PostgreSQL do usuário; portanto build/testes .NET, schema, credenciais, login HTTP, MFA e dashboard não foram declarados aprovados.

## Evolução prompt 13 — central operacional de equipe (10/09/2026)

- SHA inicial desta sessão: `297b2cde045c11f27665f4bf5d82ebd3d39b151c` (já à frente de `6ac493b` com lockfiles/CI).
- Restore bloqueado, build Release e `npm run build` aprovados com SDK 10.0.400 neste ambiente. NU1004/NU1510 não reproduzidos.
- Migração 008: checksum persistido corrigido. Migração 009 (`CurrentVersion=9`): status `expired`, aceite sem reativar vínculo bloqueado/inativo, entrega separada do ciclo do convite, expiração/cancelamento/reenvio, proteção do último administrador.
- API/BFF: central com abas Pessoas/Convites/Perfis, paginação real, 403 ≠ lista vazia, overview de assentos, ações de membro, perfis, seletor/edição de organização, preview de convite e copy honesta de reautenticação.
- Worker: lease por token; cancelamento do host separado; stub de produção falha com `notification_provider_missing`.
- Testes locais sem banco: Domain (23) + MigrationChecksum/startup/host (27) aprovados. Suíte PostgreSQL descartável e QA visual autenticado **não** executados: falta credencial admin (`development-runtime.json` ausente) e Docker Desktop parado; PostgreSQL 18 nativo está em execução na porta 5432.
- Esta entrega **não** declara conformidade LGPD plena nem e-mail de produção.

## Evolução prompt 10 — primeira fatia visual e navegação por perfil (10/09/2026)

- O contrato de autenticação agora transporta explicitamente o tipo de conta; o BFF converte esse dado em role do ticket protegido e monta navegação de plataforma ou cliente sem inferência visual.
- O layout autenticado ganhou sidebar recolhível, contexto inequívoco, topbar e drawer mobile com retorno/contenção de foco e fechamento por `Escape`. Percursos de senha inicial e MFA continuam fora do shell administrativo.
- Tokens visuais, controles, métricas, planos e autenticação foram consolidados em um sistema corporativo responsivo. A largura total saiu de `.button-primary` e passou ao modificador contextual `.button-block`.
- Login e cadastro têm exibição acessível de senha e proteção visual de envio repetido. A home do cliente diferencia limites contratados de consumo medido.
- `npm run build` e `git diff --check` foram aprovados. Restore/build/testes .NET não puderam ser executados porque o SDK 10 não existe no ambiente e a instalação oficial foi recusada por HTTP 403. Por isso, o NU1004 não foi declarado corrigido e lockfiles não foram alterados manualmente.
- QA renderizado e capturas em 360/768/1440 também ficaram bloqueados pela ausência de runtime .NET e navegador instalado. Esta fatia não declara organização múltipla, equipe, convites, quotas ou ficha de clientes concluídos.

## Etapa atual

Referência revalidada em 09/09/2026: SHA base `5ab8df3`. S00 continua **em validação de banco/navegador**. S01 está **parcial**: catálogo público e entrada pública de solicitações de privacidade existem; onboarding, MFA, equipe e operação do atendimento ainda não.

## Evolução prompt 09 — acesso local verificável

- A duplicação de `StartupValidationService` introduzida pelo merge foi removida de `StartupValidation.cs`; a implementação permanece no arquivo próprio e há um único registro hospedado.
- O Bootstrap ganhou `provision-test-access --environment Development`: destino sanitizado, recusa da base `postgres`, transação para superadministrador e cliente sintético, organização de demonstração, administrador de tenant, Basic vigente e auditoria da concessão local.
- Reexecução preserva identidades e senhas. `--rotate-passwords` é necessário para rotação; conta comum preexistente não é elevada. Após o commit, o comando relê perfil, bloqueio/exclusão, vínculo, papel e plano, e verifica senhas disponíveis pelo `IPasswordService`.
- `show-login` agora consulta o banco e não apresenta arquivo desatualizado como senha atual. `reset-password` valida exatamente uma linha de usuário, revoga sessões na mesma transação e preserva MFA.
- Este ambiente não contém SDK .NET nem conexão PostgreSQL local autorizada. Portanto persistência, senha, login HTTP e MFA ainda não foram alegados como executados; os comandos exatos estão no README.

## Correção posterior à CI 34350222079

- A configuração da API deixou de ser lida antecipadamente em `Program` e no registro da Infrastructure. JWT, política de autenticação e data source agora são materializados depois que o host terminou de compor suas fontes de configuração.
- Uma validação hospedada mantém o fail-fast para conexão, issuer, audience, chave, expiração e controles de produção, sem inserir configuração sintética na aplicação.
- Testes determinísticos cobrem aceitação da configuração isolada e rejeição de JWT/MFA inválidos. A execução local não foi possível neste ambiente porque o SDK `dotnet` não está instalado; a correção precisa do gate de CI/PostgreSQL.
- A matriz factual deste incremento está em `docs/execution/S01_RECONCILIATION.md`.

## Implementado nesta evolução

- NU1004 corrigido: JwtBearer e Mvc.Testing alinhados em `10.0.12`, locks regenerados e restore bloqueado aprovado.
- Fixture isolada: sem fallback para `development-runtime.json`, sem mutação global de ambiente e com marcador, banco `odca_test*` e role `odca_test_app_login` obrigatórios.
- CI com secret scan independente e publicação de TRX mesmo após falha de testes.
- Runner valida todo o histórico conhecido antes de mutar; rejeita checksum divergente, lacuna e banco mais novo que o código. Readiness exige PostgreSQL 18 e schema exatamente na versão 3.
- Sessão de autenticação usa a duração JWT configurada; login durante bloqueio não prorroga a janela.
- BFF diferencia 401, 403, 429, timeout e 5xx em login, troca de senha, dashboard e planos; falha do backend não é apresentada como logout.
- Catálogo público separado da administração em `/api/v1/catalog/plans` e `/conheca-os-planos`, com rejeição explícita de plano vigente ambíguo ou incompleto.
- Migração 003 e fatia ODCA-PRIV-003: formulário `/privacidade`, API pública com rate limit, protocolo opaco, resposta neutra, histórico inicial e gravação por função de privilégio restrito.
- Teste comportamental preparado para role runtime: ausência de contexto, alternância de tenant, `role_permissions`, auditoria e FK cruzada A × B.

## Histórico de verificações (resultados não cumulativos)

| Verificação | Resultado |
|---|---|
| `dotnet restore Odca.sln --locked-mode` no SHA anterior | sucesso |
| `dotnet build Odca.sln --configuration Release --no-restore` no SHA anterior | sucesso; 0 avisos, 0 erros |
| `npm run build` no SHA anterior | sucesso |
| testes de domínio no SHA anterior | 10 aprovados |
| parser/checksum/snapshots de migração no SHA anterior | 6 aprovados |
| `dotnet test Odca.sln --configuration Release --no-build` no SHA anterior | 16 aprovados; 3 cenários PostgreSQL recusados antes de conexão por ausência do marcador exclusivo |
| CI remoto do SHA `c5e5064` | secret scan aprovado; restore falhou por NU1004 antes de build/assets/testes |
| `npm run build` nesta correção | sucesso com Node 20.20.2; o projeto declara Node >=24, portanto a CI continua sendo o gate na versão suportada |
| `dotnet restore Odca.sln --locked-mode` nesta correção | não executado: SDK .NET 10 ausente e download bloqueado por HTTP 403 no ambiente |
| suíte PostgreSQL atual | não executada: faltam as três variáveis exclusivas de teste e uma credencial administrativa autorizada |

## Parcial, pendente ou bloqueado

- `database/odca.sql` permanece um histórico rastreável e o runner pula versões aplicadas, mas a execução direta integral no pgAdmin ainda reexecuta corpos históricos. A equivalência/recovery/concurrency do pacote SQL não foi comprovada.
- O teste A × B e o fluxo API foram escritos, mas aguardam banco descartável autorizado; nenhum banco de desenvolvimento foi tocado.
- MFA real, cache/Data Protection compartilhados, onboarding de organização, confirmação de e-mail, assinatura, equipe/perfis, suporte temporário, tratamento autenticado de direitos, retenção executável e S02/S03 permanecem pendentes.
- A página pública de privacidade é um canal de entrada técnico; não publica texto jurídico, prazo ou contato não aprovados e não constitui comprovação de conformidade LGPD.
- QA visual responsivo e navegador autenticado ainda não foram executados.
- Marcos B–E não foram implementados por esta correção do gate A; permanecem explicitamente pendentes e não devem ser inferidos dos testes de startup.

## Evolução prompt 08 — incremento local de integridade (09/09/2026)

- Corrigidos os três defeitos reproduzidos da CI 34367965217: contagem canônica de migrações, projeção explícita da home e conversão UTC do estado MFA.
- A elevação MFA agora renova, na mesma transação, a expiração persistida da sessão para exatamente a validade emitida no JWT. O início exige sessão/versão válidas e estado ainda não confirmado; a confirmação compara o segredo protegido observado.
- CPF/CNPJ passam por dígitos verificadores. A migração 006 preserva 001–005, impede mais de um cadastro vivo por documento e acrescenta campos de evidência de termos/aviso. Os marcadores de versão permanecem `pending-legal-approval`, sem representar texto jurídico aprovado.
- Repetição idempotente volta a criar a mensagem de outbox ligada ao token rotacionado, e a confirmação elimina o token de desenvolvimento.
- Chaves Data Protection têm caminho persistente configurável para API e BFF. O ticket store segue em cache de memória: múltiplas instâncias **não** estão declaradas prontas.

Ainda pendentes nesta entrega: transporte de e-mail de produção/worker com lease e retentativas, seleção explícita de organização, equipe/perfis/convites/quota, reset privilegiado de MFA, QA visual e suíte PostgreSQL descartável. Não há editor/OCR iniciado.

## Evolução prompt 11 — contexto explícito e primeiro percurso de convite (10/09/2026)

- A migração 007 introduz versão otimista da organização, permissões delegáveis do tenant, convites que reservam assento e outbox com lease/retry. Funções `SECURITY DEFINER` têm superfície explícita e o acesso comum continua sob RLS.
- A API lista organizações pelo `sub` autenticado, exige tenant explícito nas operações e revalida vínculo/permissão. Há leitura de equipe/perfis, criação restrita de perfil, edição organizacional com conflito e criação/aceite de convite de uso único.
- A confirmação do onboarding passa a atribuir o administrador inicial idempotentemente. Aceitar convite exige a identidade autenticada com e-mail verificado e revoga suas sessões para renovar autorização.
- O BFF ganhou escolha por nome e equipe/perfis sem IDs digitados. O shell usa o nome selecionado, fallback seguro de avatar e melhorias de drawer/localStorage/formulários.
- `npm run build`, validação local dos sete checksums e `git diff --check` passaram. O SDK 10.0.400 continua ausente e o instalador oficial respondeu HTTP 403; portanto restore forçado/bloqueado, build/testes .NET, PostgreSQL e QA renderizado não foram executados neste ambiente. Os lockfiles não foram editados manualmente.
- Bloqueio/restauração de membros, reenvio/cancelamento, proteção concorrente do último administrador e transporte de produção permanecem pendentes; esta entrega não declara o módulo integral concluído.

## Evolução S03 — obrigações e renovações (14/09/2026)

### Matriz de reconciliação

| Funcionalidade | Existente | Validada agora | Problema encontrado | Alteração prevista/realizada |
|---|---|---|---|---|
| Configuração local | Sim | Inspeção + teste isolado existente | SDK ausente impede execução .NET | CS9007 permanece corrigido por `JsonSerializer`; orientação ENC0097 preservada |
| Contratos/documentos/versões | Sim (v011) | Inspeção de código e FKs | Não havia obrigação ligada a versão | Evidência referencia a versão imutável sem novo upload nem ampliação de download |
| Revisão/aprovação | Domínio e SQL (v012) | Testes existentes inspecionados | Jornada HTTP ainda parcial | Mantida separada de cumprimento e renovação |
| Obrigações | Não | Regras puras e assets | Ausência de modelo, API, central e auditoria | Implementado agregado, API tenant-aware, concorrência, histórico, filtros e central responsiva |
| Recorrência | Não | Testes para fevereiro, bissexto e dia 31 | Risco de deriva pelo último dia ajustado | Série mensal usa sempre o dia-base e materializa no máximo 24 ocorrências |
| Alertas internos | Outbox de convites/revisão | Checksum e inspeção | Não havia lembrete de obrigação | Lease tokenizado, deduplicação, atraso limitado a um dia e notificação interna individual |
| Renovações | Campos básicos no contrato | Inspeção | Intenção poderia ser confundida com vigência | Ciclos históricos; apenas `renewed` com novas datas altera a vigência |
| Central visual | Não | `npm run build` | Sem lista operacional | Lista paginada, URL filtrável, escopo, painel lateral e adaptação móvel |

### Implementado

- Migration aditiva v013, snapshot, permissões, RLS, FKs compostas, índices, concorrência otimista, séries/ocorrências, evidências, eventos, lembretes, notificações individuais e ciclos de renovação.
- Obrigações pontuais ou mensais; atraso derivado; cumprimento, cancelamento, reabertura, reatribuição e mudança de prazo auditáveis. Obrigações financeiras mantêm valor decimal e moeda explícita e não acionam pagamento.
- API preserva tenant explícito e valida permissão por operação, vínculo ativo, transições e versão. Evidência apenas aponta para `document_versions`; download continua sob a permissão própria.
- Worker interno reivindica um lembrete com `SKIP LOCKED`, proprietário/token, tentativas limitadas e cancelamento do host. Itens muito antigos não causam avalanche.
- Renovação guarda ciclos anteriores e obrigações pendentes. Intenção e negociação não mudam vigência; somente renovação registrada altera datas.

### Validado neste ambiente

- `npm run build`, verificação independente dos 13 checksums e `git diff --check` foram executados.
- O SDK .NET 10 não está instalado (`dotnet: command not found`), portanto restore/build/testes .NET, PostgreSQL descartável, HTTP, navegador autenticado e capturas não são declarados aprovados.

### Pendente

- Conectar formulário completo de criação/ações e ficha visual do contrato; a central entregue é consultiva e o percurso de mutação está disponível na API.
- Executar CI com SDK 10/PostgreSQL 18 e QA renderizado em 360/768/1280/1440 e zoom 200%. Sem runtime não foi fabricada captura.
## Correção dos diagnósticos e integridade do histórico (14/09/2026)

- O HEAD inicial desta execução foi `e923e81f8636464cd0fe0575d359a3f8d15f8861`, posterior à referência `09b078947bdf1a39bc0966ece0f60655a1daa615`; as alterações existentes foram preservadas e ajustadas incrementalmente.
- CA1859 foi corrigido somente no campo privado de transições. CS8604 foi corrigido pelo contrato de nulabilidade de `Limit`; o motivo de reatribuição é normalizado/truncado uma vez e reutilizado na decisão e auditoria, sem alterar a política preexistente.
- CA1305 permanece corrigido com datas `DateOnly` invariáveis na query e montagem legível. CA1512 permanece corrigido com `ThrowIfNegative`, mantendo zero e o cálculo a partir da data-base.
- A API agora rejeita intervalos invertidos. O evento de cumprimento preserva data efetiva, observação e identificador da evidência; a reabertura limpa apenas a projeção atual de cumprimento, mantendo o evento histórico e respeitando o `CHECK` do banco.
- `npm run build` e `git diff --check` passaram. Restore, build e testes .NET não foram executados porque o SDK 10.0.400 não existe no contêiner e o instalador oficial respondeu HTTP 403. PostgreSQL descartável, HTTP autenticado, navegador e capturas continuam não aprovados.

## Central do cliente, consumo e concessões auditadas (15/09/2026)

### Implementado

- A central **Plano e consumo** consulta a versão imutável do plano vinculada à assinatura e separa vagas ativas, convites reservados, limite contratado, capacidade adicional vigente, bytes persistidos, reservas e disponibilidade. Falha da API apresenta indisponibilidade e nunca é convertida em zero.
- Pacotes adicionais são versionados e pedidos preservam snapshot de nome, quantidade, unidade, preço/moeda opcionais e condições. As propostas iniciais permanecem em `draft`: não há preço, checkout, PIX ou pagamento fictício. O fluxo operacional começa quando um administrador publica uma versão configurada.
- Aprovação e concessão manual são operações exclusivas do SuperAdministrador com ator real, justificativa, idempotência, bloqueio concorrente, movimento imutável e auditoria. Aprovar duas vezes não duplica capacidade.
- O upload passa a recalcular a cota efetiva (plano da assinatura + concessões vigentes), serializa a reserva na linha de consumo, confirma a reserva e grava o movimento somente com a versão persistida. A leitura continua disponível quando a capacidade está excedida; somente novas gravações deixam de reservar.
- A administração global lista clientes com documento mascarado, plano, estados independentes, usuários, consumo, pendências e atividade; detalhes deixam explícito o tenant afetado e não implementam impersonação.

### Regras e limitações comerciais

- Concessão de capacidade não equivale a pagamento. Faturas/pagamentos e prorrata não existem operacionalmente no checkout atual e não foram simulados.
- Capacidade em bytes é um limite vigente; OCR e envelopes permanecem créditos consumíveis separados. Exclusão lógica de documento não reduz consumo físico.
- Troca de plano ainda não possui fluxo comercial existente completo; nenhum cálculo de prorrata ou aplicação automática foi inventado. Bloqueio administrativo continua separado do estado comercial da assinatura e do vínculo do usuário.
- Expiração automática de reservas abandonadas e contabilização dos demais produtores de arquivo (PDF gerado/cópia/evidência) exigem integração nos respectivos workers; a tabela e os movimentos foram preparados, mas somente o upload de documento está conectado nesta entrega.

### Validação desta execução

- `npm run build`, verificação independente dos checksums das 18 migrations e `git diff --check` foram executados.
- O contêiner não contém `dotnet` nem PostgreSQL de teste; portanto restore/build/testes .NET, concorrência PostgreSQL real, HTTP autenticado e captura de navegador não são declarados aprovados.

### Execução local

1. Instale o SDK indicado em `global.json` e PostgreSQL 18.
2. Execute `pwsh ./scripts/setup-local.ps1` e configure `development-runtime.json` conforme o exemplo da API.
3. Execute `dotnet restore Odca.sln --locked-mode`, `dotnet run --project src/Odca.Bootstrap` e depois os perfis API/Web/Worker.
4. Publique uma versão de pacote somente após configurar condições comerciais; as propostas distribuídas são rascunhos intencionais.

## Evolução — Jornada documental MVP: B.3.2b finalizada (2026-10-07)

- **B.3.2b (endpoints de contrato) fechada**: detail/PATCH/duplicate/archive/restore/close/events/sign/remind implementados em `Odca.Api` com lock otimista `?version=N` (409 `contract.version.conflict`), eventos em `contract_events` e auditoria; validados na stack real com smoke **T1–T12 ALL PASS**, estável em 3 execuções. Migração `m038` aplicada (schema v038, checksum `6db2939f…`).
- **Persistência confirmada no banco** (`odca` @ `odca_test_disposable`): contrato encerrado com `closure_reason` e owner trocado, 8 eventos na ordem canônica, versão `externally_signed` com PDF concluído (sha256 registrado), preparação de assinatura confirmada e 2 participantes assinados. Os 7 resíduos de execuções de smoke anteriores foram encerrados via API; estado final do tenant TC: 1 ativo canônico + 8 encerrados + 2 arquivados (sem exclusão física).
- **Causa raiz corrigida (404 dos scripts de validação)**: os 404 vistos durante o smoke eram artefato do **lexer PowerShell 5.1** — em string entre aspas duplas, `$var` seguido imediatamente por `?chave=valor` faz o parser absorver os caracteres seguintes no nome de uma variável inexistente, expandindo para vazio e esvaziando a URL (ex.: a chamada chegava como `.../contracts/=1`). **Não** era bug do framework .NET 10 nem do Kestrel: comprovado por matriz experimental e controle wire com chegada íntegra (200) no mesmo processo. Workaround obrigatório em todos os `.ps1`: `$($var)?k=v` ou concatenação explícita, e log da URL antes do request. Detalhamento em `ENTREGA_JORNADA_MVP_20261006.md` (itens A.5 e B.3.2b).
- **Em andamento**: B.3.1 (menu do cliente + permissões na Web: `_Layout.cshtml`, página Meus contratos, `OdcaApiClient.cs`, chaves em `ModulesController.cs`), seguida de B.3.2c (ficha + impressão "RASCUNHO" + histórico), B.3.3 (central de renovações) e B.3.4 (campos por perfil de atuação).
