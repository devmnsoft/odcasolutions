# D-OC44 (Sprint 10/F-2): ensaio do transporte SMTP REAL do worker de notificacoes.
# Cria um banco de ensaio dedicado (migrado do canônico), semeia um cenario sintetico de
# lembrete de obrigacao com canal e-mail habilitado, executa o WORKER REAL apontando o
# transporte para o sink SMTP local (scripts/qa/smtp-sink.mjs) e comprova:
#   fase positiva: lembrete reclamado -> outbox enfileirado -> mensagem aceita pelo sink
#     (conteudo verificado: destinatario, link da plataforma, charset utf-8 no corpo) e
#     status final 'sent' + notificacao in-app criada no mesmo fluxo;
#   fase negativa: com o sink parado, a tentativa falha de forma CONTROLADA (status nao
#     'sent', attempt_count>=1, last_error_code populado) e o lembrete in-app continua criado.
# Nenhum fornecedor externo e usado; o provedor real segue decisao externa registrada.
# Uso: powershell -NoProfile -ExecutionPolicy Bypass -File scripts/qa/rehearse-smtp-transport.ps1 [-SinkPort 2537] [-KeepDatabase]
param(
    [int]$SinkPort = 2537,
    [switch]$KeepDatabase
)

$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$pgUser = 'postgres'
$pgHost = '127.0.0.1'
if (-not $env:PGPASSWORD) { $env:PGPASSWORD = '123456' }
$db = 'odca_smtp_rehearsal'
$email = 'ensaio-smtpp@rehearsal.test'
$sender = 'odca-rehearsal@rehearsal.local'

function Resolve-PgBinary {
    param([Parameter(Mandatory)][string]$Name)
    $cmd = Get-Command $Name -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $fallback = Join-Path 'C:\Program Files\PostgreSQL\18\bin' "$Name.exe"
    if (-not (Test-Path -LiteralPath $fallback)) { throw "Binário '$Name' não encontrado." }
    return $fallback
}
$psql = Resolve-PgBinary 'psql'

$evDir = Join-Path $env:TEMP 'opencode\evidence'
New-Item -ItemType Directory -Force -Path $evDir | Out-Null
$workDir = Join-Path $evDir "d44-smtp-rehearsal-$(Get-Date -Format yyyyMMdd-HHmmss)"
New-Item -ItemType Directory -Force -Path $workDir | Out-Null
$inboxDir = Join-Path $workDir 'inbox'
New-Item -ItemType Directory -Force -Path $inboxDir | Out-Null

