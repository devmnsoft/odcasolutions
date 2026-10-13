# D-OC22 — Bloco F: validação de formatos (data/moeda) do Estúdio

Data: 2026-10-13 · Branch: codex/s00-foundation · Base: `0dbb640`

## Contexto

Mapeamento do estado atual antes do incremento:

- **O domínio já valida canonicamente**: `CanonicalDecimal` (parser pt-BR/invariante com erros explícitos; `IsStoredCurrency` não-negativa até 2 casas; `IsStoredNumber` assinada até 6 casas), `StructuredContractDocument.ValidateValues` (data ISO `yyyy-MM-dd` com validade calendária via `DateOnly.TryParseExact`, `BrazilianDocument`, `Choice`, consistência de fórmula B.3.4, limites de texto) e `NormalizeStoredValues` (normaliza Currency/Number/Formula do texto digitado para canônico) chamados na criação e no save de minuta e no gerar de versão. A API já responde `ValidationProblem` (400 com `errors` por campo) nos pontos de entrada.
- **O client espelha o server**: `parseDecimal` (studio-decimal.js) mirroriza as regras com mensagens localizadas (D-OC21); datas usam `<input type="date">` + regex ISO + `Date.parse` (validade calendária) antes do save.

Gaps encontrados:

1. **Sem matriz de testes** cobrindo os ramos de data e de número (só moeda tinha cobertura) e casos-limite de normalização.
2. **Timestamps de metadados de UI ignoravam a língua ativa**: views do Estúdio hardcoded `dd/MM/yyyy HH:mm` (e um `ToLocalTime()` no Edit que divergia das demais views em máquina fora de UTC) e `studio.js` usava `toLocaleString()` de locale do navegador para savedAt/histórico/comentários. Após o i18n, UI em en-US exibia datas em ordem dia/mês.
3. **Mensagem por campo morria no envelope Web**: o Save da minuta devolvia `{title:null,detail:null}` (a API responde `ValidationProblemDetails` só com `errors`), então o Estúdio mostrava falha genérica em vez de "O valor de 'Início' é inválido."
4. **500 não mapeado**: `values` que não seja array JSON válido (`JsonException` na deserialização de `NormalizeStoredValues`) escapava como exceção não tratada (500) no save de minuta.

## Decisão

- **Formatos canônicos inalterados** (dados não mudam com a língua — D-OC5): data armazenada `yyyy-MM-dd` com dia real de calendário; moeda `^(?:0|[1-9]\d*)(?:\.\d{1,2})?$` não-negativa; número assinado até 6 casas. Texto de entrada em estilo pt-BR ou invariante é normalizado; **data é só validada, não normalizada** (fixado por teste).
- **Camada de documento continua canônica pt-BR**: papel/PDF/`RenderedHtml` e `formatCurrency` do editor não mudam — preview == saída independentemente da língua da interface. Só **metadados de UI** seguem a cultura.
- **Novo helper `Odca.Web.Common.CulturedTime`**: `ShortDatePattern` da cultura ativa + relógio 24h (` HH:mm` / ` HH:mm:ss` / só data); cultura nula ou desconhecida cai em pt-BR (mesma semântica de fallback do `LocalizationService`). Aplicado nas 4 views com datas e no chrome do `studio.js` (lê `window.OdcaI18n.culture` do bag; fallback pt-BR onde o bag não existe).
- **Alinear `Edit.cshtml`**: removido `UpdatedAt.ToLocalTime()` — as demais views renderizam o UTC armazenado; a conversão no servidor fazia a tela de edição divergir das demais por ΔTZ em máquina não-UTC.
- **Propagar erro por campo no save**: `StudioController.Save` junta os valores de `ValidationErrors` em `detail` quando a API só traz `errors` (mesmo padrão que o `Generate` já usa para `pendingFields`) — o `studio.js` exibe a mensagem real sem mudança de contrato.
- **API: `JsonException` vira 400** no save de minuta ("O documento enviado está em formato inválido.") em vez de 500 não mapeado.
- Mensagens da API seguem em PT (restrição declarada no D-OC21; códigos de erro estáveis é trabalho futuro).

## Mudanças

