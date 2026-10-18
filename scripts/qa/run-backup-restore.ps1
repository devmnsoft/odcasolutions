# D-OC44 (Sprint 10/F-1): ensaio real de backup/restauração sobre o PostgreSQL local.
# Executa pg_dump (formato custom/comprimido) do banco de prova, restaura para um banco
# novo e compara métricas de integridade entre os dois (versão de migração, contagens de
# tabelas-chave, fingerprints md5 por id ordenado e visibilidade RLS como odca_app sem
# contexto de tenant). O dump em si fica apenas em TEMP (binário) — salvo com
# -KeepDumpDir, que preserva uma cópia com carimbo de data/hora fora do repositório
# (procedimento de backup pré-piloto do D-OC46/F-2). O transcript versionado fica em
# evidence/d44-backup-restore-rodada-final.txt.
# Uso: powershell -NoProfile -ExecutionPolicy Bypass -File scripts/qa/run-backup-restore.ps1 [-SourceDatabase <db>] [-RestoreDatabase <db>] [-KeepRestore] [-KeepDumpDir <dir>]
param(
    [string]$SourceDatabase = 'odca_test_disposable',
    [string]$RestoreDatabase = 'odca_restore_check_s10',
    [switch]$KeepRestore,
    [string]$KeepDumpDir = ''
)

$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$pgUser = 'postgres'
$pgHost = '127.0.0.1'
if (-not $env:PGPASSWORD) { $env:PGPASSWORD = '123456' }

# Localiza os binários do PostgreSQL 18 (PATH ou instalação padrão).
function Resolve-PgBinary {
    param([Parameter(Mandatory)][string]$Name)
    $cmd = Get-Command $Name -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $fallback = Join-Path 'C:\Program Files\PostgreSQL\18\bin' "$Name.exe"
    if (-not (Test-Path -LiteralPath $fallback)) { throw "Binário '$Name' não encontrado no PATH nem em '$fallback'." }
    return $fallback
}
$psql = Resolve-PgBinary 'psql'
$pgDump = Resolve-PgBinary 'pg_dump'
$pgRestore = Resolve-PgBinary 'pg_restore'

$evDir = Join-Path $env:TEMP 'opencode\evidence'
New-Item -ItemType Directory -Force -Path $evDir | Out-Null
$workDir = Join-Path $evDir "d44-backup-restore-$(Get-Date -Format yyyyMMdd-HHmmss)"
New-Item -ItemType Directory -Force -Path $workDir | Out-Null
$transcript = New-Object System.Collections.Generic.List[string]
function LogT { param([string]$Message) $transcript.Add($Message); Write-Host $Message }
$checks = 0
$failed = 0
function Check {
    param([string]$Name, [bool]$Ok, [string]$Detail = '')
    $script:checks++
    if (-not $Ok) { $script:failed++ }
    LogT ("CHECK {0,-52} {1} {2}" -f $Name, $(if ($Ok) { 'ok' } else { 'FAIL' }), $Detail)
}

function Invoke-Psql {
    param([string]$Database, [string]$Sql)
    $argList = @('-h', $pgHost, '-U', $pgUser, '-d', $Database, '-tA', '-v', 'ON_ERROR_STOP=1', '-c', $Sql)
    & $psql @argList 2>&1
    if ($LASTEXITCODE -ne 0) { throw "psql falhou (exit=$LASTEXITCODE): $Sql" }
}

