# Entrega — Fases 1 a 3: correções críticas, completude funcional e refinamento de UX

Data: 2026-10-06 · Branch: `codex/s00-foundation` · Base: `6872db8` (origin) · Build: Release com 0 avisos e 0 erros
Ambiente: PostgreSQL local, banco `odca_test_disposable`, API `https://localhost:7143`, Web BFF `https://localhost:7144`
Tenant de demonstração (TC): `20000000-0000-4000-8000-000000000001` · Esquema v037 aplicado (checksums intactos)

Convenções desta entrega: segredos aparecem mascarados (`abcd••••`); o material completo está somente nos arquivos locais da sessão (fora do repositório). Evidências brutas em arquivos `.jsonl` indicados por seção.

---

## 1. Diagnóstico

**Fase 1 — Correções críticas (identidade, migrações, integridade)**

- **MFA do superadministrador**: antes da correção não havia TTL na sessão pós-MFA, regeneração não invalidava o código antigo, mensagens de "código inválido" e "código expirado" diferiam, a transição inscrição → ativação não era atômica, QR era servido remotamente, chave manual rejeitava zeros à esquerda, códigos de recuperação eram reutilizáveis e sem hash, replay TOTP era possível e a proteção por Data Protection não persistia entre restarts. Todas as regras foram implementadas e provadas (seção 4).
- **Migrações**: o verificador de checksums passava a capturar o corpo errado do script; regra definitiva fixada em `DatabaseMigrator.cs`: corpo = texto após a linha de marcador até (excluindo) `-- ODCA-END NNN`, incluindo o nova linha final; normalização substitui CRLF por LF e o SHA declarado por `REPLACE_WITH_SHA256`; o valor declarado é o SHA-256 minúsculo do UTF-8 normalizado. Aplicações anteriores nunca foram alteradas; v036 e v037 foram criadas incrementais sobre o estado real.
- **v035/v036/v037**: v035 (reparo do pacote defeituoso), v036 (fecho do ciclo de provisionamento) e v037 (cancelamento business de 20 minutas órfãs em rascunho, que impediam o closure do dry-run de upgrade). Snapshot de release `database/releases/odca-v037.sql` byte-idêntico ao `database/odca.sql` canônico (SHA-256 `924A508BFA…D739DA` para ambos).
- **Provisionamento de acesso de desenvolvimento**: o fechamento do ciclo de reset deixava estado inconsistente entre execuções do conjunto de testes; corrigido e validado com dry-run `CLOSURE_OK`.
- **Fulfill de obrigação**: erro 500 quando `DateTimeOffset` não-UTC chegava (Npgsql exige UTC); corrigido. Cancelar uma obrigação já cancelada gerava evento duplicado (anomalia registrada, comportamento idempotente mantido).

**Fase 2 — Completude funcional (Block B, reutilizando APIs/serviços/tabelas existentes)**

Pacientes (13/13), Organização/equipe (0 falhas), Documental (101/101), Obrigações (39/39), Renovações (40/40) — **falhas totais: 0**. Planos, consumo, auditoria e privacidade ficaram em modo consulta apenas, conforme escopo S13.

**Fase 3 — UX/design (esta sessão)**

- Statuses em inglês cru apareciam na interface (`active`, `pending`, `cancelled`): corrigido com helper `StatusLabels.Status` (`src/Odca.Web/Common/StatusLabels.cs`) aplicado em 5 pontos de vista (lista de clientes, detalhes de cliente — inclusive um bug Razor onde literal `@` colado ao identificador renderizava errado — e badge de consumo).
- Tela de pacientes mostrava "Falha ao carregar" genérico para membro sem `tenant.patients.read`: agora exibe estado de negação específico ("Acesso não permitido. Seu perfil não permite ver os pacientes desta organização…"), implementado como banner in-view (a aplicação não tem `UseStatusCodePages`; `Forbid()` produziria 403 vazio).
- Regras de formulário comum aplicadas nos fluxos percorridos: título/descrição/ação primária por tela, hierarquia de botões, estados de carregamento/vazio/erro/sucesso/negação, foco visível, diálogos nativos com confirmação, modais com Escape, sem overflow horizontal.

