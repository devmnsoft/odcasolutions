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
