# Incremento — jornada documental e biblioteca oficial

Base: `b28214f` em `codex/s00-foundation`. Nenhuma publicação, merge ou implantação foi feita.

## O que este incremento corrige

- Moeda: a interface aceita pt-BR e a persistência guarda decimal canônico. `150,00` não vira `15000`. `1.250` e `1,250` são recusados como ambíguos. O PDF e o HTML exibem `N2` pt-BR a partir do valor canônico.
- Publicação: `PUT` do modelo devolve a revisão efetivamente gravada. Se a publicação falhar depois do salvamento, o formulário permanece com o conteúdo e a revisão salvos, diz qual etapa falhou e não anuncia publicação concluída. Conflito com conteúdo idêntico reutiliza a revisão do servidor.
- Biblioteca: modelos oficiais passam a ter `official_key`, `official_revision` e `document_purpose` (migration 031). Instalação, consulta e contagem usam a chave. Nome, tipo e texto dos campos deixam de identificar o modelo. Registros antigos só recebem chave quando o evento `template.official_installed` aponta um único modelo ativo.
- Acesso local: a senha do operador deixa de ter um segundo valor no script e na documentação. Reexecução preserva o hash. Senha diferente só entra com rotação explícita. O secret scan permanece ligado; checksums de migration e as senhas iniciais de Development estão na lista de exceção. A varredura local ainda aponta arquivos ignorados pelo Git (runtime, chaves de proteção e `.env.local`), que não entram no checkout da CI.
- Funcionalidades por organização: migration 032 cria o bloqueio administrativo auditado, separado de plano e de permissão. A API recusa a chamada direta com `feature_blocked`. A liberação devolve o estado real, que pode continuar `plan_restricted`. A suspensão da única organização do usuário invalida a sessão. As regras estão em `docs/execution/BLOQUEIOS_E_LIBERACAO.md`.
- Jornada de novo documento: a busca de paciente aceita nome ou identificador e percorre as páginas. O cadastro feito no meio do fluxo volta com a busca e a página anteriores.

## Matriz

| Etapa | Tela | Endpoint | Persistência | Permissão | Evidência |
| --- | --- | --- | --- | --- | --- |
| Moeda na minuta | Estúdio, campo moeda | `PUT .../drafts/{id}` normaliza antes de validar | `values` em decimal canônico | `tenant.contract_drafts.manage` | Testes de domínio. Navegador: HOMOLOGADO (02/10/2026, jornada do cliente até a emissão) |
| Publicar modelo | Estúdio do modelo | `PUT` devolve `rowVersion`; `POST .../publish` usa essa revisão | `contract_templates.row_version` | `tenant.templates.manage` | Compilação. Edição no navegador: HOMOLOGADO (guarda não destrutiva A.5, 02/10/2026). Conflito em duas abas: não executado |
| Biblioteca oficial | Instalação da biblioteca | `GET/POST .../templates/official` | `official_key` + migration 031 | `tenant.templates.read/manage` | Catálogo de integração no banco `odca_test_increment_031` |
| Paciente e partes | Minuta nova | `POST .../drafts` preenche origem explícita | snapshot do paciente; valor na minuta | `tenant.contract_drafts.manage` | Teste de domínio do preenchimento. Jornada no navegador: HOMOLOGADO (02/10/2026, busca de paciente → minuta → emissão) |
| Terapias e consentimento | Modelo oficial | mesmos endpoints de modelo | chave `multiple-therapies` e `informed-consent` | gestão de modelos | Modelo canônico testado. O texto não é o contrato original integral |
| Acesso de teste | Login | `POST /api/v1/auth/login` | hash ASP.NET Identity | papéis reservados | Teste de integração autenticou operador e recusou rotação silenciosa |
| Busca de paciente na emissão | Novo documento | `GET .../patients?search&page` | pacientes da organização | `tenant.patients.read` | Tela ligada à paginação existente. Navegador: HOMOLOGADO (02/10/2026) |
| Bloqueio de funcionalidade | Ficha do cliente e menu | `GET/PUT .../features`; o portão cobre pacientes, minutas, modelos, PDF, revisões, assinatura e importações | `organization_feature_blocks` + auditoria | superadministrador na gravação; membro na consulta | HTTP e navegador: HOMOLOGADO nos dois sentidos por UI e API (02/10/2026); regras em `docs/execution/BLOQUEIOS_E_LIBERACAO.md` |
| Suspensão | Ficha do cliente | sessão e rotas da organização | `tenants.status` | superadministrador | HTTP e navegador: suspensão e restauração HOMOLOGADAS nos dois sentidos por UI e API (02/10/2026); limitação conhecida de UX sob suspensão documentada em `docs/execution/BLOQUEIOS_E_LIBERACAO.md` |

## Pendências

