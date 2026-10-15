# D-OC31 — Operação documental integrada e preparação comercial (15/10/2026)

**Base**: `codex/s00-foundation` @ `b8206bb` (= D-OC30). Commit único ao final, **sem push**.

## Objetivo

Consolidar a operação documental do ciclo de contrato em uma jornada contínua e auditável
(situação → pendência → próxima ação → histórico), sem efeitos colaterais entre
assinatura/vigência/obrigações/aditivos, e preparar a operação comercial multi-instância:
notificação honesta com outbox, armazenamento compartilhado (sessão + Data Protection),
ficha administrativa unificada separando situação administrativa de situação comercial.
Tudo em cima dos caminhos existentes (estúdio, ficha, caixa operacional, renovações,
administração, central de notificações da plataforma) — **sem módulos paralelos**, sem
OCR/upload nesta fatia, sem inventar preços ou faturas.

## GATE (pré-requisito dos avanços dependentes)

- Build estrito (Release + Debug) **0W/0E**; instalação limpa validada via `-FreshDb`
  (188/188 na hora do GATE; versão final após B.x: **191/191, 0 falhas**,
  `evidence/d31-freshdb-run.log`, TEST-EXIT=0).
- Regressões críticas resolvidas: `42P10` no `ON CONFLICT(source_id)` (predicado do índice
  parcial) em três sites; `42601` no `retry_notification` corrigido na **v055** sem reescrever
  a v054 já congelada.
- Regra canônica de planos reconciliada (A.3) e isolamento entre dois tenants provado:
  API 403/200/201 e RLS como `odca_app` (nA=1/nB=1; contratos 55/0) —
  `evidence/d31-gate-twotenants.txt`.

## A.3 — Regra canônica de planos (preservada até decisão comercial)

- Base fresca = **6/6/7** módulos (`module.reviews` só em `enterprise-v2`);
  `solicitations_open` efetiva = **L5706**.
- Gate estável: **409 `solicitations.plan.required`**. Os quatro serviços são
  diferenciados individualmente — documentais exigem Enterprise; `suporte_tecnico` aceita
  qualquer assinatura ativa; demais ficam bloqueados — com menus, ações, URLs e API
  mantendo a mesma combinação plano + permissão + vínculo + bloqueio.
- `PlanServiceGateTests` (novo) prova a matriz ao vivo (contagens finais 1/1/4). A regra é
  mantida como canônica até a decisão comercial; nenhum preço foi inventado
  (`MATRIZ_PLANOS_BENEFICIOS.md` documenta o estado).

## B.1 — Notificação: evento + outbox na mesma transação

Evento e fila gravados atomicamente; retry/backoff/limite configurável; lease recuperável
após interrupção; `source_id` determinístico + índice parcial → reprocessamento sem
duplicar confirmações. Estados distintos (enfileirado/aceito/entregue/comprovável/falha
temporária/falha definitiva). Transporte `INotificationTransport` com SMTP configurável e
**desativado até configuração válida** — sem configuração, o stub falha honestamente
(`notification_provider_missing`), sem entrega falsa. Central do superadmin
(`PlatformNotifications`) com filtros, campos seguros e retry auditado
(`platform.notifications.retried`). Smoke ao vivo 0 falhas (enfileiramento, retry 200 +
auditoria, 409 em pendente, pickup com 8 arquivos, worker sem erros).

## B.2 — Assinatura vinculada à versão exata + evidência

Assinatura liga-se à versão exata do contrato com **hash do PDF confirmado no servidor**;
evidência permite apontar documento do acervo (id + sha256, exigindo varredura segura do
contrato); terceiros exigem justificativa + permissão específica; vínculo ativo/sessão/MFA;
sem sobrescrita de evidências (correção por evento complementar auditado); idempotência/
replay (`replayed=true`) e negativos 400 sem máscara de estado assinado.

## B.3 — Jornada contínua + editor + restaurar = nova revisão

- **Ficha** (`#jornada`): próxima ação priorizada (overdue > revisão > alteração >
  renovação > assinatura > solicitação > obrigações), pendências contadas por seção com
  âncora, motivo de bloqueio em arquivado/encerrado e atalho para o histórico — calculada
  no BFF a partir dos dados já projetados.
- **Editor**: busca de campos + grupos por origem; preenchimento preservado após falha de
  salvamento; estados salvando/salvo/falha/conflito; saída preservada.
- **Restaurar versão = nova revisão** (`POST minutas/{id}/restaurar` →
  `POST drafts/{draftId}/restore-from-version`): normaliza, valida estrutura, protege
  origens automáticas, bloqueio otimista por `row_version`, evento `draft.restored`
  (fromVersionId/Number/newVersion) e **409 `draft.save.conflict`** em estado desatualizado.
- Versões publicadas continuam imutáveis. Sem OCR/upload nesta fatia.

## B.4 — Projeções, aditivo e histórico

