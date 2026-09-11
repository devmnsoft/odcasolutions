[CmdletBinding()]
param(
    [switch]$ImportLegacy,
    [switch]$NonInteractive
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$defaultRuntimePath = Join-Path $repositoryRoot 'src\Odca.Api\development-runtime.json'

function Resolve-RuntimePath {
    $configured = [Environment]::GetEnvironmentVariable('ODCA_RUNTIME_CONFIG')
    if ($null -ne $configured -and [string]::IsNullOrWhiteSpace($configured)) {
        throw 'ODCA_RUNTIME_CONFIG foi definida com um caminho vazio.'
    }

    # Relative overrides deliberately follow the process working directory, just like Path.GetFullPath in .NET.
    return [IO.Path]::GetFullPath($(if ($null -eq $configured) { $defaultRuntimePath } else { $configured }))
}

function Get-MissingRuntimeFields {
    param([Parameter(Mandatory)]$Configuration)

    $missing = New-Object System.Collections.Generic.List[string]
    if ($null -eq $Configuration.ConnectionStrings -or
        [string]::IsNullOrWhiteSpace([string]$Configuration.ConnectionStrings.Database)) {
        $missing.Add('ConnectionStrings:Database')
    }
    if ($null -eq $Configuration.ConnectionStrings -or
        [string]::IsNullOrWhiteSpace([string]$Configuration.ConnectionStrings.DatabaseAdmin)) {
        $missing.Add('ConnectionStrings:DatabaseAdmin')
    }
    if ($null -eq $Configuration.Jwt -or [string]::IsNullOrWhiteSpace([string]$Configuration.Jwt.Issuer)) { $missing.Add('Jwt:Issuer') }
    if ($null -eq $Configuration.Jwt -or [string]::IsNullOrWhiteSpace([string]$Configuration.Jwt.Audience)) { $missing.Add('Jwt:Audience') }
    if ($null -eq $Configuration.Jwt -or $null -eq $Configuration.Jwt.AccessTokenMinutes -or
        [int]$Configuration.Jwt.AccessTokenMinutes -lt 5 -or [int]$Configuration.Jwt.AccessTokenMinutes -gt 30) {
        $missing.Add('Jwt:AccessTokenMinutes (deve estar entre 5 e 30)')
    }
    if ($null -eq $Configuration.Jwt -or [string]::IsNullOrWhiteSpace([string]$Configuration.Jwt.SigningKey)) {
        $missing.Add('Jwt:SigningKey')
    }
    elseif ([Text.Encoding]::UTF8.GetByteCount([string]$Configuration.Jwt.SigningKey) -lt 32) {
        $missing.Add('Jwt:SigningKey (mínimo de 32 bytes)')
    }
    if ($null -eq $Configuration.Security -or $null -eq $Configuration.Security.AllowDevelopmentBootstrap) {
        $missing.Add('Security:AllowDevelopmentBootstrap')
    }
    if ($null -eq $Configuration.Security -or $null -eq $Configuration.Security.MfaRequiredForSuperAdmin) {
        $missing.Add('Security:MfaRequiredForSuperAdmin')
    }
    if ($null -eq $Configuration.DataProtection -or
        [string]::IsNullOrWhiteSpace([string]$Configuration.DataProtection.KeysPath)) {
        $missing.Add('DataProtection:KeysPath')
    }
    if ($null -eq $Configuration.DataProtection -or
        [string]::IsNullOrWhiteSpace([string]$Configuration.DataProtection.ApplicationName)) {
        $missing.Add('DataProtection:ApplicationName')
    }
    return $missing
}

function Test-RuntimeFile {
    param([Parameter(Mandatory)][string]$Path)

    try {
        $configuration = Get-Content -Raw -LiteralPath $Path | ConvertFrom-Json
    }
    catch {
        throw "JSON inválido em '$Path'. O arquivo foi preservado; corrija-o explicitamente."
    }
    if ($null -eq $configuration -or $configuration -isnot [psobject]) {
        throw "A raiz de '$Path' deve ser um objeto JSON. O arquivo foi preservado."
    }

    $missing = @(Get-MissingRuntimeFields $configuration)
    if ($missing.Count -ne 0) {
        Write-Host 'Configuração existente preservada, mas há campos ausentes ou inválidos:'
        foreach ($field in $missing) { Write-Host " - $field" }
        throw "Repare '$Path' explicitamente; chaves e propriedades válidas não foram regeneradas."
    }
    Write-Host 'Configuração válida; conexão não testada.'
}

function Read-PostgresPassword {
    if ($NonInteractive -or -not [Environment]::UserInteractive) {
        throw 'A configuração está ausente e a senha não pode ser solicitada em modo não interativo. Execute scripts\setup-local.ps1 em um terminal interativo.'
    }
    $secure = Read-Host 'Senha do PostgreSQL (não será exibida)' -AsSecureString
    $credential = New-Object System.Management.Automation.PSCredential('postgres', $secure)
    $plain = $credential.GetNetworkCredential().Password
    if ([string]::IsNullOrWhiteSpace($plain)) { throw 'A senha do PostgreSQL não foi informada.' }
    return $plain
}

function New-ConnectionString {
    param([Parameter(Mandatory)][string]$Password)
    $builder = New-Object System.Data.Common.DbConnectionStringBuilder
    $builder['Host'] = 'localhost'
    $builder['Port'] = 5432
    $builder['Database'] = 'postgres'
    $builder['Username'] = 'postgres'
    $builder['Password'] = $Password
    $builder['Pooling'] = $true
    $builder['Maximum Pool Size'] = 50
    $builder['Minimum Pool Size'] = 0
    $builder['Timeout'] = 30
    $builder['Command Timeout'] = 60
    $builder['Search Path'] = 'odca'
    $builder['Application Name'] = 'odca.api'
    return $builder.ConnectionString
}

function Write-NewFileAtomic {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][byte[]]$Bytes)
    $directory = Split-Path -Parent $Path
    [IO.Directory]::CreateDirectory($directory) | Out-Null
    $temporary = Join-Path $directory ('.development-runtime-' + [Guid]::NewGuid().ToString('N') + '.tmp')
    try {
        [IO.File]::WriteAllBytes($temporary, $Bytes)
        try {
            [IO.File]::Move($temporary, $Path)
        }
        catch [IO.IOException] {
            if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw }
            Write-Host 'Outra execução concluiu a criação primeiro; o arquivo vencedor será preservado.'
        }
    }
    finally {
        if (Test-Path -LiteralPath $temporary) { Remove-Item -Force -LiteralPath $temporary }
    }
}

