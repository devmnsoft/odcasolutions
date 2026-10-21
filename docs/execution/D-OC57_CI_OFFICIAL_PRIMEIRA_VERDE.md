# Sprint 23 — CI oficial do GitHub no runner Linux: correção da falha sistemática e primeira tentativa de verde (D-OC57) (21/10/2026)

## 1. Escopo e contexto

- A candidata (`codex/s00-foundation` @ `31b70db`, Sprint 22 / D-OC56) foi **empurrada pelo responsável** nesta sprint (decisão do dia 21/10/2026): remote `devmnsoft/odcasolutions`, avanço `27b19b6..31b70db`. Era a pendência nº 1 para virar GO (D-OC56 §4 item 1) e para creditação futura dos marcos no portal.
- CI oficial disparada automaticamente no push: **run `39635407117`** (job `build-test`, runner `ubuntu-24.04`, Postgres service `postgres:18.1-alpine`). Resultado: `secret-scan` **success**; `build-test` com `Restore`/`Build`/`Assets` **success** e o passo **`Tests` em failure** (`dotnet test Odca.sln -c Release --no-build`).
- Histórico consultado pela API pública: os dois pushes anteriores da branch também falharam no mesmo passo — runs `39024561909` (`27b19b6`) e `38911855089` (`a2b1360`) — ou seja, **falha sistemática pré-existente**, não causada pela Sprint 22. Consistente com o registro da casa ("CI oficial nunca verde publicada").
- Objetivo desta sprint: identificar a causa raiz, corrigir sem regressão, validar num ambiente fiel ao runner Linux e re-empurrar para obter a primeira execução oficial verde (job `build-test` + passo `Tests`).

## 2. Investigação

- Sem token autenticado na máquina local, o log do passo não é acessível anônimo (HTTP 403 na API de logs); a investigação partiu por varredura sistemática da suíte e dos projetos por APIs sensíveis à plataforma (`Path.*` sobre literais com backslash, `Environment.OSVersion`, `IsWindows`, comparações de string dependentes de culture, caminhos de disco).
- O único candidato com comportamento **deterministicamente divergente entre SOs** foi encontrado em:
  - `tests/Odca.IntegrationTests/FranchiseMeteringTests.cs` → fato `ScannedPageFileNamesParseToOrderedPageNumbers`, que exercita `DocumentWorker.ScanPageNumber(@"C:\tmp\odca-scan-x\page-1.png")` esperando `1`;
  - `src/Odca.Worker/DocumentWorker.cs` → `ScanPageNumber` usava `Path.GetFileNameWithoutExtension(fileName)`.
