# Sprint 17 — conta e resíduo em 4 idiomas: Account + MFA + navegação + importações + fallback global (D-OC51) (19/10/2026)

Entrega o fechamento da superfície de conta (login, minha conta, troca de senha, MFA — inscrição/desafio/códigos de recuperação — e acesso negado), o controller de conta, o middleware de erro global (`BffErrorHandlingMiddleware` com fallback HTML/JSON localizado e `lang` dinâmico), as últimas 4 chaves de navegação que faltavam no catálogo (3 grupos com acento + `central-notificacoes`), os resíduos de aria-label restantes em 10 views, os rótulos do toggle de senha do `forms.js` — e a **Central de importações** (`Imports/Index`, `Imports/Review` e `ImportsController`), área que nenhuma sprint anterior havia coberto e que um scan global desta sprint revelou ainda com literais pt-BR. Com isso **fecha o inventário pt-first da UI Razor**: scan global final de todos os views mostra 52 linhas com acentos — 49 comentários de desenvolvedor + 3 endônimos do seletor de idioma; **zero literal visível**. A auditoria de design ao vivo é estendida de 46 para 49 rotas (+`login`, +`minha-conta`, +`importacao`). Restam apenas dívidas registradas fora do Razor: atributos `[Required]/[Display]` das VMs, mensagens de domínio vindas da API e valores de domínio crus passados através.

## 1. Escopo e base

- **Base**: `codex/s00-foundation` @ `2880156` (Sprint 16 / D-OC50). Sem nova migração (canônico v062 intacto) e sem mudanças em API/Domain/Worker/assets/SQL. Única mudança de teste: `AvisosEConfirmacaoRenovacaoTests` passa a injetar `new LocalizationService()` na instanciação do middleware (novo parâmetro do construtor).
- **Delta versionado**: `scripts/i18n/d-s17-conta-residuo.json` com **163 chaves × 4 culturas = 652 entradas** → catálogos em **4014 chaves/cultura, paridade 4/4** (era 3851; 0 colisões; BOM+LF, 0 CR, 0 `\uXXXX` literal, 0 double-encoding).
  - Bloco conta/middleware/navegação/resíduo (83): `acct.*` 72, `bff.*` 4, `group_*` 3, `sol.*` 2, `cons.plan_summary_aria` 1, `central-notificacoes` 1.
  - Bloco importações (80): `imp.*` 35 (views + controller + 6 diagnósticos), `imp_rev.*` 45.
- Padrão canônico S14–S16 mantido: `var _culture=(string?)Context.Items[CultureMiddleware.ItemsKey]` (ou `_c` nos arquivos no estilo S16) no topo da view + `@L.T("chave",_culture)`; controllers usam `LocalizationService` injetado + `private string? Culture => HttpContext.Items[CultureMiddleware.ItemsKey] as string; private string L(string key, params string[] args) => loc.T(key, Culture, args);`.
- Reuso de chaves já existentes (sem duplicar): `minha-conta` (título/eyebrow da conta), `admin.summary_aria` ("Resumo da solicitação"), `sheet.details_aria` ("Detalhes do contrato"), `common.breadcrumb_aria` ("Navegação estrutural") ×8, `notificacoes`; nas importações: `inbox.how_to_use` ("Como usar"), `documents.filter_from`/`documents.filter_to` ("De"/"Até"), `action.filter` ("Filtrar"), `studio.cat_retry` ("Tentar novamente"), `inbox.type_all` ("Todos"), `group_contratos` (eyebrow — o `.eyebrow { text-transform: uppercase }` preserva o visual original em maiúsculas).

## 2. F-1 — views de conta (7 arquivos reescritos)

| View | Call sites | Conteúdo |
|---|---|---|
| `Account/Login` | 12 | eyebrow/título/card, link "Conheça os planos publicados", **toggle de senha** com `data-toggle-show`/`data-toggle-hide` localizados (consumidos pelo `forms.js`), `data-processing-label` localizado |
| `Account/Conta` | 19 | header, seção senha (sub + botão alterar), seção MFA com ambos os estados (pendente → ativar; ativo → regenerar chaves), seção notificações (ver notificações), help card |
| `Account/ChangePassword` | 5 | eyebrow/hint/título/botão salvar |
| `Account/MfaEnrollment` | 12 | notice de chave regenerada, rótulos do `dl` (QR Code, chave manual, URI TOTP), alt da imagem QR, confirmar MFA, gerar nova chave, ajuda de uso |
| `Account/MfaChallenge` | 5 | eyebrow/h/sub/botão verificar |
| `Account/RecoveryCodes` | 6 | eyebrow/h/sub, `aria-label` da lista, continuar |
| `Account/AccessDenied` | 3 | título/body |

