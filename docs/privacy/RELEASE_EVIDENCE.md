# Evidências de liberação — privacidade

## Evolução S00/S01 — 09/09/2026

Atualização do gate no SHA base `c5e5064`: a CI 34352606607 aprovou o secret scan, mas falhou em restore por divergência do `coverlet.collector`; build, assets e testes não executaram. A correção alinha o projeto de integração e seu lock em 10.0.1 e adiciona testes de host, porém ainda aguarda CI. Não reutilizar as contagens do SHA anterior como resultado deste incremento.

| Controle | Evidência | Resultado | Limitação |
|---|---|---|---|
| Histórico SQL imutável | runner valida versão, ordem, checksum, lacunas e banco mais novo | 6 testes de parser/checksum/snapshot aprovados | SQL direto/recovery/concurrency aguardam banco autorizado |
| Isolamento de testes | fixture exige marcador, banco `odca_test*` e role exclusiva | CI 34350222079 executou a suíte com PostgreSQL descartável | ampliar cobertura de pool e novos endpoints |
| Permissões/auditoria | cenário runtime A×B cobre contexto ausente/alternado, permissões, auditoria e FK cruzada | aprovado na CI 34350222079 | ampliar para os futuros endpoints S01 |
| Dashboard administrativo | função agregada exige usuário administrador | teste de fluxo preparado | banco real pendente |
| Sessão BFF | cookie opaco; ticket protegido atrás de `IDistributedCache` | build aprovado | ambiente local usa memória; produção requer cache compartilhado |
| Logout degradado | cookie local removido mesmo sem resposta da API | implementação revisada | teste de navegador com API indisponível pendente |
| Autenticação | duração configurável e bloqueio sem extensão automática | 2 testes novos; 10 testes de domínio aprovados | MFA real permanece pendente |
| Entrada de direitos | migração 003, função restrita, API/Web, protocolo opaco e resposta neutra | cenário HTTP agora independente, ainda não executado no novo SHA | triagem, identidade, prazo, exportação e operação permanecem pendentes |
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

Status externo: a CI 34350222079 do SHA `5ab8df3b8585101bae5d2068255c58e74f01ae81` aprovou restore, build, assets, secret scan, 10 testes de domínio e 8 testes da assembleia de integração; 1 teste integrado falhou durante a inicialização por ausência da seção `Jwt`. Este incremento corrige a composição antecipada, mas ainda não possui execução de CI própria. Nenhum provedor, sandbox, prazo legal ou contato real foi validado nesta entrega.
