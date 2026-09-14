# Status de execução

## Correção do controller de documentos e contexto RLS (14/09/2026)

- **Implementado:** os streams e caminhos do upload têm nomes sem conflito; operações físicas usam o alias explícito de `System.IO.File`, enquanto respostas continuam usando os helpers do MVC. O arquivo temporário é descartado após a gravação e removido no `finally`; falha do commit remove o objeto que ainda não se tornou válido no banco.
- **Implementado:** `SetTenant` expõe o `Task<int>` realmente devolvido pelo Dapper. Listagem, conteúdo e revisão de extração agora definem `odca.tenant_id` localmente em uma transação e consultam na mesma conexão, eliminando o contexto de sessão que sobrevivia no pool. Nova versão bloqueia e valida o documento lógico no tenant/contrato antes de calcular seu número.
- **Validado neste ambiente:** `npm run build` e `git diff --check`. O teste PostgreSQL foi ampliado para alternância A → B e limpeza do contexto após commit e rollback usando a role restrita.
- **Não validado neste ambiente:** restore, build e testes .NET, PostgreSQL descartável, HTTP autenticado, navegador e capturas; o executável `dotnet` não está instalado e a obtenção do instalador oficial retornou HTTP 403. Essas verificações não são declaradas aprovadas.
- **Pendente:** BFF da área de contratos/documentos/revisão, inativação, reconciliação automática de órfãos/reservas, idempotência de upload, indicadores do dashboard e QA visual. A jornada integral solicitada continua explicitamente não concluída.

## Evolução do estúdio estruturado — fundação validável (14/09/2026)

- **Implementado:** CA1859 no migrador corrigido no parâmetro privado com o tipo concreto efetivamente produzido por `LoadHistoryAsync`, sem materialização, mudança de contrato público, ordem, checksum, transação ou cancelamento.
- **Implementado:** formato canônico JSON único para documentos, com nós/atributos/marcas permitidos, limites, campos por identificador estável e recusa de HTML, handlers e recursos externos. Validação tipada de campos, confirmação obrigatória na publicação, snapshot imutável, conflito otimista e restauração como novo rascunho foram adicionados à camada Application.
- **Validado neste ambiente:** assets e integridade do diff. O SDK .NET 10.0.400 não está instalado, portanto restore, build e testes .NET não foram declarados aprovados. A CLI do GitHub não está autenticada, logo o resultado remoto atual da CI não pôde ser consultado.
- **Documentado:** ENC0097 como limitação de Hot Reload e sequência segura de reinicialização do perfil `ODCA local`, sem modificar ou excluir configuração pessoal.
- **Pendente:** persistência/API/BFF do estúdio, modelos e cláusulas, PDF rastreável, migration v012, navegador autenticado, PostgreSQL descartável e QA visual. A fundação não é apresentada como jornada completa.

## Incremento S02 recuperado sobre o checkout disponível (14/09/2026)

| Funcionalidade | Implementada | Validada | Pendência | Ação desta entrega |
|---|---|---|---|---|
| Equipe e convites | sim | testes existentes; banco não executado aqui | prova PostgreSQL | preservada; schema avançado para v011 |
| Contratos | núcleo SQL | checksum estático | telas/casos completos da referência ausente | modelo mínimo tenant-aware criado |
| Upload e versões | API e armazenamento privado | testes de detecção/limites | reconciliação automática | streaming, hash, cota transacional e imutabilidade |
| Segurança | Worker local | fail-closed por código | fixture ClamAV | estados separados; sem liberação implícita |
| Extração | PDF nativo, imagem OCR e DOCX | DOCX/sugestões unitários | OCR de PDF digitalizado e QA executável | job com lease, retry e resultado rastreável |
| Revisão/aplicação | schema de decisões | checksum | API/BFF transacional e tela split-view | persistência preparada, sem alegar jornada completa |

O SHA histórico `183aa2b5` continua ausente do pack local e o fetch do GitHub foi
recusado pelo proxy (HTTP 403). O estado real foi reavaliado em `f6954f3`: este
checkout continha S01 e apenas o reparo v010, não os módulos S02 mencionados.
Esta entrega implementa uma primeira fatia executável em vez de declarar como
existente o código inacessível. SDK .NET, PostgreSQL descartável e navegador não
estão disponíveis neste ambiente; esses gates permanecem explicitamente não
aprovados.

## Reparo do pacote S01/S02 (14/09/2026)

