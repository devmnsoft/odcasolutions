# Entrega — Jornada documental MVP (ciclo de 2026-10-06, pós-Fases 1–3)

Data: 2026-10-06 · Branch: `codex/s00-foundation` · Base: `8ac10dd` (Fases 1–3: MFA completa, migrações v036/v037, Block B e UX) · Build: Debug do repositório com 0 avisos e 0 erros (binários rodantes deste estado)
Ambiente: PostgreSQL local em 5432, banco `odca_test_disposable`, schema v037 (checksums intactos); API `https://localhost:7143`, Web BFF `https://localhost:7144`; stack iniciada às 13:49 local (`Odca.Api` PID 29684, `Odca.Web` PID 31504), health `{"status":"healthy"}` reconfirmado no início do ciclo.
Tenant de demonstração (TC): `20000000-0000-4000-8000-000000000001` ("Cliente Teste ODCA").

Convenções: segredos mascarados (`abcd••••`); valores crus somente nos arquivos locais da sessão (`C:\Users\NCELL-DEV-020\AppData\Local\Temp\opencode\`, fora do repositório). Sucesso é declarado apenas com evidência executada (API + banco), nunca por presença de tela/endpoint/build.

**Escopo decidido para este ciclo (registro formal):** OCR, importação e upload de modelos/contratos **fora de escopo agora** — devem ser retirados do menu do cliente e bloqueados nos endpoints de servidor quando os menus forem implementados (seção B). O aceite de arquivo PPTX no fluxo do superadministrador é entrega futura (o código ainda não contém qualquer referência a `pptx`); o modelo "Clínica Viva Mais" foi generalizado como conteúdo estruturado (seção 0, GATE 2).

---

## 0. Gates de aceitação do ciclo (estado e evidência)

| Gate | Critério | Estado | Evidência |
| --- | --- | --- | --- |
| GATE 1 | Login + MFA completo (segredo/QR/TOTP, tolerância ±1 step, anti-replay, recovery codes, chaves de Data Protection persistidas, restart) | **aprovado** (inclui regressão pós-restart) | Seção 0.1 |
| GATE 2 | Superadministrador cria/salva/publica modelo pelo estúdio, com persistência e auditoria | **aprovado** nesta sessão | Seção 0.2 |
| GATE 3 | Cliente cadastra paciente e cria rascunho de contrato persistido a partir de modelo publicado | **aprovado** nesta sessão | Seção 0.3 |

> **Nota de estado (atualização pós-suíte de integração):** as linhas canônicas originais dos GATES 2 e 3 foram apagadas da base de homologação pela execução da suíte (wipe do tenant demo em `DevelopmentAccessProvisioningTests.ResetScenarioAsync` — causa raiz e recuperação detalhadas em **B.2**). Os IDs citados nas subseções 0.2/0.3 permanecem como evidência histórica do momento da aprovação. O cenário foi **recriado via API** e re-validado (persistência + auditoria + GET autenticado). Novos IDs canônicos vigentes: template `261516a2-02ef-43bb-abdc-8bcf48701d5a`, paciente `75c87013-4595-41dd-93d9-ac2e9c1f8fc4`, rascunho `35cfbd2f-433b-4398-9064-723dafb1c3d6`, contrato `b0139659-75b7-4782-a250-ae41d29490e1`.

### 0.1 GATE 1 — login + MFA (com regressão pós-restart)

- Login do superadministrador: `POST /api/v1/auth/login` → 200 `requiresMfaChallenge=true`, `security_version=7`.
- Desafio TOTP com o segredo restaurado (Data Protection persistida em `AppData\Local\ODCA Solutions\data-protection-keys`): `POST /api/v1/auth/mfa/challenge` → 200 `mfaVerified=true`.
- Janela de aceitação: passo atual ±1, passo estritamente maior que o último aceito (anti-replay provado na Fase 1); inválido × expirado com mensagem única; 8 recovery codes uso único/hash no banco; inscrição/reinserção exercida com QR local e chave manual (incluindo zeros à esquerda).
- A regressão pós-restart (stack reiniciada às 13:49) passou **no primeiro ciclo de execução limpo**; as falhas intermediárias eram do tooling de teste, não do servidor (item A.3).
- Nota de desenho confirmada por leitura de código: endpoints de tenant (estúdio/pacientes) exigem somente a política "PasswordChanged"; MFA confirmado é exigido apenas em endpoints de plataforma — por isso o usuário cliente opera sem MFA, sem que isso afete o GATE 1.

### 0.2 GATE 2 — modelo do superadministrador (criar → salvar → publicar)

Modelo generalizado a partir do PPTX "Clínica Viva Mais" (`docs/references/contrato-acompanhamento-terapeutico-multiplas-terapias-viva-mais.pptx`), sem reutilizar CPF/CNPJ/endereços do exemplo e sem OCR; campos mapeados por origem canônica (`organization.displayName`, `patient.fullName`, `patient.identifierValue`).

| Etapa | Requisito | Resposta | Estado persistido |
| --- | --- | --- | --- |
| Criar (1ª etapa do ciclo, 14:10) | `POST .../studio/templates` | 201 | `contract_templates` id `b4f11df0-5490-4491-a596-8538d293ab97`, draft v1, `row_version=1`; audit `template.created` (success, scope=private) |
| Salvar (PUT) | `PUT .../studio/templates/{id}` com `expectedVersion=1` (descrição revisada; content/fields byte-exatos do create) | 200 `{version:1, rowVersion:2, status:"draft"}` | `row_version` 1→2 (validação estrutural repassada; sem conflito) |
| Publicar | `POST .../publish?expectedVersion=2` | 200 `{version:1, rowVersion:3, status:"published"}` | `status=published`, `published_at` preenchido no template **e** na linha de versão; audit `template.published` (success) |

Verificação direta no banco (executada após o publish):
`contract_templates`: `status=published`, `current_version=1`, `row_version=3`, `published_at=06/10/2026 17:17:05` (TZ do banco) · `contract_template_versions`: versão 1 com `published_at` preenchida · `audit_events`: exatamente `template.created` + `template.published`, ambas `success`, `scope_type='tenant'`.

Observação honesta: a semântica de `expectedVersion` em PUT e publish compara contra `row_version` (não `current_version`) — comportamento lido do código em `ContractStudioController.cs` e confirmado empiricamente (conflito 409 esperado em retry com versão defasada). O aceite de arquivo PPTX pelo navegador do superadmin ainda é pendente (futura entrega; o payload acima usa os endpoints canônicos do estúdio).

### 0.3 GATE 3 — paciente + rascunho persistido (conta cliente)

| Etapa | Requisito | Resposta | Estado persistido |
| --- | --- | --- | --- |
| Login | `cliente.teste@odca.local` via `POST /api/v1/auth/login` | 200 + token | — |
| Paciente | `POST .../patients` (`fullName`, e-mail, telefone, endereço, nascimento 12/04/1985, CPF válido) | 201 id `96f2f892-02b9-47d9-ab9a-b916cb1443b6` | `odca.patients`: nome completo, `identifier_type=cpf`, `identifier_value=11144477735`, `identifier_normalized=11144477735`, ativo, `row_version=1` |
| Rascunho | `POST .../studio/drafts` (template publicado `b4f11df0-…`, título, `patientId`) | 201 `{id: bb16de93-17c2-49b6-8aa1-ff3227eac75f, contractId: 6cc5408e-eaef-4333-8268-fabc72751024}` | `odca.contract_drafts` (correto `source_template_id`, `patient_id`, `row_version=1`) + `odca.contracts` (título "Contrato Ana Paula F. - Homologacao GATE 3", tenant TC) |
| Confirmação | `GET .../studio/drafts/{draftId}` | 200 (título conferido, `version=1`) | — |

O rascunho só nasce de template **publicado** (regra do estúdio respeitada: o mesmo template em draft teria sido recusado). Nada foi emitido, assinado ou movido além do rascunho — estados posteriores permanecem o escopo da seção B.

---

## A. Correções (falhas registradas do ciclo: ação / expected / observed / causa raiz / correção / regressão)

### A.1 — Login 401 das contas demo (desincronização arquivo local × hash no banco)

- **Ação**: login de `admin@odca.local` e `cliente.teste@odca.local` via API após o restart da stack (13:49), usando as senhas do arquivo local de credenciais de desenvolvimento.
- **Expected**: 200 com `accessToken` (admin com `requiresMfaChallenge=true`).
- **Observed**: 401 nas primeiras tentativas, mesmo com credenciais "documentadas" no arquivo local.
- **Causa raiz**: o arquivo `development-credentials.json` guarda senhas que **não correspondiam** ao hash persistido no banco. A suíte de integração, ao reexecutar, regrava senha/MFA dos usuários de demonstração na ordem dos facts; a última execução terminou com valores diferentes dos do arquivo local. O `show-login` (que consulta o banco) e o `provision-test-access` revelam a senha real — o arquivo, não.
- **Correção**: rotação explícita e única: `dotnet run --project src\Odca.Bootstrap -- provision-test-access --environment Development --rotate-passwords --allow-immediate-login`. Admin e cliente voltaram às senhas canônicas (`V7!q••••` / `K8@w••••`); o operador recebeu nova senha por rotação (`!4cV••••`) — sem alteração silenciosa de credenciais fora dessa rotação registrada.
- **Regressão**: logins subseqüentes de admin (GATE 1 e GATE 2) e de `cliente.teste` (GATE 3) com as senhas canônicas — todos 200; MFA do admin confirmado com o segredo persistido. Nenhum outro usuário/touchpoint foi modificado.

### A.2 — MFA `regenerate` 403 em conta já confirmada

- **Ação**: durante a regressão pós-restart, tentativa de re-inscrição/regeneração do segredo TOTP no admin **já confirmado** (`MfaConfirmedAt != null`).
- **Expected** (na hora): novo segredo + QR.
- **Observed**: recusa (enrollment indisponível).
- **Causa raiz**: **por design** — `MfaService.IsEnrollmentAvailableAsync` só libera enrollment enquanto `MfaConfirmedAt is null`. Conta confirmada deve passar pelo reset explícito (`reset-mfa`), não por regeneração silenciosa.
- **Correção**: nenhuma mudança de código; comportamento documentado. O ciclo de vida completo (reset → re-inscrição → confirmação) foi exercido no ciclo do GATE 1, com snapshot byte-exato do estado para rollback.
- **Regressão**: GATE 1 continua verde após o reset/restauração; nenhum login bloqueado; recuperação por recovery code intacta.

### A.3 — Falhas falsas no teste automatizado pós-restart (tooling, não servidor)

Três falhas distintas ocorreram antes da execução limpa do teste de restart; todas foram do tooling de teste, **não** do servidor:

| # | Ação | Observed | Causa raiz | Correção |
| --- | --- | --- | --- | --- |
| 3.1 | Script de login+TOTP (`mfa-after-restart.ps1`) | erro de invocação antes de chegar ao servidor | `curl.exe` externo removido do temp de trabalho da sessão | re-cópia do `curl.exe` para `AppData\Local\Temp\opencode\` |
| 3.2 | Mesmo script via `powershell -Command "…"` | variáveis `$` interpoladas/perdidas, bodies JSON malformados | PowerShell 5.1 expande `$` dentro de strings `-Command`; quoting de JSON com chaves/símbolos | padrão fixo: `-File` (nunca `-Command` com `$`) + bodies JSON sempre via arquivo (`--data "@file"`), gravados ASCII/UTF-8-sem-BOM |
| 3.3 | Chamada de TOTP auxiliar (`odcatools`) | subcomando rejeitado | mudança de CLI não propagada: `code` passou a receber o segredo **posicional** (`odcatools.dll code <segredo>` → `CODE=NNNNNN`), sem flags; projeto migrado p/ net10.0 (net9.0 obsoleto) | rebuild do `odcatools` (net10.0) + novos subcomandos `status`/`decode-secret`/`admin-token`/`snapshot`/`restore`/`q`; scripts atualizados |

- **Expected** (do teste como um todo): login 200 → desafio TOTP 200 `mfaVerified=true`.
- **Regressão**: com o tooling corrigido, `mfa-after-restart3.ps1` passou end-to-end (login 200 `requiresMfaChallenge=true`, `security_version=7`; challenge 200 `mfaVerified=true`) — GATE 1 aprovado. As falhas 3.1–3.3 **não** geraram nenhuma alteração no servidor.

### A.4 — Splat `-H @auth` no script do GATE 2 (401 + saída 3 do curl)

- **Ação**: reaproveito do array `$auth = @('-H', "Authorization: Bearer $token")` com splatting contra o `curl.exe` nativo para o PUT/publish do template.
- **Expected**: requisições autenticadas 200.
- **Observed**: 401 combinado com código de saída 3 do curl (falha de URL) — header de autorização não chegava corretamente e a URL era montada errada sob o splat.
- **Causa raiz**: splatting de array contra comando nativo no PowerShell 5.1 concatena argumentos de forma diferente do esperado (expansão/quoting do valor do token), diferente do comportamento no provider PowerShell.
- **Correção**: header sempre explícito `-H "Authorization: Bearer $token"` (sem splatting); padrão adotado em `gate2-complete.ps1` e `gate3.ps1`.
- **Regressão**: com o padrão direto, o ciclo completo do GATE 2 (create→PUT→publish) e todo o GATE 3 passaram verdes no primeiro loop — sem tocar em código do produto.

### A.5 — 404 falsos nos scripts de smoke do B.3.2b (artefato do lexer PowerShell 5.1, não do framework)

- **Ação**: scripts de smoke chamando os endpoints de contrato com a query string montada por interpolação direta após a variável da URL — o caso concreto foi a linha T5, `".../$dup?version=1"`.
- **Expected**: URL íntegra `.../contracts/{id}?version=1` chegando ao servidor (200, ou 409 de conflito no caso previsto).
- **Observed**: 404 sempre que o `?` vinha imediatamente após `$var`; o middleware de repro registrou a chegada como `.../contracts/=1` — o guid havia desaparecido. A mesma URL montada por literal puro respondeu 200 no mesmo processo.
- **Causa raiz** (confirmada por matriz experimental `dig1`–`dig4` + prova wire em `wire-truth.ps1`): em string entre aspas duplas do PowerShell 5.1, a expansão `$var` seguida imediatamente por outros caracteres faz o parser absorver esses caracteres no nome da variável — medido: `"$c?x"` → `""`, `"$c? x"` → ` x`, `"a$c?x"` → `a`, `"$tenant?status=active"` → `=active`. O nome composto não existe, expande para vazio, e o fragmento inteiro some da URL **antes** do request ser enviado. Não é framework .NET 10, Kestrel, curl, proxy nem codificação no disco: o byte dump do `.ps1` chega íntegro (dump de char codes) e os controles montados corretamente chegam íntegros ao middleware com 200.
- **Correção** (workaround obrigatório em todo `.ps1` novo): nunca escrever `"$var?k=v"`. Formas aprovadas: `$($var)?k=v`, concatenação explicita `"...$var" + '?k=' + $v`, ou literal/espaço antes do `?`. Regra complementar adotada neste ciclo: logar a URL final imediatamente antes de cada request.
- **Regressão**: linha T5 corrigida para concatenação; smoke B.3.2b T1–T12 **ALL PASS** na stack real (7143), estável em 3 execuções. A narrativa intermediária de "bug do framework .NET 10" é formalmente descartada por este item.

### A.6 — 503 "The view 'List' was not found" na lista de contratos (Web B.3.1)

- **Ação**: logado como cliente, acessar `/organizacoes/{TC}/contratos` com o código do B.3.1 na stack.
- **Expected**: 200 com a lista de contratos renderizada (a API respondeu 200 para `GET .../contracts?` no mesmo request).
- **Observed**: 503 "Página indisponível" (`BffErrorHandlingMiddleware[3002]`); log `ViewResultExecutor[3] The view 'List' was not found. Searched locations: /Views/Contracts/List.cshtml, /Views/Shared/List.cshtml`.
- **Causa raiz**: a ação `List` retornava `View(model)`; sem nome explícito, o MVC resolve a view pelo nome da **ação** (`List`) — o arquivo no disco é `Index.cshtml`.
- **Correção**: `return View("Index", new ContractListViewModel …)` em `ContractsController.List`; rebuild de `Odca.Web` (0 avisos / 0 erros) + restart da stack.
- **Regressão**: suíte B.3.1 completa 35/35 PASS com a lista em 200 e contagens corretas; nenhuma outra rota afetada (ficha/inbox/documentos ok).

### A.7 — Falhas do harness de validação web do B.3.1 (tooling, não produto)

| # | Ação | Observed | Causa raiz | Correção |
| --- | --- | --- | --- | --- |
| 7.1 | Login em `/account/entrar` | 405 | `AccountController` **não tem** `[Route]` de controller; as ações estão na raiz: `GET/POST /entrar`, `/sair`, `/alterar-senha` | URL do script corrigida para `/entrar` |
| 7.2 | POST do login sem token | 400 vazio (antes da ação) | `AutoValidateAntiforgeryTokenAttribute` registrada globalmente (`Program.cs`, linhas 14–15): exige GET anterior (cookie antiforgery) + `__RequestVerificationToken` no body form; falha antes de contar no rate limit | fluxo GET → extrair token via regex → POST com o token no body |
| 7.3 | Asserts com acento sempre falhando ("Renovações", "Obrigações"…) | FAIL + `?` no console | PowerShell 5.1 lê `.ps1` sem BOM como ANSI/CP1252: o literal acentuado era corrompido **dentro do próprio script** ("Renovações" → "RenovaÃ§Ãµes"); HTML no disco e na rede estava íntegro (hexdump: `C3 A7 C3 B5` em "Operações") | `validate-b31-web.ps1` salvo como UTF-8 **com** BOM (`add-bom.ps1`); asserts com acento passaram contra o HTML real — regra consolidada: scripts com literais acentuados sempre UTF-8+BOM |
| 7.4 | Probe de readiness reportando "FAIL API nao ficou pronta" | falso negativo (API saudável) | o endpoint responde `{"status":"healthy"}`, não `"status":"ready"`; e `-SkipCertificateCheck` inexistente no `Invoke-WebRequest` do PS 5.1 | probe migrado para `curl.exe -sk` + padrão `"status":"healthy"` (`wait-ready-b31.ps1`) |

Regra complementar adotada neste harness: truthiness `if ($body)` em vez de `$null -ne $body` — parâmetro `[string]` omitido chega como string vazia, não `$null`, no PS 5.1. Nenhuma das falhas acima gerou mudança no produto (a real ficou no item A.6).

**Síntese da seção A**: 7 registros; 2 correções reais (A.1 credenciais demo; A.6 view da lista de contratos), 1 desenho documentado (A.2) e 4 falhas de tooling (A.3, A.4, A.5 e A.7 — as duas últimas com regras obrigatórias consolidadas nos scripts de validação deste ciclo).

---

## B. Jornada documental do cliente (8 etapas)

### B.1 — OCR/importação/upload fora do escopo: menu removido + bloqueio no servidor (fechado)

**Decisão de escopo**: neste ciclo o OCR, a importação e o upload de arquivos (modelos e contratos) saem do fluxo do cliente. A medida tem duas camadas: (a) remoção dos itens do menu na Web; (b) bloqueio server-side dos endpoints por feature flag de release (**fail-closed**), para que nenhuma UI futura ou cliente HTTP consiga exercitar o recurso antes da entrega definitiva.

#### B.1.1 Inventário das alterações

| Arquivo | Alteração |
| --- | --- |
| `src/Odca.Api/ReleaseFeatureGate.cs` (novo) | Middleware `ReleaseFeatureGate : IMiddleware`: consulta flags de release por endpoint; se a feature estiver ausente ou `false` na configuração, responde 503 problem+json **antes** de autenticação/autorização (separado do `OrganizationFeatureGate`, que controla módulos por organização) |
| `src/Odca.Api/Program.cs` | Registro DI `AddTransient<ReleaseFeatureGate>()` (~linha 29) e `UseMiddleware<ReleaseFeatureGate>()` **antes** de `UseStatusCodePages()` (~linha 146) |
| `src/Odca.Api/development-runtime.json` | Flags explícitas `Features:ContractImports=false`, `Features:DocumentUpload=false`, `Features:OcrExtraction=false` (ausente ou `false` ⇒ bloqueado) |
| `src/Odca.Web/Views/Shared/_Layout.cshtml` | Removidos os 2 itens do menu do cliente: "Importações de contratos" e "Importações de modelos" |
| `src/Odca.Web/Views/Documents/NewDocument.cshtml` | Card 2 ("Importar documento já assinado ou legado") → estado "disponível em breve", sem envio de arquivo nem cobrança |
| `src/Odca.Web/Controllers/ModulesController.cs` | Texto do módulo `documentos` reescrito para refletir a indisponibilidade da importação nesta versão |
| `src/Odca.Web/Views/Imports/Index.cshtml` | Banner "Disponível em breve" para acesso direto à rota (que continua respondendo) |

Formato da resposta de bloqueio (problem+json, título específico em PT-BR, campo `feature` identificando a limitação): `type=https://odca.local/problems/release-feature-disabled`, `code=release_feature_disabled`, status 503.

#### B.1.2 Evidências HTTP (bateria `gate-b1-check.ps1`)

Executada **duas vezes** — após a implementação e novamente como smoke **pós-restauração** do cenário (B.2), com idêntico resultado — e confirmada uma terceira vez por **tráfego real da Web** após o restart do stack (log do `Odca.Web`: `GET .../contract-imports?` → **503** enquanto pacientes/features/inbox/saved-views seguem 200 para o mesmo usuário logado):

| Caso | Requisito | Resultado |
| --- | --- | --- |
| Listar importações | `GET .../{TC}/contract-imports` (autenticado) | **503** problem+json `release_feature_disabled` |
| Criar importação | `POST .../{TC}/contract-imports` (autenticado) | **503** idem |
| Upload de documento | `POST .../{TC}/contracts/{cid}/documents` multipart | **503** (`feature=document-upload`) |
| Extração OCR (criar/listar/aplicar) | `POST` / `GET` / `apply` de extractions | **503** (`feature=ocr-extraction`) |
| Gate antes do auth | GET de importações **anônimo** | **503** (o gate responde antes de 401 — intencional: feature desligada não depende de identidade) |
| Negativo (recurso ativo) | `GET .../contracts/{cid}/documents` (lista, sem upload) | **200**, sem gate no corpo |

Build da solução inteira: **0 avisos / 0 erros**.

#### B.1.3 Falha real encontrada e corrigida neste item (registro completo)

- **Ação**: subir a stack com o novo middleware e enviar qualquer request.
- **Expected**: 503 nas features desligadas; 200 nos demais endpoints.
- **Observed**: 500 em **todos** os requests.
- **Causa raiz**: `ReleaseFeatureGate : IMiddleware` foi escrito, mas não registrado no service collection — o pipeline resolve o middleware pelo container a cada request e falhava na resolução.
- **Correção**: `builder.Services.AddTransient<ReleaseFeatureGate>();` em `Program.cs`.
- **Regressão**: bateria completa acima re-executada após a correção e novamente após a restauração do cenário (B.2); suíte de integração verde (B.1.4).

#### B.1.4 Suíte de integração

`dotnet test` da suíte executada com `ODCA_TEST_ENVIRONMENT=ODCA_INTEGRATION_TESTS` + connection strings dedicadas (role `odca_test_app_login`): **164/164 aprovados** (1m09s). Esta execução também produziu o efeito colateral documentado em B.2 (reset do cenário demo).

#### B.1.5 Confirmação na UI (Web :7144, logado como `cliente.teste@odca.local`)

Evidência por snapshot de DOM (a janela do browser desktop estava oculta durante as verificações e a ferramenta de screenshot exige janela visível — capturas em pixels ficam pendentes para a seção D; o conteúdo abaixo é o renderizado real conferido página a página):

1. **Menu lateral completo sem nenhum item de importações.** Mapa de hrefs do nav: Visão geral, Primeiros passos, Pacientes, Acervo de documentos, Assinaturas, Modelos, Minhas minutas, Criar documento, Solicitações, Caixa, Agenda, Obrigações, Filtros salvos, Renovações e aditivos, Organização, Equipe e perfis, Plano e consumo, Auditoria, Ajuda, Privacidade.
2. **Rota direta `/organizacoes/{TC}/importacoes`**: banner "Disponível em breve: a Central de importações será habilitada em uma próxima versão. Os documentos e dados já enviados permanecem armazenados e rastreáveis." + alerta da lista refletindo o 503 do servidor.
3. **`/documentos/novo`**: card 2 "Importar documento já assinado ou legado" no estado "em breve" — "…catalogado na Central de Importações, disponível em uma próxima versão. Enquanto o recurso estiver indisponível, nenhum arquivo é enviado e nenhuma cobrança é registrada"; rodapé "Importação de arquivo externo — disponível em breve". Card 1 (elaborar a partir de modelo) intacto.
4. **`/modulos/documentos`**: texto do módulo reescrito renderizado — "A biblioteca consolidada de documentos está em evolução. A importação de arquivos externos estará disponível em uma próxima versão." / "Enquanto a Central de Importações não for liberada, use o estúdio de modelos para gerar novas minutas; os documentos existentes continuam consultáveis."

Observação menor registrada: a rota raiz `/modulos` (sem segmento) não tem action correspondente (o controller define apenas `{module}`) e devolve um documento vazio em vez do 404 padrão; nada no produto linka a rota raiz (o menu aponta para `/modulos/ajuda`). Tratamento estético fica na seção D.

### B.2 — Efeito colateral da suíte: reset do cenário demo + restauração/recriação do cenário canônico

Ao executar a suíte de integração (B.1.4), o cenário canônico dos GATES 2–3 (linhas criadas pela manhã) foi apagado da base de homologação. Causa raiz, recuperação e recriação:

#### B.2.1 Causa raiz do reset

`DevelopmentAccessProvisioningTests.ResetScenarioAsync()` (`tests/Odca.IntegrationTests/DevelopmentAccessProvisioningTests.cs`, ~linhas 237–309) executa, a cada execução da classe, um **wipe da closure transitiva de FKs dos tenants demo** (`business_code IN ('12345678000195','ODCA-DEMO-LOCAL')`) — templates do admin, pacientes/drafts/contratos/versões/imports/obrigações do tenant TC — e reseta senha/MFA dos usuários dev. É comportamento **por design** de isolamento para os testes de provisioning; a consequência prática é que linhas de homologação criadas nesses tenants não sobrevivem à suíte. Não houve truncamento global: linhas fora desses tenants/usuários sobreviveram (27 templates / 17 pacientes / 7 contratos criados antes das 17:00 intactos; tenant TC íntegro e ativo).

#### B.2.2 O que se perdeu e como se recuperou cada pedaço

| Ativo | Estado pós-suíte | Recuperação |
| --- | --- | --- |
| Senhas dos 3 usuários dev | hashes rotacionados; `development-credentials.json` desincronizado → 401 de login | `provision-test-access --environment Development --rotate-passwords --allow-immediate-login` (admin/cliente voltam às senhas de seed; operador recebe nova aleatória; arquivo local reescrito; `security_version` incrementada por conta) |
| MFA do admin (segredo, 8 recovery codes, `security_version`, `must_change_password`) | resetado | snapshot pré-suíte (`mfa-snapshot-b1`) + `odcatools restore mfa-snapshot-b1` (8 recovery codes restaurados; sessões revogadas) + UPDATE de `security_version=8` / `must_change_password=false` + re-provision |
| Template canônico (GATE 2) | apagado | recriado via API (B.2.3) |
| Paciente / rascunho / contrato (GATE 3) | apagados | recriados via API (B.2.3) |

Smoke pós-restauração (3 contas): cliente 200, operador 200, admin 200 + desafio MFA 200 `mfaVerified=true`. `odcatools status` final: `secver=8 mustChange=False mfaConfirmed=True recoveryAvailable=8`.

#### B.2.3 Recriação do cenário canônico via API (novos IDs vigentes)

Fluxo executado (`restore-gates.ps1` / `restore-gates2.ps1` / `restore-gates3.ps1`, payloads UTF-8 escritos fora do PowerShell):

| Etapa | Requisito | Resposta | Estado persistido (confirmado no banco) |
| --- | --- | --- | --- |
| Criar template | `POST .../studio/templates` (superadmin, payload PT-BR limpo) | 201 | `contract_templates` id **`261516a2-02ef-43bb-abdc-8bcf48701d5a`**, draft v1, `row_version=1`; audit `template.created` success (06/10 19:36:04) |
| Salvar (PUT) | `PUT .../studio/templates/{id}` `expectedVersion=1` | 200 `rowVersion=2` | `row_version` 1→2 |
| Publicar | `POST .../publish?expectedVersion=2` | 200 `status=published` | `status=published`, `row_version=3`; audit `template.published` success (06/10 19:36:09); tenant TC passa a ter exatamente 1 template publicado |
| Paciente | `POST .../patients` (`Ana Paula Ferreira da Costa`, CPF válido) | 201 id **`75c87013-4595-41dd-93d9-ac2e9c1f8fc4`** | `odca.patients`: nome, e-mail, CPF normalizado, ativo |
| Rascunho | `POST .../studio/drafts` (template publicado, título, `patientId`) | 201 draft **`35cfbd2f-433b-4398-9064-723dafb1c3d6`** + contrato **`b0139659-75b7-4782-a250-ae41d29490e1`** | `contract_drafts` com `source_template_id=261516a2-…`, `patient_id=75c87013-…`, `contract_id=b0139659-…`, `row_version=1` |
| Confirmação | `GET .../studio/drafts/{draftId}` (conta cliente) | 200 | content/fields/values conferidos byte-exatos: autofill correto (`organization_name=Cliente Teste ODCA` via organization, `patient_name=Ana Paula Ferreira da Costa` via patient), PT-BR íntegro |

Contagens finais no banco: **uma linha por ID** (template=1, paciente=1, rascunho=1, contrato=1). Nota semântica confirmada na prática: `POST .../studio/drafts` cria a linha de contrato **no nascimento do draft** (não existe endpoint de emissão separado; o JOIN com `odca.contracts` já popula `contract_id`) — recriar o GATE 3 = paciente + draft; o contrato é consequência, não etapa.

#### B.2.4 Desvios do processo e seus tratamentos (transparência)

1. **Recidiva do bug de splat (item A.4)**: a primeira execução do script falhou no POST de template porque `-H @auth` (array de 2 elementos) é achatado pelo PowerShell 5.1 contra o curl nativo, e `Bearer <token>` virou argumento extra → `curl: (3) URL rejected`. Correção: header único `-H "Authorization: Bearer $token"` em todos os sites dos scripts de restauração.
2. **Mojibake do primeiro template recriado**: `.ps1` UTF-8 sem BOM é lido como ANSI/CP1252 pelo PowerShell 5.1; acentos viraram `TerapǦutico Ŭ?` no template publicado `a56ea697-9596-47eb-a99d-31f6d6a28aaa`. Tratamento: template limpo (`261516a2-…`) criado com payloads JSON escritos **fora** do PowerShell (UTF-8 byte-exato) e `.ps1` 100% ASCII; validação por GET de API salvo em arquivo e leitura byte-exata. Template antigo **arquivado** via API (`POST .../archive?expectedVersion=3` → 204; o 409 anterior era por omitir `expectedVersion`, parâmetro obrigatório do endpoint de arquivamento).
3. **Limpeza do par v1** (rascunho `6ce38b6b-…` + contrato `4b59d72d-…`, apontando para o template arquivado): não há endpoint DELETE de rascunho; removidos por SQL ordenado pela closure de FK mapeada previamente em `pg_constraint` (change requests, save receipts, generated versions, comments, documents/versions, events, obligations, series, renewal cycles, review requests, extraction jobs/reviews, imports — todos zerados; contagem final 0/0). Banco descartável de homologação; auditoria append-only preservada.

#### B.2.5 Regra operacional adotada (para os próximos ciclos)

Antes de qualquer execução de suíte de integração: **snapshotar também as linhas de negócio do cenário ativo (não só MFA) ou planejar a recriação**. O script de recriação (`restore-gates*.ps1` + payloads) está reaproveitável em `%LOCALAPPDATA%`-independente `Temp\opencode\`; payloads PT-BR devem ser arquivos UTF-8 escritos fora do PowerShell.

### B.3 — Jornada completa do cliente (subplano B.3.1 → B.3.4)

**Ordem de execução (decisão registrada):** B.3.1 menu/permissões (Web) → B.3.2a migração → B.3.2b endpoints de contrato (API) → B.3.2c ações na ficha + impressão "RASCUNHO" + histórico → B.3.3 central de renovações → B.3.4 campos por perfil de atuação.

| Subitem | Estado | Registro |
| --- | --- | --- |
| B.3.1 menu do cliente + permissões (Web) | **fechada** (07/10) — suíte web 35/35 ALL PASS na stack real | B.3.1 abaixo |
| B.3.2a migração m038 | **fechada** | B.3.2a abaixo |
| B.3.2b endpoints de contrato (API) | **fechada** — smoke T1–T12 ALL PASS + persistência no banco | B.3.2b abaixo |
| B.3.2c ações na ficha + impressão "RASCUNHO" + histórico | **fechada** (07/10) — suíte web ALL PASS (T/B/W/C/H/O/R) + persistência no banco + migração v039 | B.3.2c abaixo |
| B.3.3 central de renovações | em aberto | B.3.3 abaixo |
| B.3.4 campos por perfil de atuação | em aberto | B.3.4 abaixo |

#### B.3.1 — Menu do cliente + permissões (Web) (fechada em 07/10/2026)

**Implementação** (código concluído neste ciclo; validação executada na stack real após os itens A.6/A.7):

| Arquivo | Alteração |
| --- | --- |
| `src/Odca.Web/Controllers/ContractsController.cs` | ação `List` com rota absoluta `[Route("~/organizacoes/{tenantId:guid}/contratos")]` (o template do controller carrega `{contractId}`; ação sem rota absoluta geraria `RoutePatternException` por parâmetro repetido); acesso via `GetAccessAsync` (sem vínculo ativo ⇒ `Forbid`); normalização de status (`active`/`archived`/`closed`/`all`; valor desconhecido ⇒ `active`); paginação com mínimo 1 |
| `src/Odca.Web/Views/Contracts/Index.cshtml` | lista "Meus contratos": busca por título/referência, filtro de status, linhas com badge de estado (Ativo/Arquivado/Encerrado), paginação que preserva filtros, botão "Novo contrato" (→ `/documentos/novo`) |
| `src/Odca.Web/Views/Shared/_Layout.cshtml` | menus do cliente e da plataforma completos (cliente: Início · Pacientes · Contratos {Modelos disponíveis, Meus contratos, Minhas minutas, Novo contrato} · Assinaturas · Renovações e prazos · Operações {Caixa, Obrigações, Filtros salvos} · Equipe e permissões · Minha organização e plano · Suporte; plataforma: Organizações, Usuários e acessos, Planos e módulos, Biblioteca de modelos, Publicação e versões, Solicitações Enterprise e SLA, Auditoria, Configurações operacionais). **"Solicitações ODCA" é escondido quando o plano restringe a feature `reviews` (`plan_restricted`) nos dois blocos** — não é apenas um sufixo; "Meus contratos" gateia a feature `documents` |
| `src/Odca.Web/Services/OdcaApiClient.cs` | 10 métodos da jornada de contrato (list/detail/generate/PDF/download/duplicate/archive/restore/close/history/update/sign/remind) sobre os endpoints já validados em B.3.2b |
| `src/Odca.Web/Models/…` (`ContractJourneyContracts.cs` + `OperationalWorkspaceViewModels.cs`) | DTOs de contrato + `ContractListViewModel` para o BFF |
| `src/Odca.Web/Controllers/ModulesController.cs` | textos dos módulos atualizados para o novo mapa de navegação |

Renomeações adotadas: "Equipe e perfis" → "Equipe e permissões"; "Renovações e aditivos" → "Renovações e prazos"; grupo antigo "Documentos" removido. Sem alterações de banco neste item (m038 ficou em B.3.2a).

**Validação web (07/10)** — suíte `validate-b31-web.ps1` (em `Temp\opencode\`, fora do repositório) contra a stack real (Web :7144 / API :7143, tenant TC, plano **basic v2**, conta `cliente.teste@odca.local`): **ALL PASS 35/35**:

- Login do cliente em `/entrar` com antiforgery global (GET p/ cookie + token → POST do form) e shell renderizado.
- Menu do cliente íntegro: legenda "Contratos"; Modelos disponíveis, Meus contratos, Minhas minutas, Novo contrato, Acervo de documentos, Assinaturas, Renovações e prazos, Operações, Caixa, Obrigações, Filtros salvos, Equipe e permissões, Minha organização e plano, Suporte; **"Solicitações ODCA" ausente** (plano basic — verificação efetiva após o BOM do script, item A.7); sem "Equipe e perfis", sem "Renovações e aditivos", sem grupo "Documentos".
- Contagens conferidas contra o banco: active=1 (canônico `b0139659-…`), all=11, closed=8, archived=2; status desconhecido normaliza para active (=1).
- Página da lista: `h1`, botão "Novo contrato" renderizado como `href="/organizacoes/{TC}/documentos/novo"`, linha do contrato canônico com badge "Ativo" e link profundo para a ficha.
- Regressões limpas: ficha 200 (34 kB), Inbox 200, Documentos 200, `/modulos/contratos` 200.

**Encoding (encerrado)**: `_Layout.cshtml` e `Index.cshtml` são UTF-8 válido sem BOM no disco e o HTML servido está correto — provado pelos asserts com acento (ex.: "Obrigações") passando contra o HTML real após o script ser salvo como UTF-8 **com** BOM. Os `?` vistos no console são artefato de exibição do codepage PT-BR/CP1252, não do dado.

Notas de escopo: o menu da plataforma (bloco do superadministrador) foi validado em fonte; a execução ponta-a-ponta (com MFA) entra na matriz de homologação da seção D (2 organizações × 4 perfis), assim como as capturas em pixels (janela do browser oculta durante as verificações). Falhas encontradas neste fechamento: **A.6** (correção real) e **A.7** (tooling).

#### B.3.2a — Migração m038 (fechada)

- `m038` anexada a `database/odca.sql` como incremento preservando 001–037; snapshot v038 = cópia integral do canônico; `DatabaseSchema.CurrentVersion=38`; checksum registrado: `6db2939ff8aaa65cb313eb05b67944f93bdf2cdc7f4dc6ee13ca280c260ff203` (SHA256 do body, CRLF→LF, entre pós-header e `-- ODCA-END 038`).
- Aplicada e validada na base de homologação (`odca_test_disposable`); migrador exclusivamente via `dotnet run --project src/Odca.Bootstrap -- migrate`.

#### B.3.2b — Endpoints de contrato (fechada)

**Mecânica implementada** (`src/Odca.Api/Controllers/ContractsController.cs`):

- Todas as mutações exigem `?version=N` (lock otimista): conflito ⇒ 409 `contract.version.conflict` com `currentVersion`; leitura sob `FOR UPDATE`; cada ação grava evento em `contract_events` + registro de auditoria.
- **PATCH** (atualizar): `title` (2..160), `reference` (≤160), `ownerId` (membro ativo do tenant; limpo com flag booleana).
- **Duplicate**: bloqueia fonte encerrada (409 `contract.closed`) e fonte sem minuta (409 `contract.no_draft`); copia header + minuta; título truncado a 152 chars + " (cópia)"; **não** copia versões, obrigações nem assinaturas.
- **Archive/restore**: idempotentes (repetição ⇒ `replayed=true`); arquivar ≠ rescindir; restore preserva o par `closed_*` quando presente.
- **Close**: exige `closedOn` + `reason` (a confirmação em tela é entregue com a ficha, B.3.2c); grava `closed_at`/`closed_by`/`closed_on` + `closure_reason` + `archived_at` (encerrar implica arquivar; restaurar só limpa `archived_*`).
- **Sign/remind**: sign exige preparação confirmada (assina a linha com `composition_revision = confirmed_revision`); idempotente para o mesmo ator (repetição ⇒ `replayed=true`); ator que já assinou ⇒ 409 `participant.already_signed`; última assinatura ⇒ `review_status='externally_signed'` + evento `signature.completed`. Remind tem cooldown de 60 s (409 `reminder.too_soon` + `retryAfterSeconds`). Ambos exigem `tenant.contract_drafts.manage`.
- **Gate Enterprise**: revisão ODCA restrita pela feature `reviews` (`[TenantOperation]` ⇒ 403 `plan_restricted`); os demais recursos resolvem feature nula no catálogo core. O gate responde apenas 403 ou deixa a rota prosseguir — nunca 404.
- **Geração de versão/PDF**: exige valores `Confirmed=true` na minuta (validação estrutural em modo Confirmed); a confirmação entra por `PUT studio/drafts/{id}`.

**Smoke T1–T12 — ALL PASS** na stack real (API 7143), estável em 3 execuções. Cobertura: list active contendo o canônico; detail; generate + PDF; preparação de assinatura; remind 200/409 (cooldown); sign participante 1 (remaining=1); sign participante 2 (allSigned ⇒ `externally_signed`); replay de sign (`replayed=true`); PATCH 200 (version 1→2, owner para o sub `…0003`); archive→restore idempotentes (com replay); close 200; duplicate de encerrado (409); eventos completos; 409 de concorrência (`contract.version.conflict`). O único desvio da bateria foi a URL da linha T5 — causa raiz e correção em **A.5**.

**Persistência confirmada no banco** (schema `odca` @ `odca_test_disposable`, lido via `odcatools`):

- `contracts` (duplicata `d9a77324-…`): `version=5`, `owner_id=10000000-…-0003`, `closed_on=2026-10-06`, `closure_reason` preenchida, `closed_at`/`archived_at` coerentes.
- `contract_events`: 8 eventos na ordem canônica — `contract.duplicated` (fonte = canônico `b0139659-…`), `draft.saved`, `version.generated` (sha `862d4d69…`), `signature.completed`, `contract.updated` (`changed: owner+title`), `contract.archived`, `contract.restored`, `contract.closed` — cada um com `details` jsonb específico.
- `generated_contract_versions` (`decd3a65-…`): `review_status='externally_signed'`, `canonical_sha256=862d4d69…`, 2272 B; `pdf_status='completed'` (sha `6b8a7955…`, 1618 B).
- `signature_preparations` (`358ca696-…`): status `confirmed`, `composition_revision=confirmed_revision=1`, confirmação e remind registrados.
- `signature_participants`: 2 linhas (`organization_representative`, `professional`), ambas assinadas pelo sub `…0003` na revisão 1.
- `audit_events`: trilha append-only íntegra (1418 registros acumulados na base de homologação ao fim da corrida).

**Limpeza e estado final do tenant TC**: os 7 contratos-resíduo de execuções anteriores foram encerrados via API (`failures=0`, motivo "Limpeza dev"). Inventário final (11 linhas, sem exclusão física): **1 ativo** (canônico `b0139659-…`, intacto) + **8 encerrados** (inclui `d9a77324-…`, evidência assinada desta corrida) + **2 arquivados** (`59bb862c-…`, `26837629-…` — evidências assinadas preservadas).

#### B.3.2c — Ações de contrato na ficha + impressão "RASCUNHO" + histórico (fechada em 07/10/2026)

Sobre os endpoints já validados em B.3.2b, cada ação de contrato passou a ser exposta na ficha (`GET/POST /organizacoes/{tenantId}/contratos/{contractId}`, Web BFF sobre a API) com permissão, estado, validação, feedback exato e auditoria, preservando os estados próprios — **elaboração ≠ assinatura ≠ vigência**, documento em assinatura imutável, duplicação não copia assinaturas, arquivar ≠ rescindir, sem exclusão física de emitidos/assinados e concorrência por `?version=N` (409 `contract.version.conflict`, linhas travadas com `FOR UPDATE`).

**Camadas implementadas** (ordem de execução registrada):

| Camada | Conteúdo | Arquivos principais |
| --- | --- | --- |
| A — contratos/DTO | registros de leitura/mutação do ciclo de vida de contrato + assinaturas | `src/Odca.Contracts/Contracts/` (novo) · `Operations/OperationalInboxContracts.cs` · `Renewals/RenewalContracts.cs` · `Studio/StudioContracts.cs` |
| B — repositório | consultas/mutações Dapper sob `FOR UPDATE`, eventos em `odca.contract_events` + `odca.audit_events` | `src/Odca.Infrastructure/Operations/ContractSheetRepository.cs` |
| C — controladores | endpoints de contrato na API + ações BFF na ficha (detalhes/duplicar/arquivar/restaurar/encerrar/histórico/assinar/lembrete/PDF/imprimir) + helper de URL de retorno | `src/Odca.Api/Controllers/ContractsController.cs` (novo) · `src/Odca.Web/Controllers/ContractsController.cs` · `Services/OdcaApiClient.cs` · `Models/OperationalWorkspaceViewModels.cs` |
| D — telas | seções detalhes/assinatura/histórico/ações na ficha + layout de impressão com marca "RASCUNHO"/"ASSINADO" | `Views/Contracts/Sheet.cshtml` · `Views/Contracts/Print.cshtml` (novo) · `Views/Contracts/Index.cshtml` |
| E — validação web | suíte ponta-a-ponta contra a stack real (fases T/B/W/C/H/O/R) + reset idempotente | `Temp\opencode\validate-b32c-web.ps1` · `Temp\opencode\reset-b32c.ps1` (fora do repositório) |

**Ações validadas na ficha** (cada uma com o feedback exato em tela e a persistência confirmada no banco na mesma fase da suíte):

| Ação | Feedback em tela (exato) | Evidência de persistência (fase da suíte) |
| --- | --- | --- |
| Atualizar detalhes (título/referência/responsável interno/prioridade/data de renovação) | "Detalhes do contrato atualizados." | T2 db referência+versão 1→2 · evento `contract.updated` · auditoria cresceu · T2c/T2d limpar referência (v3) |
| Duplicar | "Contrato duplicado com a minuta original. Assinaturas e versões geradas não são copiadas." | T7 novo contrato ativo v1 com título "(cópia)" + nova minuta própria (`00f5eabf-…`) · ficha nova renderizada |
| Arquivar / Restaurar (idempotentes) | "Contrato arquivado…" / "O contrato já estava arquivado." · "Contrato restaurado…" / "O contrato já estava ativo." | T3 db `archived_*` v4 + badge "Arquivado" + form restaurar · T4 replay sem bump · T5 db restaurado v5 + badge some · T5b replay sem bump |
| Encerrar (data + motivo + confirmação) | "Contrato encerrado com registro da data e do motivo. Ele também foi arquivado e não pode mais gerar assinaturas." · motivo curto: "Informe o motivo do encerramento (de 5 a 2000 caracteres)." | T8 motivo curto = erro 400 sem bump · C1 db `closed_*`+`archived_*` v2 · C2 badge "Encerrado" + evento `contract.closed` · C3 duplicar sobre encerrado → 409 `contract.closed` |
| Assinar (participante) + replay | "Assinatura de {name} registrada em nome do usuário conectado nesta sessão." / "…já havia sido registrada." | W2 db `signed_by` = cliente · W3 replay sem duplicata · W4 última assinatura → `externally_signed` + evento `signature.completed` |
| Reenviar lembrete (+ cooldown) | "Lembrete de assinatura reenviado aos participantes pendentes." / "Um lembrete foi enviado há pouco tempo; aguarde antes de reenviar." | W1 db `last_reminded_at` gravada · W5 409 `reminder.too_soon` surfacado como erro |
| Gerar PDF / Baixar / Imprimir | "PDF final da versão gerado e protegido no armazenamento privado." | B3 versão 201 → B4 `pdf_status=completed` (worker ~90 s) · W6 aviso + W7 download magic `%PDF` (1618 B > 1 KB) · W8 marca d'água "ASSINADO" (sem "RASCUNHO") + barra/auto-print |
| Histórico (paginado) | seção `#historico` na ficha | H1 canônico (updated×2, arquivado, restaurado) · H2 na cópia (version.generated, signature.completed, contract.closed) |

**Separação de perfis (fases O/R, validadas após v039):** o **cliente/admin** vê e opera todas as ações; o **operador** (com `contract_drafts.read/manage` + download de documentos e **sem** `tenant.contracts.*`) abre a ficha via `drafts.read` (200, 36 kB) mas **não** vê `<form id="details-form">`, `<form id="duplicate-form">` nem `#historico`; o `POST …/arquivar` é **negado** (API → 403 problem+json, provado diretamente com token do operador; Web → 302 para `/acesso-negado`, página "Acesso negado") e o canônico **permanece ativo** (R2: exatamente 1 contrato ativo na lista).

**Falhas registradas e corrigidas neste item** (ação / expected / observed / causa raiz / correção / regressão):

##### B.3.2c-B6 — 500 na materialização da preparação de assinatura (Dapper por construtor)

- **Ação**: `GET studio/versions/{versionId}` com preparação confirmada (passo B6 da suíte; também exercitado pelo fluxo Web de "Baixar PDF").
- **Expected**: 200 com o detalhe da versão incluindo a preparação e seus participantes.
- **Observed**: 500 na materialização por construtor do Dapper.
- **Causa raiz**: Dapper 2.1.89 exige nº de parâmetros do construtor == nº de colunas retornadas e compatibilidade de tipo; `signature_participants.signed_at` chega como `DateTime`, mas `SignatureParticipantInput` declarava `SignedAt` como `DateTimeOffset?` (conversão inexistente na materialização por construtor).
- **Correção**: `src/Odca.Contracts/Studio/StudioContracts.cs:89` — `SignedAt` de `DateTimeOffset?` para `DateTime?`.
- **Regressão**: repro isolado `b6-probe2` deu `Dapper OK rows=2` em 6/6 preparações; suíte B6 **PASS** ("preparationId obtido") e B1–B5 verdes nas execuções #5–#8.

##### B.3.2c-W1 — 503 (AnchorTagHelper) no link "Baixar PDF" da ficha

- **Ação**: renderizar a ficha de um contrato com PDF pronto (bloco de ações de documentos, `Sheet.cshtml` ~linha 946).
- **Expected**: 200 com o link "Baixar PDF" funcionando e preservando o contexto de navegação (`from/year/month/scope/kind/urgency/viewId/obrigacao`).
- **Observed**: 503 causada pelo `AnchorTagHelper`.
- **Causa raiz**: a âncora combinava `href` já composto com atributos `asp-route-*`; o tag helper aceita `href` **ou** `asp-controller/action/route-*`, nunca os dois juntos.
- **Correção**: helper público estático `ContractsController.BuildReturnUrlQuery` (`ContractsController.cs:176`) + variável `pdfBaixarQuery` no bloco `@` do `Sheet.cshtml:24` + âncora reduzida a `href="…/pdf-baixar@pdfBaixarQuery"` (`Sheet.cshtml:949`).
- **Regressão**: execuções #5–#8 W1 200, W7 `%PDF` (1618 B), W8 marca "ASSINADO" — ALL PASS.

##### B.3.2c-v039 — Operador herdando o ciclo de vida do contrato (grants m038)

- **Ação**: execução #5 da suíte, fases O (operador) e R (lista).
- **Expected** (matriz aprovada): operador **sem** `tenant.contracts.*` → O2 (ficha legível via `drafts.read`, sem forms de ciclo de vida/histórico), O3 (POST arquivar negado) e R2 (canônico segue ativo após a negação).
- **Observed**: 5 falhas (O2×3, O3, R2) — o operador **arquivou** o canônico e a lista passou a exibir 0 ativos.
- **Causa raiz única**: m038/v038 (grants originais em `odca.sql` 4398–4411) **derivava** `tenant.contracts.read/manage` de `contract_drafts.read/manage`; o seed concede `contract_drafts.manage` ao `tenant-operator`, que herdava `contracts.manage`. **Decisão (opção B — respeitar a matriz)**: o operador executa minutas, assinaturas e downloads, mas **não** detém o ciclo de vida do contrato; papeis administrator/cliente mantêm os seus grants; nenhum código C# re-deriva esses grants para tenants novos (somente a migração).
- **Correção**: migração **v039** (`odca.sql` 4492–4512) = `DELETE FROM odca.role_permissions` de `tenant.contracts.read/manage` restritos a `scope_type='tenant' AND code='tenant-operator'`, em `BEGIN/COMMIT` com `pg_advisory_xact_lock` e `INSERT … ON CONFLICT DO NOTHING` em `odca.schema_migrations` (checksum `d2df475b1a17bb10a0c987f5e60160979a310449a247c382b88c9aba7efe7496`); `DatabaseSchema.CurrentVersion` 38→39; snapshot `database/releases/odca-v039.sql` = cópia byte-completa do consolidado (SHA256 idêntico, verificado). Seed-test-access intacto (já usa `ON CONFLICT DO NOTHING` — não corrigido).
- **Regressão**: aplicada via `dotnet run --project src/Odca.Bootstrap -- migrate` (apenas v039); validação psql — `schema_migrations` com MIG 39 e checksum conferido, operador **sem nenhum** `tenant.contracts.%`, administrator/cliente mantêm `read`/`manage`/`history`; readiness (exige count/max == 39) verde; serviços reiniciados (API :7143, Worker, Web :7144). Execuções #6–#8: O1–O3 + R1–R2 ALL PASS.

**Nota do contrato Web de 403 (descoberta na execução #6, registrada para evitar falsas falhas futuras):** o 403 é retornado pela **API** (prova direta com token do operador: `403` problem+json `{status:403,title:"Forbidden",traceId:…}`); a **Web** o traduz como `Forbid()` → 302 para `/acesso-negado` (`AccessDeniedPath` em `Program.cs`; página "Acesso negado — Seu perfil não permite esta operação."), de modo que o follow termina em 200. O assert O3 foi alinhado a esse contrato real (página de negação + nenhuma mutação no banco + sem entrada nova de auditoria) — não é bug de produto, é o mapa de status do BFF (401→Challenge, 403→Forbid). Erros de negócio (400/409) continuam voltando para a ficha com erro em TempData.

**Reset operacional reforçado** (`Temp\opencode\reset-b32c.ps1`): além de fechar cópias ativas, agora limpa no canônico `archived_at/by`, `archive_reason`, `closed_at/by/on`, `closure_reason` e devolve `version=1` (necessário porque o ciclo O3/R2 deixa estados variados entre execuções). Baseline limpo confirmado antes da execução final: `UPDATE 1 / UPDATE 0 / ativos=1 / v=1`.

**Estado final após a execução de fechamento (#8, 07/10/2026)**: `validate-b32c-web.ps1` **ALL PASS** no ciclo completo (T0–T8 · B1–B6 · W1–W8 · C1–C3 · H1–H2 · O1–O3 · R1–R2), com persistência confirmada no banco em cada fase mutacional. Inventário do tenant TC (sem exclusão física): canônico `b0139659-75b7-4782-a250-ae41d29490e1` **ATIVO v5** (histórico: updated×2, arquivado T3, restaurado T5); cópia da execução `1f802cdb-…` ("(cópia)") **encerrada+arquivada v2** com a cadeia completa de evidências (assinaturas completas → `externally_signed`, PDF de 1618 B, eventos `signature.completed`/`contract.closed`); cópias das execuções anteriores seguem encerradas+arquivadas; fixture assinada `d9a77324-…` intacta (versão `decd3a65-…`).

#### B.3.3 — Central de renovações e prazos (em aberto — escopo/TC definido em 07/10/2026)

**Objetivo**: tornar "Renovações e prazos" uma jornada funcional ponta-a-ponta sobre a fundação existente, atendendo: prioridades baixa/normal/alta/crítica · datas separadas (vigência atual × proposta) · prazo indeterminado · lembretes 30/15/7 configuráveis · **sem renovação automática** (aplicação somente na central, transação pessimista) · proposta na ficha = registro de intenção, com efeito em vigência somente após aplicação.

**Estado da fundação (exploração verificada em fonte + banco em 07/10/2026):**

| Camada | O que já existe |
| --- | --- |
| API (`RenewalCenterController`, raiz `api/v1/organizations/{tenantId}/renewals`) | `GET /` — janela de 3 meses no timezone do tenant, status derivado (`expiring`/`expired`/`outside_window` × request ativo via `DISTINCT ON(contract_id)`), resumo contagens, paginação clamp 1..100, parâmetros opcionais com casts explícitos (`from/to/ownerId/contractType/counterparty/status/mine/withoutOwner`); permissão `tenant.renewals.read`. `POST /contracts/{contractId}` — cria request `draft`: `kind` renewal\|amendment, prioridade low\|normal\|high\|critical (default normal), valor total\|increase\|decrease com validação, `FOR UPDATE` no contrato + guarda de versão (409 "O contrato mudou. Faça uma nova análise."), idempotência por `idempotency_key` (replay 200 `replayed:true`), 409 "Já existe uma alteração ativa para este contrato."; permissão `tenant.renewals.prepare`. `POST /{id}/formalization` — exige estado ∈ `internally_approved`\|`awaiting_formalization`, `row_version`, data ≤ hoje, justificativa obrigatória e evidência em `document_versions` com `security_status='safe'`; `application_status='scheduled'` se `effective_on > hoje`; evento `manual_formalization`; permissão `tenant.renewals.formalize`. `POST /{id}/apply` — `FOR UPDATE OF r,c`, exige `formalized` + `application_status ∈ not_applied/scheduled/failed`, divergência de versão do contrato ⇒ request `conflict`+`failed` e 409; atualiza contrato com `COALESCE` (campos não propostos preservados) + `version+1`; grava aplicação única em `contract_change_applications` (`UNIQUE(tenant_id,request_id)`, before/after jsonb); retorna `{applied:true,novaVersão,ObligationsPreserved:true,EndsOn}`; permissão `tenant.renewals.apply` |
| Banco (`odca.sql`) | `contract_change_requests` (estados `draft→in_review→internally_approved→awaiting_formalization→formalized` + `cancelled/conflict`; `application_status not_applied/scheduled/applied/failed`; prioridade v038 CHECK low/normal/high/critical; `effective_on NOT NULL`; checks de datas/finalização); `contract_change_events` (auditoria por request); `contract_change_applications`; trigger `protect_formalized_evidence` (trava evidência ao formalizar); fila do worker `claim_due_contract_change` (formalizados/scheduled com efeito vencido); índice único parcial `contract_change_one_active_uq` — **libera o slot** para `cancelled`, `conflict` e formalizado-aplicado; `contracts.end_date` é NULL (prazo indeterminado suportado nativamente); colunas de aviso por contrato `renewal_notice_amount/unit` → `DecisionDueOn` na listagem |
| Web BFF | rota `/organizacoes/{tenantId}/renovacoes` (`RenewalsController.Index` + `POST {requestId}/aplicar` c/ antiforgery + TempData), view com cards de resumo + filtro de situação; item de menu "Renovações e prazos" no `_Layout.cshtml`; form de proposta na ficha (`Sheet.cshtml #renovacao` → `CreateRenewalProposal`, força `kind=amendment` fora da janela/com mudança de prazo); `OdcaApiClient.GetRenewalsAsync` / `ApplyRenewalAsync` / `CreateRenewalProposalAsync` |
| Permissões efetivas (banco, verificado 07/10) | **admin** e **cliente**: os 6 `tenant.renewals.*` (read/prepare/submit/formalize/apply/cancel); **operador**: apenas `read`+`prepare` (lista e propõe; não submete/formaliza/aplica/cancela) |
| Baseline | 0 requests em `contract_change_requests` no tenant TC |

**Gaps a entregar neste item:**

- **G1** — Sem endpoint nem tela de detalhe do request (DTO `RenewalRequestDetails` existe sem consumo).
- **G2** — Máquina de estados morre em `draft`: as permissões `submit`/`cancel` existem no banco **sem endpoint**; formalização só aceita `internally_approved`/`awaiting_formalization`.
- **G3** — Prioridade não é exposta na leitura: ausente de `RenewalListItem` e das telas da central/detalhe. (O form de proposta da ficha já envia `priority` e o endpoint `Create` já a persiste no request — confirmado em fonte em 07/10; o que falta é **exibi-la**.)
- **G4** — Prazo indeterminado (`end_date NULL`) cai em `outside_window` e fica **oculto na lista padrão**, sem rótulo próprio.
- **G5** — Lembretes 30/15/7 configuráveis não existem (nenhum dia configurável em `tenants`; o que há é aviso por contrato).
- **G6** — Formalização sem UI (nenhuma chamada Web ao `/formalization`; sem seletor de evidência).
- **G7** — Sem teste HTTP ponta-a-ponta dos endpoints de renovação (só asserts de esquema/domínio).

**Plano de implementação (ordem de execução):**

1. **DB v040** (migração incremental no padrão do projeto + snapshot `database/releases/odca-v040.sql` + `CurrentVersion` 39→40): coluna `odca.tenants.renewal_reminder_days text[] NOT NULL DEFAULT '{30,15,7}'` + CHECK (3 elementos, cada um 1..365); sem novas permissões neste ciclo (matriz atual atende — ver D4).
2. **API** (`RenewalCenterController`):
   - `GET /renewals/{id}` — detalhe (permissão `read`): `RenewalRequestDetails` + eventos do request + versões candidatas a evidência (`document_versions` seguras do contrato).
   - `POST /{id}/submit` — permissão `submit`; transição de draft conforme **D1**; guarda `row_version`; evento `submitted`; 409 com estado/versão inválidos.
   - `POST /{id}/cancel` — permissão `cancel`; estados abertos → `cancelled`; evento `cancelled`; 409 contrário; slot liberado para nova proposta (garantido pelo índice parcial).
   - `List`: `Priority` em `RenewalListItem` (+ filtro `priority`); ramo `end_date IS NULL → status 'indeterminate'` (visível na lista padrão, fora dos contadores expiring/expired, `DaysRemaining` nulo); próximo lembrete calculado dos dias configurados do tenant.
   - Configuração de lembretes: `GET` + `PUT` dos dias (validação server 1..365, exatamente 3 valores; GET sob `tenant.renewals.read`; PUT sob permissão de gestão organizacional a confirmar na implementação — operador não altera).
3. **Web BFF + telas**: novos métodos no `OdcaApiClient` (detalhe/submit/cancel/formalização/config de lembretes); **tela de detalhe da proposta** (`/organizacoes/{tenantId}/renovacoes/{requestId}`) com dados atuais × propostos, ações por estado (concluir/submeter · registrar formalização c/ seletor de evidência+data+justificativa · aplicar · cancelar), histórico de eventos e mensagens exatas; **central**: badges de prioridade (baixa/normal/alta/crítica), rótulo "Prazo indeterminado", indicação de janela de lembrete ativa, bloco de configuração dos dias (local por **D2**); **form de proposta da ficha**: seletor de prioridade (default normal) mantendo datas propostas separadas e proposta sem fim = indeterminado.
4. **Validação**: suíte `validate-b33-web.ps1` (fases P/L/R/I/S/F/A/C/M/O/X abaixo) + **regressão obrigatória** da suíte B.3.2c após as mudanças na ficha.

**TC — cenários mínimos de aceitação (assert em UI + API + banco):**

| Fase | Cenário | Assert |
| --- | --- | --- |
| P | Health gates (API `/health/ready`, Web `/entrar`) | 200/200 |
| L | Central padrão | só contratos da janela/requests ativos; contagens do resumo == banco; `expired`/`expiring` corretos |
| L | Contrato de prazo indeterminado | aparece como "Prazo indeterminado", `DaysRemaining` nulo (sem contagem falsa), visível na lista padrão e filtrável |
| L | Filtros + paginação | status/prioridade filtram; `pageSize` clamp 1..100 |
| R | Proposta na ficha (prioridade crítica, datas propostas separadas, valor increase) | 201 `draft` · central com badge "crítica" · banco com prioridade + datas atuais×propostas · segunda proposta do mesmo contrato → 409 "Já existe uma alteração ativa…" |
| R | `effectiveOn` retroativo → 400 exato; replay do mesmo `idempotencyKey` → 200 `replayed:true` | sem duplicata no banco |
| R | Operador propõe (tem `prepare`) | 201; cliente vê na central |
| I | Aditivo de contrato indeterminado sem `proposed_end` | OK; ao aplicar, `end_date` segue NULL (`COALESCE`) e `version+1` |
| S | Submit do draft → estado por D1 + evento `submitted`; re-submit → 409; `row_version` errado → 409 | banco confere estado/versão |
| F | Formalizar (pós-submit) com evidência segura + justificativa + data de hoje | `formalized` + evento `manual_formalization`; `effective_on > hoje` ⇒ `application_status='scheduled'` |
| F | Evidência não-segura/inexistente → 409 exato; data futura → 400 | sem mutação |
| A | Aplicar pela central | contrato start/end/value atualizados + `version+1` · linha em `contract_change_applications` c/ before/after · resposta `{applied:true,…,ObligationsPreserved:true,EndsOn}` |
| A | Re-aplicar → 409; duplo clique (2 applies seguidos) → 1 sucesso + 1 409 | aplicação única (constraint) |
| A | Aplicar após mudança independente do contrato (detalhes) | request `conflict`+`failed` + 409 |
| C | Cancelar em draft → `cancelled` + evento; nova proposta do mesmo contrato possível (slot liberado) | banco confere |
| C | Cancelar já formalizado/aplicado → 409 | sem mutação |
| M | Padrão 30/15/7 exibido; salvar 45/20/5 → 200 + persistido em `tenants` + refletido nos cálculos da central | valores inválidos (0, 366, negativos, tamanho ≠ 3) → 400 |
| M | Contrato dentro da janela de cada dia configurado exibe a etapa de lembrete ativa | cálculo server conferido contra datas do fixture |
| O | Operador: lista 200 (`read`), propõe 201 (`prepare`); submit/cancel/formalize/apply negados | API 403 problem+json (prova direta) · Web 302 `/acesso-negado` · zero mutações no canônico |
| X | Regressão: suíte B.3.2c rerun + asserts de esquema da suíte de integração | ALL PASS sem regressão |

**Decisões (registro):**

- **D1 (decidida em 07/10 — opção a)** — `POST /{id}/submit` = draft→`in_review` (coerente com o rótulo da permissão "Encaminhar renovação à revisão"); a formalização passa a aceitar também `in_review` (`IN('in_review','internally_approved','awaiting_formalization')`). `internally_approved`/`awaiting_formalization` permanecem como atalhos para o fluxo Enterprise (seção C). Operador (`read`+`prepare`) não submete; admin/cliente submetem e formalizam.
- **D2 (decidida em 07/10 — opção a)** — bloco de configuração dos lembretes fica na própria central "Renovações e prazos". **Permissão do PUT**: `tenant.organization.manage` (existente; admin/cliente têm, operador só tem `organization.read` → operador lê mas não altera). GET sob `tenant.renewals.read`. Sem permissão nova (mantém D4).
- **D3 (registrada)** — prazo indeterminado vira status `indeterminate` visível na lista padrão da central (fecha G4).
- **D4 (registrada)** — nenhuma permissão nova neste ciclo: operador segue `read`+`prepare` (propõe, não decide); admin/cliente mantêm os 6. Alterações de matriz virariam migração própria.

#### B.3.4 — Campos por perfil de atuação (fechada em 08/10/2026)

**Objetivo**: perfil de atuação (clínica de terapia, cirurgião plástico) sobre os campos comuns: regras por campo, autofill conferível, validação CPF/CNPJ/datas/valores client+server, responsável legal condicional, mensalidade em **fórmula explícita**; modelo cirúrgico exige **aprovação ODCA** (Enterprise).

**O que foi entregue:**

| Camada | Alteração |
| --- | --- |
| Banco | Migração **v041**: `odca.tenants.activity_profile text NOT NULL DEFAULT 'general'` + CHECK ∈ (`general`,`therapy_clinic`,`plastic_surgery`); snapshot `database/releases/odca-v041.sql` byte-idêntico ao consolidado; checksum conferido com a receita do migrador (declarado = computado `ef78cb21…5e411d`); `DatabaseSchema.CurrentVersion` 40→41; `bootstrap migrate` aplicado (41\|41\|41, `/health/ready` healthy) |
| API tenancy | `OrganizationsController` PUT: 204 válido · 400 ValidationProblem em `activityProfile` (valor fora da lista) · **409 + `code=organization.profile.plan_required`** para `plastic_surgery` sem assinatura Enterprise ativa (join `subscriptions`→`plan_versions` `pv.code='enterprise'`) · 409 **sem** `code` em conflito de versão; DTOs `OrganizationDetails(ActivityProfile)`/`UpdateOrganizationRequest(...,ActivityProfile)` |
| Web | `OrganizationsController` GET/POST editar com o mesmo gate (ModelState `exige plano Enterprise ativo.` via `UserMessage`, toast "Organização atualizada."); `Edit.cshtml` com `<select>` de 3 perfis (rótulo "(Enterprise)" em cirurgião plástico) + hidden TenantId/Version/Status + antiforgery |
| Overlay (D2) | `ActivityProfileOverlays.Apply(officialKey, profile, content, fields)` na **criação** de minuta nova (não em minuta retomada): `therapy_clinic` × `multiple-therapies` insere, ancorado no parágrafo do campo `fee_neuropsychology`, a CLÁUSULA 2ª-A (sessões por terapia) + parágrafo MENSALIDADE TOTAL, 4 defs `sessions_*` (Number, `RequiredWhen therapy_*=Sim`) e a def `monthly_fee_total` (tipo `Formula`, `Required=false`, 4 termos) |
| Fórmula | `StructuredContractDocument`: enum `Formula`, `FormulaTerms`, divergência em `ValidateValues` (mensagem `O valor de '{Label}' não confere com a fórmula declarada (esperado: {expected}).` em **todos** os modos), `TryComputeFormulaTotal` (ambos os lados vazios → inativo; um lado → pendente; qty<0/não-canônico → pendente; zero termos ativos → `0.00`), `FillFormulaValues` via `NormalizeStoredValues` em criar/salvar/preparar/gerar, `ProtectAutomaticOrigins`; `CanonicalDecimal.Parse` aceita pt-BR (`1.234,56`→`1234.56`) e rejeita texto |
| Cliente | `studio.js`: tipo `Formula` no mapa de tipos, validador de documento BR (`maskBrDocument`/`brDocumentValid`), `formulaTerms`/`formulaTotal` em BigInt escala 10^8 (qty 6 casas, unidade 2 casas, half-up `remainder*2>=10^6`), `recomputeFormulas` no input, `rejectInvalidDrafts` |
| Gate cirúrgico (D3) | `OfficialContractTemplates.SurgicalConsent()` (key `surgical-consent`, purpose `consent`, 16 campos incl. `BrazilianDocument` cirurgião/paciente, `Date` data, `Currency` honorários, Choice anestesia/pagamento/riscos, `Belém/PA`, responsável legal `RequiredWhen has_representative=Sim`) + `RequiresOdcaApproval`; `CreateDraft` → 409 `draft.profile.surgical_required` se perfil ≠ `plastic_surgery` (Web surf via TempData "StudioError"); `DraftResponse.RequiresOdcaApproval` + banner `data-pending-odca-approval` em `Studio/Edit.cshtml`; blocker `approval.odca.required` em `CalculateReadiness` (`CanConfirm=false`) e confirmação da preparação → 400 citando a aprovação |

**Validação na stack real: ALL PASS 93/93** — `validate-b34-web.ps1` (Temp, UTF-8+BOM), duas execuções estáveis, via Web :7144 + API :7143 + SQL direto em `odca_test_disposable`: T0 (gate/baseline/limpeza/login único) · T1 regras de perfil (400 inválido, 204 persistido, 409+code no gate sem consumir versão/alterar perfil, 409 de versão obsoleta sem `code`, restore) · T2 tela Web (label/opções/selecionado, gate Enterprise no form com perfil inalterado no banco, toast de sucesso) · T3 estáticos do `studio.js` · T4 instalação idempotente dos **9** modelos oficiais + catálogo Web com nome e descrição do cirúrgico · T5 gate cirúrgico (409 fora do perfil, 201 no perfil, tipos dos 16 campos, responsável legal condicional, banner na minuta) · T6 validação de servidor (CPF inválido→400 apontando o campo, válido→200, data inválida→400, moeda pt-BR→canônica `1234.56`, texto em moeda→400) · T7 overlay+fórmula (4 campos de sessão condicionais, fórmula com 4 termos `Required=false`, cláusulas inseridas, total semeado `0.00`, autofill `contracted_name`=`display_name`/`source=organization`, divergente→400 **com o esperado**, vazio→autofill `800.00`, obsoleto→400 `1000.00`, recalculado→200, perfil `general` **sem** overlay como controle) · T8 bloqueio ODCA (draft completo→200, geração→201, readiness→200 com blocker `approval.odca.required` + `canConfirm=false`, confirmação→400 citando aprovação **sem** deixar preparação, versão não-cirúrgica→200 sem o blocker).

**Falhas registradas no ciclo (ação/expected/observed/causa/correção):**

- **Form Web do perfil (suíte, não produto)** — POST `/editar` re-renderizava sem chamar a API: `Version='[string]@{version=42}.version'` porque `Esc [string]$org.version` em posição de argumento de comando é lexado como string literal pelo PS 5.1 (o cast não acontece); correção na suíte: cast prévio em variável (`$vS=[string]$org.version`) e depois `Esc $vS`. Asserts T2.2/T2.3 passam.
- **CPFs de teste invertidos (suíte, não produto)** — `52998224725` é **válido** (dígitos verificadores conferem) e `52998224720` é **inválido**; a suíte usava um como o outro: T6.1 esperava 400 mas o servidor respondia 200 (valor aceito), e T8.1 esperava 200 mas recebia 400 `O valor de 'Documento do paciente' é inválido`. Correção: T6.1 usa `52998224720` (inválido → 400) e T8.1 preenche `52998224725` (válido → 200); o CPF canônico `11144477735` segue no cirurgião.
- **Detail-500 no readiness (produto)** — `GET …/signature-preparation/readiness` → 500 `InvalidOperationException`: o record `ReadinessRow` recebeu `OfficialKey` **no final** mas o SELECT coloca `t.official_key` na **posição 5** (antes de `p.id`); Dapper casa construtor por posição das colunas → nenhum construtor correspondente. Correção: reordenar o record para espelhar o SELECT (`ContractStudioController.cs:1456`); rebuild+restart; readiness 200 com blocker.
- **`$pId` × `$PID` (suíte)** — atribuição a `$pId` (case-insensitive) colide com a variável automática somente-leitura `$PID` → script abortava no T8.4; correção: `$partId`.

**Decisões (registro):**

- **D1 (decidida e implementada)** — `odca.tenants.activity_profile` com migração **v041**, editável pela tela da organização (Web) e pelo `PUT` da API; o gate de plano (`plastic_surgery` → Enterprise) é aplicado nos dois caminhos com o mesmo `code`. O overlay é aplicado **somente na criação** de minuta nova sobre o template escolhido (minutas retomadas/existentes preservam o conteúdo gravado).
- **D2 (decidida e implementada)** — tipo novo somente-leitura `Formula` + campos `sessions_*` Number (`RequiredWhen therapy_*=Sim`); `monthly_fee_total = Σ(sessões × valor_por_sessão)`; o **servidor** recalcula/valida em criar/salvar/preparar/gerar (valor vazio é preenchido com o canônico; valor presente divergente é rejeitado em todos os modos de validação com o esperado na mensagem; campo criado com `Required=false` para não travar a criação da minuta); o cliente replica a conta em BigInt (sem float) e auto-confirma.
- **D3 (decidida e implementada)** — neste ciclo entra apenas o **modelo oficial cirúrgico mínimo** (`surgical-consent`, purpose `consent`); perfil `plastic_surgery` restrito ao plano Enterprise; minutas cirúrgicas ficam em estado **visual** de pendência (banner + `requiresOdcaApproval`) e com **bloqueio real** no preparativo de assinatura (blocker `approval.odca.required` no readiness + confirmação rejeitada). O workflow completo de aprovação ODCA dos modelos é da **seção C**.

**Estado**: banco v041 · build 0/0 · suíte `validate-b34-web.ps1` 93/93 (2 execuções) · git `codex/s00-foundation` @ `e1ee6b0` com árvore suja (v041, tenancy, Formula, overlays, template cirúrgico, controller/visões, studio.js, suíte) — publicar somente quando pedido.

## C. Enterprise ODCA / SLA

**Fechado (09/10/2026) — migração v042; suíte `validate-c-web.ps1` ALL PASS 93/93 (3 execuções).** Solicitações (revisão/adaptação/esclarecimento) com estados aberta→triagem→em atendimento→aguardando cliente→resolvida→encerrada (+cancelamento com motivo), SLA por plano/serviço/prioridade com snapshot no abrir, fuso/calendário explícitos, pausa/retomada justificada deslocando prazos, reatribuição sem reiniciar relógio, violações marcadas uma única vez pelo Worker (`violacao_*`), fila da Central ODCA multi-organização e aprovação formal dos modelos cirúrgicos (decisão aprovado/reprovado com nota, decisor auditado) que libera o blocker `approval.odca.required` da B.3.4 de forma reversível. Detalhamento: `docs/ENTREGA-CONCLUSAO-SISTEMA.md`, caderno `docs/execution/RESULTADO_SECAO_C_V42_20261009.md`, decisões D-C1…D-C11 em `DECISIONS-LOG.md`. Regressão B.3.4 conferida: 93/93.

## D. Design e homologação

**Em aberto.** Inclui: identidade visual (azul-marinho base, branco, verde de destaque, seções numeradas, caixas de atenção); documento com capa/sumário quando adequado e sem variáveis não resolvidas; lista + botão "Novo contrato"; homologação obrigatória em 360/768/1440 px + teclado/foco/contraste; matriz ações×perfil×plano; evidências UI; 2 organizações + 4 perfis e os 19 cenários mínimos.

---

## Anexo — credenciais atuais (mascaradas) e procedimentos

| Usuário | Senha | Observações |
| --- | --- | --- |
| `admin@odca.local` | `V7!q••••` | Superadministrador; MFA obrigatório (segredo `3NEL••••` base32, 8 recovery codes, `security_version=8` após a rotação pós-suíte do item B.2 — cada `provision-test-access --rotate-passwords` incrementa o valor) |
| `cliente.teste@odca.local` | `K8@w••••` | Admin do tenant TC; sem MFA (endpoint de tenant) |
| `operador@odca.local` | `La@R••••` | Operador; senha rotacionada novamente na re-provision pós-suíte (B.2); valor exato em `%LOCALAPPDATA%\ODCA Solutions\development-credentials.json` (fora do repositório) |

Procedimentos do ambiente (detalhados em `ENTREGA_FASES_1_A_3_20261006.md`, seção 7): `scripts\run-local.ps1`, `ODCA_RUNTIME_CONFIG=src\Odca.Api\development-runtime.json`, rate limit de login 5/min/IP (429 → aguardar janela; scripts deste ciclo tratam retry com espera de 70 s). Banco/usuários de testes separados dos manuais: o banco `odca_test_disposable` e as contas demo são exclusivas da homologação local.