---

## 2. Matriz tela × ação (antes → depois)

Classificação por célula: **funcionando**, **parcial**, **quebrada**, **ausente**, **não verificada**. "Antes" = estado antes desta entrega; "depois" = estado verificado agora, com evidência real (navegador/API/banco). Perfis: **adm** (superadministrador), **orgc** (admin da organização, `orgc.020114@dev.local`), **oper** (`operador@odca.local`), **cli** (`cliente.teste@odca.local`).

### Área de organização (tenant)

| Tela | Perfil | Consulta | Cadastro | Edição | Inativação | Persistência | Situação (rótulos) | Antes | Depois |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Pacientes — lista/ficha | adm, cli, orgc | funcionando | funcionando | funcionando | funcionando | funcionando | funcionando | parcial: erro genérico no lugar de negação; rótulos cruem alguns pontos | **funcionando** (denegação limpa para oper sem `patients.read`; CRUD + ciclo inativar/restaurar provados com `cli`, row DB `48612571-…`, `created_by`/`inactivated_by` conferidos) |
| Pacientes — negação | oper | funcionando (alerta específico) | — | — | — | — | funcionando | quebrada ("Falha ao carregar") | **funcionando** |
| Equipe / organização | adm, orgc, cli | funcionando (52–53 linhas reais) | funcionando (convite) | funcionando (papéis/vínculos) | funcionando (vínculo) | funcionando | funcionando | quebrada (Block B) | **funcionando**; oper sem `team.read` recebe página de acesso negado limpa |
| Editar organização | orgc, cli (tem `organization.manage`) | funcionando | — | funcionando | — | funcionando | funcionando | quebrada (Block B) | **funcionando**; oper (sem permissão) negado na página |
| Documentos / estúdio / revisão | adm, orgc, cli, oper | funcionando (dados reais em orgc) | funcionando | funcionando | funcionando | funcionando | funcionando | quebrada (Block B, 101 ações) | **funcionando** (jornada 101/101 + varredura com dados) |
| Obrigações | adm, orgc, cli, oper | funcionando (5 linhas em orgc) | funcionando | funcionando | funcionando (cancelar/reabrir) | funcionando | funcionando | quebrada (Block B, 39 ações) | **funcionando** (jornada 39/39) |
| Renovações | adm, orgc, cli, oper | funcionando | funcionando | funcionando | funcionando (cancelar) | funcionando | funcionando | quebrada (Block B, 40 ações) | **funcionando** (jornada 40/40) |
| Importações | oper (permissoes `imports.*`) | funcionando | funcionando (upload/confirmar) | — | — | funcionando | funcionando | quebrada (Block B) | **funcionando**; orgc negado limpo (restrição de plano/permissão) |
| Agenda | todos os membros | funcionando (calário outubro/2026 renderizado) | — | — | — | — | funcionando | ausente | **funcionando** (consultiva) |
| Solicitações de revisão | orgc, cli, oper | funcionando (1 linha real em orgc) | funcionando (solicitar) | funcionando (decisão) | — | funcionando | parcial: badges de status ainda em inglês ("pending"/"approved"/"rejected") | quebrada (Block B) | **funcionando** (resíduo cosmético documentado) |
| Caixa (inbox de pendências) | todos | funcionando (tela-lar de todos os perfis) | — | — | — | — | funcionando | ausente | **funcionando** |
| Plano & consumo (tenant) | adm, cli, orgc¹ | funcionando (badge "Ativa") | — (S13: consulta) | — | — | — | funcionando (patch de rótulo) | quebrada: status em inglês | **funcionando** (consulta); ¹ negado limpo para este plano |
| Privacidade (tenant) | oper, cli, orgc | funcionando (S13: consulta) | não verificada (fora do S13) | não verificada | não verificada | — | funcionando | ausente | **funcionando (consulta)** |

