# D-OC34 — Piloto da jornada documental para a estreia: escopo OCR honesto, assinatura real sem fornecedor inventado e diagnóstico operacional (16/10/2026)

Base: `codex/s00-foundation` @ `8095bc8` (= D-OC33). Sem push/merge/deploy automático; commit único
"sem push" ao final.

## Objetivo

Concluir a jornada documental do piloto do MVP para a estreia planejada em 11/01/2027: corrigir os
dois P1 restantes antes das funcionalidades dependentes (plano fantasma e OCR que podia aparecer
completo sem ser), entregar o cadastro de paciente/representação e a assinatura com as modalidades
reais disponíveis hoje (sem inventar fornecedor pago ou homologação de envio externo), e dar ao
superadministrador um diagnóstico honesto do que está de fato disponível na máquina de operação.

Regras duras observadas: build estrito 0W/0E; nenhum diagnóstico suprimido e nenhuma autorização
enfraquecida; invariantes preservados (RLS/isolamento por tenant, versões imutáveis, vínculo
assinatura↔versão exata, concorrência/idempotência, proteção de dados confirmados, quotas sem
consumo duplicado, ativação ≠ pagamento, 4 idiomas); sem segredos em telas e relatórios; OCR parcial
nunca aparece como integralmente analisado; dependência ausente aparece como indisponível com
decisão registrada.

## GATE (pré-requisito dos avanços dependentes)

- Builds estritos da solution (Release+Debug, `-warnaserror`): **0W/0E** sobre a base `8095bc8`.
- Domain: **364/364 PASS**.
- Suíte de integração zerada via `scripts/qa/run-integration-tests.ps1 -FreshDb`: **201/201 PASS —
  zero falhas** (+1 fato novo de arquivamento com motivo em `TemplateCatalogPersistenceTests.cs`;
  checksum v057 estendido em `MigrationChecksumTests.cs`). FRESH-DB-OK → BUILD-OK → TEST-EXIT=0
  (log `%TEMP%\opencode\d34-suite-freshdb.log`).
- Os dois P1 (F1/F2) foram corrigidos e provados **antes** de qualquer funcionalidade dependente.

## F1 — Plano fantasma `odca-pro` eliminado

O catálogo de planos servido à interface divergia do seed canônico: um quarto plano `odca-pro`
aparecia como opção mesmo não existindo em lugar nenhum na base. Decisão: **um único catálogo**,
reconciliado com os planos canônicos v2 (`basic`, `intermediate`, `enterprise`).

- Zero ocorrências de `odca-pro` em todo o repositório (grep canônico).
- Base descartável: `plan_versions` contém exatamente `basic|enterprise|intermediate`.
- Invariante mantido: ativação de plano ≠ pagamento recebido (apresentação e regra comercial do
  D-OC31 intactas).

## F2 — Escopo OCR honesto (parcial nunca aparece como integral)

Migração **v057** estendida e ancorada (checksum `d5d58743…dd7b13`, `CurrentVersion=57`, snapshot
byte-idêntico em `database/releases/odca-v057.sql`). Conteúdo do incremento em v057:

1. `odca.extraction_results` ganha `pages_total` (int nullable), `pages_processed` (int nullable) e
   `truncated` (bool, default false), mais `method_ck` para fechar o domínio de método
   (`pdf-native|ocr|docx|text`).
2. `odca.contract_templates` ganha `required_activity_profile` (nullable, allowlist do perfil do
   tenant) e `archive_reason` (varchar(500) nullable) — usados por B.5.
3. Nova função `odca.platform_queue_summary()` SECURITY DEFINER: contagens atuais de pendentes /
   paradas / falhas das três filas de trabalho, apenas para tenants ativos (usada pelo diagnóstico
   A.3; nunca expõe conteúdo dos payloads).

Semântica implementada no worker (códigos centralizados em `MarkExtractionFailed`):

- **PDF escaneado**: renderiza até `Documents:OcrMaxPages` (20); o job chega a `ready_for_review`
  declarando `pages_total=25`, `pages_processed=20`, `truncated=true` quando o documento tem mais
  páginas — a UI mostra o escopo ("Análise parcial: 20 de 25 página(s) processada(s)…"), o que
  ficou fora e a próxima ação (trecho menor ou revisar o limite). Franquia consome as páginas
  efetivamente processadas.
