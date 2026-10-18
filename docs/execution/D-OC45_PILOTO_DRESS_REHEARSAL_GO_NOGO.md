# Sprint 11 — dress rehearsal do piloto e veredito go/no-go (D-OC45) (18/10/2026)

Última sprint do plano explícito antes da estreia 11/01/2027. Entrega duas coisas: (F-1) o **ensaio consolidado vira dress rehearsal** — o orquestrador `run-pilot-rehearsal.ps1` passa a incluir os estágios de prova das Sprints 7–10 que faltavam (backup/restore real e transporte SMTP por protocolo real), de modo que **um único comando reproduz toda a cadeia de prova atual** sobre a candidata; e (F-2) este **go/no-go formal**, avaliado item a item contra as pré-condições do runbook (D-OC40 §7.1) e a matriz de prontidão (D-OC40 §6), sem inventar decisão, aprovação ou execução.

## 1. Escopo e base

- **Base**: `codex/s00-foundation` @ `42ca1b1` (Sprint 10 / D-OC44).
- **F-1 — dress rehearsal consolidado** (`scripts/qa/run-pilot-rehearsal.ps1`, único arquivo alterado nesta sprint): acrescentados dois estágios autocontidos ao fim da cadeia — `S10-BACKUP` (`run-backup-restore.ps1`) e `S10-SMTP` (`rehearse-smtp-transport.ps1`). A ordem final: `C-SLA → ACEITE → D34-JORNADA → D37-ASSINATURA → D38-ATENDIMENTO → D39-SEGURANCA → S10-BACKUP → S10-SMTP`. Regras herdadas preservadas: falha de um estágio não interrompe a rodada (fotografia completa para classificação); critério por estágio é o próprio exit code (matriz de aceite por linha de resumo); stack dev legada sobe antes do primeiro estágio que a exige e derruba após o último; prazo de 40 min por estágio. Sem `-Only`, a rodada é o dress rehearsal completo.
- **F-2 — veredito go/no-go** (este documento, §3–§4).
- **Sem nova migração** (canônico v061 intacto) e **sem mudança de produto** nesta sprint: o diff é somente o orquestrador QA + docs/evidências.

## 2. Dress rehearsal — rodada final

Executado sobre a árvore S11 (SHA do commit desta sprint, sem push). Transcritos por estágio em `$TEMP\opencode\evidence\d40-stage-*.log`; transcript consolidado versionado em `evidence/d45-dress-rehearsal-rodada-final.txt`.

| Estágio | Prova | Veredito | Saída (linha-chave) |
| --- | --- | --- | --- |
| C-SLA | `validate-c-web.ps1` (solicitações, SLA, aprovações) | PASS | exit=0 · `PASS=98 FAIL=0` |
| ACEITE | `validate-acceptance-matrix.ps1` (matriz comportamental + MFA determinístico) | PASS | exit=0 · `PASS: 15 \| FAIL: 0 \| BLOCKED: 0` |
| D34-JORNADA | `validate-d34-an-web.ps1` (jornada documental A–N) | PASS | exit=0 · `RESULTADO: TODOS OS CHECKS PASSARAM` |
| D37-ASSINATURA | `validate-d37-signature-web.ps1` (envelope local-file) | PASS | exit=0 · `PROVA-FIM fails=0` |
| D38-ATENDIMENTO | `validate-d38-s4-web.ps1` (avisos/prazos/ações) | PASS | exit=0 · `PROVA-FIM fails=0` |
| D39-SEGURANCA | `validate-d39-s5-web.ps1` (reautenticação MFA + suporte) | **FAIL → PASS (reexecução)** | Rodada completa: exit=1 por asserção QA obsoleta (abaixo); após correção: `-Only D39-SEGURANCA` exit=0 · `fails=0` — `evidence/d45-dress-rehearsal-d39-rerun.txt` |
| S10-BACKUP | `run-backup-restore.ps1` (paridade fonte×restaurado) | PASS | exit=0 · `BACKUP-RESTORE-DONE checks=26 ok=26 fail=0` |
| S10-SMTP | `rehearse-smtp-transport.ps1` (worker real + MTA local) | PASS | exit=0 · `SMTP-REHEARSAL-DONE checks=17 ok=17 fail=0` |

**Classificação da única falha (não é do produto)**: o estágio D39 rodou com o binário idêntico que está verde na suíte (234/234), nos builds estritos e nos demais estágios ao vivo da própria rodada; a falha foi a asserção QA obsoleta `P0a schema_migrations em v060` no `validate-d39-s5-web.ps1` (escrita na época do canônico v060; a base descartável segue o canônico atual, v061 desde a S9). O checkpoint do script após W1b (`exit 1` se houver qualquer falha anterior) abortou a prova antes dos blocos de suporte/reautenticação — comportamento de segurança do próprio script, que registrou a falha e parou. Correção: asserção atualizada para v061 e reexecução isolada do estágio verde (`evidence/d45-dress-rehearsal-d39-falha-classificada.txt` preserva a saída da rodada com a falha). É exatamente o tipo de pendência de prova que o dress rehearsal existe para expor antes da estreia.

