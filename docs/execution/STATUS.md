# Status de execução

## Etapa atual

Atualização local em 09/09/2026 no commit desta entrega, sobre a base remota `468dee4d6a532d58bb154d7b4932fe5511469d34`:

- CA1859 em `StartupValidationTests.Environment` corrigido com retorno concreto.
- MFA real de superadministrador implementado para TOTP: inscrição, confirmação, desafio, limitação de tentativas, replay por time-step, recovery codes hashados de uso único, segredo protegido por Data Protection, sessão com `mfa_verified` e policies que negam dashboard sem segundo fator.
- Cadastro inicial de cliente implementado em fatia S01: plano publicado escolhido, CPF/CNPJ normalizado, responsável, usuário, tenant, membership, assinatura vinculada ao `plan_version_id`, token de confirmação de e-mail hashado/uso único, outbox transacional local e home do cliente com estado comercial `commercial_pending`.
- Migrações 004 e 005 adicionadas ao SQL canônico e aos snapshots `odca-v004.sql`/`odca-v005.sql`, sem alterar os corpos 001–003.
- Telas MVC adicionadas para inscrição/desafio MFA, recovery codes, contratação, confirmação de e-mail e home inicial do cliente.

Evidência local desta atualização: `dotnet restore Odca.sln --locked-mode` aprovado; `dotnet build Odca.sln --configuration Release --no-restore` aprovado com 0 avisos/0 erros; `npm run build` aprovado; `dotnet test tests/Odca.Domain.Tests/Odca.Domain.Tests.csproj --configuration Release --no-build` aprovou 10/10; `dotnet test tests/Odca.IntegrationTests/Odca.IntegrationTests.csproj --configuration Release --no-build --filter "FullyQualifiedName!~S00FlowTests"` aprovou 21/21. A suíte completa ainda falha nos 10 cenários PostgreSQL por falta de `ODCA_TEST_ENVIRONMENT=ODCA_INTEGRATION_TESTS`, `ODCA_TEST_ADMIN_CONNECTION` e `ODCA_TEST_APP_CONNECTION`; essa recusa é intencional para impedir fallback em banco de desenvolvimento. Secret scan local não executou porque o Docker Desktop não estava ativo.

Referência revalidada em 09/09/2026: SHA base `c5e5064`. S00 continua **em validação de banco/navegador**. S01 está **parcial**: catálogo público, entrada pública de solicitações de privacidade, MFA de superadministrador e onboarding inicial de cliente existem; equipe, suporte temporário e operação do atendimento de direitos ainda não.

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

## Evolução prompt 08 — incremento local de integridade (09/09/2026)

- Corrigidos os três defeitos reproduzidos da CI 34367965217: contagem canônica de migrações, projeção explícita da home e conversão UTC do estado MFA.
- A elevação MFA agora renova, na mesma transação, a expiração persistida da sessão para exatamente a validade emitida no JWT. O início exige sessão/versão válidas e estado ainda não confirmado; a confirmação compara o segredo protegido observado.
- CPF/CNPJ passam por dígitos verificadores. A migração 006 preserva 001–005, impede mais de um cadastro vivo por documento e acrescenta campos de evidência de termos/aviso. Os marcadores de versão permanecem `pending-legal-approval`, sem representar texto jurídico aprovado.
- Repetição idempotente volta a criar a mensagem de outbox ligada ao token rotacionado, e a confirmação elimina o token de desenvolvimento.
- Chaves Data Protection têm caminho persistente configurável para API e BFF. O ticket store segue em cache de memória: múltiplas instâncias **não** estão declaradas prontas.

Ainda pendentes nesta entrega: transporte de e-mail de produção/worker com lease e retentativas, seleção explícita de organização, equipe/perfis/convites/quota, reset privilegiado de MFA, QA visual e suíte PostgreSQL descartável. Não há editor/OCR iniciado.
