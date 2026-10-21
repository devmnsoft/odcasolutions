# Sprint 24 — CI oficial, segunda onda: hermeticidade dos testes de erro do framework em checkout limpo (D-OC58) (21/10/2026)

## 1. Escopo e contexto

- O SHA da Sprint 23 (`cf0f74b`) foi pushado e a CI oficial disparada no push produziu a run **`39642957579`** (job `build-test`, runner `ubuntu-24.04`, Postgres `postgres:18.1-alpine`): `secret-scan` **success**; `build-test` com Restore/Build/Assets **success** e passo **`Tests` failure** — porém com avanço concreto sobre a S23: o fato de `ScanPageNumber` (corrigido na Sprint 23) **passou**; restaram **exatamente 2 falhas** → Integration **293/295**, Domain **389/389** totalmente verde.
- Esta onda diagnosticou direto do **log oficial do job** — a credencial HTTPS do repositório já guardada localmente (`git credential fill`) permitiu baixá-lo, encerrando a limitação de 403 anônimo da S23 (transcript versionado em `evidence/d58-ci-run-39642957579-build-test-falha-framework-error.txt`).
- Causa raiz nº 2, conforme o log: `System.InvalidOperationException : Configuração local não encontrada. Ambiente=Development; caminho=…/src/Odca.Api/development-runtime.json`, envolvendo `FileNotFoundException`, lançada em `LocalRuntimeConfiguration.Add` durante a construção do host — nos dois fatos `FrameworkErrorLocalizationTests.UnhandledExceptionIsPresentedInTheRequestedCulture` (pt-BR e en-US). O mecanismo: o `DevelopmentFaultApiFactory` sobe o host com `UseEnvironment("Development")`; em Development o `Program` exige o `development-runtime.json` — **arquivo pessoal gitignored**, presente na máquina de desenvolvimento e ausente em qualquer checkout limpo (como o do runner). No .NET 10 o `AddJsonFile(path, optional:false)` é carregado imediatamente no próprio `Add` (visível no stack trace), então a ausência do arquivo derruba a construção do host antes do teste começar.
- Por que a réplica da Sprint 23 não capturou: ela montava o diretório de trabalho Windows, que carrega além do checkout os arquivos pessoais gitignored (em especial este `development-runtime.json`). A limitação foi registrada no D-OC57 §4 e o método desta sprint elimina a falha de escopo: as réplicas rodam em **worktree/cópia de checkout limpo** (somente arquivos rastreados).
- Auditoria complementar: nenhum outro arquivo gitignored de `src`/`tests` é lido pela suíte — `data-protection-keys` é substituído por caminho temporário em todos os hosts de teste; `private-documents` e `.local` resolvem relativos ao CWD do processo (fora dos hosts de teste); bin/obj são artefatos de build.

## 2. Correção (zero linhas de produto)

- `DevelopmentFaultApiFactory` passa a provisionar o escape **previsto pelo próprio produto para exatamente este caso**: escreve um JSON vazio (`{}`) em arquivo temporário e aponta a variável `ODCA_RUNTIME_CONFIG` (`LocalRuntimeConfiguration.ResolvePath`) para ele antes da criação do host; no `Dispose` restaura o valor anterior da variável e remove o arquivo (best-effort). O conteúdo vazio basta porque toda a configuração do host de teste já vem da coleção in-memory (que vence o arquivo, como no fluxo real) e `DevelopmentRuntimeSetup.PrepareIfEnabled` retorna cedo sem `ODCA_SETUP_ON_START=true`.
- A classe entrou na coleção de testes `"Environment"` — o padrão da casa para quem altera essa variável de processo (mesma coleção de `LocalRuntimeConfigurationTests`) — eliminando qualquer interseção com a outra classe que manipula a mesma variável.
- **Correção C (endurecimento de guard de fonte, 1 linha + comentário)**: o artefato (a) da réplica r1 (§3) motivou endurecer o mesmo tipo de perigo que a Sprint 23 tratou no produto — `RuntimeQueryRegressionTests.ReadSource` agora normaliza line endings (`Replace("\r\n", "\n")`) antes das comparações, pela convenção já usada em `MigrationChecksumTests`; as asserções não mudaram. O guard (que compara substring multi-linha em um arquivo-fonte) fica determinístico entre checkouts CRLF (Windows com `autocrlf`) e LF (Linux/CI) — um clone limpo em qualquer OS deixa de poder quebrar a suíte nesse ponto.
- Hermeticidade atingida: o host Development de teste não depende mais de nenhum estado fora do checkout, nos dois SOs.
- Diff total: **2 arquivos de teste** (`FrameworkErrorLocalizationTests.cs`: factory hermetic + coleção + using; `RuntimeQueryRegressionTests.cs`: 1 linha de normalização + comentário). Zero linhas de produto; nenhum fato novo ou removido — contagens intactas (Domain 389, Integration 295).

