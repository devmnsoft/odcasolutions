# Sprint 22 — dress rehearsal final da candidata ao piloto sobre a árvore atual + veredito go/no-go atualizado + pacote de preparação do dia 1 (D-OC56) (21/10/2026)

O último dress rehearsal consolidado foi executado na Sprint 11 (D-OC45) sobre a árvore `42ca1b1` (Sprint 10 / D-OC44). Desde então as Sprints 12–21 mudaram superfícies extensas: i18n das centrais de documentos (D-OC47), organização (D-OC48), administração de plataforma (D-OC49), clientes/consumo/planos/caixa (D-OC50), conta/MFA/navegação/importações (D-OC51), módulo de IA com nova migração v063 e planos publicados 7/7/8 (D-OC52/D-OC53), overhaul de CSS/UX (D-OC54) e manuais contextuais + reescrita de linguagem das mensagens (D-OC55). A candidata ao piloto nunca foi re-ensaiada sobre a própria árvore. Esta sprint faz exatamente o que o veredito da S11 determinou como último passo técnico: **reexecuta a cadeia completa de provas sobre a árvore atual**, classifica cada falha (deriva do script QA vs regressão real de produto), **atualiza o veredito go/no-go com evidência nova** e **monta o pacote versionado de preparação do dia 1** para que o piloto seja execução de checklist, não improviso.

## 1. Escopo e base

- **Base**: `codex/s00-foundation` @ `abf2da7` (Sprint 21 / D-OC55). Sem nova migração (canônico v063 intacto). Mudança de produto somente se a rodada r1 classificar uma regressão real.
- **F-1 — dress rehearsal completo sobre a árvore S21**: reexecução de `scripts/qa/run-pilot-rehearsal.ps1` (8 estágios: C-SLA, ACEITE, D34-JORNADA, D37-ASSINATURA, D38-ATENDIMENTO, D39-SEGURANCA, S10-BACKUP, S10-SMTP) sobre `odca_test_disposable` (canônico em v063, dados sintéticos de fixture versionado). Método do precedente D-OC45: fotografia completa primeiro (falha não interrompe), classificação por estágio, correção das causas raiz e reexecução isolada dos estágios corrigidos via `-Only` quando aplicável.
- **F-2 — veredito go/no-go atualizado**: nova tabela contra as pré-condições do runbook (D-OC40 §7.1) + os itens adicionais do convite, com a evidência da S21 e o delta explícito versus o veredito da S11 (item 4 do D-OC45). Nenhuma decisão externa é antecipada.
- **F-3 — pacote de preparação do dia 1**: checklist versionada nesta sprint (§5) apoiada nos artefatos já existentes (provisioners `scripts/provision-demo-access.ps1` e `scripts/provision-local-superadmin.ps1`, `scripts/qa/run-backup-restore.ps1`, runbook D-OC40 §7) + adendo datado corrigindo referências defasadas dentro do próprio D-OC40 (§7.5/§8: "v060 é a mais recente", contagens de suíte/chaves da época da S6) sem reescrever o histórico.
- **GATE**: builds estritos + Domain + suíte `-FreshDb` + assets + scanner sobre a árvore final, e o dress rehearsal final verde (com reexecuções classificadas quando houver correções).

### Deriva pré-identificada por inspeção (antes da r1)

- `scripts/qa/validate-d39-s5-web.ps1` afirma `P0a schema_migrations em v061` (`$vMax -eq '61'`) — a mesma natureza da deriva que o próprio D-OC45 corrigiu de v060→v061. O canônico está em **v063** (v062 = idioma da outbox, D-OC46; v063 = análises de IA, D-OC53) e a base descartável já está em 63 → falha esperada no checkpoint P0a, a ser classificada e corrigida na árvore.
- Demais checks de versão dos estágios são dinâmicos (`>= 42`, `>= 62`, paridade por valor lido) e não derivam.
- Hipótese a confirmar/refutar na r1: scripts de prova escritos antes de S12–S21 podem asserir texto de UI reescrito pelas sprints de i18n e pela passada de linguagem da S21.

