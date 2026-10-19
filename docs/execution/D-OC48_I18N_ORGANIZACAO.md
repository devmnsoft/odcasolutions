# Sprint 14 — central de organização + visão geral do cliente + funil anônimo de onboarding em 4 idiomas (D-OC48) (18/10/2026)

Entrega a localização completa da próxima área grande do cliente medida por heurística pt-first: a **central de organização** (equipe com pessoas/convites/perfis + cadastro), a **visão geral do cliente** (home onboarding + primeiros passos), o **funil anônimo** (aceitar convite, contratar, confirmar e-mail) e a **visão global da plataforma** (dashboard) — nas views, nos controllers BFF e no helper de exibição tenancy ciente de cultura — com a auditoria de design ao vivo estendida a 8 rotas novas.

## 1. Escopo e base

- **Base**: `codex/s00-foundation` @ `3345ea0` (Sprint 13 / D-OC47).
- **Sem nova migração**: canônico v062 intacto (`DatabaseSchema.CurrentVersion = 62`); sem mudanças em API/Domain/Worker/assets/SQL.
- **423 chaves i18n × 4 culturas** via delta versionado `scripts/i18n/d-s14-organizacao.json` (`org.*` 213, `perm.*` 50, `onb.*` 50, `dash.*` 36, `gs.*` 35, `reg.*` 21, `permg.*` 13, `conf.*` 3, `api.*` 2) → catálogos em **3404 chaves/cultura, paridade 4/4** (delta×catálogo: 423 × 4 = 1692 comparações, **0 divergências**).
- **Política de chaves / reuso**: rótulos neutros reutilizam o catálogo existente (`common.select/type/state/actions/open/prev/next/how_to_use/system`, `action.filter/save/cancel/edit/close`, `obl.status_all`, `documents.new_document` e `documents.btn_continue`, `patient.new_cta`, `actions.item_action`, chaves flat `visao-global`/`organizacoes`, badges de revisão `common.status_in_review`/`common.status_internally_approved` + `rev.st_changes_requested`/`rev.st_cancelled`/`rev.st_superseded`, "Entrar" = `support.imp_enter`). Nomes comerciais dos planos **Basic/Enterprise** permanecem literais; apenas "Intermediário" foi p/ catálogo (`reg.plan_intermediate`).
- **`TenancyDisplay` ciente de cultura**: overloads novos aceitam delegada `Func<string,string> t` (`MemberStatus`, `InvitationStatus`, `DeliveryStatus`, `OrganizationStatus`, `DeliveryErrorMessage`, `RolePresentation`, `PermissionLabel`, `PermissionGroup`); `PermissionLabel(code,t)` resolve `perm.<código com underscores>`; `PermissionGroup(group,t)` mapeia os 13 nomes pt canônicos para slugs estáveis `permg.<slug>` (organization/team/patients/templates/drafts/documents/reviews/imports/billing/obligations/renewals/work/privacy); **fallback = valor legado quando `t` devolve a própria chave**; o nome canônico "Administrador da organização" segue como marcador determinístico → `org.role_superadmin`; overloads legados de 1 arg preservados (nenhum outro call site fora deste escopo os usa para strings).
- **Texto com `<strong>`**: fragmentos prefixo+corpo com a tag direto no Razor (mesma política da S13); nenhum `Html.Raw`.
- **Dívida registrada dentro do escopo**: `[Required(ErrorMessage)]` dos view models segue pt-first (política cross-área — atributos de data annotation não têm acesso ao `L`); mensagens vindas da API/domínio permanecem em pt (`item.Message` das pendências na visão geral, `Model.Message` no cadastro recebido) — strings de camada de domínio, não da superfície BFF.

## 2. F-1 — 10 views no padrão canônico da casa

`_c=(string?)Context.Items[CultureMiddleware.ItemsKey]` + `L.T(key,_c[,args])` (helper `L` já injetado no `_ViewImports`):