| Arquivo | Alteração |
|---------|-----------|
| `tests/Odca.Domain.Tests/DateCurrencyValidationMatrixTests.cs` | **novo**: 51 fatos — matriz de data (aceitos: bissextos 2024/2000, extremos 9999-12-31; rejeitados: 2026-02-31, 1900-02-29, formatos não-ISO, anos fora de faixa), data não-normalizada, número armazenado (±, 6 casas; rejeita 7 casas, leading zero, vírgula, `+5`), normalização de número/moeda (estilos pt-BR e invariante, `R$` sem espaço, NBSP, `+`, precisão), campo opcional com valor malformado falha em Structural, mensagem do save contém label do campo + "ambíguo", formas canônicas idênticas entre estilos |
| `src/Odca.Web/Common/CulturedTime.cs` | **novo**: helper de timestamp por cultura (overloads `DateTimeOffset`/`DateTime`; `DateTime` sem offset tratado como UTC — contrato dos registros da API) |
| `src/Odca.Web/Views/Studio/Index.cshtml` | `publishedAt` por cultura (mantém sufixo "UTC") |
| `src/Odca.Web/Views/Studio/Edit.cshtml` | `data-saved-at` por cultura; remoção do `ToLocalTime()` (alinhamento) |
| `src/Odca.Web/Views/Studio/Version.cshtml` | `createdAt`/`pdfCompletedAt`/linhas do histórico de preparação por cultura; byte-size com `InvariantCulture` |
| `src/Odca.Web/Views/Studio/CompareVersions.cshtml` | 4 timestamps (2 selects + resumo) por cultura |
| `src/Odca.Web/wwwroot/js/studio.js` | constante `uiCulture` do bag; `toLocaleString(uiCulture)` em savedAt/histórico de versões/comentários |
| `src/Odca.Web/Controllers/StudioController.cs` | `Save`: propaga `ValidationErrors` para `detail` quando `title`/`detail` chegam vazios |
| `src/Odca.Api/Controllers/ContractStudioController.cs` | `Save` (PUT drafts): `catch(JsonException)` → 400 `ValidationProblem` |

## Evidência comportamental (aplicação real, API `:7143` + Web `:7144`, banco `odca_test_disposable`)

Build da solution **0 avisos / 0 erros**. Domain **353/353 PASS** (antes 302; +51 novos). Paridade de catálogos **4/4 PASS**. Suíte de integração em `odca_test_local`: **54 falhas / 116 aprovados / 170 — idêntica à linha de base ambiental** (zero nova regressão).

Script `%TEMP%\sv-format-beh.ps1` contra a aplicação real (login admin + MFA REV21A; instalação do catálogo oficial pelo endpoint real `/estudio/biblioteca-oficial`; minuta nova criada pelo formulário real do catálogo a partir de "Contrato de Fornecimento" — campos `start_date` (Date) e `unit_price` (Currency)):

| Caso | Operação real | Resultado |
|------|---------------|-----------|
| A | PUT da minuta com `start_date="2026-02-31"` (dia inexistente) | 422 · `detail`="O valor de 'Início' é inválido." (mensagem por campo agora chega ao envelope Web) |
| B | PUT com `unit_price="1.250"` (ambíguo milhar/decimal) | 422 · `detail`="O valor de 'Preço unitário' é inválido. O valor é ambíguo: informe as casas decimais…" |
| C | PUT com `unit_price="1.250,50"` + `start_date="2026-12-31"` | 200 → GET da minuta persistiu `"value":"1250.50"` e `"value":"2026-12-31"` (normalização server-side fim-a-fim) |

Chrome de timestamps nas 4 línguas (mesma sessão, cookie `odca_lang`): savedAt do edit, publicado no índice e gerado na versão renderizam em ordem dia/mês para pt-BR/fr-FR/es-ES (`13/10/2026 09:47`, fr `dd/MM`, es `d/M` sem padding — padrão da cultura) e **invertida para en-US** (`10/13/2026`) — troca dia/mes verificada numericamente (padrões `M/d` e `d/M` não fazem padding). Na página da versão em en-US, o papel do documento **manteve a moeda canônica pt-BR** (`1.234,56` para valor armazenado `1234.56`; forma en-US `1,234.56` ausente). Evidências HTML/bodies em `%TEMP%\sv-format-evidence\` (edit/index/version × 4 culturas + bodies dos PUTs A/B/C + edit pós-save).

## Limitações

- API continua respondendo erros em PT; a propagação do item 3 entrega a mensagem existente, não sua localização (códigos de erro estáveis + catálogos da API = incremento futuro, junto com áreas fora do Estúdio).
- Fora do Escopo Estúdio: `Contracts/Sheet.cshtml` ainda usa `toLocaleString('pt-BR')` hardcoded e `PlatformLibrary/NewRevision.cshtml` segue 100% PT (sem bag) — estado prévio, registrado.
- Responsivo 360/768/1440px e homologação visual de PDF permanecem itens próprios do Bloco F.
- Sem push/merge/deploy automático.
