# D-OC33 - OCR real para imagens rasterizadas e PDF escaneado, com paginação nas sugestões (16/10/2026)

**Base**: `codex/s00-foundation` @ `8d802bd` (= D-OC32, pushado). Commit único ao final, **sem push**.

## Objetivo

Fechar a pendência declarada no fim do D-OC32: o fluxo de extração do estúdio hoje opera sobre
texto (docx estruturado e PDF com camada de texto). Este incremento adiciona **OCR de verdade** —
Tesseract com linguagem configurável (padrão `por`) — para imagens PNG/JPEG e para PDF escaneado
(sem camada de texto), com **atribuição de página** (`page_number` 1-based) em todo texto extraído
de fonte paginada, degradação honesta (página em branco → zero sugestões, não falha) e falha estável
com código nomeado quando a ferramenta simplesmente não existe. Mantém a arquitetura de executáveis
externos do worker (padrão do scanner de malware), sem OCR via NuGet gerenciado.

## GATE (pré-requisito dos avanços dependentes)

- Build estrito (`-warnaserror`, Release+Debug) **0W/0E** sobre o `8d802bd`.
- Baseline verde: suíte `-FreshDb` do D-OC32 **197/197 PASS** (`evidence/d32-freshdb-final2.log`);
  sem regressões críticas abertas.
- Após o incremento: suíte `-FreshDb` **199/199 PASS** (+2 fatos novos em
  `FranchiseMeteringTests.cs`), TEST-EXIT=0 — `evidence/d33-freshdb-final.log`. Domain
  **362/362 PASS** (+4 fatos em `DocumentContentTests.cs`).

## Ferramentas externas (sem NuGet; convenções documentadas)

Instalação na máquina de desenvolvimento (sem segredos):

- **Tesseract 5.5.0** em `C:\Program Files\Tesseract-OCR\tesseract.exe` com `por.traineddata`
  (instalador oficial da tesseract-ocr.github.io, pacote de idiomas `por` já incluso nesta máquina).
- **Poppler 25.07.0** via winget portátil (`oschwartz10612.Poppler`), binários em
  `%LOCALAPPDATA%\Microsoft\WinGet\Packages\oschwartz10612.Poppler_...\poppler-25.07.0\Library\bin`
  (`pdftotext.exe`, `pdftoppm.exe`).

Convenções de invocação (mesmo padrão dos hooks existentes do worker):

| ferramenta | argumentos | saída |
|---|---|---|
| `Documents:OcrExecutable` (Tesseract) | `<imagem> <base> -l <idioma>` | `<base>.txt` — exit 0 produz o arquivo (pode ser vazio com legitimidade), **exit 1 = "sem texto detectável" → vazio**, demais códigos = erro do parser |
| `Documents:PdfTextExecutable` (pdftotext) | `-f 1 -l 200 <entrada> <saída>` | texto bruto com separador `\f` entre páginas |
| `Documents:PdfRenderExecutable` (pdftoppm) | `-png -r <dpi> -f 1 -l <max> <pdf> <prefixo>` | `<prefixo>-N.png` por página |

Nota: esta versão do Poppler removeu `-scale-y`; o DPI vai por `-r` (validado em smoke).

## A — OCR de verdade para PNG/JPEG

- Linguagem agora vem de `Documents:OcrLanguage` (padrão `por`) — antes estava hardcoded `-l por`
  no worker; a mesma configuração alimenta imagens diretas e páginas renderizadas.
- Semântica honesta de saída: página em branco gera texto vazio e job **`ready_for_review` com zero
  sugestões** (falha não é o caminho); exit 1 do Tesseract é tratado como vazio, não como erro.
- Método persistido `ocr`; a franquia mensal `ocr_credit` consome **1 página por imagem** via
  `CountOcrPages` existente (sem nova contabilidade).

## B — Atribuição de página nas sugestões

- `DocumentContent.Suggest` passa a dividir o texto por form feed (`\f`, 12) e rodar a detecção por
  segmento; `page_number` (1-based) é atribuído **somente quando o texto extraído tem separador** —
  DOCX e fontes de página única mantêm `page` nulo, preservando o contrato da UI (`studio.x_page`
  já existia e renderiza quando presente).
- Página em branco entre separadores **conta na numeração** mas não gera sugestão (sementes fantasma
  evitadas); separador final vazio é ignorado.
- Coluna `extraction_suggestions.page_number` e endpoint `GET .../extracao/{id}` já expunham o campo
  desde o D-OC32 — nenhuma mudança de API/BFF/UI.

## C — PDF escaneado: detecção honesta + fallback renderizar→OCR

- Detecção: `DocumentContent.PdfHasUsableTextLayer` — `pdftotext` devolve texto com < 20 caracteres
  úteis (limite `MinimumPdfTextLayerChars`, constante pública) → documento considerado digitalizado.
  Nunca termina "ready" com zero sugestões por silêncio.
