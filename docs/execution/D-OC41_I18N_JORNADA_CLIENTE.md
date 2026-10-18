# Sprint 7 — jornada do cliente do piloto em 4 idiomas e acessibilidade de navegação global (D-OC41) (18/10/2026)

Plano pós-S6 definido com o usuário ("siga pelas próximas 5 sprints"): **S7** fecha a dívida i18n da jornada do **cliente** no piloto e a dívida de acessibilidade/design da navegação global; **S8** leva a mesma operação às superfícies de **plataforma/operacional**. Nenhuma mudança de esquema de banco nesta sprint — só catálogos i18n (apresentação), views, dois controllers Web e JS de navegação/diálogos.

## 1. Escopo e base

- **Base**: `codex/s00-foundation` @ `d5759f8` (Sprint 6 / D-OC40). Diferente da S6, esta sprint **altera `src/`** (catálogos i18n e apresentação Web); os invariantes canônicos (isolamento, autorização no servidor, MFA, versões imutáveis etc.) não são tocados — toda string migra para `L.T` preservando exatamente o texto atual em pt-BR.
- **F-1 (hub de documentos)**: `Documents/Index.cshtml`, `Documents/NewDocument.cshtml` e mensagens remanescentes de `DocumentsController` passam pelo catálogo (`docs.*`). A view já usava o bloco de assinatura via `documents.stage_*`; estendemos o mesmo namespace ao resto da superfície (filtros, tabela, próximas ações, paginação, empty state, jornada de criação, busca/cadastro rápido de paciente).
- **F-2 (central de obrigações)**: `Obligations/Index.cshtml` (header, escopo, vistas salvas, filtros, tabela, painel lateral, guia "como usar"), `Obligations/Nova.cshtml` (formulário completo + recorrência + lembretes + fallback JS do seletor de cláusula) e `Obligations/Acoes.cshtml` (seis operações) — namespace novo `obl.*`; `ObligationsController` ganha injeção de `LocalizationService` (padrão dos demais controllers: `L(key)` com cultura do contexto) e todos os `TempData`/notificações de operação passam a usar chaves; a view Index renderiza o pacote `obl.*`/`common.*` para `obligations.js` quando o carregar.
- **F-3 (central de privacidade)**: `Privacy/Index.cshtml` (namespace novo `priv.*`) + mensagens de falha/rate-limit/indisponibilidade de `PrivacyController`. Os rótulos de formulário usam `L.T` explícito na view (o modelo mantém suas anotações; validação exibida segue o mecanismo existente).
- **F-4 (design/acessibilidade global)**:
  - `_Layout.cshtml`: `aria-label="Navegação principal"`, sufixos de estado de funcionalidade em `MenuFeature` ("(situação não confirmada)", "(fora do plano)", "(bloqueada)", "(limite atingido)"), "Administração ODCA" e "Escolha uma organização" → `layout.*` novos.
  - `NavigationRegistry`/`NavigationRenderer`: nova propriedade `AriaKey` no `NavItem` — cada aria-label que **diferencia** do rótulo visível ganha chave `nav.aria_*` traduzida nos 4 catálogos (resolvida por `ctx.Localizer`, fallback para o valor atual pt-BR); aria-labels **idênticos** ao rótulo visível são removidos (WCAG 2.5.3: não repetir em `aria-label` o texto já renderizado no link).
  - Páginas compartilhadas: `Shared/Error.cshtml` (hoje todo em inglês → `error.*`), `AccessDenied.cshtml` (`accessdenied.*`), `ServiceUnavailable.cshtml` (`unavailable.*`), `_ConfirmDialog.cshtml` (`dialog.*`) e `_Toasts.cshtml` (botões e aria-labels → `toast.*`).
  - `wwwroot/js/navigation.js`: "Expandir menu"/"Recolher menu" passam a vir de um pequeno pacote `window.odcaNav` injetado pelo `_Layout` (chaves `layout.expand_menu`/`layout.collapse_menu`, já existentes), mantendo fallback pt-BR no JS. `wwwroot/js/dialogs.js`: fallbacks de título/ação do diálogo de confirmação passam a herdar do texto default renderizado pelo servidor (localizado) em vez de strings pt fixas.
- **Fora desta sprint (carregado para S8, registrado)**: ficha do contrato detalhada (`Contracts/Sheet.cshtml` + abas renovações/revisões/histórico — superfície maior), Atendimento ODCA do superadmin (`AdminSolicitacoes`, fila e aprovações), sessões de suporte (`SupportSessions`), central de notificações da plataforma, agenda mensal habilitada e localização do conteúdo gerado por notificações. A restrição do F-2 da S6 (superfícies pt-first só em pt-BR na auditoria) vale para esses itens até a S8.
- **Catálogos**: **+391 chaves × 4 culturas** (de 1770 para **2161/cultura**), inseridas por deltas versionados (`scripts/i18n/d-s7-documents.json` 141 · `d-s7-global.json` 86 · `d-s7-obligations.json` 141 · `d-s7-privacy.json` 23) + mesclador determinístico (`scripts/i18n/merge-i18n-delta.mjs`) que valida unicidade de chave e paridade 4/4 antes de gravar — a paridade segue coberta por `LocalizationCatalogParityTests` e pelo gate de assets.
- Sem push; commit único ao final.