Total: **62 call sites** nas 7 views (algumas chaves aparecem 2× — título `ViewData` + `<h1>`). Estrutura HTML e classes inalteradas; apenas conteúdo de texto/atributos trocado.

## 3. F-1 — controller e middleware

- **`AccountController` (19 call sites)**: 4 blocos `switch` de status de API (login, alterar senha, inscrição MFA, desafio MFA) + sucesso de alteração de senha + falha de regeneração de chave + aviso de logout sem revogação remota confirmada. Mesma política D-OC45: `UserMessage` da API quando presente, senão chave canônica por `ApiCallStatus` (`err_rate_limited`/`err_rate_limited_login`/`err_timeout`/`err_unavailable`/`err_session_expired`/`err_credentials`/`err_no_perm_password`/`err_change_pass`/`err_code_invalid`/`err_code_used`/`ok_session_revoked`).
- **`BffErrorHandlingMiddleware`**: injeção de `LocalizationService` no construtor primário; fallback JSON (consumidores SPA/JSON) e fallback HTML bruto localizados (`bff.unavailable_title`/`bff.unavailable_detail`/`bff.btn_close`/`bff.btn_back_inbox`); **`<html lang>` dinâmico** (`culture ?? "pt-BR"` — ver decisão §7.3). O método `WriteFallbackHtmlAsync` saiu de `static` para instância.

## 4. Navegação — análise, não código

`NavigationRenderer.Render` já resolve rótulos via `ctx.Localizer?.Invoke(key) ?? fallback` e o `_Layout` já liga `Localizer = key => L.T(key, culture)`. Não houve mudança de C#: faltavam apenas **4 chaves** no catálogo, verificadas por extração programática do `NavigationRegistry.cs` real em disco (42 item keys + 7 group labels + 17 `AriaKey nav.aria_*`) aplicando a fórmula exata de grupo do renderer (`"group_" + label.Trim().ToLowerInvariant().Replace(" ","_").Replace("&","").Replace("é","e").Replace("ê","e").Replace("á","a").Replace("â","a")`): `group_início`, `group_operações`, `group_minha_organização_e_plano` (acentos `ç`/`ã`/`í` sobrevivem à fórmula; espaços viram underline) e `central-notificacoes`. Verificação final: **66/66 chaves resolvem nos 4 catálogos** (script confere codepoints; saída `NAV-ALL-PASS`).

## 5. F-2 — Central de importações (2 views + controller)

| Arquivo | Call sites | Conteúdo |
|---|---|---|
| `Imports/Index.cshtml` | 34 | header/eyebrow (reuso `group_contratos`)/help card (reuso `inbox.how_to_use`), nota "Disponível em breve" split `<strong>`, filtros completos (estado com reuso "Todos", minhas/aguardando, De/Até com reuso, Filtrar/Tentar novamente com reuso), erro de carregamento com passthrough do detalhe da API, empty state, lista (solicitada por **com data culturalizada via `CulturedTime.DateTime`** — substitui o formato fixo `dd/MM/yyyy HH:mm`), etapa atual + 3 mensagens condicionais de retomada, botões Continuar revisão/Abrir; função `ImportDiagnostic` migrou de `static` em `@functions` para **função local no bloco `@{}`** para capturar a cultura (mesma técnica do S16 `ModuleName`) |
| `Imports/Review.cshtml` | 45 | header/help, barra de progresso 5 passos + tabs mobile (aria), resumo com contagem `{0}` InvariantCulture e estados possíveis (markup `<strong>` preservado em 2 chaves), painel do documento (iframe title, help do visualizador, security_status cru passthrough), formulário de conferência manual (6 rótulos + ajuda + botão), sugestões da extração (método/estado, valor extraído/escolhido com hint, trecho de origem, página/não identificada, decisões aceitar/editar/rejeitar), mensagens de extração ausente, confirm() dos dois formulários finais com chaves dedicadas (sem aspas retas no valor FR — decisão §7.6) |
| `ImportsController.cs` | 11 | título Index/Review, erro de intervalo de datas, fallback de carregamento, 2 resultados de dados manuais, 2 resultados de confirmação (já confirmada × importada), erro de confirmação, 2 resultados de cancelamento |