try {
    LogT "=== D-OC44 F-1: ensaio de backup/restore ==="
    LogT "inicio=$(Get-Date -Format o) fonte=$SourceDatabase destino=$RestoreDatabase pg=$(Split-Path $psql -Parent)"

    $version = Invoke-Psql 'postgres' 'select version();'
    Check 'servidor postgres responsivo' ($LASTEXITCODE -eq 0) ($version | Select-Object -First 1)

    foreach ($dbName in @($SourceDatabase, $RestoreDatabase)) {
        $exists = Invoke-Psql 'postgres' "select count(*) from pg_database where datname='$dbName';"
        if ($dbName -eq $SourceDatabase) {
            Check "banco de fonte '$dbName' existe" ($exists -eq '1')
        } else {
            Check "banco de restauracao '$dbName' ainda nao existe" ($exists -eq '0')
        }
    }

    $tables = @('tenants','users','memberships','contracts','contract_obligations','obligation_reminders','user_notifications','audit_events','support_sessions','sessions','notification_outbox')
    $fingerprints = @('users','contracts','audit_events')

    function Get-Metrics {
        param([string]$Database)
        $rows = [ordered]@{}
        $rows['max_version'] = Invoke-Psql $Database 'select max(version) from odca.schema_migrations;'
        foreach ($t in $script:tables) {
            $rows["count.$t"] = Invoke-Psql $Database "select count(*) from odca.$t;"
        }
        foreach ($t in $script:fingerprints) {
            $rows["md5.$t"] = Invoke-Psql $Database "select md5(string_agg(id::text,'|' order by id)) from odca.$t;"
        }
        # Superfície de leitura como odca_app SEM contexto de tenant: o valor depende das
        # políticas vigentes (há leitura permissiva de plataforma em odca.tenants desde S5);
        # o que o ensaio prova é a PARIDADE entre fonte e restaurado (políticas preservadas).
        $rows['rls.tenants_as_app_no_context'] = Invoke-Psql $Database "select c.n from (select set_config('role','odca_app',false)) r, lateral (select count(*) as n from odca.tenants) c;"
        return $rows
    }

    $source = Get-Metrics $SourceDatabase
    foreach ($key in $source.Keys) { LogT "SRC   $key = $($source[$key])" }

    $dumpFile = Join-Path $workDir "$SourceDatabase.dump"
    $dumpArgs = @('-h', $pgHost, '-U', $pgUser, '-Fc', '-Z', '6', '-f', $dumpFile, $SourceDatabase)
    $dumpOut = & $pgDump @dumpArgs 2>&1
    Check 'pg_dump custom format concluido' ($LASTEXITCODE -eq 0 -and (Test-Path -LiteralPath $dumpFile))
    if ($LASTEXITCODE -ne 0) { LogT ($dumpOut -join "`n"); exit 1 }
    $dumpBytes = (Get-Item -LiteralPath $dumpFile).Length
    $dumpSha = (Get-FileHash -LiteralPath $dumpFile -Algorithm SHA256).Hash.ToLowerInvariant()
    LogT "DUMP  bytes=$dumpBytes sha256=$dumpSha"
    Check 'dump tem tamanho minimo' ($dumpBytes -gt 1024) "bytes=$dumpBytes"

    $listOut = & $pgRestore '-h' $pgHost '-U' $pgUser '-l' $dumpFile 2>&1
    Check 'pg_restore -l lista objetos' ($LASTEXITCODE -eq 0 -and ($listOut -join '' -match 'schema'))

    Invoke-Psql 'postgres' "drop database if exists $RestoreDatabase with (force);"
    Invoke-Psql 'postgres' "create database $RestoreDatabase;"
    Check 'banco de restauracao criado' ($true) $RestoreDatabase

    $restoreArgs = @('-h', $pgHost, '-U', $pgUser, '--no-owner', '--exit-on-error', '-d', $RestoreDatabase, $dumpFile)
    $restoreOut = & $pgRestore @restoreArgs 2>&1
    if ($LASTEXITCODE -ne 0) { LogT ($restoreOut -join "`n"); }
    Check 'pg_restore aplicado sem erros' ($LASTEXITCODE -eq 0)

    $restored = Get-Metrics $RestoreDatabase
    foreach ($key in $restored.Keys) { LogT "RST   $key = $($restored[$key])" }

    foreach ($key in $source.Keys) {
        Check "paridade $key" ([string]$source[$key] -eq [string]$restored[$key]) "src=$($source[$key]) rst=$($restored[$key])"
    }

    # Função DEFINER ainda executa no banco restaurado (retorna false p/ grant inexistente,
    # o que prova que a função roda sem erro de permissão/esquema apos o restore).
    $fnOk = Invoke-Psql $RestoreDatabase 'select odca.support_impersonation_is_valid(gen_random_uuid())::text;'
    Check 'funcao security definer executavel apos restore' ($fnOk -match '^(f|false)$') "resultado=$fnOk"

    # D-OC46 (Sprint 12/F-2): backup pré-piloto — preserva o dump validado fora do
    # repositório, com carimbo de data/hora, somente apos TODOS os checks de paridade.
    if (-not [string]::IsNullOrWhiteSpace($KeepDumpDir)) {
        New-Item -ItemType Directory -Force -Path $KeepDumpDir | Out-Null
        $keepName = "$SourceDatabase-pre-pilot-$(Get-Date -Format 'yyyy-MM-dd_HHmmss').dump"
        Copy-Item -LiteralPath $dumpFile -Destination (Join-Path $KeepDumpDir $keepName)
        Check 'dump preservado em KeepDumpDir' ($true) "$KeepDumpDir\$keepName bytes=$dumpBytes sha256=$dumpSha"
    }

    if ($KeepRestore) {
        LogT "banco de restauracao preservado: $RestoreDatabase"
    } else {
        Invoke-Psql 'postgres' "drop database if exists $RestoreDatabase with (force);"
        Check 'banco de restauracao removido' ($true)
    }

    LogT "BACKUP-RESTORE-DONE checks=$checks ok=$($checks - $failed) fail=$failed"
    $stamp = ($transcript -join "`r`n") + "`r`n"
    Set-Content -LiteralPath (Join-Path $workDir 'transcript.txt') -Value $stamp -Encoding utf8
    $evidenceCopy = Join-Path $repoRoot "evidence\d44-backup-restore-rodada-final.txt"
    Set-Content -LiteralPath $evidenceCopy -Value $stamp -Encoding utf8
    LogT "transcript versionado: evidence/d44-backup-restore-rodada-final.txt"
    if ($failed -gt 0) { exit 1 }
    exit 0
}
catch {
    LogT "ERRO: $($_.Exception.Message)"
    Set-Content -LiteralPath (Join-Path $workDir 'transcript.txt') -Value (($transcript -join "`r`n") + "`r`n") -Encoding utf8
    Write-Host 'BACKUP-RESTORE-FAILED'
    exit 1
}
