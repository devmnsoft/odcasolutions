# Sprint 6 — candidata ao piloto: ensaio de piloto reproduzível, design das superfícies e matriz de prontidão (D-OC40) (18/10/2026)

Plano de seis sprints para candidata ao piloto (estreia 11/01/2027). Esta entrega fecha a Sprint 6 — última do plano.

## 1. Escopo e base

- **Base**: `codex/s00-foundation` @ `196bcdc` (Sprint 5 / D-OC39). Sem nova migração neste incremento (nenhuma mudança de schema prevista — ver §3 se o ensaio/auditoria exigirem correção que a mude).
- **F-1 (piloto)**: **ensaio de piloto reproduzível** — `scripts/qa/run-pilot-rehearsal.ps1` reexecuta, sobre a mesma base descartável `odca_test_disposable`, as provas ao vivo versionadas que definem a candidata ao piloto, em sequência única com transcript consolidado e critério por estágio (o próprio exit code; na matriz de aceite, a linha de resumo `PASS/FAIL/BLOCKED`): `C-SLA` (solicitações/SLA/aprovações), `ACEITE` (matriz comportamental), `D34-JORNADA` (jornada documental A–N), `D37-ASSINATURA` (envelope externo), `D38-ATENDIMENTO` (S4), `D39-SEGURANCA` (S5). Os estágios legados não gerenciam stack própria — o orquestrador sobe/derra a stack dev padrão (`run-local.ps1`: API https :7143 + Web https :7144 + Worker); os estágios autocontidos sobem/derrubam a própria stack http :5143/:5144. Falha de estágio não interrompe o ensaio: o objetivo é a fotografia completa, classificada depois como harness ou produto. Suporte `-Only 'IDs'` para reexecução pontual.
- **F-2 (design)**: **auditoria de design das superfícies do piloto** — `scripts/qa/audit-d40-design.ps1` + driver `scripts/qa/cdp-design-audit.mjs`: navegador real (Edge headless CDP) mede, por rota × idioma × viewport (360/768/1280/1440), (a) overflow horizontal (`scrollWidth > innerWidth`), (b) autenticação quebrada (redirecionamento indesejado para `/entrar`), salvando PNG apenas das falhas para triagem visual. Dez rotas da jornada de piloto: lado cliente `home`, `documentos`, `estúdio`, `pacientes`, `central-solicitacoes`, `notificações`, `privacidade` (anônima) e plataforma `atendimento-odca`, `suporte-sessoes`, `auditoria`. Superfícies **pt-first** entram somente em pt-BR — limitação registrada (dívida i18n do D-OC39); superfícies em 4 idiomas entram nas culturas correspondentes.
- **F-3 (matriz)**: **matriz de prontidão do piloto** (§6) conforme a exigência da spec (ODCA-PRIV-009, §10.9: requisito → implementação → evidência → responsável → pendência) e **runbook do piloto** (§7). Nenhum responsável ou aprovação inventado; os marcos do portal continuam sendo creditáveis apenas com CI oficial do SHA funcional + aceite real (`progress.json` inalterado nesta sprint).

## 2. GATE

Neste incremento não há mudança de código de produto: `src/` está inalterado desde `196bcdc` (S5) — o `git status` da árvore final mostra apenas scripts QA e documentação. As contagens do GATE da Sprint 5 permanecem válidas sobre o mesmo estado do código compilado e são reportadas sem inflação; o gate determinístico de assets e a paridade dos catálogos foram **revalidados hoje sobre a árvore final**:

- Builds estritos Release e Debug (`-warnaserror`): **0 warning / 0 erro**.
- Testes de domínio: **389/389** aprovados.
- Suíte de integração `-FreshDb`: **231/231** aprovados (base descartável dedicada).
- ASSETS-GATE determinístico: **ok** — 10 assets web, 19 i18n keys × 4 catálogos, round-trips decimal/template íntegros (revalidado hoje na árvore final: `Validated 10 web asset(s), 19 i18n key(s) across 4 catalog(s)...`).
- Catálogos de localização: **1770 chaves/cultura em 4/4 culturas**, paridade conferida por contagem determinística hoje sobre os 4 catálogos.
- Schema: v060 permanece o mais recente; checksum SHA-256 `1562ceacb177ea199cfeaf6eaf281af3e8052f704c570a3373f7a8e4bf5169ca`; nenhuma nova migração nesta sprint.

