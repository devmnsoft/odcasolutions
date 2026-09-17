# Acesso local do superadministrador

A autenticacao sempre consulta odca.users (email_normalized/login_normalized + password_hash). Nao existe senha em Odca.Api ou Odca.Web.

Development:
- Superadministrador: admin@odca.local / OdcaAdmin#2026Local
- Cliente: cliente.teste@odca.local / OdcaCliente#2026Local

Provisionar: scripts/provision-local-superadmin.ps1 (ou .sh). O Bootstrap gera o hash e o SQL database/development/seed-test-access.sql grava so o hash. show-login so imprime a senha se ela conferir com o banco.

Entrar em https://localhost:7144/entrar. --allow-immediate-login desobriga must_change_password somente em Development. MFA de superadmin continua se Security:MfaRequiredForSuperAdmin=true.