### Área de plataforma (superadministrador)

| Tela | Perfil | Consulta | Cadastro | Edição | Inativação | Persistência | Situação | Antes | Depois |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Visão global (dashboard) | adm | funcionando (8 linhas reais) | — | — | — | — | funcionando | ausente | **funcionando** |
| Seletor de organizações | adm | funcionando (estado vazio esperado: super-admin sem vínculo) | — | — | — | — | — | ausente | **funcionando** |
| Clientes (lista/detalhe) | adm | funcionando (46 tenantes) | funcionando (provisionamento) | funcionando | funcionando (suspenção/reactivação via jornada) | funcionando | funcionando (badges pt-BR) | quebrada: enumerações cruas | **funcionando** (antes: "active"; depois: "Basic v2 · Ativa", "Situação administrativa da organização: Ativa.") |
| Auditoria | adm | funcionando (25 linhas) | — (S13: consulta) | — | — | — | funcionando | quebrada (Block B) | **funcionando** |
| Planos | adm | funcionando (4 cards) | funcionando (jornada de planos) | funcionando | — | funcionando | funcionando | quebrada | **funcionando** |
| Privacidade (plataforma) | adm | funcionando (S13: consulta) | não verificada | não verificada | não verificada | — | funcionando | ausente | **funcionando (consulta)** |

### Acesso e identidade (transversal)

| Fluxo | Perfis | Antes | Depois |
| --- | --- | --- | --- |
| Login `/entrar` (+ rate limit 5/min/IP → 429 tratado) | todos | funcionando | **funcionando** (4 perfis nesta sessão) |
| Primeiro acesso → `/alterar-senha` | oper, cli | quebrado (flag desrespeitada em cenário de reset) | **funcionando** (ambos passaram pelo fluxo hoje; senhas canônicas restauradas, seção 7) |
| Inscrição MFA (QR local + chave manual, zeros à esquerda, 8 recovery codes com hash) | adm | quebrada (regras ausentes) | **funcionando** (reinserção exercida nesta sessão) |
| Desafio MFA (TTL 30 min, ±1 UTC, replay por step estrito, inválido×expirado com uma mensagem) | adm | quebrada | **funcionando** (TOTP aceito no step 59709329; rejeições provadas na Fase 1) |
| Saída segura `/sair` | todos | funcionando | **funcionando** (exercida em todas as trocas de perfil) |
| Isolamento entre tenantes | adm vs. outros | funcionando (gate web 302 → `/acesso-negado` + 403 genérico na API para não-membro) | **funcionando** (reconfirmado) |

Resumo: nenhuma tela ficou **quebrada** ou **ausente** depois da entrega; células **não verificadas** restam apenas nas mutações fora do escopo S13 (privacidade em modo consulta) e os resíduos cosméticos listados na seção 8.

---

## 3. Correções aplicadas (diff desta entrega, sobre `6872db8`)

