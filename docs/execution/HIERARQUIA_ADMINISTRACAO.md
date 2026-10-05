# Hierarquia, perfis e acesso por plano

Baseline de partida: `2a50a22f9444ab7fa3fd137ac6df60ce7f916784`. Nenhuma migration de 001 a 033 foi reescrita. A migration nova é a 034.

## Acesso efetivo

Uma operação segue quando todas as condições abaixo são verdadeiras:

1. Usuário autenticado, habilitado e com sessão válida.
2. Vínculo ativo com a organização, salvo administrador da plataforma no fluxo global.
3. Organização operacional, não suspensa.
4. Módulo habilitado na versão de plano vinculada à assinatura.
5. Sem bloqueio administrativo daquele módulo.
6. Permissão da ação, pelo vínculo de perfil ou pelo código `tenant-administrator`.
7. Franquia disponível quando a operação consome vaga ou armazenamento.

O administrador principal também passa por plano, bloqueio e limite. Liberar um módulo na administração não edita o contrato. Trocar perfil não habilita módulo fora do plano. Contratar o módulo não grava permissão para todas as pessoas.

Motivos: `permission_denied`, `membership_blocked`, `organization_suspended`, `plan_restricted`, `feature_blocked`, `quota_exhausted`.

A franquia mensal de assinatura e OCR não é medida. `quota_exhausted` só aparece quando a versão declara módulos e a franquia correspondente está ausente ou em zero. Vaga e armazenamento continuam com as regras já existentes.

## Perfis iniciais

O código `tenant-administrator` permanece o único bypass e o nome gravado continua “Administrador da organização”. A tela pode apresentá-lo como “Superadministrador da organização”. Os outros perfis não recebem esse código. Permissão de plataforma não entra em perfil de organização.

| Perfil | Código | Poder inicial | Fora do perfil |
| --- | --- | --- | --- |
| Superadministrador da organização | `tenant-administrator` | Todas as permissões `tenant.%` já concedidas ao perfil, com bypass no servidor | Plano, bloqueio, limite e suspensão |
| Administrador delegado | `tenant-delegated-administrator` | Organização (consulta), equipe (consulta e gestão), consulta de pacientes, modelos, minutas, revisão e importações, download e consulta de plano | Sem `tenant.billing.manage`, sem transferência da administração principal |
| Coordenador | `tenant-coordinator` | Consulta operacional, revisão (`reviews.decide` e histórico) e atribuição de obrigação | Sem gestão de equipe, plano ou perfis |
| Usuário | `tenant-member` | Pacientes, consulta de modelos e minutas, edição de minuta, download, consulta e solicitação de revisão | Sem administração |

Perfis personalizados continuam no catálogo existente. Perfis de sistema não são regravados pela edição de permissões. Quem não é administrador principal não atribui esse perfil nem uma permissão que não possua. O último administrador principal ativo permanece protegido pelo bloqueio consultivo já existente.

## Planos publicados

Versão 1, efetiva desde 2026-09-09 e encerrada em 2026-10-05, não declara `module.*`. Assinaturas antigas permanecem nessa versão até uma alteração confirmada.

Versão 2, vigente desde 2026-10-05, repete os limites e habilita os sete módulos: pacientes, minutas, modelos, documentos, revisões, assinaturas e importações.

| Plano | Vagas | Armazenamento | Teto por usuário | Arquivo | OCR mensal | Assinaturas mensais |
| --- | --- | --- | --- | --- | --- | --- |
| basic | 3 | 10 GB | 5 GB | 25 MB | 300 | 10 |
| intermediate | 10 | 100 GB | 25 GB | 100 MB | 3 000 | 50 |
| enterprise | 30 | 500 GB | 100 GB | 250 MB | 15 000 | 200 |

Os valores seguem os inteiros já gravados em bytes e páginas. Não há preço novo. Downgrade usa a política `keep_members_and_documents`: preserva pessoas, pacientes, documentos e histórico; não escolhe quem bloquear; novas vagas continuam limitadas pelo plano. A auditoria grava `paymentRecorded=false`.

## Verificação executada

- `dotnet restore Odca.sln --locked-mode`
- `dotnet build Odca.sln --configuration Release --no-restore`: 0 avisos, 0 erros
- `npm run build`: 10 assets e os percursos decimal/modelo
- `dotnet test Odca.sln --configuration Release --no-build`: Domain.Tests 298, IntegrationTests 159, em `odca_test_hierarchy_base`

A suíte de integração cobre a migration 034, o catálogo vigente com sete módulos, o downgrade com pacientes preservados, papéis padrão, o gatilho de permissão de plataforma, a remoção concorrente do último administrador e o bloqueio de um vínculo sem afetar o outro. A jornada de estúdio e documentos existente permanece na suíte que passou.

Não houve navegação em navegador. Não houve publicação nem merge.
