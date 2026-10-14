# D-OC29 — Dedup cosmética das regras duplicadas de `.studio-grid` em max-width:1100px

Data: 14/10/2026 · ramo `codex/s00-foundation` (base `76d2705` = D-OC28) · não pushado

## Objetivo

Fechar o item cosmético que carregava no backlog desde o D-OC24: o `site.css` declarava **dois** blocos `@media(max-width:1100px)` para o Estúdio — um por seção ("Contract library and structured drafting studio" e "Contextual contract studio", originados de incrementos distintos, o segundo vindo de `7d2f20d`) — com colunas de `.studio-grid` repetidas, `min-width:0` de `.paper` repetido e um conflito resolvido apenas pela ordem da cascata.

## O problema encontrado

| Propriedade | Bloco 1 (seção "Contract library") | Bloco 2 (seção "Contextual") | Valor efetivo pré-fix (último vence) |
|---|---|---|---|
| `.studio-grid grid-template-columns` | `minmax(0,1fr)` | `minmax(0,1fr)` | idêntico → dedup pura |
| `.studio-side position` | `static` | — | `static` |
| `.studio-context` | `display:block` | `margin-top:1rem` | ambos sem conflito |
| `.studio-document` | `overflow-x:auto` | `overflow:visible` | **`visible`** (shorthand posterior sobrescreve o eixo x) |
| `.paper` | `min-width:0` | `min-width:0;width:100%` | `min-width:0;width:100%` |
| `.reading-mode .studio-grid` | — | `grid-template-columns:minmax(0,52rem)` | idem, mas **dependia de ficar após a regra base da linha anterior** |

Detalhe relevante: o `overflow-x:auto` do bloco 1 era **código morto** — o shorthand `overflow:visible` do bloco 2 (posterior na fonte) já o anulava. O fix remove o que estava morto e preserva o vencedor de cada propriedade.

## Correção (1 arquivo: `src/Odca.Web/wwwroot/css/site.css`)

- Bloco 1 **removido**, substituído por um comentário marcando a consolidação.
- Bloco 2 vira o **único** bloco 1100px, consolidando tudo:

```css
@media(max-width:1100px){.studio-grid{grid-template-columns:minmax(0,1fr)}.studio-side{position:static}.studio-context{display:block;margin-top:1rem}.studio-document{overflow:visible}.paper{min-width:0;width:100%}.reading-mode .studio-grid{grid-template-columns:minmax(0,52rem)}}
```

- **Posição importa e foi preservada**: o bloco consolidado continua no lugar do bloco 2 — *após* a regra base `.reading-mode .studio-grid{grid-template-columns:15rem minmax(0,52rem);justify-content:center}` da linha acima. Consolidar na posição do bloco 1 teria invertido a ordem para o seletor `.reading-mode` (mesma especificidade, ordem da fonte decide) e quebrado o modo leitura em ≤1100px.
- Blocos 768/600px intocados (propriedades diferentes, sem sobreposição).

## Prova de equivalência renderizada (headless)

Harness CDP (msedge `--headless=new --remote-debugging-port` + Node 24 com WebSocket global) com uma página-probe que carrega a **stylesheet real do repo** via query string e reproduz os seletores afetados (duas instâncias: grade normal e grade dentro de wrapper `.reading-mode`). Para cada CSS (antigo = cópia pré-edito; novo = arquivo do repo) foram medidos `getComputedStyle` de 7 grupos (grade: display/colunas/justify/gap · side: position · context: display/margin-top · document: overflowX/Y · paper: min-width/largura/box-sizing · grade de leitura: colunas/justify · paper de leitura: min-width/largura) nas larguras exatas **1101 / 1100 / 900 / 768 / 600 / 360px** (contorna o breakpoint pelos dois lados).

Resultado: **`COMPARISON=IDENTICAL (6 larguras × 2 css)`** — zero diferença de pixel em qualquer propriedade medida. Saída completa em `%TEMP%\d29-probe-out.txt` (harness em `%TEMP%\d29-probe.mjs` + `d29-probe.html`, padrão da casa de manter o esqueleto em temp).

## Gates

- Build da solution (`Odca.sln`): **0W/0E**.
- Domain: **358/358 PASS** (linha de base herdada intacta).
- Integração em `odca_test_integration` zerado via `scripts/qa/run-integration-tests.ps1 -FreshDb`: **182/182 PASS — zero falhas** (FRESH-DB-OK → BUILD-OK → TEST-EXIT=0).

## Fora de escopo

- Textos 429/500 genéricos do framework (exclusão desde o D-OC27). Sem push/merge/deploy automático.

## Próximos passos

- Push da branch quando o usuário pedir.
- Backlog restante (único): textos 429/500 do framework, se um dia entrar em escopo.
