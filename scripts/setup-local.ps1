[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$globalJsonPath = Join-Path $repositoryRoot 'global.json'
$runtimePath = Join-Path $repositoryRoot 'src\Odca.Api\development-runtime.json'

function Invoke-DotNetChecked {
    param([Parameter(Mandatory)][string[]]$Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') falhou com código $LASTEXITCODE. As etapas dependentes não foram executadas."
    }
}

Push-Location $repositoryRoot
try {
    $requiredSdk = (Get-Content -Raw $globalJsonPath | ConvertFrom-Json).sdk.version
    $installedSdks = @(& dotnet --list-sdks)
    if ($LASTEXITCODE -ne 0) { throw "dotnet --list-sdks falhou com código $LASTEXITCODE." }
    if (-not ($installedSdks | Where-Object { $_ -match ('^' + [regex]::Escape($requiredSdk) + '\s') })) {
        throw "SDK .NET $requiredSdk exigido por $globalJsonPath não está instalado."
    }

    Write-Host "Ambiente=Development; configuração=$runtimePath"
    Invoke-DotNetChecked @('run', '--project', 'src/Odca.Bootstrap', '--', 'init')
    Invoke-DotNetChecked @('run', '--project', 'src/Odca.Bootstrap', '--', 'repair')

    $securePassword = Read-Host 'Senha do PostgreSQL (não será exibida)' -AsSecureString
    $credential = New-Object System.Management.Automation.PSCredential('postgres', $securePassword)
    $password = $credential.GetNetworkCredential().Password
    if ([string]::IsNullOrWhiteSpace($password)) { throw 'A senha do PostgreSQL não foi informada.' }

    $connection = 'Host=localhost;Port=5432;Database=postgres;Username=postgres;Search Path=odca;Pooling=true;Maximum Pool Size=50;Minimum Pool Size=0;Timeout=30;Command Timeout=60;Application Name=odca.api;Include Error Detail=false'
    $env:ODCA_NATIVE_PASSWORD = $password
    $env:ODCA_NATIVE_ADMIN_CONNECTION = $connection
    $env:ODCA_NATIVE_APPLICATION_CONNECTION = $connection
    Invoke-DotNetChecked @('run', '--project', 'src/Odca.Bootstrap', '--', 'configure-native')
    Invoke-DotNetChecked @('run', '--project', 'src/Odca.Bootstrap', '--', 'diagnose', '--connection')

    Write-Host 'Configuração e conexão verificadas. Nenhuma migração ou seed foi executada.'
    Write-Host 'Próximos comandos (alteram o banco):'
    Write-Host '  dotnet run --project src/Odca.Bootstrap -- migrate'
    Write-Host '  dotnet run --project src/Odca.Bootstrap -- provision-test-access --environment Development --allow-postgres-development'
    Write-Host '  dotnet run --project src/Odca.Bootstrap -- show-login'
}
finally {
    Remove-Item Env:ODCA_NATIVE_ADMIN_CONNECTION -ErrorAction SilentlyContinue
    Remove-Item Env:ODCA_NATIVE_APPLICATION_CONNECTION -ErrorAction SilentlyContinue
    Remove-Item Env:ODCA_NATIVE_PASSWORD -ErrorAction SilentlyContinue
    if (Get-Variable password -ErrorAction SilentlyContinue) { $password = $null }
    Pop-Location
}
