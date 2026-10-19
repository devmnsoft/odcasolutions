# Sprint 18 — fundações de IA: análise com provedor configurável + fallback honesto (D-OC52) (19/10/2026)

Entrega a fundação de inteligência artificial do produto: novo projeto **`Odca.AI`** com `IAiService` e provedor configurável por operação (`none` padrão / `http` compatível com a API de chat em `/chat/completions`), parser defensivo de resposta, endpoint de **análise de contrato por versão exata do documento** (`GET …/documents/{docId}/versions/{versionId}/ai-analysis`) com vínculo análise↔conteúdo (SHA-256 do texto analisado + caracteres efetivamente enviados), feature gate falha-fechada `Features:AiAnalysis`, linha de dependência `ai.provider` no diagnóstico de plataforma, BFF Web + painel no estúdio documental (botão "Analisar com IA" por versão com estados honestos: carregando / indisponível / sem texto extraído), e 21 novas chaves de i18n ×4 culturas (catálogos 4014 → **4035**). A análise consome o `raw_text` já persistido pela extração (OCR/docx) da versão — **sem nova migração** (canônico v062 intacto). O provedor real segue pendente de decisão externa: com `Provider=none` a interface exibe o estado de indisponibilidade com transparência e a análise vira funcional na hora em que um endpoint configurável existir. **30 novos testes** (25 unidades puras + 5 controller-level sobre banco) entram na suíte de integração (236 → 266).

## 1. Escopo e base

- **Base**: `codex/s00-foundation` @ `0d75eb8` (Sprint 17 / D-OC51). Sem nova migração (canônico v062 intacto) e sem mudança em SQL/Domain/Worker. A fonte da análise é `odca.extraction_results.raw_text` (já persistida por versão pelos jobs de extração).
- **Novo projeto** `src/Odca.AI` (net10.0, FrameworkReference Microsoft.AspNetCore.App, `InternalsVisibleTo` → `Odca.IntegrationTests`) adicionado à `Odca.sln`; referenciado por `Odca.Api` e, para testes explícitos, por `Odca.IntegrationTests`.
- **Contrato de wire** novo em `Odca.Contracts/Ai/AiAnalysisResponse.cs`: `(Available, Reason, Summary, OverallRisk, Findings[{Code,Risk,Evidence}], Categories[], ProviderName, ContentHash, AnalyzedChars)`.
- **Delta versionado**: `scripts/i18n/d-s18-ia.json` com **21 chaves × 4 culturas = 84 entradas** → catálogos em **4035 chaves/cultura, paridade 4/4** (era 4014; 0 colisões): `studio.ai_analyze`, `studio.ai_loading`, `studio.ai_unavailable`, `studio.ai_no_text`, `studio.ai_result_h`, `studio.ai_findings_title`, `studio.ai_no_findings`, `studio.ai_categories_title`, `studio.ai_risk_{low,medium,high}`, `studio.ai_footer` ({0} modelo, {1} chars, {2} hash curto), `studio.ai_clause_{price_change,termination_notice,penalty,exclusivity,data_protection,liability_limit,indemnity,jurisdiction}` e `api.gate.release_ai`.
- **Correção do mesclador canônico** (`scripts/i18n/merge-i18n-delta.mjs`): o regex de validação de chave era `[a-z][a-z0-9]*` por segmento — recusava as chaves já aprovadas da S17 (`central-notificacoes` sem segmento pontuado e `group_início` com acento), que até aqui entravam por script dedicado. Ampliado para `\p{L}[\p{L}\p{N}_\-]*` por segmento (UTF-16/codepoint-safe); mesclagem revalidada 4/4.
- Configuração `Ai:` (section binder): `Provider` ("none"|"http"), `Endpoint` (base absoluta do gateway compatível), `ApiKey`, `Model`, `TimeoutSeconds` (clamp 5–120), `MaxInputChars` (default 24.000). `appsettings.json` do API traz `"Ai": {"Provider": "none"}`; `development-runtime.example.json` documenta o bloco completo com placeholders; segredo só em `development-runtime.json` (ignorado) ou env `Ai__ApiKey` — nada commitado.