## 2. GATE sobre a árvore final

Sobre a árvore final com todas as edições desta sprint:

| Gate | Resultado |
|---|---|
| Build estrito `Odca.sln` Release (`-warnaserror`) | **0W/0E** |
| Build estrito `Odca.sln` Debug | **0W/0E** |
| Domain (`Odca.Domain.Tests`) | **389/389 PASS** |
| Suíte de integração `run-integration-tests.ps1 -FreshDb` | **231/231 PASS — TEST-EXIT=0** (FRESH-DB-OK → BUILD-OK) |
| Gate de assets (`scripts/check-assets.mjs`) | **exit=0** — 10 web assets + 19 chaves i18n JS × 4 catálogos + round trips decimal/editor |
| Paridade de catálogos | **4/4 em 2161 chaves/cultura** (base 1770 + 391 dos deltas S7), validada pelo mesclador, por `LocalizationCatalogParityTests` (na suíte) e pelo gate de assets |
| BOM/encoding dos 4 catálogos | UTF-8 **BOM preservado**, sem CRLF introduzido |
| Migrações | **inalteradas** — checksum canônico v060 `1562ceac…` intacto (nenhuma mudança de esquema nesta sprint) |

Sem novos fatos de teste nesta sprint (o escopo é apresentação/catálogos); a paridade ampliada é coberta pelos fatos de paridade existentes rodando sobre o total novo de 2161.

## 3. Alterações

### 3.1 Navegação (F-4)

| Arquivo | Mudança |
|---|---|
| `src/Odca.Web/Navigation/NavModels.cs` | `NavItem` ganha propriedade opcional `AriaKey` — chave de catálogo para o `aria-label` quando ele **diferencia** do rótulo visível. |
| `src/Odca.Web/Navigation/NavigationRegistry.cs` | `-80/+…`: todo o `AriaLabel` literal (pt) sai do registro; itens distintos passam a declarar `AriaKey` (`nav.aria_*`); itens cujo aria-label era idêntico ao rótulo visível simplesmente não declaram mais nada (WCAG 2.5.3). |
| `src/Odca.Web/Navigation/NavigationRenderer.cs` | resolve `aria-label` via `ctx.Localizer` a partir de `AriaKey`, com fallback preservando o texto anterior; rótulo visível continua pelo `label`/chave existente. |

### 3.2 Views (F-1/F-2/F-3 + compartilhadas de F-4)

| Arquivo | Mudança |
|---|---|
| `Views/Documents/Index.cshtml` | toda a superfície migrada para chaves `documents.*` (filtros, tabela, badges de estágio, próximas ações, paginação, empty state, jornada de criação); data-bound e códigos de API intactos. |
| `Views/Documents/NewDocument.cshtml` | form de criação (modelos, cadastro/busca rápida de paciente, etapas) via `documents.*`/`patient.*`. |
| `Views/Obligations/Index.cshtml` | header/escopo/vistas salvas/filtros/tabela/painel lateral/guia "como usar" via pacote novo `obl.*` + `common.*`; `obligations.js` mantém o contrato (o JS recebe strings já localizadas pela view). |
| `Views/Obligations/Nova.cshtml` | formulário completo (recorrência mensal, lembretes 7/1/0, seletor de cláusula com fallback JS) via `obl.*`; o JS inline lê os rótulos do DOM em vez de literals pt. |
| `Views/Obligations/Acoes.cshtml` | as seis operações (iniciar/cumprir/cancelar/reabrir/reagendar/reatribuir) + mensagens de conflito 409 via `obl.*`. |
| `Views/Privacy/Index.cshtml` | canal público em 4 idiomas via namespace novo `priv.*`; rótulos de formulário por `L.T` explícito na view (o modelo mantém as anotações existentes). |
| `Views/Shared/Error.cshtml` | antes **todo em inglês**; agora `error.*` nos 4 catálogos (título por código HTTP + descrição + link para início). |
| `Views/Shared/AccessDenied.cshtml` | `accessdenied.*`. |
| `Views/Shared/ServiceUnavailable.cshtml` | `unavailable.*`. |
| `Views/Shared/_ConfirmDialog.cshtml` | título/ação/cancelamento padrão via `dialog.*`. |
| `Views/Shared/_Toasts.cshtml` | textos e `aria-label`s dos toasts (ok/erro/conflito) via `toast.*`. |
| `Views/Shared/_Layout.cshtml` | `aria-label="Navegação principal"` → `layout.main_nav`; sufixos de estado da funcionalidade no menu ("(fora do plano)", "(bloqueada)", "(limite atingido)", "(situação não confirmada)") → `layout.feature_*`; "Administração ODCA" e "Escolha uma organização" → `layout.*`; `title` de sessão sem credencial localizado. |

### 3.3 Controllers Web

