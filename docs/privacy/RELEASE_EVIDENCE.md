# Evidências de liberação — privacidade

Esta matriz registra controles técnicos e pendências; não é selo de conformidade.

| Requisito | Implementação S00 | Evidência | Responsável | Pendência |
|---|---|---|---|---|
| ODCA-PRIV-001 | tabela/inventário versionado e bloqueio documentado de integração | migração 001; `PROCESSING_REGISTER.md` | Engenharia + Privacidade | validar bases/responsáveis |
| ODCA-PRIV-002 | somente política de cookies necessários; sem trackers | inspeção Web | Produto + Privacidade | avisos/eventos/preferências em S01 |
| ODCA-PRIV-003 | desenho do fluxo | `DATA_SUBJECT_REQUESTS.md` | Privacidade | API/UI/testes em S01 |
| ODCA-PRIV-004 | categorias e bloqueio de operação sem prazo | `RETENTION_POLICY.md` | Privacidade + Operações | motor/holds/backups em etapas posteriores |
| ODCA-PRIV-005 | role mínima, RLS base, token/sessão revogável | SQL, auth flow test | Segurança | suporte com MFA/escopo em S01 |
| ODCA-PRIV-006 | cadastro obrigatório documentado; nenhum provedor ativo | `PROCESSORS_AND_TRANSFERS.md` | Compras + Privacidade | implementar por integração |
| ODCA-PRIV-007 | runbook inicial | `INCIDENT_RESPONSE.md` | Segurança | módulo e simulação posterior |
| ODCA-PRIV-008 | processing_activities/privacy_contacts incrementais | migração 001 | Engenharia | demais tabelas na etapa responsável |
| ODCA-PRIV-009 | sete documentos operacionais presentes | revisão do diretório | Privacidade | preenchimento organizacional contínuo |
| ODCA-PRIV-010 | senha/troca/logout/revogação cobertos | Domain + `S00FlowTests` | Engenharia/QA | suíte A × B, direitos, suporte, retenção, incidente e IA nas etapas aplicáveis |

Status externo: nenhum provedor, sandbox, CI remota, prazo legal ou contato real foi validado nesta entrega.
