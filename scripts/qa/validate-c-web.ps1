$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }

$api    = 'https://localhost:7143'
$psql   = 'C:\Program Files\PostgreSQL\18\bin\psql.exe'
$db     = 'odca_test_disposable'
$TC     = '20000000-0000-4000-8000-000000000001'
$ENT_ID   = '30000000-0000-0000-0000-000000000013'
$BASIC_ID = '30000000-0000-0000-0000-000000000011'
$QA_OP    = '4e000000-0000-4000-8000-000000000021'   # identidade propria da suita (D-A5)
$QA_PWD   = 'La@RsjC5p-hmq_JpReGW'   # mesma senha do operador (hash clonado no setup)
$tmpRoot = 'C:\Users\NCELL-DEV-020\AppData\Local\Temp\opencode'

$script:passes = 0
$script:failures = 0
$script:unhandled = ''
$script:seedBefore = $null
$script:planBefore = $null

function Assert([string]$name, [bool]$cond, [string]$detail) {
    if ($cond) { $script:passes++; Write-Host ('PASS  ' + $name) }
    else {
        Write-Host ('FAIL  ' + $name)
        if ($detail) { Write-Host ('      observed: ' + $detail.Substring(0, [Math]::Min(400, $detail.Length))) }
        $script:failures++
    }
}

function Snip([string]$s) { if (-not $s) { return '' } return $s.Substring(0, [Math]::Min(400, $s.Length)) }

function Sql([string]$sql) {
    $tmp = Join-Path $env:TEMP ('odca-c-' + [guid]::NewGuid().ToString('N') + '.sql')
    [IO.File]::WriteAllText($tmp, $sql, (New-Object System.Text.UTF8Encoding($false)))
    $prev = $env:PGPASSWORD; $env:PGPASSWORD = '123456'
    $out = & $psql -U postgres -d $db -P pager=off -t -A -q -v ON_ERROR_STOP=1 -f $tmp 2>&1
    $code = $LASTEXITCODE
    $env:PGPASSWORD = $prev
    Remove-Item $tmp -EA SilentlyContinue
    $txt = (($out | Out-String)).Trim()
    if ($code -ne 0) { throw ('psql falhou: ' + $txt) }
    return $txt
}

function SqlOpt([string]$sql) {
    try { return (Sql $sql) } catch { Write-Host ('      [warn] cleanup: ' + $_.Exception.Message); return '' }
}

function Invoke-Api([string]$method, [string]$url, [string]$tok, [string]$body) {
    $req = [System.Net.HttpWebRequest]::Create($url)
    $req.Method = $method
    $req.Accept = 'application/json'
    $req.Timeout = 60000
    if ($tok) { $req.Headers.Add('Authorization', 'Bearer ' + $tok) }
    if ($body) {
        $req.ContentType = 'application/json; charset=utf-8'
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($body)
        $req.ContentLength = $bytes.Length
        $s = $req.GetRequestStream(); $s.Write($bytes, 0, $bytes.Length); $s.Close()
    }
    elseif ($method -ne 'GET' -and $method -ne 'HEAD' -and $method -ne 'DELETE') {
        $req.ContentLength = 0
    }
    try { $resp = $req.GetResponse() } catch [System.Net.WebException] { $resp = $_.Exception.Response }
    $code = 0
    $text = ''
    if ($resp) {
        $code = [int]$resp.StatusCode
        $ms = New-Object IO.MemoryStream
        $resp.GetResponseStream().CopyTo($ms)
        $resp.Close()
        $text = [Text.Encoding]::UTF8.GetString($ms.ToArray())
    }
    Write-Host ('  api ' + $method + ' ' + $url.Substring($api.Length) + ' -> ' + $code)
    return @{ Code = $code; Body = $text }
}

function JGet($obj, [string]$prop) {
    if ($null -eq $obj) { return '' }
    $p = $obj.PSObject.Properties[$prop]
    if ($null -eq $p -or $null -eq $p.Value) { return '' }
    return ('' + $p.Value)
}
function JBody($body) { if ($body -isnot [string]) { $cs = (Get-PSCallStack)[1]; throw ('JBody recebeu ' + $body.GetType().Name + ' na linha ' + $cs.ScriptLineNumber) }; return ($body | ConvertFrom-Json) }
function ToJson($obj) { return (ConvertTo-Json -InputObject $obj -Depth 30 -Compress) }

function Get-TotpCode([string]$secret) {
    $alpha = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'
    $clean = ($secret.ToUpperInvariant() -replace '[^A-Z2-7]', '')
    $bits = ''
    foreach ($ch in $clean.ToCharArray()) { $bits += [Convert]::ToString($alpha.IndexOf($ch), 2).PadLeft(5, '0') }
    $byteCount = [int][Math]::Floor($bits.Length / 8)
    $key = New-Object 'byte[]' $byteCount
    for ($i = 0; $i -lt $byteCount; $i++) { $key[$i] = [Convert]::ToByte($bits.Substring($i * 8, 8), 2) }
    $counter = [long][Math]::Floor([DateTimeOffset]::UtcNow.ToUnixTimeSeconds() / 30)
    $cb = New-Object 'byte[]' 8
    for ($j = 7; $j -ge 0; $j--) { $cb[$j] = [byte]($counter -band 0xff); $counter = $counter -shr 8 }
    $hmac = New-Object System.Security.Cryptography.HMACSHA1
    $hmac.Key = $key
    $h = $hmac.ComputeHash($cb)
    $o = $h[$h.Length - 1] -band 0x0f
    $bin = ((([int]$h[$o]) -band 0x7f) * 16777216) + ((([int]$h[$o + 1]) -band 0xff) * 65536) + ((([int]$h[$o + 2]) -band 0xff) * 256) + (([int]$h[$o + 3]) -band 0xff)
    return ('{0:D6}' -f ($bin % 1000000))
}

