# Incremento 23 — paciente e geração documental

## Responsabilidades entregues

`/api/v1/organizations/{tenantId}/patients` expõe consulta paginada, cadastro mínimo,
edição otimista, inativação/restauração e ficha. O identificador é opcional para salvar;
quando informado, tipo e valor são atômicos e a unicidade vale para paciente ativo dentro
do tenant. Representação é uma relação explícita e não cria identidade de login.

Ao criar uma minuta no estúdio, `patientId` é opcional e, quando presente, precisa apontar
para paciente ativo do mesmo tenant. A geração existente continua validando no servidor
os campos obrigatórios do modelo e passa a congelar paciente e representante no artefato e
em `patient_snapshot`. Alterações cadastrais posteriores não reescrevem a emissão.

O acervo em `GET .../patients/{patientId}/documents` pagina versões geradas por referência,
sem duplicar bytes. `documentStatus`, `reviewStatus` e `signatureStatus` são distintos. Sem
provedor operacional, assinatura retorna `not_available`.

## Roteiro de homologação

1. Autenticar usuário com senha trocada e selecionar uma organização ativa.
2. Criar paciente apenas com nome; editar contatos usando a `version` retornada.
3. Cadastrar identificador e confirmar conflito numa segunda inclusão simultânea.
4. Vincular representante com nome e relação, sem criar usuário.
5. Consultar somente modelos publicados no catálogo e criar uma minuta com `patientId`.
6. Preencher contrato e termo como minutas distintas, visualizar e gerar cada versão com
   chave idempotente própria.
7. Alterar o paciente e confirmar que `patient_snapshot` das versões não mudou.
8. Consultar o acervo, confirmar revisão separada e assinatura indisponível.
9. Inativar o paciente e confirmar que o acervo permanece; restaurar e validar conflito de
   identificador ativo.
10. Repetir URLs, corpos e consultas com usuário do tenant B: a operação deve ser negada
    ou não encontrada pela autorização e pela RLS.

## Estado da validação neste ambiente

- **PASSOU:** checksum local dos 23 blocos, snapshot v023 idêntico ao SQL canônico,
  `git diff --check` e build de assets.
- **BLOQUEADO:** build/testes .NET e PostgreSQL descartável; o SDK .NET 10 e runtime de
  containers não estão instalados nesta imagem e a tentativa de baixar o instalador foi
  recusada pelo servidor (HTTP 403).
- **NÃO EXECUTADO:** navegador responsivo e CI remoto do SHA final.
- **FALHOU:** nenhum teste executado retornou falha funcional; itens bloqueados não são
  apresentados como aprovados.

## Incremento 24 — validação e central de pacientes

- O controller retorna `ProblemDetails` diretamente nos conflitos, com os códigos estáveis
  `patient.identifier.duplicate` e `patient.version.conflict`; não há mais resultado HTTP
  serializado dentro de outro resultado.
- As regras da Application normalizam os textos e tipos, validam CPF (formato e dígitos),
  pares de identificador, representante, data de nascimento via relógio injetado e limites
  coerentes com o schema. O cadastro mínimo continua aceitando somente o nome.
- A Web ganhou lista paginada, estados vazios/erro, cadastro, edição otimista, ficha em
  seções, inativação/restauração, entrada no estúdio com paciente selecionado e acervo com
  estados documental, de revisão e de assinatura separados.
- A migração v024 congela a versão cadastral selecionada na minuta. A emissão é recusada
  quando o paciente foi alterado ou inativado durante a conferência; uma versão gerada
  continua com seu snapshot imutável e a idempotência existente.

A assinatura eletrônica permanece explicitamente indisponível enquanto não houver provedor
operacional. Este incremento não representa homologação dessa integração.

## Incremento 25 — conferência recuperável

A conferência reutiliza a minuta do estúdio e expõe, no backend, organização, paciente e
representante, modelo e versão, tipo documental, pendências explicáveis, permissão de
correção e próxima ação. O cadastro selecionado passa a ter um retrato próprio na minuta;
ele serve somente para comparação e não substitui valores manuais do documento.

Quando a versão do paciente muda, a geração continua bloqueada com
`patient.version.conflict`. Uma pessoa com `tenant.contract_drafts.manage` pode comparar
os campos relevantes e confirmar explicitamente a nova versão. A confirmação usa as
versões esperadas da minuta e do paciente, atualiza o retrato, incrementa a versão da
minuta e registra `patient.reconfirmed` no histórico. Repetir uma confirmação já aceita é
idempotente. Se paciente ou minuta mudarem durante a conferência, a API devolve conflito
e exige nova leitura.

A geração mantém a checagem dentro da transação e agora bloqueia também a linha do
paciente até o commit. A mesma chave de geração continua retornando a emissão existente;
se for reapresentada para outra versão da minuta, retorna
`idempotency.payload.conflict`. Versões já emitidas e seus `patient_snapshot` permanecem
imutáveis. A migration v025 adiciona apenas o retrato de seleção à minuta e uma função
canônica, sujeita a RLS, para montá-lo.

O acervo informa a próxima ação sem apresentar abertura de revisão a quem não possui
`tenant.reviews.request`. A assinatura permanece `not_available`: nenhuma entrega ou
assinatura é simulada.