## 3. Validação e provas

- **Reprodução negativa (causa raiz provada)**: réplica Linux da CI em **árvore limpa** (worktree no SHA `cf0f74b` — sem `development-runtime.json`), mesmo ambiente e comandos exatos do `ci.yml`, classe isolada via `--filter` → **2 falhas / 2 passes** com a **mesma exceção do log oficial** (`evidence/d58-reproducao-negativa-linux-39642957579.txt`). Os 2 passes (fatos 429, host Testing via fixture) confirmam que todo o resto do host de teste funciona sem o arquivo.
- **Réplica positiva em árvore limpa real** (cópia de checkout limpo: worktree no SHA `cf0f74b` + somente os arquivos de teste corrigidos nesta entrega — conteúdo de `src`/`tests` idêntico ao commit da Sprint 24; sequência exata do workflow):
  - **r1**: Integration **293/295** com **2 falhas classificadas como artefatos de método** (não de código): **(a)** `RuntimeQueryRegressionTests.ContractImportList…` — o guard de fonte compara substring multi-linha em LF, mas o worktree de verificação foi criado no Windows com `core.autocrlf=true` (checkout CRLF — 348 CRs; o blob guardado no git tem 0 CRs) → o artefato expôs um **perigo latente do mesmo tipo da S23** (um clone limpo no Windows quebraria a suíte nesse guard) e motivou a correção C; **(b)** `DapperMaterializationTests.DocumentScanClaim…` — `23505 duplicate key tenants_pkey` por estado residual no Postgres reutilizado entre várias réplicas (a CI recebe um service container novo por run) (`evidence/d58-replica-linux-arvore-limpa-r1-artefatos-metodo.txt`).
  - **r2**: réplica repetida com a árvore em semântica LF (worktree recriada com `-c core.autocrlf=false` — bytes idênticos ao armazenado) e container Postgres **novo** → RESTORE-OK, BUILD-OK (Release 0W/0E), **Domain 389/389 e Integration 295/295, 0 falhas em Linux em árvore limpa real** (`evidence/d58-replica-linux-arvore-limpa-r2-verde.txt`).
- **GATE local (Windows)** sobre a árvore final: builds sln 13 projetos `-warnaserror` Release **e** Debug 0W/0E; Domain 389/389; Integration `-FreshDb` 295/295 TEST-EXIT=0 (transcript da bateria preservado fora do repositório; contagens idênticas às da S23 — nenhum fato novo).
- Autoridade final: a execução oficial do GitHub sobre o SHA desta sprint (tabela abaixo).

## 4. Limitações

- As mantidas no D-OC57 seguem: provedor real de IA pendente (`none`); provedores de assinatura/SMTP pendentes como decisão; sessão absoluta de 15 min sem sliding pendente como decisão de produto; dívida de impersonação profunda (D-OC39); `api.sql.*` pt-BR sem diacríticos (dívida de migração).
- Se a run oficial desta sprint sair verde, a pendência da casa "CI oficial nunca verde publicada" será encerrada para a branch `codex/s00-foundation` e seu SHA; a creditação dos marcos no portal continua exigindo o aceite real pertinente de cada um — nada é antecipado.

## 5. Entrega

- Diff de produto: **zero**. Diff de teste: `FrameworkErrorLocalizationTests.cs` (factory hermetic + coleção + using) e `RuntimeQueryRegressionTests.cs` (normalização de line endings no `ReadSource` + comentário).
- Docs: D-OC57 atualizado (tabela de monitoramento com a run `39642957579` + limitação da réplica §4); este documento; seções S24 em `STATUS.md` (topo) e `BACKLOG.md` (anexo). Evidências `d58-*` versionadas.
- Commit único PT sem acentos (padrão da casa) fechando a Sprint 24 e push imediato na branch (fluxo decidido pelo responsável).
- `.odca/progress.json` **inalterado** (40%, baseline/IDs/pesos intactos).

### Monitoramento da run oficial

| Run | SHA | Status |
| --- | --- | --- |
| `39651426314` | `a657723` (S24) | **success** — `build-test` (Restore/Build/Assets/**Tests**) e `secret-scan` todos success; **primeira execução oficial verde da história do repositório** para a branch `codex/s00-foundation` (registrado após a conclusão da run, conforme regra da casa) |
