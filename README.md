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

`init` cria segredos aleatórios em `%LOCALAPPDATA%\ODCA Solutions` e o `.env.local` ignorado pelo Git. Reexecutar não troca credenciais existentes. `migrate` aplica somente versões ausentes, sob lock e checksum, cria uma role de aplicação sem `SUPERUSER`/`BYPASSRLS` e preserva a senha do superadministrador já criado. `show-login` é a única forma documentada de exibir a credencial local; a senha precisa ser alterada no primeiro acesso. Para recuperação explícita:

```powershell
dotnet run --project src/Odca.Bootstrap -- reset-password
dotnet run --project src/Odca.Bootstrap -- show-login
```

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

Os testes de integração usam a configuração local criada pelo bootstrap. Na CI, usam exclusivamente o serviço PostgreSQL sintético do workflow.

## Estrutura

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
