# D-OC23 — Bloco F: responsivo 360/768/1440px do Estúdio

Data: 2026-10-13 · Branch: codex/s00-foundation · Base: `386edc2` (D-OC22)

## Contexto e escopo

Item do Bloco F: verificar que as páginas do Estúdio renderizam **sem quebra** (sem overflow horizontal) em viewports de exatamente **360px, 768px e 1440px**, corrigir os defeitos reais encontrados e documentar a matriz de evidência.

- **Páginas em escopo (6)**: Índice (`/estudio`), Minuta (`/minutas/{draftId}`), Versão (`/versoes/{versionId}`), Comparar versões (`/comparar-versoes`), Modelos/novo e Modelos `{id}/editar` — os dois últimos usam a mesma view `TemplateEdit.cshtml`.
- **Alvos reais no banco QA** (`odca_test_disposable`, tenant `20000000-…-0001`): minuta `b815795d-e513-49d9-abfa-6082307ee6f4`, versão `b384b859-d500-4f5a-acec-07cfc1457c2d`, modelo `261516a2-02ef-43bb-abdc-8bcf48701d5a`.
- Matriz: 6 páginas × 3 larguras = **18 combinações**.

## Método (harness reprodutível)

Não há ferramenta de viewport no catálogo de tools → harness próprio: `msedge` headless (Edg/155.0.4283.45) dirigido por Node 24 via CDP (WebSocket nativo).