## 2. Dress rehearsal — rodadas

### Rodada r1 — fotografia completa sobre a árvore S21 (`abf2da7`) — 21/10/2026, 12:15→12:42

Comando: `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/qa/run-pilot-rehearsal.ps1` (8 estágios; base descartável `odca_test_disposable` canônica em v063 com dados sintéticos de fixture versionado). Final: `REHEARSAL-FIM stage_fails=2`. Nenhum evento de ambiente nesta rodada (a máquina não adormeceu; a árvore de processos sobreviveu à execução integral de ~27 min — diferente das ocorrências da S21).

| Estágio | Veredito r1 | Saída (transcript) |
| --- | --- | --- |
| C-SLA | **PASS** | `PASS=98 FAIL=0` em 39 s, com limpeza e seed intactos |
| ACEITE | **PASS** | `PASS: 15 \| FAIL: 0 \| BLOCKED: 0` em 31 s |
| D34-JORNADA | **FAIL** | 8 checks falharam / 53 verdes (X1-1, X1-2, X2-1, X2-2, X3, T1, N1, N2) |
| D37-ASSINATURA | **PASS** | `PROVA-FIM fails=0` em 76 s (E1–E8: envios, dedupe determinístico, 409 após `accepted`) |
| D38-ATENDIMENTO | **PASS** | `PROVA-FIM fails=0` em 55 s (O1–O8: 409 c/ valor preservado, trilha start/cancel) |
| D39-SEGURANCA | **FAIL** | 1 falha em P0a (`schema_migrations em v061 max=63`) + 9 verdes até W1b; abortou no checkpoint pós-W1b |
| S10-BACKUP | **PASS** | `BACKUP-RESTORE-DONE checks=26 ok=26 fail=0` (paridade fonte×restaurado) |
| S10-SMTP | **PASS** | `SMTP-REHEARSAL-DONE checks=17 ok=17 fail=0` (inclui `ECONNRESET` real tratado) |

Transcripts versionados: `evidence/d56-dress-rehearsal-r1.txt` (console do orquestrador), `evidence/d56-d34-jornada-r1.txt` (checks da jornada), `evidence/d56-d34-r1-worker-erro.txt` (log do worker com a exceção), `evidence/d56-d39-seguranca-r1.txt`.

### Falha da D34-JORNADA (análise com evidência)

Os 8 checks falhos estão todos nas cadeias de varredura/extração documental: os jobs enviados **nunca saem de `security_status='pending'`** (estados vazios/`n/a` nas asserções de escopo, total e status). O log do worker captura a causa:

```text
fail: Odca.Worker.DocumentWorker[0]
      Falha isolada no processamento documental; o ciclo continuará.
      System.InvalidOperationException: A parameterless default constructor or one matching
      signature (System.Guid id, System.Guid tenantid, System.String storagekey, System.String
      detectedtype) is required for Odca.Worker.DocumentWorker+VersionItem materialization
         at Odca.Worker.DocumentWorker.ScanOne(CancellationToken ct)
```

Causa raiz: a Sprint 19 (`1f94693`, D-OC53) estendeu o record `VersionItem` de 4 para 6 parâmetros (`DocumentId`/`UploadedBy`, contexto documental da extração/IA) sem atualizar a projeção de 4 colunas do `ScanOne` (`odca.claim_document_scan()`). O Dapper não localiza construtor compatível com os aliases e **toda varredura** lança em tempo de execução. O tratador de falha isolada mantém o ciclo vivo — por isso 53 checks continuam verdes e a auto-recuperação da stack funciona (T0/N3) — mas nenhum documento é processado. É a mesma classe de bug que a própria S19 corrigiu no `RenewalListItem` (pegue na auditoria ao vivo r6); o primo do worker passou despercebido porque o build estrito só prova compilação, a suíte não tem e2e de scan executado (limitação registrada na S19) e as auditorias d41/d54 não realizam varredura de documentos. **Regressão real de produto**, introduzida entre S11 (último ensaio verde) e S21 — exatamente o que este dress rehearsal existe para pegar.

