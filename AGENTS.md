# Acompanhamento rastreável do MVP ODCA

Estas instruções tratam somente do relatório de progresso do portal; preserve as demais regras e arquitetura do projeto.

- Ao concluir uma entrega, mantenha `docs/execution/STATUS.md` e o relatório da entrega com nomes compreensíveis das funcionalidades, cenários, comandos, resultados e limitações. Separe teste local, CI e aceite humano.
- Consulte `.odca/progress.json`. O portal parte de aproximadamente 40%, referência histórica do briefing, e só acrescenta os pesos dos seis marcos inteiramente concluídos com evidência. Não altere baseline, IDs ou pesos para aumentar o índice.
- Não marque um marco inteiro como concluído por corrigir um arquivo ou aprovar uma suíte geral. Confira todos os critérios e obtenha o aceite real pertinente. Nunca invente responsável, aprovação, teste ou log.
- Para creditar um marco: inclua a evidência de aceite em `docs/execution/*.md` no commit funcional; após a CI oficial de push desse SHA terminar com sucesso, registre `status=completed`, `completed_commit`, `ci_run_id`, `acceptance.approved=true`, `approved_by` e `evidence_paths` no manifesto em uma atualização posterior. As evidências devem existir no SHA testado, que precisa continuar ancestral da branch. O job `build-test` e seu passo `Tests` precisam passar; etapas omitidas não contam.
- Se não houver execução CI válida ou aprovação exigida, preserve o marco pendente e explique o impedimento. Se uma regressão invalidar um marco, registre `status=reopened` para retirar seu crédito até nova comprovação.
- A identificação automática de áreas por arquivos não é prova de função concluída nem de cobertura individual. Não confunda testes alterados, testes locais declarados e suíte executada no GitHub.
- Preserve segredos e dados pessoais. Use dados sintéticos nos testes e não inclua credenciais nos relatórios.
