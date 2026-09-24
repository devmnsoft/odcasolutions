# Jornada do documento final — incremento 026

## Diagnóstico confirmado no HEAD

| Categoria | Resultado |
|---|---|
| Existente e preservado | Autenticação/MFA, isolamento por organização, pacientes e representantes, Studio, snapshot JSON canônico, comparação e conferência, revisão com comentários/decisão/idempotência, acervo, contratos B2B e documentos sem paciente. |
| Defeito confirmado | A versão era exibida como JSON; os metadados visíveis eram consultados nos cadastros atuais; não havia PDF final nem preparação persistente de participantes; a caixa operacional consultava a tabela histórica `contract_reviews`, enquanto a jornada atual grava em `contract_review_requests`; sucesso da solicitação afirmava incorretamente notificação do responsável. |
| Avanço desta entrega | Leitor HTML seguro derivado do JSON, metadados congelados para novas emissões, PDF privado imutável com integridade/contabilização, revisão ligada ao leitor/PDF, caixa na fonte atual e preparação de participantes vinculada à versão com concorrência otimista. |
| Dependência posterior | Provedor real de assinatura, envio idempotente, eventos autenticados, reconciliação/cancelamento e preservação do documento assinado e evidências. A assinatura permanece **Não integrada**. |

O `schemaVersion` dentro do artefato identifica o envelope serializado em arquivo (atualmente 2). `content_schema_version` identifica o formato da árvore de conteúdo armazenada no banco (atualmente 1). São conceitos distintos; nenhuma versão histórica é reescrita.

## Decisões técnicas

* O JSON validado por `StructuredContractDocument` permanece a única fonte canônica. HTML e PDF passam pelo mesmo interpretador de nós e pelo mesmo mapa de valores congelados.
* Texto é codificado na saída HTML, atributos e nós não reconhecidos continuam rejeitados e o formato não admite imagens ou URLs externas. Campo opcional ausente aparece explicitamente como “Não informado”.
* Novas versões congelam título, organização, modelo e autor em `emission_metadata`. Versões anteriores mantêm compatibilidade por fallback claramente limitado aos dados atuais; hashes e bytes antigos não são recalculados ou substituídos.
* O PDF usa PDF 1.4, texto WinAnsi selecionável, paginação A4, margens, quebras explícitas e numeração. Não há dependência binária ou licença adicional; Helvetica é uma das fontes base de PDF. O renderizador tem versão registrada.
* A geração bloqueia a linha da versão, é idempotente quando concluída, grava em arquivo temporário e faz movimento atômico. Falha antes do commit remove o arquivo criado. Download recalcula SHA-256 e falha com segurança em ausência ou divergência.
* O hash canônico e o hash do PDF são deliberadamente separados. Bytes do PDF concluído não são regenerados por atualização do renderizador.
* O tamanho do PDF incrementa `tenant_storage_usage` e cria `resource_movements`, as fontes existentes de consumo. Não existe liberação automática por exclusão lógica.
* `contract_review_requests` é a fonte canônica da jornada atual. `contract_reviews` permanece no banco por compatibilidade histórica e não foi removida.
* Participantes são cópias explícitas da preparação: tipo, origem opcional, papel, nome e contato. Alterações não escrevem no paciente e igualdade de contato não mescla pessoas.

## Banco

A migration incremental 026 adiciona estado/arquivo/hash/tamanho/renderizador do PDF e metadados da emissão; cria `signature_preparations` e `signature_participants`, com FKs compostas, constraints, índices, RLS e concorrência por `row_version`. O SQL consolidado e o snapshot `odca-v026.sql` contêm exatamente o mesmo histórico.

## Roteiro manual

1. Na ficha do paciente, abra o acervo e uma versão emitida (ou abra uma versão B2B sem paciente pelo Studio).
2. Confirme título, partes, hierarquia, listas/tabela, formatação, campos repetidos e vazio opcional no documento legível.
3. Gere o PDF, baixe-o e confira seleção de texto, acentos, tabelas, quebras e rodapé em documento curto e extenso.
4. Edite paciente/representante, organização, contrato e modelo; reabra a versão e confirme que conteúdo e metadados de uma nova emissão permanecem iguais.
5. Solicite revisão, abra a versão exata a partir da revisão, registre comentários e decida. Em “ajustes”, volte ao Studio e gere outra versão, sem aprovação herdada.
6. Confira uma única ocorrência da revisão na Caixa e na Agenda, com responsável/prazo corretos.
7. Prepare paciente, representante ou profissional, salve, reabra e confirme. Edite em duas abas e confirme conflito na segunda. Verifique que a interface continua dizendo “Não integrada”.
8. Com usuário de outra organização, tente URLs da versão, PDF, revisão e preparação; todas devem ser negadas ou não encontradas.

## Evidências e limitações deste ambiente

