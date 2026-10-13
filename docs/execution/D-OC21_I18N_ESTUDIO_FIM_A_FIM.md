# D-OC21 — Bloco F: i18n fim-a-fim do Estúdio em 4 idiomas

Data: 2026-10-13 · Branch: codex/s00-foundation · Base: `4b14f4a`

## Contexto

Bloco F exige localização fim-a-fim (pt-BR/en-US/fr-FR/es-ES) da área do Estúdio. A infraestrutura já existia desde o incremento de localização (catálogos JSON por cultura, `CultureMiddleware` com cookie `odca_lang`, `LocalizationService` singleton, `_ViewImports` injetando `L`), mas as 6 views do Estúdio e seus 3 scripts JS eram 100% PT-hardcoded — assim como o `StudioController` (~50 strings visíveis ao usuário em TempData/fallbacks/títulos).

## Decisão

**Uma única fonte por página, sem request extra**: cada string de JS vem de um *bag* inline emitido pelo servidor no HTML da própria página (`Views/Studio/_I18nBag.cshtml`), filtrado para chaves com prefixo `studio.`/`common.` (as únicas que chegam ao bag). Views usam `@L.T("chave",_culture,...)` direto; JS usa o alias `const t=(window.OdcaI18n&&window.OdcaI18n.t)||((k)=>k)` — fallback de identidade preserva o comportamento em páginas que não injetam o bag.

Decisões específicas:

- **Extensão do service mínima**: `T()` ganhou `params string[] args` (`string.Format` com `InvariantCulture`) e um novo `Catalog(string?)` que devolve o dicionário completo resolvido (com fallback pt-BR por chave) para o bag. Sem novas dependências, sem cache invalidação (arquivos estáticos lidos no startup, padrão existente).
- **Paridade obrigatória por teste**: `LocalizationCatalogParityTests` (sem banco) asser que os 4 catálogos parseiam para dicionários achatados de strings não vazias, têm **conjunto idêntico de chaves**, contêm ~90 chaves-sentinela que views/scripts consomem e preservam whitespace intencional (`studio.e_next_prefix` termina com espaço; `studio.tpl_help_prefix` começa e termina com espaço; `studio.tpl_or_separator` = `" ou "`).
- **Reuso antes de criar**: títulos compartilhados usam chaves existentes (`action.save/back/cancel/close/submit/filter`, `inbox.prev/next/page`, `studio.status_*`, `studio.nav_catalog`, `studio.cmpv_title`, `studio.cat_title`, `studio.field_confirm_btn`…); nada de chave duplicada com valor igual.
- **Controller como superfície de i18n**: `StudioController` recebe `LocalizationService` no ctor primário, lê a cultura de `HttpContext.Items[CultureMiddleware.ItemsKey]` (mesma fonte das views) e troca todas as strings visíveis — títulos `ViewData["Title"]` (que alimentam `<title>` e a topbar do layout), fallbacks de erro da API e mensagens TempData — por `L("studio.ctl_*",...)`. Argumentos numéricos sempre via `.ToString(CultureInfo.InvariantCulture)` (análises CA1305 como erro).
- **Dados ≠ UI**: o conteúdo-semente do novo modelo ("CONTRATO DE PRESTAÇÃO DE SERVIÇOS" + parágrafo de abertura), o sufixo de nome "— cópia editável" (persistido no nome) e o fallback de nome "Modelo" permanecem como conteúdo/documental — traduzir nome persistido mudaria dados, não interface. O sufixo, porém, é aplicado e checado (`EndsWith`) pela mesma chave traduzida, preservando consistência dentro do idioma ativo.
- **Fora de escopo declarado**: `Views/PlatformLibrary/NewRevision.cshtml` (área plataforma, ainda 100% PT) importa `template-editor-document.js`; como não injeta bag, o alias-fallback mantém as strings de perda de estrutura em PT naquela página — consistente com a página. Mensagens de erro retornadas pela API seguem em PT (localização server-side da API é outro incremento); a heurística `error.message.includes("atualiz")` em `studio.js` (detecção de conflito por texto do servidor) foi mantida por esse motivo.

## Mudanças

