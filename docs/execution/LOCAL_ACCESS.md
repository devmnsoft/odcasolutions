# Acesso local do superadministrador

## Regra de negócio

A autenticação **sempre** consulta `odca.users` por `email_normalized` ou `login_normalized` e compara a senha informada com `password_hash` (ASP.NET Identity). Não existe usuário ou senha embutida em `Odca.Api` ou `Odca.Web`.

Identidades reservadas de Development:

| Perfil | Login | Senha do script | Onde vive o segredo |
|---|---|---|---|
| Superadministrador | `admin@odca.local` | `OdcaAdmin#2026Local` | script local / `local-access.env` / `%LOCALAPPDATA%\ODCA Solutions\development-credentials.json` |
| Cliente de demonstração | `cliente.teste@odca.local` | `OdcaCliente#2026Local` | idem |

O SQL `database/development/seed-test-access.sql` recebe somente o **hash**. Texto puro nunca entra no instalador canônico `database/odca.sql`.

## Provisionar

```powershell
.\scripts\setup-local.ps1
dotnet run --project src/Odca.Bootstrap -- migrate
.\scripts\provision-local-superadmin.ps1
```

O script chama `provision-test-access --administrator-password --client-password --allow-immediate-login`, grava o hash, relê a linha e só imprime a senha se ela conferir com o banco (`show-login`).

## Entrar

1. `https://localhost:7144/entrar`
2. Login `admin@odca.local` / senha `OdcaAdmin#2026Local`
3. Se `Security:MfaRequiredForSuperAdmin` estiver `true`, conclua a inscrição TOTP. Sem isso o shell administrativo permanece restrito.

`--allow-immediate-login` desobriga `must_change_password` somente neste Development e somente quando o operador passa o flag. Produção continua com troca inicial obrigatória.

## Fora de escopo

Não use estas senhas em produção, CI pública ou dump compartilhado. Rotacione com `--rotate-passwords` se o hash local divergir.
