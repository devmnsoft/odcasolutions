# Entrega — Conclusão do sistema (Seções A–C): plataforma Odca operacional e validada de ponta a ponta

**Data:** 09/10/2026 · **Ramo:** `codex/s00-foundation` · **Esquema do banco:** **v042** (`CurrentVersion=42`, aplicado com checksum `cfb7990e2d98…6880947f`) · **Veredito da etapa C: ALL PASS 93/93 × 3 execuções · regressão B.3.4 93/93 sem falhas**

## 1. Escopo desta entrega

Fecha a **Seção C — Enterprise ODCA/SLA** do MVP (definida em `docs/execution/ENTREGA_JORNADA_MVP_20261006.md`): solicitações de clientes (revisão/adaptação/esclarecimento) com máquina de estados completa, SLA por plano/serviço/prioridade com fuso e calendário comercial explícitos, pausa/retomada justificadas, reatribuição sem reiniciar relógio, violações registradas pelo Worker, fila da Central ODCA e o workflow formal de **aprovação ODCA dos modelos cirúrgicos oficiais**, que libera o blocker `approval.odca.required` deixado pela etapa B.3.4. Persistem abertas somente a **Seção D (design + homologação visual/19 cenários)**.

## 2. O que foi implementado

| Camada | Superfície |
| --- | --- |
| Banco (**migração v042**, snapshot `database/releases/odca-v042.sql` + consolidado `database/odca.sql`) | Tabelas `odca.sla_policies`, `odca.solicitations`, `odca.solicitation_messages`, `odca.solicitation_events`, `odca.solicitation_pauses`, `odca.template_approvals` (+índices de fila); funções definer `solicitations_open/_transition/_message/_list`, `solicitation_detail/_payload`, `sla_add_business_minutes`, `sla_scan_violations`, `template_approvals_queue/_decide`; políticas SLA semeadas por plano/serviço/prioridade |
| API tenant (`SolicitationsController.cs`) | `GET/POST /api/v1/organizations/{t}/solicitations[/{id}[/messages|/actions]]` — abrir (gate Enterprise 409 `solicitations.plan.required`), listar/filtrar/buscar, detalhe com thread+eventos, mensagens com auto-retomada, ações com concorrência otimista (`rowVersion`; obsoleto → 409 `stale` + `currentRowVersion`); ações exclusivas da ODCA bloqueadas para tenants (403 `permission_odca`) |
| API plataforma (`PlatformSolicitationsController.cs`) | `GET /api/v1/platform/solicitations[/{id}]`, `POST .../{id}/messages|/actions` (fila unificada multi-organização com coluna de organização e filtro por tenant); `GET /api/v1/platform/template-approvals` e `POST /api/v1/platform/template-approvals/{tenantId}/{key}/decision` (aprovado/reprovado com nota obrigatória ≥ 5 caracteres, decisor e data auditados); política `PlatformAdministrator` (SuperAdministrator + senha alterada + sessão nível-MFA) |
| Web (`SolicitacoesController`, `AdminSolicitacoesController` + visões, `OdcaApiClient.Solicitations.cs`) | Portal do cliente (abrir, acompanhar, responder, encerrar, cancelar com motivo) e Central ODCA (triagem, prioridade, reatribuição, pausar/retomar, aguardar cliente, resolver, fila com violações destacadas, decisões de aprovação dos modelos) |
| Worker (`SlaViolationScanWorker.cs`) | Varredura periódica chamando `odca.sla_scan_violations()`: marca `first_response_breach_at`/`resolution_breach_at` uma única vez e registra eventos `violacao_*` que aparecem na fila da plataforma |
| Integração Studio | `CalculateReadiness` passa a consultar `odca.template_approvals`: blocker `approval.odca.required` presente enquanto tenant×`official_key` não estiver **aprovado**; decisão reversível — voltar a pendente re-bloqueia a confirmação de assinatura |

## 3. Validação na stack real (black-box)

`validate-c-web.ps1` (PowerShell 5.1, UTF-8+BOM, versionada em `scripts/qa/`; executada de cópia local contra a stack): HTTP direto contra API :7143/Web :7144 + asserts SQL diretos em `odca_test_disposable`. **ALL PASS 93/93 em três execuções consecutivas** — S1 gate de plano · S2 abertura com protocolo/SLA snapshot/fuso/calendário · S3 idempotência de abertura · S4–S7 fluxo de triagem/início com recalibragem de relógio e 409 de versão obsoleta · S8 pausa/retomada deslocando prazos e fechando trilha · S9 espera do cliente com auto-retomada por mensagem + evento `retorno_cliente` · S9b resposta ODCA na thread · S10 exclusividade ODCA (403) · S11 resolver/encerrar com trilha de auditoria completa · S12 cancelamento com motivo · S13 listas/filtros/busca e fila da plataforma (incl. exigência de admin) · S14 violações marcadas/expostas uma única vez · S15 calendário comercial (janela, fim de semana, contínuo) · S16 aprovação ODCA (fila, persistência, decisor, nota mínima, auditoria, exclusividade) · S17 ciclo do blocker de readiness (bloqueia→aprova→libera→reverte). Caderno completo: `docs/execution/RESULTADO_SECAO_C_V42_20261009.md`.

Regressão: `validate-b34-web.ps1` **93/93** após todas as mudanças. Estado do ambiente restaurado e conferido pós-suíte (fixtures `Val C:` zerados, tenant no plano BASIC, operador rebaixado sem MFA).

## 4. Bugs reais encontrados e corrigidos no ciclo (detalhes no log de resultados)

1. `solicitations_list` quebrava todas as listagens (500 `42804`): `RETURN QUERY` recusa coerção implícita `varchar(200)→text` — casts `::text` explícitos na migração/snapshot e na função viva.
2. Fila de aprovações 500 (`42883`→`42501`): GUC `odca.user_id` agora definido com `@actor::text` e escopo de **sessão** (o `local=true` expirava antes da query seguinte no pooling transacional).
3. Envelope 409 de conflito serializava `currentRowVersion` nulo (chave errada `"currentVersion"`) — leitura corrigida em `EnvelopeResult`.

Decisões de arquitetura/validação numeradas em **`DECISIONS-LOG.md`** (D-C1…D-C11).

## 5. Git

Toda a Seção C permanece na base local `codex/s00-foundation`: controllers API/Contracts/Web, visões, Worker, migração v042 + snapshot, `DatabaseSchema.CurrentVersion=42`, documentos desta entrega e suítes. Commit único da etapa nesta execução; **push somente quando solicitado**.