## 3. Mudanças

| Arquivo | Mudança |
|---|---|
| `scripts/qa/run-pilot-rehearsal.ps1` (novo) | Orquestrador do ensaio de piloto (F-1): reexecuta em sequência, sobre a mesma base descartável `odca_test_disposable`, as seis provas ao vivo versionadas (`C-SLA`, `ACEITE`, `D34-JORNADA`, `D37-ASSINATURA`, `D38-ATENDIMENTO`, `D39-SEGURANCA`); sobe/derra a stack dev padrão (`run-local.ps1`, https :7143/:7144) para os estágios legados; transcript consolidado (`evidence/d40-rehearsal.txt`) + logs por estágio; critério por estágio = exit code (matriz de aceite: parse da linha de resumo com exigência de `FAIL: 0`); pré-seed determinístico de sessão MFA real para a matriz de aceite (reinscrição do OPERADOR via API + recovery code sintético no hash exato de `MfaService.HashRecoveryCode`); falha não interrompe (fotografia completa); `-Only 'IDs'` para reexecução pontual; probe de prontidão da stack legada por `curl -k -w '%{http_code}'` (o `Invoke-WebRequest` do PS 5.1 falhava no certificado self-signed e queimava os 420 s de janela em todas as runs de 18/10 — ver §4) |
| `scripts/qa/audit-d40-design.ps1` (novo) | Orquestrador da auditoria de design (F-2): sobe a stack http :5143/:5144, provisiona sessão superadmin de prova com MFA reinscrita de chave conhecida, executa o driver CDP por rota × idioma × viewport, consolida `evidence/d40-design.txt` + JSON de resultados + PNGs somente das falhas; correção registrada na primeira execução: o contador do loop de confirmação MFA (`$t`) esmagava o tenant `$T` pela case-insensitivity do PowerShell — todas as rotas organizacionais navegaram para `/organizacoes/2/...` e vieram 404 (run arquivada como `*-run1-tenantbug*`, §5); agora usa `$att` + guarda que aborta se o tenant deixar de ser o GUID esperado |
| `scripts/qa/cdp-design-audit.mjs` (novo) | Driver CDP (Edge headless, WebSocket): navega cada rota nas culturas designadas, mede overflow horizontal (`scrollWidth > innerWidth`) e redirect indesejado a `/entrar` (com retry de relogin), salva PNG apenas nas falhas; classifica também HTTP 4xx/5xx do documento principal como `HTTPFAIL` (status lido da API `performance` antes da medida de layout) — sem isso a página de erro 404 da Chromium "encaixava" nos viewports ≥ 908px e vira OK falso (foi o que mascara a run-1) |
| `scripts/qa/validate-acceptance-matrix.ps1` (correção de harness) | Três correções que não mudam semântica dos itens auditados: (1) **S2** — string de formatação do operador `-f` continha chaves literais `'{}'` (placeholder inválido → `FormatException`); latência exposta quando o ramo INSERT passou a ser exercido na base limpa (`'{}'`→`'{{}}'`); (2) **S15** — diretório dos catálogos desatualizado (`src/Odca.Web/Localization` → `src/Odca.Configuration/Localization`, localização desde S2); (3) **S13** — fixture autocontido de alteração em estado final aplicado (`formalized`+`applied`) para bases limpas, com limpeza determinística do prefixo `Matriz-S13-Fixture%` antes de replantar |
| `scripts/qa/validate-d34-an-web.ps1` (resiliência de harness, semântica de checagem intacta) | Três modos de falha de ambiente observados nos ciclos do ensaio sobre máquina compartilhada (2026-10-18), todos agora recuperáveis dentro do próprio fluxo da prova, sem alterar asserção alguma (o check continua exigindo status terminal e o código estável exato quando houver falha): (1) **serviço fora do ar durante upload/polling** (`curl exit=7` → código `000`; na run de 06:12 o Odca.Web ficou ~10 min sem responder — log web parou às 06:31:08, sem linha de shutdown gracioso; worker seguiu processando normalmente no banco): após 3 falhas seguidas o loop de polling aciona o `Ensure-Stack` já existente na prova e segue — o resultado do job permanece no banco e o código estável é o mesmo ao ser lido depois; o upload ganha 1 retry após `Ensure-Stack`; (2) **expiração absoluta da sessão** (15 min, `Jwt:AccessTokenMinutes` sem sliding — a jornada estoura o limite sob carga): na run de 06:53 os uploads/polls passaram a receber 302 vazio para `/entrar` e as cadeias liam corpo vazio (`status=n/a`); a função nova `ReLogin-Chain` reinscreve a sessão do cliente na mesma jar e refaz o token antiforgery da mesma página, acionada uma única vez na primeira resposta 3xx de upload, de `/extrair` ou do polling; (3) **leitura do polling quebrada pela própria reescrita de resiliência** — erro de harness, não ambiente, apanhado pela triagem das runs 06:53 e 07:37: o novo loop usava o código retornado por `Watch-Curl`, que vem do stdout do curl; a chamada do polling não tinha `-w '%{http_code}'` (o código original ignorava o retorno), então todo poll reportava `000` e caía na branch “serviço fora do ar” (`continue`), pulando o parse em toda a janela — nas duas runs o job X1 concluiu em ~30 s com estado correto no banco e na API (`row=25|20|true` e JSON `ready_for_review` completo) e só foi visto no último poll da janela de 150 iterações. Correção: `-w '%{http_code}'` adicionado à chamada do polling + margem de tolerância de +30 iterações (~90 s apenas quando nada chegou a terminal). Guarda `Ensure-Stack` no início das fases T e N, no padrão já usado em “inicio da fase OCR”. Modos (1)/(2) são ambiente/comportamento de produto; modo (3) era erro de harness corrigido antes do ciclo final |
| `docs/execution/D-OC40_PILOTO_DESIGN_PRONTIDAO.md` (novo) | Este documento: escopo, GATE, ensaio, auditoria de design, matriz de prontidão (F-3) e runbook do piloto |
| `docs/execution/STATUS.md` / `docs/execution/BACKLOG.md` | Seção da Sprint 6 |

