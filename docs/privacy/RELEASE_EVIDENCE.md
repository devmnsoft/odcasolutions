# Evidências de liberação — privacidade

## Evolução S00/S01 desta execução

| Controle | Evidência | Resultado | Limitação |
|---|---|---|---|
| Histórico SQL imutável | runner valida versão, ordem, checksum e texto fora de blocos | 4 testes aprovados | upgrade real aguarda credencial PostgreSQL autorizada |
| Isolamento de papéis | migração 002 adiciona FK `(tenant_id, role_id)` | teste PostgreSQL preparado | ainda não executado localmente |
| Permissões/auditoria | RLS em `role_permissions` e `audit_events` | inspeção + build | testes negativos A×B permanecem obrigatórios |
| Dashboard administrativo | função agregada exige usuário administrador | teste de fluxo preparado | banco real pendente |
| Sessão BFF | cookie opaco; ticket protegido atrás de `IDistributedCache` | build aprovado | ambiente local usa memória; produção requer cache compartilhado |
| Logout degradado | cookie local removido mesmo sem resposta da API | implementação revisada | teste de navegador com API indisponível pendente |
| Suíte local | domínio e validação de migrações | 11 testes aprovados; 2 integrações não iniciaram por conexão recusada em `localhost:55432` | reconfigurar para o PostgreSQL nativo autorizado |
| Entrada Web | página de login local | HTTP 200 e token antiforgery renderizado | fluxo autenticado depende do banco |
| Entrada Web | página de login local | HTTP 200 e antiforgery renderizado | fluxo autenticado depende do banco |

Esta evidência não declara conformidade LGPD nem conclusão de S00/S01. Textos, prazos, bases legais, canal de titulares e acesso de suporte continuam sujeitos às etapas e validações registradas no backlog.

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