### Falha da D39-SEGURANCA (análise com evidência)

A única falha é a deriva pré-identificada em §1: o script QA assercia `schema_migrations == '61'`; o canônico vigente é **v063**. O comportamento de produto foi exercitado até W1b e saiu verde — cadeia completa de MFA do superadministrador (A1 exige inscrição, A2 devolve chave manual de 32 chars, A3 token verificado de 877, W1a 302 para o desafio, W1b sessão estabelecida) — e a prova abortou no próprio checkpoint pós-W1b por causa da falha acumulada. É deriva do script de prova, não do produto.

### Correções aplicadas nesta sprint (após a foto r1, método D-OC45)

1. **Produto (D34)**: construtor secundário de 4 parâmetros em `DocumentWorker.VersionItem` ancorando exatamente os aliases da projeção do claim (o registro virou `internal` para visibilidade à suíte via `InternalsVisibleTo` já presente no csproj; a projeção da extração com 6 colunas segue no construtor primário). Blindado por dois testes, no padrão da S19: **(a)** comportamental — materializa o claim real `odca.claim_document_scan()` contra uma linha semeada pela role da aplicação, com o mesmo SQL e tipo de produção (`DapperMaterializationTests.DocumentScanClaimMaterializesVersionItemFromRealPostgreSql`); **(b)** guard de fonte fixando o pareamento alias↔construtor (`RuntimeQueryRegressionTests.DocumentWorkerScanClaimProjectionMatchesVersionItemSecondaryConstructor`).
2. **QA (D39)**: `validate-d39-s5-web.ps1` passa a asserir `v063` com comentário datado — mesma natureza da correção v060→v061 feita pelo próprio D-OC45.

## 3. Classificação das falhas

| # | Estágio / checks | Classificação | Fundamento | Correção |
| --- | --- | --- | --- | --- |
| 1 | D34-JORNADA (X1-1, X1-2, X2-1, X2-2, X3, T1, N1, N2 — 8) | **Regressão real de produto** (S19/D-OC53) | Exceção Dapper repetida no log do worker em toda varredura; reprodução determinística no claim; `git log -L` isola a mudança do record em `1f94693` | Construtor secundário + 2 testes (comportamental e de fonte) |
| 2 | D39-SEGURANCA (P0a — 1) | **Deriva QA — script de prova** | Script congelado em v061 na S11; canônico vigente v063; comportamento de produto envolvido saiu verde até o abort (A1–A3, W1a, W1b) | Asserção atualizada para v063 |

Hipótese aberta em §1 ("scripts de prova asserciondo texto de UI reescrito por S12–S21"): **não confirmada nas superfícies que rodaram** — C-SLA, ACEITE, D37 e D38 exercitaram extensivamente as áreas reescritas pelas sprints de i18n/UX/linguagem e passaram 100%; no D39 nenhuma falha de texto apareceu antes do abort por P0a. As falhas r1 se explicam integralmente pelos dois itens da tabela acima.

### Rodada r2 — reexecução isolada dos estágios corrigidos (árvore corrigida, binários estritos 0W/0E)

Comando: `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/qa/run-pilot-rehearsal.ps1 -Only 'D34-JORNADA;D39-SEGURANCA'` — 21/10/2026, 12:49→13:04. Final: `REHEARSAL-FIM stage_fails=0` (EXIT=0).