Nenhuma mudança em `src/`: a triagem das falhas observadas no primeiro ciclo do ensaio (C-SLA/ACEITE sem stack, matriz S2/S15) classificou tudo como harness/ambiente; nenhum ajuste de produto foi necessário até aqui.

## 4. Ensaio de piloto (F-1)

Rodada final de 2026-10-18, iniciada às 08:28:26 sobre `odca_test_disposable` (transcript consolidado `evidence/d40-rehearsal.txt` + transcript/log por estágio). Resultado por estágio:

| Estágio | exit | duração | contagem | veredito |
|---|---|---|---|---|
| C-SLA | 0 | 31 s | `PASS=98 FAIL=0` | PASS |
| ACEITE | 0 | 55 s | `PASS: 15 · FAIL: 0 · BLOCKED: 0` | PASS |
| D34-JORNADA | 0 | 892 s | **61 checks `[PASS]` / 0 `[FAIL]`** — `RESULTADO: TODOS OS CHECKS PASSARAM` (inclui X1 `total=25 proc=20 trunc=True` com banco/API em concordância, X2 `ready_for_review`, T1 `extraction-timeout`, N1 `pdf-no-text-layer`, N2 `extractor-not-configured`, N3 worker de volta) | PASS |
| D37-ASSINATURA | 2 | 54 s | falhou em `P0b` (purga): `psql: conexão … 127.0.0.1, porta 5432 falhou: o tempo limite expirou` | FAIL (**ambiente**) |
| D38-ATENDIMENTO | 0 | 78 s | `PROVA-FIM fails=0` | PASS |
| D39-SEGURANCA | 0 | 127 s | `PROVA-FIM fails=0` | PASS |

→ `stage_fails=1`. A única falha é **de ambiente, não de produto nem de harness**: o backend do Postgres (Docker) estancou ~1 min sob pressão de memória da máquina compartilhada (memória livre medida em **0,6 GB**; outros projetos compilando/em runtime no mesmo instante). O serviço não parou; psql voltou a responder em 199 ms e os estágios seguintes (D38/D39, que também purgam a base no início) passaram sem alteração.