- Viewports exatos via `Emulation.setDeviceMetricsOverride`: 360×800 / 768×1024 / 1440×900.
- Sessão real: cookie `__Host-odca-session` injetado via `Network.setCookie` (jar do login+MFA via curl).
- Auditoria por combinação: `documentElement.scrollWidth − innerWidth` (tolerância ≤ 2px) + lista dos elementos com `right > viewport + 2px` (descendentes de scrollers horizontais excluídos) + screenshot full-page.
- Runner encadeado único `%TEMP%\sv-resp-chain.ps1` (o app dotnet morre quando o shell que o lançou sai): gate Postgres por `psql` (não por estado de serviço — quirk conhecido da máquina) → kill dotnet → API/Web com logs redirecionados → readiness → reinserção do código MFA REV21A (hash SHA-256 `uid:CODE`) → login+MFA via curl → jar pt-BR → 6 URLs → captura Node (18 PNGs + `sv-resp-report.json` em `%TEMP%\sv-resp-evidence\`).
- Probes complementares: `sv-resp-jsprobe.mjs` (exceções/console por página), `sv-resp-brun.mjs` (avaliação in-browser: `textContent` exato de cada `<script>` + `new Function` na própria página — ground truth do parser HTML).

## Defeitos encontrados e correções

### A) Layout (responsivo em si) — 4 defeitos

| # | Combo | Sintoma | Causa raiz | Correção |
|---|-------|---------|-----------|----------|
| 1 | 360 · model-editar | overflow 53px | grids inline em `TemplateEdit.cshtml` (`style="grid-template-columns: 2fr 1fr 1fr"` e pares `1fr 1fr`) imunes às media queries, somado ao `min-width:auto` implícito dos itens de grid | classes `.form-grid.tpl-meta` e `.tpl-pair` (×2) no lugar dos styles inline; CSS: `.tpl-pair{minmax(0,1fr)×2}`, `.form-grid.tpl-meta{2fr 1fr 1fr}` com `@media(max-width:768px){…:1fr}` e `.template-editor-grid>*{min-width:0}` |
| 2 | 360 · version | overflow 56px | `<code>` com o hash SHA-256 da versão (token de 64 chars) não quebra linha | `.document-shell code{overflow-wrap:anywhere}` (sem mudar markup) |
| 3 | 1440 · edit | overflow 16px | coluna central de `.studio-grid` era `minmax(35rem,1fr)` (mínimo 1136px); em 1440px com a sidebar de 17rem, o disponível é ≈1072px | `minmax(0,1fr)` |
| 4 | 360 · edit | overflow 4px | margem negativa `calc(-1*clamp(1.5rem,4vw,3rem))` = 24px, mas o padding do `.page-shell` em ≤768 é 1.25rem = 20px (o grid escapava 4px para cada lado do container) | margens `-1.25rem` explícitas no bloco `@media(max-width:600px)` |

### B) JavaScript (bug pré-existente descoberto pela auditoria) — bag i18n quebrado

A auditoria registrou exceções de parse JS em **todas** as cargas das 4 páginas que incluem `_I18nBag.cshtml` (Edit, TemplateEdit ×2 rotas, Version): `SyntaxError: Unexpected token '&'` (linha do bag) + `SyntaxError: Invalid or unexpected token` (linha da IIFE). Correlação exata: as duas páginas sem o partial (Index, CompareVersões) tinham zero exceções. Duas causas raiz independentes:

1. **Bag emitido com HTML-encoding**: `@JsonSerializer.Serialize(...)` sofre o encoding padrão do Razor (`"`→`&quot;`) dentro do `<script>`. No ambiente de validação, referências de caractere **não são decodificadas dentro de `<script>`** — demonstrado empiricamente com teste controlado na mesma engine (página local com `&amp;`/`&quot;`/`&#65;`: o `textContent` visto pelo navegador permanece cru e o script falha no parse). Consequência: `window.odcaI18n` nunca existia e a IIFE também falhava (ver item 2), então `window.OdcaI18n` tampouco existia — todos os strings dinâmicos das 4 páginas (`t('studio.ver_pdf_generating')`, diálogos de `confirm()`, toasts, reindexação de participantes…) caíam no fallback de identidade e exibiam a **chave crua** em vez do texto traduzido. Só as strings renderizadas pelo Razor (que usam o catálogo direto) estavam corretas.
2. **IIFE sintaticamente inválida**: o tail do inline era `_+i+"};});})();` — aspas fechando mal a string vazia + chaves/ponto-e-vírgula deslocados (string não terminada). Corrigido para `_+i+"";});}};})();` (validado com V8 `new Function` sobre o trecho exato antes e após aplicar).

Correção do bag: emitir o JSON com `JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }` (`\"`, `\u003C`, `\u003E`, `\u0026`) via `Html.Raw` — o padrão canônico para embutir JSON em `<script>`: é válido JS **independentemente** de a engine decodificar entidades e torna impossível surgir `</script>` em dado (o `<` vira `\u003C`). Sem mudança de contrato para o JS consumidor.

## Mudanças

| Arquivo | Alteração |
|---------|-----------|
| `src/Odca.Web/wwwroot/css/site.css` | coluna central `.studio-grid` → `minmax(0,1fr)`; margens `-1.25rem` no bloco ≤600; novo bloco D-OC23: `.form-grid.tpl-meta` (+ media ≤768 → 1 coluna), `.tpl-pair` com `minmax(0,1fr)×2`, `.template-editor-grid>*{min-width:0}`, `.document-shell code{overflow-wrap:anywhere}` |
| `src/Odca.Web/Views/Studio/TemplateEdit.cshtml` | 3 grids inline convertidos em classes (`.form-grid.tpl-meta`, `.tpl-pair` ×2) — sem mudança de conteúdo |
| `src/Odca.Web/Views/Studio/_I18nBag.cshtml` | bag serializado com `UnsafeRelaxedJsonEscaping` + `Html.Raw` (antes encoding padrão do Razor); syntax fix do tail da IIFE `OdcaI18n` |

## Evidência comportamental

- Build da solution **0W/0E**.
- **Matriz 18/18 após as correções: `loaded=true · overflow=0px · elements=0 · jsErrors=0` em todas as combinações → `RESPONSE-CLEAN`** (18 screenshots full-page + report JSON em `%TEMP%\sv-resp-evidence\`). Antes da correção JS: 12/18 combinações com `jsErrors=2` (layout já limpo após os 4 fixes CSS/markup).
- Verificação funcional do i18n dinâmico (eval dentro da página real de Modelos/novo): `window.odcaI18n=true`, `window.OdcaI18n=true`, `t('studio.preview_title') = "Prévia do modelo"`; bag pt-BR encolheu de 45.599 → 31.318 chars com **zero** entidades HTML residuais.
- Gates: Domain **353/353 PASS** · paridade de catálogos **4/4 PASS** · suíte de integração **54 falhas / 116 aprovados / 170 — idêntica à linha de base ambiental** (as 54 são as suites gateadas por banco falhando na fixture, mesma condição do baseline; zero nova regressão).

## Limitações

- Homologação visual de PDFs permanece item próprio do Bloco F.
- A matriz cobre as 6 páginas do Estúdio; demais áreas não foram auditadas neste incremento.
- As regras duplicadas de `.studio-grid` em `max-width:1100px` (L202 `overflow-x:auto` vs L207 `overflow:visible`) permanecem como estão — ambas colapsam para coluna única e o comportamento final é idêntico; deduplicação cosmética fica para um incremento de limpeza.
- O teste controlado de decodificação de entidades foi feito na engine do ambiente (Chromium/Edge 155 headless); a correção do bag foi escolhida especificamente para ser robusta sob os dois comportamentos (decodifica ou não).
- Sem push/merge/deploy automático.