Resumo da rodada completa: `REHEARSAL-FIM stage_fails=1` (13:55:54→14:13:44, ~18 min em máquina compartilhada); reexecução isolada do D39 pós-correção: `stage_fails=0` (105 s). Com todos os estágios verdes, a candidata está coberta de ponta a ponta por um único comando reproduzível. Nota de transparência: os estágios S10-BACKUP/S10-SMTP reexecutaram os próprios scripts da S10 e atualizaram seus transcripts versionados (`evidence/d44-backup-restore-rodada-final.txt`, `evidence/d44-smtp-transport-rodada-final.txt`) com esta rodada — resultados idênticos em contagem (26/26 e 17/17, só timestamps/GUIDs sintéticos novos).

## 3. Go/no-go contra as pré-condições do runbook (D-OC40 §7.1)

| # | Pré-condição | Estado | Evidência / impedimento |
| --- | --- | --- | --- |
| 1 | Push da branch e **CI oficial verde** no SHA do piloto (job `build-test` + passo `Tests`) | **NÃO ATENDIDA** | Bloqueio externo registrado desde S00/S6: branch sem push autorizado; a CI oficial nunca foi verde publicada para nenhum SHA (S1–S10); nenhum ID de execução inventado |
| 2 | **Backup/restauração real ensaiado** e registrado | **ATENDIDA (local)** | `evidence/d44-backup-restore-rodada-final.txt` (26/26, EXIT=0; paridade fonte×restaurado) + estágio `S10-BACKUP` verde no dress rehearsal §2 |
| 3 | Decisão explícita do transporte de assinatura: provedor externo contratado (com homologação real registrada) **ou** transporte de referência local-file declarado aos usuários | **DECISÃO PENDENTE** | A mecânica está comprovada nos dois modos: local-file ponta a ponta com eventos/negativas/documento final (D-OC37, fails=0); fornecedor externo permanece "proposto, não escolhido". Se o piloto seguir com local-file, isso deve ser declarado aos convidados (não é integração com provedor real) |
| 4 | Decisão do canal de aviso: SMTP real configurado **ou** somente sino in-app | **DECISÃO PENDENTE** | Os dois mecanismos estão prontos e demonstrados: sino in-app (padrão, exercitado na suíte e nas provas) e SMTP real de ponta a ponta com falha controlada (D-OC44, 17/17) + estágio `S10-SMTP` no §2. Contratar provedor SMTP é o próximo passo se a decisão for e-mail real |
| 5 | Ensaio (§2) e auditoria de design verdes na árvore final | **ATENDIDO (local)** | §2 (dress rehearsal completo) + `evidence/d41-design-audit-s9-rodada-final.txt` (`AUDIT-DONE checks=204 ok=204 overflow=0 authfail=0 httpfail=0`); nenhuma superfície auditada (`src/Odca.Web`, catálogos, assets) mudou desde a S9 |

Itens adicionais exigidos para o convite (fora do §7.1, incluídos por honestidade):

| Item | Estado | Observação |
| --- | --- | --- |
| Usuários convidados definidos pelo responsável | **PENDENTE** | Sem eles o marco `pilot` não fecha (D-OC40 §6) |
| Aprovação explícita da versão pelo responsável | **PENDENTE** | Critério de saída do piloto (§7.6); não registrada |
| Superadministradores do piloto inscritos em MFA antes do dia 1 | **PRONTO PARA EXECUTAR** | Fluxo inscrito e deterministicamente verificável na matriz ACEITE; executar na preparação do ambiente |
| Decisão de produto: duração absoluta de sessão de 15 min (sem sliding) | **PENDENTE (não bloqueante)** | Risco registrado em D-OC40 §9; reautenticação existe; confirmar manter estrieta ou alongar antes do convite |

## 4. Veredito

**NO-GO formal — capacidade técnica de GO.** Não há como declarar GO: a pré-condição 1 (CI oficial verde do SHA do piloto) e as decisões 3 e 4 estão pendentes, e usuários convidados + aprovação da versão também. Nenhuma dessas pendências foi resolvida nem simulada nesta sprint. O que está feito e comprovado: **tudo que depende do repositório** — a candidata compila estrita (0W/0E), o comportamento unitário/integrado está verde (Domain 389/389; suíte `-FreshDb` 234/234), a prova ao vivo completa cabe num comando único (§2) e o rollback pré-piloto tem procedimento ensaiado (pré-condição 2).