$runtimePath = Resolve-RuntimePath
Write-Host "Ambiente=Development; configuração=$runtimePath"
if (Test-Path -LiteralPath $runtimePath) {
    if (-not (Test-Path -LiteralPath $runtimePath -PathType Leaf)) { throw "O destino '$runtimePath' não é um arquivo." }
    Test-RuntimeFile $runtimePath
    Write-Host 'Arquivo existente não foi alterado.'
    return
}

$legacyRuntime = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'ODCA Solutions\development-runtime.json'
if (Test-Path -LiteralPath $legacyRuntime -PathType Leaf) {
    $useLegacy = $ImportLegacy
    if (-not $useLegacy -and -not $NonInteractive -and [Environment]::UserInteractive) {
        $answer = Read-Host "Configuração antiga encontrada em '$legacyRuntime'. Importar sem alterar o original? [s/N]"
        $useLegacy = $answer -match '^(s|sim|y|yes)$'
    }
    if ($useLegacy) {
        Test-RuntimeFile $legacyRuntime
        Write-NewFileAtomic $runtimePath ([IO.File]::ReadAllBytes($legacyRuntime))
        Test-RuntimeFile $runtimePath
        Write-Host "Configuração importada para '$runtimePath'; o original foi preservado."
        return
    }
    Write-Host 'Importação não realizada; uma configuração nova será criada.'
}

$password = Read-PostgresPassword
try {
    $connection = New-ConnectionString $password
    $keyBytes = New-Object byte[] 48
    $random = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $random.GetBytes($keyBytes) } finally { $random.Dispose() }
    $keysPath = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'ODCA Solutions\data-protection-keys'
    $runtime = [ordered]@{
        ConnectionStrings = [ordered]@{ Database = $connection; DatabaseAdmin = $connection }
        Jwt = [ordered]@{ Issuer = 'odca-api'; Audience = 'odca-bff'; AccessTokenMinutes = 15; SigningKey = [Convert]::ToBase64String($keyBytes) }
        Security = [ordered]@{ AllowDevelopmentBootstrap = $true; MfaRequiredForSuperAdmin = $true }
        DataProtection = [ordered]@{ KeysPath = $keysPath; ApplicationName = 'ODCA Solutions' }
    }
    $json = $runtime | ConvertTo-Json -Depth 10
    $utf8 = New-Object System.Text.UTF8Encoding($false)
    Write-NewFileAtomic $runtimePath ($utf8.GetBytes($json + [Environment]::NewLine))
}
finally {
    $password = $null
    $connection = $null
}

Test-RuntimeFile $runtimePath
Write-Host "Configuração criada em '$runtimePath'. Nenhuma conexão, migration, seed, senha de banco ou usuário foi alterado."
