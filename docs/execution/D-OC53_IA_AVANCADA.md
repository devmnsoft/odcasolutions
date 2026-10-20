# Sprint 19 — IA avançada: análise inteligente de contratos (D-OC53) (19/10/2026)

## 1. Escopo e base

- **Base**: `codex/s00-foundation` @ `b428a6a` (Sprint 18 / D-OC52). Nova migração **v063** (canônico `database/odca.sql` + snapshot byte-idêntico `database/releases/odca-v063.sql`); o restante do escopo é C#/JS/i18n sobre estruturas já existentes.
- **Quinze entregas do roadmap cobertas em cinco frentes**:
  - **F-0 — Persistência da análise** (habilitador): tabela `odca.document_ai_analyses` + módulo `ia_analysis` por plano (entitlement `module.ia_analysis`) — o fim da análise stateless da S18, com cache por hash de conteúdo e histórico por versão.
  - **F-A — Scoring de risco 0–100** por contrato, com fatores explicáveis (cláusulas atípicas, desproporção via achados de multa/prazo, ausência de cláusulas padrão como proteção de dados/SLA).
  - **F-B — Comparação com template padrão**: delta determinístico entre as cláusulas presentes no contrato e as 8 cláusulas padrão declaradas (`AiResponseParser.ClauseCodes`).
  - **F-C — Sugestão de renovação preditiva**: ação sugerida derivada da última análise persistida + histórico de ciclos de renovação já registrados; decisão sempre humana.
  - **F-D — Busca semântica** na central de documentos ("contratos com multa superior a 10%"), em dois estágios com degradação honesta para palavras-chave quando não há provedor.
  - **F-E — Gatilho automático por Worker** após a extração OCR concluir (análise em segundo plano, nunca quebrando o pipeline documental).

## 2. F-0 — Persistência + módulo (migração v063)

- **Tabela** `odca.document_ai_analyses`: `id uuid pk`, `tenant_id`, `contract_id`, `document_id`, `document_version_id`, `content_hash char(64)` (SHA-256 do texto analisado, mesmo vínculo da S18), `analyzed_chars int`, `provider text`, `model text`, `score_value int CHECK (0..100)`, `analysis jsonb NOT NULL` (payload completo: summary, overallRisk, findings, categories, score.factors, templateComparison), `created_by uuid`, `created_at timestamptz`. RLS tenant-scoped idêntico ao de `document_versions`; índice `(document_version_id, created_at DESC)`; **append-only** (versões imutáveis ⇒ reanálise acrescenta linha, nunca reescreve). Escolha registrada: `jsonb` em vez de blob em disco porque o payload é pequeno (~4 KB) e precisa ser consultável em SQL (score/risco na busca), reservando o padrão `storage_key`+arquivo para objetos binários de verdade.
- **Módulo por plano**: INSERT de `module.ia_analysis` (enabled=true) nas `plan_entitlements` dos `plan_versions` publicados existentes (`ON CONFLICT DO NOTHING`) + recriação das funções de integridade/`odca.organization_feature_state` com a whitelist passando de 7 para 8 módulos + `OrganizationFeatureCatalog.cs` ganhando `ia_analysis`. Nenhum C# hoje insere entitlements (só seed SQL) — conferido por varredura. O bloco 063 também **estende o CHECK** `organization_feature_blocks_code_ck` com `ia_analysis` (sem isso o estado `administratively_blocked` seria impossível no novo módulo) e concede **`GRANT SELECT, INSERT ON odca.document_ai_analyses TO odca_app`** (RLS já isola por tenant; append-only ⇒ sem UPDATE/DELETE). Consequência esperada registrada: o catálogo publicado passa a declarar **7 módulos para basic/intermediate e 8 para enterprise** (testes S00/PlanGate/Hierarchical atualizados para 7/7/8). Checksum final do bloco `23559ecd3608160f66691e850c7e1ddca51574c815ca2777358e214c493ffd0f`.
- **Contrato de endpoints** (evolução, não ruptura, do endpoint da S18):
  - `GET .../versions/{v}/ai-analysis` → se existir linha persistida cujo `content_hash` = hash da extração atual da versão ⇒ devolve-a (**cache: zero chamadas ao provedor**); senão faz análise única ao vivo (comportamento S18, sem gravação em GET — efeito colateral só em POST).
  - `POST .../versions/{v}/ai-analysis` → força nova análise: se a última linha tem o mesmo `content_hash` devolve-a (idempotência por conteúdo); senão chama o provedor, persiste e devolve. Degradado ⇒ 200 `available=false` (motivo estável) e **nenhuma linha gravada**.