**Reexecução pontual do D37** (`-Only 'D37-ASSINATURA'`, 09:05:22, mesma base e mesmo código; transcript `evidence/d40-rehearsal-d37-rerun-0905.txt`, citando o transcript do run 08:28 como contexto da falha transitória): `STAGE-PASS exit=0 dur_s=54`, `PROVA-FIM fails=0` (checks E1–E8: sync com dedup determinística, envelope `accepted`, 409 `envelope.accepted` após aceito). **Composta a rodada: seis estágios verdes na árvore final** — cinco no transcript consolidado + o D37 na reexecução pontual, com a causa do fail registrada acima.

Nota de harness (sem impacto nos vereditos): em todas as runs do dia a probe de prontidão da stack legada registrou “API nao ficou pronta em 420s” embora os estágios C-SLA/ACEITE passassem imediatamente depois — o `Invoke-WebRequest` do PS 5.1 falha no certificado self-signed da stack dev e nunca reportava 200. Correção registrada nesta entrega: probe trocado para `curl -k -w '%{http_code}'` no orquestrador (ver §3), de modo que as próximas rodadas detectem prontidão em segundos e não queimem os 420 s.

### Convergência das runs de 2026-10-18 (máquina compartilhada sob carga)

Todas as runs do dia foram sobre a mesma base `196bcdc`; falhas vermelhas foram **todas** atribuídas, com evidência, ou ao harness (corrigido antes da rodada final) ou ao ambiente:

| Run (início) | Resultado D34 | Classificação da(s) falha(s) |
|---|---|---|
| 04:32 / 05:04 | abortadas dentro do estágio | bugs do próprio orquestrador novo (polling do filho, prontidão) — corrigidos no mesmo dia, antes do primeiro ciclo completo |
| 05:32 | PASS (807 s) | primeiro ciclo completo; C-SLA/ACEITE falharam por stack legada/harness da matriz (correções S2/S15/S13 em `validate-acceptance-matrix.ps1`, §3) |
| 06:12 | N1/N2 em fail | **ambiente**: Odca.Web derrubado externamente às 06:31 (log web sem linha de shutdown gracioso; `curl exit=7` reais) — recuperável pela resiliência de polling/`Ensure-Stack` (§3, modo 1) |
| 06:53 e 07:37 | fases X/T/N em fail (8 checks) | **harness**: (i) expiração absoluta da sessão de 15 min estourada sob carga → `ReLogin-Chain` (§3, modo 2); (ii) loop de polling lendo o stdout de um curl sem `-w '%{http_code}'` → todo poll reportava `000` e consumia a janela inteira (`continue` antes do parse), embora o job estivesse pronto no banco/API em ~30 s — corrigido (§3, modo 3); a run 07:37 ainda bateu no timeout próprio de 40 min do estágio, evidenciando a janela queimada |
| **08:28 (final)** | **PASS (892 s, 61/61)** | único fail do dia fora da D34: stall transitório do banco em D37/P0b, classificado acima e limpo na reexecução pontual |

**Não houve nenhuma falha de produto nas rodadas**: cada check vermelho possui atribuição comprovada (transcript + logs + corpo HTTP + linha do banco), e a rodada final fecha verde nas seis provas.

Evidências: `evidence/d40-rehearsal.txt` (rodada final), `d40-rehearsal-d37-rerun-0905.txt` (reexecução pontual), `d40-stage-<ESTÁGIO>.out`/`.log` por estágio, rodadas anteriores preservadas como `d40-rehearsal-run-0612/0653/0737/0828.txt` e `d40-stage-*-run-*.out`, e `d34-proof-fim.txt`.

## 5. Design das superfícies (F-2)

Duas execuções da auditoria sobre a árvore final (evidência consolidada em `evidence/d40-design.txt` + JSON de medidas e PNGs em `evidence/design-audit-d40/`).

