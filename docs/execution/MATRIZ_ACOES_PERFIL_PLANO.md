# Matriz ações × perfil × plano (Seção D — D.4)

Fonte da verdade: comportamento **medido** pelas suítes `scripts/qa/validate-c-web.ps1` e
`scripts/qa/validate-d-web.ps1` sobre os seeds de desenvolvimento (`database/development/seed-test-access.sql`)
— não teoria de SPEC. Os códigos de permissão abaixo foram extraídos do banco
(`odca.roles` × `odca.role_permissions`, tenant demo `20000000-…-000000000001`), que é exatamente o parâmetro
das fixtures `Val D:` usadas na homologação (os papéis das fixtures copiam as permissões do tenant demo).

Legenda: ✓ permitido · ✗ bloqueado pela API (**403** `permission_*` ou checagem de rota) · 🧾 = asserção que prova a linha.

## Eixo 1 — Perfis

Perfis em prova: `tenant-administrator` e `tenant-client` (ambos exercidos por `cliente.teste@odca.local`,
que é administrador também na ficha da organização), `tenant-operator` (`operador@odca.local`) e o ator de
plataforma **SuperAdministrator** (`operador@odca.local` promovido com sessão nível-MFA, padrão da Seção C).

> Medido no seed MVP: `tenant-administrator` e `tenant-client` possuem **o mesmo conjunto** de códigos
> `tenant.%`. A diferença entre eles não vem de códigos e sim das checagens de rota/escopo no servidor
> (p.ex. páginas `/administracao/*` exigem claim de plataforma, não papel de tenant).

| Ação (tela · rota) | admin · client | operator | plataforma | Prova |
| --- | --- | --- | --- | --- |
| Login + menu (só itens permitidos) | ✓ | ✓ | ✓ | D01 · C-S12 |
| Área `/administracao/*` (plataforma) | ✗ acesso-negado | ✗ | ✓ | D01 · C-S13 |
| Wizard → Studio: criar/editar minuta | ✓ `tenant.contract_drafts.manage` | ✓ (mesmo código) | — | D02 · D07 |
| Salvar minuta com valor obsoleto | ✗ 409 conflito | ✗ 409 | — | D10 |
| Gerar versão final / PDF | ✓ | ✓ | — | D05 · D07 |
| Modelo cirúrgico sem aprovação ODCA | ✗ blocker `approval.odca.required` (revolta: `approval.odca.rejected`) | ✗ | decisão exclusiva | D07 · D09 |
| Decidir aprovação de modelo (`/platform/template-approvals/*/decision`) | ✗ 403 | ✓ (promovido) | ✓ nota ≥ 5 caracteres | D08 · C-S16 |
| Preparativo de assinatura (readiness/confirm/reindiciar) | ✓ | ✓ | — | D08 · D11 |
| Central de renovações — ler listas | ✓ | ✓ `tenant.renewals.read` | — | D16 |
| Renovações — preparar proposta | ✓ | ✓ `tenant.renewals.prepare` | — | D16 |
| Renovações — submeter/formalizar/aplicar | ✓ `…submit/formalize/apply` | ✗ 403 (sem os códigos) | — | D13 · D16 |
| Lembretes 45/20/5 (config) | ✓ `tenant.organization.manage` | ✗ 403 | — | D15 · D16 |
| Solicitações — abrir/falar (tenant) | ✓ (com plano Enterprise) | ✓¹ | — | D17 |
| Solicitações — triar/pausar/resolver (fila ODCA) | ✗ 403 `permission_odca` | ✓ (promovido) | ✓ | C-S10 · D17 |
| Isolamento entre organizações (rota cruzada) | ✗ 404 | ✗ 404 | n/a (atua multi-tenant) | D19 |

¹ O papel `tenant-operator` do seed tem `tenant.solicitations.manage/read`; a **decisão** de fila continua
restrita ao ator de plataforma (403 `permission_odca` para tokens de tenant — C-S10).

Códigos medidos do `tenant-operator` (subconjunto): `contract_drafts.*`, `documents.download`, `imports.*`,
`obligations.{assign,cancel,fulfill,manage,read,read_all,reopen}`, `renewals.{prepare,read}`,
`reviews.{decide,read,request}`, `saved_views.manage`, `solicitations.*`, `templates.*`.
Ausências relevantes: `contracts.*`, `renewals.{submit,formalize,apply,decide,cancel,register}`,
`organization.manage`, `billing.*`, `team.*`, `extractions.*`, `privacy.*`, `contract_copies.issue`.

## Eixo 2 — Planos

| Ação / recurso | BASIC | ENTERPRISE | Comportamento medido | Prova |
| --- | --- | --- | --- | --- |
| Studio, modelos oficiais, capa/sumário, PDF | ✓ | ✓ | instalação `templates/official` independe de plano | D02 |
| Overlay terapias (cláusula 2ª-A, sessões/mensalidades) | ✓ | ✓ | gate é `activity_profile=therapy_clinic`, **não plano** | D02 |
| Perfil `plastic_surgery` na organização | ✗ | ✓ | 400 "O perfil Cirurgião plástico exige plano Enterprise ativo" | C · OrganizationsController |
| Minuta do modelo cirúrgico | ✗² | ✓ | 409 `draft.profile.surgical_required` fora do perfil | D06 |
| Aprovação ODCA do modelo cirúrgico | — | ✓ | exigida independentemente do plano, antes do preparativo | D07–D09 |
| Central de Solicitações (abrir) | ✗ | ✓ | 409 enquanto o tenant está em BASIC | C-S1 · D17 |
| Limites de upload/armazenamento de documentos | limite menor | limite maior | `plan_entitlements.file_bytes/storage_bytes` por plano | ContractDocumentsController |
| Lembretes de renovação configuráveis | ✓ | ✓ | gated por permissão, não por plano | D15 |

² Indireto: o modelo cirúrgico é gateado pelo perfil de atuação, que por sua vez exige Enterprise ativo.

## Notas de homologação

- A prova de cada linha é automatizada (API + banco) na suíte D; a trilha de UI correspondente está nas
  capturas `%TEMP%\opencode\evidencias-d\` (fora do repositório, convém à convenção do projeto).
- A suíte roda contra fixtures `Val D:` (org1 terapia/BASIC, org2 cirurgia/Enterprise) criadas e removidas
  pela própria suíte; o estado dos seeds é restaurado no cleanup (D99).
