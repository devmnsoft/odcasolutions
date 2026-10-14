# D-OC30 — Localização dos erros emitidos pelo framework (429 rate limiting + 500 não tratado)

Data: 14/10/2026 · ramo `codex/s00-foundation` (base `ea3ead3` = D-OC29) · não pushado

## Objetivo

Último item do backlog: os textos de usuário gerados **pelo próprio framework** (não por controllers) — o 429 do rate limiting e o 500 de exceção não tratada — passam a seguir o catálogo via `X-Odca-Culture`, com código estável no wire. Regra da casa mantida: nada inventado; valores pt-BR canônicos.

## Sondagem do estado real (antes)

- **429**: o `RateLimitingMiddleware` do .NET 10 **já emite problem+json pela fábrica registrada** (a presença de `traceId` no corpo confirma que passa pelo `CustomizeProblemDetails` do D-OC27) — mas com `title` hardcoded `"Too Many Requests"` (en-US) **nas 4 culturas**, sem `detail` e sem código estável:

```
HTTP/1.1 429 Too Many Requests
Content-Type: application/problem+json

{"title":"Too Many Requests","status":429,"traceId":"…"}   ← idêntico em pt-BR/en-US/fr-FR/es-ES
```

- **500**: já coberto pelo D-OC27 (`CustomizeProblemDetails` trata 500/502/503: título localizado + `code = api.internal.error`). Nenhum ponto de produto mudaria neste incremento — o que faltava era **fixar o contrato em teste** com um caminho de falha determinístico.

## Correção (mínima, 2 arquivos de produto + 4 catálogos)

- **`Program.cs` — extensão do MESMO hook do D-OC27** (`AddProblemDetails.CustomizeProblemDetails`) para 429: título `api.rate_limited`, detalhe `api.rate_limited.detail` (resolvidos por `LocalizationService` + `ApiCulture.For`), `code = rate_limited`. Convenção de código: curto, como os gates de transporte (`release_feature_disabled`, `permission_denied`) — o 429 nasce no middleware, não num controller (códigos de negócio usam a chave cheia, ex.: `api.internal.error`).
- **`Program.cs` — endpoint diagnóstico `/api/v1/diagnostics/fault`** (somente Development, anônimo): lança `InvalidOperationException` determinística para homologar o pipeline 500 real (`UseExceptionHandler()` — variante rethrow — + `CustomizeProblemDetails`). Ausente em Production/Testing.
- **Catálogos (+2 chaves × 4 idiomas)** → **1319 por idioma**, paridade 4/4 (validada em teste).

Novos textos (não havia texto prévio para espelhar — o framework emitia zero guidance no 429):
| chave | pt-BR | en-US | fr-FR | es-ES |
|---|---|---|---|---|
| `api.rate_limited` | Muitas requisições. | Too many requests. | Trop de requêtes. | Demasiadas peticiones. |
| `api.rate_limited.detail` | O limite de requisições para este endereço foi atingido. Tente novamente em instantes. | The request limit for this address was reached. Please try again shortly. | La limite de requêtes pour cette adresse a été atteinte. Veuillez réessayer dans un instant. | Se alcanzó el límite de peticiones para esta dirección. Inténtelo de nuevo en unos instantes. |

## Estabilidade de wire (o que NÃO muda)

- Status 429 (`RejectionStatusCode` preservado), `Content-Type: application/problem+json`, `traceId`, URL `type` do 500, `code = api.internal.error` do 500, políticas (`login` 5/min/IP, `privacy-intake` 5/10 min/IP) e particionamento por IP.
- 401/400/503 dos gates intactos. O 429 do framework **não traz `Retry-After`** (observado antes e depois — comportamento do middleware, fora de escopo).

## Fatos de teste novos (`FrameworkErrorLocalizationTests`, +4 casos)

- **429 (pt-BR/en-US)**: 5 logins inválidos (401 — já localizados no D-OC27) consomem a janela de 5/min/IP; a 6ª asserciona `429`, `application/problem+json`, `title`/`detail` locais, `status:429`, `code:"rate_limited"`.
- **500 (pt-BR/en-US)**: host em ambiente **não-Testing** (o `Program.cs` registra `UseExceptionHandler()` apenas fora do Testing) + endpoint diagnóstico → `500` com `title` localizado, `status:500`, `code:"api.internal.error"`.
- **Detalhe empírico registrado**: `UseExceptionHandler()` (sem argumentos) é a variante *rethrow* — ela grava o problem+json e só então relança; o TestServer retorna a **resposta completa** quando a exceção é lançada após o início da resposta (`Response.Started`), então o teste lê o corpo normalmente.

## Provas HTTP reais (4 idiomas)

Contra a API Development (`https://localhost:7143`), variando apenas `X-Odca-Culture`.

**429** (política `login`: 5 requisições consomem a janela por IP; 4 seguintes, nas 4 culturas, todas rejeitadas na mesma janela):

- `pt-BR` → `HTTP/1.1 429 Too Many Requests` · `Content-Type: application/problem+json` · `{"title":"Muitas requisições.","status":429,"detail":"O limite de requisições para este endereço foi atingido. Tente novamente em instantes.","traceId":"…","code":"rate_limited"}`
- `en-US` → `{"title":"Too many requests.","status":429,"detail":"The request limit for this address was reached. Please try again shortly.","traceId":"…","code":"rate_limited"}`
- `fr-FR` → `{"title":"Trop de requêtes.","status":429,"detail":"La limite de requêtes pour cette adresse a été atteinte. Veuillez réessayer dans un instant.","traceId":"…","code":"rate_limited"}`
- `es-ES` → `{"title":"Demasiadas peticiones.","status":429,"detail":"Se alcanzó el límite de peticiones para esta dirección. Inténtelo de nuevo en unos instantes.","traceId":"…","code":"rate_limited"}`

**500** (`GET /api/v1/diagnostics/fault`, anônimo, ×4 culturas):

- `pt-BR` → `{"type":"https://tools.ietf.org/html/rfc9110#section-15.6.1","title":"Erro interno do servidor.","status":500,"traceId":"…","code":"api.internal.error"}`
- `en-US` → `{"type":"…rfc9110#section-15.6.1","title":"Internal server error.","status":500,"traceId":"…","code":"api.internal.error"}`
- `fr-FR` → `{"type":"…rfc9110#section-15.6.1","title":"Erreur interne du serveur.","status":500,"traceId":"…","code":"api.internal.error"}`
- `es-ES` → `{"type":"…rfc9110#section-15.6.1","title":"Error interno del servidor.","status":500,"traceId":"…","code":"api.internal.error"}`

## Gates

- Build da solution (`Odca.sln`): **0W/0E**.
- Domain: **358/358 PASS**.
- Integração em `odca_test_integration` zerado via `scripts/qa/run-integration-tests.ps1 -FreshDb`: **186/186 PASS — zero falhas** (baseline 182 + 4 novos; FRESH-DB-OK → BUILD-OK → TEST-EXIT=0).

## Fora de escopo / backlog

- 413 do Kestrel (tamanho máximo de requisição — resposta sem corpo) e 404/405 (sem texto de usuário) não têm texto para localizar. **Backlog zerado** — nenhum item cosmético pendente. Sem push/merge/deploy automático.

## Próximos passos

- Push da branch quando o usuário pedir.