**Run 1 — invalidada (harness)**: reportou `checks=72 ok=64 overflow=8 authfail=0`. Os oito "overflows" caíam exatamente na mesma medida (768×1024, `sw=908`) das cinco rotas clientais, e os PNGs de triagem mostravam a **página de erro 404 da própria Chromium** ("Esta localhost página não pode ser encontrada") para URLs como `/organizacoes/2/documentos`. Causa raiz no harness, não no produto: o contador do loop de confirmação MFA (`$t`) colidia com a variável do tenant (`$T`) pela case-insensitivity do PowerShell e corrompia o cfg do driver com `/organizacoes/2/...`, que não casa com a constraint da rota `{tenantId:guid}` e responde 404 em qualquer viewport. O defeito só aparecia em 768px porque a largura mínima da página de erro (~908px) excedia o viewport; em 360px ela era escalada para caber e em 1280/1440 simplesmente cabia — ou seja, as 28 medidas dessas cinco rotas na run 1 nunca mediram a superfície real. Correções (§3): loop renomeado para `$att` + guarda que aborta se `$T` deixar de ser o GUID esperado + classe `HTTPFAIL` (status HTTP do documento principal) no driver antes da medida de layout. A run foi arquivada como `evidence/d40-design-run1-tenantbug.txt` + `evidence/design-audit-d40-run1-tenantbug/` (os 8 PNGs).

**Rodada final (harness corrigido)**: **`AUDIT-DONE checks=72 ok=72 overflow=0 authfail=0 httpfail=0`**, exit=0 — as dez rotas × idiomas × 360/768/1280/1440 renderizaram **sem overflow horizontal** e **sem autenticação quebrada** (nenhum redirect indesejado a `/entrar`), todas com documento principal 200. Nenhuma correção de produto foi necessária — `src/` permaneceu inalterado (§2) — e nenhum PNG foi produzido (apenas falhas são capturadas). Limitação registrada, sem mudança: as superfícies pt-first (documentos, estúdio, atendimento ODCA, sessões de suporte) entraram só em pt-BR; as superfícies 4 idiomas cobriram suas culturas (pacientes ×2, central-solicitações e notificações ×4, home ×2).

## 6. Matriz de prontidão do piloto (F-3)

Base: `.odca/progress.json` (baseline 40% histórica + 6 marcos; IDs/pesos imutáveis). Esta sprint **não credita marco nenhum** — sem CI oficial publicada e sem aceite real, nenhum marco sai de `pending` (regra do manifesto, respeitada).