Assinatura **não altera vigência/obrigações** sem regra existente; pendências encerradas
saem das projeções abertas (caixa operacional); aditivo = **nova versão + vínculo**
(`contract_change_applications`) com obrigações preservadas e **sem cópia implícita**
(cópia só explícita); critérios consistentes entre dashboard/ficha/agenda/caixa; histórico
consultável via `GET contracts/{id}/events`.

## B.5 — Ficha administrativa e preparação comercial

Ficha administrativa consolidada (plano/versão, situação, recursos, consumo, membros,
bloqueios, histórico) distinguindo **situação administrativa** de **situação comercial**
(seis situações administrativas diferenciadas); ativação administrativa **não equivale a
pagamento recebido** — o estado `commercial_pending` exibe "Aguardando confirmação
comercial (sem pagamento recebido)"; mudança de plano mostra **impacto antes de confirmar**
(preview) e grava auditoria `organization.plan_changed` com motivo, versão comercial
anterior/nova e `payment_recorded=false`; último admin preservado.

## C — Telas, idiomas, responsivo e capturas

- **7 telas prioritárias** (login, caixa operacional, ficha de contrato, estúdio/minuta,
  renovações, cliente da administração, minha conta) × **4 idiomas** (pt-BR/en-US/es-ES/
  fr-FR — apenas UI traduzida; códigos, identificadores e conteúdo do usuário intactos) ×
  **360 / 768 / 1280 / 1440 px** + **zoom 200%** (1280×800@2x): **140 capturas full-page**
  das jornadas autenticadas.
- Driver: msedge `--headless=new` + CDP (Node 24); por captura: cookies da sessão +
  `odca_lang` + `Emulation.setDeviceMetricsOverride`. Correção entre rodada 1 e 2: o cookie
  `__Host-odca-session` exige `secure:true` no `Network.setCookies` (sem ele era descartado
  silenciosamente → redirecionamento para `/entrar`) e login fresco via HTTP no driver com
  TOTP controlado por passo (replay protection server-side rejeita passo repetido).
- Evidência: `evidence/captures/` (nomeação `{tela}_{idioma}_{L}x{A}.png` e
  `{tela}_{idioma}_1280x800_2x.png`) — corrida final `CAPTURES-DONE ok=140 fail=0`.

## D — Banco e multi-instância

- Migrations incrementais: **v054** (`odca.distributed_cache_entries` +
  `odca.data_protection_keys`; `PostgresDistributedCache` + repositório compartilhado de
  chaves do Data Protection) e **v055** (correção do `retry_notification`).
  `database/odca.sql` consolidado (+467 linhas). SQL parametrizado; RLS preservado;
  concorrência otimista; auditoria com ator real.
- Seleção condicional: `Cache:Store` e `DataProtection:KeysStore` aceitam `postgres`; o
  desenvolvimento permanece simples (memória/filesystem).
- **Prova duas instâncias 17/17**: login numa → sessão continua na outra (ticket + chaves
  DP compartilhados no Postgres) → logout numa → revogada na outra (302 `/entrar`) —
  `evidence/d31-gate-twostack.txt`. Detalhe net10: o repositório exige
  `AddKeyManagementOptions(o => o.XmlRepository = ...)`; singleton puro de `IXmlRepository`
  deixa as chaves efêmeras.

## E — Matriz de aceite (dez itens)

| # | Item | Status | Evidência |
|---|------|--------|-----------|
| 1 | B.3a jornada contínua: arquivado exibe bloqueio; ativo exibe próxima ação "Acompanhar obrigações abertas" com cartão ancorado | aprovado | rodada final 47/47: "ficha arquivada: proxima acao = arquivado", "ficha ativa: proxima acao = obrigacoes", âncora — `evidence/d31-b345-final.txt` |
| 2 | B.3b editor: busca de campos, grupos por origem, preenchimento preservado pós-falha | aprovado | busca validada ao vivo ("editor tem busca de campos"); preservação em `studio.js` (valores permanecem no formulário após falha de salvamento), exercitada pelo caminho de conflito 409 com re-render |
| 3 | B.3c restaurar = nova revisão; conflito recuperável | aprovado | restore 200 (version 2→3, sourceNumber=1, evento `draft.restored`), 409 `draft.save.conflict` em estado desatualizado; retrato restaurado (row_version=2) |
| 4 | B.4a assinatura não altera vigência/obrigações | aprovado | snapshot `NULL|NULL|NULL|NULL|2|md5` idêntico antes/depois; evento `signature.completed` com hash do PDF |
| 5 | B.4b encerradas saem das projeções abertas | aprovado | obrigação aberta aparece na caixa (scope=mine, 5 itens) e sai após fulfill 200 |
| 6 | B.4c aditivo = nova versão + vínculo + sem cópia implícita | aprovado | versão 1→2, `obligationsPreserved=true`, md5 do conjunto idêntico, `contract_change_applications` `NULL -> 100.00\|BRL` com trilha create/submit/formalização |
| 7 | B.4d histórico consultável | aprovado | `GET contracts/{id}/events` 200 (total=12), incluindo `draft.restored` da prova |
| 8 | B.5a ficha administrativa/comercial diferenciada; ativação ≠ pagamento | aprovado | "diferencia situacao administrativa", "exibe situacao comercial", "ativacao != pagamento", label `commercial_pending` |
| 9 | B.5b impacto antes de confirmar mudança + auditoria com motivo/versão | aprovado | preview 200 (basic→intermediate, assento alvo 10), apply 204, auditoria `organization.plan_changed` c/ motivo + versões + `payment_recorded=false` |
| 10 | regressão: build estrito 0W/0E + suíte `-FreshDb` + paridade de catálogos | aprovado | **191/191 PASS**, TEST-EXIT=0 — `evidence/d31-freshdb-run.log`; paridade 4/4 (1353 chaves/idioma) |