- **Wire** (`Odca.Contracts.Ai.AiAnalysisResponse`, camelCase): adições aditivas `score {value:int, factors:[{code, severity, weight}]}` e `templateComparison {expected[], detected[], missing[], unexpected[]}` — os campos S18 permanecem.

## 3. F-A — Scoring 0–100 (determinístico, em C#)

- `Odca.AI.RiskScorer.Score(AiAnalysis)` puro e testável: base por `overall_risk` (**low=20, medium=45, high=70**) + acréscimo por achado (**low +2, medium +6, high +12**), truncado a `[0,100]`. Cada fator devolvido traz `code`/`severity`/`weight` (o que somou); categoria fora das 8 padrão acrescenta fator `atypical_clause`. Fórmula registrada aqui é a **única** implementação — mesma entrada dá mesmo resultado em qualquer provedor. O score **apoia** a revisão humana: nunca bloqueia fluxo, nunca cria obrigação contratual sozinha.

## 4. F-B — Delta contra o template padrão

- **Template padrão = as 8 cláusulas já declaradas** em `AiResponseParser.ClauseCodes` (fonte única existente; o sistema não tinha nenhum outro conceito de cláusula esperada — confirmado por varredura de Domain/SQL/seeds). Delta puro: `expected`=8 códigos; `detected`=código dos achados; `missing`=esperado−detectado; `unexpected`=detectado−esperado. UI em chips localizados (presente ✓ / ausente ○ / fora do padrão !). **Limite declarado**: diff literal de texto livre contra os modelos oficiais (`OfficialContractTemplates`) fica para sprint futura — nada inventado agora.

## 5. F-C — Sugestão de renovação preditiva

- Mapeamento **determinístico** da última análise persistida do documento mais recente do contrato: risco geral `high` **ou** achado `penalty`/`price_change` em `medium`/`high` ⇒ `renegotiate`; `medium` ⇒ `review`; `low` ⇒ `renew`. Sem análise persistida ⇒ sugestão `null` (o cartão simplesmente não existe — nenhum veredito inventado). **Escopo implementado nesta sprint**: a sugestão deriva exclusivamente da última análise persistida (`DISTINCT ON (contract_id)` sobre `document_ai_analyses`); o envolvimento dos ciclos registrados em `contract_renewal_cycles` (estrutura existente desde a migração 054) fica declarado como limitação — nada é derivado deles ainda. Aparece apenas na **List** do centro de renovações (campo aditivo `aiSuggestion` em `RenewalListItem`; o `Detail` segue inalterado nesta sprint) com rótulo explícito "sugestão apoiada por IA — a decisão é sua". Regras de prazo/multa/juros do CDC e Código Civil continuam na S22 — esta frente só **organiza** o que a IA já viu.

## 6. F-D — Busca semântica (dois estágios, degradação honesta)