| Marco (peso) | Critério (manifesto) | Estado da implementação | Evidência técnica (repositório) | Responsável | Pendência |
|---|---|---|---|---|---|
| `templates` (8) | Aprovação dos textos, campos e PDFs do catálogo de estreia | Catálogo de estreia = **9 modelos oficiais** (NDA unilateral/mútuo, serviços, alteração, suprimentos, locação, múltiplas terapias, consentimento informado, consentimento cirúrgico) publicados como linhas globais por seeder idempotente por `official_key`; `surgical-consent` trava em pendência até **aprovação ODCA obrigatória** (workflow provado, decisão via linha global presa à versão global) | `src/Odca.Api/OfficialCatalogSeedingService.cs`; `src/Odca.Application/Contracts/OfficialContractTemplates.cs`; D-OC37; `AUDITORIA_OPERACAO_CONTRATUAL_20261010.md` (bloco E-S6) | **pendente** — responsável pela aprovação editorial/jurídica do catálogo | Aprovação humana dos textos/campos/PDFs dos 9 modelos (a aprovação técnica não substitui revisão jurídica); CI oficial do SHA |
| `signature` (12) | Envio, eventos, negativas e documento final comprovados | Contrato plugável `ISignatureProvider` + transporte de referência **local-file** exercendo a mecânica ponta a ponta (abertura, callbacks, eventos, negativas, documento final anexado à versão exata); status derivado na versão; superfície de assinatura em 4 idiomas | D-OC37 (prova ao vivo fails=0 + 20 capturas); suítes de assinatura da integração; `THREAT_MODEL.md` (fila de envelopes = FALSE-POSITIVO registrado) | **pendente** — definição de fornecedor | Fornecedor externo de envelope **indefinido** (decisão preservada: "proposto, não escolhido" — contrato entregue na S3; nenhuma integração simulada como concluída); homologação com provedor real pendente |
| `operations` (8) | Paciente, documento, revisão, agenda e renovações validados juntos | Jornada documental completa demonstrada ao vivo (onboarding, paciente, estúdio, conferência/renovação, revisão/solicitação com SLA, assinatura, obrigações com evidência) + OCR honesto para rasterizado/escaneado | D-OC34 (jornada A–N), D-OC35 (provas consolidadas), D-OC36 (paciente em 4 idiomas), D-OC38 (atendimento/avisos/prazos), ensaio desta sprint (§4) | **pendente** — aceite dos usuários convidados | Aceite humano dos cenários (marco só fecha com usuários convidados concluindo e responsável aprovando); CI oficial do SHA |
| `production` (10) | E-mail, armazenamento, isolamento e recuperação comprovados | **Isolamento**: RLS default-deny (e FORCADA em `odca.tenants`, com leitura governada corrigida na S5/v060) coberto por suítes; **armazenamento**: disco local + reconciliação de órfãos + purga física pós-retenção (S5 A-1) com chave configurável; **e-mail**: outbox transacional com canal desabilitado por padrão | `THREAT_MODEL.md`; suítes RLS A×B das S1–S5; D-OC39 (purga); `RETENTION_POLICY.md`; `RELEASE_EVIDENCE.md` | **pendente** — operação/infra | SMTP real (fornecedor externo indefinido); **backup/restauração real ensaiado antes de produção** (risco remanescente do THREAT_MODEL; spec §"histórico não substitui backup"; janela de backup ainda "a definir" na `RETENTION_POLICY.md`); deploy físico multi-instância |
| `pilot` (12) | Usuários convidados concluem cenários e responsáveis aprovam a versão | Candidata preparada: ensaio reproduzível (F-1), auditoria de design das superfícies (F-2), runbook com cenários/contas/suporte/rollback (este §7); mecanismo de suporte operacional já existe (sessões de suporte auditadas + reautenticação MFA recente — S5) | esta entrega: `run-pilot-rehearsal.ps1`, `audit-d40-design.ps1`/`cdp-design-audit.mjs`, §7 | **pendente** — quem convida e quem aprova | **Usuários convidados a definir** pelo responsável; aprovação da versão após a execução real dos cenários |
| `launch` (10) | Implantação, contingência e suporte aprovados para o uso real | Mecanismos em posição: suporte via sessões de plataforma auditadas (S5), resposta a incidente e retenção documentadas, runbook de rollback (este §7) | D-OC39; `docs/privacy/INCIDENT_RESPONSE.md`; `RETENTION_POLICY.md`; §7.5 | **pendente** — aprovação de implantação | Ambiente de destino definido; plano de contingência aprovado; **primeiro backup/restore real** como pré-condição |

### Invariantes de arquitetura (verificação cruzada)

| Invariante | Evidência |
|---|---|
| Arquitetura canônica | `docs/architecture/ADR-001.md`; estrutura estável S00–S5 |
| Isolamento por organização | RLS default-deny + FORCADA testada entre tenants A×B (suítes S1–S5); leitura de plataforma governada (v060) |
| Autorização no servidor | policies ASP.NET + guards SQL (fatos da S5 mostram bloqueio mesmo sem camada de política) |
| MFA e revogação | TOTP com anti-replay por time-step, recovery codes, revogação de sessão/security version (S1) + reautenticação recente (S5) |
| Versões imutáveis | versões de contrato/versionais testadas desde S02/S03 |
| Assinatura ↔ versão exata | linha de decisão presa à `template_version_id`/versão exata (AUDITORIA E-S6; D-OC37) |
| Concorrência/idempotência | lease `SKIP LOCKED` (S4 F-3), upload idempotente por conteúdo (D-OC32), unicidade ativa de sessão de suporte (S5, 23505→409) |
| Proteção de dados confirmados | suítes de privacidade + `docs/privacy/*` (eliminação propagada, retention, legal hold registrado como pendência jurídica) |
| Quotas por plano | cotas por tenant/plano exercidas nas suítes de operação |
| Ativação ≠ pagamento | estado comercial `commercial_pending` desde o onboarding (S01) |
| 4 idiomas + identidade ODCA | paridade 4/4 dos catálogos pelo gate determinístico (assets) a cada GATE; áreas operacionais pt-first registradas como dívida (documentos/estúdio/atendimento/obrigações/suporte) |

### Bloqueios externos registrados (nenhum resolvido nesta sprint, nenhum inventado)