Nenhum item falhou, foi bloqueado ou ficou não executado nesta fatia.

## Configuração sem segredos (produção multi-instância)

| Chave | Valor | Observação |
|---|---|---|
| `Cache:Store` | `postgres` | desenvolvimento: padrão `memory` |
| `DataProtection:KeysStore` | `postgres` | desenvolvimento: padrão `filesystem` |
| conexão Postgres / SMTP | variáveis de ambiente | nunca no repositório nem em logs |
| `Notifications:Smtp:*` + `Enabled=true` | apenas com host/porta válidos | sem configuração válida, o stub falha honestamente (`notification_provider_missing`) |

Duas instâncias apontando para o mesmo Postgres com as duas chaves em `postgres`
compartilham sessão e chaves de proteção — continuidade de login, logout e revogação
comprovados (item D acima).

## Arquivos e migrations

- **Novos**: `database/releases/odca-v054.sql`, `odca-v055.sql`;
  `src/Odca.Api/Controllers/PlatformNotificationsController.cs`;
  `src/Odca.Api/NotificationEnqueue.cs`; `src/Odca.Infrastructure/Distributed/`
  (cache + repositório de chaves Postgres);
  `src/Odca.Web/Controllers/PlatformNotificationsController.cs`;
  `src/Odca.Web/Services/OdcaApiClient.Notifications.cs`;
  `src/Odca.Web/Views/PlatformNotifications/`;
  `src/Odca.Worker/RenewalReminderWorker.cs`;
  `src/Odca.Worker/Transports/SmtpNotificationTransport.cs`;
  `tests/Odca.IntegrationTests/PlanServiceGateTests.cs`;
  `tests/Odca.IntegrationTests/SharedStorageTests.cs`.
- **Modificados** (42 arquivos, +1299/−100): destaque para
  `ContractStudioController.cs` (+ endpoint restore-from-version),
  `Sheet.cshtml` (bloco `#jornada`), `Customers/Details.cshtml` (ficha administrativa),
  `studio.js` (busca/estados/restaurar), `DatabaseSchema.cs`, `DependencyInjection.cs`,
  `Program.cs` (Api/Bootstrap/Worker), `NavigationRegistry.cs`, `StatusLabels.cs`,
  `ConsumptionRepository`, contratos de estúdio/consumo, 4 catálogos de localização
  (**+34 chaves × 4 idiomas**, paridade mantida — 1353 cada) e `database/odca.sql`.

## Dependências restantes / próximo backlog

- Decisão comercial de planos (a regra canônica 6/6/7 permanece valendo até ela; nenhum
  preço/fatura inventado neste incremento).
- OCR e upload (fatia seguinte do estúdio).
- Integração real de pagamento (`payment_recorded` segue manual/`false`).
- Credenciais reais do provedor SMTP (até lá o stub falha honestamente).
- Deploy físico multi-instância (a prova de continuidade foi feita com duas instâncias no
  mesmo Postgres local).

## Evidências (fora do repositório, em `%TEMP%\opencode\evidence\`)

- `d31-gate-twotenants.txt` — GATE: isolamento 2 tenants (API + RLS).
- `d31-gate-twostack.txt` — A.4/D: continuidade/revogação entre 2 instâncias (17/17).
- `d31-b345-run3.txt` — evolução da prova integrada (rodada intermediária).
- `d31-b345-final.txt` — **prova integrada B.3/B.4/B.5 final: 47/47 PASS, 0 falhas**
  (rota dupla de jornada, editor+restaurar+conflito, assinatura/inbox/aditivo/histórico,
  ficha administrativa + preview/aplicação de plano + auditoria).
- `d31-freshdb-run.log` — suíte completa `-FreshDb` 191/191 (FRESH-DB-OK → BUILD-OK → TEST-EXIT=0).
- `captures/` — 140 PNGs (7 telas × 4 idiomas × 4 viewports + zoom 200%).

Drifts aceitáveis em `odca_test_disposable` (aceitos, não afetam a suíte): draft
`8677f06d…` revertido ao retrato (row_version=2); título do tenant B alterado pela prova de
isolamento; `must_change_password=false` pré-existente (o script restaura); MFA do operador
re-enrolled a cada rodada (reset-mfa); eventos/auditoria da prova permanecem como trilha.
