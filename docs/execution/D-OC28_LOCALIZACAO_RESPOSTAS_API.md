# D-OC28 — Localização total das respostas de sucesso + erros remanescentes da API

Data: 14/10/2026 · ramo `codex/s00-foundation` (base `006f185` = D-OC27) · não pushado

## Objetivo

Todo texto de usuário PT em **respostas** da API passa pelo catálogo na cultura do usuário (`X-Odca-Culture`): (1) todos os payloads de **sucesso/informativos** (~90 textos distintos) e (2) os **gaps de erro** que sobraram do D-OC27 (~25). Camada de armazenamento continua **canônica em pt-BR** (`odca.sql` inalterado; literais emitidos por SQL continuam localizados apenas na fronteira da API, por pós-processamento das linhas Dapper). Regra da casa mantida em tudo: **texto desconhecido volta como veio** — nada é inventado. Valores pt-BR do catálogo são **byte-idênticos** ao texto original da fonte.

## Mecanismos (sem nova infraestrutura — reaproveita o que o D-OC27 instalou)

- **Texto dono-controller** → `this.T(key[, args])` inline (receiver `this.`, cultura via `X-Odca-Culture`, args com `{0}` sempre no formato exato que `LocalizationService.ApplyArgs` espera).
- **Mensagens dinâmicas donas-validador** → mapa reverso `ApiMessages.Localize` (as 3 mensagens de `StructuredContractDocument` do Estúdio já estavam em `KnownKeys` → `fields.invalid` resolve para o catálogo sem mudança).
- **Middleware/gates (fora de controller)** → `context.RequestServices.GetRequiredService<LocalizationService>().T(key, ApiCulture.For(context))` — `ReleaseFeatureGate`, `OrganizationFeatureGate`.
- **Evento JWT do `Program.cs`** → mesmo estilo FQDN já usado no handler de 500 do D-OC27.
- **Literals SQL** (`NextAction`/`reason` emitidos por queries C#) → pós-processamento exato (switch por valor canônico / `record with { }` nas linhas Dapper); as queries inline não mudam.

## Estabilidade de wire (o que NÃO muda)

- **Códigos dos gates preservados**: `release_feature_disabled`, `feature_check_failed`, `permission_denied`, `organization_suspended`, `membership_blocked`, `feature_blocked`, `plan_restricted`, `quota_exhausted` — apenas `title`/`detail` localizados.
- **`{name}` (feature)**: resolve `api.gate.feature_<código>`; chave sem valor ⇒ fallback ao `OrganizationFeatureCatalog.DisplayName` (rótulo canônico PT — nunca inventado). **`{reason}`** (justificativa registrada pelo superadmin) e conteúdo de usuário passam como vieram.
- **Valores-máquina estáveis**: `code` dos itens, `RequiredPermission`, status, priority, `feature`, `channel`, ids/tokens.
- pt-BR (cultura default) continua exibindo exatamente o que o código sempre exibiu — nenhuma regressão de bytes.

## Catálogo

- **+131 chaves × 4 idiomas** nos quatro catálogos (`pt-BR/en-US/fr-FR/es-ES.json`): 1186 + 131 = **1317 por idioma**, paridade de chaves **4/4** (validada na pipeline de merge + `LocalizationCatalogParityTests` + cobertura no gate).
- **Dedup de textos compartilhados**: `api.studio.readiness_action_none` ("Nenhuma ação." ×6 sites), `api.studio.readiness_storage_reconcile_action` (×2), `api.common.next_analyze_expired_contract` (OperationsCenter "contract/overdue" + RenewalCenter — bytes idênticos sem acento).
- Distribuição: onboarding 2 · privacy 1 · plans 1 · platform 2 · solicitations 1 · obligations 3 · operations 17 (14 `next_*` + 3 `reason_*`) · renewals 6 · reviews 2 · common 2 · auth 2 (sessão JWT) · studio 65 (resume 1, reminder 1, participantes 6, conferência 13, checklist 4, readiness 40) · gates 27 (release 4, organização ~16, nomes de feature 7).

## Exclusões documentadas (valores de dados, não textos fixos)

- Sufixos gerados em cópias: `" (cópia)"` / `' — cópia'` (ContractsController, ContractStudioController).
- Fallbacks de nome de arquivo/título de documento: `"Documento"` / `"documento"` (Estúdio).
- Fallback de contexto `'Documento gerado'` (Central de Operações, SQL).
- Conteúdo de usuário sempre em trânsito: responsáveis/nomes de membros, tipo de participante fora dos 4 conhecidos (volta tipo bruto), justificativa `{reason}` dos gates, descrições que não são os 3 rótulos fixos de motivo.

## Sites alterados (21 arquivos)

- **Controllers (12)**: Onboarding (accepted ×2) · PrivacyRequests (accepted) · Plans (const `billingLimitation` vira argumento do `ToPreview`) · Obligations (hints ×3) · OperationsCenter (`ToDto`/`GetNextAction` instanciadas + `LocalizeReason`; 14 next + 3 reasons) · RenewalCenter (`LocalizeNextAction` ×7 via `with { }`) · ReviewRequests (400 datas → `api.reviews.date_range_invalid`; fallback 'Revisor' → catálogo) · Dashboard (fallback 'Administrador') · PlatformOperations (template criado + fallback genérico do envelope) · Solicitations (fallback genérico do envelope) · ContractStudio (resume de minuta, conferência completa, endpoint de checklist, lembrete local, `ParticipantOrigin`/`CompareCompositions` instanciadas, readiness completo).
- **Não-controllers (2)**: `ReleaseFeatureGate` (503 title/detail ×4), `OrganizationFeatureGate` (~16 titles/details nos 8 códigos + `LocalizeFeatureName`), `Program.cs` (JWT 401 ×2).
- **Catálogos (4)**: +131 chaves cada (merge idempotente preservando BOM/CRLF; valores pt-BR copiados verbatim da fonte).
- **Testes (3)**: `LocalizationEndToEndTests` +2 teorias (4 casos) · `DatabaseFixture.CreateApi(configure)` (parâmetro opcional de override de configuração — callers existentes inalterados) · `ApiLocalizationCoverageTests.NonCatalogLiterals` + prefixo `api.gate.feature_` (literal crua concatenado em runtime com o código da feature — o scanner da fonte a captava como chave).

## Defeitos encontrados pelos gates novos (e corrigidos)

1. **Scanner de cobertura vs. prefixo dinâmico**: o helper do gate lê `"api.gate.feature_" + feature` — o scanner (que pega todo literal `"api.*"`) reclamou "chave ausente" em 4 idiomas. Corrigido listando o prefixo em `NonCatalogLiterals` (mesmo tratamento dos prefixos MFA do D-OC27).
2. **CA1822 no `ToPreview` de Plans**: após mover o texto para argumento, o método deixou de tocar estado de instância e voltou a ser `static` (analisadores estritos em 0W/0E).

## Fatos de teste novos

- **`LocalizationEndToEndTests.SuccessfulRegistrationFlowIsLocalizedInTheRequestedCulture`** (pt-BR/en-US): cadastro **válido** (CPF válido gerado com dígitos verificadores) → 202 com `message` localizado + `confirm-email` → 200 com `message` localizado.
- **`LocalizationEndToEndTests.DisabledReleaseFeatureProblemIsLocalizedInTheRequestedCulture`** (pt-BR/en-US): factory com `Features:ContractImports=false` → `GET /api/v1/organizations/{tenant}/contract-imports` → **503 problem+json** com `title`/`detail` localizados e `code = release_feature_disabled` estável. O gate roda antes da autenticação no pipeline (Program.cs), então o teste é anônimo e barato.
- A varredura existente do scanner cobre automaticamente as ~131 chaves novas (todo literal `"api.*"` da fonte precisa existir nos 4 catálogos).

## Provas HTTP reais (4 idiomas)

Contra a API Development (`https://localhost:7143`), variando apenas `X-Odca-Culture`. Duas superfícies: payload de sucesso (registro + confirmação) e erro remanescente (gate de release 503):

- `pt-BR` → registrations **202**: `{"registrationId":"…","message":"Cadastro recebido. Confirme o e-mail para liberar a organização em estado comercial pendente.","developmentConfirmationToken":"…"}` · confirm-email **200**: `{"message":"E-mail confirmado. A organização está acessível com contratação pendente."}` · gate **503**: `{"type":"https://odca.local/problems/release-feature-disabled","title":"A Central de importações não está disponível nesta versão.","status":503,"detail":"O recurso será habilitado em uma versão futura. Documentos, dados e histórico já existentes permanecem preservados.","code":"release_feature_disabled","feature":"ContractImports"}`
- `en-US` → **202** `{"message":"Registration received. Confirm your email to release the organization in pending commercial state.",…}` · **200** `{"message":"Email confirmed. The organization is accessible with pending onboarding."}` · **503** `{"title":"The Import Center is not available in this version.","detail":"This feature will be enabled in a future version. Existing documents, data and history remain preserved.","code":"release_feature_disabled",…}`
- `fr-FR` → **202** `{"message":"Inscription reçue. Confirmez votre e-mail pour libérer l'organisation en état commercial en attente.",…}` · **200** `{"message":"E-mail confirmé. L'organisation est accessible avec un abonnement en attente."}` · **503** `{"title":"Le Centre d'importations n'est pas disponible dans cette version.","detail":"Cette fonctionnalité sera activée dans une version future. Les documents, données et historique existants restent préservés.","code":"release_feature_disabled",…}`
- `es-ES` → **202** `{"message":"Registro recibido. Confirme el correo electrónico para habilitar la organización en estado comercial pendiente.",…}` · **200** `{"message":"Correo confirmado. La organización está accesible con la contratación pendiente."}` · **503** `{"title":"El Centro de importaciones no está disponible en esta versión.","detail":"Este recurso se habilitará en una versión futura. Los documentos, datos e historial existentes permanecen preservados.","code":"release_feature_disabled",…}`

Nota operacional: `POST /api/v1/onboarding/registrations` tem a política `privacy-intake` (5 req/10 min/IP) — a primeira captura de 4 idiomas bateu em 429 no meio da janela (pedidas de probe somadas); restart da API zerou o limitador em memória e as 4 culturas completaram em uma única passada (4 ≤ 5). O corpo 429 ("Too Many Requests" + traceId) é padrão do framework e ficou fora de escopo desde o D-OC27.

## Gates

- Build `Odca.sln`: **0W/0E**.
- Domain: **358/358 PASS**.
- Integração em banco zerado via runner commitado `scripts/qa/run-integration-tests.ps1 -FreshDb`: **182/182 PASS — zero falhas** (linha de base D-OC27: 178; +4 casos e2e de duas culturas).
- Paridade de catálogos: **4/4** (1317 chaves por idioma).

## Decisões e limites de escopo

- `odca.sql` **não mudou**: canônico PT intacto (checksums de D-OC26/D-OC27 preservados).
- Sem push/merge/deploy automático.
- Texto de 429/500 genéricos do framework: fora de escopo (o 500 custom do Program.cs já saiu localizado no D-OC27).

## Próximos passos

- Push da branch quando o usuário pedir.
- Dedup cosmética das regras duplicadas de `.studio-grid` em max-width:1100px (backlog).
