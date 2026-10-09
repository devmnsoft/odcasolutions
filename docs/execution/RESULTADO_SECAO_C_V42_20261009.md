# Resultado da validação — Seção C (Solicitações/SLA + Aprovação ODCA), migração v042

**Data:** 09/10/2026 · **Suíte:** `scripts/qa/validate-c-web.ps1` (UTF-8+BOM) · **Veredito: ALL PASS 93/93 — 3 execuções consecutivas estáveis**

Ambiente: API `https://localhost:7143` (build pós-fixos, 0 erros/0 avisos) · Web `https://localhost:7144` (302 saudável) · Worker ativo (`SlaViolationScanWorker`) · PostgreSQL 18 nativo, banco `odca_test_disposable`, schema **v042** (`CurrentVersion=42`, checksum aplicado `cfb7990e…0947f`). Logins usados pelas suítes: `operador@odca.local` (com enrollment TOTP scripted para atingir sessão nível-MFA) e `cliente.teste@odca.local`. Fixtures com prefixo `Val C:`; suíte restaura o seed ao final (plano BASIC, operador rebaixado, MFA do operador limpo).

## Caderno de asserts (93)

| Grupo | Asserts | Resultado |
| --- | --- | --- |
| setup | `setup.cleanup` · `setup.schema_v42` · `setup.logins` | PASS |
| S1 gate de plano | `S1.gate_basic_409` · `S1.gate_plan_code` · `S1.plan_flip_enterprise` | PASS |
| S2 abertura | `S2.open_200` · `S2.open_payload` · `S2.protocol_number` · `S2.initial_status_aberta` · `S2.sla_snapshot_dues` · `S2.sla_timezone_explicit` · `S2.sla_calendar_explicit` · `S2.policies_seeded` | PASS |
| S3 idempotência | `S3.idempotent_replay_same_id` · `S3.idempotent_no_extra_row` | PASS |
| S4 pré-condição | `S4.iniciar_before_triage_409` | PASS |
| S5 triagem | `S5.triar_200` · `S5.status_triagem` · `S5.priority_applied` · `S5.triaged_at_stamped` · `S5.clock_recalculated_on_priority` | PASS |
| S6 reatribuição | `S6.reatribuir_200` · `S6.assignee_changed` · `S6.reatribuir_keeps_clock` | PASS |
| S7 concorrência | `S7.stale_409` · `S7.stale_current_version_ext` · `S7.iniciar_200` · `S7.status_em_atendimento` · `S7.first_response_stamped` | PASS |
| S8 pausa/retomada | `S8.pausar_sem_justificativa_400` · `S8.pausar_200` · `S8.paused_flag` · `S8.retomar_200` · `S8.dues_shifted_by_pause` · `S8.pause_trail_closed` | PASS |
| S9 espera do cliente | `S9.aguardar_200` · `S9.status_aguardando_cliente` · `S9.wait_pauses_clock` · `S9.client_message_200` · `S9.auto_resume_state` · `S9.auto_resume_clock` · `S9.retorno_cliente_event` | PASS |
| S9b resposta ODCA | `S9b.odca_message_200` | PASS |
| S10 exclusividade ODCA | `S10.tenant_resolver_403` · `S10.tenant_pausar_403` · `S10.tenant_reatribuir_403` | PASS |
| S11 resolução/encerramento | `S11.resolver_200` · `S11.status_resolvida` · `S11.resolved_at_stamped` · `S11.thread_client_and_odca` · `S11.audit_trail_full` · `S11.encerrar_by_tenant_200` · `S11.status_encerrada` · `S11.closed_rejects_message_409` | PASS |
| S12 cancelamento | `S12.cancel_sem_motivo_400` · `S12.cancel_200` · `S12.status_cancelada` · `S12.cancellation_reason_saved` · `S12.cancel_event` | PASS |
| S13 listas/filas | `S13.list_rows` · `S13.filter_status` · `S13.filter_service` · `S13.search_subject` · `S13.platform_queue_contains` · `S13.platform_org_column` · `S13.platform_tenant_filter` · `S13.platform_requires_admin` | PASS |
| S14 violações SLA | `S14.first_breach_marked` · `S14.resolution_breach_marked` · `S14.breach_set_once` · `S14.breach_events` · `S14.breach_visible_in_queue` | PASS |
| S15 calendário comercial | `S15.outside_window_deferred` · `S15.weekend_skipped` · `S15.continuous_calendar` | PASS |
| S16 aprovação ODCA | `S16.official_installed` · `S16.queue_200` · `S16.queue_has_surgical` · `S16.pending_by_default` · `S16.decide_aprovado_200` · `S16.approval_persisted` · `S16.decider_attributed` · `S16.reject_short_note_400` · `S16.reject_with_note_200` · `S16.rejection_shows_note` · `S16.audit_logged` · `S16.odca_only_decision` | PASS |
| S17 liberação do blocker | `S17.back_to_pending` · `S17.readiness_blocks_when_not_approved` · `S17.final_approval_200` · `S17.readiness_lifts_on_approval` | PASS |
| cleanup | `cleanup.final` | PASS |

