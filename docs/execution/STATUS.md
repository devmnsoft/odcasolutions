# Status de execução

## Etapa atual

Referência revalidada em 09/09/2026: SHA base `c5e5064`. S00 continua **em validação de banco/navegador**. S01 está **parcial**: catálogo público e entrada pública de solicitações de privacidade existem; onboarding, MFA, equipe e operação do atendimento ainda não.

## Correção posterior às CI 34350222079 e 34352606607

- A configuração da API deixou de ser lida antecipadamente em `Program` e no registro da Infrastructure. JWT, política de autenticação e data source agora são materializados depois que o host terminou de compor suas fontes de configuração.
- Uma validação hospedada mantém o fail-fast para conexão, issuer, audience, chave, expiração e controles de produção, sem inserir configuração sintética na aplicação.
- Testes determinísticos cobrem aceitação da configuração isolada e rejeição de JWT/MFA inválidos. A execução local não foi possível neste ambiente porque o SDK `dotnet` não está instalado; a correção precisa do gate de CI/PostgreSQL.
- A CI 34352606607 do SHA `c5e5064` aprovou o secret scan e falhou no restore por NU1004: o projeto de integração pedia `coverlet.collector` 6.0.4 enquanto seu lock pedia 10.0.1. O projeto agora acompanha 10.0.1, versão já usada pelo outro projeto de testes e já materializada no lock; o modo bloqueado da CI foi preservado.
- `StartupValidationService` foi separado em arquivo próprio. Novos testes sobem o host real com configuração isolada, fazem uma requisição a `/health/live` e verificam que conexão ausente, issuer/audience vazios, chave curta e validade fora de 5–30 minutos impedem o início em Production.
- O cenário HTTP agregado foi dividido em casos independentes para entrada de privacidade, senha inválida, troca obrigatória, dashboard/catálogo e revogação após logout. MFA real continua pendente; portanto esses testes não são apresentados como aceite de acesso administrativo S01.
- A matriz factual deste incremento está em `docs/execution/S01_RECONCILIATION.md`.

## Implementado nesta evolução

- NU1004 corrigido: JwtBearer e Mvc.Testing alinhados em `10.0.12`, locks regenerados e restore bloqueado aprovado.
- Fixture isolada: sem fallback para `development-runtime.json`, sem mutação global de ambiente e com marcador, banco `odca_test*` e role `odca_test_app_login` obrigatórios.
- CI com secret scan independente e publicação de TRX mesmo após falha de testes.
- Runner valida todo o histórico conhecido antes de mutar; rejeita checksum divergente, lacuna e banco mais novo que o código. Readiness exige PostgreSQL 18 e schema exatamente na versão 3.
- Sessão de autenticação usa a duração JWT configurada; login durante bloqueio não prorroga a janela.
- BFF diferencia 401, 403, 429, timeout e 5xx em login, troca de senha, dashboard e planos; falha do backend não é apresentada como logout.
- Catálogo público separado da administração em `/api/v1/catalog/plans` e `/conheca-os-planos`, com rejeição explícita de plano vigente ambíguo ou incompleto.
- Migração 003 e fatia ODCA-PRIV-003: formulário `/privacidade`, API pública com rate limit, protocolo opaco, resposta neutra, histórico inicial e gravação por função de privilégio restrito.
- Teste comportamental preparado para role runtime: ausência de contexto, alternância de tenant, `role_permissions`, auditoria e FK cruzada A × B.

## Histórico de verificações (resultados não cumulativos)

| Verificação | Resultado |
|---|---|
| `dotnet restore Odca.sln --locked-mode` no SHA anterior | sucesso |
| `dotnet build Odca.sln --configuration Release --no-restore` no SHA anterior | sucesso; 0 avisos, 0 erros |
| `npm run build` no SHA anterior | sucesso |
| testes de domínio no SHA anterior | 10 aprovados |
| parser/checksum/snapshots de migração no SHA anterior | 6 aprovados |
| `dotnet test Odca.sln --configuration Release --no-build` no SHA anterior | 16 aprovados; 3 cenários PostgreSQL recusados antes de conexão por ausência do marcador exclusivo |
| CI remoto do SHA `c5e5064` | secret scan aprovado; restore falhou por NU1004 antes de build/assets/testes |
| `npm run build` nesta correção | sucesso com Node 20.20.2; o projeto declara Node >=24, portanto a CI continua sendo o gate na versão suportada |
| `dotnet restore Odca.sln --locked-mode` nesta correção | não executado: SDK .NET 10 ausente e download bloqueado por HTTP 403 no ambiente |
| suíte PostgreSQL atual | não executada: faltam as três variáveis exclusivas de teste e uma credencial administrativa autorizada |

## Parcial, pendente ou bloqueado

- `database/odca.sql` permanece um histórico rastreável e o runner pula versões aplicadas, mas a execução direta integral no pgAdmin ainda reexecuta corpos históricos. A equivalência/recovery/concurrency do pacote SQL não foi comprovada.
- O teste A × B e o fluxo API foram escritos, mas aguardam banco descartável autorizado; nenhum banco de desenvolvimento foi tocado.
- MFA real, cache/Data Protection compartilhados, onboarding de organização, confirmação de e-mail, assinatura, equipe/perfis, suporte temporário, tratamento autenticado de direitos, retenção executável e S02/S03 permanecem pendentes.
- A página pública de privacidade é um canal de entrada técnico; não publica texto jurídico, prazo ou contato não aprovados e não constitui comprovação de conformidade LGPD.
- QA visual responsivo e navegador autenticado ainda não foram executados.
- Marcos B–E não foram implementados por esta correção do gate A; permanecem explicitamente pendentes e não devem ser inferidos dos testes de startup.