- **Páginas brancas contam** como processadas (texto vazio é resultado, não falha — D-OC33).
- **"Fraca" = < 20 caracteres úteis**; o gatilho de truncamento honesta é atingiu o limite de
  páginas escaneáveis (cap visível < total, ou >= OcrMaxPages em documento escaneado).
- **Imagem única**: `(pages_total, pages_processed, truncated) = (1, 1, false)`; **docx**:
  `(null, null, false)` — texto direto não fabrica paginação.
- **`pdfinfo` opcional**: quando ausente, `pages_total` fica NULL e a UI declara "total não pôde
  ser confirmado" (mensagem própria `studio.x_scope_unknown`) — jamais presumido.
- **Códigos de falha distintos e estáveis**: `extraction-timeout` (limite excedido),
  `extractor-not-configured` (executável de extração ausente na configuração),
  `extractor-failed` (executável presente que falhou) e `pdf-no-text-layer` (PDF só de imagem sem
  renderer); entrada não suportada vira `invalid-document` — centralizados em
  `MarkExtractionFailed`, retratáveis até o limite de tentativas.
- Revisão humana e origem das sugestões preservadas (nada muda no fluxo aceitar/editar/rejeitar).

## B.5 — Arquivar exige motivo + gate opcional por perfil

- `POST studio/templates/{id}/archive` passa a exigir `reason` (5..500 chars; 400
  `api.studio.archive_reason_required` com mensagem amigável); o motivo é persistido em
  `archive_reason` (auditável) e as versões publicadas continuam **imutáveis** (conteúdo, campos e
  `current_version` intocados). BFF: campo inline na edição do modelo.
- Novo gate opcional no `CreateDraft`: template com `required_activity_profile` diferente do
  `activity_profile` do tenant → **409 `draft.profile.template_mismatch`** com orientação estável
  (mudar o perfil da organização em Organização → Editar). `NULL` = sem gate (comportamento
  anterior preservado).
- O filtro care/consent (purpose `care_agreement`/`consent`) já existia de ponta a ponta
  (estúdio → API) — validado, sem nova mudança de código.

## B.6/B.7 — Paciente e representação dentro da jornada

- **Vincular/desvincular paciente na minuta** (`PUT studio/drafts/{id}/patient` + BFF
  `vincular-paciente`): row_version otimista (conflito 409 com recuperação), snapshot imutável da
  seleção persistido, autofill **somente** dos campos patient/representative **não confirmados**
  (campo confirmado nunca é sobrescrito — invariante de proteção de dados confirmados), evento
  auditável `patient.linked` no contrato (inclui `previousPatientId`). Desvincular restaura
  `patient_id=NULL` preservando o snapshot.
- **Conflito de versão explícito**: o banner de conflito do editor ganhou botões
  "Carregar versão atual" / "Manter minha versão" (retry mantido), tratados no `studio.js`.
- **Chip de checklist** no cabeçalho do editor conta pendências bloqueantes
  (`studio.e_checklist_ok/blocked`).
- **Cadastro rápido inline** na página de novo documento (`POST documentos/novo/paciente`):
  nome + tipo/número do documento (+ nascimento/representante opcionais), validação de CPF/CNPJ,
  feedback de sucesso/erro legível; paciente criado já entra como selecionado no fluxo.

## B.8/B.9 — Modalidades reais de assinatura e próxima ação final

- Banner nas fichas (§assinatura) deixando claro ao leigo: cada participante assina **em sessão**
  (própria conta + MFA) ou com **registro em evidência** por terceiro; **nenhum envelope externo é
  enviado e a franquia de assinatura não é consumida** enquanto não houver fornecedor escolhido.
- `nextAction='completed'` na listagem de documentos → rótulo fixo **"Concluído — disponível para
  download"** na central (jornada terminada: versão assinada externamente).

## B.10 — Assuntos de e-mail genéricos e fixos por kind

