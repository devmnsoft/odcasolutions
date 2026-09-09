# Status de execução

## Etapa atual

S00 — Fundação executável: **em validação**.

## Implementado

- Solução .NET 10 com Domain, Application, Contracts, Infrastructure, Api, Web, Worker, Bootstrap e testes.
- SQL canônico PostgreSQL 18 com advisory lock, versão/checksum, identidade, tenants, perfis, sessões, auditoria mínima, inventário Privacy inicial e RLS nas tabelas com escopo de tenant.
- Bootstrap Development idempotente com segredos aleatórios fora do Git, seed com hash do ASP.NET Identity, recuperação explícita e role de aplicação de menor privilégio.
- API com JWT HS256 curto, issuer/audience/lifetime/algoritmo validados, sessão persistida, security version, bloqueio, logout, ProblemDetails, rate limit, liveness/readiness e OpenAPI restrito a Development.
- Web MVC/BFF navy/branco/gelo com cookie `HttpOnly`, `Secure`, `SameSite=Lax`, antiforgery global, troca inicial e dashboard de dados da API.
- CI com versões fixas, PostgreSQL sintético, restore bloqueado por lock file, build, testes e Gitleaks.
- Documentos de arquitetura, segurança, privacidade, status e backlog.

## Verificações executadas

| Comando | Resultado |
|---|---|
| `dotnet --info` | SDK 10.0.400; runtime 10.0.11 |
| `node --version` | 24.18.0 |
| `docker --version` | cliente 29.4.3 |
| `psql --version` | indisponível; não é requisito do runner |
| `dotnet build Odca.slnx --configuration Release` | sucesso, 0 avisos, 0 erros |
| `dotnet run --project src/Odca.Bootstrap -- init` | sucesso; arquivos locais preservados fora do Git |
| `docker compose --env-file .env.local up -d --wait` | bloqueado: daemon Docker Desktop não respondeu |

## Pendências que impedem declarar S00 concluída

- Executar `database/odca.sql` em PostgreSQL 18 novo e reaplicar.
- Rodar o teste integrado API/login/dashboard/logout contra esse banco.
- Revisar visualmente Web em 360, 768 e 1440 px após a API estar disponível.
- Validar o workflow no GitHub; nenhum push foi feito nesta execução.

O PostgreSQL 18 nativo encontrado exige SCRAM e não havia credencial local disponível. Nenhum resultado de integração foi inventado.
