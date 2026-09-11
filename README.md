## Configuração de desenvolvimento no projeto principal

API, Web, Worker e Bootstrap usam `src/Odca.Api/development-runtime.json` ao executar dentro do repositório. `ODCA_RUNTIME_CONFIG` continua permitindo um caminho explícito. Produção e Testing não carregam esse arquivo automaticamente.

Execute `.\scripts\setup-local.ps1` na raiz. O script confere o SDK do `global.json`, reutiliza o Bootstrap e só pede a senha sem eco quando não existe configuração atual ou anterior. Ele grava a conexão escolhida sem trocar `Database=postgres`, usuário ou parâmetros; reexecuções validam e completam apenas seções ausentes. Se houver configuração antiga em LocalApplicationData, `init` copia o conteúdo existente sem sobrescrever um arquivo no projeto; o original é preservado. Use `-TestConnection` para também abrir uma conexão, sem executar migrations.

`development-runtime.example.json` é apenas uma referência sem segredos; não o copie como configuração funcional. O arquivo real e seus backups são ignorados pelo Git e não são publicados. As credenciais iniciais continuam no diretório pessoal usado pelo Bootstrap. Não gere novamente uma chave JWT válida para mudar o arquivo de lugar.

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

O ambiente confirmado usa a base existente `postgres` e o schema `odca`. O setup não altera o banco, `pg_hba.conf`, senha do servidor ou outros bancos e não usa `psql`. No Windows PowerShell 5.1 ou PowerShell 7, a partir de qualquer pasta, execute o script pelo caminho do repositório (na raiz, o primeiro comando é):

```powershell
.\scripts\setup-local.ps1
```

Ele cria/preserva `src/Odca.Api/development-runtime.json`, gera somente segredos ausentes e executa o diagnóstico estrutural. “JSON válido”, “conexão aprovada” e “schema compatível” são estados distintos na saída. Para diagnóstico posterior, use `diagnose` (sem acessar o banco) ou `diagnose --connection`; ambos retornam código diferente de zero para configuração inválida ou falha de conexão.

Somente depois da conexão aprovada, execute separadamente os comandos abaixo, que **alteram o banco**:

```powershell
dotnet run --project src/Odca.Bootstrap -- migrate
dotnet run --project src/Odca.Bootstrap -- provision-test-access --environment Development --allow-postgres-development
dotnet run --project src/Odca.Bootstrap -- show-login
```

`configure-native` mantém separadas `DatabaseAdmin` (operações de setup) e `Database` (aplicação) e exige que ambas sejam informadas explicitamente; nunca substitui a segunda por `odca_app_login`. Neste Development ambas preservam exatamente a conexão escolhida com `Database=postgres`, `Search Path=odca`, pool 0–50, timeouts 30/60 e `Application Name=odca.api`. Usar `postgres` localmente não demonstra isolamento RLS; os testes automatizados continuam exigindo a role restrita e banco descartável, e produção mantém menor privilégio obrigatório.

As migrations foram inspecionadas quanto ao alcance: tabelas, funções, políticas e dados da aplicação são qualificados no schema `odca`, mas a migration inicial também cria, se ausente, a role compartilhada de cluster `odca_app`; o comando `migrate` provisiona ainda o login `odca_app_login`. Não há criação de extensões. Esses objetos preexistentes não são removidos, e o percurso não executa `DROP DATABASE`, `DROP SCHEMA` ou `TRUNCATE`.

### Alternativa com Docker

Na raiz do repositório:

```powershell
dotnet run --project src/Odca.Bootstrap -- init
docker compose --env-file .env.local up -d --wait
dotnet run --project src/Odca.Bootstrap -- migrate
dotnet run --project src/Odca.Bootstrap -- show-login
```

`init` cria segredos aleatórios em `%LOCALAPPDATA%\ODCA Solutions` e o `.env.local` ignorado pelo Git. Em sistemas Unix, os JSON locais são gravados com modo `0600`. Reexecutar não troca credenciais existentes. `migrate` aplica somente versões ausentes, sob lock e checksum, cria uma role de aplicação sem `SUPERUSER`/`BYPASSRLS` e preserva a senha do superadministrador já criado.

`provision-test-access` exige `--environment Development`; a base genérica `postgres` exige ainda a confirmação explícita `--allow-postgres-development`, recusada fora de Development. O comando mostra apenas destino sanitizado e valida ou cria `admin@odca.local` como superadministrador e `cliente.teste@odca.local` como administrador da organização **ODCA Cliente de Demonstração**, com concessão local auditada do plano Basic vigente. A operação é transacional, relê perfil/vínculo/plano e confere senhas disponíveis pelo mesmo serviço usado no login. Uma conta comum preexistente nunca é promovida silenciosamente. Reexecução preserva senhas; para rotação explícita, use:

```powershell
dotnet run --project src/Odca.Bootstrap -- provision-test-access --environment Development --allow-postgres-development --rotate-passwords
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

Em terminal interativo, `run-local.ps1` encaminha automaticamente ao setup se o arquivo estiver ausente; com `-NonInteractive`, falha antes de criar qualquer processo e informa o caminho esperado. Antes de anunciar prontidão, ele valida a configuração e aguarda `https://localhost:7143/health/ready` responder com sucesso.

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

Depois de autenticar, abra `/organizacoes` e escolha a organização pelo nome. A central operacional fica em `/organizacoes/{tenantId}/equipe` com abas `pessoas`, `convites` e `perfis` (parâmetro `tab`). O tenant permanece explícito na URL e nos formulários; a API deriva o usuário do JWT e revalida vínculo e permissão antes de configurar o contexto RLS.

Convites reservam um assento enquanto estiverem `pending`/`sent` e não expirados. Aceite exige identidade autenticada com o e-mail destinatário já verificado e **não** reativa vínculo bloqueado ou inativo. Após o aceite, a sessão é encerrada para renovar autorizações no próximo login. Enfileirar um convite **não** significa e-mail entregue.

Em `Development`, o worker grava mensagens exclusivamente no diretório configurado por `Notifications:DevelopmentPickupDirectory`. Fora de Development, ausência de provedor mantém a mensagem em retry/falha com código `notification_provider_missing`, nunca como enviada.

API e Worker devem compartilhar o mesmo `DataProtection:KeysPath` persistente e o mesmo `ApplicationName` (`ODCA Solutions`) para desencriptar o token protegido do convite. Em múltiplas instâncias do BFF, o ticket store também precisa de cache distribuído compartilhado; o desenvolvimento local ainda usa cache em memória.
