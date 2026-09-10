# ODCA Solutions

Fundação executável do SaaS corporativo de gestão do ciclo de vida de contratos. Esta entrega implementa a etapa S00; ela não representa o produto comercial completo e não constitui certificação de conformidade com a LGPD.

## Pré-requisitos

- .NET SDK 10.0.400 (fixado em `global.json`)
- Node.js 24 ou superior
- PostgreSQL 18 nativo (recomendado no Windows) ou Docker
- certificado HTTPS de desenvolvimento do ASP.NET (`dotnet dev-certs https --trust`)

O cliente `psql` não é necessário: o runner usa Npgsql. O arquivo `database/odca.sql` também pode ser executado diretamente no Query Tool do pgAdmin.

## Setup local reproduzível

### PostgreSQL nativo no Windows (sem Docker)

Crie uma base vazia chamada `odca` pelo pgAdmin, usando uma conta administrativa já autorizada. O setup não altera `pg_hba.conf`, não redefine a senha existente e não usa `trust`. Na raiz do repositório, em uma sessão temporária do PowerShell:

```powershell
dotnet run --project src/Odca.Bootstrap -- init
$env:ODCA_NATIVE_ADMIN_CONNECTION = 'Host=localhost;Port=5432;Database=odca;Username=postgres;Include Error Detail=false'
dotnet run --project src/Odca.Bootstrap -- configure-native
Remove-Item Env:ODCA_NATIVE_ADMIN_CONNECTION
dotnet run --project src/Odca.Bootstrap -- migrate
dotnet run --project src/Odca.Bootstrap -- provision-test-access --environment Development
dotnet run --project src/Odca.Bootstrap -- show-login
```

`configure-native` pede a senha administrativa sem ecoá-la, valida a conexão e exige PostgreSQL 18+. Para automação local controlada, `Password` também pode vir na variável temporária. A string administrativa e a senha aleatória da role de runtime ficam somente em `%LOCALAPPDATA%\ODCA Solutions\development-runtime.json`, que não pertence ao repositório.

### Alternativa com Docker

Na raiz do repositório:

```powershell
dotnet run --project src/Odca.Bootstrap -- init
docker compose --env-file .env.local up -d --wait
dotnet run --project src/Odca.Bootstrap -- migrate
dotnet run --project src/Odca.Bootstrap -- show-login
```

`init` cria segredos aleatórios em `%LOCALAPPDATA%\ODCA Solutions` e o `.env.local` ignorado pelo Git. Em sistemas Unix, os JSON locais são gravados com modo `0600`. Reexecutar não troca credenciais existentes. `migrate` aplica somente versões ausentes, sob lock e checksum, cria uma role de aplicação sem `SUPERUSER`/`BYPASSRLS` e preserva a senha do superadministrador já criado.

`provision-test-access` é deliberadamente restrito a `--environment Development`, recusa a base genérica `postgres` e mostra host, porta e banco sem senha. Ele valida ou cria `admin@odca.local` como superadministrador e `cliente.teste@odca.local` como administrador da organização **ODCA Cliente de Demonstração**, com concessão local auditada do plano Basic vigente. A operação é transacional, relê perfil/vínculo/plano e confere senhas disponíveis pelo mesmo serviço usado no login. Uma conta comum preexistente nunca é promovida silenciosamente. Reexecução preserva senhas; para rotação explícita, use:

```powershell
dotnet run --project src/Odca.Bootstrap -- provision-test-access --environment Development --rotate-passwords
```

`show-login` consulta o banco configurado e só exibe uma senha inicial local quando ela confere com o hash persistido. A senha precisa ser alterada no primeiro acesso. Para recuperação explícita somente do superadministrador:

```powershell
dotnet run --project src/Odca.Bootstrap -- reset-password
dotnet run --project src/Odca.Bootstrap -- show-login
```

O reset revoga sessões, mas não remove o autenticador MFA. Depois da troca inicial, acesse `https://localhost:7144`, siga a inscrição exibida, adicione a chave no aplicativo autenticador e informe o TOTP. O provisionador distingue persistência, conferência de senha e login HTTP; ele **não** declara login HTTP aprovado nem MFA concluído sem executar esses passos.