- Homologação de navegador dos roteiros A–H: executada para a jornada do cliente até a emissão (PDF baixado e validado pelo cabeçalho), bloqueio/suspensão por UI e API e auditoria aberta na tela com filtros (ver seção Homologação em navegador). Restam sem prova visual: menus em 360/768/1440 px e os roteiros não exercidos neste ciclo.
- O estúdio visual, a jornada guiada e o padrão responsivo não foram refeitos. O editor existente ganhou origem do campo e moeda pt-BR. A emissão nova passou a buscar paciente fora da primeira página.
- Assinatura continua na fronteira já existente: preparação confirmada não envia ao provedor. Sandbox não foi exercitado.
- O contrato de múltiplas terapias fornecido continua mais longo que o modelo oficial. Cláusulas substantivas não foram copiadas para o modelo; a seleção de terapias, o preço condicional e o representante passaram a ser campos canônicos.
- A divergência entre cadastro atualizado e minuta já aberta, com atualização consciente do snapshot, não foi implementada.
- O bloqueio de vínculo em uma organização, sem afetar outra, continua no mecanismo existente e não foi reexecutado neste ciclo.
- A mensagem `organization_suspended` para quem ainda possui outra organização ativa está no portão da API e não teve chamada HTTP própria.
- Faturas, pagamentos e cobranças continuam ausentes. Nenhum lançamento financeiro foi inventado.

## Homologação em navegador (02/10/2026)

Executada em navegador real (Chrome) contra Web `https://localhost:7144` + API `https://127.0.0.1:7143` e banco PostgreSQL local descartável `odca_test_disposable`. Implementado, testado e homologado seguem separados nas evidências acima; só o que foi executado no navegador com banco real está listado aqui.

Homologado neste ciclo:

- Jornada do cliente até a emissão: login como `cliente.teste@odca.local`, busca de paciente na tela de novo documento, criação da minuta com valor em pt-BR normalizado para decimal canônico, salvamento de rascunho incompleto aceito pela validação estrutural, emissão rejeitada sem confirmação (A.3: `Structural` ≠ `Complete` ≠ `Confirmed`), emissão concluída com PDF baixado e validado pelo cabeçalho `%PDF-`, e duplicata de identificador respondida com `409 patient.identifier.duplicate`.
- Guarda do editor de documento não destrutiva (A.5): o root cause era um IIFE interpretado como chamada de conjunto em `TemplateEdit.cshtml`; corrigido e homologados os três desfechos (recusar, aprovar, sem alteração) sem perda de conteúdo digitado.
- Superadministrador: troca obrigatória de senha na primeira entrada, inscrição MFA com chave TOTP exibida na tela, desafio com código e regra anti-reapresentação (passo TOTP menor ou igual ao último passo aceito é recusado como já utilizado; tolerância de ±1 período; até 5 tentativas), códigos de recuperação de uso único e painel acessível em seguida. A mensagem genérica de erro do desafio em `/mfa/desafio` mascara o erro exato da API; mantida como pendência menor.
- Bloqueio de funcionalidade e suspensão/restauração da organização por UI e API, nos dois sentidos. A limitação conhecida de UX sob suspensão (banner inacessível para quem está suspenso; membro recebe 401 genérico) está documentada em `docs/execution/BLOQUEIOS_E_LIBERACAO.md`, sem mudança de código neste ciclo.
- Auditoria da plataforma em tela: `/administracao/auditoria` com listagem paginada, badges de resultado, datas `dd/MM/yyyy HH:mm`, links de metadados, busca por texto, filtro por organização e clamps de página (`999`→`100`, `2`→`10`). Hub `/modulos/auditoria` respondendo 200.
- Caixa operacional (`/caixa`): dois defeitos corrigidos na origem — registro DI ausente (`AddOdcaOperationalInbox()` não era chamado no wiring central) e coluna errada no SQL (`c.row_version` → `c.version`) em `OperationalInboxRepository.cs`. Fluxo completo homologado; linha de seed removida.

Correção de teste (não de produto):

- O reset do fixture `ResetScenarioAsync` em `DevelopmentAccessProvisioningTests` apagava apenas um subconjunto das tabelas; linhas remanescentes da jornada (minutas, contratos, pacientes etc.) bloqueavam o delete de `memberships` por FK. O reset agora executa o fechamento transitivo ordenado das FKs dos tenants e usuários de demonstração (filhos antes dos pais), com escopo por `tenant_id`/`user_id` onde a coluna existe. Após o ajuste: 153/153.

Contagens finais desta execução:

- Suíte de domínio: 286/286 aprovados.
- Suíte completa de integração: 153/153 aprovados em banco descartável (antes do ajuste do reset: 150/153, com as 3 falhas no fixture).
- A última execução da suíte reinicia os dados de demonstração do banco local (comportamento esperado do fixture); as credenciais iniciais documentadas foram restauradas com `provision-test-access --rotate-passwords`. A verificação final do superadministrador no navegador alterou a senha local para outro valor forte e reinscreveu o MFA local; ambos os estados existem apenas no banco descartável.

## Verificação

- Passou: suíte de domínio, 261 testes.
- Passou: checksums, catálogo oficial e provisionamento de acesso, 38 testes de integração, em banco descartável, com a migration 031 aplicada e reaplicada.
- Passou: checksums até a migration 032 e o teste HTTP de bloqueio, liberação, preservação do paciente e suspensão da organização única, no banco descartável `odca_test_feature_032`.
- Passou: `npm run build` (8 assets).
- Passou para arquivos versionados: secret scan com a configuração do repositório. Oito achados restantes estão em arquivos ignorados pelo Git.
- Passou: suíte de domínio, 286/286 (reexecução de 02/10/2026).
- Passou: suíte completa de integração, 153/153, em banco descartável (02/10/2026, após endurecimento do reset do fixture).
- Passou: homologação em navegador real, conforme a seção Homologação em navegador (02/10/2026).
- Não executado neste ciclo: sandbox de assinatura e upgrade de um banco já populado na versão 030.