Total: **90 call sites**. Valores de domínio crus seguem passthrough registrado como dívida (Status, CurrentStep, review_status, method, security_status, field_name, document display name).

## 6. Resíduo global (13 locais / 10 views) + forms.js

- 8× `aria-label="Navegação estrutural"` → `common.breadcrumb_aria`: `Solicitacoes/{Index,Nova,Detalhes}`, `Modules/Index`, `PlatformAudit/Index`, `PlatformLibrary/{Index,NewRevision}`, `PlatformOperations/Index`.
- `Solicitacoes/Index`: `Filtros da central` → `sol.filters_aria`; `Lista de solicitações` → `sol.list_aria`.
- `Solicitacoes/Detalhes`: `Resumo da solicitação` → `admin.summary_aria` (reuso).
- `Contracts/Sheet`: `Detalhes do contrato` → `sheet.details_aria` (reuso).
- `Consumption/Index`: `Resumo do plano` → `cons.plan_summary_aria`.
- **`wwwroot/js/forms.js`**: o toggle de senha lia strings fixas; agora lê `data-toggle-show`/`data-toggle-hide` do botão (fallback pt-first quando ausentes — pt-first by design). O rótulo "Processando…" segue o padrão S16 via `data-processing-label`.
- **Varredura final global** (todos os views + todos os JS): scan de strings entre aspas = **0 ocorrências**; scan de qualquer caractere acentuado = **52 linhas, todas classificadas**: 49 comentários de desenvolvedor (`//`, `<!--`, `@* *@`) + 3 endônimos do seletor de idioma do `_Layout` (Português (BR), Français, Español — nomes nativos por convenção). **Zero literal visível restante na UI Razor.**

## 7. Evidência

### GATE

| Verificação | Resultado |
|---|---|
| Build Release `-warnaserror` sln | 0W/0E ✓ |
| Build Debug `-warnaserror` sln | 0W/0E ✓ |
| Domain tests | 389/389 ✓ (com nova instanciação do middleware) |
| Integration `-FreshDb` | 236/236, TEST-EXIT=0 ✓ (rodada limpa após o d41 r3 liberar o Postgres) |
| check-assets.mjs | exit=0 ✓ |
| Catálogos | 4014 chaves/cultura, paridade 4/4, BOM+LF, 0 CR, 0 `\uXXXX` literal, 0 double-encoding ✓ |
| Delta↔catálogo | merge idempotente: 163 added / 0 colisões ×4 ✓ |
| Chaves usadas | 820 referências únicas em 22 arquivos (18 S17 + 4 da dif S16, incluindo `Consumption/Index` com a aria re-aplicada) conferidas vs catálogo real 4014: **0 ausentes** (KEYS-ALL-OK) ✓ |
| Chaves de navegação | 66/66 resolvem nos 4 catálogos (NAV-ALL-PASS, codepoints) ✓ |
| Preservação estrutural | dif de multiset de tags HEAD(`2880156`)×trabalho nos 19 views = **0/0 em 19 arquivos** (TAGDIFF-S17-OK) ✓ |
| d41 ao vivo estendido (764 medidas) | rodada **r4b** (reexecução integral após a falha ambiental da r4 — §7 item 7): `AUDIT-DONE checks=764 ok=764 overflow=0 authfail=0 httpfail=0` (exit 0), 0 PNGs de falha; transcript commitado `evidence/d41-design-audit-s17-rodada-final.txt` |

### F-3 — auditoria de design estendida 46 → 49 rotas

Entram as três superfícies com maior mudança visual desta sprint, que antes não tinham rota fixa: `login` (`/entrar`, sessão anônima), `minha-conta` (`/minha-conta`, sessão cliente) e `importacao` (`/organizacoes/{fixture}/importacoes`, sessão cliente) ×4 idiomas ×4 viewports = **+12 pares** → **191 pares rota×idioma × 4 viewports = 764 medidas** (era 179/716). Demais mudanças desta sprint nas rotas auditadas são atributos não visuais (aria-label) — não alteram overflow.

### Classificação QA (não é do produto)

