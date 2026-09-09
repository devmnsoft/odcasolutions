# Backlog

## Fechar S00

1. Fornecer ao `configure-native` uma conexão administrativa autorizada para o PostgreSQL 18 já ativo em `localhost:5432`.
2. Executar upgrade 001→002, reaplicação e `dotnet test` completo.
3. Confirmar role `odca_app_login` sem `SUPERUSER`, `CREATEDB`, `CREATEROLE` ou `BYPASSRLS`.
4. Fazer QA visual responsivo e atualizar `STATUS.md`/`RELEASE_EVIDENCE.md`.
5. Validar CI após push autorizado.

## Próxima fatia: S01 — SaaS e acessos

- [x] Catálogo versionado Basic/Intermediário/Enterprise sem preços inventados, com API e tela de leitura.
- Cadastro CPF/CNPJ, responsável, termos/aviso separados e confirmação por caixa local.
- Memberships, convites, perfis padrão/personalizados e seleção pós-login.
- Tenant context transacional, FKs compostas e testes negativos de RLS/pool A × B.
- CRUD e inativação/restauração auditados; proteção do último administrador.
- Canal público de direitos, avisos/preferências e fila que revalida a preferência.
- Sessão temporária de suporte com MFA, escopo, expiração, revogação e ator real.

Dependências externas: validação jurídica dos textos e prazos; contatos de privacidade reais; provedor de e-mail de produção. O desenvolvimento usa somente dados sintéticos e outbox local identificada.