Assuntos SMTP viraram textos **fixos pt-BR por kind** (sem dados livres do payload no assunto —
proteção contra vazamento e spoofing): `review_assigned`, `review_decided` (aprovada/alterações),
`signature_pending`, `solicitation_opened`, `solicitation_updated`, `obligation_due`,
`renewal_due`, e o novo **`signature_completed` = "Assinatura concluída"** (destinatário = criador
da preparação; deduplicação determinística por preparação+versão — replay do ato não duplica).
`EnqueueEmailAsync` continua escrevendo **apenas outbox** (mesmo contrato dos demais eventos de
assinatura); o envio externo segue sem homologação.

## A.3 — Diagnóstico operacional para superadministrador

`GET /administracao/diagnostico` (BFF) → `GET platform/diagnostics` (API, política
`PlatformAdministrator`): sonda as dependências reais da máquina e apresenta:

- Estados em **quatro níveis**: `unavailable < configured < accessible < verified`
  (+ `not_applicable`). Configurar não comprova funcionar — credencial preenchida ≠ acessível ≠
  comprovado; o scanner internalizado fica no máximo `accessible` (prova exige payload conhecido).
- **Valores configurados nunca são exibidos** (só presença/chave); a página não contém senhas,
  connection strings nem tokens.
- Envelope externo de assinatura aparece como `unavailable` com a decisão registrada.
- **Resumo de filas da plataforma** (`odca.platform_queue_summary()`) para triagem: pendentes,
  paradas, falhas e idade do mais antigo por fila.

## Decisão registrada — envelope externo de assinatura: "proposto, não escolhido"

Na triada de modalidades, o envelope assinado por prestador externo (Clicksign foi o candidato
proposto na análise) **não foi escolhido para a estreia**: depende de fornecedor pago, homologação
de envio e contrato comercial. Até lá ele é exibido como indisponível e a decisão fica registrada
no DECISIONS-LOG — sem inventar integração, custo ou status de homologação.

## D — Provas ao vivo (contra a base descartável `odca_test_disposable`)

Método: jornada fim-a-fim A–N sobre a base descartável `odca_test_disposable` (separada da
`odca_test_integration` da suíte), conduzida por script reproduzível em um único comando —
`scripts/qa/validate-d34-an-web.ps1`. Cada rodada começa com **purga determinística** dos dados do
tenant + fixtures fixas (plano enterprise v2 para o módulo de revisões, paciente da jornada,
canal de e-mail `signature_completed` opt-in, diretório de pickup limpo), sobe stack própria
(api :5143 / web :5144 / worker), executa os segmentos A→N e termina com marker `PROVA-FIM
fails=<n>` em `d34-proof-fim.txt`. Dados sintéticos ancorados (tenant/contas/pacientes de fixture);
sem credenciais nos relatórios.

Convergência das rodadas locais (todas em 17/10, máquina de desenvolvimento compartilhada):

| Rodada | Falhas | Causa (compartilhada em `evidence/d34-proof-an-rodada10-falhas-classificadas.md`) |
|---|---|---|
| 10 | 31 | zero falhas de produto: 6 causas próprias do script (purga de tabelas sem `tenant_id`, quoting PS 5.1 p/ psql, assembly base do `ZipArchiveMode`, dígito verificador CPF/CNPJ) + 15 cascata (suíte paralela derrubou dotnet da prova) + 10 OCR na fase X/T/N |
| 11a / 11b (final-1) | abortadas | cura aplicada no ponto de morte de cada rodada (privacy_request_events; CPF inválido no fixture J0) |
| FINAL-2 | abortada | MFA challenge 400: anti-replay TOTP da `MfaService` rejeita código de janela igual à confirmação da inscrição; retry antigo não esperava janela nova |
| FINAL-3 | 15 | primeiro `vidB` real: `plan_restricted` — módulo de revisões só existe habilitado no enterprise v2; X1-2 causa própria (paginação extra do wkhtmltopdf: 26≠25); demais em cascata do J4 |
| FINAL-4 | 12 | contato obrigatório do participante de assinatura (`Informe e-mail ou telefone.`) — validação real da API; J0→J4 verde pela 1ª vez |
| FINAL-5 | 5 | só B10: coluna `payload` inexistente (attribution por `source_id` determinístico), pickup dir via runtime config (não appsettings) |
| FINAL-6 | **0** | todos os checks PASS (transcript em `evidence/d34-proof-an-rodada-final.txt`) |