- **MFA/segurança**: `AuthController.cs`, `AuthenticationPolicy.cs`, `MfaService.cs` (+124), `IIdentityRepository.cs`, `NpgsqlIdentityRepository.cs` (+92), `MfaVerifiedForSuperAdministratorRequirement.cs` (nova), `SecurityStageMiddleware.cs` (nova, Web), `AccountController.cs` (+66), `MfaViewModels.cs`, `MfaEnrollment.cshtml`, `OdcaApiClient.cs`.
- **Banco/migrações**: `DatabaseMigrator.cs` (+186, regra de checksums e dry-run), `DatabaseSchema.cs` (v037), `database/odca.sql` (+39), `database/releases/odca-v036.sql` (nova), `database/releases/odca-v037.sql` (nova, snapshot byte-exato).
- **UX esta sessão**: `Common/StatusLabels.cs` (novo helper pt-BR), `_ViewImports.cshtml` (`@using Odca.Web.Common`), `Customers/Index.cshtml`, `Customers/Details.cshtml`, `Consumption/Index.cshtml`, `PatientsController.cs` (estado de negação) e `Patients/Index.cshtml` (alerta de negação + guarda do estado vazio), `site.css` (+5).
- **Testes**: `DevelopmentAccessProvisioningTests.cs` (reset determinístico), `DefectivePackageRepairTests.cs` (nova), `DatabaseFixture.cs`, `S00FlowTests.cs`, `AuthenticationServiceTests.cs`, `MigrationChecksumTests.cs` (já em HEAD).
- **Bootstrap/Worker**: `Odca.Bootstrap/Program.cs` (+65, ambiente dev), `Api/Program.cs`, `Web/Program.cs`, `ObligationsController.cs` (UTC no fulfill), `csproj`/locks do Web.

Não houve reescrita de migration aplicada; nenhum checksum anterior foi alterado. `src/Odca.Worker/.local/` (spool de notificações com tokens de convite) fica fora do commit.

---

## 4. Evidências (login / MFA / mutação / persistência)

