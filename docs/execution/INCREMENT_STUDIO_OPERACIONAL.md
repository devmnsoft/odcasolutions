# Incremento operacional do Studio

Referência: `3a81140d22262f965ea1e04739e8e07aa2219266`. Não houve commit posterior a essa referência antes deste incremento. Nada foi publicado nem integrado.

## Preservado

A jornada já entregue permanece: login individual, paciente, modelo, minuta, conferência, PDF e preparação de assinatura. Preenchimento automático, bloqueio de módulo e auditoria global não foram recriados. Não há migration nova: `sourceProperty`, condições e a autoria da atualização cadastral ficam no JSON do modelo, nos valores da minuta e em `odca.contract_events`. O schema continua na versão 33. Migrations aplicadas e checksums não foram editados.

## Corrigido e evoluído

- Moeda e número no Studio separam o texto digitado, o valor canônico e o que é enviado. A formatação ocorre ao sair do campo, confirmar ou salvar. Entrada inválida ou incompleta impede o sucesso do salvamento e não regrava o valor anterior.
- A origem do modelo e a origem do valor aparecem separadas. Alterar um valor automático grava a origem `manual` e remove a confirmação.
- Conflito de modelo compara também a descrição. Conteúdo diferente não é tratado como sucesso e a revisão local não é substituída pela do servidor.
- O editor simples não converte um modelo cuja estrutura ele não preserva. A cópia editável cria outro rascunho e deixa o original intacto.
- O mapeamento aceita propriedade fechada de paciente, organização, representante e contratante, com identificadores legados. Condições rejeitam referência ausente, autorreferência e ciclo.
- A reconfirmação cadastral mostra as diferenças, atualiza só o que foi marcado, preserva valor manual e não altera versão já emitida.
- O rótulo “ORGANIZAÇÃO ATIVA” só aparece quando o catálogo autenticado confirma a situação. Suspensão, sessão recusada, acesso recusado e falha de comunicação têm textos distintos. Não há endpoint público de enumeração.

## Evidência reproduzida neste ciclo

- Compilação Release da solução: sucesso, 0 avisos e 0 erros, com `-m:1 -nodeReuse:false` depois de uma falha interna do MSBuild (`MSB1025`, nó inválido) numa execução paralela anterior.
- Baseline `dotnet test Odca.sln --configuration Release --no-build`: `Odca.Domain.Tests` 293 aprovados e `Odca.IntegrationTests` 153 aprovados, 0 falhas, no banco `odca_test_studio`.
- `npm run build` (`scripts/check-assets.mjs`): os exemplos `150,00`, `1.250,00`, `1250.00`, vazio, parcial, separador ambíguo, negativo e zero à esquerda passam no mesmo parser servido ao Studio. Um documento com título, campo, quebra, lista, tabela e negrito sobrevive à ida e volta. Lista aninhada, célula com dois blocos, alinhamento central e marcações combinadas não sobrevivem e entram na lista de perdas.
- `OrganizationFeatureGateTests.AdministrativeBlockStopsDirectPatientCallAndReleasePreservesTheRecord` está dentro dos 153. Com a organização suspensa, pacientes responde 401 e `GET .../features` responde 200 com `tenantStatus=suspended`.
- Instalação limpa e reaplicação: o fixture aplicou `database/odca.sql` duas vezes em `odca_test_studio`.
- Upgrade: `database/releases/odca-v030.sql` em `odca_test_v030_upgrade` (30 migrations), um tenant e `public.upgrade_sentinel` inseridos, migrador aplicado duas vezes. Resultado: 33 migrations, tenant e sentinela preservados, `odca.organization_feature_blocks` e `odca.contract_templates` presentes. Não havia linha de modelo antes do upgrade, então a preservação de um modelo populado não foi medida.
- Gitleaks 8.30.1, com redação, sobre os arquivos rastreados: nenhum vazamento. Um segredo inesperado acrescentado só numa cópia temporária de `TestAccessProvisioner.cs` foi detectado pela regra `github-pat` na linha 282. A cópia e o relatório foram apagados. O arquivo do repositório não recebeu esse segredo.

## Homologado no navegador

Microsoft Edge, sem interface, contra `odca_test_browser`, com API em `https://127.0.0.1:7143` e Web em `https://localhost:7144`. Cliente individual da organização ativa. Minuta `Minuta Homologacao 738511`, campo `Valor da sessao`.

- Login chegou à caixa da organização. O rótulo confirmado foi `ORGANIZAÇÃO ATIVA`.
- A digitação de `1.` permaneceu `1.` e `1.250,00` permaneceu `1.250,00` até sair do campo. O salvamento seguinte respondeu 200.
- `abc` exibiu “Informe apenas o valor numérico.” junto ao campo. Não houve PUT nem no blur nem no botão salvar. O texto `abc` continuou no campo.
- `-10,00` e `1.250` exibiram, respectivamente, moeda negativa recusada e valor ambíguo.
- Colagem de `1250.00` normalizou para `1.250,00` ao sair do campo.
- Com o PUT interrompido, o estado foi “Falha de comunicação. Nada foi confirmado como salvo.” O salvamento seguinte gravou `150,00`.
- Duas abas: a primeira gravou `200,00`; a segunda, com `300,00`, mostrou conflito e o painel de conflito. A minuta foi restaurada para `150.00`, origem manual e confirmação verdadeira.
- Em 360, 768 e 1440 px, o botão salvar, o campo e o papel do documento permaneceram visíveis.
- A página de edição do rascunho `Minuta Homologacao Jornada` abriu, serviu o script do editor e não mostrou faixa de perda, porque esse modelo passa na ida e volta. A ajuda “Como usar” estava presente.

## Não homologado

Não foram executados no navegador: cadastro e busca de paciente, configuração visual de mapeamento e condição, atualização seletiva do snapshot, conferência, revisão, PDF, clique repetido, conflito com payload idêntico, alteração só da descrição, bloqueio da conversão numa minuta real com tabela ou lista aninhada, cópia editável, superadministrador (módulo, suspensão, isolamento de dois vínculos, auditoria) e falha de catálogo. A restrição de uma organização para usuário com duas organizações não foi reexecutada na interface. A assinatura externa continua `not_available`, com `IntegrationAvailable` falso e sem provedor ou sandbox configurado. Envio externo não está concluído. Nada foi publicado nem integrado.
