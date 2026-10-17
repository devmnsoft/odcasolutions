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
  `extractor-failed` (executável ausente/falhou) e `pdf-no-text-layer` (PDF só de imagem sem
  renderer) — centralizados, retratáveis até o limite de tentativas.
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

[consolidação da rodada final de A–N — `%TEMP%\opencode\evidence\d34-proof-a.txt`]

## E — Matriz de estreia

[consolidação — cada item com evidência apontada; sem percentuais inventados]

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

## Dependências restantes / próximo backlog

- Purga física após a retenção dos órfãos em `reconciled/{yyyy-MM}/` (aberta desde o D-OC32).
- Idiomas de OCR além de `por` (traineddata + `Documents:OcrLanguage`).
- Homologação real de envio externo: SMTP com credenciais e envelope comercial escolhido.
- Deploy físico multi-instância.

## Evidências (fora do repositório, em `%TEMP%\opencode\evidence\`)

[consolidação ao final]