- Fallback: `RenderScannedPagesToOcrText` renderiza até `Documents:OcrMaxPages` (padrão 20) páginas a
  `Documents:OcrDpi` (padrão 300, clamp 72–600) em um diretório temporário próprio, roda o Tesseract
  por página na mesma linguagem, e junta o texto final com `\n\f\n` — preservando os form feeds para
  paginação e para `CountOcrPages` (a franquia consome **as páginas efetivamente renderizadas**).
  Método do resultado vira `ocr` (e não `pdf-native`).
- Sem `PdfRenderExecutable` (ou sem `OcrExecutable`) configurados → `NoPdfTextLayerException`
  (singleton interno) → `failure_code='pdf-no-text-layer'` estável em todas as tentativas
  (retrai até `max_attempts` como qualquer falha estrutural, ex.: `invalid-document`), visível no
  `GET .../extracao/{id}`. Sem migração: `failure_code` é coluna livre `text`/`varchar(120)`.
- Renderização per-página + join no gerenciado (em vez de TIFF multipágina) evita quirk de
  nomenclatura entre versões do Poppler/Tesseract.

## D — Provas ao vivo (contra a base descartável `odca_test_disposable`)

- Smoke pré-codificação (`%TEMP%\opencode\d33-smoke.ps1`): PNG→Tesseract `por` com todos os alvos
  lidos corretamente (inclusive acento inferido); PDF 2 páginas→`pdftotext` com `\f`; PDF composto
  só de JPEG→`pdftotext` ~0 chars→`pdftoppm -r 300`→OCR das 2 páginas; página branca→exit 0 + vazio.
- Provas finais (script `%TEMP%\opencode\d33-proof.ps1`, cadeias C1–C4 + fase negativa N1–N3,
  tudo via BFF/Web com sessão real de browser): **33/33 checks PASS** na rodada final (iniciada
  08:04) — upload real pelo painel nas quatro cadeias, fase negativa determinística com worker
  alternativo sem executáveis (mesmo binário, config sem as chaves OCR) e worker principal de
  volta ao fim.

## E — Matriz de aceite (dez itens)

| # | item | status | evidência |
|---|------|--------|-----------|
| 1 | A PNG 1 página → OCR real c/ sugestões, evidência, método `ocr` persistido e `ocr_credit` 1 página | aprovado | `evidence/d33-ocr-a.txt` C1-1…C1-6 (upload→safe→extrair→ready; referências lidas pelo OCR; `method='ocr'` no banco; franquia consumiu 1 página) |
| 2 | A página em branco → vazio honesto (ready, 0 sugestões, sem falha, franquia 1 página) | aprovado | C4-1…C4-4 (JPEG branco 800×800) |
| 3 | B PDF 2 páginas com texto → `pdf-native` com `page_number` 1 e 2 atribuídos por segmento | aprovado | C2-1…C2-3 (referência/início na pág. 1; término/valor na pág. 2) |
| 4 | B sem separador de página → `page` nula preservada (DOCX/imagem única) + paginação unitária | aprovado | C1-3 (imagem única, 0 com página) + testes domain `SinglePageTextKeepsPageUnset`, `AttributesPageNumbersOnlyWhenTheExtractedTextHasFormFeeds`, `BlankPageSegmentsAreCountedWithoutCreatingGhostSuggestions` (362/362) |
| 5 | C PDF escaneado (só imagem) → fallback renderizar→OCR c/ método `ocr`, form feeds no raw_text e páginas OCRizadas | aprovado | C3-1…C3-4 (CT-D33-SCAN-001 pág. 1; aviso/término pág. 2; `strpos(raw_text, E'\f')>0` no banco) |
| 6 | C franquia consome as páginas efetivas do fallback (2 renderizadas → 2 créditos) | aprovado | C3-5 (`resource_movements` somou 2 para o job) |
| 7 | C sem renderer → falha estável `pdf-no-text-layer` (job `failed` + código no GET) | aprovado | G1+G2 (config alternativa verificada sem as chaves; worker principal morto, worker2 vivo) + N1 |
| 8 | Negativo sem `OcrExecutable` → falha estável `extractor-failed` (honesto, retratável) | aprovado | N2 (worker2 sem OCR executável) |
| 9 | Cadeia completa pelo painel web: upload imagem → safe → extrair → sugestões → aplicar ao rascunho (campo confirmado intocado, razão estável, reversão) | aprovado | W1/W2 + C1-7…C1-10 (apply 200 `applied`; skip `already_confirmed` no `patient_document`; baseline intocado; `row_version` restaurado) |
| 10 | Regressão: build estrito 0W/0E + suíte `-FreshDb` + GATE baseline | aprovado | Debug+Release `-warnaserror` 0W/0E; `evidence/d33-freshdb-final.log` 199/199 PASS; baseline herdada `evidence/d32-freshdb-final2.log` 197/197 |