* **Passou:** `npm run build` e verificação textual de consistência entre referências diretas e arquivos de lock.
* **Bloqueado pelo ambiente:** SDK .NET 10.0.400, PostgreSQL/`psql`, Docker e navegador não estão instalados. O download do instalador do SDK respondeu HTTP 403. Portanto restore bloqueado, build .NET, testes unitários/HTTP/PostgreSQL e inspeção visual/PDF automatizada não foram executados localmente e não são declarados como homologados.
* **Não executado:** screenshot da aplicação, pois o runtime e o banco necessários para iniciar a aplicação não estão disponíveis.

## Incremento 027 — preparação rastreável e prontidão

### Auditoria e causas confirmadas

* O restore bloqueado da referência falha porque os locks dos projetos que referenciam `Odca.Infrastructure` ainda resolviam Dapper 2.1.86 e IdentityModel 8.22.0, enquanto a infraestrutura passou a exigir 2.1.89 e 8.23.0. Os grafos foram sincronizados com as entradas geradas pelo lock da própria infraestrutura, sem remover `--locked-mode`.
* A role `odca_app` não possui `DELETE` em `signature_participants`, mas o endpoint apagava toda a composição. A migration 027 mantém cada composição como uma revisão somente-acréscimo e registra inclusão, alteração, retirada, reordenação, confirmação e reabertura.
* A criação anterior usava `ON CONFLICT DO UPDATE`: duas primeiras gravações podiam se substituir. Criação (`ExpectedVersion=0`) e atualização agora são distintas, bloqueiam a preparação e retornam conflito recuperável.
* O arquivo PDF tinha nome compartilhado entre tentativas e era apagado em qualquer exceção, inclusive depois de um commit possivelmente concluído. O nome agora inclui versão do renderizador e hash; somente o temporário identificado pela tentativa é removido. Um arquivo publicado preexistente é reutilizado apenas se seus bytes tiverem o mesmo hash.
* O débito de PDF agora sincroniza a cota efetiva com concessões vigentes e somente incrementa `used_bytes` quando o movimento idempotente é criado. O download usa contexto RLS local a uma transação.

### Avanços entregues

A conferência de prontidão é calculada no backend e separa preparação completa, revisão interna, integração e envio. Ela verifica versão, arquivo publicado, hash físico, composição e ajustes de revisão; sem provedor, a composição pode ser confirmada, mas a integração continua explicitamente indisponível. Confirmar congela revisão da composição, chave e hash do PDF. Reabrir exige justificativa e preserva a evidência anterior no log.

O editor permite adicionar participantes manuais, escolher as identidades disponíveis no snapshot pelo nome, editar, ordenar e retirar com confirmação. Trocar o tipo limpa a origem. Documentos B2B sem paciente começam sem participante fictício.

### Migration e compatibilidade

A migration 027 é incremental: adiciona revisão de composição e evidência de confirmação, transforma participantes em snapshots revisionados, cria eventos append-only com RLS e privilégios mínimos e passa a incluir o identificador do representante em novos snapshots. Registros 026 existentes permanecem legíveis; confirmações legadas sem hash precisam ser reabertas e confirmadas novamente para obter evidência completa.

### Evidência e limitações deste ambiente

* **Passou:** validação de checksums das 27 migrations, consistência JSON dos locks, build dos assets e verificação de whitespace do patch.
* **Bloqueado:** SDK .NET 10.0.400, PostgreSQL, Docker e navegador continuam ausentes; a tentativa de baixar o instalador oficial recebeu HTTP 403. Restore bloqueado, compilação, testes .NET/PostgreSQL com `odca_app`, concorrência real, execução limpa/upgrade e captura visual não puderam ser executados localmente.
* **Dependência posterior:** central operacional completa e evolução tipográfica avançada do PDF (fonte Unicode incorporada e layout de tabelas) não são declaradas homologadas neste incremento. Provedor, envio, eventos externos, cancelamento, documento assinado e certificados continuam fora do escopo e não são simulados.

### Roteiro de homologação

1. Execute restore bloqueado, build, assets e toda a suíte com o SDK fixado.
2. Aplique `odca-v026.sql` em banco descartável, execute upgrade para 027 e repita em instalação limpa; valide checksums e RLS.
3. Autentique a API com conexão `odca_app`; crie, edite, reordene e retire participantes, verificando as revisões e eventos sem `DELETE`.
4. Abra duas sessões: faça duas criações e duas atualizações concorrentes; a segunda deve receber HTTP 409 sem sobrescrever a primeira.
5. Corrompa/remova uma cópia descartável do PDF e confirme que prontidão, confirmação e download recusam o artefato; restaure-o e valide o hash.
6. Interrompa geração antes/depois da publicação e antes/depois do commit; repita e confira um arquivo, um movimento e um débito.
7. Confirme, reabra com justificativa e confirme novamente; confira as duas evidências, revisões e hashes.
8. Repita como usuário de outra organização e confirme ausência de leitura e gravação cruzadas.
