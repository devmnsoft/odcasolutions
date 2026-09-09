# Contrato de execução

Data-base: 2026-09-08. Branch: `codex/s00-foundation`. A especificação vinculante está em `docs/product/ODCA_SPEC.md`.

| Requisito | Entrega S00 | Arquivos principais | Evidência automatizada | Estado |
|---|---|---|---|---|
| ODCA-001 | Preservar seções 1–13 da especificação | `docs/product/ODCA_SPEC.md` | revisão de arquivo | concluído |
| ODCA-002 | Solução .NET 10 em camadas e worker separado | `Odca.slnx`, `src/*` | build Release | concluído |
| ODCA-003 | SQL PostgreSQL 18 incremental, reexecutável, com lock/checksum | `database/odca.sql`, `database/releases/odca-v001.sql` | `CanonicalSql*` | implementado; execução local depende do banco |
| ODCA-004 | Bootstrap idempotente e segredos fora do Git | `src/Odca.Bootstrap`, `.gitignore` | execução de `init`; teste de login | implementado |
| ODCA-005 | API JWT curta, ProblemDetails, rate limit, health e OpenAPI Dev | `src/Odca.Api` | `S00FlowTests` | implementado |
| ODCA-006 | MVC/BFF com cookie seguro, antiforgery, login e dashboard real | `src/Odca.Web` | build + fluxo manual após banco | implementado |
| ODCA-007 | Troca inicial, bloqueio, logout, revogação e security version | Identity em Application/Infrastructure/API | `S00FlowTests` | implementado |
| ODCA-008 | CI sem `continue-on-error`, banco sintético e secret scan | `.github/workflows/ci.yml` | execução no GitHub após push | implementado, não validado no provedor |
| ODCA-009 | Logs estruturados minimizados | `src/Odca.Api/Program.cs`, docs de segurança | inspeção + testes futuros de captura | parcial |
| ODCA-PRIV-001 | Inventário versionado inicial | SQL e `docs/privacy/PROCESSING_REGISTER.md` | seed reexecutável | implementado; base legal pendente de validação |
| ODCA-PRIV-002 | Avisos, preferências e consentimentos | S01 | testes S01 | backlog |
| ODCA-PRIV-003 | Entrada pública de direitos | migração 003, Privacy Application/API/Web | testes unitários aprovados; integração preparada | parcial: operação do atendimento pendente |
| ODCA-PRIV-004 | Política/motor de retenção | modelo documental em S00; execução em S02+ | testes de retenção | parcial |
| ODCA-PRIV-005 | Isolamento base e desenho de suporte | RLS base e threat model; sessões de suporte em S01 | testes negativos S01 | parcial |
| ODCA-PRIV-006 | Cadastro de fornecedores/transferências | modelo documental; implementação por integração | testes das etapas | parcial |
| ODCA-PRIV-007 | Resposta a incidentes | runbook inicial | simulação em S10 | parcial |
| ODCA-PRIV-008 | Tabelas Privacy incrementais | `processing_activities`, `privacy_contacts` | migração | parcial conforme S00 |
| ODCA-PRIV-009 | Documentação operacional | `docs/privacy/*` | revisão de presença | concluído para S00 |
| ODCA-PRIV-010 | Testes obrigatórios aplicáveis | autenticação/revogação; demais acompanham etapas | testes Domain/Integration | parcial |
| ODCA-010 | Execução local sem Docker | `Odca.sln`, `.slnLaunch`, `scripts/run-local.ps1`, Bootstrap `configure-native` | build Release; banco depende de credencial local autorizada | implementado; integração pendente |
| ODCA-011 | Migrações históricas incrementais | runner e migrações 002/003 em `database/odca.sql` | 6 testes de parser/checksum/snapshot; teste real preparado | runner implementado; SQL direto e PostgreSQL real pendentes |
| ODCA-S01-PLANOS | Catálogo Basic/Intermediário/Enterprise | SQL, Application, Infrastructure, API e Web pública/autenticada | build + teste integrado preparado | parcial; onboarding/assinatura pendentes |

Estados distinguem código implementado, teste local e validação externa. Nenhum provedor, hipótese legal ou contato de privacidade foi tratado como aprovado.