1. **Corrupção de canal nos escapes `\uXXXX` do gerador v3**: 3 chaves `group_*` chegaram ao delta com backslash literal e 2 valores vazios. Regeneração final via montagem 100% ASCII + `String.fromCharCode` (sem regex nem escapes no fonte do script) + validação por codepoint dump arquivo a arquivo.
2. **Espaço × underline na chave de grupo**: delta/catálogo carregaram `group_minha_organização e plano` (espaço literal) — a fórmula do renderer converte espaço em underscore. Capturado pelo teste de paridade existente (`chave com espaco`); corrigido delta + 4 catálogos e reverificado por codepoint nas 4 culturas (7 chaves de grupo ×4 = GROUPS-4x-PASS).
3. **CS7036 no teste de domínio**: `AvisosEConfirmacaoRenovacaoTests` instanciava o middleware sem o novo parâmetro → passou a injetar `new LocalizationService()` (construtor tolerante: catálogos ausentes ⇒ fallback pt-BR). Nenhuma asserção mudou.
4. **Flake de banco sob carga**: `OutboxLeaseClaimTests.ExpiredLease…` falhou em duas rodadas da suíte rodando **em paralelo** à auditoria d41 (Postgres compartilhado); isolado passa 2/2 — a rodada limpa completa é feita após o fim do audit.
5. **Catálogos lidos da cópia em bin**: o teste de paridade lê `Localization/*.json` do output — editar o JSON fonte exige rebuild (regra consolidada; rodadas intermediárias usavam cópia anterior, o que explicava "falhas" que sumiam no rebuild).
6. **`État` e apóstrofo francês via codepoint**: o valor `imp.lbl_state`/fr-FR chegou sem a circumflexa pelo canal de escrita — corrigido e validado por codepoint (69,116,97,234). O `imp_rev.confirm_msg`/fr-FR usa apóstrofo tipográfico U+2019 ("l’importation") porque o valor é embutido em string JS simples inline (`onsubmit="return confirm('…')"`) — U+0027 quebraria o script; U+2019 é correto tipograficamente em francês e neutro para JS/HTML.
7. **Falha ambiental na rodada r4 do d41 + endurecimento do driver**: a rodada r4 (transcript em `evidence/d41-design-audit-s17-r4-falha-ambiental.txt`) terminou com `AUDIT-DONE checks=764 ok=692 overflow=72 authfail=0 httpfail=0` (exit 1) — mas as 72 medidas marcadas eram a **página de erro `ERR_CONNECTION_REFUSED` da Chromium** (título `"localhost"`, largura fixa 908px), não páginas do produto: o processo Web (`localhost:5144`) morreu por causa externa em plena execução (~80% da rodada, sem qualquer registro no stderr — nenhum crash do dotnet) e o driver, desprovido de detecção de falha de rede, classificava o erro como OK nos viewports 360/1280/1440 (a página de erro "encaixava") e como OVERFLOW no 768 (908px > 768px, delta=140px idêntico nas 18 rotas finais ×4 idiomas). Todas as ~690 medidas contra servidor vivo: **0 overflow, 0 authfail** — nenhuma regressão de design. Endurecimento aplicado ao driver (`scripts/qa/cdp-design-audit.mjs`, entra neste commit): `responseStatus` vazio/0 agora vira **HTTPFAIL** (com PNG `_httpneterr`) em vez de OK/OVERFLOW falso, e um circuit breaker encerra a rodada com exit 2 após 4 falhas de rede consecutivas — uma queda futura de servidor produz evidência falante, não meia auditoria mascarada. A rodada final da sprint é a **r4b**, reexecutada integralmente sobre a mesma árvore.

## 8. Decisões registradas