| Estágio | Veredito r2 | Saída (transcript) |
| --- | --- | --- |
| D34-JORNADA | **PASS** | 61 checks verdes / 0 falhas em 773 s — `RESULTADO: TODOS OS CHECKS PASSARAM`. Os 8 falhos da r1 saíram todos verdes com escopo honesto (PDF escaneado: 20 processadas de 25 com `truncado=true`; docx: chega a `ready_for_review` sem fabricar páginas; imagem única: 1 de 1; OCR pendurado: `extraction-timeout`; sem renderer: `pdf-no-text-layer`; sem OCR: `extractor-not-configured`) |
| D39-SEGURANCA | **PASS** | `fails=0` em 113 s — P0a agora em v063 + prova completa: A1–A3 (MFA), W1a/W1b (desafio web), W2/W2b (página e navegação de sessões de suporte em pt-BR), W3–W5 (ciclo abrir→estender→encerrar com eventos e ator real na auditoria), F1a–F1c (escrita stale → redirect ao desafio preservando `returnUrl` → sucesso pós-reautenticação), F2/F2b (API stale = 403 puro; leitura segue 200) |

Transcripts: `evidence/d56-dress-rehearsal-r2.txt` (console), `evidence/d56-d34-jornada-r2-verde.txt`, `evidence/d56-d39-seguranca-r2-verde.txt`.

**Veredito consolidado do dress rehearsal sobre a árvore final** (foto r1 + reexecução classificada r2, método D-OC45): **8/8 estágios verdes** — C-SLA 98/0 · ACEITE 15/0/0 · D34-JORNADA 61/0 · D37-ASSINATURA fails=0 · D38-ATENDIMENTO fails=0 · D39-SEGURANCA fails=0 · S10-BACKUP 26/26 · S10-SMTP 17/17. O transcript da r1 permanece intocado com as 2 falhas originais (`evidence/d56-dress-rehearsal-r1.txt`). Os estágios verdes na r1 não foram reexecutados: a correção toca apenas `Odca.Worker` (binário já usado pelos próprios estágios D37/D38 que não exercitam varredura documental), um script de QA e os testes — nenhum comportamento coberto por C-SLA/ACEITE/D37/D38/S10 mudou.

## 4. Veredito go/no-go atualizado (S21)

| # | Pré-condição (D-OC40 §7.1 + convite) | Estado | Evidência / impedimento |
| --- | --- | --- | --- |
| 1 | Push da branch e **CI oficial verde** no SHA do piloto (job `build-test` + passo `Tests`) | **NÃO ATENDIDA** | Bloqueio externo mantido desde S00/S6: branch `codex/s00-foundation` sem push autorizado; CI oficial nunca verde publicada para nenhum SHA; nenhum ID de execução inventado |
| 2 | **Backup/restauração real ensaiado** e registrado | **ATENDIDA (local)** | Reexecutado verde nesta árvore na r1: estágio `S10-BACKUP` 26/26 (`BACKUP-RESTORE-DONE checks=26 ok=26 fail=0`, paridade fonte×restaurado incl. `max_version`) + transcript re-versionado `evidence/d44-backup-restore-rodada-final.txt` |
| 3 | Decisão do transporte de assinatura (provedor externo c/ homologação registrada **ou** local-file declarado) | **DECISÃO PENDENTE** | Mecânica re-comprovada nesta árvore: `D37-ASSINATURA` fails=0 na r1 (envios, dedupe determinístico, 409 pós-`accepted`); fornecedor externo segue "proposto, não escolhido" |
| 4 | Decisão do canal de aviso (SMTP real **ou** sino in-app) | **DECISÃO PENDENTE** | Mecânica re-comprovada nesta árvore: `S10-SMTP` 17/17 na r1 (inclui `ECONNRESET` real tratado) + sino in-app padrão exercitado na suíte/provas |
| 5 | Ensaio e auditorias verdes na árvore final | **ATENDIDO (local)** | Dress rehearsal **8/8 estágios verdes** (§2, consolidado r1+r2); d41 (764 medidas) e d54 (382 medidas) da S21 valem nesta mesma árvore porque esta sprint não alterou superfície auditada (`src/Odca.Web`, catálogos, assets) — só `Odca.Worker`, um script QA e testes |

