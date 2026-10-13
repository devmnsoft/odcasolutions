# D-OC19 — Fila de Exceções na Caixa Operacional (Bloco E, item 20)

Data: 2026-10-13 · Branch: codex/s00-foundation · Base: `090439a`

## Escopo

Dar à Caixa Operacional uma **fila de exceções**: itens que precisam de atenção fora do fluxo normal — falhas que ocorreram e precisam de decisão humana — reunidos em um único kind (`Exception`), sem nova tabela, sem migração e sem novo endpoint. A fila reusa o endpoint `GET /api/v1/organizations/{t}/inbox`, a página `/organizacoes/{t}/caixa`, a paginação/urgência existentes e o modelo de autorização por fonte.

## Fontes e regras adotadas

| Fonte | Condição de entrada na fila | Porta de permissão (idêntica à do kind de origem) | Urgência | Destino (âncora) |
|-------|------------------------------|----------------------------------------------------|----------|------------------|
| Renovação/aditivo/alteração — `contract_change_requests` | `status='conflict' OR application_status='failed'` (valores já permitidos pelo CHECK canônico; o código os grava juntos em `RenewalCenterController`) | `tenant.renewals.read` + escopo (`read_all`-equivalente do kind Renewal ou `responsible_id=viewer` em escopo próprio) | `DueToday` (`due_on = hoje` — exceção é atenção imediata; aparece no cartão "Prazo hoje") | `/organizacoes/{t}/contratos/{id}?from=caixa#renovacao` |
| Solicitação com SLA estourado — `solicitations` | ativa (`status NOT IN resolvida/encerrada/cancelada`) E (`first_response_breach_at IS NOT NULL OR resolution_breach_at IS NOT NULL`) | `tenant.solicitations.read` + escopo (`opened_by=viewer` em escopo próprio) | `Overdue` (`due_on = data da primeira brecha`, sempre no passado; aparece no cartão "Atrasada") | com contrato: `…/contratos/{id}?from=caixa#solicitacoes`; sem contrato: raiz do tenant (fallback existente) |

Decisões e justificativas:

