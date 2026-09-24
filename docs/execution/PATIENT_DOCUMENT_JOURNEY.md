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
