# Auditoria do ciclo de importação — 22/09/2026

## Estado encontrado

| Área | Classificação | Evidência no repositório |
|---|---|---|
| Login, MFA e sessão | Funcional por inspeção; runtime não verificado | `AuthController`, ticket store do BFF e testes de autenticação preservados. |
| Organização, equipe, planos e permissões | Funcional por inspeção; runtime não verificado | contexto explícito, memberships e permissões consultadas no servidor. |
| Documentos privados | Funcional por inspeção; runtime não verificado | upload com assinatura, cota, antivírus, armazenamento privado e download autorizado. |
| Importação com extração | Parcial | rastreamento e revisão existiam, mas confirmação exigia `extraction_job` mesmo quando a tela afirmava aceitar conferência manual. |
| Importação sem extração | Quebrada | não havia comando para persistir os dados manuais e o `INNER JOIN` de confirmação impedia concluir. |
| Confirmação idempotente | Funcional por inspeção | bloqueio `FOR UPDATE`, estado confirmado e retorno do contrato já associado. |
| Revisão de consultoria | Funcional por inspeção; runtime não verificado | fila canônica e versão específica existentes; não foi criada fila paralela. |
| Obrigações e renovações | Funcional por inspeção; runtime não verificado | cadastro contextual, histórico, concorrência e parâmetros tipados existentes. |
| Carteira consolidada | Parcial | ficha, inbox, obrigações, renovações e vistas salvas existem; uma listagem única com todos os filtros solicitados ainda não foi localizada. |
| Aditivos e encerramento | Parcial | propostas de aditivo ligadas à revisão existem; jornada integral de encerramento não foi homologada. |
| Relatórios | Não verificado | não houve execução de runtime neste contêiner. |

## Causa confirmada e correção

A importação reutiliza uma versão documental vinculada ao contrato provisório. A API
modelava `extraction_job_id` como opcional, mas `Confirm` fazia `INNER JOIN` no job e
exigia sempre `ready_for_review`. Além disso, a tela não enviava os campos preenchidos
manualmente. Assim, ausência de OCR tornava a jornada inconclusiva.

O ciclo agora possui comando autenticado de conferência manual com validação de título,
datas, valor/moeda, tenant, permissão, antivírus e versão otimista. A alteração do
contrato e o evento da importação são atômicos. A confirmação aceita ausência real de
extração, continua exigindo que qualquer extração existente esteja pronta e mantém a
idempotência e o bloqueio concorrente já adotados. A conclusão abre a ficha canônica.

## Roteiro executável de homologação

1. Preparar PostgreSQL 18 descartável, aplicar migrations e executar o seed duas vezes.
2. Entrar como administrador do tenant, concluir senha inicial/MFA e enviar PDF válido.
3. Após estado `safe`, registrar a importação sem solicitar extração, preencher os dados,
   salvar, revisar o resumo e confirmar; repetir o POST e comprovar o mesmo contrato.
4. Repetir com extração e resolver todas as sugestões antes de confirmar.
5. Em duas sessões, salvar a mesma revisão e comprovar conflito na segunda; concorrer
   cancelamento e confirmação e comprovar um único estado terminal.
6. Abrir a ficha, solicitar revisão, cadastrar e concluir obrigação e consultar histórico.
7. Buscar o contrato nas centrais disponíveis e salvar uma vista de obrigações.
8. Como usuário operacional sem `tenant.imports.confirm`, comprovar `403` ao confirmar.
9. Como consultor, comprovar somente o acesso permitido à solicitação atribuída.
10. Como usuário de outro tenant, trocar IDs de importação, documento e contrato e
    comprovar `403/404` sem metadados do primeiro tenant.
11. Como superadministrador, conferir administração autorizada e eventos, sem
    impersonação implícita.

## Evidências e limites desta execução

`npm run build` e `git diff --check` foram aprovados. O contêiner não possui o SDK
.NET, Docker nem `psql`; portanto build/testes .NET, migrations, seed, PostgreSQL real,
login HTTP, concorrência e capturas em 360/768/1440 px estão bloqueados e não são
declarados homologados. A carteira consolidada permanece uma pendência explícita.
