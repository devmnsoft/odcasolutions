# Sprint 16 — gestão de clientes em 4 idiomas: Customers + consumo + planos + caixa (D-OC50) (19/10/2026)

Entrega a localização completa da gestão de clientes medida pela heurística pt-first: a **listagem de clientes** (filtros, tabela com utilização, atividade, status), o **detalhe do tenant** (plano com preview de mudança, métricas, funcionalidades com bloqueio administrativo, situação organizacional, solicitações com aprovação/rejeição, concessão manual de armazenamento, franquias, histórico), os **usuários da plataforma** (busca, empty states), o **cadastro de organização** (3 seções com notas e ações), o **plano e consumo do cliente** (métricas, barra de uso, breakdown, solicitação de armazenamento em 3 passos, créditos, solicitações, módulos, histórico), o **catálogo de planos** (cards com limites, módulos habilitados) e a **caixa operacional do cliente** (vistas pessoais, filtros, tabela de pendências) — nas 7 views, nos 3 controllers BFF e no StatusLabels (sobrecargas culturais) — com a auditoria de design ao vivo estendida de 39 para 46 rotas.

## 1. Escopo e base

- **Base**: `codex/s00-foundation` @ `1534bc7` (Sprint 15 / D-OC49).
- **Sem nova migração**: canônico v062 intacto (`DatabaseSchema.CurrentVersion = 62`); sem mudanças em API/Domain/Worker/assets/SQL.
- **231 chaves i18n × 4 culturas** via delta versionado `scripts/i18n/d-s16-clientes.json` (`cust.*` 131, `cons.*` 41, `usage.*` 17, `plans.*` 19, `inbox.*` 17, `status.*` 6) → catálogos **3851 chaves/cultura, paridade 4/4** (delta↔catálogo: 924 comparações / 0 divergências; **308 chaves únicas referenciadas no código × 4 catálogos = 1232 comparações / 0 ausentes**).
- **Reuso de chaves existentes**: labels flat pré-existentes (`visao-global`, `inicio`, `plano-consumo`, `localizar-usuarios`), `common.*` (breadcrumb_aria, filters, situation, reason, all, how_to_use, org, search), `studio.cat_all`, `studio.ver_lower_version`, `documents.filter_search`, `action.search`, `action.cancel`, `reg.lbl_plan`, `reg.plan_intermediate`, `org.orgst_active/suspended`, `org.mem_active/blocked`, `org.ps_unsubscribed/quota/ready`, `admin.decision`, `support.eyebrow`, `api.gate.feature_*` (nomes de módulo — ver decisão §6).
- **StatusLabels** segue o padrão TenancyDisplay: métodos legados pt-BR preservados (compatibilidade) + sobrecargas `(value, Func<string,string> t)` para apresentação localizada; estados desconhecidos exibidos como gravados.
- **Literais mantidos por decisão**: "Enterprise" (nome comercial do plano, regra S14), preposição "de" na linha de usuários do Consumption (sem chave aprovada), glifos neutros `—`/`·`/`✓` (separadores e prefixos de alerta), placeholders técnicos CNPJ/CPF, valores de domínio exibidos crus (`@r.Status`, `@e.Type`, `@e.Resource`, RoleCode, PackageName, Currency), unidades invariáveis GB/MB/TB.
- **Dívida registrada no escopo**: aria-label "Resumo do plano" na grid métrica do Consumption permanece pt (chrome não visível, sem chave no conjunto aprovado — junta-se à dívida S17 de breadcrumb/aria global).

## 2. F-1 — Views (7 arquivos, 329 call sites L.T)

### Customers/Index.cshtml (41 call sites)

Listagem completa: título `cust.title`, header, filtros (plan/status/busca), tabela com 7 colunas (cliente+doc mascarado, plano, situação dual Status×Status, usuários, consumo OCR/assinatura via `cust.cell_usage` com 4 args InvariantCulture, pendentes, atividade `CulturedTime.Date`), action link detalhes, help card.

### Customers/Users.cshtml (28 call sites)

Busca de usuários da plataforma: header, form de busca, empty states (sem resultados geral + sem match `{0}`), tabela com situação `org.mem_active/blocked`, role code literal, footer com total.

### Customers/Create.cshtml (29 call sites)

Cadastro de organização: breadcrumb, header, 3 seções (organização, plano+consumo, administrador inicial) com notas pre/role/post split em `<strong>`/`<em>`, select de planos do catálogo, ações criar/cancelar, help card.

### Customers/Details.cshtml (80 call sites)

Ficha completa do tenant: título com arg `{0}`, breadcrumb + banner de suspensão + header com 3 status, seção de plano com preview de mudança (preview_change 4 args, preview_limits 4 args, preview_preserved 2 args), métricas (invitations_count arg), funcionalidades (tabela c/ estado switch→4 chaves, justificativas bloq/liber), situação da organização (admin+comercial via StatusLabels overloads), solicitações (approve/reject), concessão manual (qty+unit+justificativa+note), franquias do mês (credit_line 4 args + note), histórico (timeline c/ CulturedTime.DateTime), help card.

