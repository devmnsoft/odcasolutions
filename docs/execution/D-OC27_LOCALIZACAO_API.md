# D-OC27 — Localização total da API (erros em pt-BR/en-US/fr-FR/es-ES via catálogo)

Data: 14/10/2026 · ramo `codex/s00-foundation` (base `9eda889` = D-OC26) · não pushado

## Objetivo

Toda mensagem de erro da API sai no idioma do usuário, com **código estável no wire** e texto resolvido por **catálogo**. A cultura viaja do BFF para a API no header `X-Odca-Culture`. A camada SQL permanece **canônica em pt-BR** (`odca.sql` inalterado neste incremento): a localização acontece somente na fronteira da API. Regra da casa mantida em tudo: **texto desconhecido volta como veio** — nada é inventado.

## Design

- **`LocalizationService` (compartilhada, `Odca.Configuration`)**: construtor sem parâmetros; carrega `Localization/{lang}.json` de `AppContext.BaseDirectory` (os catálogos já viajam com cada app); fallback por chave para pt-BR; chave ausente ⇒ a própria chave (nunca texto fabricado).
- **Cultura BFF→API**: `CultureMiddleware` (Web) resolve o cookie `odca_lang` e grava `HttpContext.Items["culture_code"]`; `OdcaApiClient.ApplyCulture` anexa `X-Odca-Culture` em toda chamada autenticada (`CreateAuthorized`) e no `LoginAsync`. Sem `HttpContext` (fundões fora de request), o header não vai — a API cai em pt-BR.
- **Fábricas de erro (`Odca.Api.Localization.OdcaErrors`)**: extension helpers em `ControllerBase` — `Culture()` (lê `X-Odca-Culture`, fallback pt-BR), `T(key, args)` (resolve pelo catálogo via `HttpContext.RequestServices`), além de Invalid/Field/Fields/Conflict/Status/Errors. Convenções da casa preservadas: receiver `this.` explícito, e `new ObjectResult(new ProblemDetails{…}) { StatusCode }` em vez de `Problem(ProblemDetails,…)` (a sobrecarga `Problem(…)` do MVC re-titula o objeto pronto).
- **`ApiMessages` (mapa reverso PT canônico → chave)**:
  - `KnownKeys`: mensagens exatas dos serviços/validações;
  - `TemplatePatterns[24]` + `TemplateKeys[24]`: mensagens dinâmicas (nome de campo, limite de bytes/pixels, plano…) com grupos de captura que alimentam placeholders `{n}` do catálogo;
  - `MfaSuffixes[9]` × prefixo de escopo (`api.auth.mfa_confirm_` / `api.auth.mfa_challenge_`) = **18 chaves concretas** de MFA;
  - `Localize(localizer, text, culture, args)` resolve; `null` ⇒ chamador mantém o original.
- **`ApiSqlMessages` (envelopes jsonb)**: mapa `(code, message)` → `api.sql.*` para os **37 envelopes** canônicos de `database/odca.sql`. Códigos multi-mensagem usam slug estável por mensagem (ex.: `solicitations.transition.triage_before_reply`); sem mapeamento ⇒ texto original preservado.
- **Varredura**: todos os 27 controllers da API converteram seus erros para o padrão código-estável + título/detalhe/erros localizados; o handler de 500 do `Program.cs` também saiu localizado.

## Catálogo de erros

