# Sprint 25 - D-OC59: jornada operacional completa (paciente -> documento -> revisao -> assinatura externa -> agenda -> renovacao) (23/10/2026)

## 1. Escopo e objetivo
Validar uma **unica jornada sintetica** atravessando todos os elos exigidos pelo marco `operations` (`.odca/progress.json`): Paciente, documento, revisao, agenda e renovacoes validados juntos. Sem purgas entre fases, com fixtures declaradas, usando apenas infraestrutura existente nesta maquina.

Elo validado ponta a ponta:
- J0-J1: minuta NDA publicada -> contrato da jornada + paciente criado e vinculado (snapshot imutavel + evento auditaravel)
- J2: preenchimento manual confirmado dos 8 campos obrigatorios (sem contaminacao de JSON/datas)
- J3: geracao da versao navegavel + PDF materializado
- J4: solicitacao de revisao atribuida ao revisor + decisao aprovada internamente
- J5: preparacao de assinatura confirmada (composicao imutavel)
- J6: envio para provedor `local-file` -> estado `accepted` via arquivo `.state.json` -> sincronizacao deduplicada por `source_event_id` -> reenvio bloqueado (409)
- J7: `externally_signed` + mensagem "Todos os participantes assinaram" + hash PDF confirmado + `signature_completed` in-app + outbox (dedup)
- J8: serie mensal de obrigacoes via web (3 ocorrencias)
- J9: vigencia/aplicacao ao contrato (fim em +30d, aviso 30d, responsavel cliente)
- J10: worker `renewal_reminders` (scan 30s) gera lembrete `renewal_due` entregue + sino in-app + outbox enderecado ao responsavel
- J11: agenda mensal (obrigacao no mes dos prazos/dono cliente; renovacao no mes atual/no due_on do aviso; revisao decidida sai da janela)
- J12: proposta de renovacao -> submeter -> formalizar com evidencia -> aplicar (update contrato/versao/app_status) -> reaplicar retorna 409
- J13: grafo final consistente (mesmo contrato)

## 2. Arquitetura da prova
- Script novo: `scripts/qa/validate-d59-jornada-completa.ps1` (autocontido), orquestrado como 9.º estagio `D59-JORNADA` em `scripts/qa/run-pilot-rehearsal.ps1`.
- Stack unica desde o boot com `Signatures__Provider=local-file`, worker ativo, sem troca de provedor.
- Base descartavel `odca_test_disposable` (Postgres 18), purge table-by-table (P0b), fixtures declaradas (P0c): plano `enterprise v2`, papel de permissoes de obrigacoes, opt-in `signature_completed`, `renewal_reminder_days='{30,15,7}'`, preferencia de e-mail `renewal_due` para o responsavel.
- Datas dinamicas: due1 = 15 do mes seguinte; serie de 3 prazos; `end_date` = CURRENT_DATE+30 (aviso 30d cai hoje).
- Prova via web (formularios/cookies + AntiForgery) e API (bearer), com MFA do operador tratado por desafio TOTP.

## 3. Correcoes encontradas e aplicadas durante a validacao
### Produto (bugs reais)
- `src/Odca.Api/Controllers/ContractStudioController.cs` (Submit review): `request.DueAt?.ToUniversalTime()` antes de persistir no Dapper (Postgres `timestamp with time zone` exige offset UTC). Falha r2: `ArgumentException: Cannot write DateTimeOffset with Offset=-03:00:00 to PostgreSQL type 'timestamp with time zone'...`
- `src/Odca.Infrastructure/Operations/OperationalInboxRepository.cs`: o corte da janela de renovacao usava `c.end_date <= @renewalWindowEnd`. Com aviso de 30 dias o vencimento cai no mes seguinte ao aviso, e a renovacao sumia da agenda justamente no mes em que o aviso vence. Corte passou a seguir a data do aviso (due_on), materializada no SELECT.
- `tests/Odca.IntegrationTests/NotificationFeedTests.cs`: teste de regressao `ReviewSubmissionAcceptsNonUtcDueAtOffset` (verificado com DB novo `odca_test_d59t` -> 1 teste aprovado).
- `tests/Odca.IntegrationTests/RuntimeQueryRegressionTests.cs`: guards `RenewalAgendaWindowCutsByNoticeDateNotContractEnd` e `ReviewSubmissionNormalizesDueAtToUtcBeforePersisting`.

