# Prompt — ficha, agenda, inbox, contexto e templates

Repositório: https://github.com/devmnsoft/odcasolutions
Base: codex/s00-foundation após merge dos PRs #62 e #63
Branch: feat/contract-sheet-agenda-inbox
Pacote: copiar os arquivos desta pasta. Ver INSTALL.md e ANALISE_E_PROMPT.md.

## Objetivo

Jornada autenticada da caixa operacional + agenda do mês + ficha do contrato, com regras de visibilidade/aditivo e recomendação da minuta oficial correta. Sem tabela de tarefas. Sem parecer jurídico.

## Fazer

1. Aplicar os arquivos deste pacote nos mesmos caminhos.
2. Registrar services.AddOdcaOperationalInbox() no DI da Infrastructure.
3. Colar os métodos de OdcaApiClient.Operations.fragment.cs no cliente BFF.
4. Manter OperationalInbox / MonthlyAgendaWindow / OfficialContractTemplates existentes.
5. ContractWorkspacePolicy decide aditivo vs minuta primária e se a biblioteca oficial pode ser instalada.
6. Testes do serviço e da policy. Restore bloqueado + build Release quando o SDK existir.

Development: admin@odca.local / OdcaAdmin#2026Local.