## Regressão das etapas anteriores

- `validate-b34-web.ps1` (B.3.4): **PASS=93 FAIL=0** após todas as mudanças da Seção C — sem regressões.

## Restauração do ambiente conferida pós-suíte (psql direto)

- `Val C:%` restantes nas solicitações: **0** · `template_approvals` órfãs: **0**
- `operador@odca.local`: `is_platform_administrator=false` · estado MFA todo nulo (inclusive `mfa_pending_since`) · recovery codes removidos
- `odca.subscriptions` do tenant TC: `plan_version_id` = **BASIC** (`30000000-…-011`)

## Falhas reais encontradas e corrigidas durante o ciclo (todas com causa-raiz)

1. **Produto — lista 500 (`42804`)**: `odca.solicitations_list` declarava `"Subject"/"CancellationReason" text`, mas `RETURN QUERY` devolvia `varchar(200)` sem coerção implícita → casts explícitos `s.subject::text` / `s.cancellation_reason::text` na função (migração v042 + snapshot consolidado + troca aplicada no banco vivo).
2. **Produto — fila de aprovações 500 (`42883`)**: `set_config('odca.user_id', @actor, true)` recebia `Guid` (Npgsql envia `uuid`) e PG18 não resolve `set_config(unknown, uuid, boolean)` → SQL passou a `@actor::text`; além disso `local=true` expirei no fim da transação implícita da própria instrução e o `assert_platform_actor` da query seguinte via a falhando (`42501`) → GUC agora definido em escopo de sessão (`false`), ressobrescrito a cada requisição (`PlatformSolicitationsController.EnsurePlatformGuc`).
3. **Produto — envelope de versionamento obsoleto** (corrigido no início do ciclo): `EnvelopeResult` lia a chave errada do JSONB (`"currentVersion"` em vez de `"currentRowVersion"`) e serializava o campo de extensão vazio no 409 de conflito; leitura corrigida (visível em `S7.stale_current_version_ext`/`S10`).
4. **Suíte — TOTP próprio inválido**: padding base32 em PowerShell com `'{0:D5}' -f <string>` não zero-padding → bits desalinhados e códigos recusados (`400` no confirm); substituído por `.PadLeft(5,'0')` e validado contra os vetores RFC 6238 (T=59→`287082`, T=1111111109→`081804`).
5. **Suíte — reset de MFA incompleto**: o CHECK `users_mfa_state_ck` tem 4 colunas (inclui `mfa_pending_since`); reset com 3 colunas violava a constraint → reset completo no setup e no cleanup.
6. **Suíte — versionamento obsoleto proposital vs. cascata**: os fluxos intercalam escritas cliente/ODCA que avançam `row_version`; leituras frescas de `rowVersion` antes dos blocos S10/S11 (o 409 de `stale` precede o `permission_odca` na ordem de checagem da função).