- **A visibilidade da exceção é decidida pela branch de origem no SQL** (mesma porta do kind nativo). Não há permissão própria de "exceções": quem lê renovações vê exceções de renovação; quem lê solicitações, as de solicitação. No serviço, `CanRead(Exception)`/`CanReadTenant(Exception)` retornam `true` porque o filtro já aconteceu na branch (testado: linha `Exception` sobrevive ao serviço com todos os flags de permissão `false`).
- **Sem duplicação**: solicitações ativas **com** brecha saem da branch nativa `Solicitation` e aparecem apenas como `Exception` (a lista nativa fica mais curta e a fila concentra o crítico). Change requests em conflito não aparecem na branch `Renewal` hoje (ela lista contratos por janela de fim), logo o fonte de conflito é puramente aditivo.
- **Agenda mensal excluída**: as branches de exceção usam o mesmo sentinel de window do kind `Signature` (`CAST(@windowFrom AS date) IS NULL OR FALSE`) — exceção não tem data de trabalho previsível, então só vive na Caixa.
- **`notification_outbox.status='failed'` ficou de fora** (critério definido no design: "se o porte funcionar limpo"): a tabela não tem dono e não existe permissão `*.notifications.read` no catálogo — qualquer porta escolhida vazaría infraestrutura para readers sem relação com ela. Fica registrada como fonte candidata futura.
- **Sem migração**: projeção de leitura sobre tabelas existentes; o kind é computado, nunca persistido.
- **Vistas salvas**: `SavedViewFilterPolicy` passou a aceitar `kind=Exception` em vistas de listagem `inbox`.
- **UI/i18n**: nova opção no filtro "Tipo" da Caixa + rótulos de kind/ação nos 4 idiomas (`inbox.kind_exception`: Exceções / Exceptions / Excepciones / Exceptions; `inbox.action_exception`: Abrir exceção / Open exception / Abrir excepción / Ouvrir l'exception).
- **Controller de API inalterado**: o `Forbid` exige leitura de alguma das 5 fontes — exceção não adiciona a sexta.

## Mudanças

| Arquivo | Alteração |
|---------|-----------|
| `src/Odca.Application/Operations/OperationalInbox.cs` | `OperationalWorkKind.Exception` (novo membro) |
| `src/Odca.Application/Operations/OperationalInboxModels.cs` | `OperationalInboxRow.Anchor` (`string?`, default `null`) — âncora de navegação das projeções derivadas |
| `src/Odca.Application/Operations/OperationalInboxService.cs` | `Map`: roteia `Exception` pela âncora (`#renovacao`/`#solicitacoes`, fallback raiz do tenant p/ contrato nulo); `CanRead`/`CanReadTenant`: `Exception => true` |
| `src/Odca.Infrastructure/Operations/OperationalInboxRepository.cs` | SQL da Caixa: coluna `anchor` nas 7 branches; 2 novas branches `Exception` (gates por fonte, sentinel de window); exclusão de solicitacões em brecha da branch nativa |
| `src/Odca.Infrastructure/Operations/ContractSheetRepository.cs` | `NULL::text AS Anchor` na projeção de obrigações da Ficha (mesmo shape de coluna) |
| `src/Odca.Contracts/SavedViews/SavedViewFilterPolicy.cs` | whitelist de `kind` da listagem `inbox` + `"Exception"` |
| `src/Odca.Web/Views/Inbox/Index.cshtml` | opção do filtro + casos de rótulo (kind e ação) |
| `src/Odca.Web/Localization/*.json` (×4) | `inbox.kind_exception` + `inbox.action_exception` |
| `tests/Odca.Domain.Tests/OperationalInboxServiceTests.cs` | helper `Row` ganha `anchor`; 2 fatos novos (pass-through do serviço + roteamento do `Map` c/ fallback órfão) |

## Evidência comportamental (aplicação real, API `:7143` + Web `:7144`, banco `odca_test_disposable`)

Build da solution **0 avisos / 0 erros**. Domain **302/302 PASS** (incluindo os 2 fatos novos). Suíte de integração em `odca_test_local`: **idêntica à linha de base** — 111 aprovados / 54 falhas pré-existentes de ambiente (zero nova regressão deste incremento). Fixtures reversíveis (removidos ao final — teste T7): 1 linha em `contract_change_requests` (`conflict`/`failed`, contrato `cfd0bf87…`) e 1 flag de brecha em solicitação ativa existente (`first_response_breach_at = now() - 5 days`).

Sessões MFA consumidas nesta homologação: `ODCA-QA-REV18A` (admin/API), `ODCA-QA-REV19A` (admin/Web), `ODCA-QA-REV11A` (operador/API). Nota operacional: códigos de recuperação exigem ≥ 6 caracteres (`MfaChallengeRequest.Code` MinimumLength=6) — por isso o sufixo `A`.

| Teste | Cenário | Resultado observado |
|-------|---------|---------------------|
| T1 | Baseline (antes dos fixtures) `GET inbox?scope=organization&kind=Exception` (admin) | `total=0` — endpoint responde e a fila está vazia quando nada falhou |
| T2 | Após fixtures, admin escopo organização `kind=Exception` | `total=2`: (a) conflito → `urgency=DueToday`, `status=conflict`, `openUrl=/organizacoes/20000000…/contratos/cfd0bf87…?from=caixa#renovacao` (byte-exato), título = razão da mudança; (b) brecha SLA → `urgency=Overdue`, contrato nulo → `openUrl=/organizacoes/20000000…` (fallback), `contractTitle=(sem contrato vinculado)` |
| T3 | Admin escopo organização `kind=Solicitation` | A solicitacão em brecha **sumiu da lista nativa** (regra anti-duplicação); as 12 solicitações nativas restantes seguem visíveis |
| T4 | Portes por escopo/propriedade | admin `scope=mine` → **2** (é dono das duas); operador `scope=mine` → **0** (fixtures têm dono=admin — porte por dono funciona); operador `scope=organization` → **2** (o papel dele possui `renewals.read`+`solicitations.read` — porte por permissão funciona) |
| T5 | Agenda mensal 2026-10 (admin, organização) | JSON da agenda **sem nenhum** item `kind="Exception"` (sentinel de window exclui a fila da agenda) |
| T6 | Web: login real + MFA (cookie) → `GET /organizacoes/{t}/caixa?kind=Exception` | **200**; página renderiza o rótulo `Exceções · Atrasada`, o título da solicitação, `(sem contrato vinculado) · <dono>`, o link com âncora `#renovacao` e a opção `value="Exception"` no filtro "Tipo" |
| T7 | Restore: `DELETE` do change request + `NULL` na flag de brecha → `GET inbox kind=Exception` | `total=0` — a fila volta a zerar sem resíduo |

Classificação: comportamento provado por comandos reais da aplicação (HTTP + renderização + estado do banco). SQL usado apenas para criar/remover as duas linhas-fixture e para asserções de observação.

## Limitações

- `notification_outbox.status='failed'` ainda não alimenta a fila (sem dono/permissão limpa — ver decisão acima).
- A urgência de conflito é sempre "hoje" independentemente de **quando** a falha ocorreu (o bucket do caixa é temporal; o estado `conflict`/`failed` já sinaliza o quê). Se a fila crescer, um bucket "aberta há N dias" é evolução natural.
- A exclusão de solicitações em brecha da lista nativa muda o que `?kind=Solicitation` retorna (elas vivem na fila) — decisão registrada acima e coberta pelo teste T3.
- Sem push/merge/deploy automático.
