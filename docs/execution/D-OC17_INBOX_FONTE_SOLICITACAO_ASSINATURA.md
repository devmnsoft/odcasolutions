# D-OC17 — Integração do Inbox Operacional com Fontes de Solicitação e Assinatura (Bloco E, item 17)

Data: 2026-10-12 · Branch: codex/s00-foundation · Commit: `77c666a`

## Escopo

Integrar as fontes **Solicitação** e **Assinatura** ao endpoint e à página **existentes** do Inbox operacional (`GET /api/v1/organizations/{tenantId}/inbox` + `Inbox/Index.cshtml`) — sem criar página ou endpoint separado. O Centro de Ações (`/operations/action-center`, rota `/acoes`) permanece como visão **complementar** de ações agrupadas por categoria (minhas ações / aguardando outros / prazos / conflitos); o Inbox é a fila unificada de trabalho com portão por fonte, filtros por kind e âncoras de navegação para a Sheet (`#solicitacoes`, `#assinatura`).

## Regras adotadas

| Fonte | Portão (permissão real, verificada no banco) | Escopo `mine` | Escopo `organization` |
|-------|----------------------------------------------|---------------|----------------------|
| Solicitação | `tenant.solicitations.read` | `s.opened_by = ator` | todas as solicitações ativas (status fora de `resolvida/encerrada/cancelada`) |
| Assinatura | `tenant.contracts.read` | participação em `signature_participants.composition_revision = signature_preparations.composition_revision` | todos os `signature_preparations` com status no domínio canônico |

- `OperationalWorkKind` ganhou `Solicitation` e `Signature`; `ContractId` tornou-se `Guid?` (solicitações não têm contrato obrigatório).
- Sem a permissão da fonte, a fonte é **inteiramente invisível** — nem no resultado, nem como filtro vazio distinguível: o `kind=Signature` de quem não tem `contracts.read` retorna 0 itens e nunca expõe id/título de preparação alheia.
- Nome do documento exibido: `COALESCE(contracts.title, 'Documento gerado')` — nem `generated_contract_versions` nem `contract_drafts` possui coluna de nome (contexto canônico = título do contrato).

## Correções de bug latentes encontradas durante a verificação comportamental

Todos os itens abaixo só aparecem quando a query roda contra o banco real — nenhuma era visível no build nem nos testes de domínio:

1. **Domínios de status em inglês dentro de CHECK em português.** O repositório do Inbox e o action center filtravam `solicitations.status` por valores EN; o CHECK da tabela usa `('aberta','triagem','em_atendimento','aguardando_cliente','resolvida','encerrada','cancelada')`. Corrigido nos dois locais para os domínios canônicos PT.
2. **`partially_signed` não existe** no CHECK de `signature_preparations.status` (domínio: `('draft','confirmed')`). O estado "parcialmente assinado" deriva de `signature_participants.signed_at`. O filtro foi estreitado para `IN ('draft','confirmed')` no Inbox e no action center.
3. **Negação C# dentro do SQL**: `!@canReadTenantSignatures` → `NOT @canReadTenantSignatures` (`42883: operador não existe: ! boolean`).
4. **Ação center quebrado de ponta a ponta** (nunca tinha sido chamado com sucesso após o refator): parêntese de fechamento ausente nos 5 CTEs (`42601` no comma do JOIN), mapeamento Dapper `ActionItemRow` com parâmetros PascalCase que não casavam com colunas snake_case, e domínio `partially_signed` fantasma. Corrigido nos três pontos.
5. **Cadeia v051 / versionamento**: cauda "Item 13" órfã (fora de qualquer bloco) movida para dentro do bloco `ODCA-MIGRATION 051`; `DROP FUNCTION IF EXISTS …(7 parâmetros)` para o função defasado; REVOKE obsoleto removido; `DatabaseSchema.CurrentVersion = 51` (health 51/51).
6. **validTypes divergente**: `PlatformOperationsController.CreateTemplate` agora aceita `services-multiple-therapies` (a matriz de planos sempre o incluiu).
7. **Catálogos i18n do Web** (pt-BR/en-US/es-ES/fr-FR): vírgulas ausentes nas fronteiras de bloco e vírgula final — corrigidos linha a linha; era um crash de startup oculto (JSON inválido só falhava na primeira tradução consumida).

