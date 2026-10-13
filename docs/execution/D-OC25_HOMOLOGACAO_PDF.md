# D-OC25 — Homologação visual de PDFs (último item do Bloco F)

Data: 13/10/2026 · ramo `codex/s00-foundation` (base `08d14d5` = D-OC24) · não pushado

## Objetivo

Fechar o último item do Bloco F: homologar visualmente a camada de impressão. Renderizar PDFs **gerados pela aplicação real** e inspecionar página a página (layout, fontes, valores canônicos pt-BR, capa/sumário, quebras de página, rodapé), conferindo a equivalência com o preview HTML (`preview == saída`, D-OC5/D-OC22) antes de aprovar a camada.

## Método

- **Gerador auditado**: `ContractDocumentRenderer.ToPdf` (Odca.Application) → PDF 1.4 mínimo determinístico ("SimplePdf"): A4 595×842pt, margens 54pt, leading 14pt, máx. 52 linhas/página, Helvetica/Helvetica-Bold WinAnsi/CP1252 (acentos, travessão U+2014 e bullet U+2022 como bytes selecionáveis), caixa de callout (fundo claro + barra escura à esquerda), rodapé `Página X de Y` fora do bloco `BT..ET`, dicionário `/Info` com `/Producer (ODCA Solutions (odca-pdf-N))` e `/CreationDate` somente com capa (data do snapshot, D-A4).
- **E2E pela aplicação real**: API `https://localhost:7143` com token Bearer de login + desafio MFA reais (código de recuperação REV21A, uso único) → `POST studio/drafts` (modelo `261516a2…` publicado) → `PUT studio/drafts/{id}` com documento rico próprio (2×h1 + h2, parágrafos com campos Currency/Date/text, tabela 3×2, 2 callouts, bullets, quebra explícita e URL longa de probe) → `POST drafts/{id}/versions` (chave de idempotência) → `POST studio/versions/{id}/pdf` (200 `completed`) → `GET …/pdf` → sha256 do download comparado com `pdf_sha256` do banco.
- **Renderização para inspeção**: Ghostscript 10.06.0 (`gswin64c -sDEVICE=png16m -r150`). Pegadinha descoberta e documentada: no Windows o gs falha com caminho absoluto de **backslash** (`/undefinedfilename`); precisa de barras diretas.
- **Medição objetiva de overflow**: varredura de pixels (System.Drawing) da página renderizada — pixel escuro mais à direita da faixa do texto vs. margem direita `x=(595−54)/595·W`.
- **Documentos inspecionados** (PNGs em `%TEMP%\sv-pdf-evidence\`):
  - versão real `b384b859-d500-4f5a-acec-07cfc1457c2d` (consentimento cirúrgico do catálogo oficial; sem capa; callout, moeda, datas, campo opcional vazio);
  - versão `69415b94-f5e3-457d-843b-fce27635139a` (v1 do fixture rico), gerada em `odca-pdf-2` **pré-fix** — evidência dos defeitos;
  - versão `4f11eea5-5553-4d5a-a4c5-307932eafd0a` (v2 do mesmo rascunho), gerada em `odca-pdf-3` **pós-fix** — estado aprovado.

## Aprovado (comportamento confirmado em páginas reais, pré e pós-fix)

- Acentuação integral e símbolos WinAnsi corretos ("CIRÚRGICO", "organização", "jurídica.", travessão, bullets "•") — zero `?` nos trechos inspecionados.
- Camada canônica pt-BR (D-OC22): moeda `R$ 1.234,56` / `R$ 4.825,90`; datas ISO preservadas (`2027-03-15`, `2026-12-01`); campo opcional vazio → travessão `—` sem inventar dado.
- Capa: organização em maiúsculas (cultura invariante — espelha o `text-transform:uppercase` do CSS), CNPJ formatado, "Emitido por", data congelada do snapshot.
- Sumário: entradas das cláusulas com acentos, página própria após quebra explícita.
- Callouts: caixa + barra, rótulo em negrito, corpo dentro da caixa (inclusive quando o texto quebra em várias linhas).
- `pageBreak` explícito honrado; "Página X de Y" centralizado e com acento.
- Pipeline de integridade: sha256 do download == `pdf_sha256` do banco nas duas gerações; replay de versão `completed` devolve bytes idênticos; `/Producer` identifica a versão do renderer.

## Defeitos encontrados e corrigidos (`odca-pdf-2` → `odca-pdf-3`)

| # | Defeito | Evidência pré-fix (medida/renderizada) | Correção |
|---|---------|----------------------------------------|----------|
| F1 | Linha de versão da capa sem acento e separador diferente do preview | capa "Versao 1 - Emitido em…" vs. preview e corpo "Versão" | `Versão {n} · Emitido em {dd/MM/yyyy}` — idêntico à linha de `RenderCoverHtml` |
| F2 | Título em maiúsculas só na capa | mesmo documento: capa "TERMO DE FIDELIDADE ODCA HOM PDF" vs. corpo "Termo de Fidelidade ODCA HOM PDF" (e preview HTML) | removido `ToUpperInvariant()` do título (a organização segue maiúscula, como no CSS) |
| F3 | Título do sumário sem acento | "SUMARIO" vs. preview "Sumário" | "SUMÁRIO" (estilo maiúsculo preservado — escolha de estilo da camada impressa) |
| F4 | Token indivisível longo estoura a margem direita | URL de 108 chars: pixel escuro até **x=1133** vs. margem **x=1127** (borda da página 1239) | `Wrap()` divide palavra mais larga que a linha em blocos fixos (sem hifenização, determinístico) |
| F5 | Pipe fantasma após a última cela de cada linha de tabela | "Item  |  Detalhe  |" / "Protocolo  |  VAL-ORTO-2026-00471  |" | separador `  |  ` apenas ENTRE celas (reestrutura de `Flatten`/`CollectText`; afeta só o PDF) |

`PdfRendererVersion` avançou para **`odca-pdf-3`** seguindo a convenção D-A4 (bytes de saída mudaram ⇒ nova versão de renderer; artefatos antigos permanecem imutáveis e baixáveis). Pins atualizados: `StructuredContractDocumentTests` (aserto "SUMARIO"→"SUMÁRIO" + fidelidade da capa) e `scripts/qa/validate-e-web.ps1` (3 asserções).

### Verificação pós-fix (versão `4f11eea5`, `odca-pdf-3`, 4 páginas)

- Capa: "Termo de Fidelidade ODCA HOM PDF" (caso autoral) + "Versão 2 · Emitido em 13/10/2026" — iguais ao preview.
- "SUMÁRIO" com acento; URL dividida em blocos 93+15 chars, pixel mais à direita **x=986 < 1127** (OVERFLOW-MARGEM=NO, medido); tabela sem pipe final; callouts/bullets/quebras intactos.
- sha256 `b5a81761f2d3e7ae84de0425d8b6de6bcdb64386e0762ed8efc1c5191c360fbb` (4.274 bytes) == banco; `/Producer (ODCA Solutions (odca-pdf-3))` presente.

## Gates

- Build de `Odca.sln`: **0W/0E** · Domain **355/355 PASS** (353 anteriores + 2 novos: separador final de tabela e quebra de token longo) · Suíte de integração **54F/116P/170 — idêntica à linha de base ambiental** (mesma condição: `Defina ODCA_TEST_ADMIN_CONNECTION e ODCA_TEST_APP_CONNECTION`).

## Achados operacionais (não são defeitos)

- **Cota de storage na regeneração**: o movimento de consumo do PDF usa chave de idempotência `pdf:<versionId>` em `resource_movements`; resetar a linha da versão para `pending` sem dar crédito do consumo antigo faz a próxima geração falhar com **413** (cota) — o `ON CONFLICT DO NOTHING` deixa o CTE `movement` vazio e nada é reservado. O fluxo desenhado para reimpressão é o *replay* (status `completed` ⇒ mesmo artefato); regenerar sob novo renderer exige creditar o consumo antigo (remover o movimento + `used_bytes -= size`).
- **gs no Windows**: `-sOutputFile` só aceita caminho absoluto com barras diretas.

## Limitações homologadas (sem mudança nesta janela)

- Renderer de texto puro: alinhamentos de parágrafo (justificado/centro/direita) valem só no HTML/impressão — o PDF sai justificado-esquerda.
- Entradas do sumário no PDF sem numeração decimal (o preview exibe lista numerada).
- Caracteres fora do CP1252 viram `?` selecionável (decisão do renderer determinístico, já documentada).
- Tokens longos sofrem quebra dura sem hifenização (margem-seguro, hifenização seria motor extra).
- A versão `b384b859` permanece `completed` sob `odca-pdf-2` (artefato imutável do renderer anterior, ainda baixável); idem a v1 do fixture rico.

## Arquivos alterados

- `src/Odca.Application/Contracts/ContractDocumentRenderer.cs` — F1–F5 + bump do renderer
- `tests/Odca.Domain.Tests/StructuredContractDocumentTests.cs` — fidelidade da capa + 2 fatos novos
- `scripts/qa/validate-e-web.ps1` — pins de versão do renderer e sumário
- `docs/execution/STATUS.md` — seção do incremento