O que foi provado na rodada final (todos os checks PASS):

- **A** (diagnóstico operacional): página do superadmin renderiza; 8 dependências sondadas com
  rótulos; envelope externo `Indisponível` com decisão registrada; renderer em estado honesto
  (nunca "Configurado"); nenhum valor de configuração exposto; resumo de filas visível.
- **W/F**: login web do cliente sem MFA (302 direto); catálogo único de planos v2 sem fantasma.
- **B.5–B.7**: arquivar exige motivo (persistido + estado), versões imutáveis, gate de perfil
  (rejeição + nenhuma minuta criada); vincular/desvincular paciente com snapshot imutável,
  autofill só de campos não confirmados e evento `patient.linked`; cadastro rápido (cria, valida
  CPF/CNPJ e explica erro).
- **J (jornada de assinatura completa)**: fixture NDA (8 campos manual+confirmado, documentos
  sintéticos válidos), gerar versão, PDF completo, solicitação de revisão ao operador (login com
  MFA), aprovação interna, preparação confirmada, assinatura em modalidade **evidence** — linha
  persiste modalidade + descrição (rastreável) e a versão fica `externally_signed`.
- **B.8/B.9**: banner de modalidades reais na ficha (nenhum envelope enviado, franquia intacta);
  central de documentos exibe "Concluído — disponível para download".
- **B.10**: exatamente **um** `signature_completed` na outbox, amarrado ao ato por `source_id`
  determinístico (SHA-256 de `signature.completed|preparação|versão`, reproduzido pelo script);
  destinatário = criador da preparação; replay do ato avisa e não duplica; transporte local
  devolve notificação renderizada com assunto genérico fixo, mencionando o destinatário sem segredos.
- **X (escopo OCR honesto)**: PDF escaneado 25 páginas → `ready_for_review` com `total=25,
  processed=20, truncated=true` e banco/API concordando; docx não fabrica paginação; imagem única
  reporta 1 de 1.
- **T/N (códigos de falha estáveis)**: OCR pendurado → `extraction-timeout` (distinto de
  `extractor-failed`); PDF sem renderer → `pdf-no-text-layer`; imagem sem OCR →
  `extractor-not-configured`; worker principal de volta íntegro.

