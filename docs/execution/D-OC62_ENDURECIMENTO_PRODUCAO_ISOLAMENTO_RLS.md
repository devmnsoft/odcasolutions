# Sprint 28 - D-OC62: endurecimento tecnico de producao (isolamento RLS AxB) (23/10/2026)

## 1. Escopo e objetivo
Prova tecnica de isolamento multi-tenant em nivel HTTP (marco `production`, `.odca/progress.json`): um token de um tenant (cliente) tenta ler dados de outros tenants (isolamento A e B) e deve ser recusado (403), enquanto a leitura do proprio tenant funciona (200). Complementa os testes de integracao (`SeedTenantIsolationScenarioAsync`) com evidencia end-to-end via API.

## 2. Resultado da prova (r1, 7/0)
- J0-1: cliente do tenant home NAO le contratos do tenant A (403).
- J0-2: cliente do tenant home NAO le contratos do tenant B (403).
- J0-3: cliente do tenant home NAO le inbox do tenant A (403).
- J0-4: cliente do tenant home NAO le agenda do tenant A (403).
- J1-1: leitura do proprio tenant funciona (200).
- J2-1: endpoint de notificacoes do proprio tenant responde (200).
- W1: login API do cliente (bearer) OK.

## 3. Evidencias
- Transcript: `evidence/d62-producao-rodada-final.txt` (r1, 7/0).
- Script: `scripts/qa/validate-d62-producao.ps1` (HTTP-level, reutiliza stack unica).

## 4. Arquivos versionados
- `scripts/qa/validate-d62-producao.ps1`
- `docs/execution/D-OC62_ENDURECIMENTO_PRODUCAO_ISOLAMENTO_RLS.md`
- `docs/execution/STATUS.md`, `docs/execution/BACKLOG.md`
- `evidence/d62-producao-rodada-final.txt`

## 5. GATE
- Nenhuma mudanca de produto (apenas prova). GATE da S25 permanece valido.

## 6. Classificacao
- Prova local (D-OC62): verde (7/0) em r1.
- CI oficial: a ser disparada apos o push deste commit.
- Aceite humano: nao obtido (impedimento para creditar `production`); a parte de SMTP real, storage real e recuperação permanece pendente.

## 7. Observacoes
- A prova valida isolamento RLS em nivel HTTP, complementando os testes de integracao.
- O marco `production` (peso 10) exige tambem SMTP real, storage real e recuperação comprovados (aceite humano/infra real pendente).
- `.odca/progress.json` permanece inalterado.
## 8. Recuperação (restore) — stage S10-BACKUP (24/10/2026)- Stage S10-BACKUP (un-backup-restore.ps1) executado com **exit=0**, **26/26 checks OK** (paridade RLS tenants_as_app_no_context: src=109/rst=109; SECURITY DEFINER executável após restore; banco de restauração removido).- Transcript: evidence/d44-backup-restore-rodada-final.txt.- Complementa a prova de isolamento RLS (D-OC62) com evidência de recuperação validada.