### Consumption/Index.cshtml (68 call sites)

Central do cliente: título+h1 `plano-consumo`, eyebrow/sub, 5 cards métricos (plano atual c/ versão+status, usuários+seats_detail, armazenamento+max_file, período vigente c/ CulturedTime.Date), barra de progresso (bar_aria), breakdown 5 itens (bd_*), solicitar armazenamento em 3 passos (select pacote, qty, confirmação c/ step3_note em quotes curly), créditos (credit_line_unit 5 args c/ unidade via chave páginas/envelopes), solicitações (tabela 5 colunas + data CulturedTime.DateTime), módulos (modules_p longo c/ quotes curly), histórico (timeline + history_empty), help card (4 li).

### Plans/Index.cshtml (28 call sites)

Catálogo versionado: title/h1/eyebrow/sub, grid (grid_aria), empty state, cards de plano (versão `{0}`, dt/dd 7 limites, módulos via ModuleName localizado reutilizando `api.gate.feature_*`, btn Contratar), help card. `ModuleName` migrada de static local para função local dentro de `@{}` para capturar `_c`.

### Inbox/Index.cshtml (resíduos de 8 call sites adicionados nesta sprint; total 55 incluindo S7-S14)

Vistas pessoais (aria-label, label, badge padrão, botões set-default/delete), salvar vista (summary, placeholder, botão), data DueOn → `CulturedTime.Date(DateOnly,_c)`.

## 3. F-1 — Controllers BFF (3 arquivos, 40 call sites L()) + StatusLabels (8 t())

| Controller | Call sites | Mensagens |
|---|---|---|
| `CustomersController` | 28 | Title (localizar-usuarios, new_title×2), err/ok plan (justify, preview, apply, invalid), err/ok decide, grant (qty, range, ok, err), err_plans_catalog, err_create, ok_create `{0}`, PlanItems→instância (plan_item `{0} {1}`), feature (block, unblock, err), status (reason_short, restore, suspend, err), RenderDetails (features, catalog) |
| `ConsumptionController` | 2 | err_request, ok_request |
| `InboxController` | 10 | Title (inbox.title), err_open, saved_saved/error, view_unavailable, default_conflict, default_set/error, deactivated/deactivate_error |
| `StatusLabels.cs` | 8 | Sobrecargas `Status(v,t)` + `Commercial(v,t)` reutilizando `status.pending/cancelled/commercial_*` + `org.orgst_active/suspended` |

Total: **377 call sites** em 11 arquivos. `PlanItems` transitou de `private static` para instância para acessar `L()`. Padrão canônico: `private string? Culture => HttpContext.Items[CultureMiddleware.ItemsKey] as string;` + `private string L(string key, params string[] args) => loc.T(key, Culture, args);`.

## 4. F-2 — d41 estendida: 39 → 46 rotas (+7 × 4 idiomas = +28 pares)

Novas rotas cobertas ao vivo em 4 viewports:
- Superadmin: `/administracao/clientes`, `/administracao/clientes/novo`, `/administracao/clientes/usuarios`, `/administracao/clientes/{tenantId}` (fixture)
- Cliente: `/conheca-os-planos` (catálogo público — a rota atribuída da action `Plans`; ver QA item 4), `/organizacoes/{tenantId}/plano-e-consumo`, `/organizacoes/{tenantId}/caixa`

Contagem final: **46 rotas × idiomas = 179 pares × 4 viewports = 716 medidas** (antes 151 pares / 604 medidas).

Execução ao vivo: **`AUDIT-DONE checks=716 ok=716 overflow=0 authfail=0 httpfail=0`** sobre o banco descartável de auditoria (login cliente e superadmin OK; RELOGIN automáticos por expiração de sessão no meio da rodada — comportamento esperado do fluxo, sem falha associada). Transcript completo: `evidence/d41-design-audit-s16-rodada-final.txt`. Observação: a rodada intermediária registrou `httpfail=16` concentrados na rota de catálogo por URL incorreta no script — corrigida antes desta rodada final (classificação QA item 4).

## 5. Evidência

### GATE

| Verificação | Resultado |
|---|---|
| Build Release `-warnaserror` sln | 0W / 0E ✓ |
| Build Debug `-warnaserror` sln | 0W / 0E ✓ |
| Domain tests | 389/389 ✓ |
| Integration `-FreshDb` | 236/236 TEST-EXIT=0 ✓ |
| check-assets.mjs | exit=0 ✓ |
| Catálogos (s16-vcat2) | 3851 chaves/cultura, BOM ok, 0 CR, 0 faltantes, 0 divergentes ✓ |
| Delta↔catálogo (merge) | 924 comparações, 0 divergências ✓ |
| Chaves usadas (s16-keys) | 308 únicas × 4 culturas, 0 ausentes ✓ |
| Tag diff HEAD×working (7 views) | 0 diffs (TAGDIFF-OK) ✓ |
| Varredura não-ASCII (11 arquivos) | Apenas glifos neutros + comentários ✓ |
| Double-encoding (4 catálogos) | 0 ocorrências ✓ |
| d41 ao vivo (716 medidas) | 716/716 OK, overflow=0, authfail=0, httpfail=0 ✓ |