- **+437 chaves `api.*`** nos quatro catálogos (`pt-BR/en-US/fr-FR/es-ES.json`): 749 chaves BFF pré-existentes + 437 = **1186 por idioma**, paridade de chaves **4/4** (teste existente + auditoria de merge).
- **Origem dos valores pt-BR (precedência)**: (1) originais verificados manualmente contra o código-fonte (inclui títulos novos deliberados do incremento); (2) inversão exata de `ApiMessages.KnownKeys`/templates/MFA e `ApiSqlMessages`; (3) diff-register + resolvidor de hunk genérico sobre `git diff 9eda889`. Resultado final: **MISSING 0 / EXTRAS 0**.
- **Decisões registradas**:
  - *Quirk pré-existente "Ombra"* em `renewals.currency_without_value` ("Moeda proposta sem valor correspondente. Ombra a moeda atual não será alterada.") — mantido **byte-idêntico** no pt-BR (camada canônica não muda sem decisão de produto); EN/FR/ES traduzem o restante mantendo o rótulo "Ombra:".
  - **Chaves compartilhadas neutralizadas**: `api.solicitations.message_invalid` e `api.solicitations.action_invalid` (usados por Solicitations e PlatformSolicitations com textos divergentes) receberam valor único neutro no catálogo.
  - Originais interpolados viraram valores com `{0}`/`{1}` onde o call site atualiza argumentos: `platform_catalog.type_invalid`, `catalog.not_found`, `change_request_invalid_status`, `template.key_exists`, `template.revision_conflict`, `documents.file_too_large.limit_detail/.stream_detail`, `patients.validation.max_length`, `draft_template_conflict`.

## Defeitos reais achados pela cobertura nova (e corrigidos)

1. **Regex malformado em `ApiMessages.TemplatePatterns`** (`api.studio.formula_terms_only_calculated`): `\('(.*)')\.$` com parêntese de fechamento **sem escape** → `RegexParseException` no `..cctor()` → `TypeInitializationException` em **todo** caminho que tocasse `ApiMessages` (qualquer erro localizado em runtime). Invisível ao build; descoberto pelo teste de completude. Correção: `\)` na linha do padrão.
2. **Invocação direta de controllers sem `RequestServices`** — a varredura passou a resolver `LocalizationService` via `HttpContext.RequestServices`, nula em todo contexto hand-built: `PatientsControllerTests` (×4, Domain) + helpers de `PlatformCustomerCreationTests`, `DocumentCentralAndStudioTests`, `DapperMaterializationTests` e `ReviewAuthorizationBehaviorTests` (Integração). Correção: helper compartilhado `TestControllerContexts.Create(actorId, extraClaims)` (DI mínima com `LocalizationService` real — catálogos já copiados ao output do teste — + `DefaultProblemDetailsFactory`) adotado pelos 4 arquivos de integração; o helper de Domain registra os mesmos serviços inline. Detalhe de plataforma registrado: no .NET 10 `ProblemDetailsFactory` é **classe abstrata** (não interface) e `MvcProblemDetailsFactory` deixou de existir; o concreto público é `DefaultProblemDetailsFactory(IOptions<ApiBehaviorOptions>, IOptions<ProblemDetailsOptions>?)`.
3. **Shadowing do extension `OdcaErrors.Conflict`** (27 sites em 9 controllers): `this.Conflict("api.…")` ligava ao **método de instância** `ControllerBase.Conflict(object?)` (métodos de instância têm precedência sobre extensions) → o body 409 era a **chave crua do catálogo** como string JSON (`"api.renewals.apply_not_ready"`), e não um ProblemDetails — regressão de forma de wire vs. 9eda889, onde todos esses sites retornavam `Conflict(new ProblemDetails{…})`. Correção: qualificação explícita `OdcaErrors.Conflict(this, "api.…")` nos 27 sites (a fábrica retorna `ConflictObjectResult(ProblemDetails)` com título localizado + `code` aditivo). Nenhum outro membro de `OdcaErrors` colide com método de instância de `ControllerBase`.

## Fatos de teste novos

- **`ApiLocalizationCoverageTests` (3 fatos, sem DB)**:
  1. todo literal `"api.*"` em `src/Odca.Api/**/*.cs` (scanner idêntico ao usado na construção do catálogo; piso ≥400 contra regressão do próprio scanner; 3 literais não-chave documentados: exemplo de doc-comment `api.area.erro` + os 2 prefixos MFA) existe nos 4 catálogos;
  2. cada envelope `jsonb_build_object('error','…','code','…')` re-extraído de `database/odca.sql` (≥37; regex tolera argumentos extras após o código — o envelope `solicitations.stale` leva `currentRowVersion`) resolve por `ApiSqlMessages.KeyFor`, existe nos 4 catálogos, e o valor **pt-BR == bytes do SQL**;
  3. `KnownKeys` + `TemplateKeys` (+ placeholders `{n}` espelhando exatamente os grupos capturantes de cada padrão) + chaves MFA concretas existem nos 4 catálogos.