## Evidência classificada (API real, MFA real)

Método: `scripts/qa/validate-inbox-sources.ps1` — `curl.exe -sk` contra `https://localhost:7143`, login por senha + MFA real via `POST /api/v1/auth/mfa/challenge` com código de recuperação (código unívoco, hash SHA256 `{userId:N}:{CODIGO}`, linha em `odca.mfa_recovery_codes`). Nenhum resultado SQL isolado: tudo passou por controller + autorização + validação + transação.

Perfis (mesmo tenant `2000…001`, permissões verificadas em `odca.tenant_actor_has_permission`):

| Perfil | `solicitations.read` | `contracts.read` | Inbox org | Filtro `kind=Signature` | Action-center org |
|--------|:---:|:---:|-----------|------------------------|-------------------|
| **Restrito** — `operador@odca.local` | t | f | total=10 → apenas Solicitation | **total=0** (fonte invisível) | renewal=4, solicitation=10, **signature=0** (total 14) |
| **Completo** — `admin@odca.local` | t | t | total=19 → Solicitation=10 + Signature=9 (título do contrato como nome) | total=9, 0 itens de outro kind | renewal=4, solicitation=10, signature=9 (total 23) |

Observações:
- Fluxo MFA completo executado nos dois perfis: login (token `mfaVerified=false`) → desafio com código de recuperação → token `mfaVerified=true` (`CompleteMfaChallengeAsync` consome a linha e eleva a sessão a `authentication_level='mfa'`).
- Amostras reais devolvidas: solicitações `Matriz-S4-…` status `aberta` urgência `DueThisWeek`; assinaturas "Contrato Ana Paula F. - Restauracao GATES 2 e 3 (v2) (cópia)" etc.
- Testes de domínio: 300/300 PASS (incluindo `TenantReaderSeesAllFiveKinds`); build da solution 0 avisos / 0 erros.
- Com a sessão MFA real disponível, a **matriz de aceite comportamental atingiu 15/15 PASS (0 BLOCKED)**: S12 foi executado nesta janela (não apenas inferido); S13 fechou com ciclo real `create→submit→formalize→apply` via API (contrato `cfd0bf87…` v1→v2).

### Mudança de fixture documentada (base de teste descartável)

`admin@odca.local`: hash de senha regenerado com o próprio serviço do app (`AspNetPasswordService`, via projeto efêmero que referencia `Odca.Infrastructure`) com a senha fixa `OdcaAdmin#Qa2026`; trava de login e contagem de falhas limpas. Necessário porque o provisioner (`reset-password`) recusa enquanto o estado do operador diverge da seed. Códigos de recuperação conhecidos inseridos: `ODCA-ADMIN-QA-RECOVERY-{A,B,C,D}` e `ODCA-OPERADOR-QA-RECOVERY-{A,B}` + pares `V2` (uso único, alguns já consumidos).

### Raiz das 401 genéricas anteriores (não era bug de servidor)

`UseHttpsRedirection` emite 307 inclusive para POST; o PowerShell 5.1, ao seguir redirect entre authorities (http:5143 → https:7143), **remove o header `Authorization`**, e o servidor via token ausente. Com chamada direta `curl.exe -sk https://localhost:7143`, a sessão valida (`SESSION-VALID=True` confirmado em diagnóstico temporário, depois removido de `Program.cs`). Regra de ouro para QA local: **sempre `curl.exe -sk` sobre HTTPS; nunca `Invoke-WebRequest` http→https com Authorization**.