- O checkout disponível está em `596ffe6` (S01), e não contém o SHA S02 informado (`183aa2b5`); o acesso ao remoto foi recusado pelo proxy HTTP 403. Assim, a central de contratos/documentos não foi recriada sobre uma base antiga nem declarada concluída.
- Foi reproduzido por inspeção o bloqueio PostgreSQL 42P13 da migração 009: parâmetro de entrada e coluna `RETURNS TABLE` usavam `invitation_id`. O instalador canônico agora usa `p_invitation_id`, sem alterar a assinatura por tipos, permissões ou o snapshot distribuído v009.
- O release v009 defeituoso permanece imutável para auditoria. O migrador aceita **somente** a transição dos checksums conhecidos v009 defeituoso → reparado, recria a função sem `DROP CASCADE` e atualiza o registro; qualquer outro checksum continua sendo recusado. O v010 registra o reparo e é o pacote canônico integral para instalação nova, atualização completa ou histórico parcialmente aplicado.
- Os testes de host agora isolam também Data Protection e cada caso mantém válidos os campos alheios ao alvo. As mensagens JWT identificam a própria validação, sem reduzir os requisitos de produção.
- Não verificado neste ambiente: restore/build/testes .NET (SDK ausente), PostgreSQL descartável, HTTP autenticado, navegador e larguras visuais. O build de assets e as verificações estáticas executadas estão registrados no PR.

## Configuração local consolidada (10/09/2026)

- O setup oficial passou a ser `.\scripts\setup-local.ps1`; ele mantém `Database=postgres`, schema `odca` e separa configuração/conectividade de migration e provisionamento.
- A exceção para provisionar acesso de demonstração na base `postgres` requer simultaneamente `--environment Development` e `--allow-postgres-development`; as recusas históricas abaixo descrevem versões anteriores.
- Neste ambiente de revisão o executável `dotnet` não está instalado e não há conexão autorizada ao PostgreSQL do usuário; portanto build/testes .NET, schema, credenciais, login HTTP, MFA e dashboard não foram declarados aprovados.

## Evolução prompt 13 — central operacional de equipe (10/09/2026)

- SHA inicial desta sessão: `297b2cde045c11f27665f4bf5d82ebd3d39b151c` (já à frente de `6ac493b` com lockfiles/CI).
- Restore bloqueado, build Release e `npm run build` aprovados com SDK 10.0.400 neste ambiente. NU1004/NU1510 não reproduzidos.
- Migração 008: checksum persistido corrigido. Migração 009 (`CurrentVersion=9`): status `expired`, aceite sem reativar vínculo bloqueado/inativo, entrega separada do ciclo do convite, expiração/cancelamento/reenvio, proteção do último administrador.
- API/BFF: central com abas Pessoas/Convites/Perfis, paginação real, 403 ≠ lista vazia, overview de assentos, ações de membro, perfis, seletor/edição de organização, preview de convite e copy honesta de reautenticação.
- Worker: lease por token; cancelamento do host separado; stub de produção falha com `notification_provider_missing`.
- Testes locais sem banco: Domain (23) + MigrationChecksum/startup/host (27) aprovados. Suíte PostgreSQL descartável e QA visual autenticado **não** executados: falta credencial admin (`development-runtime.json` ausente) e Docker Desktop parado; PostgreSQL 18 nativo está em execução na porta 5432.
- Esta entrega **não** declara conformidade LGPD plena nem e-mail de produção.

## Evolução prompt 10 — primeira fatia visual e navegação por perfil (10/09/2026)

- O contrato de autenticação agora transporta explicitamente o tipo de conta; o BFF converte esse dado em role do ticket protegido e monta navegação de plataforma ou cliente sem inferência visual.
- O layout autenticado ganhou sidebar recolhível, contexto inequívoco, topbar e drawer mobile com retorno/contenção de foco e fechamento por `Escape`. Percursos de senha inicial e MFA continuam fora do shell administrativo.
- Tokens visuais, controles, métricas, planos e autenticação foram consolidados em um sistema corporativo responsivo. A largura total saiu de `.button-primary` e passou ao modificador contextual `.button-block`.
- Login e cadastro têm exibição acessível de senha e proteção visual de envio repetido. A home do cliente diferencia limites contratados de consumo medido.
- `npm run build` e `git diff --check` foram aprovados. Restore/build/testes .NET não puderam ser executados porque o SDK 10 não existe no ambiente e a instalação oficial foi recusada por HTTP 403. Por isso, o NU1004 não foi declarado corrigido e lockfiles não foram alterados manualmente.
- QA renderizado e capturas em 360/768/1440 também ficaram bloqueados pela ausência de runtime .NET e navegador instalado. Esta fatia não declara organização múltipla, equipe, convites, quotas ou ficha de clientes concluídos.