| Arquivo | Alteração |
|---------|-----------|
| `src/Odca.Web/Localization/pt-BR.json` (+ en-US/fr-FR/es-ES) | 223 → **749 chaves** (526 novas, todas `studio.*`/`common.*`); paridade 4×749 validada; UTF-8 BOM + LF preservados |
| `src/Odca.Web/Localization/LocalizationService.cs` | `T(key, culture, params string[] args)` (format `InvariantCulture`) + `Catalog(string?)` para o bag |
| `src/Odca.Web/Views/Studio/_I18nBag.cshtml` | **novo**: partial que emite `window.odcaI18n={culture,t:{…}}` (bag filtrado `studio.`/`common.`) + IIFE `window.OdcaI18n.t(key,…args)` com substituição `{n}` |
| `src/Odca.Web/Views/Studio/Index.cshtml` | catálogo/filtros/paginação/cards por `L.T`; reuso de `studio.cat_title`, `status_*`, `inbox.page` |
| `src/Odca.Web/Views/Studio/Edit.cshtml` | minuta completa + bag injetado antes dos consumers JS |
| `src/Odca.Web/Views/Studio/Version.cshtml` | versão/revisão/preparação/histórico/comparação + bag; contrato DOM `.participant-number` mantido (JS `reindex()` depende dele); correção de bug de chave existente (`nav.templates` → `studio.nav_catalog`) |
| `src/Odca.Web/Views/Studio/TemplateEdit.cshtml` | view + módulo inline JS (jornada, banner de conflito, painéis, barra de ações, placeholders, alertas, banners de perda) |
| `src/Odca.Web/Views/Studio/TemplatePreview.cshtml` | aria/breadcrumb corrigidos para chaves existentes |
| `src/Odca.Web/Views/Studio/CompareVersions.cshtml` | diff categorizado; h1 corrigido de `action.filter` ("Filtrar") para `studio.cmpv_title` |
| `src/Odca.Web/wwwroot/js/studio.js` | ~50 strings (estados de digitação/salvamento, validações dec/doc/data, origem de campo, comentários, checklist de encaminhamento, histórico/versões, compare) via `t('studio.…')` |
| `src/Odca.Web/wwwroot/js/studio-decimal.js` | mensagens do parser decimal (required/numeric_only/invalid/negative_currency/separator_explicit/ambiguous/group_inconsistent/max2/max6) |
| `src/Odca.Web/wwwroot/js/template-editor-document.js` | 10 mensagens de perda de estrutura (`loss_*`) |
| `src/Odca.Web/Controllers/StudioController.cs` | ctor + cultura por request + ~50 strings visíveis (títulos, validações, conflitos, publicação, arquivamento, biblioteca oficial, duplicar, minuta, conferência, geração, PDF, preparação, revisão) por `L("studio.ctl_*")` |
| `tests/Odca.IntegrationTests/LocalizationCatalogParityTests.cs` | **novo**: 4 fatos — parse achatado não-vazio + sem espaços em chaves, paridade exata de conjunto, sentinelas do bag por cultura, valores com whitespace intencional |
| `tests/Odca.IntegrationTests/Odca.IntegrationTests.csproj` | link `<Content>` dos 4 catálogos do projeto Web para a pasta de saída do teste |

## Evidência comportamental (aplicação real, API `:7143` + Web `:7144`, banco `odca_test_disposable`)

Build da solution **0 avisos / 0 erros**. Domain **302/302 PASS**. Paridade **4/4 PASS** (novo). Suíte de integração em `odca_test_local`: **54 falhas / 116 aprovados / 170** — os 54 são a linha de base ambiental conhecida (backlog separado); os 116 = 112 prévios + 4 novos de paridade (zero nova regressão).

Check de 4 idiomas contra a aplicação real: login admin via formulário Web + MFA (código de recuperação **REV21A**, recém-provisionado), cookie `odca_lang` trocado entre requisições da mesma sessão, asserções sobre o HTML retornado (com decode de entidades — Razor codifica acentos como `&#xFA;`):

| Cultura | Index do Estúdio (bytes esperados presentes) | Modelos/novo (bytes esperados presentes) | Negativos ausentes | Bag inline |
|---------|----------------------------------------------|-------------------------------------------|---------------------|------------|
| pt-BR | "Modelos de contrato" | "Estúdio de Documentos · Gestão de Modelos", "Criar Novo Modelo", "← Voltar ao Catálogo" | — | `culture:"pt-BR"`, 34 929 chars |
| en-US | "Contract templates" | "Document Studio · Template Management", "Create New Template", "← Back to catalog" | "Modelos de contrato", "Estúdio de Documentos", "Criar Novo Modelo", "Voltar ao Catálogo" | `culture:"en-US"`, 30 562 chars |
| fr-FR | "Modèles de contrat" | "Studio de documents · Gestion des modèles", "Créer un nouveau modèle", "← Retour au catalogue" | "Modelos de contrato", "Estúdio de Documentos", "Criar Novo Modelo", "Voltar ao Catálogo" | `culture:"fr-FR"`, 37 336 chars |
| es-ES | "Modelos de contrato" | "Estudio de documentos · Gestión de modelos", "Crear nuevo modelo", "← Volver al catálogo" | "Estúdio de Documentos", "Criar Novo Modelo" | `culture:"es-ES"`, 33 766 chars |

O teste en-US "Modelos de contrato ausente no index" **falhava antes da conversão do controller** (vazava em `<title>` e na topbar via `ViewData["Title"]` hardcoded) e passou depois — prova de que a mudança do controller muda bytes renderizados, não só o catálogo. Evidências HTML salvas em `%TEMP%\sv-i18n-evidence\` (8 arquivos: index/new × 4 culturas).

Grep final de resíduo: nenhuma string PT com acento fora de comentários (Razor `@* *@`/JS `//`) nas 6 views e nos 3 JS; no controller restam apenas as duas linhas de conteúdo-semente documental (dado, não UI).

## Limitações

- A API continua respondendo erros em PT; fallbacks `UserMessage(…)` agora são PT apenas no último recurso quando a API não traz mensagem, e a heurística de detecção de conflito por substring PT em `studio.js` permanece acoplada a isso (incremento futuro: códigos de erro estáveis em vez de texto).
- Áreas fora do Estúdio (layout de plataforma, Inbox, Folha, etc.) seguem no estado anterior — este incremento entrega o Estúdio fim-a-fim; o bag existe por página e a regra de prefixo (`studio.`/`common.`) deixa pronta a extensão a outras áreas.
- Sem push/merge/deploy automático.
