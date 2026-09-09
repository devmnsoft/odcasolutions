# Evidências de liberação — privacidade

## Evolução S00/S01 — 09/09/2026

| Controle | Evidência | Resultado | Limitação |
|---|---|---|---|
| Histórico SQL imutável | runner valida versão, ordem, checksum, lacunas e banco mais novo | 6 testes de parser/checksum/snapshot aprovados | SQL direto/recovery/concurrency aguardam banco autorizado |
| Isolamento de testes | fixture exige marcador, banco `odca_test*` e role exclusiva | revisão + build | suíte PostgreSQL ainda não executada |
| Permissões/auditoria | cenário runtime A×B cobre contexto ausente/alternado, permissões, auditoria e FK cruzada | teste implementado | execução aguarda banco descartável |
| Dashboard administrativo | função agregada exige usuário administrador | teste de fluxo preparado | banco real pendente |
| Sessão BFF | cookie opaco; ticket protegido atrás de `IDistributedCache` | build aprovado | ambiente local usa memória; produção requer cache compartilhado |
| Logout degradado | cookie local removido mesmo sem resposta da API | implementação revisada | teste de navegador com API indisponível pendente |
| Autenticação | duração configurável e bloqueio sem extensão automática | 2 testes novos; 10 testes de domínio aprovados | MFA real permanece pendente |
| Entrada de direitos | migração 003, função restrita, API/Web, protocolo opaco e resposta neutra | testes unitários aprovados; teste API/DB implementado | triagem, identidade, prazo, exportação e operação permanecem pendentes |
| Suíte local sem banco | domínio, assets e validação de migrações | build 0/0, assets, 16 testes aprovados; 3 cenários DB recusados com mensagem segura | suíte PostgreSQL requer configuração explícita |

Esta evidência não declara conformidade LGPD nem conclusão de S00/S01. Textos, prazos, bases legais, canal de titulares e acesso de suporte continuam sujeitos às etapas e validações registradas no backlog.

Esta matriz registra controles técnicos e pendências; não é selo de conformidade.

| Requisito | Implementação S00 | Evidência | Responsável | Pendência |
|---|---|---|---|---|
| ODCA-PRIV-001 | tabela/inventário versionado e bloqueio documentado de integração | migração 001; `PROCESSING_REGISTER.md` | Engenharia + Privacidade | validar bases/responsáveis |
| ODCA-PRIV-002 | somente política de cookies necessários; sem trackers | inspeção Web | Produto + Privacidade | avisos/eventos/preferências em S01 |
| ODCA-PRIV-003 | entrada pública com protocolo opaco, resposta neutra e evento inicial | migração 003; API/Web; testes unitários e integração preparada | Engenharia + Privacidade | operação, identidade, prazo, encaminhamento e exportação |
| ODCA-PRIV-004 | categorias e bloqueio de operação sem prazo | `RETENTION_POLICY.md` | Privacidade + Operações | motor/holds/backups em etapas posteriores |
| ODCA-PRIV-005 | role mínima, RLS base, token/sessão revogável | SQL, auth flow test | Segurança | suporte com MFA/escopo em S01 |
| ODCA-PRIV-006 | cadastro obrigatório documentado; nenhum provedor ativo | `PROCESSORS_AND_TRANSFERS.md` | Compras + Privacidade | implementar por integração |
| ODCA-PRIV-007 | runbook inicial | `INCIDENT_RESPONSE.md` | Segurança | módulo e simulação posterior |
| ODCA-PRIV-008 | processing_activities/privacy_contacts incrementais | migração 001 | Engenharia | demais tabelas na etapa responsável |
| ODCA-PRIV-009 | sete documentos operacionais presentes | revisão do diretório | Privacidade | preenchimento organizacional contínuo |
| ODCA-PRIV-010 | senha/troca/logout/revogação cobertos | Domain + `S00FlowTests` | Engenharia/QA | suíte A × B, direitos, suporte, retenção, incidente e IA nas etapas aplicáveis |

Status externo: o CI do SHA remoto `f710c42` foi consultado e falhou por NU1004; a correção atual ainda não foi enviada. Nenhum provedor, sandbox, prazo legal ou contato real foi validado nesta entrega.