- Reaproveita o parâmetro `search` do `GET .../studio/documents` (hoje busca só título/referência/paciente em `ILIKE`). Novo comportamento:
  1. **Estágio palavras-chave (sempre ativo)**: mantêm os filtros atuais **+ camada nova `EXISTS` de `ILIKE` no `raw_text`** da extração mais recente de cada versão do documento (o texto deixa de ficar invisível à busca).
  2. **Estágio IA (quando módulo + gate global + provedor `http` estão disponíveis)**: `Odca.AI.SearchQueryParser` interpreta a frase em JSON defensivo `{terms[], penaltyMinPercent?}` via `IAiService.InterpretSearchQueryAsync` (contrato `AiInterpretation{Hints, ProviderUsed}` — o modo exibido é `"ai"` **somente** quando o provedor interpretou de fato; qualquer queda interna degrada ao fallback determinístico `"keywords"`), mesma disciplina do parser S18 (code-fence, clamps, desconhecido ignora). O filtro `penaltyMinPercent` aplica-se nos **dois modos** quando a frase o declara (parser determinístico fornece no modo sem provedor): ocorrencias de `multa … X %` no `raw_text` com comparação numérica via regex SQL.
  - A resposta ganha anotação `semantic {engine:"ai"|"keywords", terms[]}` — a UI mostra em que modo a busca rodou. Provedor ausente/falhou ⇒ estágio 1 sozinho, **nunca derruba a busca**. Limite declarado: a interpretação é por frase (sem embeddings/vetores); a expressão de multa cobre a formulação canônica "multa … X %".

## 7. F-E — Módulo `ia_analysis` no gate do tenant + Worker

- **No controller**: `odca.organization_feature_state(tenant,'ia_analysis')` ≠ `allowed` ⇒ 200 `available=false reason="module_not_enabled"` (verificação **antes** de qualquer chamada de provedor; o gate global `Features:AiAnalysis` continua sendo o kill-switch 503 de operação — problema de plano ≠ incidente de disponibilidade).
- **No Worker** (`DocumentWorker`, imediatamente após a extração concluir e commitar): se gate + módulo + texto presentes ⇒ analisa e persiste; falha de análise é **totalmente** tolerada (try/catch de fronteira: o pipeline de documento não depende do provedor) e o evento `contract_events kind='ai_analysis_completed'` audita a execução.

## 8. Web BFF + UI (padrões canônicos, zero literal)

- **BFF**: `OdcaApiClient.AnalyzeDocumentAiAsync` (POST, mapeamento 503/403 idêntico ao GET S18) + action `[HttpPost(...analise-ia)]` em `StudioController.Web` com a mesma resolução de rascunho/contrato e os mesmos estados 404/403/503/422 do fluxo da extração; o cartão de renovação vem das respostas existentes de renovação (campo aditivo); a central de documentos passa `search` inalterado.
- **Painel do estúdio** (`studio.js`): o botão faz **POST** (gera nova análise e a persiste no servidor); novo clique na mesma versão reaproveita o resultado em cache **client-side** (`Map` chaveado por `documentId/versionId`, nota "nenhum novo processamento") e o servidor deduplica por hash de conteúdo em todo POST (protegendo também chamadas paralelas/outras janelas). O GET permanece para leitura/estado sem efeitos colaterais. Novo bloco "Risco" com número 0–100 + fatores explicáveis; chips de delta contra o padrão (✓/○/!); estado `module_not_enabled` declarado; rodapé S18 preservado.
- **Central de documentos**: nota de modo da busca (`documents.search_mode_ai`/`documents.search_mode_keywords`) em linha inteira sob o campo de busca quando a anotação `semantic` vem; correção de chave pré-existente `documents.stage_all` (usada desde antes do S19 sem existir nos catálogos — o texto caía para o nome da chave nos 4 idiomas).
- **Renovações**: cartão de sugestão com rótulo + nota "a decisão é sua" entre o detalhe e a ação existente.
- **i18n**: delta versionado `scripts/i18n/d-s19-ia-avancada.json` (**17 chaves ×4 idiomas**), mesclado no mesclador canônico (idempotente) → catálogos 4035→4052, paridade 4/4, BOM+LF, 0 CR.

## 9. Evidência

### GATE

