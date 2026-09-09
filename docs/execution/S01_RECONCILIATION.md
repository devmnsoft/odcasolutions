# Reconciliação S00/S01 — 09/09/2026

Referência examinada: branch de trabalho no SHA base `5ab8df3b8585101bae5d2068255c58e74f01ae81`.
Esta matriz distingue implementação, teste escrito e evidência efetivamente executada; nenhum serviço externo foi validado.

| Requisito | Implementação encontrada | Evidência executada | Lacuna | Ação desta evolução |
|---|---|---|---|---|
| Inicialização da API em teste | A fixture injeta conexão e JWT com `ConfigureAppConfiguration` | CI 34350222079 chegou à suíte PostgreSQL, mas 1 fluxo falhou antes das asserções HTTP | `Program` e Infrastructure liam configuração antes da composição do test host | Leituras adiadas para options/factories e validação no início do host; testes de validação adicionados |
| PostgreSQL isolado | Marcador, banco `odca_test*` e role exclusiva são obrigatórios | 8 testes de integração aprovados na CI citada, incluindo cenários PostgreSQL | SQL direto, concorrência e recuperação não comprovados | Preservadas as proteções; cenários avançados permanecem no backlog |
| MFA | Flag de configuração é exigida fora de Development | Nenhum fluxo de segundo fator aprovado | Não existe inscrição, desafio, recuperação nem nível MFA na sessão | Pendente; acesso após somente senha não representa o aceite final de S01 |
| Onboarding, assinatura e equipe | Somente catálogo/versionamento de planos | Nenhum fluxo comercial aprovado | Organização, membership, confirmação de e-mail, assinatura e convites ausentes | Pendente; não foi simulado pagamento ou ativação |
| Privacidade | Entrada pública, protocolo opaco e evento inicial | Testes unitários aprovados; fluxo HTTP ficou bloqueado pela inicialização na CI citada | Identidade, triagem, exportação e conclusão controlada ausentes | Correção de host desbloqueia a execução; operação permanece pendente |
| BFF distribuído | Cookie protegido com ticket em `IDistributedCache` | Build da CI aprovado | Implementação usa memória e Data Protection não compartilhado | Pendente para produção multi-instância |

## Incremento sobre `c5e5064` — matriz verificável

| Requisito | Implementação deste incremento | Teste/check executado | Próximo passo |
|---|---|---|---|
| Restore bloqueado | `coverlet.collector` de IntegrationTests alinhado em 10.0.1 com DomainTests e com o lock já gerado | coerência de csproj/lock inspecionada; execução .NET local indisponível | confirmar `dotnet restore --locked-mode` na CI do commit |
| Inicialização real | hosted service separado; `WebApplicationFactory` inicia a aplicação e consulta `/health/live` | teste escrito para host válido e seis configurações Production inválidas | executar na CI e preservar logs/TRX |
| Cenários HTTP legíveis | fluxo agregado separado em privacidade, login inválido, troca inicial, dashboard/planos e logout/revogação | `npm run build` aprovado; suíte .NET não executada neste ambiente | executar contra PostgreSQL descartável com role restrita |
| MFA e acesso administrativo | nenhuma implementação funcional adicionada | nenhuma evidência de segundo fator | implementar Marco B antes de considerar dashboard administrativo aceito |
| Onboarding, equipe e privacidade operacional | nenhuma implementação funcional adicionada | nenhuma evidência nova | executar Marcos C–E na ordem do backlog |

Consulta remota da PR/CI não foi autenticada pelo ambiente local (`gh auth status` sem credencial e API retornando 401). A situação da CI acima registra o log fornecido para o SHA `c5e5064`; o resultado do novo commit deve substituí-la quando disponível.

O escopo amplo de S01 não é declarado concluído. O incremento atual fecha o bloqueio reproduzido de composição do host e mantém as demais lacunas explícitas para não confundir código existente com controle aprovado.

## Incremento local sobre `468dee4` — MFA e primeiro cliente

| Requisito | Implementação desta atualização | Teste/check executado | Pendência |
|---|---|---|---|
| CA1859 do gate A | `StartupValidationTests.Environment` retorna `TestHostEnvironment` | restore bloqueado e build Release aprovados | confirmar CI do commit final |
| MFA de superadministrador | TOTP, inscrição, confirmação, desafio, recovery codes hashados, replay por time-step, limite de tentativas e claim `mfa_verified` | testes HTTP escritos em `S00FlowTests`; testes sem DB aprovados; cenários DB aguardam banco descartável | reset/revogação administrativa de autenticador e MFA recente para futuros endpoints sensíveis |
| Dashboard administrativo | policy `PlatformAdministrator` exige `must_change_password=false` e `mfa_verified=true` | build aprovado; teste DB preparado para negar senha sem MFA e autorizar MFA válido | executar contra PostgreSQL |
| Cadastro do primeiro cliente | registro idempotente com CPF/CNPJ, responsável, plano vigente, termos/aviso, token hashado, tenant, membership e subscription `commercial_pending` | teste HTTP DB preparado para cadastro idempotente, confirmação uso único, login e home do cliente | provedor real de e-mail, reenvio com quota temporal refinada, seleção explícita entre múltiplas organizações |
| Banco | migrações 004/005 e snapshots `odca-v004.sql`/`odca-v005.sql` | checksums/snapshots aprovados na suíte sem DB | vazio/upgrade/reaplicação/concurrency/recovery em PostgreSQL real |

S01 permanece parcial: equipe, convites, suporte temporário, atendimento autenticado de privacidade, operação comercial real, cobrança e cache/Data Protection compartilhados para múltiplas instâncias continuam no backlog.

## Reconciliação do prompt 08 — 09/09/2026

A referência `a9077f6` continha MFA e onboarding, mas três leituras/testes estavam quebrados. Este incremento corrige esses contratos, adiciona renovação persistida de sessão MFA, proteção concorrente da inscrição, validação de documentos e migração 006 de unicidade/evidência. Código de equipe, convites e seletor ainda não existe; outbox continua sem transporte de produção. Portanto S01 permanece parcial e nenhuma alegação de produção/multi-instância foi feita.
