[CmdletBinding()]
param(
    [switch]$TestConnection
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$globalJsonPath = Join-Path $repositoryRoot 'global.json'
$defaultRuntimePath = Join-Path $repositoryRoot 'src\Odca.Api\development-runtime.json'

function Invoke-DotNetChecked {
    param([Parameter(Mandatory)][string[]]$Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') falhou com código $LASTEXITCODE. As etapas dependentes não foram executadas."
    }
}

Push-Location $repositoryRoot
try {
    $requiredSdk = (Get-Content -Raw -LiteralPath $globalJsonPath | ConvertFrom-Json).sdk.version
    $installedSdks = @(& dotnet --list-sdks)
    if ($LASTEXITCODE -ne 0) { throw "dotnet --list-sdks falhou com código $LASTEXITCODE." }
    if (-not ($installedSdks | Where-Object { $_ -match ('^' + [regex]::Escape($requiredSdk) + '\s') })) {
        throw "SDK .NET $requiredSdk exigido por $globalJsonPath não está instalado."
    }

    $runtimePath = $env:ODCA_RUNTIME_CONFIG
    if ($null -ne $runtimePath -and [string]::IsNullOrWhiteSpace($runtimePath)) {
        throw 'ODCA_RUNTIME_CONFIG foi definida com um caminho vazio.'
    }
    if ([string]::IsNullOrWhiteSpace($runtimePath)) { $runtimePath = $defaultRuntimePath }
    $runtimePath = [IO.Path]::GetFullPath($runtimePath)
    Write-Host "Ambiente=Development; configuração=$runtimePath"

    if (Test-Path -LiteralPath $runtimePath) {
        Write-Host 'Configuração existente encontrada; conexões e propriedades conhecidas serão preservadas.'
        Invoke-DotNetChecked @('run', '--project', 'src/Odca.Bootstrap', '--', 'repair')
    }
    else {
        $legacyRuntime = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'ODCA Solutions\development-runtime.json'
        if (Test-Path -LiteralPath $legacyRuntime) {
            Write-Host "Importando configuração anterior sem alterar o original: $legacyRuntime"
            Invoke-DotNetChecked @('run', '--project', 'src/Odca.Bootstrap', '--', 'init', '--postgres-development')
        }
        else {
            $securePassword = Read-Host 'Senha do PostgreSQL (não será exibida)' -AsSecureString
            $credential = New-Object System.Management.Automation.PSCredential('postgres', $securePassword)
            $password = $credential.GetNetworkCredential().Password
            if ([string]::IsNullOrWhiteSpace($password)) { throw 'A senha do PostgreSQL não foi informada.' }
            $env:ODCA_LOCAL_POSTGRES_PASSWORD = $password
            Invoke-DotNetChecked @('run', '--project', 'src/Odca.Bootstrap', '--', 'init', '--postgres-development')
        }
    }

    Invoke-DotNetChecked @('run', '--project', 'src/Odca.Bootstrap', '--', 'diagnose')
    if ($TestConnection) {
        Invoke-DotNetChecked @('run', '--project', 'src/Odca.Bootstrap', '--', 'diagnose', '--connection')
    }

    Write-Host 'Configuração validada. Nenhuma migração, seed ou alteração de senha foi executada.'
    Write-Host 'Próximos comandos explícitos: diagnose --connection; migrate; provision-test-access; run-local.ps1.'
}
finally {
    Remove-Item Env:ODCA_LOCAL_POSTGRES_PASSWORD -ErrorAction SilentlyContinue
    if (Get-Variable password -ErrorAction SilentlyContinue) { $password = $null }
    Pop-Location
}