Itens adicionais exigidos para o convite (fora do §7.1, incluídos por honestidade):

| Item | Estado | Observação |
| --- | --- | --- |
| Usuários convidados definidos pelo responsável + cenários atribuídos | **PENDENTE** | Sem eles o marco `pilot` não fecha (D-OC40 §6); cenários prontos em §5.3 |
| Aprovação explícita da versão pelo responsável | **PENDENTE** | Critério de saída (§7.6); não registrada |
| Superadministradores do piloto inscritos em MFA antes do dia 1 | **PRONTO PARA EXECUTAR** | Fluxo re-comprovado na r2 (A1–A3, W1a/W1b, F1a–F1c, F2/F2b) e deterministicamente verificável na matriz ACEITE |
| Política de sessão absoluta de 15 min (sem sliding) | **PENDENTE (não bloqueante)** | Risco em D-OC40 §9; reautenticação existe e foi exercitada (F1/F2 na r2) |

### Veredito

**NO-GO formal — capacidade técnica de GO reforçada sobre a S11.** Não há como declarar GO: a pré-condição 1 (push + CI oficial) e as decisões 3–4, usuários convidados e aprovação da versão seguem pendentes — nenhuma delas foi resolvida nem simulada nesta sprint. O que muda em relação ao veredito da S11 (D-OC45): **(i)** a candidata passou pela primeira vez pelo ensaio completo sobre a própria árvore desde S11 — e o ensaio cumpriu o papel que existe para isso, capturando uma **regressão real de produto** introduzida por S12–S21 (varredura documental quebrada para todo usuário; corrigida e blindada por 2 testes); **(ii)** toda a cadeia de sessão de suporte + stale/MFA do D39 foi validada pela primeira vez nesta árvore (na r1 abortava antes, por deriva de script); **(iii)** o pacote executável do dia 1 deixou de ser memória e virou checklist versionada (§5), ancorada em provisioners/scripts desta árvore. Tudo o que depende do repositório está comprovado localmente; o NO-GO restante é 100% decisão externa.

Para virar **GO**, fecham-se, nesta ordem (mesma ordem do D-OC45, agora com o ensaio final já cumprido):

1. **Push + CI oficial verde** no SHA do piloto (desbloqueia a pré-condição 1 e a creditação futura dos marcos no portal).
2. **Decisão do transporte de assinatura** (provedor externo + homologação registrada, ou local-file declarado aos convidados).
3. **Decisão do canal de aviso** (SMTP real + contrato/homologação, ou sino in-app).
4. **Definição dos usuários convidados** + cenários atribuídos (§5.3) e **confirmação da política de sessão (15 min)**.
5. Preparação do ambiente de piloto segundo §5.2 (provisioners sintéticos + MFA dos superadministradores + **backup pré-piloto com restore validado no dia 1**).
6. Se a árvore mudar entre as decisões e o dia 1: reexecução do dress rehearsal no SHA final (§5.2 item 3) + **aprovação explícita da versão** → GO.

## 5. Pacote de preparação do dia 1 (checklist versionada)

Toda referência executa sobre artefatos versionados deste repositório; nada de dia 1 depende de memória ou improviso. Registro dos participantes (papel, conta, cenários atribuídos) permanece **fora** do repositório (minimização — D-OC40 §7.2).

### 5.1 Antes do convite (decisões + pré-condições)