- **`LocalizationEndToEndTests` (2 casos, DB real)**: mesmo registro inválido em `POST /api/v1/onboarding/registrations` com `X-Odca-Culture` **pt-BR** e **en-US** → 400, `code = api.onboarding.registration_invalid` no wire (**nível superior** — no .NET 9+ as extensions do ProblemDetails serializam como propriedades de nível superior; o BFF sempre leu `code` assim) + título e erros de campo nos dois idiomas.
- **`OdcaApiClientTests` (+3)**: header `X-Odca-Culture` presente com cultura ativa no contexto do BFF (teoria en-US/es-ES) e **ausente** sem `HttpContext`.
- Paridade de conjuntos de chaves 4/4 coberta pelo teste existente `LocalizationCatalogParityTests`.

## Provas HTTP reais (4 idiomas)

Mesma requisição inválida (`POST /api/v1/onboarding/registrations`, `document="abc"`, `responsibleName="A"`, demais campos válidos) contra a API Development rodando em `https://localhost:7143`, variando apenas o header `X-Odca-Culture`. Status 400 nos quatro idiomas; **`code` estável no wire**; título e erros de campo resolvidos pelo catálogo:

- `X-Odca-Culture: pt-BR` →
  `{"title":"Um ou mais campos estão inválidos.","status":400,"errors":{"registration":["Informe CPF ou CNPJ em formato válido.","Informe o nome do responsável."]},"code":"api.onboarding.registration_invalid"}`
- `X-Odca-Culture: en-US` →
  `{"title":"One or more fields are invalid.","status":400,"errors":{"registration":["Provide a valid-format CPF or CNPJ.","Provide the responsible person's name."]},"code":"api.onboarding.registration_invalid"}`
- `X-Odca-Culture: fr-FR` →
  `{"title":"Un ou plusieurs champs sont invalides.","status":400,"errors":{"registration":["Indiquez un CPF ou CNPJ au format valide.","Indiquez le nom du responsable."]},"code":"api.onboarding.registration_invalid"}`
- `X-Odca-Culture: es-ES` →
  `{"title":"Uno o más campos están inválidos.","status":400,"errors":{"registration":["Indique CPF o CNPJ en formato válido.","Indique el nombre del responsable."]},"code":"api.onboarding.registration_invalid"}`

Nota operacional: a política de rate limit `privacy-intake` (5 req/10 min/IP) aceitou as provas sem conflito em uma única janela; o 429 de rejeição é comportamento padrão do framework e fica fora de escopo (mensagens de status/sucesso).

## Gates

- Build `Odca.sln`: **0W/0E**.
- Domain: **358/358 PASS** (355 de base + 3 novos de cultura BFF→API).
- Integração em banco zerado via runner commitado `scripts/qa/run-integration-tests.ps1 -FreshDb`: **178/178 PASS — zero falhas** (linha de base D-OC26: 173; +3 fatos de cobertura sem DB +2 e2e de duas culturas).
- Paridade de catálogos: **4/4** (1186 chaves por idioma).

## Decisões e limites de escopo

- Sucesso/mensagens de resposta (payloads `Ok`/`Accepted`) **fora de escopo** — erros primeiro; os textos de sucesso remanescentes ficam no backlog (PrivacyRequests accepted, hints de Obligations, payloads de readiness, etc.).
- `odca.sql` **não mudou**: canônico PT intacto (checksums de D-OC26 preservados).
- Sem push/merge/deploy automático.

## Próximos passos

- i18n das mensagens de sucesso/resposta além do Estúdio (backlog).
- Dedup cosmética das regras duplicadas de `.studio-grid` em max-width:1100px (backlog).
- Push da branch quando o usuário pedir.