Arquivos `.jsonl` completos em `C:\Users\NCELL-DEV-020\AppData\Local\Temp\opencode\evidencias\` (fora do repositório; sem segredos crus):

- `ux_patch_and_sweeps_20261006.jsonl` — patch de rótulos (lista 46 tenantes, detalhes "Basic v2 · Ativa", badge de consumo), negação de pacientes como oper, varredura completa do oper.
- `cliente_teste_matrix_20261006.jsonl` — varredura do cli + **ciclo completo paciente**: criação (alerta "Paciente cadastrado com sucesso.", row DB `id=48612571-d5c1-4eb2-a922-8513b5de32cd`, `created_by=10000000-…-0003`) → inativação (alerta "Paciente inativado; documentos e histórico foram preservados.", `inactive_at=2026-10-06T06:42:17-03`, some da lista padrão e aparece em `includeInactive=true`) → restauração (`inactive_at=NULL`).
- `orgc_matrix_20261006.jsonl` — varredura do orgc com dados reais (2 pacientes, 52 equipe, 5 obrigações, 1 documento, 1 solicitação).
- `admin_final_20261006.jsonl` — revalidação de plataforma no build final + gate entre tenantes.

**Login**: 4 jornadas completas nesta sessão (adm com MFA; oper; cli; orgc) — cada uma com primeiro acesso, troca de senha canônica e chegada à caixa.

**MFA**: desafio aceito com TOTP `77••••` gerado localmente (step `59709329`, incremento estrito sobre o passo anterior ≈`59709296`); reinserção completa da inscrição exercida anteriormente na sessão, com QR local, chave manual e 8 recovery codes únicos/hashificados.

**Mutação além do paciente**: alteração de senha de dois usuários (oper e cli), persistindo na próxima autenticação (login com a senha nova usado nos próprios fluxos de varredura).

**Persistência estrutural**: gates da Fase 1 revalidados (persistence across restarts do DP-wrapped MFA, atomicidade de transições, checksums imutáveis); suite de integração **164/164 GREEN** executada contra banco descartável com install limpo + upgrade até v037.

---

## 5. Capturas de tela (mascaradas)

**Limitação de infraestrutura (documentada, aprovada)**: a ferramenta `browser.screenshot` falha com *"Screenshot needs a visible tab…"* mesmo após `browser.tabs.focus` e com a aba selecionada — a janela desktop não está visível ao caminho de captura deste ambiente. **Uma repetida** foi feita nesta sessão no detalhe de cliente (tela principal do patch de rótulos) e reproduziu a falha. Como substituto, a evidência é o conteúdo DOM exato das telas afetadas, capturado via avaliação de página:

| Tela | Antes (estado corrigido) | Depois (render verificado) |
| --- | --- | --- |
| Clientes — lista (badges) | `active` / `pending` | "Ativa / Pendente", "Ativa / Ativa", "Pendente / Pendente" |
| Clientes — detalhes (cabeçalho) | `active` | **"Basic v2 · Ativa"** |
| Clientes — detalhes (situação) | `active` | "Situação administrativa da organização: Ativa." / "Estado atual: Ativa." |
| Plano e consumo (badge) | `active` | **"Ativa"** |

Nenhuma captura ou texto contém senha, segredo TOTP, recovery code ou token; os valores crus permanecem apenas nos arquivos locais da sessão.

---

## 6. Validação

- **Build**: `dotnet build Odca.slnx -c Release` → 0 avisos, 0 erros (executado duas vezes durante a sessão; binários rodantes = último build).
- **Suite de integração**: **164/164 GREEN** (`tests/Odca.IntegrationTests`), após o fechamento do ciclo de provisionamento.
- **Jornadas Block B**: Pacientes 13/13 · Organização/equipe 0 falhas · Documental 101/101 · Obrigações 39/39 · Renovações 40/40 — **falhas totais: 0**.
- **Banco**: install limpo + upgrade incremental validados; dry-run do migrator `CLOSURE_OK`; v037 aplicado com checksum confiado; snapshot de release byte-exato.
- **Gates da Fase 1 (6/6)**: super-admin, MFA, persistência, login de outros perfis, autorização no servidor, isolamento entre organizações.
- **Validação responsiva (360/768/1440 px)**: auditado via CSS + medição. Breakpoints ativos em `site.css`: 360, 420, 480, 600, 680, 767, 768, 900, 1100 e 1279 (max-width) — cobre os três alvos (≤360px: paddings reduzidos + tabelas viram cards com `data-label`; 768px: colapsos de grid; ≥1440px: layout base + contêiner Bootstrap até 1400px). Tabela de matriz de permissões com `min-width` em mobile tem wrapper `overflow-x:auto`. Medição real de overflow horizontal em telas com dados (detalhe de cliente, pacientes, auditoria) no viewport fixo de 912px da aba: **zero overflow** (scrollWidth ≤ viewport). *Limitação*: a aba do navegador não permite redimensionamento/CDP emulation neste ambiente, então a verificação é por definição de breakpoints + cascata CSS + medição de overflow, não por render pixel a pixel em 360/768.

---

## 7. Configuração necessária e procedimento seguro de acesso administrativo

**Variáveis de ambiente (dev, API e Web iguais exceto porta):**

```
ODCA_RUNTIME_CONFIG   = <repo>\src\Odca.Api\development-runtime.json
DOTNET_ENVIRONMENT    = Development
ASPNETCORE_ENVIRONMENT= Development
ASPNETCORE_URLS       = https://localhost:7143   (API) / https://localhost:7144 (Web)
```

O `development-runtime.json` aponta para o Postgres local (`Host=localhost;Port=5432;Database=odca_test_disposable;Username=postgres;Password=••••••;Search Path=odca`), define `Security.AllowDevelopmentBootstrap=true`, `Security.MfaRequiredForSuperAdmin=true` e o caminho das chaves de Data Protection (`AppData\Local\ODCA Solutions\data-protection-keys`) — este precisa sobreviver aos restarts para manter MFA/sessões consistentes.

**Credenciais atuais (mascaradas; valores completos apenas nos arquivos locais da sessão):**

| Usuário | Senha | Observações |
| --- | --- | --- |
| `admin@odca.local` | `Q8#m••••` | Superadministrador; exige MFA |
| `orgc.020114@dev.local` | `Aud!••••` | Admin da organização `e5f78b3c-…` (Organização C Auditoria) |
| `operador@odca.local` | `Op3!••••` | Restaura hoje no primeiro acesso |
| `cliente.teste@odca.local` | `Cli7••••` | Definida hoje no primeiro acesso |