Abra `Odca.sln` no Visual Studio e selecione o perfil de vários projetos `ODCA local`, ou inicie API, Web e Worker em um só terminal:

```powershell
.\scripts\run-local.ps1
```

Também é possível iniciar API e Web em terminais separados:

```powershell
dotnet run --project src/Odca.Api --launch-profile https
dotnet run --project src/Odca.Web --launch-profile https
dotnet run --project src/Odca.Worker --launch-profile Odca.Worker
```

- Web: `https://localhost:7144`
- API: `https://localhost:7143`
- Liveness: `https://localhost:7143/health/live`
- Readiness: `https://localhost:7143/health/ready`
- OpenAPI somente em Development: `https://localhost:7143/openapi/v1.json`

## Verificação

```powershell
dotnet restore Odca.sln --locked-mode
dotnet build Odca.sln --configuration Release --no-restore
npm run build
dotnet test Odca.sln --configuration Release --no-build
```

Os testes de integração nunca usam a configuração de desenvolvimento. Eles exigem um banco descartável cujo nome comece com `odca_test`, a role exclusiva `odca_test_app_login` e o marcador explícito abaixo. A fixture recusa qualquer outra combinação antes de migrar ou alterar dados:

```powershell
$env:ODCA_TEST_ENVIRONMENT = 'ODCA_INTEGRATION_TESTS'
$env:ODCA_TEST_ADMIN_CONNECTION = 'Host=localhost;Port=5432;Database=odca_test_local;Username=postgres;Password=<senha-admin>;Include Error Detail=false'
$env:ODCA_TEST_APP_CONNECTION = 'Host=localhost;Port=5432;Database=odca_test_local;Username=odca_test_app_login;Password=<senha-exclusiva-de-teste>;Include Error Detail=false'
dotnet test tests/Odca.IntegrationTests --configuration Release
```

Na CI, essas três variáveis apontam exclusivamente para o PostgreSQL sintético do workflow. Remova as variáveis da sessão quando terminar. As rotas públicas demonstráveis são `GET /conheca-os-planos` e `GET/POST /privacidade`; a segunda registra uma solicitação inicial e devolve protocolo opaco, sem confirmar a existência de contratos ou dados.

## Estrutura

Após autenticação completa, o BFF apresenta navegação distinta: superadministradores recebem contexto de plataforma e catálogo administrativo; clientes recebem somente a visão geral da organização. Senha inicial e MFA pendentes continuam no layout restrito, sem expor a navegação administrativa. A sidebar é recolhível no desktop e funciona como drawer com foco contido e fechamento por `Escape` no mobile.

- `src/Odca.Domain`: invariantes puras.
- `src/Odca.Application`: casos de uso e portas.
- `src/Odca.Contracts`: contratos HTTP versionados.
- `src/Odca.Infrastructure`: Dapper, Npgsql, identidade e banco.
- `src/Odca.Api`: API autenticada e health checks.
- `src/Odca.Web`: MVC/BFF; o cookie contém uma referência opaca e o ticket/token protegido fica no cache do servidor, sem chegar ao JavaScript. Desenvolvimento usa cache em memória; múltiplas instâncias exigem um provedor distribuído compartilhado.
- `src/Odca.Worker`: host separado para jobs das etapas posteriores.
- `src/Odca.Bootstrap`: setup, migração e credenciais locais.
- `database`: SQL canônico e snapshots de release.
- `docs`: especificação, decisões, execução, segurança e privacidade.

Não adicione contratos reais, CPF/CNPJ, tokens, chaves, `.env.local` ou arquivos de configuração gerados ao repositório público.

### Contexto de organização, equipe e convites

Depois de autenticar, abra `/organizacoes`. O tenant permanece explícito na URL de equipe e também no formulário; a API deriva o usuário do JWT e revalida vínculo e permissão antes de configurar o contexto RLS. Convites reservam um assento enquanto estiverem pendentes/enviados e somente são aceitos pela conta autenticada com o e-mail destinatário já verificado. A aceitação revoga as sessões da identidade para que as permissões sejam renovadas.

Em `Development`, o worker grava mensagens exclusivamente no diretório configurado por `Notifications:DevelopmentPickupDirectory`. Esse transporte é para contas sintéticas locais. Fora de Development, ausência de provedor mantém a mensagem em retry/falha, nunca como enviada.