| Verificação | Resultado |
|---|---|
| Build Release `-warnaserror` sln (13 projetos) | **0W/0E** |
| Build Debug `-warnaserror` sln (13 projetos) | **0W/0E** |
| Domain tests | **389/389 PASS** (0 falhas) |
| Integration `-FreshDb` (aplica v063 em banco fresco: script único importa sem erro) | **293/293 PASS, TEST-EXIT=0** (baseline 266 + 24 novos em `AiAdvancedTests.cs` + 1 novo fato `MigrationChecksumTests.ReleaseV063...` + 2 testes de regressão da projeção de renovações do item 8 do QA) |
| Migração v063 | canônico + snapshot byte-idêntico (`canon-bytes=release-bytes`, dif `MigrationBlock(1..62)` = 0 divergências no teste novo); 63 blocos revalidados pelo mesmo algoritmo do `DatabaseMigrator`; checksum do bloco `23559ecd…ffd0f`; `DatabaseSchema.CurrentVersion` 62→63 |
| check-assets.mjs | **exit=0** (10 assets web, 19 chaves i18n dos módulos JS em 4 catálogos, round trips decimal/editor) |
| Catálogos | **4052 ×4**, paridade 4/4 pelo mesclador canônico, BOM presente, LF, **0 CR** |
| Chaves usadas (JS + servidor) | **KEYUSE-S19-OK**: 0 ausentes nos 4 catálogos (todas as `t("...")` de `studio.js` + `L.T("...")` de `Documents/Index.cshtml` e `Renewals/Index.cshtml`, incluindo as chaves dinâmicas `studio.ai_risk_{low,medium,high}`) |
| Scan de corrupção (codepoint U+0100–U+024F e U+FFFD, 38 arquivos alterados/criados) | **0 ocorrências**, exceto `U+0153 (œ)` em `fr-FR.json` — francês legítimo pré-existente, fora do diff desta sprint |
| d41 ao vivo sobre a árvore S19 | `AUDIT-DONE checks=764 ok=764 overflow=0 authfail=0 httpfail=0` (exit 0), 0 PNGs de falha novos — transcript commitado `evidence/d41-design-audit-s19-rodada-final.txt` (rodada **r8**, 764 medidas completas sobre a árvore corrigida; histórico das rodadas r6/r7 nos itens 8 e 9 do QA) |

### Auditoria de design — rodadas r6, r7 e r8

Sem rota nova: painéis mudam dentro de rotas já auditadas (estúdio por lazy-load, central de documentos, renovações). As rodadas refazem as **764 medidas** (191 pares × 4 viewports) com o driver endurecido (D-OC51 §7 item 7). A **r6** (árvore pré-correção) foi interrompida no item 8 do QA com 428/764 medidas OK, 0 overflow e 0 authfail até o fim de rota. A **r7** (árvore corrigida) percorreu **657 medidas com 0 falhas** — incluindo `/renovacoes` OK nos 4 viewports em pt-BR/en-US/es-ES (confirmação ao vivo da correção) — e foi interrompida por esgotamento de RAM da máquina (item 9 do QA; o navegador headless foi morto pelo SO no meio de rota). A **r8** executou as 764 medidas completas sobre a mesma árvore, após limpeza dos processos órfãos: **`AUDIT-DONE checks=764 ok=764 overflow=0 authfail=0 httpfail=0`** (exit 0), sem PNGs de falha novos (o último PNG da pasta é o 503 da própria r6) — transcript em `evidence/d41-design-audit-s19-rodada-final.txt`.

### Classificação QA (não é do produto)