| # | Item | Artefato / comando | Estado desta sprint |
| --- | --- | --- | --- |
| 1 | Push da candidata + CI oficial verde no SHA do piloto (job `build-test` + passo `Tests`) | decisão externa do responsável | PENDENTE |
| 2 | Decisão do transporte de assinatura (provedor externo c/ homologação registrada **ou** local-file declarado aos convidados) | decisão externa (D-OC45 item 3; mecânica dos dois modos comprovada no D-OC37) | PENDENTE |
| 3 | Decisão do canal de aviso (SMTP real contratado **ou** sino in-app) | decisão externa (D-OC45 item 4; mecanismo comprovado no D-OC44/S10-SMTP) | PENDENTE |
| 4 | Definição dos usuários convidados + cenários atribuídos (por perfil, §5.3) e confirmação da política de sessão de 15 min | decisão do responsável + produto | PENDENTE |
| 5 | Dress rehearsal completo verde na árvore final pós-decisões | `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/qa/run-pilot-rehearsal.ps1` (comando único, 8 estágios) | ATENDIDO nesta sprint sobre a árvore atual (§2); reexecutar no SHA final (§5.2.3) |
| 6 | Aprovação explícita da versão pelo responsável | registro fora do repositório (critério D-OC40 §7.6) | PENDENTE |

### 5.2 Dia 1 (ordem exata)

| # | Item | Artefato / comando |
| --- | --- | --- |
| 1 | Verificar stack operacional: API/Web/Worker com `/health/ready` = 200 na instância do piloto | probe `curl -k https://localhost:7143/health/ready` (dev legada) ou a porta correspondente da instância |
| 2 | **Backup pré-piloto executado e restore validado** (pré-condição 2 do D-OC40 §7.1; sem ele o rollback do §7.5 não é confiável) | `scripts/qa/run-backup-restore.ps1` → esperado `BACKUP-RESTORE-DONE checks=26 ok=26 fail=0` (paridade fonte×restaurado, incluindo `max_version` do schema) |
| 3 | Reexecução pontual do dress rehearsal já coberta em 5.1.5 sobre o SHA do dia — se a árvore mudar entre a decisão e o dia 1, refazer o item 5.1.5 antes do convite | idem 5.1.5 |
| 4 | Contas dos convidados provisionadas pelos provisioners versionados (perfis `admin`/`client`, senha resetável; superadministrador local) | `scripts/provision-demo-access.ps1` e `scripts/provision-local-superadmin.ps1` (dados sintéticos; nenhuma credencial real antes da contratação formal) |
| 5 | MFA ativo para todos os superadministradores e usuários que exigirem segundo fator (TOTP + recovery codes registrados pelos próprios participantes) antes das 09:00 — fluxo inscrito e deterministicamente verificável pela matriz ACEITE (S5 F-1) | inscrição na própria UI (MfaEnrollment) ou prova ACEITE como referência de comportamento |
| 6 | Runbook D-OC40 §7 à mão: cenários por perfil (§5.3), suporte por sessão (§7.4), monitoramento/incidente/rollback (§7.5) | `docs/execution/D-OC40_PILOTO_DESIGN_PRONTIDAO.md` |

### 5.3 Cenários por perfil durante o piloto (D-OC40 §7.3 — exercitados agora com usuário real)

- **Cliente (admin/terapeuta do tenant)**: (1) login (+ MFA quando aplicável) e visão geral; (2) cadastrar/editar paciente com validação server-side (4 idiomas); (3) gerar contrato no estúdio a partir de modelo oficial; (4) preparar e assinar (transporte decidido em 5.1.2) até documento final anexado à versão; (5) abrir solicitação de revisão/adaptação e acompanhar SLA; (6) receber avisos no canal decidido (5.1.3) e agir nos prazos; (7) cumprir obrigação com evidência quando `evidenceRequired`; (8) renovação/alteração.
- **Superadministrador da plataforma**: (1) triar fila da Central ODCA (reatribuição sem reiniciar relógio, pausa justificada); (2) aprovar/recusar o modelo cirúrgico no workflow obrigatório; (3) abrir/estender/encerrar sessão de suporte da organização participante (motivo, duração, ator real na auditoria); (4) consultar a trilha de auditoria das ações do piloto.
- **Titular de dados (opcional)**: submeter pedido pela central de privacidade (`/privacidade`) e acompanhar o tratamento.

