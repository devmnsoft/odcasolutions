# Próxima execução

## Fechar evidência do prompt 13

1. Com PostgreSQL 18 autorizado, criar/usar `odca` e `odca_test_local`, aplicar migrações até a versão 9 e executar:

```powershell
dotnet run --project src/Odca.Bootstrap -- init
$env:ODCA_NATIVE_ADMIN_CONNECTION = 'Host=localhost;Port=5432;Database=odca;Username=postgres;Password=<senha>;Include Error Detail=false'
dotnet run --project src/Odca.Bootstrap -- configure-native
Remove-Item Env:ODCA_NATIVE_ADMIN_CONNECTION
dotnet run --project src/Odca.Bootstrap -- migrate
dotnet run --project src/Odca.Bootstrap -- provision-test-access --environment Development
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