Capturas (prova em navegador): `scripts/qa/capture-d34-screens.ps1` +
`scripts/qa/cdp-capture-v2.mjs` (msedge headless, sessões MFA reais) produziram **100 PNGs** —
5 telas da jornada × 360/768/1280/1440 + zoom 200% × 4 idiomas — em
`%TEMP%\opencode\evidence\captures-d34\` (`CAPTURES-DONE ok=100 fail=0`). Ficam fora do
repositório (tamanho) e são regeneráveis em um comando a partir do estado da rodada final.

Separação explícita dos tipos de evidência (regra do portal):

- **Teste local**: builds/suítes (GATE acima e seção Sprint 1 do STATUS.md).
- **Prova ao vivo (esta seção)**: script reproduzível contra base descartável.
- **Prova em navegador**: capturas CDP das telas renderizadas.
- **CI**: **não verificada** para estes SHAs — branch sem push oficial; nenhum ID de execução inventado.
- **Aceite humano**: pendente — nenhum responsável ou aprovação inventada.

## E — Matriz de estreia

Cada item do escopo de estreia com a evidência que o comprova na rodada final (rodada 10→6 em
`evidence/`; nenhum percentual inventado):

| Item da estreia | Evidência (check da prova A–N) | Status |
|---|---|---|
| OCR parcial nunca aparece como integral | X1-1 `total=25 proc=20 truncated=true`; X1-2 banco/API concordam | provado |
| Códigos de falha distintos e estáveis (timeout ≠ falha ≠ camada de texto) | T1 `extraction-timeout`; N1 `pdf-no-text-layer`; N2 `extractor-not-configured` | provado |
| docx/imagem sem paginação fabricada | X2-1/X2-2 `(null,null,false)`; X3 `(1,1,false)` | provado |
| Assinatura real sem fornecedor inventado (sessão/evidência) | J5/J6/J7 + B8 (nenhum envelope enviado, franquia intacta); decisão no DECISIONS-LOG | provado |
| Assinatura vinculada à versão exata | J6 `confirmedPdfSha256` no ato; J7 linha com modalidade+descrição; B9-1 `externally_signed` | provado |
| Notificação sem duplicata e sem segredos no assunto | B10-1..B10-5 (um evento por preparação+versão; assunto fixo "Assinatura concluída") | provado |
| Paciente dentro da jornada, com proteção de dados confirmados | B6-1..B6-6 (snapshot imutável, autofill só não confirmados, `patient.linked`), B7-1..B7-3 | provado |
| Arquivamento com motivo + versões imutáveis + gate de perfil | B5-1..B5-6 | provado |
| Catálogo único de planos (sem fantasma) | F1 `basic\|enterprise\|intermediate` | provado |
| Diagnóstico honesto para superadmin (estados 4 níveis, sem valores) | A2..A7 | provado |
| MFA exigido para superadmin (API + web) | A0b/A1 (inscrição+desafio TOTP), J3 (operador com MFA), W1 (cliente sem MFA segue direto) | provado |
| Envelope externo exibido como indisponível (decisão registrada) | A4 | provado |

Itens que dependem de fora do repositório (registrados como pendência, não como entrega):

- **CI oficial**: branch ainda sem push — nenhuma execução CI verificada para estes SHAs.
- **Aceite humano** dos fluxos da jornada (superadmin/cliente/operador) — capturas disponíveis
  para revisão.
- **Envio externo real** (SMTP/envelope comercial) — sem homologação, por decisão registrada.

## Configuração (sem novos segredos; arquivo de desenvolvimento untracked)

- Nenhuma nova chave obrigatória. Chaves em uso pelo incremento (padrões existentes):
  `Documents:OcrExecutable`/`PdfRenderExecutable`/`PdfTextExecutable`/`OcrMaxPages` (D-OC32/33).
- Executáveis externos continuam fora do repositório (poppler/Tesseract no padrão do scanner),
  sem caminhos ou credenciais sensíveis em telas e relatórios.

## Arquivos e migrations

- `database/odca.sql` / `database/releases/odca-v057.sql`: v057 estendido e ancorado
  (checksum `d5d58743…dd7b13`), `CurrentVersion=57`; prefixos 1..56 imutáveis.
- Worker/API/Web/BFF conforme F2/B.5/B.6/B.7/B.8/B.9/B.10/A.3; catálogos ×4 idiomas (11 chaves
  novas) com paridade.
- Testes: `TemplateCatalogPersistenceTests.cs` (arquivar exige motivo; estado `archived|razão`;
  versão imutável; segundo arquivamento → Conflict) e `MigrationChecksumTests.cs` (v057 com
  `archive_reason`).

## Lições registradas (bugs encontrados em smoke/prova)

- **Redirect 307 http→https devora o header Authorization no Invoke-WebRequest (PS 5.1).** Com
  listeners duais (`https://localhost:7143;http://localhost:5143`), o `app.UseHttpsRedirection()`
  responde o HTTP puro com 307 para o HTTPS; o IWR segue o redirect e **saca o header
  `Authorization` no caminho** — todo endpoint autenticado volta 401 com desafio vazio
  (`WWW-Authenticate: Bearer`, sem `error_description`). Sintoma enganoso: login (anônimo) passa,
  `mfa/enrollment`/logout/cada POST autenticado falha. Provação: mesmo token aceito em 200 quando
  chamado direto no https (sem redirect). Cura na prova local: expor os serviços **apenas no
  http puro** durante a sessão (padrão do D-OC31/b345) ou apontar o cliente direto no https — o
  que o driver de capturas (node `fetch`) e o curl não reproduzem, porque seguem headers inteiros.