- `Organizations\Team.cshtml`: título/métricas, abas pessoas/convites/perfis, busca e filtros, tabelas com badges localizados (`MemberStatus`/`InvitationStatus` via delegada), confirmações com motivo padronizado, transferência de administração, drawer de convite, drawer de perfil com **matriz de permissões completa** (estados por plano/bloqueio/franquia, permissões inertes gravadas sem efeito até contratação) e estados de linha, drawer de perfis do membro, help card. Datas de convite preservam o relógio exibido: `CulturedTime.DateTime(valor.ToLocalTime(),_c)` como no original.
- `Organizations\Edit.cshtml`: cadastro com controle de versão (alerta 409 com recarga), nome/fuso/perfil de atuação (geral/clínica de terapia/cirurgião plástico), estado atual via `OrganizationStatus` localizado, botões salvar/cancelar.
- `Organizations\Index.cshtml`: picker de organização com filtro client-side por nome, cards com contexto (badge de estado, abrir equipe, editar cadastro quando houver permissão), empty states separados (filtro sem correspondência × nenhum acesso ativo).
- `Organizations\Accept.cshtml`: aceite de convite anônimo — preview do destino (organização/perfil em `<strong>` via fragmentos, validade datada na ordem da língua ativa), estados link incompleto/sem preview, chamada para login reutilizando `support.imp_enter` + planos opcionais, nota de troca de sessão antes do aceite.
- `Onboarding\CustomerHome.cshtml`: visão geral da organização — faixa suspensa/situação comercial, CTAs (novo documento, cadastrar paciente, configurações pendentes, equipe), tabela de documentos recentes (badges de tipo e situação reutilizando `common.status_*`/`onb.*`, datas `CulturedTime.DateTime(TimeZoneInfo.ConvertTime(…,tzTenant),_c)` — fuso do tenant preservado), revisões atribuídas (switch de status localizado, prazo formatado culturalmente), métricas (membros ativos, convites válidos, assentos disponíveis, plano contratado, limite de usuários, capacidade) e pendências acionáveis.
- `Onboarding\GettingStarted.cshtml`: progresso real por dados atuais, 5 etapas com estado (concluída/pendente/depender de permissão + nota de escopo), responsável, ação contextual e guia de uso.
- `Onboarding\Register.cshtml`: funil anônimo de contratação — **rótulos explícitos localizados substituem `DisplayNameFor`** dos atributos `[Display(Name)]` (acessibilidade preservada: o label agora envolve o controle; multiset de tags invariante), checkboxes termos/privacidade/marketing, opção de plano "Intermediário" localizada, estado de processamento e botão.
- `Onboarding\ConfirmEmail.cshtml` / `RegistrationStarted.cshtml`: confirmação do e-mail e tela de recebimento (com token de transporte local de desenvolvimento).
- `Home\Index.cshtml`: visão global da plataforma — saudação, 14 indicadores, ações rápidas, últimos eventos de auditoria (`CulturedTime` + fallbacks `common.system`/`layout.platform`) e guia de uso.
- **Datas (invariante D-OC22)**: `.ToString("dd/MM/yyyy HH:mm")`/`"g"` fixos saem das UIs → `CulturedTime.DateTime(value,_c)` (ordem da data por língua ativa, relógio 24h); o **horário exibido é preservado** onde o original convertia zona (CustomerHome: fuso do tenant; Accept/Team/Home: `ToLocalTime()` do original) — verificado ponto a ponto contra o HEAD. Formato canônico dd/MM/yyyy do documento (PDF/papel) intacto.

## 3. F-1 — 2 controllers BFF + mensagens compartilhadas

Injeção canônica de `LocalizationService` (propriedade `Culture` do `CultureMiddleware` + helper privado `L(key,args)`):

| Controller | Call sites localizados | Observações |
| --- | --- | --- |
| `OrganizationsController` | 41 | equipe (pessoas/convites/perfis), transferência de administração, edição (conflito de versão/not-found), aceite de convite (preview/falhas); helpers `DefaultInviteError`/`MemberActionSuccess`/`UserFacingError` tornados instâncias com texto localizado |
| `OnboardingController` | 17 | visão geral do cliente (erros de carga de documentos/revisões/overview), primeiros passos, registro (validação/sucesso), confirmação de e-mail |