## Etapa atual

Referência revalidada em 09/09/2026: SHA base `5ab8df3`. S00 continua **em validação de banco/navegador**. S01 está **parcial**: catálogo público e entrada pública de solicitações de privacidade existem; onboarding, MFA, equipe e operação do atendimento ainda não.

## Evolução prompt 09 — acesso local verificável

- A duplicação de `StartupValidationService` introduzida pelo merge foi removida de `StartupValidation.cs`; a implementação permanece no arquivo próprio e há um único registro hospedado.
- O Bootstrap ganhou `provision-test-access --environment Development`: destino sanitizado, recusa da base `postgres`, transação para superadministrador e cliente sintético, organização de demonstração, administrador de tenant, Basic vigente e auditoria da concessão local.
- Reexecução preserva identidades e senhas. `--rotate-passwords` é necessário para rotação; conta comum preexistente não é elevada. Após o commit, o comando relê perfil, bloqueio/exclusão, vínculo, papel e plano, e verifica senhas disponíveis pelo `IPasswordService`.
- `show-login` agora consulta o banco e não apresenta arquivo desatualizado como senha atual. `reset-password` valida exatamente uma linha de usuário, revoga sessões na mesma transação e preserva MFA.
- Este ambiente não contém SDK .NET nem conexão PostgreSQL local autorizada. Portanto persistência, senha, login HTTP e MFA ainda não foram alegados como executados; os comandos exatos estão no README.

## Correção posterior à CI 34350222079

- A configuração da API deixou de ser lida antecipadamente em `Program` e no registro da Infrastructure. JWT, política de autenticação e data source agora são materializados depois que o host terminou de compor suas fontes de configuração.
- Uma validação hospedada mantém o fail-fast para conexão, issuer, audience, chave, expiração e controles de produção, sem inserir configuração sintética na aplicação.
- Testes determinísticos cobrem aceitação da configuração isolada e rejeição de JWT/MFA inválidos. A execução local não foi possível neste ambiente porque o SDK `dotnet` não está instalado; a correção precisa do gate de CI/PostgreSQL.
- A matriz factual deste incremento está em `docs/execution/S01_RECONCILIATION.md`.

## Implementado nesta evolução

- NU1004 corrigido: JwtBearer e Mvc.Testing alinhados em `10.0.12`, locks regenerados e restore bloqueado aprovado.
- Fixture isolada: sem fallback para `development-runtime.json`, sem mutação global de ambiente e com marcador, banco `odca_test*` e role `odca_test_app_login` obrigatórios.
- CI com secret scan independente e publicação de TRX mesmo após falha de testes.
- Runner valida todo o histórico conhecido antes de mutar; rejeita checksum divergente, lacuna e banco mais novo que o código. Readiness exige PostgreSQL 18 e schema exatamente na versão 3.
- Sessão de autenticação usa a duração JWT configurada; login durante bloqueio não prorroga a janela.
- BFF diferencia 401, 403, 429, timeout e 5xx em login, troca de senha, dashboard e planos; falha do backend não é apresentada como logout.
- Catálogo público separado da administração em `/api/v1/catalog/plans` e `/conheca-os-planos`, com rejeição explícita de plano vigente ambíguo ou incompleto.
- Migração 003 e fatia ODCA-PRIV-003: formulário `/privacidade`, API pública com rate limit, protocolo opaco, resposta neutra, histórico inicial e gravação por função de privilégio restrito.
- Teste comportamental preparado para role runtime: ausência de contexto, alternância de tenant, `role_permissions`, auditoria e FK cruzada A × B.

## Histórico de verificações (resultados não cumulativos)