- **Serviços não sobrevivem ao fim da chamada de ferramenta.** Processos dotnet criados via
  `Start-Process` dentro de um shell do opencode são colhidos junto com o grupo quando a chamada
  termina — a stack "no ar" existe apenas enquanto o script que a subiu estiver vivo. Consequência
  prática: cada script (prova, sondagens, capturas) sobe o que precisa internamente; nada de
  "stack persistente" entre passos.
- **Expressão `(if ...)` em posição de valor: parse OK no PowerShell 7, falha silenciosa no 5.1.**
  `X = (if ($c) { 'a' } else { 'b' })` é expressão válida só no PS 7+; no powershell.exe 5.1 o
  parser **aceita** o arquivo (o check de sintaxe dá PARSE-OK) e, em tempo de execução, trata `if`
  como comando ausente (`O termo 'if' não é reconhecido`) — erro não-terminante que deixa a
  variável vazia e segue o script. Efeito em cascata na rodada de prova: o `Location` do POST
  `/entrar` veio vazio, a branch do desafio MFA do superadmin nunca executou e os checks da página
  de diagnóstico falharam por sessão sem MFA (e não por defeito do produto). Padrão adotado nos
  scripts de prova: atribuição explícita antes (`$loc=''; if (...) { $loc = ... }`) ou subexpressão
  `$(if ...)`; nunca `(if ...)` solto na posição de valor.
- **Arquivo `.ps1` sem BOM: literais acentuados corrompidos no PowerShell 5.1 (e HTML com acento
  viram entidade).** O powershell.exe 5.1 lê script sem BOM como ANSI/cp1252 — `'Diagnóstico'` vira
  `'DiagnÃ³stico'` e nenhuma comparação casca (parse OK, erro só em runtime, silencioso). Separado
  disso, o Razor escreve acentos como `&#xE1;` etc. nos trechos codificados. Cura dupla nos scripts:
  salvar os `.ps1` **com BOM UTF-8** e passar a página por `[Net.WebUtility]::HtmlDecode` no
  `Web-Get`. Sintoma característico: checks com agudo/cedilha falham, os mesmos checks em ASCII
  passam.
- **Máquina de desenvolvimento compartilhada: processo dotnet pode morrer sem evento (nem WER,
  nem OOM no System log, nem exceção no stdout).** Na rodada 3 da prova a API sumiu entre duas
  fases sem deixar rastro (concorrência de outros devs/projetos na mesma máquina). Postura adotada:
  a prova revalida a API com `Ensure-Api` (health + restart se morto) antes das fases críticas e
  registra explicitamente no log quando reinicia — instabilidade de ambiente fica separada de
  defeito de produto na leitura da evidência.
- **MFA tem anti-replay por timestep TOTP** (`MfaService`): código com timestep `<=` o último
  aceito é rejeitado como "já utilizado" — a confirmação da inscrição do boot conta. Quando ela e o
  desafio caem na mesma janela de 30 s (rodadas rápidas), o mesmo código volta 400. Retry correto:
  esperar a próxima janela antes de reenviar (código fresco), com captura do corpo da resposta em
  falha para a mensagem de erro virar evidência. O loop de retry deve **esperar o fim da janela
  atual**, não contar iterações de sleep fixo: 12 skips de 2 s (~22 s) esgotavam as tentativas
  dentro de uma única janela (bug próprio do driver de capturas, curado em
  `scripts/qa/cdp-capture-v2.mjs`).
- **Polling grosso mascara latência real**: `Start-Sleep -Milliseconds 5000` no loop de detecção
  de fim do curl somava ~5,1 s a *toda* chamada observada (rede medida em ~50–70 ms). Reduzir para
  300 ms devolve timing verdadeiro aos traces.
- **Módulo de revisões só existe habilitado no enterprise v2** (`plan_entitlements`: basic/intermediate
  v2 declaram `module.reviews = f`) — a jornada J precisa dele; fixture determinística ativa a
  assinatura do tenant no enterprise v2 (ativação ≠ pagamento; catálogo intacto). Sem isso o
  BFF redireciona de volta à versão sem criar revisão (302 enganoso de sucesso).