- No Windows `Path.GetFileNameWithoutExtension("C:\tmp\odca-scan-x\page-1.png")` = `"page-1"` (o `\` é separador). **No Linux o `\` não é separador**: a função devolve a string inteira (`"C:\tmp\odca-scan-x\page-1"`), que não começa por `page-` → retorna `-1` → o fato falha. Todos os demais itens da varredura já eram portáveis (`Path.Combine` com segmentos relativos, literais de mensagem hardcoded nos dois lados do assert, formatações com culture explícita/invariante).
- **Causa raiz provada por reprodução negativa**: num worktree no SHA `31b70db` (árvore S22, sem a correção), a mesma réplica Linux executou somente esse fato → **falhou exatamente como esperado** — `Assert.Equal() Failure: Values differ / Expected: 1 / Actual: -1` em `FranchiseMeteringTests.ScannedPageFileNamesParseToOrderedPageNumbers` — transcript versionado em `evidence/d57-ci-linux-prefix-sem-fix-falha.txt`. A suíte completa na mesma réplica **com** a correção saiu 0 falhas (§4): a falha da CI oficial é este fato, isolado e determinístico.
- Nota de produto: além de quebrar a CI, o mesmo risco valia para o worker em produção Linux interpretando os nomes de página renderizadas (`page-N.png`) gerados sob qualquer convenção de caminho — a correção torna a função portável nos dois lados.

## 3. Correção

`DocumentWorker.ScanPageNumber` passou a resolver o último segmento manualmente (independente de SO): substitui `\` por `/`, toma o trecho após o último `/` e remove a extensão — mantendo idênticos os resultados para todos os casos já asseridos nos fatos existentes (nome simples, `page-12.png`, `page-.png` → -1, `page-a.png` → -1, `foo-page-3.png` → -1, `outro.png` → -1 e o caminho Windows completo → 1 agora em **qualquer** plataforma). Nenhuma outra linha do produto ou da suíte foi tocada.

## 4. Validação

- **Réplica Linux do runner via Docker** (mesmo ambiente da CI, mesma sequência exata): container `postgres:18.1-alpine` com as mesmas credenciais/banco do `ci.yml` + container `mcr.microsoft.com/dotnet/sdk:10.0` com o repositório montado e as mesmas variáveis `ODCA_TEST_*`; comandos exatos do workflow: `dotnet restore Odca.sln --locked-mode` → **RESTORE-OK**; `dotnet build Odca.sln -c Release --no-restore` → **BUILD-OK**; `dotnet test Odca.sln -c Release --no-build` → **Domain 389/389 (5 s) e Integration 295/295 (2 m 37 s), 0 falhas** — suíte completa verde em Linux (transcript versionado em `evidence/d57-ci-linux-replica-com-fix-verde.txt`).
- **Réplica negativa (mesmo ambiente, árvore sem a correção)**: worktree no SHA `31b70db`, mesmo container/PG/comandos, fato isolado via `--filter` → **1 falha / 0 passes** com `Expected: 1, Actual: -1` (`evidence/d57-ci-linux-prefix-sem-fix-falha.txt`) — confirma que era este fato o responsável pelo failure do passo `Tests`.
- **GATE local (Windows)** sobre a árvore corrigida: builds sln 13 projetos `-warnaserror` Release+Debug 0W/0E; Domain 389/389; Integration `-FreshDb` 295/295 TEST-EXIT=0 (transcript da bateria preservado fora do repositório; contagens idênticas às da S22 + nenhum teste novo nesta sprint).
- Limitação registrada com honestidade: a réplica reproduz o ambiente e a sequência do runner, mas o resultado que vale para creditação é o da execução oficial do GitHub sobre o novo SHA (monitorada após o push; ID da run registrado abaixo ao concluir).
- **Limitação da réplica verde descoberta nesta sequência (registrada em 21/10/2026)**: a réplica montava o diretório de trabalho Windows (`C:\MNSOFT\odcasolutions`), que carrega além do checkout os arquivos pessoais gitignored — em especial `src/Odca.Api/development-runtime.json`. Ela portanto prova a correção de plataforma (a varredura de `ScanPageNumber` era o alvo) mas **não** a limpeza do checkout; essa falha de escopo ficou visível na run oficial `39642957579` (§ monitoramento) e foi tratada na Sprint 24 / D-OC58, cujas réplicas usam worktree (somente arquivos rastreados = árvore limpa real).

## 5. Entrega

- Mudança de produto: **1 função** (`DocumentWorker.ScanPageNumber`, `Odca.Worker`) — portabilidade de SO sem mudança de comportamento nos casos já asseridos; nenhum teste novo (o fato existente era o guarda já escrito; o que mudou foi o código para que ele passe em qualquer plataforma).
- Docs: `D-OC57_CI_OFFICIAL_PRIMEIRA_VERDE.md` (este documento) + seções S23 em `STATUS.md` (topo) e `BACKLOG.md` (anexo); evidências versionadas em `evidence/d57-ci-linux-prefix-sem-fix-falha.txt` e `evidence/d57-ci-linux-replica-com-fix-verde.txt`.
- Commit único PT sem acentos (padrão da casa) fechando a Sprint 23 e push imediato na branch (fluxo decidido pelo responsável).
- `.odca/progress.json` **inalterado** (40%, baseline/IDs/pesos intactos): a CI oficial verde do SHA é condição necessária, mas a creditação dos marcos exige ainda o aceite real pertinente — nada é antecipado.

### Monitoramento da CI oficial pós-push

| Run | SHA | Status |
| --- | --- | --- |
| `39635407117` | `31b70db` (S22) | failure no passo `Tests` (falha que motivou a Sprint 23 — causa raiz §2) |
| `39642957579` | `cf0f74b` (S23) | failure no passo `Tests` — **avanço real**: o fato de `ScanPageNumber` passou; restaram 2 falhas (Integration 293/295, Domain 389/389 verde): `FrameworkErrorLocalizationTests.UnhandledExceptionIsPresentedInTheRequestedCulture` pt-BR/en-US por falta do `development-runtime.json` no checkout limpo (arquivo pessoal gitignored, exigido pelo ambiente Development) — log oficial versionado em `evidence/d58-ci-run-39642957579-build-test-falha-framework-error.txt`; causa tratada na Sprint 24 / D-OC58 |
| _(run sobre o SHA da S24 — registro no D-OC58)_ | | |