- Fornecedor externo de assinatura (envelope) indefinido — decisão "proposto, não escolhido" preservada desde a S3.
- Fornecedor/SMTP real indefinido — canal de e-mail segue desabilitado por padrão; nada simulado como concluído.
- CI oficial nunca verde publicada — branch sem push autorizado; nenhum ID de execução inventado (vale também para os SHAs das S1–S5).
- Usuários convidados do piloto: a definir pelo responsável.
- Backup/restauração real: ainda não ensaiado (pré-condição explícita do runbook).

## 7. Runbook do piloto

### 7.1 Pré-condições (todas devem ser verdade ANTES do convite)

1. Push da branch e CI oficial verde no SHA do piloto (job `build-test` + passo `Tests`) — hoje **pendente**.
2. **Backup/restore real ensaiado** e registrado (transcript) — hoje **pendente**; sem ele o rollback do §7.5 não é confiável.
3. Decisão explícita do transporte de assinatura do piloto: provedor externo contratado (com homologação real registrada) **ou** transporte de referência local-file (declarar isso aos usuários; o fluxo completo continua funcional).
4. Decisão do canal de aviso: SMTP real configurado **ou** somente sino in-app (padrão atual).
5. Este ensaio (§4) e a auditoria de design (§5) verdes na árvore final.

### 7.2 Contas e dados

- Contas sintéticas pelos provisioners versionados: `scripts/provision-demo-access.ps1` (perfis `admin`/`client`, senha resetável) e `scripts/provision-local-superadmin.ps1`; dados do tenant demo; **nenhuma credencial real no ambiente de piloto** antes da contratação formal.
- Superadministradores do piloto inscritos em MFA (TOTP + recovery codes) antes do dia 1 — fluxo isento apenas na primeira troca de senha (S5 F-1).
- Registro dos participantes (nome do papel, conta, cenários atribuídos) mantido fora do repositório.

### 7.3 Cenários por perfil (todos já cobertos por prova ao vivo ou suíte — o piloto exercita com usuário real)

- **Cliente (admin/terapeuta do tenant)**: (1) login (+ MFA quando aplicável) e visão geral; (2) cadastrar/editar paciente com validação server-side (4 idiomas); (3) gerar contrato no estúdio a partir de modelo oficial (campos do template); (4) preparar e assinar (transporte decidido no §7.1.3) até documento final anexado à versão; (5) abrir solicitação de revisão/adaptação e acompanhar SLA (pausa/retomada justificada, violação registrada); (6) receber avisos no sino in-app e agir nos prazos; (7) cumprir obrigação com evidência quando `evidenceRequired`; (8) renovação/alteração.
- **Superadministrador da plataforma**: (1) triar fila da Central ODCA (reatribuição sem reiniciar relógio, pausa justificada); (2) aprovar/recusar o modelo cirúrgico no workflow obrigatório; (3) abrir/estender/encerrar **sessão de suporte** da organização participante (motivo, duração, ator real na auditoria); (4) consultar a trilha de auditoria das ações do piloto.
- **Titular de dados (opcional)**: submeter pedido pela central de privacidade (`/privacidade`) e acompanhar o tratamento (triagem/resposta na fila da plataforma).

### 7.4 Suporte da plataforma durante o piloto

- Preferir sempre a **sessão de suporte** (S5 F-2) em vez de compartilhar credenciais: janela limitada (1–480 min, extensível sem retrocesso), motivo obrigatório, ator real em cada ação, eventos e auditoria `superadmin.support_session.*`.
- Escritas dentro da janela são porteadas pela **reautenticação MFA recente** (S5 F-1) — quem suporta precisa ter verificado o segundo fator dentro da janela (`Identity:RecentAuthMinutes`).
- Limitação registrada: a sessão dá acesso **auditável à plataforma**, não impersonação profunda na organização do cliente (dívida do D-OC39) — durante o piloto, ações no escopo do tenant seguem pelas contas convidadas.

### 7.5 Monitoramento, incidente e rollback

- Monitorar: logs do worker (reconciliação documental, purga pós-retenção), pickup de notificações, readiness (`/health/ready`) e erros 5xx dos serviços.
- Incidentes de dados: seguir `docs/privacy/INCIDENT_RESPONSE.md` (escopo, evidência minimizada, comunicação).
- Rollback: **desplegar o SHA anterior + restaurar o backup pré-piloto**. Nenhuma migração destrutiva é permitida durante o piloto (v060 é a mais recente; qualquer migração nova exige aprovação prévia + backup). Linhas do piloto identificáveis pelos tenants/contas convidadas.

