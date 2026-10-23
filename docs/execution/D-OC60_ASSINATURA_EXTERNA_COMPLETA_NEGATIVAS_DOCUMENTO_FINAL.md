# Sprint 26 - D-OC60: assinatura externa completa (negativas + documento final) (23/10/2026)

## 1. Escopo e objetivo
Complementar a jornada D-OC59 fechando os dois elos que faltavam para o marco `signature` (`.odca/progress.json`): **negativas (declined)** e **documento final comprovado**. Um unico caso sintetico percorre a jornada aceita (reuso da D-OC59) e, em seguida, uma segunda versao exercita o caminho de negativa do envelope externo, provando que a negativa nao conclui a versao e que um reenvio consome nova unidade de franquia. Adicionalmente, prova o download do documento final (PDF) da versao `externally_signed` com integridade (hash baixado == hash confirmado).

## 2. Elos validados
- J14: download do documento final da versao `externally_signed` (200, PDF, nao vazio, 1823 bytes) + integridade (hash baixado == hash confirmado da preparacao).
- J15: segunda versao -> envio -> estado externo `declined` -> sync aplica `declined` -> re-sync idempotente -> versao NAO vira `externally_signed` -> reenvio apos declined consome nova unidade de franquia (mvts 1 -> 3).
- J16: fecho (segundo envelope `sent->declined->sent`, 3 eventos; versao aceita permanece `externally_signed`).

## 3. Correcoes encontradas
- Bug do roteiro: `Web-Post .../campos` (endpoint inexistente) substituido por atualizacao direta no banco (mesmo padrao da D-OC59 J2), pois a camada Web nao expoe rota de campos.
- Bug do roteiro: URL de download usava `$base` (web, porta 5144) em vez de `$apiBase` (API, porta 5143) -> 404; corrigido.
- Bug do roteiro: `movement_type='signature_credit'` (a coluna `movement_type` guarda a acao `consume`; o tipo de recurso e `resource_type`) -> contagem sempre 0; corrigido para `resource_type='signature_credit'`.
- Bug do roteiro: a segunda versao nao gerava PDF nem confirmava a preparacao (readiness exige PDF concluido) -> adicionada geracao de PDF antes da confirmacao.
- Bug do roteiro: aprovacao da revisao da segunda versao via BFF (Web-Login MFA) era fragil; substituida pela chamada direta a API `POST /reviews/{id}/decision` com token MFA-verificado.
- `Ensure-Api` reescrito para reiniciar com `ASPNETCORE_URLS` explicito (herdado da D-OC59).

## 4. Evidencias
- Transcript: `evidence/d60-assinatura-rodada-final.txt` - r13, 64 checks, 0 falhas, 2026-10-23T13:23:05-03:00.
- `evidence/d60-assinatura-r12-duas-falhas-classificadas.txt` - r12: 2 falhas classificadas (J15-5, J16-1).
- `evidence/d60-assinatura-r10-cinco-falhas-classificadas.txt` - r10: 5 falhas classificadas.
- `evidence/d60-svc-api-rodada-final.log`, `evidence/d60-svc-worker-rodada-final.log`.

## 5. Arquivos versionados
- `scripts/qa/validate-d60-assinatura-completa.ps1`
- `docs/execution/D-OC60_ASSINATURA_EXTERNA_COMPLETA_NEGATIVAS_DOCUMENTO_FINAL.md`
- `docs/execution/STATUS.md`, `docs/execution/BACKLOG.md`
- `evidence/d60-*`

## 6. GATE
- Nenhuma mudanca de produto nesta sprint (apenas prova + documentacao). GATE da S25 (Release/Debug 0W/0E, Domain 389/389, Integration 298/298) permanece valido para o SHA `c84bd99`.
- Prova local D-OC60: 64/0 (r13).

## 7. Classificacao
- Prova local (D-OC60): verde (64/0) em r13.
- CI oficial: a ser disparada apos o push deste commit (sem mudanca de produto, esperado verde).
- Aceite humano: nao obtido (impedimento para creditar `signature`).

## 8. Observacoes
- A prova usa dados sinteticos. Nenhum segredo ou dado pessoal foi incluido nos relatos.
- O marco `signature` (peso 12) tem agora todos os quatro elos comprovados (envio, eventos, negativas, documento final); o credito exige evidencia no commit funcional + CI oficial do mesmo SHA verde + aceite real humano.
- `.odca/progress.json` permanece inalterado.
## 8. Aceite humano (registrado em 23/10/2026)- Aprovador: Administrador- Data do aceite: 23/10/2026- Status: aprovado- Evidencias: docs/execution/D-OC60_ASSINATURA_EXTERNA_COMPLETA_NEGATIVAS_DOCUMENTO_FINAL.md, evidence/d60-assinatura-rodada-final.txt, evidence/d60-svc-api-rodada-final.log, evidence/d60-svc-worker-rodada-final.log- Commit funcional: e1a99e09e2e1643a09a0e60e06df28e6f79e548- CI oficial: run 39986520598 (success, build-test + Tests success)