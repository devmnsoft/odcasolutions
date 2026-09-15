# Central de Renovações e Aditivos — v017

## Diagnóstico da base e ponto de partida

- Branch de trabalho: `work`; commit inicial inspecionado: `57447d8`.
- Contratos, documentos imutáveis e extração assistida já existiam desde v011. Resultados de OCR continuam sugestões e não definem política jurídica.
- A revisão interna sequencial existente (v012), a central de obrigações e seus lembretes (v013) e o Estúdio/versionamento (v015–v016) foram preservados; esta entrega os referencia por chave em vez de duplicá-los.
- A correção de `CS0819` no `ContractStudioController` já estava no commit inicial. O controller usa tipos explícitos nos locais em que o inicializador não permite inferência.
- `OrganizationAccess` já é materializado por uma linha Dapper intermediária que converte o array; `OverviewMetrics` usa aliases/tipos explícitos. Os testes de integração existentes permanecem como regressão desses mapeamentos.
- Login, contas de desenvolvimento, runtime local, senhas e provisionamento não foram alterados.

## Implementado

- Migration aditiva v017 e snapshot: política explícita (`not_defined` nunca significa renovação automática), dias corridos ou meses de calendário, responsável, contraparte e tipo; processos estruturados, eventos e aplicações com RLS, FKs de tenant, índices e permissões separadas.
- Central paginada no servidor, com a mesma consulta/filtros para lista e indicadores, janela por `AddMonths(3)`, vencimento separado do prazo de aviso, filtros compactos, estados vazios e layout responsivo.
- Uma alteração ativa por contrato. Chave de idempotência impede reenvio; a proposta captura a versão e valores atuais sem modificar o contrato.
- Formalização manual exige permissão, justificativa, data e uma versão documental `safe` do mesmo tenant/contrato. A interface a identifica como registro manual, não assinatura digital.
- Aplicação valida novamente estado e versão. Efeito futuro fica agendado; o Worker aplica uma única vez e registra antes/depois. Divergência vira conflito, sem sobrescrita. Datas retroativas são rejeitadas.
- A rota legada não aceita mais `renewed` com alteração imediata de vigência: direciona para o fluxo formalizado.
- Obrigações existentes não são editadas nem apagadas durante a aplicação. A resposta explicita essa preservação.

## Fluxo de uso

1. Abra **Renovações e aditivos** no menu da organização e filtre o período, situação ou responsabilidade.
2. Na ficha/integração consumidora, crie `POST /api/v1/organizations/{tenant}/renewals/contracts/{contract}` com UUID idempotente, versão observada e alteração proposta.
3. Gere uma minuta no Estúdio e encaminhe a versão imutável ao mecanismo de revisão já existente; mantenha os IDs no processo.
4. Após aprovação interna, carregue a evidência pelo upload seguro existente. Somente após estado `safe`, registre a formalização manual.
5. Aplique alterações com efeito atual pelo endpoint `/apply`; efeitos futuros são processados pelo `RenewalApplicationWorker` segundo o fuso da organização.
6. Confira o histórico de aplicação e mantenha correções como novas operações documentadas.

## Validado nesta entrega

- Regras unitárias de meses de calendário (inclusive fevereiro bissexto), diferença entre comunicação e vencimento e contrato sem término.
- Checksum do SQL, consistência do snapshot e verificações estáticas de assets foram preparados para a suíte existente.
- Os mapeamentos Dapper citados foram inspecionados no código e continuam cobertos pelos testes `OrganizationAccessMaterializationTests` e `OrganizationOverviewTests`.

## Pendente / dependências externas

- Não há provedor operacional de assinatura digital. Por isso somente o registro manual, explicitamente rotulado, foi implementado.
- O formulário completo da ficha contratual, seleção de modelo e ligação visual minuta → revisão exigem a evolução da ficha HTTP, ainda ausente na base. As FKs para minuta, versão gerada e revisão já estão disponíveis.
- Prévia/ajuste confirmado de obrigações futuras e invalidação histórica de alertas obsoletos precisam de regras de recorrência específicas do produto. A aplicação atual preserva todas as obrigações e nunca altera as concluídas.
- Não há calendário de dias úteis configurado; a política aceita somente dias corridos e meses de calendário.
- Entrega por e-mail/WhatsApp depende de transporte real configurado. O Worker existente continua sem anunciar entrega quando há somente tarefa interna.
- QA autenticado em navegador e testes PostgreSQL exigem SDK .NET 10.0.400, Node 24 e banco descartável/role restrita conforme o README.