## 2. F-1 — projeto `Odca.AI` (6 arquivos)

| Arquivo | Conteúdo |
|---|---|
| `IAiService.cs` | `AiContractInput(Text, TotalChars, Culture)`, `AiClauseFinding(Code, Risk, Evidence)`, `AiAnalysis(Available, Reason, Summary, OverallRisk, Findings, Categories, ProviderName)` + `AiAnalysis.Unavailable(provider)`; `IAiService.AnalyzeContractAsync`; `NullAiService` (provedor padrão, sem efeito colateral) |
| `AiOptions.cs` | seção `Ai` da configuração (documentação inline do contrato com o operador) |
| `AiResponseParser.cs` | conversão defensiva: remove code-fence ```` ```json ````, JSON inválido ⇒ null; clamps (resumo 1.200 / evidência 300 / categoria 40 chars; máx. 12 achados, 6 categorias com dedupe case-insensitive); risco normalizado `low|medium|high` (aceita "baixo"/"alto"/"média"; **desconhecido vira `medium` — nunca infla para alto**); código slug estável `[a-z0-9_.-]` ≤48; resposta sem nenhum conteúdo aproveitável ⇒ null (indisponível, nunca parcial disfarçado) |
| `HttpAiService.cs` | POST relativo `chat/completions` contra a base configurada; Bearer; `temperature:0`, `max_tokens:700`, `response_format:json_object`; **system prompt instrui idioma-alvo pela cultura** (pt-BR/en-US/es-ES/fr-FR) e o conjunto de códigos permitidos; texto truncado em `MaxInputChars`; qualquer falha (rede/timeout/status/JSON) degrada para indisponível com reason estável — a análise nunca propaga exceção para a jornada documental |
| `ServiceCollectionExtensions.cs` | `AddOdcaAi(config)`: provider `http` só é ativado com Endpoint absoluto **e** ApiKey presentes (barra final normalizada na base); qualquer configuração incompleta cai em `none` por construção (falha honesta, sem modo parcial cego) |

## 3. F-2 — API

- **`ContractAiController`** (`GET api/v1/organizations/{tenantId}/contracts/{contractId}/documents/{documentId}/versions/{versionId}/ai-analysis`): política `PasswordChanged` + RLS `SetContext` + permissão `tenant.contracts.read` no servidor (mesmo parâmetro dos endpoints de documento — `Actor`/`Allowed`/`SetContext` copiados do padrão `ContractDocumentsController`). Query version-scoped com LEFT JOIN no **resultado de extração mais recente da versão** (`created_at DESC, id DESC` — a análise sempre acompanha a última extração, nunca uma anterior). Estados: versão inexistente ⇒ 404; sem texto extraído ⇒ **200 `available=false, reason="no_text"`** (estado, não erro); caso contrário SHA-256 (hex, UTF-8 do texto trimado) + `AnalyzedChars = min(len, MaxInputChars)` + chamada ao `IAiService` ⇒ 200 com o estado do provedor.
- **`ReleaseFeatureGate`**: nova feature **`AiAnalysis`** resolvida por `contracts/{id}/documents/{id}/versions/{id}/ai-analysis` (prioridade preservada: `extractions` continua resolvendo antes); 503 problem+json falha-fechada com título `api.gate.release_ai` localizado na cultura do usuário (mesmo mecanismo D-OC28). `TryResolveFeature` saiu de `private` para `internal static` cobrindo a resolução por teste.
- **`PlatformDiagnosticsController`**: dependência **`ai.provider`** — `configured` quando `Ai:Provider=http` com endpoint+chave (detalhe declara que não há sonda automática) ou `not_applicable` com detalhe honesto ("nenhum provedor configurado; a interface exibe o estado de indisponibilidade"). Numeração dos comentários 8/9 preservada.

## 4. F-3 — Web BFF + UI

- **`OdcaApiClient.GetDocumentAiAnalysisAsync`** (GET autenticado, passthrough do JsonElement) e **`StudioController.DocumentAiAnalysis`** (`GET minutas/{draftId}/documentos/{documentId}/versoes/{versionId}/analise-ia`): o `contractId` é resolvido **do rascunho no servidor** (mesmo padrão da extração — o cliente nunca manda o contrato); 503/timeout do gate preservam título/detalhe localizados; 403/404 mapeados.
- **`Studio/Edit.cshtml`**: atributo `data-ai-base="/organizacoes/{t}/estudio/minutas/{draftId}/documentos/"` (a URL completa é montada pela JS por versão).
- **`wwwroot/js/studio.js`**: botão **"Analisar com IA"** ao lado do de extração em cada versão `safe`; `startAiAnalysis(documentId, versionId, row)` renderiza sob a linha com estados honestos — `studio.ai_loading` (role=status) → resultado ou erro (mensagem da API já localizada via header de cultura). Resultado: título `studio.ai_result_h` + chip de risco geral, resumo em parágrafo, seção "Pontos que merecem atenção" como cards `.suggestion` (rótulo do código resolvido na bolsa i18n pelo mapa `aiClauseKey`, **código desconhecido exibido cru — a UI nunca inventa interpretação**), categorias como chips, rodapé com `studio.ai_footer` ({0} nome do modelo, {1} caracteres com formatação cultural, {2} hash curto). Sem estado sem texto ⇒ `.warning` `studio.ai_no_text`; indisponível de provedor ⇒ `.muted` `studio.ai_unavailable`. **Zero literal de UI na JS**: todos os textos vêm da bolsa `_I18nBag` existente (prefixo `studio.` já incluído automaticamente — o partial não precisou mudar). Nenhuma classe CSS nova (reuso de `.status-chip`/`.suggestion`/`.muted`/`.success`/`.warning` — a conta das 64 classes pendentes de S20 não aumenta).

## 5. Evidência

### GATE

| Verificação | Resultado |
|---|---|
| Build Release `-warnaserror` sln (13 projetos, +`Odca.AI`) | 0W/0E ✓ |
| Build Debug `-warnaserror` sln | 0W/0E ✓ (13 projetos, BUILD-EXIT=0; após a r4b liberar Debug/Postgres) |
| Domain tests | 389/389 ✓ (inalterados — unidades de IA entraram na suíte de integração, padrão da casa) |
| Integration `-FreshDb` | **266/266 TEST-EXIT=0** ✓ (236 baseline + 30 novos; banco descartável fresco, 56 s; a 1ª rodada saiu 265/266 e capturou o defeito do item 5 abaixo — corrigido e rodada limpa reexecutada) |
| check-assets.mjs | exit=0 ✓ |
| Catálogos | 4035 chaves/cultura, paridade 4/4, BOM+LF, 0 CR ✓ (validação por codepoint) |
| Delta↔catálogo | merge canônico idempotente: **+21 por cultura / 0 colisões**; conferência pós-merge 21×4 = 84 / 0 divergências ✓ |
| Chaves usadas (JS) | `studio.js`: 85 chaves `t("…")` únicas ×4 catálogos = 0 ausentes (KEYUSE-S18-OK) ✓ |
| Chaves usadas (servidor) | `api.gate.release_ai` (ReleaseFeatureGate) + 20 `studio.ai_*` conferidas contra o delta e o catálogo real ✓; nenhum novo `L.T` em Razor nesta sprint |
| Scan de corrupção | 14 arquivos S18 validados por codepoint (glifos especiais `∈ ↔ − ⚠ ⚔ Σ` confirmados pré-existentes no HEAD do `studio.js`) — MOJIBAKE-SCAN-OK ✓ |
| d41 ao vivo (rodada r5 sobre a árvore S18) | `AUDIT-DONE checks=764 ok=764 overflow=0 authfail=0 httpfail=0` (exit 0), 0 PNGs de falha; transcript commitado `evidence/d41-design-audit-s18-rodada-final.txt` |

### Auditoria de design — rodada r5

Sem rota nova: o painel de análise é **lazy-load por fetch dentro da rota do estúdio** (já auditada em 4 idiomas ×4 viewports) e o endpoint é JSON de API. A rodada r5 refaz as mesmas **764 medidas** (191 pares × 4 viewports) sobre a árvore S18, validando que a adição do botão e dos atributos não altera overflow em nenhum dos viewports. A rodada usa o driver já endurecido na S17 (falha de rede vira HTTPFAIL + circuit breaker — D-OC51 §7 item 7): uma eventual queda ambiental de servidor produz evidência falante, não OVERFLOW falso.

### Classificação QA (não é do produto)

1. **Mesclador canônico recusava chaves aprovadas da S17**: `central-notificacoes` (sem segmento pontuado) e `group_início` (acentada) falhavam no regex ASCII — o `merge-i18n-delta.mjs` nunca havia passado sobre o delta S17 (entrado por script dedicado). Regex ampliado para segmentos `\p{L}`; comportamento idempotente revalidado (re-mesclagem 0 novas).
2. **Corrupção de canal em `PlatformDiagnosticsController.cs`**: `ã` chegou como `ǜ/ǭ` (U+01DC/U+01ED) e uma quebra de linha foi engolida entre o `if` e o branch — reescrita via script `.mjs` com montagem por charcode + validação final por codepoint (227=`ã`, 225=`á`) e swap posicional das linhas (declaração movida antes do `if`).
3. **Analyzers estritos em projeto/teste novo** (build `-warnaserror`): `CA1707` (29 métodos de teste renomeados sem underscore), `CA2016` (token passado a `Task.Delay`), `CS0246`/`CS0103` (`using System.Text.Json` ausente no `HttpAiService`), `CA1865` (`EndsWith(char)`), `CA1848`/`CA1873` (LoggerMessage — resolvido removendo o `ILogger` do controller, alinhando aos controllers irmãos de documento que não logam), `CS1503` (`EndsWith(char,StringComparison)` não existe) e `CS0029` (spread `[..]` não converte `AiClauseFinding` → DTO; mapeamento `Select` explícito).
4. **Flake de I/O no shell** durante a r4: comandos `node` isolados travaram ~3 min e voltaram sozinhos (carga da auditoria); nenhuma falha de produto associada — verificados novamente e verdes.
5. **Defeito real capturado pelos próprios testes novos (produto, corrigido antes do GATE verde)**: `HttpAiService.ExtractMessageContent` chamava `TryGetProperty` sem validar o tipo do elemento — um corpo de provedor com JSON válido porém em outra forma (raiz array, como `[]`, ou `choices`/`message` não-object) lançava `InvalidOperationException`, que não estava coberta pelo filtro de exceções do `AnalyzeContractAsync` (`JsonException` etc.) e poderia virar 500 em vez da degradação para `provider_unavailable`. O teste `ExtractMessageContentReadsChatPayloadAndToleratesMalformedBodies` falhou na 1ª rodada `-FreshDb` (265/266); corrigido com guards de `ValueKind` em raiz/`choices`/`message` (tolerância determinística a qualquer corpo com forma diferente), rebuild estrito Release+Debug 0W/0E e rodada limpa completa reexecutada: 266/266 TEST-EXIT=0.

## 6. Decisões registradas

- **Provedor http compatível com OpenAI (sem SDK)**: máximo alavancamento do mercado (OpenAI, gateways, modelos open-source via proxy) e mínimo acoplamento; a escolha do provedor segue pendente externa (registrada nas dependências do D-OC45) e vira decisão de operação, não de release.
- **Dois estados distintos, nunca misturados**: recurso desligado na liberação ⇒ **503** do gate (título/detalhe localizados); recurso ligado sem provedor disponível ⇒ **200 `available=false`**. A interface trata cada um com sua mensagem (`api.gate.release_ai` vs `studio.ai_unavailable`/`studio.ai_no_text`).
- **Vínculo análise↔versão exata**: `ContentHash` = SHA-256 do `raw_text` trimado da versão + `AnalyzedChars` declarando truncamento (limites do provedor são reais; a interface mostra quantos caracteres entraram). Mesma invariante assinatura↔versão aplicada à análise — a UI diz explicitamente "sobre o texto desta versão".
- **Códigos estáveis + rótulos por catálogo com fallback cru**: o model pode devolver um código fora do conjunto de 8 esperados (o prompt o orienta, mas a fronteira é defensiva) — a UI exibe o slug cru em vez de inventar rótulo; os 8 esperados têm rótulos em 4 idiomas.
- **Risco desconhecido ⇒ `medium`**: o parser nunca infla atenção (alto) nem apaga (baixo) informação fora do domínio declarado.
- **Estadoless em S18**: cada clique analisa sob demanda (sem tabela de cache/versões de análise) — simplicidade máxima para a fundação; o custo e o cache versionado entram em S19 junto dos cenários avançados (decisão registrada para não surpreender na trilha).
- **Autorização idêntica à leitura do documento** (`tenant.contracts.read`): ver análise não dá mais direito do que ver o documento; isolamento por organização garantido por RLS + escopo de tenant nas queries (coberto por teste de foraneio).
- **Detalhes do diagnóstico seguem em pt** (política cross-área D-OC15): o **código** da dependência (`ai.provider`) é estável; o detalhe é para operador.
- **Apóstrofo francês U+2019 nos novos valores de catálogo** (convenção tipográfica; consistente com `imp_rev.confirm_msg` da S17).

## 7. Cobertura honesta e dívidas mantidas

**Coberto**:
- Estados e autorização por teste controller-level sobre banco fresco: `no_text`, `provider_unavailable` (com hash+chars conferidos), **vínculo com a extração mais recente** (duas extrações, hashes distintos), 404 de versão inexistente, 403 de membro sem permissão (5 testes).
- Unidades puras (25): parser (forma, fence, inválido, vazio, clamps, limites, riscos, slugs), provedores (null; http: payload/autorização/URL, truncamento, degradação em 500/JSON ruim/timeout), seleção de DI (padrão none; http completo; faltas de endpoint/chave/URL inválida; binding de options) e resolução de caminho do gate (AI, prioridade extractions, upload, rotas fora do escopo).
- Suíte baseline integral (236) + build estrito + assets + catálogos + keyuse + scan de corrupção (tabela §5).
- d41 r5: 764 medidas sobre a árvore S18.

**Não coberto por execução ao vivo**: o painel de análise em si — é lazy-load dentro do editor do estúdio, que exige rascunho com documento enviado (o fixture da auditoria não carrega esse caminho de dados); o risco visual é baixo porque o render usa apenas classes existentes e `textContent` (sem nova CSS, sem HTML dinâmico inseguro). A rodada r5 cobre regressão visual das 764 medidas existentes.

**Dívidas registradas**:
- Provedor de IA real (endpoint + chave + modelo) — decisão externa pendente (item do D-OC45 sec.4).
- Cenários avançados de S19: scoring de risco, comparação com template, renovação preditiva, busca semântica, cache/versionamento de análises, disparo assíncrono via Worker.
- `[Required(ErrorMessage)]`/mensagens de domínio da API em pt (dívida cross-camada herdada — S21/S22).
- 64 classes CSS sem definição (S20).

**Roadmap**: `docs/execution/SPRINT-ROADMAP-AI.md` inalterado nesta sprint (S18 = exatamente o item registrado); sequência S19 em diante conforme diretrizes de 19/10.

## 8. Separação de evidências e portal

- **Testes locais**: GATE (§5) + d41 r5 (transcript em `evidence/d41-design-audit-s18-rodada-final.txt`).
- **CI oficial**: não verificada (sem push autorizado neste ciclo).
- **Aceite humano**: pendente (painel de análise + estados + fallback em 4 idiomas).
- **Portal**: `.odca/progress.json` inalterado em 40% (baseline/IDs/pesos intactos, sem marco credenciável).