Mensagens comuns de erro de API compartilhadas pelos dois controllers: `api.too_many_attempts` + `api.service_unavailable` (o `UserFacingError` de Organizations migrou de chaves próprias duplicadas para as chaves `api.*` existentes).

## 4. F-2 — auditoria de design estendida (d41)

`scripts/qa/audit-d41-design.ps1`: tabela de rotas 20 → **28** — 5 rotas de sessão **cliente** (`org-picker` `/organizacoes`, `equipe` `/organizacoes/{t}/equipe`, `editar-org` `/organizacoes/{t}/editar`, `visao-geral-cliente` `/cliente`, `primeiros-passos` `/organizacoes/{t}/primeiros-passos`) e 3 de sessão **anônima** (`aceitar-convite` `/convites/aceitar?invitationId=…` sem token, `contratar` `/contratar`, `confirmar-email` `/confirmar-email?registrationId=…`) — todas ×4 idiomas; caminhos gravados só após conferir os atributos `[HttpGet]` reais dos dois controllers. 63 → **95 pares** rota×idioma × 4 viewports (360/768/1280/1440) = **380 medidas** (era 252). Comentários e LogEv atualizados junto. Execução ao vivo sobre o banco descartável: **AUDIT-DONE checks=380 ok=380 · overflow=0 · authfail=0 · httpfail=0 (AUDIT-EXIT=0)**; transcript commitado `evidence/d41-design-audit-s14-rodada-final.txt`; 0 PNGs de falha.

## 5. Evidência

### GATE sobre a árvore final (executado localmente)

| Etapa | Comando | Resultado |
| --- | --- | --- |
| Build Debug estrito | `dotnet build Odca.sln -c Debug -warnaserror` | **0W/0E** |
| Build Release estrito | `dotnet build Odca.sln -c Release -warnaserror` | **0W/0E** |
| Domain | `dotnet test tests/Odca.Domain.Tests` | **389/389** |
| Integração em DB nova | `scripts/qa/run-integration-tests.ps1 -FreshDb` | **236/236, TEST-EXIT=0** (baseline S12/S13 preservado; nenhum teste novo) |
| Assets/i18n | `node scripts/check-assets.mjs` | **exit=0** — 10 assets, 19 chaves do conjunto d41, 4 catálogos (**3404 chaves/cultura, paridade 4/4**) |
| Paridade delta↔catálogo | script efêmero (removido antes do commit) | 423 × 4 = 1692 comparações, **0 divergências** |
| Chaves usadas × catálogo | script efêmero (removido antes do commit) | 430 chaves únicas referenciadas no código (chaves estáticas `L.T/L` das 10 views + 2 controllers + chaves dinâmicas `perm.*`/`permg.*` derivadas do catálogo de permissões) × 4 catálogos = **0 ausentes** |
| Auditoria de design d41 | `scripts/qa/audit-d41-design.ps1` | **380/380 · overflow=0 · authfail=0 · httpfail=0 · exit=0** (transcript §4) |
| SMTP / backup | — | **não reexecutados de propósito**: sem diff em transporte, outbox, worker nem scripts desses domínios (rodadas S12 seguem válidas) |

### Verificação byte-a-byte dos valores i18n (leção S12 aplicada)

Delta escrito 100% ASCII com `\uXXXX`; amostras decodificadas conferidas por bytes nos catálogos: pt-BR "Superadministrador da organiza**ção**" termina `… 6e 6f c3 a7 c3 a3 6f` (ç=c3 a7, ã=c3 a3); pt-BR "Crie um perfil deleg**ável** para organizar permiss**ões**." contém `c3 a1 76 65` (á) e `c3 b5 65 73` (õ); FR/ES/EN conferidos por paridade estrutural e round-trip JSON (BOM+LF, sem nova linha final).