### 5.4 Suporte, monitoramento e saída

- Suporte sempre por **sessão de suporte** (janela 1–480 min extensível sem retrocesso, motivo obrigatório, ator real em cada ação, eventos `superadmin.support_session.*`); escritas exigem reautenticação MFA recente. Limitação registrada: sessão dá acesso auditável à plataforma, não impersonação profunda no tenant (dívida D-OC39).
- Monitoramento: logs do worker (reconciliação documental, purga pós-retenção), pickup de notificações, readiness e 5xx. Incidentes de dados seguem `docs/privacy/INCIDENT_RESPONSE.md`.
- Rollback: desplegar o SHA anterior + restaurar o backup pré-piloto. Nenhuma migração destrutiva durante o piloto (canônico atual v063; qualquer migração nova exige aprovação prévia + backup). Linhas do piloto identificáveis pelos tenants/contas convidadas.
- Critério de saída (D-OC40 §7.6): todos os cenários atribuídos concluídos com falhas classificadas e tratadas (transcript por usuário) **e** aprovação explícita da versão → só então creditar em `.odca/progress.json` os marcos que tiverem `completed_commit` + `ci_run_id` + `acceptance.approved=true` + `approved_by` + `evidence_paths` rastreáveis, sem alterar baseline/IDs/pesos.

## 6. GATE e separação de evidências

GATE sobre a árvore final (após as correções, antes do veredito):

| Etapa | Resultado |
| --- | --- |
| Build Debug `-warnaserror` (sln, 13 projetos) | **0W/0E**, EXIT=0 |
| Build Release `-warnaserror` (sln, 13 projetos) | **0W/0E**, EXIT=0 |
| Domain tests | **389/389**, EXIT=0 |
| Integration `-FreshDb` | **295/295**, TEST-EXIT=0 (baseline 293 da S21 + **2 novos** guardas de regressão do §2/§3) |
| `scripts/check-assets.mjs` | **exit=0** (10 assets / 19 chaves / round trips decimal+template) |
| `scripts/qa/scan-i18n-keys.mjs` | **KEYUSE-OK: 0 ausentes** (2746 chaves / 92 arquivos) |
| d41 (764) / d54 (382) | Não reexecutadas: nenhuma superfície auditada (`src/Odca.Web`, catálogos, assets) mudou nesta sprint — as voltas finais da S21 valem sobre esta mesma árvore (§4 item 5) |
| Dress rehearsal final | **8/8 estágios verdes** (consolidado r1 + r2, §2) |

Separação de evidências (rodada de falha ≠ rodada verde; nada sobrescreve nada):

- `evidence/d56-dress-rehearsal-r1.txt` — console completo da foto r1 (`stage_fails=2`), com os vereditos por estágio;
- `evidence/d56-d34-jornada-r1.txt` — 53 PASS/8 FAIL da jornada na r1;
- `evidence/d56-d34-r1-worker-erro.txt` — log do worker com a exceção Dapper de materialização (`DocumentWorker+VersionItem`), causa raiz da regressão;
- `evidence/d56-d39-seguranca-r1.txt` — sequência real da r1: P0a falho em v061 + 9 verdes até o abort no checkpoint pós-W1b;
- `evidence/d56-dress-rehearsal-r2.txt` — console da reexecução corrigida (`stage_fails=0`);
- `evidence/d56-d34-jornada-r2-verde.txt` — 61 PASS/0 FAIL, `RESULTADO: TODOS OS CHECKS PASSARAM`;
- `evidence/d56-d39-seguranca-r2-verde.txt` — 22 PASS/0 FAIL, `fails=0`;
- `evidence/d44-backup-restore-rodada-final.txt` / `evidence/d44-smtp-transport-rodada-final.txt` — transcripts re-versionados pelos próprios scripts nos estágios S10 da r1 (mesmas provas, nova data).