function Login-Plain([string]$loginName, [string]$pwd) {
    $body = ToJson @{ login = $loginName; password = $pwd }
    for ($try = 1; $try -le 4; $try++) {
        $r = Invoke-Api 'POST' ($api + '/api/v1/auth/login') $null $body
        if ($r.Code -eq 200) { return $r }
        Write-Host ('      login tentiva ' + $try + ' falhou (' + $r.Code + '), aguardando rate limit...')
        Start-Sleep -Seconds 65
    }
    throw ('login ' + $loginName + ' falhou: ' + $r.Code + ' ' + (Snip $r.Body))
}

function Login-MfaAdmin([string]$loginName, [string]$pwd) {
    # A identidade QA de plataforma precisa de sessao com claim mfa_verified=true (politica
    # PlatformAdministrator). A inscricao TOTP e concluida por aqui com os endpoints da API;
    # nesta versao so administradores de plataforma concluem inscricao MFA.
    $r = Login-Plain $loginName $pwd
    $j = JBody $r.Body
    $tok = $j.accessToken
    if ((JGet $j 'requiresMfaEnrollment') -ne 'True') {
        if ((JGet $j 'mfaVerified') -eq 'True') { return $tok }
        throw ('esperava requiresMfaEnrollment para ' + $loginName + ': ' + (Snip $r.Body))
    }
    $e = Invoke-Api 'POST' ($api + '/api/v1/auth/mfa/enrollment') $tok $null
    if ($e.Code -ne 200) { throw ('mfa/enrollment falhou: ' + $e.Code + ' ' + (Snip $e.Body)) }
    $secret = JGet (JBody $e.Body) 'manualKey'
    $code = Get-TotpCode $secret
    $c = Invoke-Api 'POST' ($api + '/api/v1/auth/mfa/enrollment/confirm') $tok (ToJson @{ code = $code })
    if ($c.Code -ne 200) { throw ('mfa/confirm falhou: ' + $c.Code + ' ' + (Snip $c.Body)) }
    $vtok = (JBody $c.Body).accessToken
    if (-not $vtok) { throw ('mfa/confirm sem token: ' + (Snip $c.Body)) }
    return $vtok
}

