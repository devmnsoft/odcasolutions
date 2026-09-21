# Acesso local de desenvolvimento

A autenticacao sempre consulta odca.users (email_normalized/login_normalized + password_hash). Nao existe senha em Odca.Api ou Odca.Web.

Development:
- Superadministrador: admin@odca.local / V7!qM2#rL9@xT4$p (plataforma + membership no tenant demo)
- Operador da organização: operador@odca.local / OdcaOperador#2026Local (permissões operacionais completas)
- Cliente: cliente.teste@odca.local / K8@wR3!nF6#zP2$m (perfil de cliente do tenant demo, com os módulos tenant implementados)

Landing:
- Operador autenticado: /organizacoes/{tenantId}/caixa
- Cliente autenticado: /cliente (CustomerHome)

Provisionar: scripts/provision-local-superadmin.ps1 (ou .sh). O Bootstrap gera o hash ASP.NET Identity e o SQL database/development/seed-test-access.sql grava somente o hash. O comando canônico `provision-test-access` corrige hashes locais divergentes das duas identidades reservadas de forma transacional; `show-login` termina com erro se uma credencial obrigatória não conferir com o banco.

Entrar em https://localhost:7144/entrar. --allow-immediate-login desobriga must_change_password somente em Development. MFA de superadmin continua se Security:MfaRequiredForSuperAdmin=true.

## Sequência canônica (PostgreSQL 18 local)

```powershell
Set-Location C:\MNSOFT\odcasolutions
.\scripts\setup-local.ps1
dotnet run --project src/Odca.Bootstrap -- migrate
dotnet run --project src/Odca.Bootstrap -- provision-test-access --environment Development --allow-postgres-development --rotate-passwords
dotnet run --project src/Odca.Bootstrap -- show-login
.\scripts\run-local.ps1
```

O provisionador é o caminho preferencial: ele gera hashes com o mesmo
`AspNetPasswordService` usado no login, valida-os antes do commit e mantém a operação
transacional. O seed também é executável diretamente pelo `psql` **depois** das
migrations. Ele contém somente hashes ASP.NET Identity v3 PBKDF2 previamente gerados,
restaura as duas credenciais documentadas, é idempotente e recusa servidor não local
ou banco com nome diferente de `postgres`/`odca`:

```powershell
& "C:\Program Files\PostgreSQL\18\pgAdmin 4\runtime\psql.exe" `
  --host "localhost" --port "5432" --username "postgres" --dbname "postgres" `
  --file "C:\MNSOFT\odcasolutions\database\development\seed-test-access.sql"
```

Se esse executável não existir, use o `psql.exe` da instalação PostgreSQL disponível
(por exemplo `C:\Program Files\PostgreSQL\18\bin\psql.exe`) ou um `psql` presente no
`PATH`. Não execute o arquivo em produção nem o inclua em migrations.