- **Contato é obrigatório na composição de assinatura**: participante sem e-mail nem telefone
  falha com 400 `participants[0].contact` — o BFF devolve 302 (redirect back) e quem espera a
  criação da preparação vê `row=` vazio.
- **E-mail por tipo de notificação é opt-in**: `odca.notification_channel_state` dá
  `channel_email = false` como padrão — sem preferência explícita, o ato de negócio commita evento
  e auditoria mas a outbox fica vazia *sem erro* (apenas warning de log). Fixture dá opt-in do
  criador para `signature_completed`.
- **O payload da outbox é protegido (DataProtection)**: não contém os ids em texto puro. A
  atribuição da linha ao ato é pela `source_id` determinística
  (`SHA-256('scope|id|id')[0..16]`, `NotificationEnqueue.DeterministicSource`) — reproduzível fora
  do processo e imune a replay.
- **O diretório do FilePickup vem da runtime config em uso** (`development-runtime.json`, caminho
  absoluto sob `%TEMP%`), não do `appsettings.Development.json` relativo: a prova lê o diretório do
  JSON que efetivamente carrega e o limpa na purga (arquivo de rodada anterior cascaria com o
  check do assunto).
- **PS 5.1 corrompe argumento nativo com aspas duplas + espaços** (psql `-c "SQL 'com aspas'"`):
  SQL com aspas internas sempre via `psql -f arquivo` ou pipe de stdin; e `Fn "a" + $v` liga o
  `+` ao RETORNO da função — parentizar a concatenação no ponto de chamada.
- **Purga precisa auditar tabelas sem `tenant_id`**: `support_session_events` e
  `privacy_request_events` só existem via referência (sessão/requisição do tenant); DELETE genérico
  por `tenant_id` falhava nelas (auditoria das 49 tabelas via `information_schema`).
- **wkhtmltopdf cria página vazia extra** quando o último bloco tem `page-break-after: always`
  (PDF 26 em vez de 25): quebra somente entre páginas.
- **Regra de processo**: nunca rodar a suíte de integração em paralelo à prova A–N — o runner mata
  todo `dotnet.exe` cuja CommandLine contenha a raiz do repositório (derrubou api+web da prova e
  produziu falhas em cascata, classificadas na rodada 10).

## Dependências restantes / próximo backlog

- Purga física após a retenção dos órfãos em `reconciled/{yyyy-MM}/` (aberta desde o D-OC32).
- Idiomas de OCR além de `por` (traineddata + `Documents:OcrLanguage`).
- Homologação real de envio externo: SMTP com credenciais e envelope comercial escolhido.
- Deploy físico multi-instância.

## Evidências (reproduzíveis)

No repositório:

- `evidence/d34-proof-an-rodada-final.txt` — transcript completo da rodada final A–N
  (todos os checks PASS + marker `PROVA-FIM fails=0`).
- `evidence/d34-proof-an-rodada10-falhas-classificadas.md` — classificação das 31 falhas da
  rodada 10 (causa raiz + cura por grupo) com adendos das rodadas seguintes.
- `scripts/qa/validate-d34-an-web.ps1` — prova A–N reproduzível em um comando (purga + fixtures
  + stack + segmentos A→N + marker).
- `scripts/qa/capture-d34-screens.ps1` + `scripts/qa/cdp-capture-v2.mjs` — capturas CDP das telas
  da jornada (sessões MFA reais, msedge headless).

Fora do repositório (máquina local, `%TEMP%\opencode\`):

- `evidence\d34-proof-a.txt` (transcript vivo) e `d34-proof-fim.txt` (marker `PROVA-FIM fails=0`).
- `d34-a1-trace.log` (marcas WC-inicio/WC-done por curl), `d34-proof-run{,-err}.log`,
  logs dos serviços (`d34-svc-{api,web,worker,worker-final,wtimeout,wnoocr}.log`).
- `evidence\captures-d34\` — 100 capturas PNG da rodada final (log em `d34-captures.txt`).
- `odca-notification-pickup\` (transporte local de e-mail).

Dados sintéticos em todos os artefatos; sem credenciais ou valores pessoais. CI: não verificada
(sem push oficial — ver §D). Aceite humano: pendente.