Para virar **GO**, fecham-se, nesta ordem:

1. **Push + CI oficial verde** no SHA do piloto (desbloqueia a pré-condição 1 e a creditação futura dos marcos no portal).
2. **Decisão do transporte de assinatura** (provedor externo + homologação registrada, ou local-file declarado).
3. **Decisão do canal de aviso** (SMTP real + contrato/homologação do provedor, ou sino in-app).
4. **Definição dos usuários convidados** + cenários atribuídos (D-OC40 §7.3) e **confirmação da política de sessão (15 min)**.
5. Preparação do ambiente de piloto: provisioners sintéticos + MFA dos superadministradores + **backup pré-piloto executado e restore validado** no dia 1 (script versionado do F-1 da S10).
6. Reexecução do dress rehearsal sobre a árvore final pós-decisões e **aprovação explícita da versão** pelo responsável → GO.

## 5. Sequência até 11/01/2027 (85 dias a partir de 18/10/2026)

1. Push quando solicitado → CI oficial do SHA candidata (item 1 acima).
2. Decisões de provedores/canais/sessão (itens 2–4) — são decisões externas/jurídicas/produto, todas já registradas como bloqueio nas sprints anteriores; esta sprint não antecipa nenhuma.
3. Convite e agendamento do piloto com janela suficiente antes da estreia para falhas classificadas e tratamento (transcript por usuário, D-OC40 §7.6).
4. Dia 1: contas provisionadas, MFA ativo, backup pré-piloto + restore verificado, runbook §7 à mão.
5. Após o piloto: creditar em `.odca/progress.json` os marcos que tiverem `completed_commit` + `ci_run_id` + aceite aprovado + evidências rastreáveis (baseline 40% / IDs / pesos intactos).

## 6. Riscos e dívidas mantidos (nenhum resolvido nesta sprint)

- **CI oficial nunca verde publicada** — o bloqueio dominante para creditação de marcos.
- **Fornecedores externos**: assinatura ("proposto, não escolhido") e SMTP real (mecanismo comprovado; contratação ainda decisão).
- **Deploy físico multi-instância** e **backup offsite de produção** — infra externa; o ensaio cobre a instância local do piloto.
- **Janela de retenção legal de backups** "a definir" (`docs/privacy/RETENTION_POLICY.md`); cronograma diário/30 dias proposto em D-OC44 aguardando aprovação.
- **i18n das áreas operacionais pt-first** (documentos/estúdio/atendimento/obrigações/suporte) — dívida registrada do D-OC42/D-OC43; catálogos em paridade 4/4 mantida pelo gate de assets.
- **Sessão absoluta de 15 min sem sliding** — decisão de produto pendente (D-OC40 §9).

## 7. GATE e separação de evidências

- **GATE sobre a árvore final** (SHA desta sprint, executado localmente):

| Etapa | Comando | Resultado |
| --- | --- | --- |
| Build Debug estrito | `dotnet build Odca.sln -c Debug -warnaserror` | **0W/0E** |
| Build Release estrito | `dotnet build Odca.sln -c Release -warnaserror` | **0W/0E** |
| Domain | `dotnet test tests/Odca.Domain.Tests` | **389/389** |
| Integração em DB nova | `scripts/qa/run-integration-tests.ps1 -FreshDb` | **234/234, TEST-EXIT=0** |
| Assets/i18n | `node scripts/check-assets.mjs` | **exit=0** — 10 assets, 19 chaves em 4 catálogos, round trips íntegros |
| Dress rehearsal | `scripts/qa/run-pilot-rehearsal.ps1` (+ reexecução `-Only D39-SEGURANCA`) | 8/8 estágios verdes após a única correção QA classificada (§2) |
| Auditoria de design d41 | — | **não reexecutada de propósito**: o diff desta sprint não toca `src/Odca.Web`, catálogos i18n nem assets; o resultado da S9 permanece válido (`AUDIT-DONE checks=204 ok=204 overflow=0 authfail=0 httpfail=0`) |

- **Teste local**: GATE acima + dress rehearsal §2 + transcripts versionados (`evidence/d45-dress-rehearsal-rodada-final.txt`; logs desta rodada em `$TEMP` — `s11-rehearsal.log`).
- **CI oficial**: **não verificada** — branch sem push nesta rodada; nenhum ID de execução inventado.
- **Aceite humano**: **pendente** — go/no-go deste documento é avaliação técnica consolidada; a decisão final (convite + aprovação da versão) é do responsável.
- **Portal / `.odca/progress.json`**: **inalterado** — baseline 40%; sem CI oficial verde do SHA e sem aceite real, nenhum marco sai de `pending`; IDs e pesos intactos.