### Verificação byte-a-byte dos valores i18n

Delta escrito 100% ASCII com `\uXXXX` (arquivo verificado byte a byte). Validação verbatim camada 1+2 contra o código-fonte original: 231 trechos assertados contra texto fonte. Catálogos: BOM+LF sem nova linha final, 0 CR. Varredura de double-encoding (Â/Ã+bytes altos, U+FFFD): **0 ocorrências** nos 4 catálogos.

### Preservação estrutural

Dif de multiset de tags (abertura/fechamento por nome) entre `git show HEAD:` e working tree nas 7 views editadas: **0 diferenças** (apenas trocas de conteúdo de atributos e texto entre tags).

### Classificação QA (não é do produto)

Três incidentes capturados antes do commit:
1. **CS1503 int/long→string em `L.T`** (21 erros): os params são `string[]`; correção: wrap `.ToString(System.Globalization.CultureInfo.InvariantCulture)` em todos os argumentos numéricos (padrão S15). Encontrado no build Release antes do GATE.
2. **Indentação inconsistente no CustomersController** (5 vs 6 spaces): o arquivo usa indentação irregular pré-existente; edições com substring matching sem leading whitespace para evitar mismatch.
3. **PowerShell inline node -e com regex especial**: tokenização falha em expressões com parênteses/aspas; padronizado para scripts `.mjs` (regra consolidada da S16).
4. **URL da rota de catálogo na entrada nova do script do audit**: a entrada S16 usou `/Plans` (nome da action); a rota atribuída real é `[HttpGet("conheca-os-planos")]` no `CatalogController`. A rodada intermediária registrou `httpfail=16` (4 idiomas × 4 viewports) todos nessa rota; corrigido o caminho no script antes da rodada final — que passou 716/716.

## 6. Decisões registradas

- **Nomes de módulo no Plans reutilizam `api.gate.feature_*`**: unificação com os rótulos canônicos de funcionalidade usados em erros de API. Consequência pt: "Documentos"→"Documentos e PDF", "Assinaturas"→"Assinatura" (mesma identidade funcional, rótulo já usado na plataforma). Registrado como decisão consciente de unificação.
- **StatusLabels legado + sobrecarga**: método original com strings hardcoded pt-BR mantido para compatibilidade (outros callers não afetados); novos call sites usam a sobrecarga cultural. Padrão idêntico ao TenancyDisplay (S10).
- **"de" literal no Consumption** (`@s.ActiveUsers de @s.ContractedSeats`): preposição curta sem chave no conjunto aprovado de 231; em outras línguas fica pt — dívida trivial S17 se necessário.
- **`ModuleName` como função local dentro de `@{}`**: necessária para capturar `_c` (variável de bloco Razor); não pode ser static em `@functions` porque precisa do escopo da view.

## 7. Cobertura honesta e dívidas mantidas

**Coberto por rota fixa**: 7 rotas GET × 4 idiomas × 4 viewports (716 medidas ao vivo).

**Não coberto por rota fixa** (POSTs/estados internos): criar organização, preview/apply mudança de plano, bloquear/liberar funcionalidade, mudar situação, aprovar/rejeitar solicitação, conceder armazenamento, solicitar armazenamento, salvar/definir-padrão/inativar vista. Cobertura indireta: build estrito + varredura de chaves + tag diff + suíte inalterada. Risco baixo (strings são as mesmas chaves).

**Dívida pt-first restante (particionada S17)**:
- Account ×7 views + controllers + MFA/ChangePassword VMs
- Solicitações (resíduo fora do escopo S16)
- BffErrorHandlingMiddleware (mensagens de erro global)
- _Layout endonyms (nomes de línguas nativos)
- NavigationRegistry (30 rótulos) + renderer + breadcrumb aria-label global (+ residual "Resumo do plano" Consumption)
- `[Required(ErrorMessage)]` / `[Display(Name)]` pt-first (VMs)
- Mensagens de domínio da API em pt (ErrorDetail/ErrorTitle passthrough, BillingLimitation/Reason)

**Bloqueios externos mantidos**: CI oficial nunca verde publicada (sem push) → progress.json permanece em 40%; aceite humano dos 4 idiomas pendente.

## 8. Separação de evidências e portal

- **Testes locais**: GATE completo (builds, Domain, Integration, assets, catálogos, chaves, tag diff, varreduras) + d41 ao vivo (transcript em `evidence/d41-design-audit-s16-rodada-final.txt`).
- **CI oficial**: não verificada (sem push autorizado neste ciclo).
- **Aceite humano**: pendente (gestão de clientes em 4 idiomas).
- **Portal**: `.odca/progress.json` inalterado em 40% (baseline/IDs/pesos intactos, sem marco credenciável).