### Classificação QA (não é do produto)

Dois incidentes capturados **antes** do commit: (1) o primeiro sync line-based dos catálogos corrompeu o `pt-BR.json` (vírgulas finais perdidas em 10 linhas) — sincronização reescrita como parse→mutate→reserialize e arquivo reparado byte-a-byte, com paridade 4/4 revalidada; (2) bug de zona em 3 datas onde o original exibía relógio local (`Team.cshtml` datas de convite ×2 e `Accept.cshtml` validade ×1) — detectado ao conferir o comportamento do helper (`CulturedTime` formata o offset como chegado; `Kind.Local` preserva o relógio local), corrigido passando `.ToLocalTime()` ao helper. Nenhum literal de UI pt remanescente: varredura de caracteres acentuados nos 13 arquivos do escopo = apenas glifos neutros originais (`—`, `×`) e o catálogo legado do `TenancyDisplay` (pt-BR canônico por design, usado como fallback).

### Confirmação de preservação estrutural

Dif de multiset de tags (HEAD × árvore) nos 10 views: **0 remoções / 0 adições** em 9 arquivos; `CustomerHome.cshtml` com **+2 `<span aria-hidden="true">`** propositais (a seta "→" literal dos links "ver todos/as" ficou encapsulada, aparência idêntica). Em `Register.cshtml` o aninhamento label→controle mudou propositadamente (wrapping para acessibilidade) com o multiset de tags invariante.

## 6. Cobertura honesta e dívidas mantidas

- **Coberto**: as 5 superfícies com rota fixa (picker, equipe, editar, visão geral do cliente, primeiros passos) + as 3 anônimas (aceitar convite, contratar, confirmar e-mail) medidas ao vivo em 4 idiomas × 4 viewports (overflow/auth/HTTP) + todas as strings das 10 views e 2 controllers em catálogo (zero literais de UI verificado por varredura + checagem das 430 chaves). Estrutura comprovadamente preservada (§5).
- **Não coberto pela rota fixa da auditoria**: estados internos da equipe (painéis de convite/perfil/perfis do membro são da mesma rota `equipe`), preview real do convite com token válido (rota anônima auditada no estado sem token) e POSTs do funil (registro/confirmar e-mail) — cobertos por compilação estrita, varredura, dif de estrutura e suíte de integração inalterada; risco residual avaliado baixo. Aceite humano das 4 línguas pendente (como S7/S8/S13).
- **Dívida i18n pt-first restante** (fora do escopo decidido desta sprint, ~370 linhas por heurística): controllers/views `PlatformLibrary`, `Customers`, `Imports`, `Consumption`, `Plans`/`Modules`, `Account`, `Inbox`, `PlatformAudit`/`Diagnostics`/`Operations` (registros D-OC42/43); atributos `[Required(ErrorMessage)]` em pt-first; mensagens de domínio da API em pt (pendências da visão geral, mensagem do cadastro recebido).
- **Bloqueios externos mantidos (registrados, nenhuma inventada)**: CI oficial nunca verde publicada (branch sem push) → `.odca/progress.json` permanece **40%**; provedor de assinatura, canal SMTP real, usuários convidados + aprovação da versão, política de sessão de 15 min e retenção de backups seguem abertos (D-OC45 §4). Nenhum marco credenciável nesta rodada.

## 7. Separação de evidências e portal

- **Teste local**: GATE §5 + execução d41 ao vivo + transcript versionado + checagem das 430 chaves + amostras byte-a-byte.
- **CI oficial**: **não verificada** — sem push nesta rodada; nenhum ID de execução inventado.
- **Aceite humano**: pendente (central de organização + visão geral do cliente + funil anônimo em 4 idiomas + itens D-OC45).
- **Portal / `.odca/progress.json`**: **inalterado** — baseline 40%; IDs e pesos intactos; sem CI oficial verde do SHA nem aceite real, nenhum marco sai de `pending`.