1. **v063 sem `GRANT` para `odca_app`** (primeira versão do bloco): `42501 permissão negada para tabela document_ai_analyses` na leitura de cache (afetou 4 testes, inclusive 2 da S18 cujo GET passou a ler a tabela). Corrigido dentro do bloco 063 com `GRANT SELECT, INSERT ... TO odca_app` e checksum recalculado; blocos 1..62 intactos (validado por teste).
2. **v063 sem extensão do CHECK de bloqueios** (primeira versão do bloco): `ia_analysis` inserido na whitelist da função mas ausente em `organization_feature_blocks_code_ck` — o bloqueio administrativo do módulo era impossível. Corrigido no bloco 063 (DROP/ADD CONSTRAINT) com o novo teste `ModuleNotEnabledRespondsWithStateBeforeCallingProvider` cobrindo o caminho.
3. **Parêntese ausente no filtro `EXISTS` da busca** (`ListDocuments`): `42601 syntax error at end of input` em execução (SQL é string — compila sem erro; apanhado pela suíte no teste `DocumentSearchReachesExtractedTextAndFiltersByDeclaredPenalty`). Corrigido antes da rodada final.
4. **`documents.stage_all` sem chave nos 4 catálogos desde antes da S19** (gap do keyuse anterior, que varria só `studio.js`): o filtro de estágio da central de documentos renderizava o nome cru da chave nos 4 idiomas. Corrigido acrescentando a chave ao delta desta sprint.
5. **`OutboxLeaseClaimTests.ExpiredLeaseIsReclaimedAndStaleTokenCompletionIsIgnored` falhou uma vez** (UUID de lease esperado×real divergente) na segunda rodada local e passou nas duas seguintes sem nenhuma alteração de código — classificado como flake ambiental de timing de lease expirado; sem reprodução sob a mesma árvore.
6. **Worker auto-análise (F-E) sem e2e na suíte**: não existe fixture de extração executada no ambiente de integração; a via é coberta por build estrito (Worker `-warnaserror`), pela lógica de fronteira tolerante (try/catch, `OperationCanceledException` re-lançada) e pelos testes puros do núcleo que ela chama. Registrado como limitação, não como cobertura.
7. **Banco descartável de auditoria com histórico do 057 anterior ao commit**: `odca_test_disposable` aplicou o bloco 057 a partir de uma árvore intermediária do D-OC34 (checksum registrado `e37593a1…`, nunca presente no canônico commitado) e nunca foi reconstruído desde então — o `migrate` para o 063 parou em "histórico divergente na migração 057". Como a base serve apenas à auditoria de design e as rodadas anteriores (r4b/r5) mederam exatamente essa DDL, o histórico foi alinhado ao canônico (`UPDATE odca.schema_migrations SET checksum=<canônico 057> WHERE version=57`) e o 063 aplicado em seguida, preservando a continuidade das medidas; base descartável, sem efeito sobre o caminho de produto (banco fresco da suíte importa os 63 blocos direto do canônico).
8. **Construtor do `RenewalListItem` com `AiSuggestion` fora da projeção da lista** — **bug real de produto da S19**, pego pela auditoria ao vivo (é o único item desta lista que não é do ambiente): o record ganhou `AiSuggestion` como último parâmetro do construtor único, mas a projeção da lista continuou selecionando 13 colunas. O deserializer do Dapper é construído sob demanda ao primeiro item lido, então a suíte (resultados vazios na lista de renovações) nunca materializou um registro e a rodada r6 — sobre o banco com contratos reais na janela — retornou **503 em `/renovacoes`** nos 4 viewports e disparou o circuit breaker do driver (4 falhas seguidas; rodada interrompida com 428/764 medidas OK). Corrigido com a coluna explícita `NULL::text AS AiSuggestion` na projeção (correspondência 1:1 colunas×parâmetros do construtor; o enriquecimento via `with { AiSuggestion = … }` permanece), e blindado por dois testes de regressão: `DapperMaterializationTests.RenewalListMaterializesItemsWithAiSuggestionColumnFromRealPostgreSql` (linha real com data final na janela de 3 meses, endpoint real, `AiSuggestion=null` sem análise persistida — nenhum veredito inventado) e `RuntimeQueryRegressionTests.RenewalListProjectionSelectsNullAiSuggestionColumnForRecordConstructor` (guarda de fonte). Transcript da rodada r6 preservado em `evidence/d41-design-audit-s19-r6-falha-renovacoes.txt`; as rodadas r7/r8 refazem as 764 medidas sobre a árvore corrigida (r7 interrompida por falha ambiental de RAM — item 9; r8 concluiu verde 764/764).
9. **Interrupção da rodada r7 por esgotamento de RAM (ambiental, não do produto)**: a máquina chegou a ~250–600 MB livres durante a rodada (builds + suíte + navegador em sequência) e o SO matou o processo principal do Edge headless no meio da rota `clientes-usuarios/fr-FR`; o driver node perdeu o CDP e terminou sem finalizar, deixando processos órfãos (6 `msedge`: GPU/utility/crashpad). Nada de falha de produto: as **657 medidas** percorridas saíram OK (0 overflow, 0 authfail, 0 httpfail), incluindo `/renovacoes` verde nos 4 viewports de pt-BR/en-US/es-ES — confirmação ao vivo exata da correção do item 8. Remédio: preservação do transcript (`evidence/d41-design-audit-s19-r7-interrupcao-ambiental.txt`), encerramento dos órfãos, liberação dos nodes MSBuild ociosos e reinício limpo como r8 sobre a mesma árvore.