| Arquivo | Mudança |
|---|---|
| `Controllers/DocumentsController.cs` | títulos `ViewData["Title"]` e 3 mensagens remanescentes (`load_error`/permissão) via `L()`. |
| `Controllers/ObligationsController.cs` | injeção de `LocalizationService` (padrão dos demais controllers); **todos** os `TempData` de notificação/erro das operações e do CRUD de vistas salvas, `UserMessage` de falhas e validações de campo passam a chaves `obl.*`; nenhum literal pt permanece. |
| `Controllers/PrivacyController.cs` | injeção de `LocalizationService`; rate-limit, timeout e indisponibilidade do canal via `priv.*`. |

### 3.4 JavaScript

| Arquivo | Mudança |
|---|---|
| `wwwroot/js/navigation.js` | "Expandir menu"/"Recolher menu" passam a vir do bag `window.odcaNav` injetado pelo `_Layout` (chaves `layout.expand_menu`/`layout.collapse_menu`), mantendo fallback pt-BR quando o bag ausente. |
| `wwwroot/js/dialogs.js` | fallbacks de título/ação do diálogo de confirmação herdam do texto **default renderizado pelo servidor** (já localizado) em vez de strings pt fixas. |

### 3.5 Catálogos e tooling

| Arquivo | Mudança |
|---|---|
| `src/Odca.Configuration/Localization/{pt-BR,en-US,es-ES,fr-FR}.json` | **+391 chaves × 4 culturas** (1770 → 2161): `documents.*` (estendidos), `obl.*` (novo), `priv.*` (novo), `nav.aria_*`, `layout.*` (novos), `error.*`, `accessdenied.*`, `unavailable.*`, `dialog.*`, `toast.*`, `common.*` (complementos). BOM UTF-8 preservado; paridade 4/4. |
| `scripts/i18n/d-s7-{documents,global,obligations,privacy}.json` (novos) | deltas versionados desta sprint (141+86+141+23 = 391), a fonte auditável das novas chaves. |
| `scripts/i18n/merge-i18n-delta.mjs` (novo) | mesclador determinístico: insere as chaves dos deltas nos 4 catálogos respeitando BOM/ordem, valida unicidade de chave e paridade 4/4 **antes** de gravar qualquer arquivo. |

## 4. Prova ao vivo e auditoria de design

**Auditoria de design estendida em 4 idiomas** (`scripts/qa/audit-d41-design.ps1`, novo; driver `cdp-design-audit.mjs` e `audit-d40-design.ps1` inalterados) em navegador real (msedge CDP), sobre a stack dev provisionada pelo próprio script com o superadmin sintético da prova e MFA reinscrita determinística:

- **Cobertura**: 13 rotas × idiomas × 4 viewports (360/768/1280/1440) = **152 medidas**. Versus D-OC40, entram nesta rodada `documentos`, `documentos/novo`, `obrigacoes` e `obrigacoes/nova` em **pt-BR/en-US/es-ES/fr-FR**, e `home`, `central-solicitacoes`, `notificacoes` e `privacidade` (anônima) passam a ser medidas nos 4 idiomas. Restam em pt-BR apenas as superfícies de plataforma fora desta sprint (`atendimento-odca`, `suporte-sessoes`, `auditoria` da plataforma — dívida registrada para a S8), como `estudio` (pt) e `pacientes` (pt/en).
- **Medidas**: overflow horizontal, autenticação quebrada e HTTP 4xx/5xx por rota×idioma×viewport; PNG de triagem somente nas falhas.
- **Resultado**: `AUDIT-DONE checks=152 ok=152 overflow=0 authfail=0 httpfail=0` (**exit=0**) — nenhuma correção de produto foi necessária nesta rodada.
- **Transcript commitado**: `evidence/d41-design-audit-rodada-final.txt`. O JSON consolidado das medidas permanece em `%TEMP%\opencode\evidence\design-audit-d41\design-audit-results.json` (fora do repo por tamanho/convenção dos artefatos CDP).

## 5. Separação de evidências e portal

- **Teste local** (este incremento): GATE do §2 sobre a árvore final — builds estritos 0W/0E, Domain 389/389, suíte `-FreshDb` 231/231 TEST-EXIT=0, gate de assets exit=0, paridade de catálogos 4/4 em 2161 chaves/cultura.
- **Prova em navegador**: auditoria de design estendida do §4 (`audit-d41-design.ps1`, navegador real via CDP) cobrindo as superfícies desta sprint nas 4 culturas.
- **CI não verificada**: branch `codex/s00-foundation` segue **sem push** — nenhum ID de execução de CI foi inventado para este SHA.
- **Aceite humano: pendente** — nenhum responsável ou aprovação inventada; a auditoria de design fica disponível para revisão.
- **Portal**: `.odca/progress.json` **inalterado** (baseline 40%, IDs e pesos intactos) — nenhum marco pode ser creditado sem CI oficial do SHA funcional + aceite real.
- **Segredos/dados**: apenas dados sintéticos dos fixtures versionados foram usados na auditoria; nenhuma credencial entra nesta documentação além das já usadas nos scripts de prova commitados.
