# Análise do que foi feito e próxima evolução

Repositório: https://github.com/devmnsoft/odcasolutions  
Data: 17/09/2026  
Pacote: `evolucao-operacional-templates.zip`

---

## 1. O que já foi feito

| Entrega | Onde | Status |
|---|---|---|
| CA1848 LoggerMessage no ContractImportsController | PR #61 | mergeado em `codex/s00-foundation` |
| Biblioteca oficial do Estúdio (NDA uni/recíproco, serviços, aditivo) + filtros tipo/escopo | PR #62 | aberto |
| Superadmin local via script; hash só no banco | PR #63 | aberto |
| Regras de inbox (`OperationalInbox`) e janela mensal (`MonthlyAgendaWindow`) | PR #63 / domínio | sem HTTP |
| Lockout 5 falhas / 15 min | domínio | persistência já em `odca.users` |
| `ValidateProposal` com `today` injetável | domínio | |
| Allowlist de minutas `nda/services/amendment/supply/lease` | Application | supply/lease no snapshot local |
| Login consulta `odca.users.password_hash` | Auth | não mudar |

Development: `admin@odca.local` / `OdcaAdmin#2026Local` via `scripts/provision-local-superadmin.ps1`. A API não lê o script.

---

## 2. Qual é a próxima evolução

Não é editor visual, PDF, OCR nem cobrança.

É ligar **quatro eixos** na jornada autenticada:

1. **Funcionalidades** — caixa operacional, agenda do mês, ficha do contrato (HTTP + BFF).
2. **Regras de negócio** — visibilidade próprio vs tenant, janela inclusiva, aditivo quando a vigência muda ou entra nos 3 meses, revisão aberta bloqueia nova minuta primária.
3. **Contexto** — tenant na URL, `SET LOCAL odca.tenant_id` na mesma transação, fuso de `tenants.timezone`, bearer só no BFF, antiforgery nos POSTs.
4. **Template** — `ContractWorkspacePolicy` recomenda a minuta oficial certa na ficha; instalar biblioteca idempotente (`POST .../studio/templates/official`); textos continuam ponto de partida, não parecer jurídico.

---

## 3. Prompt de execução (copiar)

Repositório https://github.com/devmnsoft/odcasolutions, base `codex/s00-foundation` após merge #62 e #63.  
Branch `feat/contract-sheet-agenda-inbox`.  
Copiar os arquivos deste pacote para os mesmos caminhos. Ver INSTALL.md.

Implemente:

1. Inbox `GET /api/v1/organizations/{tenantId}/inbox` usando `OperationalInbox.Rank`. Sem tabela `odca.tasks`.
2. Agenda `GET .../agenda?year=&month=` só com `MonthlyAgendaWindow.ForMonth`. `from > to` → 400.
3. Ficha `GET .../contracts/{contractId}/sheet` com documentos, revisão, obrigações, renovação e **minutas recomendadas**.
4. BFF `/organizacoes/{tenantId}/caixa|agenda|contratos/{id}`. Token no servidor.
5. `ContractWorkspacePolicy.Recommend` na ficha. POST `biblioteca-oficial` com antiforgery chama o endpoint oficial já existente.
6. Testes em `OperationalInboxServiceTests` e `ContractWorkspacePolicyTests`.

Regras que não podem quebrar:

1. Login no banco; falha genérica.
2. Senha 12+ com maiúscula, minúscula, dígito e símbolo.
3. Produção: `must_change_password` + MFA de superadmin se configurado.
4. `--allow-immediate-login` só em Development.
5. Sem senha em `database/odca.sql`, controllers ou views.
6. RLS + `SET LOCAL` na mesma transação. Sem BYPASSRLS.
7. Antiforgery no BFF. Bearer fora do JavaScript.
8. Intenção de renovação ≠ extensão de vigência. Janela de 3 meses exige aditivo.
9. Revisão aberta não sugere NDA/serviços/fornecimento/locação.
10. Sem pragma CA1848/CA1859.

Fora de escopo: editor visual, PDF, OCR, série “esta e futuras”, dashboard consolidado.

Gates: restore `--locked-mode`, build Release, testes. Postgres 18 + RLS A×B quando existir. QA 360/768/1280/1440 só com host no ar.

---

## 4. Código deste pacote

```
src/Odca.Contracts/Operations/OperationalInboxContracts.cs
src/Odca.Application/Operations/OperationalInboxService.cs
src/Odca.Application/Operations/OperationalInboxModels.cs
src/Odca.Application/Operations/MonthlyAgendaService.cs
src/Odca.Application/Operations/ContractSheetService.cs
src/Odca.Application/Operations/ContractWorkspacePolicy.cs
src/Odca.Infrastructure/Operations/OperationalInboxRepository.cs
src/Odca.Infrastructure/Operations/ContractSheetRepository.cs
src/Odca.Infrastructure/Operations/OperationsServiceCollectionExtensions.cs
src/Odca.Api/Controllers/OperationalInboxController.cs
src/Odca.Web/Controllers/InboxController.cs
src/Odca.Web/Controllers/AgendaController.cs
src/Odca.Web/Controllers/ContractsController.cs
src/Odca.Web/Models/OperationalWorkspaceViewModels.cs
src/Odca.Web/Services/OdcaApiClient.Operations.cs
src/Odca.Web/Services/OdcaApiClient.Operations.fragment.cs
src/Odca.Web/Views/Inbox/Index.cshtml
src/Odca.Web/Views/Agenda/Index.cshtml
src/Odca.Web/Views/Contracts/Sheet.cshtml
tests/Odca.Domain.Tests/OperationalInboxServiceTests.cs
tests/Odca.Domain.Tests/ContractWorkspacePolicyTests.cs
```

Classes de domínio que já estão no repo e **não** devem ser copiadas por cima: `OperationalInbox`, `MonthlyAgendaWindow`, `OfficialContractTemplates`, `RenewalRules`, `ContractObligation`, `ContractReview`, `Pagination`.
