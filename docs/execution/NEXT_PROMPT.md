# Próxima execução

## Continuação obrigatória do estúdio

Usar `StructuredContractDocument` como única fonte canônica e não criar outro modelo paralelo. A próxima fatia deve persistir o agregado com tenant/RLS e permissões, expor autosave com revisão esperada e integrar o BFF; depois implementar publicação e PDF por versão. Antes de promover, instalar o SDK fixado e executar restore/build/testes, PostgreSQL descartável e jornada autenticada.

## Pré-condição para continuar S02

Partir da migração v011 e reconciliar o commit `183aa2b5153cf7dbfa475c65fcdeb1c36f0f911b` caso ele se torne acessível, sem substituir versões ou decisões já persistidas. Executar primeiro `dotnet restore Odca.sln --locked-mode`, `dotnet build Odca.sln -c Release --no-restore`, `npm run build` e a suíte PostgreSQL descartável abaixo; em seguida concluir decisão/aplicação transacional e a tela de revisão. O incremento posterior é o editor estruturado, biblioteca de cláusulas e campos preenchíveis, reutilizando documento lógico, versões e proveniência v011.

## Fechar evidência do prompt 13

1. Com PostgreSQL 18 autorizado, configurar a base `postgres`/schema `odca` sem alterar o banco durante o setup:

```powershell
.\scripts\setup-local.ps1
```

Depois, em uma etapa explícita que altera o banco:

```powershell
dotnet run --project src/Odca.Bootstrap -- migrate
dotnet run --project src/Odca.Bootstrap -- provision-test-access --environment Development --allow-postgres-development
dotnet run --project src/Odca.Bootstrap -- show-login
```

2. Suíte descartável:

```powershell
$env:ODCA_TEST_ENVIRONMENT = 'ODCA_INTEGRATION_TESTS'
$env:ODCA_TEST_ADMIN_CONNECTION = 'Host=localhost;Port=5432;Database=odca_test_local;Username=postgres;Password=<senha-admin>;Include Error Detail=false'
$env:ODCA_TEST_APP_CONNECTION = 'Host=localhost;Port=5432;Database=odca_test_local;Username=odca_test_app_login;Password=<senha-exclusiva-de-teste>;Include Error Detail=false'
dotnet test tests/Odca.IntegrationTests --configuration Release
```

3. Jornada E2E: administrador autentica → escolhe organização → cria perfil permitido → convida → verifica fila local → convidado aceita → administrador bloqueia → usuário perde acesso → auditoria registra. Usar duas organizações para tentativa cruzada. Capturas 360/768/1280/1440.

## Próxima fatia vertical — contratos (S02)

Cadastro manual de tipo e contraparte → contrato com vigência → listagem/detalhes → permissões → histórico → alerta interno.

Depois: upload seguro e revisão de extração; editor e versões; assinatura e créditos; integrações de comunicação e cobrança. Não implementar vários módulos pela metade.
## Próximo incremento: concluir a jornada de documentos no BFF

Parta do controller corrigido e da migration v011 sem duplicá-los. Primeiro execute o gate .NET/PostgreSQL descartável e os cenários de upload, quota, isolamento e compensação. Depois implemente idempotência/reservas expiradas/reconciliação e inativação auditada em uma migration nova e aditiva. Só então conecte a API existente a uma ficha responsiva no BFF (documentos, visualização segura, revisão e histórico), mantendo o editor estruturado como integração separada. Registre capturas reais em 360, 768, 1280 e 1440 px e zoom 200%; não apresente scanner, extração, dados do dashboard ou progresso simulados.
