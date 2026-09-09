# Status de execução

## Etapa atual

S00 — Fundação executável: **em validação de banco/navegador**. S01: **iniciada, catálogo de planos entregue**.

## Implementado

- Solução .NET 10 com Domain, Application, Contracts, Infrastructure, Api, Web, Worker, Bootstrap e testes.
- SQL canônico PostgreSQL 18 com advisory lock, versão/checksum, identidade, tenants, perfis, sessões, auditoria mínima, inventário Privacy inicial e RLS nas tabelas com escopo de tenant.
- Bootstrap Development idempotente com segredos aleatórios fora do Git, seed com hash do ASP.NET Identity, recuperação explícita e role de aplicação de menor privilégio.
- API com JWT HS256 curto, issuer/audience/lifetime/algoritmo validados, sessão persistida, security version, bloqueio, logout, ProblemDetails, rate limit, liveness/readiness e OpenAPI restrito a Development.
- Web MVC/BFF navy/branco/gelo com cookie `HttpOnly`, `Secure`, `SameSite=Lax`, antiforgery global, troca inicial e dashboard de dados da API.
- CI com versões fixas, PostgreSQL sintético, restore bloqueado por lock file, build, testes e Gitleaks.
- Documentos de arquitetura, segurança, privacidade, status e backlog.
- Solution clássica `Odca.sln`, perfil multiprojeto e script para API/Web/Worker sem Docker.
- Bootstrap `configure-native` para PostgreSQL 18 local com credencial autorizada e segredo fora do Git.
- Runner incremental que mantém lock durante toda a implantação, rejeita blocos inválidos e não reaplica versões registradas.
- Migração 002 com FK composta de papel/tenant, RLS em permissões de papel e auditoria, agregação administrativa restrita e catálogo versionado Basic/Intermediário/Enterprise.
- API e tela autenticada de planos; limites em bytes e sem preços inventados.
- BFF com ticket/token em cache protegido do servidor e cookie opaco; logout local é concluído mesmo se a revogação remota falhar.

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
| `dotnet restore Odca.sln --locked-mode` | sucesso |
| `dotnet build Odca.sln --configuration Release --no-restore` | sucesso, 0 avisos, 0 erros |
| `npm run build` | sucesso |
| testes de checksum/estrutura do runner/snapshot | 5 aprovados |
| testes de domínio | 6 aprovados |
| `dotnet test Odca.sln --configuration Release --no-build` | 11 aprovados; 2 integrações bloqueadas porque a configuração local ainda aponta para o Docker em `localhost:55432` |
| detecção do PostgreSQL nativo | serviço PostgreSQL 18 ativo em `localhost:5432`; credencial administrativa não foi presumida |
| smoke Web `GET https://localhost:7144/entrar` | HTTP 200; página e antiforgery renderizados |
| smoke Web `GET https://localhost:7144/entrar` | HTTP 200; página e antiforgery renderizados |

## Pendências que impedem declarar S00 concluída

- Configurar uma credencial administrativa autorizada com `configure-native`, executar `001 → 002` e reaplicar.
- Rodar o teste integrado API/login/dashboard/planos/logout contra esse banco.
- Revisar visualmente Web em 360, 768 e 1440 px após a API estar disponível.
- Validar o workflow no GitHub; nenhum push foi feito nesta execução.

O PostgreSQL 18 nativo encontrado exige SCRAM e não havia credencial administrativa autorizada disponível nesta execução. Nenhuma senha, `pg_hba.conf` ou autenticação existente foi alterada; nenhum resultado de integração foi inventado. S01 ainda não inclui cadastro de organizações, equipe, seleção de tenant ou Central de Privacidade; S02/S03 não foram iniciadas.