$transcript = New-Object System.Collections.Generic.List[string]
function LogT { param([string]$Message) $transcript.Add($Message); Write-Host $Message }
$checks = 0
$failed = 0
function Check {
    param([string]$Name, [bool]$Ok, [string]$Detail = '')
    $script:checks++
    if (-not $Ok) { $script:failed++ }
    LogT ("CHECK {0,-56} {1} {2}" -f $Name, $(if ($Ok) { 'ok' } else { 'FAIL' }), $Detail)
}
function Invoke-Psql {
    param([string]$Database, [string]$Sql)
    & $psql '-h' $pgHost '-U' $pgUser '-d' $Database '-tA' '-v' 'ON_ERROR_STOP=1' '-c' $Sql 2>&1
    if ($LASTEXITCODE -ne 0) { throw "psql falhou (exit=$LASTEXITCODE): $Sql" }
}
function Stop-ProcessTree {
    param($Process)
    if ($null -eq $Process -or $Process.HasExited) { return }
    $children = Get-CimInstance Win32_Process -Filter "ParentProcessId = $($Process.Id)" -ErrorAction SilentlyContinue
    foreach ($c in $children) { Stop-Process -Id $c.ProcessId -Force -ErrorAction SilentlyContinue }
    Stop-Process -Id $Process.Id -Force -ErrorAction SilentlyContinue
}
function Start-Worker {
    param([string]$LogBase)
    # DOTNET_ENVIRONMENT tem precedencia sobre ASPNETCORE_ENVIRONMENT no .NET; um valor
    # de maquina em Production faria o worker ignorar appsettings.Development.json (onde
    # vive Notifications:PublicOrigin) e derrubar todo envio com
    # notification_configuration_invalid — por isso os dois sao fixados aqui.
    $env:DOTNET_ENVIRONMENT = 'Development'
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:ConnectionStrings__Database = "Host=$pgHost;Port=5432;Database=$db;Username=$pgUser;Password=$env:PGPASSWORD;Search Path=odca"
    # O PowerShell 5.1 NAO propaga variaveis de ambiente vazias ('') para o filho (testado:
    # o filho ve NULL) — por isso o runtime json pessoal ganharia e o FilePickup local
    # capturaria as mensagens em vez do SMTP. Espaco branco e nao-vazio (propagado) e, ao
    # mesmo tempo, IsNullOrWhiteSpace=true, o que desliga o FilePickup na selecao do worker.
    $env:Notifications__DevelopmentPickupDirectory = ' '
    $env:Notifications__Smtp__Host = '127.0.0.1'
    $env:Notifications__Smtp__Port = "$SinkPort"
    $env:Notifications__Smtp__SenderAddress = $sender
    $env:Notifications__Smtp__SenderName = 'ODCA Rehearsal'
    $env:Notifications__Smtp__UseStartTls = 'false'
    $dll = Join-Path $repoRoot 'src\Odca.Worker\bin\Debug\net10.0\Odca.Worker.dll'
    return Start-Process dotnet -ArgumentList "`"$dll`"" -WorkingDirectory $repoRoot `
        -RedirectStandardOutput (Join-Path $workDir "$LogBase.out.log") `
        -RedirectStandardError (Join-Path $workDir "$LogBase.err.log") -PassThru
}
function Await-Sql {
    param([string]$Sql, [int]$TimeoutSeconds, [string]$Description)
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $value = Invoke-Psql $db $Sql
            if ($null -ne $value -and -not [string]::IsNullOrWhiteSpace([string]$value)) { return $value }
        } catch { }
        Start-Sleep -Milliseconds 1500
    }
    throw "timeout aguardando: $Description"
}

$node = Get-Command node -ErrorAction SilentlyContinue
if (-not $node) { throw 'node não encontrado no PATH (necessário para o smtp-sink.mjs).' }

# Limpeza previa: execucoes abortadas podem deixar sink/worker do ensaio vivos segurando
# a porta 2537 ou o banco; mata por assinatura de linha de comando.
foreach ($proc in (Get-CimInstance Win32_Process -Filter "Name='dotnet.exe' OR Name='node.exe'" |
        Where-Object { $_.CommandLine -match 'Odca\.Worker|smtp-sink' })) {
    Write-Host ("MATE processo residual " + $proc.ProcessId + " (" + $proc.Name + ")")
    Stop-Process -Id $proc.ProcessId -Force -ErrorAction SilentlyContinue
}
Start-Sleep -Milliseconds 500

try {
    LogT "=== D-OC44 F-2: ensaio do transporte SMTP real ==="
    LogT "inicio=$(Get-Date -Format o) banco=$db porta=$SinkPort remetente=$sender"

    # 1) worker compilado (Debug, idempotente).
    & dotnet build (Join-Path $repoRoot 'src\Odca.Worker') -c Debug --nologo -v m 2>&1 | Out-Null
    Check 'build do worker (Debug)' ($LASTEXITCODE -eq 0)

    # 2) banco dedicado migrado do canônico.
    Invoke-Psql 'postgres' "drop database if exists $db with (force);" | Out-Null
    Invoke-Psql 'postgres' "create database $db;" | Out-Null
    & $psql '-h' $pgHost '-U' $pgUser '-d' $db '-q' '-v' 'ON_ERROR_STOP=1' '-f' (Join-Path $repoRoot 'database\odca.sql') 2>&1 | Out-Null
    $maxVersion = Invoke-Psql $db 'select max(version) from odca.schema_migrations;'
    Check 'banco de ensaio migrado' ($maxVersion -ge '61') "max_version=$maxVersion"

    # 3) sink SMTP local (MTA sintético).
    $sinkProc = Start-Process $node.Source `
        -ArgumentList "`"$repoRoot\scripts\qa\smtp-sink.mjs`" --port $SinkPort --inbox `"$inboxDir`" --max 2" `
        -WorkingDirectory $repoRoot -RedirectStandardOutput (Join-Path $workDir 'sink.log') `
        -RedirectStandardError (Join-Path $workDir 'sink.err.log') -PassThru
    $ready = $false
    for ($i = 0; $i -lt 20; $i++) {
        Start-Sleep -Milliseconds 750
        $logFile = Join-Path $workDir 'sink.log'
        if ((Test-Path $logFile) -and ((Get-Content $logFile -Raw -ErrorAction SilentlyContinue) -match 'SINK-READY')) { $ready = $true; break }
    }
    Check 'sink SMTP local pronto' $ready "porta=$SinkPort"

    # 4) cenario sintetico: tenant + usuario + contrato + obrigacao + lembrete + preferencia de canal.
    # Nomes em ASCII proposital: o arquivo PS e lido como ANSI pelo PowerShell 5.1 (sem BOM),
    # e esses valores sao gravados no banco; os enderecos de e-mail sao sintéticos fixos.
    $seed = @'
BEGIN;
INSERT INTO odca.tenants(id,business_code,display_name,status,timezone)
VALUES('b1f00000-0000-4000-8000-000000000001','s10smtp','Organizacao do ensaio SMTP','active','America/Sao_Paulo');
INSERT INTO odca.users(id,email,email_normalized,login_normalized,display_name,password_hash,must_change_password,is_platform_administrator,email_verified_at)
VALUES('b1f00000-0000-4000-8000-0000000000a1',':email',':emailUpper',':emailUpper','Ensaiante sintetico S10','hash',false,false,now());
INSERT INTO odca.memberships(tenant_id,user_id,status)
VALUES('b1f00000-0000-4000-8000-000000000001','b1f00000-0000-4000-8000-0000000000a1','active');
INSERT INTO odca.contracts(id,tenant_id,title)
VALUES('b1f00000-0000-4000-8000-0000000000c1','b1f00000-0000-4000-8000-000000000001','Contrato do ensaio de transporte');
INSERT INTO odca.contract_obligations(id,tenant_id,contract_id,title,category,obligated_party,owner_id,due_date,priority,status,origin,created_by)
VALUES('b1f00000-0000-4000-8000-0000000000d1','b1f00000-0000-4000-8000-000000000001','b1f00000-0000-4000-8000-0000000000c1',
       'Enviar declaracao mensal','document','Cliente sintetico','b1f00000-0000-4000-8000-0000000000a1',current_date+7,'normal','open','manual','b1f00000-0000-4000-8000-0000000000a1');
INSERT INTO odca.obligation_reminders(tenant_id,obligation_id,recipient_id,days_before,scheduled_for,deduplication_key)
VALUES('b1f00000-0000-4000-8000-000000000001','b1f00000-0000-4000-8000-0000000000d1','b1f00000-0000-4000-8000-0000000000a1',7,current_date,'s10-sink-positive');
INSERT INTO odca.user_notification_preferences(tenant_id,user_id,kind,enabled,channel_in_app,channel_email)
VALUES('b1f00000-0000-4000-8000-000000000001','b1f00000-0000-4000-8000-0000000000a1','obligation_due',true,true,true);
COMMIT;
'@.Replace(':emailUpper', $email.ToUpperInvariant()).Replace(':email', $email)
    Invoke-Psql $db $seed | Out-Null
    Check 'cenario sintetico semeado' ($true) 'tenant/usuario/contrato/obrigacao/lembrete/canal_email'

    # 5) worker real em modo SMTP (fase positiva).
    $workerA = Start-Worker 'worker-a'
    Start-Sleep -Seconds 2
    $sentRow = $null
    try {
        $sentRow = Await-Sql "select status||'|'||attempt_count||'|'||coalesce(last_error_code,'-') from odca.notification_outbox where kind='obligation_due' and destination='$email' and status='sent'" 90 'saida sent na outbox (fase positiva)'
    } catch { LogT "AVISO fase positiva: $_" }
    Check 'mensagem aceita pelo sink (outbox sent)' ($null -ne $sentRow) "row=$sentRow"

    $inApp = Invoke-Psql $db "select count(*) from odca.user_notifications where kind='obligation_due';"
    Check 'notificacao in-app criada no mesmo fluxo' ([int]$inApp -ge 1) "count=$inApp"

    Stop-ProcessTree $workerA

    $emlFiles = Get-ChildItem -Path $inboxDir -Filter '*.eml' -ErrorAction SilentlyContinue
    Check 'eml recebido pelo sink' (@($emlFiles).Count -ge 1) "arquivos=$(@($emlFiles).Count)"
    if (@($emlFiles).Count -ge 1) {
        $content = Get-Content -LiteralPath $emlFiles[0].FullName -Raw -Encoding ascii
        Check 'destinatario correto no eml' ($content -match [regex]::Escape("To: $email"))
        Check 'remetente do ensaio no eml' ($content -match [regex]::Escape($sender))
        Check 'corpo em utf-8 declarado no header' ($content -match 'charset=utf-8')
        # O corpo vai em Content-Transfer-Encoding: base64; decodifica para validar o
        # conteudo real (link + sobrevivencia de caracteres nao-ASCII, sem trocar por '?').
        $headerEnd = $content.IndexOf("`r`n`r`n")
        $bodyB64 = if ($headerEnd -ge 0) { $content.Substring($headerEnd + 4) } else { $content }
        $bodyB64 = ($bodyB64 -replace '[^A-Za-z0-9+/=]', '')
        $decoded = ''
        try { $decoded = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String($bodyB64)) } catch { }
        Check 'link da plataforma no corpo decodificado' ($decoded -match '/minha-conta/notificacoes')
        Check 'texto esperado presente no corpo' ($decoded -match 'na plataforma')
        $nonAscii = @($decoded.ToCharArray() | Where-Object { [int]$_ -gt 127 }).Count
        Check 'acentos sobreviveram ao transporte (sem perda por US-ASCII)' ($nonAscii -gt 0) "chars-nao-ascii=$nonAscii"
    }
    Stop-ProcessTree $sinkProc

    # 6) fase negativa: mesmo fluxo sem sink ativo -> falha controlada, sem perda.
    $seedB = @"
BEGIN;
INSERT INTO odca.obligation_reminders(tenant_id,obligation_id,recipient_id,days_before,scheduled_for,deduplication_key)
VALUES('b1f00000-0000-4000-8000-000000000001','b1f00000-0000-4000-8000-0000000000d1','b1f00000-0000-4000-8000-0000000000a1',7,current_date,'s10-sink-negative');
COMMIT;
"@
    Invoke-Psql $db $seedB | Out-Null
    $workerB = Start-Worker 'worker-b'
    $negRow = $null
    try {
        $negRow = Await-Sql "select status||'|'||attempt_count||'|'||coalesce(last_error_code,'-') from odca.notification_outbox where kind='obligation_due' and status<>'sent' and last_error_code is not null order by created_at desc limit 1" 90 'falha controlada completa na outbox (fase negativa)'
    } catch { LogT "AVISO fase negativa: $_" }
    Check 'falha controlada registrada (tentativa+codigo)' (($null -ne $negRow) -and ($negRow -notmatch '^sent')) "row=$negRow"
    if ($null -ne $negRow) {
        $parts = $negRow -split '\|'
        Check 'codigo de erro capturado' (-not [string]::IsNullOrWhiteSpace($parts[2]) -and $parts[2] -ne '-') "code=$($parts[2])"
    }
    $inApp2 = Invoke-Psql $db "select count(*) from odca.user_notifications where kind='obligation_due';"
    Check 'canal in-app continuo apesar da falha de e-mail' ([int]$inApp2 -ge 2) "count=$inApp2"
    Stop-ProcessTree $workerB

    if ($KeepDatabase) {
        LogT "banco de ensaio preservado: $db"
    } else {
        Invoke-Psql 'postgres' "drop database if exists $db with (force);" | Out-Null
        Check 'banco de ensaio removido' ($true)
    }

    foreach ($logName in @('worker-a.out.log', 'worker-a.err.log', 'worker-b.out.log', 'worker-b.err.log', 'sink.log')) {
        $p = Join-Path $workDir $logName
        if (Test-Path $p) {
            $tail = @(Get-Content $p -Tail 30 -ErrorAction SilentlyContinue)
            LogT "--- $logName (ultimas 30 linhas) ---"
            foreach ($line in $tail) { LogT "LOG[$logName] $line" }
        }
    }

    LogT "SMTP-REHEARSAL-DONE checks=$checks ok=$($checks - $failed) fail=$failed"
    $stamp = ($transcript -join "`r`n") + "`r`n"
    Set-Content -LiteralPath (Join-Path $workDir 'transcript.txt') -Value $stamp -Encoding utf8
    Set-Content -LiteralPath (Join-Path $repoRoot 'evidence\d44-smtp-transport-rodada-final.txt') -Value $stamp -Encoding utf8
    LogT 'transcript versionado: evidence/d44-smtp-transport-rodada-final.txt'
    if ($failed -gt 0) { exit 1 }
    exit 0
}
catch {
    LogT "ERRO: $($_.Exception.Message)"
    LogT "ERRO DETALHE: $($_.ScriptStackTrace)"
    # Para processos filhos ANTES de sair: eles podem segurar handles do pipeline e
    # travar a execucao do script mesmo apos o catch (lixo de porta/stderr herdados).
    Stop-ProcessTree $workerB
    Stop-ProcessTree $workerA
    Stop-ProcessTree $sinkProc
    Set-Content -LiteralPath (Join-Path $workDir 'transcript.txt') -Value (($transcript -join "`r`n") + "`r`n") -Encoding utf8
    Write-Host 'SMTP-REHEARSAL-FAILED'
    exit 1
}