Dez itens: **dez aprovados; zero falharam, bloqueados ou não executados**. Cada linha aponta a
evidência específica (checks nomeados no `d33-ocr-a.txt`, fatos de teste unitário ou log de suíte);
nenhuma linha depende de evidência genérica. Os quatro checks de leitura direta no banco (C1-4,
C2-4, C3-4, C4-3) assertram o `method` persistido via join com sentinel (`<sem>`/`<nulo>` quando
ausente), de modo que "vazio por silêncio" não passa como positivo.

## Configuração (sem novos segredos; arquivo de desenvolvimento untracked)

Adicionados à seção `Documents` do `development-runtime.json` (arquivo local untracked; em produção,
nas mesmas chaves de deploy):

| chave | padrão | uso |
|---|---|---|
| `OcrExecutable` | ausente | caminho do Tesseract (sem → `extractor-failed` honesto) |
| `OcrLanguage` | `por` | idioma do Tesseract (imagens diretas + páginas renderizadas) |
| `PdfTextExecutable` | ausente | caminho do `pdftotext` (hook existente do D-OC32) |
| `PdfRenderExecutable` | ausente | caminho do `pdftoppm` (novo; sem ele o escaneado falha c/ `pdf-no-text-layer`) |
| `OcrMaxPages` | `20` | limite de páginas renderizadas no fallback |
| `OcrDpi` | `300` | DPI da renderização (clamp 72–600) |

Sem novo segredo, sem mudança de banco (colunas/código já livres), sem nova chave de catálogo
(UI inalterada — `studio.x_page` já existia).

## Arquivos e migrations

- Sem migração (nada de esquema novo; `failure_code` já aceita texto livre).
- Modificados: `src/Odca.Application/Documents/DocumentContent.cs` (`Suggest` paginada +
  `PdfHasUsableTextLayer`/`MinimumPdfTextLayerChars`), `src/Odca.Worker/DocumentWorker.cs`
  (`Extract` com linguagem configurável e exit semantics do Tesseract; `RenderScannedPagesToOcrText`;
  `ScanPageNumber`; `NoPdfTextLayerException`; mapeamento de falha `pdf-no-text-layer`),
  `tests/Odca.Domain.Tests/DocumentContentTests.cs` (+4 fatos),
  `tests/Odca.IntegrationTests/FranchiseMeteringTests.cs` (+2 fatos: junção de páginas renderizadas
  consome 1 crédito por página; parsing de `page-N.png`), `docs/execution/STATUS.md`,
  `docs/execution/BACKLOG.md`, este documento.
- Fora do git (dev): `src/Odca.Api/development-runtime.json` (+6 chaves na seção `Documents`).

## Lições registradas (bugs encontrados em smoke/prova)

- Tesseract trata página branca com **exit 0 + txt vazio** (não exit 1) — o "sem texto detectável"
  (exit 1) também foi codificado como vazio por robustez; os dois caminhos são vazio honesto.
- Poppler 25.07.0 **removeu `-scale-y`** (`pdftoppm` exit 99 + usage): o DPI vai por `-r <dpi>`.
- `wkhtmltopdf` 0.12.6 (Qt 4.8) não quebra página com `img { height: 100% }` — exige elemento de
  quebra explícito (`page-break-before: always` em div de altura 0) para PDF multipágina só de imagem.
- PowerShell 5.1: `PscObjectProperties` não existe (era `InvokeMethodOnNull` engolida por
  `EAP=Continue` ao remover chaves da config alternativa) — reconstruir propriedades por
  `PSObject.Properties` e **garantir por assert** a ausência das chaves antes de usar a config.
- Regex de kill por command line deve ancorar em `.dll`: o caminho do config embutido na
  command-line de um wrapper (`...src\Odca.Api\development-runtime.json`) casava com `Odca\.Api`
  e matava o processo errado (o web).
- `pdftotext` emite form feed **também após a última página** (split gera segmento extra vazio) —
  `Suggest`/`CountOcrPages` ignoram separador final vazio por construção.
- No fluxo do estúdio (feature `ContractImports` desligada), `contract_imports` **não recebe linha**
  por upload/extração — o `UPDATE` do worker é no-op honesto; a superfície de falha é
  `extraction_jobs.failure_code` via `GET .../extracao/{id}`.

## Dependências restantes / próximo backlog

- Purga física após a retenção dos órfãos (`reconciled/{yyyy-MM}/`) segue aberta desde o D-OC32.
- Idiomas de OCR além de `por`: bastam `traineddata` + mudar `Documents:OcrLanguage` (não testados
  nesta máquina).
- Push da branch quando o usuário pedir.

## Evidências (fora do repositório, em `%TEMP%\opencode\evidence\`)

- `d33-ocr-a.txt` — provas ao vivo D-OC33 (F1 fixtures, SV1 serviços, W1/W2 sessão web, C1–C4
  cadeias reais, G1/G2 fase negativa determinística, N1/N2/N3 falhas estáveis + retorno do worker).
- `d33-freshdb-final.log` — suíte completa `-FreshDb` 199/199 PASS (FRESH-DB-OK → BUILD-OK →
  TEST-EXIT=0).