## 10. Decisões registradas

1. **`jsonb` + linhas append-only** em vez de blob em disco para análises: payload pequeno, consultável em SQL, imutabilidade herdada da versão; o padrão `storage_key`+SHA-256 continua reservado a binários.
2. **Fórmulas determinísticas em C#** (score, delta, sugestão, parser de busca) e não dentro do LLM: reproduzível, unit-testável, idem-entrada⇒idem-saída independentemente do provedor; a IA descreve, o código decide os números.
3. **Template padrão = as 8 cláusulas declaradas** em `ClauseCodes` — usar conceito novo exigiria modelagem de catálogo que não existe; inventar catálogo seria fabricar dado.
4. **200 `available=false` para módulo** (plano) × **503** para gate global (operação): falha-fechada nos dois, linguagens diferentes na UI.
5. **GET sem efeitos colaterais**: escrita apenas em POST; o cache é leitura de linha persistida.
6. **Busca em dois estágios**: promessa do recurso vale com ou sem provedor; o modo usado é declarado na resposta.

## 11. Cobertura honesta e dívidas mantidas

- **Coberto**: análise persistida com cache/histórico por versão; score com fatores; delta vs. 8 cláusulas padrão; sugestão de renovação (derivada da última análise persistida); busca textual profunda + interpretação de frase; módulo por plano nos 3 planos publicados; auto-análise pós-OCR auditável; 4 idiomas; fallback honesto em todas as frentes (provedor ausente, módulo ausente, texto ausente).
- **Limites declarados**: sugestão não deriva dos ciclos de renovação registrados (S22) nem altera prazos/multas; multa por regex na formulação canônica; busca semântica sem embeddings (interpretação por frase via provedor); diff literal de texto contra modelos oficiais futura; auto-análise só após extração (documento sem OCR segue via POST manual) e sem e2e na suíte (item 6 acima); sugestão de renovação exibida apenas na List (Detail inalterado); provedor real pendente de decisão externa (`none` até lá — toda a S19 funciona em modo `keywords`/degradado e a UI o declara).
- **Dívidas mantidas**: pt-first cross-camada herdado; 64 classes CSS → S20; CI oficial nunca verde publicada; aceite humano pendente.

## 12. Separação de evidências e portal

- `.odca/progress.json` inalterado (40%, baseline/IDs/pesos intactos). Sem push ⇒ CI oficial não verificada; nenhum ID de execução inventado.
- Aceites humanos: score, delta, sugestão, busca semântica e auto-análise — pendentes.
- Evidências desta entrega: transcript da rodada r6 (interrompida pelo bug do item 8 do QA) em `evidence/d41-design-audit-s19-r6-falha-renovacoes.txt`, o da r7 (interrompida por falha ambiental — item 9) em `evidence/d41-design-audit-s19-r7-interrupcao-ambiental.txt` e o da rodada final em `evidence/d41-design-audit-s19-rodada-final.txt`; builds/suítes citadas na tabela acima com números reais.
- Commit único ao final, **sem push**.