| Verificação | Resultado |
|---|---|
| `dotnet restore Odca.sln --locked-mode` no SHA anterior | sucesso |
| `dotnet build Odca.sln --configuration Release --no-restore` no SHA anterior | sucesso; 0 avisos, 0 erros |
| `npm run build` no SHA anterior | sucesso |
| testes de domínio no SHA anterior | 10 aprovados |
| parser/checksum/snapshots de migração no SHA anterior | 6 aprovados |
| `dotnet test Odca.sln --configuration Release --no-build` no SHA anterior | 16 aprovados; 3 cenários PostgreSQL recusados antes de conexão por ausência do marcador exclusivo |
| CI remoto do SHA `c5e5064` | secret scan aprovado; restore falhou por NU1004 antes de build/assets/testes |
| `npm run build` nesta correção | sucesso com Node 20.20.2; o projeto declara Node >=24, portanto a CI continua sendo o gate na versão suportada |
| `dotnet restore Odca.sln --locked-mode` nesta correção | não executado: SDK .NET 10 ausente e download bloqueado por HTTP 403 no ambiente |
| suíte PostgreSQL atual | não executada: faltam as três variáveis exclusivas de teste e uma credencial administrativa autorizada |

## Parcial, pendente ou bloqueado

- `database/odca.sql` permanece um histórico rastreável e o runner pula versões aplicadas, mas a execução direta integral no pgAdmin ainda reexecuta corpos históricos. A equivalência/recovery/concurrency do pacote SQL não foi comprovada.
- O teste A × B e o fluxo API foram escritos, mas aguardam banco descartável autorizado; nenhum banco de desenvolvimento foi tocado.
- MFA real, cache/Data Protection compartilhados, onboarding de organização, confirmação de e-mail, assinatura, equipe/perfis, suporte temporário, tratamento autenticado de direitos, retenção executável e S02/S03 permanecem pendentes.
- A página pública de privacidade é um canal de entrada técnico; não publica texto jurídico, prazo ou contato não aprovados e não constitui comprovação de conformidade LGPD.
- QA visual responsivo e navegador autenticado ainda não foram executados.
- Marcos B–E não foram implementados por esta correção do gate A; permanecem explicitamente pendentes e não devem ser inferidos dos testes de startup.

## Evolução prompt 08 — incremento local de integridade (09/09/2026)

- Corrigidos os três defeitos reproduzidos da CI 34367965217: contagem canônica de migrações, projeção explícita da home e conversão UTC do estado MFA.
- A elevação MFA agora renova, na mesma transação, a expiração persistida da sessão para exatamente a validade emitida no JWT. O início exige sessão/versão válidas e estado ainda não confirmado; a confirmação compara o segredo protegido observado.
- CPF/CNPJ passam por dígitos verificadores. A migração 006 preserva 001–005, impede mais de um cadastro vivo por documento e acrescenta campos de evidência de termos/aviso. Os marcadores de versão permanecem `pending-legal-approval`, sem representar texto jurídico aprovado.
- Repetição idempotente volta a criar a mensagem de outbox ligada ao token rotacionado, e a confirmação elimina o token de desenvolvimento.
- Chaves Data Protection têm caminho persistente configurável para API e BFF. O ticket store segue em cache de memória: múltiplas instâncias **não** estão declaradas prontas.

Ainda pendentes nesta entrega: transporte de e-mail de produção/worker com lease e retentativas, seleção explícita de organização, equipe/perfis/convites/quota, reset privilegiado de MFA, QA visual e suíte PostgreSQL descartável. Não há editor/OCR iniciado.

## Evolução prompt 11 — contexto explícito e primeiro percurso de convite (10/09/2026)

- A migração 007 introduz versão otimista da organização, permissões delegáveis do tenant, convites que reservam assento e outbox com lease/retry. Funções `SECURITY DEFINER` têm superfície explícita e o acesso comum continua sob RLS.
- A API lista organizações pelo `sub` autenticado, exige tenant explícito nas operações e revalida vínculo/permissão. Há leitura de equipe/perfis, criação restrita de perfil, edição organizacional com conflito e criação/aceite de convite de uso único.
- A confirmação do onboarding passa a atribuir o administrador inicial idempotentemente. Aceitar convite exige a identidade autenticada com e-mail verificado e revoga suas sessões para renovar autorização.
- O BFF ganhou escolha por nome e equipe/perfis sem IDs digitados. O shell usa o nome selecionado, fallback seguro de avatar e melhorias de drawer/localStorage/formulários.
- `npm run build`, validação local dos sete checksums e `git diff --check` passaram. O SDK 10.0.400 continua ausente e o instalador oficial respondeu HTTP 403; portanto restore forçado/bloqueado, build/testes .NET, PostgreSQL e QA renderizado não foram executados neste ambiente. Os lockfiles não foram editados manualmente.
- Bloqueio/restauração de membros, reenvio/cancelamento, proteção concorrente do último administrador e transporte de produção permanecem pendentes; esta entrega não declara o módulo integral concluído.