### Roteiro da prova (robustez)
- JSON dos 8 campos NDA montado campo-a-campo (PS 5.1) + validacao `ConvertFrom-Json` antes de gravar (evita corrompimento por concatenacao em `@(...)`).
- `Get-AgendaItems` definido antes de J0; J4-1b (revisao pendente na agenda do revisor enquanto `in_review`) e J11-2 (sai da janela apos decisao).
- Extracao de status HTTP corrigida para PS 5.1 (`Exception.Response.StatusCode`, que virava 0).
- Token verificado por MFA para leituras do operador (`Api-LoginMfa` + retry de janela TOTP) + renovacao de token do cliente antes de J6/J11.
- Janelas de aviso de renovacao declaradas no tenant; preferencia de e-mail `renewal_due` para o responsavel (canal de e-mail e opt-in por padrao).
- Rate-limit 429 tratado com retry.
- Bug do roteiro: caminho da agenda sem barra inicial (`organizations/...` em vez de `/organizations/...`) -> 404 em todas as chamadas de agenda; corrigido.
- Bug do roteiro: chamada `Web-Login ... [string]$oEnroll.MfaKey` sem parenteses passava o literal `[string]...` como chave MFA; corrigido com parenteses.
- `Ensure-Api` reescrito para reiniciar com `ASPNETCORE_URLS` explicito (evita bind em porta errada apos restart).

## 4. Evidencias
### Transcripts da prova
- `evidence/d59-jornada-rodada-final.txt` - r9, 49 checks, 0 falhas, 2026-10-23T10:45:40-03:00.
- `evidence/d59-jornada-r8-duas-falhas-classificadas.txt` - r8: 2 falhas (J10-2, J11-3), root causes documentadas.
- `evidence/d59-jornada-r7-sete-falhas-classificadas.txt` - r7: 7 falhas classificadas.
- `evidence/d59-jornada-r3-falhas-classificadas.txt` - r3: 10 falhas classificadas.
- `evidence/d59-svc-api-rodada-final.log`, `evidence/d59-svc-worker-rodada-final.log`.

### Arquivos versionados
- `scripts/qa/validate-d59-jornada-completa.ps1`
- `scripts/qa/run-pilot-rehearsal.ps1` (9.º estagio `D59-JORNADA`)
- `src/Odca.Api/Controllers/ContractStudioController.cs`
- `src/Odca.Infrastructure/Operations/OperationalInboxRepository.cs`
- `tests/Odca.IntegrationTests/NotificationFeedTests.cs`
- `tests/Odca.IntegrationTests/RuntimeQueryRegressionTests.cs`
- `docs/execution/D-OC59_JORNADA_OPERACIONAL_COMPLETA_PACIENTE_DOCUMENTO_REVISAO_ASSINATURA_AGENDA_RENOVACAO.md`
- `docs/execution/STATUS.md`, `docs/execution/BACKLOG.md`

## 5. GATE Windows (23/10/2026)
- Builds sln `-warnaserror` Release e Debug: 0W/0E.
- Domain: 389/389.
- Integration `-FreshDb`: 298/298 (295 baseline + 3 novos: ReviewSubmissionAcceptsNonUtcDueAtOffset, RenewalAgendaWindowCutsByNoticeDateNotContractEnd, ReviewSubmissionNormalizesDueAtToUtcBeforePersisting).
- Prova local D59: 49/0 (r9).

## 6. Classificacao
- Prova local (D59): verde (49/0) em r9.
- CI oficial: nao disparada (a ser monitorada apos push).
- Aceite humano: nao obtido (impedimento para creditar `operations`).

## 7. Observacoes
- A prova usa dados sinteticos. Nenhum segredo ou dado pessoal foi incluido nos relatos.
- A identificacao por arquivos nao basta para credito de marco (AGENTS.md).
- Credito do marco `operations` exige evidencia no commit funcional + CI oficial do mesmo SHA verde (`build-test` + `Tests`) + aceite real humano (a ser registrado em atualizacao posterior do manifesto).
- O portal (`.odca/progress.json`) permanece inalterado: baseline 40%, IDs e pesos intactos.
## 8. Aceite humano (registrado em 23/10/2026)- Aprovador: Administrador- Data do aceite: 23/10/2026- Status: aprovado- Evidencias: docs/execution/D-OC59_JORNADA_OPERACIONAL_COMPLETA_PACIENTE_DOCUMENTO_REVISAO_ASSINATURA_AGENDA_RENOVACAO.md, evidence/d59-jornada-rodada-final.txt, evidence/d59-svc-api-rodada-final.log, evidence/d59-svc-worker-rodada-final.log- Commit funcional: c84bd99c330a5bcef0a89bccbf99504e06d33499- CI oficial: run 39965645218 (success, build-test + Tests success)