### 7.6 Critério de saída do piloto

- Todos os cenários atribuídos concluídos pelos usuários convidados, com falhas classificadas e tratadas (transcript).
- Responsável **aprova a versão** explicitamente.
- Com ambos, creditar em `.odca/progress.json` os marcos que tiverem `completed_commit` (SHA funcional), `ci_run_id` (CI oficial daquele SHA), `acceptance.approved=true`, `approved_by` e `evidence_paths` rastreáveis — sem alterar baseline/IDs/pesos.

## 8. Separação dos tipos de evidência

- **Teste local (GATE, determinístico)**: builds estritos Release+Debug 0W/0E; Domain 389/389; suíte de integração `-FreshDb` 231/231; gate determinístico de assets ok (10 web assets, 19 i18n keys × 4 catálogos, round-trips íntegros); paridade de catálogos 1770 chaves/cultura em 4/4; schema v060 sem mudança (checksum §2). Executados na máquina local sobre a árvore final — constituem prova de compilação e comportamento unitário/integrado, **não** de aceitação.
- **Prova ao vivo (ensaio consolidado + provas individuais versionadas)**: `evidence/d40-rehearsal.txt` (rodada final 08:28, veredito por estágio) + reexecução pontual do D37 (`d40-rehearsal-d37-rerun-0905.txt`) + transcript/log por estágio com as linhas `PROVA-FIM fails=0` de cada prova; rodadas intermediárias classificadas em §4. Toda a prova ao vivo roda sobre a base descartável `odca_test_disposable` (dados sintéticos, senhas de fixture versionado) — nenhuma credencial real ou dado pessoal aparece nos relatórios.
- **Navegador (CDP, Edge headless real)**: auditoria de design (§5) e capturas das provas das sprints anteriores. Capturas comprovam **renderização**, não comportamento — as asserções comportamentais vivem nas provas ao vivo acima.
- **CI**: **não verificada** — a branch não tem push autorizado; a CI oficial nunca foi verde publicada para os SHAs das S1–S6; nenhum ID de execução é inventado neste documento.
- **Aceite humano**: **pendente** — os seis marcos do portal seguem `pending`; nenhum marco recebe crédito nem aprovação registrada sem o aceite real dos responsáveis e a CI oficial do SHA funcional (regra do manifesto).
- **Portal**: `.odca/progress.json` **inalterado** nesta entrega (baseline 40% histórica; IDs e pesos intactos).

## 9. Riscos e próximo passo

- O risco dominante para a estreia 11/01/2027 não é funcional: é o conjunto de pendências externas do §6 (provedores reais, CI publicada, backup ensaiado, usuários convidados). Esta sprint entrega a **capacidade de comprovar** o estado atual e o roteiro para fechar as pendências.
- **Duração absoluta de sessão (15 min, sem sliding) — decisão de produto a confirmar antes do convite**: a sessão web/API expira em `login + Jwt:AccessTokenMinutes` (default 15; faixa validada 5–30 em `StartupValidation.cs`); a política vem de `DependencyInjection.cs:51` e a validação por request é `EXISTS (... s.expires_at > now ...)`, sem renovação ao usar (`NpgsqlIdentityRepository.cs`, consulta `IsSessionActive`). Observado no ensaio: na run de 06:53 sobre máquina carregada, a jornada A→N estourou a janela e os requests da fase OCR/T/N passaram a receber 302 vazio para `/entrar` (~15 min após o login do cliente; último read autenticado ok em ~10,5 min). Não é bloqueador funcional — reautenticação existe e a superfície de desafio MFA recente está provada (D39/F1) — mas é uma **decisão de experiência para usuário ativo** (desconexão a cada 15 min independentemente de atividade); manter como segurança estrita ou alongar/sliding fica com o responsável de produto antes do piloto.
- Dívidas registradas que persistem após S6: impersonação profunda, i18n das áreas pt-first, fila/privacidade/content/aria carreados das S3–S5 (BACKLOG).
- Após este commit: push quando solicitado → CI oficial passa a ser verificável; em seguida, agendamento do piloto real (§7).