- **`bff.*` separadas de `unavailable.*`**: a view Razor `/erro-indisponivel` (chaves `unavailable.*`, localizada desde S9) e o fallback bruto do middleware (sem contexto Razor, string crua) mantêm famílias distintas com texto equivalente. Duplicação consciente e documentada: o fallback do middleware é o último recurso da pipeline e não pode depender de view engine.
- **Toggle de senha via data-attributes**: rótulos passam da JS fixa para `data-toggle-show`/`data-toggle-hide` renderizados pela view; a JS mantém fallback pt-BR quando o atributo não existe (princípio pt-first: nenhum usuário fica sem rótulo).
- **Ordem da pipeline preserva fallback correto**: `BffErrorHandlingMiddleware` entra **antes** da `CultureMiddleware` (posição pré-existente, inalterada); erros capturados antes da cultura existir caem em `culture=null` → `T` resolve pt-BR e `lang="pt-BR"` — comportamento correto por construção, não por acoplamento.
- **Constantes `[LoggerMessage]` permanecem em PT**: são constantes de compilação voltadas a operador/desenvolvedor (não a usuário final); mesma política dos logs das demais sprints.
- **Endônimos do `_Layout` mantidos**: nomes nativos das línguas são marcadores de identidade da língua-alvo, não strings traduzíveis — mantidos por convenção (dívida S16 reclassificada como decisão).
- **Data culturalizada em `Imports/Index`**: o formato fixo `dd/MM/yyyy HH:mm` (pt-BR hardcoded) foi substituído por `CulturedTime.DateTime(value,_c)` — dívida de formatação eliminada nesta sprint junto da localização.
- **`Consumption/Index` pertence ao commit S16**: o arquivo foi 100% localizado na S16; para manter a árvore S16 autossuficiente (a chave `cons.plan_summary_aria` é do delta S17), a linha de aria-label foi revertida ao literal pt-BR antes do commit S16 e re-aplicada como dif da Sprint 17 — a mudança viaja neste commit.
- **Reuso canônico nas importações**: 6 chaves existentes reaproveitadas (lista §1) em vez de duplicar; o eyebrow "CONTRATOS" reutiliza `group_contratos` e o CSS `.eyebrow { text-transform: uppercase }` preserva o visual.

## 9. Cobertura honesta e dívidas mantidas

**Coberto por rota fixa (auditada)**: `/entrar`, `/minha-conta` e `/organizacoes/{fixture}/importacoes` (novas) + todas as 46 anteriores.

**Não coberto por rota fixa** (GETs internos/estados POST-only): `importacao/{id}/revisao` (exige id de importação específica), `mfa/inscricao` (exige sessão MFA-pendente), `mfa/desafio` (exige fluxo de login), `recuperacao-codigos` (pós-inscrição), `alterar-senha` (autorizado), `acesso-negado`. Cobertura indireta: build estrito 0W/0E, todas as chaves referenciadas conferidas contra os 4 catálogos, varredura global de acentos = 0 visível, suíte inalterada. Risco avaliado baixo (strings = mesmas famílias auditadas).

**Dívida pt-first restante (reduzida a cross-camada, zero no Razor visível)**:
- Atributos `[Required(ErrorMessage)]`/`[Display(Name)]` das VMs (~54 instâncias) — política cross-área, próxima sprint ou com S21 hardening.
- Mensagens de domínio vindas da API em pt (`ErrorDetail`/`ErrorTitle` passthrough, `BillingLimitation`/`Reason`) — exigem trabalho na API, fora do escopo Razor.
- Valores de domínio crus passados através (status/steps/codes) — política de todas as sprints; mapeados por chave onde possível.
- **64 classes CSS usadas em views sem definição em `site.css`** (inventário mapeado em `SPRINT-ROADMAP-AI.md`) → S20 QA de design.

**Roadmap registrado** (`docs/execution/SPRINT-ROADMAP-AI.md`, atualizado nesta sprint com as diretrizes do usuário de 19/10 — trilha estendida para mais 50 sprints): S18 fundações IA (`Odca.AI`, resumo executivo, classificação automática, detecção de cláusulas), S19 IA avançada (scoring de risco, comparação com template, renovação preditiva, busca semântica), S20 QA de design (contraste texto×fundo WCAG AA, formas, responsivo 360px, as 64 classes CSS inventariadas), S21 manual em todas as páginas + linguagem para leigos, S22 regras de negócio × legislação brasileira (LGPD/CDC/Código Civil, CNPJ/CPF real) com extensão internacional documentada, S23 banco em script único com import one-shot verificado + runbook, S24 hardening E2E (cálculos, CRUD, concorrência, segurança), S25 dress rehearsal do piloto + Go/No-Go, S26+ runway contínuo.

## 10. Separação de evidências e portal

- **Testes locais**: GATE completo (builds, Domain, Integration, assets, catálogos, chaves, navegação) + d41 ao vivo estendido (transcript em `evidence/d41-design-audit-s17-rodada-final.txt`).
- **CI oficial**: não verificada (sem push autorizado neste ciclo).
- **Aceite humano**: pendente (conta + MFA + importações em 4 idiomas).
- **Portal**: `.odca/progress.json` inalterado em 40% (baseline/IDs/pesos intactos, sem marco credenciável).