# ---- Setup ------------------------------------------------------------------
try {
    $curDb = Sql 'SELECT current_database();'
    Assert 'S0.current_database' ($curDb -eq $db) "db=$curDb"

    # Fingerprint dos tres usuarios-semente: o finally assertera que permanecem intactos.
    $script:seedBefore = Sql @"
SELECT email_normalized || '|' || password_hash || '|' || coalesce(mfa_secret_protected,'none') || '|'
  || coalesce(mfa_confirmed_at::text,'none') || '|' || coalesce(mfa_pending_since::text,'none') || '|'
  || failed_login_count || '|' || security_version || '|' || is_platform_administrator || '|'
  || (SELECT count(*) FROM odca.mfa_recovery_codes rc WHERE rc.user_id=u.id)
FROM odca.users u
WHERE email_normalized IN ('ADMIN@ODCA.LOCAL','OPERADOR@ODCA.LOCAL','CLIENTE.TESTE@ODCA.LOCAL')
ORDER BY email_normalized;
"@
    $script:planBefore = Sql "SELECT plan_version_id FROM odca.subscriptions WHERE tenant_id='$TC' AND status='active';"
    Assert 'S0.plano_capturado' ($script:planBefore -match '[0-9a-f]{8}-') "plan=$script:planBefore"

    Write-Host '--- S0 limpeza de execucoes anteriores (best effort) ---'
    SqlOpt @"
DELETE FROM odca.solicitation_events WHERE solicitation_id IN (SELECT id FROM odca.solicitations WHERE subject LIKE 'Val C:%');
DELETE FROM odca.solicitation_messages WHERE solicitation_id IN (SELECT id FROM odca.solicitations WHERE subject LIKE 'Val C:%');
DELETE FROM odca.solicitation_pauses WHERE solicitation_id IN (SELECT id FROM odca.solicitations WHERE subject LIKE 'Val C:%');
DELETE FROM odca.solicitations WHERE subject LIKE 'Val C:%';
DELETE FROM odca.template_approvals WHERE tenant_id='$TC' AND official_key='surgical-consent';
DELETE FROM odca.sessions WHERE user_id='$QA_OP';
DELETE FROM odca.mfa_recovery_codes WHERE user_id='$QA_OP';
DELETE FROM odca.resource_movements WHERE actor_user_id='$QA_OP';
DELETE FROM odca.audit_events WHERE actor_user_id='$QA_OP';
UPDATE odca.users SET deleted_by=NULL WHERE id='$QA_OP';
DELETE FROM odca.users WHERE id='$QA_OP';
"@ | Out-Null
    Assert 'setup.cleanup' $true ''

    # Identidade propria da suita (D-A5): operador de plataforma dedicado. Nenhum
    # usuario-semente e mutado; a senha e apenas o hash clonado do operador para login.
    Sql @"
INSERT INTO odca.users(id,email,email_normalized,login_normalized,display_name,password_hash,must_change_password,is_platform_administrator)
SELECT '$QA_OP','qa-c-op@odca.local','QA-C-OP@ODCA.LOCAL','qa-c-op','QA C Operador',h.password_hash,false,true
FROM odca.users h WHERE h.email_normalized='OPERADOR@ODCA.LOCAL'
AND NOT EXISTS(SELECT 1 FROM odca.users u WHERE u.id='$QA_OP');
"@ | Out-Null

    # Garante a permissao no papel do cliente: no seed ja vem pelo wildcard tenant.%,
    # a guarda NOT EXISTS torna a execucao idempotente em qualquer estado anterior.
    Sql "INSERT INTO odca.role_permissions(role_id,permission_code,created_at) SELECT mr.role_id,'tenant.solicitations.manage',now() FROM odca.member_roles mr WHERE mr.tenant_id='$TC' AND mr.user_id=(SELECT id FROM odca.users WHERE email_normalized=upper('cliente.teste@odca.local')) AND NOT EXISTS(SELECT 1 FROM odca.role_permissions rp WHERE rp.role_id=mr.role_id AND rp.permission_code='tenant.solicitations.manage')" | Out-Null
    Sql "UPDATE odca.subscriptions SET plan_version_id='$BASIC_ID' WHERE tenant_id='$TC';" | Out-Null

    $mig = Sql 'SELECT max(version) FROM odca.schema_migrations;'
    Assert 'setup.schema_v42' ([int]$mig -ge 42) "max=$mig"

    $opTok    = Login-MfaAdmin 'qa-c-op@odca.local' $QA_PWD
    $cliR     = Login-Plain 'cliente.teste@odca.local' 'K8@wR3!nF6#zP2$m'
    $cliTok   = (JBody $cliR.Body).accessToken
    if (-not $cliTok) { throw 'login cliente sem accessToken' }
    Assert 'setup.logins' $true ''

    # S16/S17 dependem do modelo cirurgico no TC; suites que exercitam o modulo de
    # aprovacao (ex.: suite E) removem esses modelos em seus cleanups, entao a suita
    # garante a biblioteca oficial proprio punho (idempotente pelo endpoint). O
    # endpoint e de escopo da organizacao: exige membership no TC (o administrador
    # de plataforma sem membership recebe 403), por isso usa o cliente-semente,
    # membro tenant-client do TC — a mesma combinacao de papeis da suite E.
    $cntTpl = Sql "SELECT count(*) FROM odca.contract_templates WHERE owner_tenant_id='$TC' AND official_key IS NOT NULL AND status<>'archived';"
    if ([int]$cntTpl -lt 9) {
        $ri = Invoke-Api 'POST' ($api + "/api/v1/organizations/$TC/studio/templates/official") $cliTok ''
        Assert 'S0.biblioteca_oficial_tc' ($ri.Code -eq 200) (Snip $ri.Body)
    }

    # ---- S1: abertura exige plano Enterprise -------------------------------------
    $r = Invoke-Api 'POST' ($api + "/api/v1/organizations/$TC/solicitations") $cliTok (ToJson @{
        service='esclarecimento'; priority='normal'; subject='Val C: gate plano'; body='Abertura deve falhar enquanto o tenant esta no plano basic.'; idempotencyKey=[guid]::NewGuid().ToString() })
    Assert 'S1.gate_basic_409' ($r.Code -eq 409) ('code=' + $r.Code + ' ' + (Snip $r.Body))
    Assert 'S1.gate_plan_code' ($r.Body -match 'solicitations\.plan\.required') (Snip $r.Body)
    Sql "UPDATE odca.subscriptions SET plan_version_id='$ENT_ID' WHERE tenant_id='$TC';" | Out-Null
    $planNow = Sql "SELECT pv.code FROM odca.subscriptions s JOIN odca.plan_versions pv ON pv.id=s.plan_version_id WHERE s.tenant_id='$TC' AND s.status='active';"
    Assert 'S1.plan_flip_enterprise' ($planNow -eq 'enterprise') "plan=$planNow"

    # ---- S2: abertura + idempotencia + numeracao + snapshot SLA -------------------
    $idem1 = [guid]::NewGuid().ToString()
    $r = Invoke-Api 'POST' ($api + "/api/v1/organizations/$TC/solicitations") $cliTok (ToJson @{
        service='esclarecimento'; priority='normal'; subject='Val C: esclarecimento de SLA'; body='Preciso entender como funciona a contagem de horas uteis nos prazos da central.'; idempotencyKey=$idem1 })
    Assert 'S2.open_200' ($r.Code -eq 200) ('code=' + $r.Code + ' ' + (Snip $r.Body))
    $j = JBody $r.Body
    $solId = JGet $j 'id'
    $rv = [long](JGet $j 'rowVersion')
    Assert 'S2.open_payload' ($solId -match '[0-9a-f]{8}-') (Snip $r.Body)
    Assert 'S2.protocol_number' ((JGet $j 'protocol') -like 'SOL-*') (Snip $r.Body)
    Assert 'S2.initial_status_aberta' ((JGet $j 'status') -eq 'aberta') (Snip $r.Body)

    $d = JBody (Invoke-Api 'GET' ($api + "/api/v1/organizations/$TC/solicitations/$solId") $cliTok $null).Body
    $frBefore = JGet $d.sla 'firstResponseDueAt'
    $resBefore = JGet $d.sla 'resolutionDueAt'
    Assert 'S2.sla_snapshot_dues' ($frBefore -ne '' -and $resBefore -ne '') (Snip ($d.sla | ConvertTo-Json -Compress))
    Assert 'S2.sla_timezone_explicit' ((JGet $d.sla 'timezone') -match 'America/Sao_Paulo') (Snip ($d.sla | ConvertTo-Json -Compress))
    Assert 'S2.sla_calendar_explicit' ((JGet $d.sla 'calendar') -match 'dias_uteis|continuo') (Snip ($d.sla | ConvertTo-Json -Compress))
    $polCnt = [int](Sql "SELECT count(*) FROM odca.sla_policies WHERE plan_code='enterprise' AND enabled;")
    Assert 'S2.policies_seeded' ($polCnt -ge 12) "count=$polCnt"

    $dup = Invoke-Api 'POST' ($api + "/api/v1/organizations/$TC/solicitations") $cliTok (ToJson @{
        service='esclarecimento'; priority='normal'; subject='Val C: outra coisa'; body='Reenvio com a mesma chave de idempotencia nao cria nova solicitacao.'; idempotencyKey=$idem1 })
    Assert 'S3.idempotent_replay_same_id' ($dup.Code -eq 200 -and (JGet (JBody $dup.Body) 'id') -eq $solId) (Snip $dup.Body)
    $dupCnt = [int](Sql "SELECT count(*) FROM odca.solicitations WHERE subject LIKE 'Val C:%';")
    Assert 'S3.idempotent_no_extra_row' ($dupCnt -eq 1) "rows=$dupCnt"

    # ---- S4/S5: maquina de estados na triagem + recalculo de relogio --------------
    $r = Invoke-Api 'POST' ($api + "/api/v1/platform/solicitations/$solId/actions") $opTok (ToJson @{ action='iniciar'; rowVersion=$rv; tenantId=$TC })
    Assert 'S4.iniciar_before_triage_409' ($r.Code -eq 409 -and $r.Body -match 'solicitations\.transition') ('code=' + $r.Code + ' ' + (Snip $r.Body))

    $r = Invoke-Api 'POST' ($api + "/api/v1/platform/solicitations/$solId/actions") $opTok (ToJson @{ action='triar'; priority='alta'; rowVersion=$rv; tenantId=$TC })
    Assert 'S5.triar_200' ($r.Code -eq 200) ('code=' + $r.Code + ' ' + (Snip $r.Body))
    $j = JBody $r.Body
    $rv = [long](JGet $j 'rowVersion')
    Assert 'S5.status_triagem' ((JGet $j 'status') -eq 'triagem') (Snip $r.Body)
    Assert 'S5.priority_applied' ((JGet $j 'priority') -eq 'alta') (Snip $r.Body)
    Assert 'S5.triaged_at_stamped' ((JGet $j 'triagedAt') -ne '') (Snip $r.Body)
    $resAfterRetri = JGet $j.sla 'resolutionDueAt'
    Assert 'S5.clock_recalculated_on_priority' ($resAfterRetri -ne '' -and $resAfterRetri -ne $resBefore) ("before=$resBefore after=$resAfterRetri")

    # ---- S6: reatribuicao mantem o relogio ----------------------------------------
    $target = Sql "SELECT id FROM odca.users WHERE email_normalized=upper('admin@odca.local') AND is_platform_administrator AND NOT is_deleted;"
    $r = Invoke-Api 'POST' ($api + "/api/v1/platform/solicitations/$solId/actions") $opTok (ToJson @{ action='reatribuir'; assigneeUserId=$target; rowVersion=$rv; tenantId=$TC })
    Assert 'S6.reatribuir_200' ($r.Code -eq 200) ('code=' + $r.Code + ' ' + (Snip $r.Body))
    $j = JBody $r.Body
    $rv = [long](JGet $j 'rowVersion')
    Assert 'S6.assignee_changed' ((JGet $j 'assigneeUserId') -eq $target) (Snip $r.Body)
    $resAfterRe = JGet $j.sla 'resolutionDueAt'
    Assert 'S6.reatribuir_keeps_clock' ($resAfterRe -eq $resAfterRetri) ("before=$resAfterRetri after=$resAfterRe")

    # ---- S7: stale 409 + iniciar ---------------------------------------------------
    $r = Invoke-Api 'POST' ($api + "/api/v1/platform/solicitations/$solId/actions") $opTok (ToJson @{ action='iniciar'; rowVersion=($rv - 1); tenantId=$TC })
    Assert 'S7.stale_409' ($r.Code -eq 409) ('code=' + $r.Code + ' ' + (Snip $r.Body))
    Assert 'S7.stale_current_version_ext' ($r.Body -match '"currentRowVersion":\s*\d+') (Snip $r.Body)
    $r = Invoke-Api 'POST' ($api + "/api/v1/platform/solicitations/$solId/actions") $opTok (ToJson @{ action='iniciar'; rowVersion=$rv; tenantId=$TC })
    Assert 'S7.iniciar_200' ($r.Code -eq 200) ('code=' + $r.Code + ' ' + (Snip $r.Body))
    $j = JBody $r.Body
    $rv = [long](JGet $j 'rowVersion')
    Assert 'S7.status_em_atendimento' ((JGet $j 'status') -eq 'em_atendimento') (Snip $r.Body)
    Assert 'S7.first_response_stamped' ((JGet $j 'firstResponseAt') -ne '') (Snip $r.Body)

    # ---- S8: pausa justificada + retomada desloca vencimentos -----------------------
    $r = Invoke-Api 'POST' ($api + "/api/v1/platform/solicitations/$solId/actions") $opTok (ToJson @{ action='pausar'; rowVersion=$rv; tenantId=$TC })
    Assert 'S8.pausar_sem_justificativa_400' ($r.Code -eq 400 -and $r.Body -match 'validation_reason') ('code=' + $r.Code + ' ' + (Snip $r.Body))
    $r = Invoke-Api 'POST' ($api + "/api/v1/platform/solicitations/$solId/actions") $opTok (ToJson @{ action='pausar'; text='Aguardando comprovante do cliente.'; rowVersion=$rv; tenantId=$TC })
    Assert 'S8.pausar_200' ($r.Code -eq 200) ('code=' + $r.Code + ' ' + (Snip $r.Body))
    $j = JBody $r.Body
    $rv = [long](JGet $j 'rowVersion')
    Assert 'S8.paused_flag' ((JGet $j 'paused') -eq 'True') (Snip $r.Body)
    $resOnPause = JGet $j.sla 'resolutionDueAt'
    Start-Sleep -Seconds 6
    $r = Invoke-Api 'POST' ($api + "/api/v1/platform/solicitations/$solId/actions") $opTok (ToJson @{ action='retomar'; rowVersion=$rv; tenantId=$TC })
    Assert 'S8.retomar_200' ($r.Code -eq 200) ('code=' + $r.Code + ' ' + (Snip $r.Body))
    $j = JBody $r.Body
    $rv = [long](JGet $j 'rowVersion')
    $resOnResume = JGet $j.sla 'resolutionDueAt'
    Assert 'S8.dues_shifted_by_pause' ($resOnResume -gt $resOnPause) ("pause=$resOnPause resume=$resOnResume")
    $pauseTrail = Sql "SELECT CASE WHEN count(*)>=1 AND bool_and(ended_at IS NOT NULL) THEN 'ok' ELSE 'bad' END FROM odca.solicitation_pauses WHERE solicitation_id='$solId';"
    Assert 'S8.pause_trail_closed' ($pauseTrail -eq 'ok') "trail=$pauseTrail"

    # ---- S9: aguardando_cliente + retomada automatica por mensagem do cliente -------
    $r = Invoke-Api 'POST' ($api + "/api/v1/platform/solicitations/$solId/actions") $opTok (ToJson @{ action='aguardar'; text='Envie o print da tela de consumo.'; rowVersion=$rv; tenantId=$TC })
    Assert 'S9.aguardar_200' ($r.Code -eq 200) ('code=' + $r.Code + ' ' + (Snip $r.Body))
    $j = JBody $r.Body
    $rv = [long](JGet $j 'rowVersion')
    Assert 'S9.status_aguardando_cliente' ((JGet $j 'status') -eq 'aguardando_cliente') (Snip $r.Body)
    Assert 'S9.wait_pauses_clock' ((JGet $j 'paused') -eq 'True') (Snip $r.Body)
    $d = JBody (Invoke-Api 'GET' ($api + "/api/v1/organizations/$TC/solicitations/$solId") $cliTok $null).Body
    $rvCli = [long](JGet $d 'rowVersion')
    $r = Invoke-Api 'POST' ($api + "/api/v1/organizations/$TC/solicitations/$solId/messages") $cliTok (ToJson @{ body='Segue o print da tela de consumo.'; rowVersion=$rvCli })
    Assert 'S9.client_message_200' ($r.Code -eq 200) ('code=' + $r.Code + ' ' + (Snip $r.Body))
    $j = JBody $r.Body
    $rvCli = [long](JGet $j 'rowVersion')
    Assert 'S9.auto_resume_state' ((JGet $j 'status') -eq 'em_atendimento') (Snip $r.Body)
    Assert 'S9.auto_resume_clock' ((JGet $j 'paused') -eq 'False') (Snip $r.Body)
    $retEvt = Sql "SELECT count(*) FROM odca.solicitation_events WHERE solicitation_id='$solId' AND event_type='retorno_cliente';"
    Assert 'S9.retorno_cliente_event' ([int]$retEvt -ge 1) "rows=$retEvt"

    # ---- S9b: resposta da ODCA na thread (autor 'odca' via rota da plataforma) ------
    $rvM  = [long](JGet $j 'rowVersion')
    $r = Invoke-Api 'POST' ($api + "/api/v1/platform/solicitations/$solId/messages") $opTok (ToJson @{ body='ODCA: parametros em analise com a equipe cirurgica.'; rowVersion=$rvM; tenantId=$TC })
    Assert 'S9b.odca_message_200' ($r.Code -eq 200) ('code=' + $r.Code + ' ' + (Snip $r.Body))
    $j = JBody $r.Body
    $rvCli = [long](JGet $j 'rowVersion')

    # ---- S10: acoes exclusivas da ODCA bloqueadas para o tenant ---------------------
    foreach ($act in @('resolver', 'pausar', 'reatribuir')) {
        $r = Invoke-Api 'POST' ($api + "/api/v1/organizations/$TC/solicitations/$solId/actions") $cliTok (ToJson @{ action=$act; text='tentativa do tenant'; assigneeUserId=$target; rowVersion=$rvCli })
        Assert ("S10.tenant_" + $act + "_403") ($r.Code -eq 403 -and $r.Body -match 'permission_odca') ('code=' + $r.Code + ' ' + (Snip $r.Body))
    }

    # ---- S11: resolver + encerrar + historico ---------------------------------------
    $d = JBody (Invoke-Api 'GET' ($api + "/api/v1/organizations/$TC/solicitations/$solId") $cliTok $null).Body
    $rv = [long](JGet $d 'rowVersion')
    $r = Invoke-Api 'POST' ($api + "/api/v1/platform/solicitations/$solId/actions") $opTok (ToJson @{ action='resolver'; text='Parametros corrigidos: contagem considera apenas dias uteis entre 09h e 18h.'; rowVersion=$rv; tenantId=$TC })
    Assert 'S11.resolver_200' ($r.Code -eq 200) ('code=' + $r.Code + ' ' + (Snip $r.Body))
    $j = JBody $r.Body
    $rvRes = [long](JGet $j 'rowVersion')
    Assert 'S11.status_resolvida' ((JGet $j 'status') -eq 'resolvida') (Snip $r.Body)
    Assert 'S11.resolved_at_stamped' ((JGet $j 'resolvedAt') -ne '') (Snip $r.Body)
    $d = JBody (Invoke-Api 'GET' ($api + "/api/v1/organizations/$TC/solicitations/$solId") $cliTok $null).Body
    $msgs = @($d.messages)
    Assert 'S11.thread_client_and_odca' (@($msgs | Where-Object { (JGet $_ 'authorKind') -eq 'cliente' }).Count -ge 1 -and @($msgs | Where-Object { (JGet $_ 'authorKind') -eq 'odca' }).Count -ge 1) ($msgs | ConvertTo-Json -Compress)
    $evts = @($d.events)
    Assert 'S11.audit_trail_full' (@($evts | Where-Object { (JGet $_ 'eventType') -eq 'pausa_sl' }).Count -ge 1 -and @($evts | Where-Object { (JGet $_ 'eventType') -eq 'retomada_sl' }).Count -ge 1 -and @($evts | Where-Object { (JGet $_ 'eventType') -eq 'reatribuicao' }).Count -ge 1) ($evts | ForEach-Object { JGet $_ 'eventType' } | Sort-Object -Unique)
    $r = Invoke-Api 'POST' ($api + "/api/v1/organizations/$TC/solicitations/$solId/actions") $cliTok (ToJson @{ action='encerrar'; rowVersion=$rvRes })
    Assert 'S11.encerrar_by_tenant_200' ($r.Code -eq 200) ('code=' + $r.Code + ' ' + (Snip $r.Body))
    $j = JBody $r.Body
    $rvEnc = [long](JGet $j 'rowVersion')
    Assert 'S11.status_encerrada' ((JGet $j 'status') -eq 'encerrada') (Snip $r.Body)
    $r = Invoke-Api 'POST' ($api + "/api/v1/organizations/$TC/solicitations/$solId/messages") $cliTok (ToJson @{ body='Mensagem depois de encerrada.'; rowVersion=$rvEnc })
    Assert 'S11.closed_rejects_message_409' ($r.Code -eq 409) ('code=' + $r.Code + ' ' + (Snip $r.Body))

    # ---- S12: cancelamento com motivo ------------------------------------------------
    $idem2 = [guid]::NewGuid().ToString()
    $r = Invoke-Api 'POST' ($api + "/api/v1/organizations/$TC/solicitations") $cliTok (ToJson @{
        service='adaptacao'; priority='baixa'; subject='Val C: cancelamento com motivo'; body='Solicitacao aberta apenas para validar o cancelamento com motivo obrigatorio.'; idempotencyKey=$idem2 })
    $j = JBody $r.Body
    $smallId = JGet $j 'id'
    $rvSmall = [long](JGet $j 'rowVersion')
    $r = Invoke-Api 'POST' ($api + "/api/v1/organizations/$TC/solicitations/$smallId/actions") $cliTok (ToJson @{ action='cancelar'; rowVersion=$rvSmall })
    Assert 'S12.cancel_sem_motivo_400' ($r.Code -eq 400 -and $r.Body -match 'validation_reason') ('code=' + $r.Code + ' ' + (Snip $r.Body))
    $r = Invoke-Api 'POST' ($api + "/api/v1/organizations/$TC/solicitations/$smallId/actions") $cliTok (ToJson @{ action='cancelar'; text='Demanda desistida pelo proprio time interno.'; rowVersion=$rvSmall })
    Assert 'S12.cancel_200' ($r.Code -eq 200) ('code=' + $r.Code + ' ' + (Snip $r.Body))
    $j = JBody $r.Body
    Assert 'S12.status_cancelada' ((JGet $j 'status') -eq 'cancelada') (Snip $r.Body)
    Assert 'S12.cancellation_reason_saved' ((JGet $j 'cancellationReason') -match 'desistida') (Snip $r.Body)
    $cev = Sql "SELECT count(*) FROM odca.solicitation_events WHERE solicitation_id='$smallId' AND event_type='cancelamento';"
    Assert 'S12.cancel_event' ([int]$cev -ge 1) "rows=$cev"

    # ---- S13: listagens e filtros (tenant + plataforma) --------------------------------
    $all = @(JBody (Invoke-Api 'GET' ($api + "/api/v1/organizations/$TC/solicitations") $cliTok $null).Body)
    Assert 'S13.list_rows' (@($all | Where-Object { (JGet $_ 'id') -eq $solId }).Count -eq 1 -and @($all | Where-Object { (JGet $_ 'id') -eq $smallId }).Count -eq 1) (Snip ($all | ConvertTo-Json -Compress -Depth 3))
    $closed = @(JBody (Invoke-Api 'GET' ($api + "/api/v1/organizations/$TC/solicitations?status=encerrada") $cliTok $null).Body)
    Assert 'S13.filter_status' (@($closed | Where-Object { (JGet $_ 'id') -eq $solId }).Count -eq 1) (Snip ($closed | ConvertTo-Json -Compress -Depth 3))
    $svc = @(JBody (Invoke-Api 'GET' ($api + "/api/v1/organizations/$TC/solicitations?service=adaptacao") $cliTok $null).Body)
    Assert 'S13.filter_service' (@($svc | Where-Object { (JGet $_ 'id') -eq $smallId }).Count -eq 1) (Snip ($svc | ConvertTo-Json -Compress -Depth 3))
    $srch = [uri]::EscapeDataString('cancelamento com motivo')
    $sc = @(JBody (Invoke-Api 'GET' ($api + "/api/v1/organizations/$TC/solicitations?search=$srch") $cliTok $null).Body)
    Assert 'S13.search_subject' (@($sc | Where-Object { (JGet $_ 'id') -eq $smallId }).Count -eq 1) (Snip ($sc | ConvertTo-Json -Compress -Depth 3))
    $plat = @(JBody (Invoke-Api 'GET' ($api + '/api/v1/platform/solicitations') $opTok $null).Body)
    $prow = $plat | Where-Object { (JGet $_ 'id') -eq $solId } | Select-Object -First 1
    Assert 'S13.platform_queue_contains' ($null -ne $prow) (Snip ($plat | ConvertTo-Json -Compress -Depth 3))
    Assert 'S13.platform_org_column' ((JGet $prow 'organizationName') -ne '') (Snip ($prow | ConvertTo-Json -Compress))
    $otherTenant = [guid]::NewGuid().ToString()
    $platOther = @(JBody (Invoke-Api 'GET' ($api + "/api/v1/platform/solicitations?tenantId=$otherTenant") $opTok $null).Body)
    Assert 'S13.platform_tenant_filter' (@($platOther | Where-Object { (JGet $_ 'id') -eq $solId }).Count -eq 0) (Snip ($platOther | ConvertTo-Json -Compress -Depth 3))
    $p403 = Invoke-Api 'GET' ($api + '/api/v1/platform/solicitations') $cliTok $null
    Assert 'S13.platform_requires_admin' ($p403.Code -eq 403 -or $p403.Code -eq 401) ('code=' + $p403.Code)

    # ---- S14: violacoes de SLA marcadas uma unica vez ------------------------------------
    $r = Invoke-Api 'POST' ($api + "/api/v1/organizations/$TC/solicitations") $cliTok (ToJson @{
        service='revisao'; priority='baixa'; subject='Val C: violacao de SLA forcada'; body='Solicitacao deslocada no tempo para disparar a varredura de violacoes.'; idempotencyKey=[guid]::NewGuid().ToString() })
    $brkId = JGet (JBody $r.Body) 'id'
    Sql "UPDATE odca.solicitations SET opened_at=now()-interval '40 days', first_response_due_at=now()-interval '39 days', resolution_due_at=now()-interval '30 days' WHERE id='$brkId';" | Out-Null
    Sql 'SELECT odca.sla_scan_violations();' | Out-Null
    $b1 = Sql "SELECT coalesce(first_response_breach_at::text,'none') FROM odca.solicitations WHERE id='$brkId';"
    $b2 = Sql "SELECT coalesce(resolution_breach_at::text,'none') FROM odca.solicitations WHERE id='$brkId';"
    Assert 'S14.first_breach_marked' ($b1 -ne 'none') "b1=$b1"
    Assert 'S14.resolution_breach_marked' ($b2 -ne 'none') "b2=$b2"
    Sql 'SELECT odca.sla_scan_violations();' | Out-Null
    $b1b = Sql "SELECT coalesce(first_response_breach_at::text,'none') FROM odca.solicitations WHERE id='$brkId';"
    $b2b = Sql "SELECT coalesce(resolution_breach_at::text,'none') FROM odca.solicitations WHERE id='$brkId';"
    Assert 'S14.breach_set_once' ($b1b -eq $b1 -and $b2b -eq $b2) ("b1=$b1/$b1b b2=$b2/$b2b")
    $bev = Sql "SELECT count(*) FROM odca.solicitation_events WHERE solicitation_id='$brkId' AND event_type LIKE 'violacao_%';"
    Assert 'S14.breach_events' ([int]$bev -ge 2) "rows=$bev"
    $platBrk = @(JBody (Invoke-Api 'GET' ($api + '/api/v1/platform/solicitations') $opTok $null).Body) | Where-Object { (JGet $_ 'id') -eq $brkId } | Select-Object -First 1
    Assert 'S14.breach_visible_in_queue' ((JGet $platBrk 'firstResponseBreachAt') -ne '') (Snip ($platBrk | ConvertTo-Json -Compress))

    # ---- S15: relogio de dias uteis ---------------------------------------------------------
    $span = Sql "SELECT EXTRACT(epoch FROM (odca.sla_add_business_minutes('2026-10-09 16:00-03'::timestamptz, 600, 'America/Sao_Paulo', 'dias_uteis', '09:00', '18:00') - '2026-10-09 16:00-03'::timestamptz))/3600.0;"
    Assert 'S15.outside_window_deferred' ([double]$span -ge 26) "hours=$span"
    $wknd = Sql "SELECT EXTRACT(epoch FROM (odca.sla_add_business_minutes('2026-10-10 10:00-03'::timestamptz, 60, 'America/Sao_Paulo', 'dias_uteis', '09:00', '18:00') - '2026-10-10 10:00-03'::timestamptz))/3600.0;"
    Assert 'S15.weekend_skipped' ([double]$wknd -ge 23) "hours=$wknd"
    $cont = Sql "SELECT odca.sla_add_business_minutes('2026-10-09 22:00-03'::timestamptz, 60, 'America/Sao_Paulo', 'continuo', '09:00', '18:00') AT TIME ZONE 'America/Sao_Paulo';"
    Assert 'S15.continuous_calendar' ($cont -match '23:00') "res=$cont"

    # ---- S16: aprovacao ODCA de modelo cirurgico ---------------------------------------------
    $inst = [int](Sql "SELECT count(*) FROM odca.contract_templates WHERE owner_tenant_id='$TC' AND official_key='surgical-consent' AND status<>'archived';")
    Assert 'S16.official_installed' ($inst -ge 1) "rows=$inst"
    $q = Invoke-Api 'GET' ($api + '/api/v1/platform/template-approvals') $opTok $null
    Assert 'S16.queue_200' ($q.Code -eq 200) ('code=' + $q.Code + ' ' + (Snip $q.Body))
    $row = @(JBody $q.Body) | Where-Object { (JGet $_ 'officialKey') -eq 'surgical-consent' -and (JGet $_ 'tenantId') -eq $TC } | Select-Object -First 1
    Assert 'S16.queue_has_surgical' ($null -ne $row) (Snip $q.Body)
    Assert 'S16.pending_by_default' ((JGet $row 'decision') -eq '') (Snip ($row | ConvertTo-Json -Compress))
    $r = Invoke-Api 'POST' ($api + "/api/v1/platform/template-approvals/$TC/surgical-consent/decision") $opTok (ToJson @{ decision='aprovado'; note='Modelo revisado pelo comite clinico da ODCA.' })
    Assert 'S16.decide_aprovado_200' ($r.Code -eq 200) ('code=' + $r.Code + ' ' + (Snip $r.Body))
    $row2 = @(JBody (Invoke-Api 'GET' ($api + '/api/v1/platform/template-approvals') $opTok $null).Body) | Where-Object { (JGet $_ 'officialKey') -eq 'surgical-consent' -and (JGet $_ 'tenantId') -eq $TC } | Select-Object -First 1
    Assert 'S16.approval_persisted' ((JGet $row2 'decision') -eq 'aprovado') (Snip ($row2 | ConvertTo-Json -Compress))
    Assert 'S16.decider_attributed' ((JGet $row2 'decidedByName') -ne '') (Snip ($row2 | ConvertTo-Json -Compress))
    $r = Invoke-Api 'POST' ($api + "/api/v1/platform/template-approvals/$TC/surgical-consent/decision") $opTok (ToJson @{ decision='reprovado'; note='oi' })
    Assert 'S16.reject_short_note_400' ($r.Code -eq 400 -and $r.Body -match 'validation_note') ('code=' + $r.Code + ' ' + (Snip $r.Body))
    $r = Invoke-Api 'POST' ($api + "/api/v1/platform/template-approvals/$TC/surgical-consent/decision") $opTok (ToJson @{ decision='reprovado'; note='Clausa de risco sem adequacao ao CFM.' })
    Assert 'S16.reject_with_note_200' ($r.Code -eq 200) ('code=' + $r.Code + ' ' + (Snip $r.Body))
    $row3 = @(JBody (Invoke-Api 'GET' ($api + '/api/v1/platform/template-approvals') $opTok $null).Body) | Where-Object { (JGet $_ 'officialKey') -eq 'surgical-consent' -and (JGet $_ 'tenantId') -eq $TC } | Select-Object -First 1
    Assert 'S16.rejection_shows_note' ((JGet $row3 'decisionNote') -match 'Clausa') (Snip ($row3 | ConvertTo-Json -Compress))
    $aud = [int](Sql "SELECT count(*) FROM odca.audit_events WHERE action LIKE 'odca.template_approval.%';")
    Assert 'S16.audit_logged' ($aud -ge 2) "rows=$aud"
    $noPerm = Invoke-Api 'POST' ($api + "/api/v1/platform/template-approvals/$TC/surgical-consent/decision") $cliTok (ToJson @{ decision='aprovado'; note='Tentativa do cliente aprovar sozinho.' })
    Assert 'S16.odca_only_decision' ($noPerm.Code -eq 403 -or $noPerm.Code -eq 401) ('code=' + $noPerm.Code)

    # ---- S17: gate de prontidao acompanha o estado da aprovacao ------------------------------
    Sql "DELETE FROM odca.template_approvals WHERE tenant_id='$TC' AND official_key='surgical-consent';" | Out-Null
    $rowP = @(JBody (Invoke-Api 'GET' ($api + '/api/v1/platform/template-approvals') $opTok $null).Body) | Where-Object { (JGet $_ 'officialKey') -eq 'surgical-consent' -and (JGet $_ 'tenantId') -eq $TC } | Select-Object -First 1
    Assert 'S17.back_to_pending' ((JGet $rowP 'decision') -eq '') (Snip ($rowP | ConvertTo-Json -Compress))
    $pendBlock = [int](Sql "SELECT count(*) FROM odca.contract_templates ct LEFT JOIN odca.template_approvals ta ON ta.tenant_id=ct.owner_tenant_id AND ta.official_key=ct.official_key WHERE ct.owner_tenant_id='$TC' AND ct.official_key='surgical-consent' AND ct.status<>'archived' AND coalesce(ta.decision,'pendente')<>'aprovado';")
    Assert 'S17.readiness_blocks_when_not_approved' ($pendBlock -eq 1) "rows=$pendBlock"
    $r = Invoke-Api 'POST' ($api + "/api/v1/platform/template-approvals/$TC/surgical-consent/decision") $opTok (ToJson @{ decision='aprovado'; note='Liberacao final apos checagem do gate.' })
    Assert 'S17.final_approval_200' ($r.Code -eq 200) ('code=' + $r.Code + ' ' + (Snip $r.Body))
    $okBlock = [int](Sql "SELECT count(*) FROM odca.contract_templates ct JOIN odca.template_approvals ta ON ta.tenant_id=ct.owner_tenant_id AND ta.official_key=ct.official_key WHERE ct.owner_tenant_id='$TC' AND ct.official_key='surgical-consent' AND ct.status<>'archived' AND ta.decision='aprovado';")
    Assert 'S17.readiness_lifts_on_approval' ($okBlock -eq 1) "rows=$okBlock"

# ---- Cleanup ------------------------------------------------------------------------------
} catch {
    $script:unhandled = $_.Exception.Message
    Assert 'S?.execucao_sem_excecao' $false $script:unhandled
} finally {
    Write-Host '--- cleanup final (best effort) + verificacao do seed ---'
    SqlOpt "UPDATE odca.subscriptions SET plan_version_id='$($script:planBefore)' WHERE tenant_id='$TC';" | Out-Null
    SqlOpt @"
DELETE FROM odca.solicitation_events WHERE solicitation_id IN (SELECT id FROM odca.solicitations WHERE subject LIKE 'Val C:%');
DELETE FROM odca.solicitation_messages WHERE solicitation_id IN (SELECT id FROM odca.solicitations WHERE subject LIKE 'Val C:%');
DELETE FROM odca.solicitation_pauses WHERE solicitation_id IN (SELECT id FROM odca.solicitations WHERE subject LIKE 'Val C:%');
DELETE FROM odca.solicitations WHERE subject LIKE 'Val C:%';
DELETE FROM odca.template_approvals WHERE tenant_id='$TC' AND official_key='surgical-consent';
"@ | Out-Null
    SqlOpt @"
DELETE FROM odca.sessions WHERE user_id='$QA_OP';
DELETE FROM odca.mfa_recovery_codes WHERE user_id='$QA_OP';
DELETE FROM odca.resource_movements WHERE actor_user_id='$QA_OP';
DELETE FROM odca.audit_events WHERE actor_user_id='$QA_OP';
UPDATE odca.users SET deleted_by=NULL WHERE id='$QA_OP';
DELETE FROM odca.users WHERE id='$QA_OP';
"@ | Out-Null
    $left = SqlOpt "SELECT count(*) FROM odca.solicitations WHERE subject LIKE 'Val C:%';"
    if ($left -ne '') { Assert 'cleanup.solicitacoes_removidas' ([int]$left -eq 0) "restantes=$left" }
    $usersLeft = SqlOpt "SELECT count(*) FROM odca.users WHERE id='$QA_OP';"
    if ($usersLeft -ne '') { Assert 'cleanup.usuario_qa_removido' ([int]$usersLeft -eq 0) "restantes=$usersLeft" }
    Assert 'cleanup.final' $true ''

    if ($script:seedBefore) {
        $seedAfter = Sql @"
SELECT email_normalized || '|' || password_hash || '|' || coalesce(mfa_secret_protected,'none') || '|'
  || coalesce(mfa_confirmed_at::text,'none') || '|' || coalesce(mfa_pending_since::text,'none') || '|'
  || failed_login_count || '|' || security_version || '|' || is_platform_administrator || '|'
  || (SELECT count(*) FROM odca.mfa_recovery_codes rc WHERE rc.user_id=u.id)
FROM odca.users u
WHERE email_normalized IN ('ADMIN@ODCA.LOCAL','OPERADOR@ODCA.LOCAL','CLIENTE.TESTE@ODCA.LOCAL')
ORDER BY email_normalized;
"@
        Assert 'cleanup.seed_intacto' ($seedAfter -eq $script:seedBefore) "apos=`n$seedAfter"
    }
}

Write-Host ''
Write-Host ('=== validate-c-web: PASS=' + $script:passes + ' FAIL=' + $script:failures + ' ===')
if ($script:failures -gt 0) { exit 1 }