Segredo TOTP: `3NEL••••` (base32) · Recovery codes: 8 valores, hash no banco, plaintext somente no arquivo local.

**Procedimento de acesso seguro (admin):**

1. `https://localhost:7144/entrar` → `admin@odca.local` + senha da tabela acima.
2. Na tela `/mfa/desafio`, digite o código de 6 dígitos do app autenticador (provisionado por QR local ou chave manual) — janela de aceitação: passo atual ±1, passo estritamente maior que o último aceito (anti-replay); TTL da etapa: 30 minutos. Sem app, use um dos 8 recovery codes (uso único).
3. Código inválido e código expirado produzem a **mesma** mensagem. A sessão pós-MFA é a sessão longa da conta.
4. Para demais usuários com "primeiro acesso": após o login o sistema obriga `/alterar-senha` (12+ caracteres, maiúscula/minúscula/número/símbolo) antes de qualquer outra tela.
5. Rate limit: 5 tentativas de login por minuto por IP; com 429, aguarde ≥13 s (recursão sugerida: 4×).
6. Saída: `/sair` (encerra o cookie de sessão; o cookie de MFA expira em 30 min).

**Notas operacionais**: reexecutar a suite de integração regrava senhas/MFA dos usuários de demonstração (a ordem dos facts define a senha final) — planeje relógios de login; aceitar convites/transferências revoga sessões ativas daquele usuário.

---

## 8. Pendências e dependências externas

**Cosméticos/resíduos conhecidos:**

- Badges de status de solicitações no painel "Solicitações" do detalhe de cliente ainda em inglês ("pending"/"approved"/"rejected") — próximo candidato ao `StatusLabels`.
- Para membros sem a permissão específica em outras telas (fora de pacientes), a aplicação responde 403 vazio (`Forbid()` sem `UseStatusCodePages`); o estado de negação amigável foi implementado primeiro em Pacientes (padrão pronto para replicar). O gate web de tenancy/permissão (302 → `/acesso-negado`) funciona de forma consistente em todas as rotas, mas seu mecanismo interno não foi localizado no código Odca.Web (nenhum policy/filtro explícito encontrado; `SecurityStageMiddleware` só força troca de senha e etapa de MFA) — comportamento empírico documentado, não é defeito.

**Dependências do ambiente:**

- PostgreSQL local em `5432` (banco descartável `odca_test_disposable`); ClamAV opcional — o stub `clamscan` está no PATH e o Web requer `DOTNET_ENVIRONMENT=Development` (configuração de desenvolvimento).
- Não há servidor de e-mail: convites/notificações vão para spool local em `src/Odca.Worker/.local/notifications/` (fora do commit).
- Chaves de Data Protection em pasta de usuário específica — não limpar entre restarts se quiser preservar MFA/sessões.

**Anomalias observadas e mantidas:**

- Convite: `protected_token` permanece NULL após envio (token só materializa no aceite) — por desenho.
- Aceitar convite / transferir administração revoga sessões ativas — por desenho de segurança.
- Lote de assentos (seat-quota) sem módulo: `quota_exhausted` apenas quando a versão declara módulos com franquia zero — comportamento esperado.
- Fulfill exige UTC (corrigido em API; feeds externos devem enviar UTC).
- Divergência de storage-root entre configurações de dev registrada (sem impacto no escopo).
- PowerShell 5.1: ambiente exige `DoNotExpandEnvironmentNames` para variáveis com nomes de ambiente; scripts auxiliares da sessão já tratam.

**Limitações de ferramenta:**

- Screenshots por infraestrutura (seção 5) — evidência DOM/API como substituto aprovado.
- Validação responsiva sem redimensionamento de viewport (seção 6) — auditada por breakpoints + medição de overflow.

---

*Entrega concluída sem push; commit local em `codex/s00-foundation` cobre todo o diff da seção 3 excluindo `src/Odca.Worker/.local/`.*
