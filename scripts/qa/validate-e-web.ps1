# Estabilização A (Bloco A) — Suite E: modalidade de assinatura, vínculo de identidade,
# MFA de sessão, aprovação de modelos por revisão e revisão interna.
# Convenções: mesmo padrão das suítes B.3.4/C (HttpWebRequest + psql temp-file),
# identidade QA própria (D-A5), guard de banco, fingerprint de seed e try/finally.
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }
# PS 5.1 decodifica saida de comandos nativos (psql) via Console.OutputEncoding;
# sem isto os literais pt-BR retornados do banco chegam corrompidos (CP850).
[Console]::OutputEncoding = [Text.Encoding]::UTF8

$web   = 'https://localhost:7144'
$api   = 'https://localhost:7143'
$psql  = 'C:\Program Files\PostgreSQL\18\bin\psql.exe'
$db    = 'odca_test_disposable'
$TC    = '20000000-0000-4000-8000-000000000001'
$QA_PWD = 'La@RsjC5p-hmq_JpReGW'   # mesma senha do operador (hash clonado no setup)
$BC_SEED = '12345678000195'        # business_code do seed (database/development/seed-test-access.sql)
$ENT_ID  = '30000000-0000-0000-0000-000000000013'  # plano enterprise (module.reviews habilitado)

$G_SIGNER   = '4e000000-0000-4000-8000-000000000001'
$G_RECORDER = '4e000000-0000-4000-8000-000000000002'
$G_REVIEWER = '4e000000-0000-4000-8000-000000000003'
$G_ADMIN    = '4e000000-0000-4000-8000-000000000004'

$script:passes = 0
$script:failures = 0
$script:lastUrl = ''
$script:seedBefore = $null
$script:bcBefore = $null
$script:profileBefore = $null
$script:unhandled = ''

function Assert([string]$name, [bool]$cond, [string]$detail) {
    if ($cond) { $script:passes++; Write-Host ('PASS  ' + $name) }
    else {
        Write-Host ('FAIL  ' + $name)
        if ($detail) { Write-Host ('      observed: ' + $detail.Substring(0, [Math]::Min(400, $detail.Length))) }
        $script:failures++
    }
}

function Snip([string]$s) { if (-not $s) { return '' } return $s.Substring(0, [Math]::Min(400, $s.Length)) }

function De([string]$h) {
    if (-not $h) { return '' }
    $s = [regex]::Replace($h, '&#[xX]([0-9a-fA-F]+);', { param($m) [char][Convert]::ToInt32($m.Groups[1].Value, 16) })
    $s = [regex]::Replace($s, '&#(\d+);', { param($m) [char][int]$m.Groups[1].Value })
    return ($s.Replace('&amp;', '&').Replace('&lt;', '<').Replace('&gt;', '>').Replace('&quot;', '"').Replace('&apos;', "'"))
}

function Dec([string]$s) {
    if ([string]::IsNullOrEmpty($s)) { return '' }
    return [regex]::Replace($s, '\\u([0-9a-fA-F]{4})', { param($m) [string][char][Convert]::ToInt32($m.Groups[1].Value, 16) })
}

function Esc([string]$s) { return [Uri]::EscapeDataString($s) }

function Sql([string]$sql) {
    $tmp = Join-Path $env:TEMP ('odca-e-' + [guid]::NewGuid().ToString('N') + '.sql')
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

# Batch de cleanup sem ON_ERROR_STOP: cada instrucao roda mesmo se outra falhar.
# Assim uma dependencia de schema desconhecida nao impede a limpeza do restante;
# os invariantes finais sao checados pelos asserts apos a chamada.
function SqlBest([string]$sql) {
    $tmp = Join-Path $env:TEMP ('odca-e-' + [guid]::NewGuid().ToString('N') + '.sql')
    [IO.File]::WriteAllText($tmp, $sql, (New-Object System.Text.UTF8Encoding($false)))
    $prev = $env:PGPASSWORD; $env:PGPASSWORD = '123456'
    $out = & $psql -U postgres -d $db -P pager=off -t -A -q -f $tmp 2>&1
    $code = $LASTEXITCODE
    $env:PGPASSWORD = $prev
    Remove-Item $tmp -EA SilentlyContinue
    if ($code -ne 0) { $t = (($out | Out-String)).Trim(); Write-Host ('      [warn] batch parcial: ' + $t.Substring(0, [Math]::Min(300, $t.Length))) }
    return ''
}

# Limpeza completa da pegada da suite (docs/modelos da suita + ator QA), em ordem
# de dependencia (filhos antes dos pais). Usada no inicio (residuos de execucao
# anterior interrompida) e no finally.
function Get-SuiteCleanupSql {
    $vset = "(SELECT id FROM odca.generated_contract_versions WHERE (contract_id IN (SELECT id FROM odca.contracts WHERE tenant_id='$TC' AND title LIKE 'Val E:%')) OR (source_template_id IN (SELECT id FROM odca.contract_templates WHERE owner_tenant_id='$TC' AND official_key='surgical-consent')))"
    $dset = "(SELECT id FROM odca.contract_drafts WHERE (contract_id IN (SELECT id FROM odca.contracts WHERE tenant_id='$TC' AND title LIKE 'Val E:%')) OR (source_template_id IN (SELECT id FROM odca.contract_templates WHERE owner_tenant_id='$TC' AND official_key='surgical-consent')))"
    $qa = "'$G_SIGNER','$G_RECORDER','$G_REVIEWER','$G_ADMIN'"
    return @"
DELETE FROM odca.contract_review_notifications WHERE review_id IN (SELECT id FROM odca.contract_review_requests WHERE generated_version_id IN $vset);
DELETE FROM odca.contract_review_comments WHERE review_id IN (SELECT id FROM odca.contract_review_requests WHERE generated_version_id IN $vset);
DELETE FROM odca.contract_review_events WHERE review_id IN (SELECT id FROM odca.contract_review_requests WHERE generated_version_id IN $vset);
DELETE FROM odca.contract_review_steps WHERE review_id IN (SELECT id FROM odca.contract_review_requests WHERE generated_version_id IN $vset);
DELETE FROM odca.contract_review_requests WHERE generated_version_id IN $vset;
DELETE FROM odca.signature_participants WHERE preparation_id IN (SELECT id FROM odca.signature_preparations WHERE generated_version_id IN $vset);
DELETE FROM odca.signature_preparation_operations WHERE preparation_id IN (SELECT id FROM odca.signature_preparations WHERE generated_version_id IN $vset);
DELETE FROM odca.signature_preparation_events WHERE preparation_id IN (SELECT id FROM odca.signature_preparations WHERE generated_version_id IN $vset);
DELETE FROM odca.signature_preparations WHERE generated_version_id IN $vset;
DELETE FROM odca.document_versions WHERE uploaded_by IN ($qa);
DELETE FROM odca.contract_documents WHERE created_by IN ($qa) OR deleted_by IN ($qa);
DELETE FROM odca.draft_save_receipts WHERE draft_id IN $dset;
DELETE FROM odca.generated_contract_versions WHERE id IN $vset;
DELETE FROM odca.contract_drafts WHERE id IN $dset;
DELETE FROM odca.contract_events WHERE contract_id IN (SELECT id FROM odca.contracts WHERE tenant_id='$TC' AND title LIKE 'Val E:%');
DELETE FROM odca.contracts WHERE tenant_id='$TC' AND title LIKE 'Val E:%';
DELETE FROM odca.template_approvals WHERE tenant_id='$TC' AND official_key='surgical-consent';
DELETE FROM odca.contract_template_access WHERE granted_by IN ($qa) OR revoked_by IN ($qa);
DELETE FROM odca.resource_movements WHERE actor_user_id IN ($qa);
DELETE FROM odca.tenant_invitations WHERE created_by IN ($qa);
DELETE FROM odca.sessions WHERE user_id IN ($qa);
DELETE FROM odca.mfa_recovery_codes WHERE user_id IN ($qa);
DELETE FROM odca.member_roles WHERE user_id IN ($qa) OR assigned_by IN ($qa);
DELETE FROM odca.memberships WHERE user_id IN ($qa);
DELETE FROM odca.audit_events WHERE actor_user_id IN ($qa);
UPDATE odca.users SET deleted_by=NULL WHERE id IN ($qa);
DELETE FROM odca.contract_template_versions WHERE template_id IN (SELECT id FROM odca.contract_templates WHERE owner_tenant_id='$TC' AND official_key='surgical-consent');
DELETE FROM odca.contract_templates WHERE owner_tenant_id='$TC' AND official_key='surgical-consent';
DELETE FROM odca.users WHERE id IN ($qa);
"@
}

function JGet($obj, [string]$prop) {
    $p = $obj.PSObject.Properties[$prop]
    if ($null -eq $p -or $null -eq $p.Value) { return '' }
    return ('' + $p.Value)
}

function ToJson($obj) { return (ConvertTo-Json -InputObject $obj -Depth 40 -Compress) }

function New-CookieJar { return (New-Object System.Net.CookieContainer) }

function Invoke-Page([string]$method, [string]$url, [string]$body, $jar) {
    $req = [System.Net.HttpWebRequest]::Create($url)
    $req.Method = $method
    $req.CookieContainer = $jar
    $req.AllowAutoRedirect = $true
    $req.Timeout = 60000
    if ($body) {
        $req.ContentType = 'application/x-www-form-urlencoded'
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($body)
        $req.ContentLength = $bytes.Length
        $s = $req.GetRequestStream(); $s.Write($bytes, 0, $bytes.Length); $s.Close()
    }
    try { $resp = $req.GetResponse() } catch [System.Net.WebException] { $resp = $_.Exception.Response }
    $script:lastUrl = $resp.ResponseUri.ToString()
    $ms = New-Object IO.MemoryStream
    $resp.GetResponseStream().CopyTo($ms)
    $resp.Close()
    $html = De([Text.Encoding]::UTF8.GetString($ms.ToArray()))
    Write-Host ('{0} {1} -> {2} | last={3}' -f $method, $url.Substring(0, [Math]::Min(95, $url.Length)), [int]$resp.StatusCode, $script:lastUrl.Substring(0, [Math]::Min(95, $script:lastUrl.Length)))
    return ,$html
}

function Get-Token([string]$html) {
    $m = [regex]::Match($html, 'name="__RequestVerificationToken"[^>]*value="([^"]+)"')
    if (-not $m.Success) { throw 'antiforgery token nao encontrado na pagina' }
    return $m.Groups[1].Value
}

function Login-Web([string]$loginName, [string]$password) {
    $jar = New-CookieJar
    $h = Invoke-Page 'GET' ($web + '/entrar') '' $jar
    $t = Get-Token $h
    $b = 'Login=' + (Esc $loginName) + '&Password=' + (Esc $password) + '&__RequestVerificationToken=' + (Esc $t)
    $h = Invoke-Page 'POST' ($web + '/entrar') $b $jar
    return @{ Jar = $jar; Html = $h }
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
    Write-Host ('  api {0} {1} -> {2}' -f $method, $url.Substring($api.Length), $code)
    return @{ Code = $code; Body = $text }
}

function Invoke-ApiBytes([string]$method, [string]$url, [string]$tok, [string]$body) {
    $req = [System.Net.HttpWebRequest]::Create($url)
    $req.Method = $method
    $req.Accept = 'application/pdf'
    $req.Timeout = 60000
    if ($tok) { $req.Headers.Add('Authorization', 'Bearer ' + $tok) }
    if ($body) {
        $req.ContentType = 'application/json; charset=utf-8'
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($body)
        $req.ContentLength = $bytes.Length
        $s = $req.GetRequestStream(); $s.Write($bytes, 0, $bytes.Length); $s.Close()
    }
    try { $resp = $req.GetResponse() } catch [System.Net.WebException] { $resp = $_.Exception.Response }
    $code = 0
    $raw = New-Object 'byte[]' 0
    if ($resp) {
        $code = [int]$resp.StatusCode
        $ms = New-Object IO.MemoryStream
        $resp.GetResponseStream().CopyTo($ms)
        $resp.Close()
        $raw = $ms.ToArray()
    }
    return @{ Code = $code; Bytes = $raw }
}

function Sha256Hex($bytes) {
    $h = [System.Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($h.ComputeHash([byte[]]$bytes)).Replace('-', '').ToLowerInvariant() }
    finally { $h.Dispose() }
}

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

function Wait-TotpWindow([string]$userId) {
    # MfaService rejeita TOTP cujo passo ja foi consumido (anti-reuso de codigo):
    # a inscricao no setup consumiu um passo, entao o desafio na mesma janela falha.
    # Aguarda (no maximo ~32 s) a janela virar para que o codigo seja novo.
    for ($i = 0; $i -lt 64; $i++) {
        $last = Sql "SELECT coalesce(mfa_last_accepted_time_step,-1) FROM odca.users WHERE id='$userId';"
        $cur = [long][Math]::Floor([DateTimeOffset]::UtcNow.ToUnixTimeSeconds() / 30)
        if ([long]$last -lt $cur) { return }
        Start-Sleep -Milliseconds 500
    }
}

function Login-Plain([string]$loginName, [string]$pwd) {
    $body = ToJson @{ login = $loginName; password = $pwd }
    for ($try = 1; $try -le 4; $try++) {
        $r = Invoke-Api 'POST' ($api + '/api/v1/auth/login') $null $body
        if ($r.Code -eq 200) { return $r }
        Write-Host ('      login tentativa ' + $try + ' falhou (' + $r.Code + '), aguardando rate limit...')
        Start-Sleep -Seconds 65
    }
    throw ('login ' + $loginName + ' falhou: ' + $r.Code + ' ' + (Snip $r.Body))
}

function Login-MfaAdmin([string]$loginName, [string]$pwd) {
    # Administrador de plataforma precisa de sessao MFA (politica PlatformAdministrator).
    # A inscricao TOTP e concluida aqui pelos endpoints proprios da API.
    $r = Login-Plain $loginName $pwd
    $j = $r.Body | ConvertFrom-Json
    $tok = $j.accessToken
    if ((JGet $j 'requiresMfaEnrollment') -ne 'True') {
        if ((JGet $j 'mfaVerified') -eq 'True') { return $tok }
        throw ('esperava requiresMfaEnrollment para ' + $loginName + ': ' + (Snip $r.Body))
    }
    $e = Invoke-Api 'POST' ($api + '/api/v1/auth/mfa/enrollment') $tok $null
    if ($e.Code -ne 200) { throw ('mfa/enrollment falhou: ' + $e.Code + ' ' + (Snip $e.Body)) }
    $secret = JGet ($e.Body | ConvertFrom-Json) 'manualKey'
    $script:lastMfaManualKey = $secret
    $code = Get-TotpCode $secret
    $c = Invoke-Api 'POST' ($api + '/api/v1/auth/mfa/enrollment/confirm') $tok (ToJson @{ code = $code })
    if ($c.Code -ne 200) { throw ('mfa/confirm falhou: ' + $c.Code + ' ' + (Snip $c.Body)) }
    $vtok = (JGet ($c.Body | ConvertFrom-Json) 'accessToken')
    if (-not $vtok) { throw ('mfa/confirm sem token: ' + (Snip $c.Body)) }
    return $vtok
}

function New-Draft([string]$templateId, [string]$title) {
    $body = ToJson @{
        templateId = $templateId; title = $title; reference = $null
        patientId = $null; contractId = $null; changeRequestId = $null
        purpose = $null; contractorSource = $null
    }
    return Invoke-Api 'POST' ("$api/api/v1/organizations/$TC/studio/drafts") $script:tok $body
}

function Get-Draft([string]$draftId) {
    $r = Invoke-Api 'GET' ("$api/api/v1/organizations/$TC/studio/drafts/$draftId") $script:tok $null
    if ($r.Code -ne 200) { throw ('GET minuta falhou: ' + $r.Code + ' ' + (Snip $r.Body)) }
    $d = $r.Body | ConvertFrom-Json
    $list = New-Object System.Collections.ArrayList
    foreach ($v in @($d.values)) { if ($null -ne $v) { [void]$list.Add($v) } }
    $d.values = $list
    return $d
}

function Save-Draft([string]$draftId, $d) {
    $payload = @{
        content = $d.content
        fields = $d.fields
        values = @($d.values)
        expectedVersion = [int64]$d.version
        clientRevision = [guid]::NewGuid().ToString()
    }
    return Invoke-Api 'PUT' ("$api/api/v1/organizations/$TC/studio/drafts/$draftId") $script:tok (ToJson $payload)
}

function Get-Val($vals, [string]$id) {
    foreach ($v in $vals) { if (('' + $v.fieldId) -eq $id) { return $v } }
    return $null
}

function Set-Val($vals, [string]$id, [string]$value) {
    $e = Get-Val $vals $id
    if ($null -eq $e) {
        $vals.Add(@{ fieldId = $id; value = $value; confirmed = $true; source = 'manual' }) | Out-Null
    }
    else {
        $e.value = $value
        $e.confirmed = $true
    }
}

function Confirm-Values($vals) {
    foreach ($v in $vals) {
        if (-not [string]::IsNullOrWhiteSpace([string]$v.value)) { $v.confirmed = $true }
    }
}

function Generate-Version([string]$draftId, $d) {
    $body = ToJson @{ idempotencyKey = [guid]::NewGuid().ToString(); expectedVersion = [int64]$d.version }
    return Invoke-Api 'POST' ("$api/api/v1/organizations/$TC/studio/drafts/$draftId/versions") $script:tok $body
}

function Get-Readiness([string]$versionId) {
    $r = Invoke-Api 'GET' ("$api/api/v1/organizations/$TC/studio/versions/$versionId/signature-preparation/readiness") $script:tok $null
    if ($r.Code -ne 200) { return $null }
    $j = $r.Body | ConvertFrom-Json
    return @{
        canConfirm = (JGet $j 'canConfirm')
        blockers = @($j.blockers | ForEach-Object { JGet $_ 'code' })
        ok = @($j.satisfied | ForEach-Object { JGet $_ 'code' })
        raw = $j
    }
}

function Fill-Informed($d) {
    Set-Val $d.values 'organization_name' (Sql "SELECT display_name FROM odca.tenants WHERE id='$TC';")
    Set-Val $d.values 'patient_name' 'Paciente Val E'
    Set-Val $d.values 'patient_document' '11144477735'
    Set-Val $d.values 'purpose' 'Tratamento clínico de rotina.'
    Set-Val $d.values 'has_representative' 'Não'
    Set-Val $d.values 'consent_date' '2026-10-10'
    Set-Val $d.values 'image_consent_option' 'Não autorizo uso de imagem'
    Confirm-Values $d.values
}

function Fill-Surgical($d) {
    Set-Val $d.values 'organization_name' (Sql "SELECT display_name FROM odca.tenants WHERE id='$TC';")
    Set-Val $d.values 'surgeon_name' 'Dra. Marina Cirurgia E'
    Set-Val $d.values 'surgeon_document' '11144477735'
    Set-Val $d.values 'patient_name' 'Paciente Teste E'
    Set-Val $d.values 'patient_document' '52998224725'
    Set-Val $d.values 'procedure_description' 'Rinoplastia funcional com septoplastia.'
    Set-Val $d.values 'surgery_date' '2027-03-15'
    Set-Val $d.values 'facility_name' 'Hospital Central E'
    Set-Val $d.values 'anesthesia_type' 'Anestesia geral'
    Set-Val $d.values 'has_representative' 'Não'
    Set-Val $d.values 'procedure_fee' '1234.56'
    Set-Val $d.values 'payment_terms' 'À vista'
    Set-Val $d.values 'risks_acknowledgement' 'Li e compreendi os riscos descritos'
    Set-Val $d.values 'signing_date' '2026-10-10'
    Set-Val $d.values 'jurisdiction_city' 'Belém/PA'
    Confirm-Values $d.values
}

Write-Host '=== ESTABILIZACAO A (BLOCO A) — SUITE E ==='

try {
    # ---------- S0: guard, fingerprint, limpeza e identidades QA ----------
    $curDb = Sql 'SELECT current_database();'
    Assert 'S0.current_database' ($curDb -eq $db) "db=$curDb"

    $seedBefore = Sql @"
SELECT email_normalized || '|' || password_hash || '|' || coalesce(mfa_secret_protected,'none') || '|'
  || coalesce(mfa_confirmed_at::text,'none') || '|' || coalesce(mfa_pending_since::text,'none') || '|'
  || failed_login_count || '|' || security_version || '|' || is_platform_administrator || '|'
  || (SELECT count(*) FROM odca.mfa_recovery_codes rc WHERE rc.user_id=u.id)
FROM odca.users u
WHERE email_normalized IN ('ADMIN@ODCA.LOCAL','OPERADOR@ODCA.LOCAL','CLIENTE.TESTE@ODCA.LOCAL')
ORDER BY email_normalized;
"@
    $script:seedBefore = $seedBefore
    $script:bcBefore = Sql "SELECT business_code FROM odca.tenants WHERE id='$TC';"
    $script:profileBefore = Sql "SELECT activity_profile FROM odca.tenants WHERE id='$TC';"
    $script:planBefore = Sql "SELECT plan_version_id FROM odca.subscriptions WHERE tenant_id='$TC' AND status='active';"

    Write-Host '--- S0 limpeza de execucoes anteriores (best effort) ---'
    $null = SqlBest (Get-SuiteCleanupSql)

    Write-Host '--- S0 criacao das identidades QA (senha clonada do operador) ---'
    $null = Sql @"
INSERT INTO odca.users(id,email,email_normalized,login_normalized,display_name,password_hash,must_change_password,is_platform_administrator)
SELECT '$G_SIGNER','qa-e-signer@odca.local','QA-E-SIGNER@ODCA.LOCAL','qa-e-signer','QA E Signer',h.password_hash,false,true
FROM odca.users h WHERE h.email_normalized='OPERADOR@ODCA.LOCAL'
AND NOT EXISTS(SELECT 1 FROM odca.users u WHERE u.id='$G_SIGNER');
INSERT INTO odca.users(id,email,email_normalized,login_normalized,display_name,password_hash,must_change_password,is_platform_administrator)
SELECT '$G_RECORDER','qa-e-recorder@odca.local','QA-E-RECORDER@ODCA.LOCAL','qa-e-recorder','QA E Recorder',h.password_hash,false,false
FROM odca.users h WHERE h.email_normalized='OPERADOR@ODCA.LOCAL'
AND NOT EXISTS(SELECT 1 FROM odca.users u WHERE u.id='$G_RECORDER');
INSERT INTO odca.users(id,email,email_normalized,login_normalized,display_name,password_hash,must_change_password,is_platform_administrator)
SELECT '$G_REVIEWER','qa-e-reviewer@odca.local','QA-E-REVIEWER@ODCA.LOCAL','qa-e-reviewer','QA E Reviewer',h.password_hash,false,false
FROM odca.users h WHERE h.email_normalized='OPERADOR@ODCA.LOCAL'
AND NOT EXISTS(SELECT 1 FROM odca.users u WHERE u.id='$G_REVIEWER');
INSERT INTO odca.users(id,email,email_normalized,login_normalized,display_name,password_hash,must_change_password,is_platform_administrator)
SELECT '$G_ADMIN','qa-e-admin@odca.local','QA-E-ADMIN@ODCA.LOCAL','qa-e-admin','QA E Admin',h.password_hash,false,true
FROM odca.users h WHERE h.email_normalized='OPERADOR@ODCA.LOCAL'
AND NOT EXISTS(SELECT 1 FROM odca.users u WHERE u.id='$G_ADMIN');
"@

    # Memberships e papel tenant-client das identidades QA (o admin nao precisa de membership).
    $null = Sql @"
INSERT INTO odca.memberships(tenant_id,user_id)
SELECT '$TC','$G_SIGNER'
WHERE NOT EXISTS(SELECT 1 FROM odca.memberships m WHERE m.tenant_id='$TC' AND m.user_id='$G_SIGNER');
INSERT INTO odca.memberships(tenant_id,user_id)
SELECT '$TC','$G_RECORDER'
WHERE NOT EXISTS(SELECT 1 FROM odca.memberships m WHERE m.tenant_id='$TC' AND m.user_id='$G_RECORDER');
INSERT INTO odca.memberships(tenant_id,user_id)
SELECT '$TC','$G_REVIEWER'
WHERE NOT EXISTS(SELECT 1 FROM odca.memberships m WHERE m.tenant_id='$TC' AND m.user_id='$G_REVIEWER');
INSERT INTO odca.member_roles(tenant_id,user_id,role_id,assigned_by)
SELECT '$TC',u.uid,r.id,'$G_ADMIN'
FROM (VALUES ('$G_SIGNER'::uuid),('$G_RECORDER'::uuid),('$G_REVIEWER'::uuid)) AS u(uid)
CROSS JOIN odca.roles r
WHERE r.tenant_id='$TC' AND r.code='tenant-client'
AND NOT EXISTS(SELECT 1 FROM odca.member_roles mr WHERE mr.tenant_id='$TC' AND mr.user_id=u.uid AND mr.role_id=r.id);
"@

    $mig = Sql 'SELECT max(version) FROM odca.schema_migrations;'
    Assert 'S0.schema_v43' ([int]$mig -ge 43) "max=$mig"

    Write-Host '--- S0 logins (API) ---'
    # O assinante e administrador de plataforma de proposito: nesta versao so
    # administradores concluem inscricao MFA (MfaService.IsEnrollmentAvailableAsync)
    # e a autoassinatura por sessao exige sessao em nivel MFA. Alem disso, a policy
    # PasswordChanged exige mfa_verified para superadministradores em TODOS os
    # endpoints de studio, entao o assinante ja inscrito (via API) faz o setup.
    # O fluxo web do S3 exercita o desafio (/mfa/desafio) com a chave salva aqui.
    $script:signerTok = Login-MfaAdmin 'qa-e-signer@odca.local' $QA_PWD
    $script:signerMfaKey = $script:lastMfaManualKey
    $rr = Login-Plain 'qa-e-recorder@odca.local' $QA_PWD
    $script:recorderTok = (JGet ($rr.Body | ConvertFrom-Json) 'accessToken')
    $vr = Login-Plain 'qa-e-reviewer@odca.local' $QA_PWD
    $script:reviewerTok = (JGet ($vr.Body | ConvertFrom-Json) 'accessToken')
    $script:adminTok = Login-MfaAdmin 'qa-e-admin@odca.local' $QA_PWD
    Assert 'S0.logins_api' ([bool]$script:signerTok -and [bool]$script:recorderTok -and [bool]$script:reviewerTok -and [bool]$script:adminTok) ''

    $rh = Invoke-Api 'GET' "$api/health/ready" $null $null
    Assert 'S0.api_ready' ($rh.Code -eq 200) (Snip $rh.Body)
    $hw = Invoke-Page 'GET' ($web + '/entrar') '' (New-CookieJar)
    Assert 'S0.web_entra_acessivel' $hw.Contains('__RequestVerificationToken') $script:lastUrl

    # Modelos: garante biblioteca oficial presente; resolve os ids usados pelas secoes.
    $script:tok = $script:signerTok
    $cnt = Sql "SELECT count(*) FROM odca.contract_templates WHERE owner_tenant_id='$TC' AND official_key IS NOT NULL AND status<>'archived';"
    if ([int]$cnt -lt 9) {
        $ri = Invoke-Api 'POST' ("$api/api/v1/organizations/$TC/studio/templates/official") $script:tok ''
        Assert 'S0.biblioteca_oficial_instalada' ($ri.Code -eq 200) (Snip $ri.Body)
    }
    $script:gatesId = Sql "SELECT id FROM odca.contract_templates WHERE owner_tenant_id='$TC' AND official_key IS NULL AND status<>'archived';"
    $script:informId = Sql "SELECT id FROM odca.contract_templates WHERE owner_tenant_id='$TC' AND official_key='informed-consent' AND status<>'archived';"
    Assert 'S0.modelo_gates_localizado' ($script:gatesId -match '[0-9a-f]{8}-') $script:gatesId
    Assert 'S0.modelo_informed_localizado' ($script:informId -match '[0-9a-f]{8}-') $script:informId

    # ---------- S1: emissao com metadados congelados + PDF deterministico ----------
    Write-Host '--- S1 emissao, metadados congelados e PDF ---'
    # Normaliza o business_code ao valor do seed antes de emitir: uma execucao
    # anterior interrompida pode ter deixado o tenant com o CNPJ temporario desta
    # suita, o que tornaria a premissa dos asserts de congelamento falsa. O
    # finally restaura $bcBefore como sempre.
    $null = Sql "UPDATE odca.tenants SET business_code='$BC_SEED' WHERE id='$TC';"
    $bcNow = Sql "SELECT business_code FROM odca.tenants WHERE id='$TC';"
    Assert 'S1.business_code_seed' ($bcNow -eq $BC_SEED) "bc=$bcNow"

    $r = New-Draft $script:gatesId 'Val E: Termo Emissao'
    Assert 'S1.draft_201' ($r.Code -eq 201) (Snip $r.Body)
    $draftA = ''
    if ($r.Code -eq 201) { $draftA = ($r.Body | ConvertFrom-Json).id }

    if ($draftA) {
        $d = Get-Draft $draftA
        Set-Val $d.values 'organization_name' (Sql "SELECT display_name FROM odca.tenants WHERE id='$TC';")
        Set-Val $d.values 'patient_name' 'Paciente Val E'
        Set-Val $d.values 'patient_document' '11144477735'
        Confirm-Values $d.values
        $rs = Save-Draft $draftA $d
        Assert 'S1.minuta_salva_200' ($rs.Code -eq 200) (Snip $rs.Body)

        $d = Get-Draft $draftA
        $rg = Generate-Version $draftA $d
        Assert 'S1.versao_gerada_201' ($rg.Code -eq 201) (Snip $rg.Body)
        $verA = ''
        if ($rg.Code -eq 201) { $verA = ($rg.Body | ConvertFrom-Json).id }
        Assert 'S1.id_da_versao' ([bool]$verA) (Snip $rg.Body)

        if ($verA) {
            # D-A4: o CNPJ da organizacao no momento da emissao congela no snapshot.
            $frozenTax = Sql "SELECT coalesce(emission_metadata->>'tax_id','none') FROM odca.generated_contract_versions WHERE id='$verA';"
            Assert 'S1.emission_tax_congelado' ($frozenTax -eq $BC_SEED) "frozen=$frozenTax esperado=$BC_SEED"

            $script:contractA = Sql "SELECT contract_id FROM odca.generated_contract_versions WHERE id='$verA';"
            $null = Sql "UPDATE odca.tenants SET business_code='VAL-E-TMP-999999' WHERE id='$TC';"

            $dv = Invoke-Api 'GET' ("$api/api/v1/organizations/$TC/studio/versions/$verA") $script:tok $null
            Assert 'S1.detail_200' ($dv.Code -eq 200) (Snip $dv.Body)
            $dj = $dv.Body | ConvertFrom-Json
            Assert 'S1.detail_review_generated' ((JGet $dj 'reviewStatus') -eq 'generated') (JGet $dj 'reviewStatus')
            $html = Dec ($dv.Body)
            Assert 'S1.html_cnpj_congelado_com_capa' ($html.Contains('CNPJ 12.345.678/0001-95')) (Snip $html)
            Assert 'S1.html_sem_cnpj_vivo' (-not $html.Contains('VAL-E-TMP')) ''

            $pg = Invoke-Api 'POST' ("$api/api/v1/organizations/$TC/studio/versions/$verA/pdf") $script:tok ''
            $pj = $pg.Body | ConvertFrom-Json
            Assert 'S1.pdf_completed' ($pg.Code -eq 200 -and (JGet $pj 'status') -eq 'completed' -and (JGet $pj 'replayed') -eq 'False') (Snip $pg.Body)
            $renderer = Sql "SELECT coalesce(pdf_renderer_version,'none')||'|'||coalesce(pdf_sha256,'none') FROM odca.generated_contract_versions WHERE id='$verA';"
            Assert 'S1.renderer_odca_pdf_2' ($renderer.StartsWith('odca-pdf-2|')) $renderer
            $shaDb = $renderer.Split('|')[1]

            $dl = Invoke-ApiBytes 'GET' ("$api/api/v1/organizations/$TC/studio/versions/$verA/pdf") $script:tok $null
            Assert 'S1.pdf_download_200' ($dl.Code -eq 200) ('code=' + $dl.Code)
            $shaPdf = Sha256Hex $dl.Bytes
            Assert 'S1.pdf_sha256_confere_banco' ($shaPdf -eq $shaDb) ("pdf=$shaPdf db=$shaDb")
            $pdfText = [Text.Encoding]::GetEncoding('ISO-8859-1').GetString($dl.Bytes)
            Assert 'S1.pdf_producer_odca_pdf_2' $pdfText.Contains('/Producer (ODCA Solutions (odca-pdf-2))') ''
            Assert 'S1.pdf_info_presente' $pdfText.Contains('/Info ') ''
            Assert 'S1.pdf_capa_cnpj_congelado' $pdfText.Contains('CNPJ 12.345.678/0001-95') ''
            Assert 'S1.pdf_sem_cnpj_vivo' (-not $pdfText.Contains('VAL-E-TMP')) ''
            Assert 'S1.pdf_sumario_presente' $pdfText.Contains('SUMARIO') ''
            $cdMatch = [regex]::Match($pdfText, '/CreationDate \(D:(\d{14})Z\)')
            $createdAtUtc = Sql "SELECT to_char(created_at AT TIME ZONE 'UTC','YYYYMMDDHH24MISS') FROM odca.generated_contract_versions WHERE id='$verA';"
            Assert 'S1.pdf_creationdate_do_snapshot' ($cdMatch.Success -and $cdMatch.Groups[1].Value -eq $createdAtUtc) ("pdf=" + $cdMatch.Groups[1].Value + ' criado=' + $createdAtUtc)

            $pg2 = Invoke-Api 'POST' ("$api/api/v1/organizations/$TC/studio/versions/$verA/pdf") $script:tok ''
            $pj2 = $pg2.Body | ConvertFrom-Json
            Assert 'S1.pdf_replay_estavel' ($pg2.Code -eq 200 -and (JGet $pj2 'replayed') -eq 'True' -and ([int64](JGet $pj2 'byteSize') -eq [int64](JGet $pj 'byteSize'))) (Snip $pg2.Body)
        }
    }

    # ---------- S2: preparacao com 2 participantes + confirmacao ----------
    Write-Host '--- S2 preparacao de assinatura ---'
    if ($verA) {
        $rd0 = Get-Readiness $verA
        Assert 'S2.readiness_anterior_200' ($null -ne $rd0) ''
        Assert 'S2.readiness_participants_empty' (($rd0.blockers -contains 'participants.empty') -and ($rd0.canConfirm -eq 'False')) ('blockers=' + ($rd0.blockers -join ','))

        $partIds = @([guid]::NewGuid(), [guid]::NewGuid())
        $prepBody = ToJson @{
            expectedVersion = 0; confirm = $false; operationId = $null
            participants = @(
                @{ id = $partIds[0]; participantType = 'professional'; sourceId = $null; role = 'Profissional responsável'; name = 'Dra. Assinatura E'; email = 'dra.e@odca.local'; phone = $null; position = 1 },
                @{ id = $partIds[1]; participantType = 'organization_representative'; sourceId = $null; role = 'Recepção'; name = 'Recepcionista Registro E'; email = $null; phone = '91999990001'; position = 2 }
            )
        }
        $rp = Invoke-Api 'PUT' ("$api/api/v1/organizations/$TC/studio/versions/$verA/signature-preparation") $script:tok $prepBody
        $prj = $rp.Body | ConvertFrom-Json
        Assert 'S2.preparacao_criada_200' ($rp.Code -eq 200 -and (JGet $prj 'status') -eq 'draft') (Snip $rp.Body)

        $rd1 = Get-Readiness $verA
        Assert 'S2.readiness_limpa_para_confirmar' (($rd1.canConfirm -eq 'True') -and @($rd1.blockers).Count -eq 0) ('blockers=' + ($rd1.blockers -join ',') + ' ok=' + ($rd1.ok -join ','))

        $opId = [guid]::NewGuid().ToString()
        $confBody = ToJson @{
            expectedVersion = 1; confirm = $true; operationId = $opId
            participants = @(
                @{ id = $partIds[0]; participantType = 'professional'; sourceId = $null; role = 'Profissional responsável'; name = 'Dra. Assinatura E'; email = 'dra.e@odca.local'; phone = $null; position = 1 },
                @{ id = $partIds[1]; participantType = 'organization_representative'; sourceId = $null; role = 'Recepção'; name = 'Recepcionista Registro E'; email = $null; phone = '91999990001'; position = 2 }
            )
        }
        $rc = Invoke-Api 'PUT' ("$api/api/v1/organizations/$TC/studio/versions/$verA/signature-preparation") $script:tok $confBody
        $crj = $rc.Body | ConvertFrom-Json
        Assert 'S2.confirmacao_200' ($rc.Code -eq 200 -and (JGet $crj 'status') -eq 'confirmed' -and (JGet $crj 'replayed') -eq 'False') (Snip $rc.Body)
        $rc2 = Invoke-Api 'PUT' ("$api/api/v1/organizations/$TC/studio/versions/$verA/signature-preparation") $script:tok $confBody
        $crj2 = $rc2.Body | ConvertFrom-Json
        Assert 'S2.confirmacao_idempotente' ($rc2.Code -eq 200 -and (JGet $crj2 'replayed') -eq 'True') (Snip $rc2.Body)

        $script:prepId = Sql "SELECT id FROM odca.signature_preparations WHERE tenant_id='$TC' AND generated_version_id='$verA';"
        $rows = Sql "SELECT client_id||'|'||name FROM odca.signature_participants WHERE tenant_id='$TC' AND preparation_id='$($script:prepId)' AND composition_revision=(SELECT confirmed_revision FROM odca.signature_preparations WHERE id='$($script:prepId)') ORDER BY position;"
        $script:p1Client = ''; $script:p2Client = ''
        foreach ($line in ($rows -split "`n")) {
            $p = $line.Trim() -split '\|'
            if ($p.Count -eq 2 -and $p[1] -eq 'Dra. Assinatura E') { $script:p1Client = $p[0] }
            if ($p.Count -eq 2 -and $p[1] -eq 'Recepcionista Registro E') { $script:p2Client = $p[0] }
        }
        Assert 'S2.participantes_confirmados' ([bool]$script:p1Client -and [bool]$script:p2Client) $rows
    }

    # ---------- S3: vinculo de identidade + autoassinatura por sessao (MFA) ----------
    Write-Host '--- S3 assinatura por sessao ---'
    if ($script:p1Client) {
        $signUrl = "$api/api/v1/organizations/$TC/studio/versions/$verA/signature-preparations/$($script:prepId)/participants/$($script:p1Client)/sign"
        $linkUrl = "$api/api/v1/organizations/$TC/studio/versions/$verA/signature-preparations/$($script:prepId)/participants/$($script:p1Client)/identity-link"

        $r1 = Invoke-Api 'POST' $signUrl $script:signerTok (ToJson @{ mode = 'session' })
        Assert 'S3.session_sem_vinculo_409' ($r1.Code -eq 409 -and $r1.Body -match 'participant\.identity_not_linked') ('code=' + $r1.Code + ' ' + (Snip $r1.Body))
        $r2 = Invoke-Api 'POST' $signUrl $script:signerTok (ToJson @{ mode = 'session'; evidence = 'evidencia invalida aqui' })
        Assert 'S3.session_com_evidence_400' ($r2.Code -eq 400 -and $r2.Body -match '"evidence"') ('code=' + $r2.Code + ' ' + (Snip $r2.Body))
        $r3 = Invoke-Api 'POST' $signUrl $script:signerTok (ToJson @{ mode = 'evidence'; evidence = 'abc' })
        Assert 'S3.evidence_curta_400' ($r3.Code -eq 400 -and $r3.Body -match '"evidence"') ('code=' + $r3.Code + ' ' + (Snip $r3.Body))
        $r4 = Invoke-Api 'POST' $signUrl $script:signerTok (ToJson @{ mode = 'outro' })
        Assert 'S3.modalidade_invalida_400' ($r4.Code -eq 400 -and $r4.Body -match '"mode"') ('code=' + $r4.Code + ' ' + (Snip $r4.Body))

        # Estado anterior na vista do recrador (participante ainda sem vinculo).
        $recWeb = Login-Web 'qa-e-recorder@odca.local' $QA_PWD
        $hr = Invoke-Page 'GET' ("$web/organizacoes/$TC/contratos/$($script:contractA)") '' $recWeb.Jar
        Assert 'S3.ficha_mostra_evidencia_sem_vinculo' $hr.Contains('Registrar por evidência — Dra. Assinatura E') (Snip $hr)

        # Sessao API fresca no nivel senha: a policy PasswordChanged exige
        # mfa_verified para superadministradores ANTES das checagens do servico,
        # entao a tentativa sem MFA cai em 403 (o 409 sign.mfa_required fica
        # shadowed pela policy — achado registrado no DECISIONS-LOG).
        $sr2 = Login-Plain 'qa-e-signer@odca.local' $QA_PWD
        $script:signerPlainTok = (JGet ($sr2.Body | ConvertFrom-Json) 'accessToken')
        $r5 = Invoke-Api 'POST' $signUrl $script:signerPlainTok (ToJson @{ mode = 'session' })
        Assert 'S3.sessao_sem_mfa_403' ($r5.Code -eq 403) ('code=' + $r5.Code + ' ' + (Snip $r5.Body))

        # Assinante (ja inscrito no S0) entra na Web e e redirecionado ao DESAFIO MFA.
        $sigWeb = Login-Web 'qa-e-signer@odca.local' $QA_PWD
        Assert 'S3.login_redireciona_pra_desafio' ($sigWeb.Html.Contains('Confirme o segundo fator')) (Snip $script:lastUrl)
        $chTok = Get-Token $sigWeb.Html
        Wait-TotpWindow $G_SIGNER
        $totp = Get-TotpCode $script:signerMfaKey
        $chBody = 'Code=' + (Esc $totp) + '&__RequestVerificationToken=' + (Esc $chTok)
        $hCh = Invoke-Page 'POST' ($web + '/mfa/desafio') $chBody $sigWeb.Jar
        Assert 'S3.desafio_concluido_sessao_mfa' (-not $hCh.Contains('Confirme o segundo fator')) ('last=' + $script:lastUrl + ' ' + (Snip $hCh))

        $hSheet = Invoke-Page 'GET' ("$web/organizacoes/$TC/contratos/$($script:contractA)") '' $sigWeb.Jar
        Assert 'S3.ficha_acessivel_pos_mfa' $hSheet.Contains('Dra. Assinatura E') (Snip $script:lastUrl)
        $t1 = Get-Token $hSheet
        $vincBody = 'participantName=' + (Esc 'Dra. Assinatura E') + '&__RequestVerificationToken=' + (Esc $t1)
        $hLink = Invoke-Page 'POST' ("$web/organizacoes/$TC/contratos/$($script:contractA)/assinaturas/vincular?versionId=$verA&preparationId=$($script:prepId)&participantClientId=$($script:p1Client)") $vincBody $sigWeb.Jar
        Assert 'S3.vinculo_web_sucesso' $hLink.Contains('Sua conta foi vinculada a Dra. Assinatura E.') (Snip $hLink)

        $membershipSig = Sql "SELECT id FROM odca.memberships WHERE tenant_id='$TC' AND user_id='$G_SIGNER';"
        $linkedTo = Sql "SELECT coalesce(identity_membership_id::text,'none') FROM odca.signature_participants WHERE tenant_id='$TC' AND preparation_id='$($script:prepId)' AND client_id='$($script:p1Client)' AND composition_revision=(SELECT confirmed_revision FROM odca.signature_preparations WHERE id='$($script:prepId)');"
        Assert 'S3.vinculo_persistido' ($linkedTo -eq $membershipSig) "linked=$linkedTo membership=$membershipSig"
        $audLink = Sql "SELECT count(*) FROM odca.audit_events WHERE action='signature.identity_linked' AND entity_type='signature_participant' AND entity_id='$($script:p1Client)' AND actor_user_id='$G_SIGNER';"
        Assert 'S3.auditoria_identity_linked' ([int]$audLink -ge 1) "rows=$audLink"

        $hSheet2 = Invoke-Page 'GET' ("$web/organizacoes/$TC/contratos/$($script:contractA)") '' $sigWeb.Jar
        Assert 'S3.ficha_oferece_assinar_sessao' $hSheet2.Contains('Assinar pela minha conta (MFA)') (Snip $hSheet2)
        $t2 = Get-Token $hSheet2
        $assBody = 'signMode=' + (Esc 'session') + '&participantName=' + (Esc 'Dra. Assinatura E') + '&__RequestVerificationToken=' + (Esc $t2)
        $hSign = Invoke-Page 'POST' ("$web/organizacoes/$TC/contratos/$($script:contractA)/assinaturas/assinar?versionId=$verA&preparationId=$($script:prepId)&participantClientId=$($script:p1Client)") $assBody $sigWeb.Jar
        Assert 'S3.assinatura_sessao_registrada' $hSign.Contains('registrada em nome do usuário conectado nesta sessão') (Snip $hSign)

        $spRow = Sql "SELECT signed_through||'|'||signed_by||'|'||coalesce(signed_session_id::text,'none')||'|'||coalesce(signature_evidence,'none') FROM odca.signature_participants WHERE tenant_id='$TC' AND preparation_id='$($script:prepId)' AND client_id='$($script:p1Client)' AND composition_revision=(SELECT confirmed_revision FROM odca.signature_preparations WHERE id='$($script:prepId)');"
        $parts = $spRow -split '\|'
        Assert 'S3.signed_through_session' ($parts[0] -eq 'session') $spRow
        Assert 'S3.signed_by_assinante' ($parts[1] -eq $G_SIGNER) $spRow
        $sidSession = Sql "SELECT id||'|'||authentication_level FROM odca.sessions WHERE user_id='$G_SIGNER' AND revoked_at IS NULL ORDER BY created_at DESC LIMIT 1;"
        $sidParts = $sidSession -split '\|'
        Assert 'S3.signed_session_id_atual' ($parts[2] -eq $sidParts[0]) "signed_session=$($parts[2]) ultimo_sid=$($sidParts[0])"
        Assert 'S3.sessao_em_nivel_mfa' ($sidParts[1] -eq 'mfa') $sidSession
        Assert 'S3.sem_evidencia_no_session' ($parts[3] -eq 'none') $spRow
        $audSign = Sql "SELECT count(*) FROM odca.audit_events WHERE action='signature.participant_signed' AND entity_id='$verA' AND metadata->>'mode'='session' AND actor_user_id='$G_SIGNER';"
        Assert 'S3.auditoria_participant_signed' ([int]$audSign -ge 1) "rows=$audSign"

        $rl = Invoke-Api 'POST' $signUrl $script:signerTok (ToJson @{ mode = 'session' })
        $rlj = $rl.Body | ConvertFrom-Json
        Assert 'S3.replay_estado_real' ($rl.Code -eq 200 -and (JGet $rlj 'replayed') -eq 'True' -and (JGet $rlj 'remaining') -eq '1' -and (JGet $rlj 'allSigned') -eq 'False') (Snip $rl.Body)
    }

    # ---------- S4: registro por evidencia completa a composicao ----------
    Write-Host '--- S4 assinatura por evidencia ---'
    if ($script:p2Client) {
        $evText = 'Contrato físico assinado em papel e arquivado na recepção.'
        $hSheetR = Invoke-Page 'GET' ("$web/organizacoes/$TC/contratos/$($script:contractA)") '' $recWeb.Jar
        $tr = Get-Token $hSheetR
        $evBody = 'signMode=' + (Esc 'evidence') + '&evidence=' + (Esc $evText) + '&participantName=' + (Esc 'Recepcionista Registro E') + '&__RequestVerificationToken=' + (Esc $tr)
        $hEv = Invoke-Page 'POST' ("$web/organizacoes/$TC/contratos/$($script:contractA)/assinaturas/assinar?versionId=$verA&preparationId=$($script:prepId)&participantClientId=$($script:p2Client)") $evBody $recWeb.Jar
        Assert 'S4.evidence_registrada_todos_assinaram' $hEv.Contains('Todos os participantes assinaram') (Snip $hEv)

        $sp2 = Sql "SELECT signed_through||'|'||signed_by||'|'||coalesce(signed_session_id::text,'none')||'|'||coalesce(signature_evidence,'none') FROM odca.signature_participants WHERE tenant_id='$TC' AND preparation_id='$($script:prepId)' AND client_id='$($script:p2Client)' AND composition_revision=(SELECT confirmed_revision FROM odca.signature_preparations WHERE id='$($script:prepId)');"
        $q = $sp2 -split '\|'
        Assert 'S4.signed_through_evidence' ($q[0] -eq 'evidence') $sp2
        Assert 'S4.signed_by_recorder' ($q[1] -eq $G_RECORDER) $sp2
        Assert 'S4.sem_sessao_na_evidence' ($q[2] -eq 'none') $sp2
        Assert 'S4.evidencia_persistida' ($q[3] -eq $evText) $sp2
        $revStatus = Sql "SELECT review_status FROM odca.generated_contract_versions WHERE id='$verA';"
        Assert 'S4.versao_externally_signed' ($revStatus -eq 'externally_signed') $revStatus
        $audDone = Sql "SELECT count(*) FROM odca.audit_events WHERE action='signature.completed' AND entity_id='$verA' AND metadata->>'mode'='evidence' AND actor_user_id='$G_RECORDER';"
        Assert 'S4.auditoria_signature_completed' ([int]$audDone -ge 1) "rows=$audDone"
        $evDone = Sql "SELECT count(*) FROM odca.contract_events WHERE contract_id='$($script:contractA)' AND event_type='signature.completed';"
        Assert 'S4.evento_de_contrato' ([int]$evDone -ge 1) "rows=$evDone"

        $signUrl2 = "$api/api/v1/organizations/$TC/studio/versions/$verA/signature-preparations/$($script:prepId)/participants/$($script:p2Client)/sign"
        $rr2 = Invoke-Api 'POST' $signUrl2 $script:recorderTok (ToJson @{ mode = 'evidence'; evidence = 'replay da evidencia de teste' })
        $rrj = $rr2.Body | ConvertFrom-Json
        Assert 'S4.replay_evidence_estado_real' ($rr2.Code -eq 200 -and (JGet $rrj 'replayed') -eq 'True' -and (JGet $rrj 'remaining') -eq '0' -and (JGet $rrj 'allSigned') -eq 'True') (Snip $rr2.Body)

        $stage = Invoke-Api 'GET' "$api/api/v1/organizations/$TC/studio/documents?stage=completed" $script:tok $null
        Assert 'S4.listagem_stage_completed' ($stage.Code -eq 200 -and $stage.Body -match 'Val E: Termo Emissao') (Snip $stage.Body)
    }

    # ---------- S5: revisao interna (ajustes e aprovacao) ----------
    Write-Host '--- S5 revisoes internas ---'
    # O modulo reviews so existe no plano enterprise (em basic os endpoints de
    # revisao respondem 403 plan_restricted); chama-se o plano para enterprise
    # para exercitar os fluxos e restaura o plano original no finally.
    $null = Sql "UPDATE odca.subscriptions SET plan_version_id='$ENT_ID' WHERE tenant_id='$TC';"
    $planNow = Sql "SELECT pv.code FROM odca.subscriptions s JOIN odca.plan_versions pv ON pv.id=s.plan_version_id WHERE s.tenant_id='$TC' AND s.status='active';"
    Assert 'S5.plan_flip_enterprise' ($planNow -eq 'enterprise') "plan=$planNow"
    $reviewB = ''
    $reviewC = ''
    $verB = ''
    $verC = ''

    function Submit-Review([string]$versionId, [string]$reviewerId) {
        $body = ToJson @{ generatedVersionId = $versionId; reviewerId = $reviewerId; idempotencyKey = [guid]::NewGuid().ToString(); dueAt = $null; instructions = 'Suite E: fluxo de revisao.' }
        return Invoke-Api 'POST' "$api/api/v1/organizations/$TC/studio/reviews" $script:tok $body
    }

    $r = New-Draft $script:informId 'Val E: Revisao Ajustes'
    if ($r.Code -eq 201) {
        $draftB = ($r.Body | ConvertFrom-Json).id
        $d = Get-Draft $draftB
        Fill-Informed $d
        $rs = Save-Draft $draftB $d
        Assert 'S5.minuta_B_salva_200' ($rs.Code -eq 200) (Snip $rs.Body)
        $d = Get-Draft $draftB
        $rg = Generate-Version $draftB $d
        if ($rg.Code -eq 201) {
            $verB = ($rg.Body | ConvertFrom-Json).id
            $sub = Submit-Review $verB $G_REVIEWER
            $subj = $sub.Body | ConvertFrom-Json
            Assert 'S5.envio_revisao_200' ($sub.Code -eq 200 -and (JGet $subj 'status') -eq 'in_review') (Snip $sub.Body)
            $reviewB = (JGet $subj 'reviewId')

            $listIn = Invoke-Api 'GET' "$api/api/v1/organizations/$TC/studio/documents?stage=in_review" $script:tok $null
            Assert 'S5.listagem_in_review' ($listIn.Body -match 'Val E: Revisao Ajustes') (Snip $listIn.Body)

            $dtl = Invoke-Api 'GET' "$api/api/v1/organizations/$TC/reviews/$reviewB" $script:reviewerTok $null
            $dtlj = $dtl.Body | ConvertFrom-Json
            Assert 'S5.detail_revisao_in_review' ($dtl.Code -eq 200 -and (JGet $dtlj 'status') -eq 'in_review') (Snip $dtl.Body)
            $rvB = [long](JGet $dtlj 'version')
            Assert 'S5.detail_responsavel' ((JGet $dtlj 'assignee') -ne '') (JGet $dtlj 'assignee')

            $wrong = Invoke-Api 'POST' "$api/api/v1/organizations/$TC/reviews/$reviewB/decision" $script:recorderTok (ToJson @{ action = 'approve'; justification = 'Nao sou o revisor atual.'; expectedVersion = $rvB; idempotencyKey = [guid]::NewGuid().ToString() })
            Assert 'S5.decisao_por_outro_409' ($wrong.Code -eq 409) ('code=' + $wrong.Code + ' ' + (Snip $wrong.Body))
            $staleV = Invoke-Api 'POST' "$api/api/v1/organizations/$TC/reviews/$reviewB/decision" $script:reviewerTok (ToJson @{ action = 'approve'; justification = 'Versao obsoleta de proposito.'; expectedVersion = ($rvB + 7); idempotencyKey = [guid]::NewGuid().ToString() })
            Assert 'S5.decisao_versao_obsoleta_409' ($staleV.Code -eq 409 -and $staleV.Body -match 'currentVersion') ('code=' + $staleV.Code + ' ' + (Snip $staleV.Body))

            $idemB = [guid]::NewGuid().ToString()
            $decB = Invoke-Api 'POST' "$api/api/v1/organizations/$TC/reviews/$reviewB/decision" $script:reviewerTok (ToJson @{ action = 'request_changes'; justification = 'Ajustar a finalidade informada antes do uso.'; expectedVersion = $rvB; idempotencyKey = $idemB })
            $decBj = $decB.Body | ConvertFrom-Json
            Assert 'S5.request_changes_200' ($decB.Code -eq 200 -and (JGet $decBj 'status') -eq 'changes_requested' -and (JGet $decBj 'replayed') -eq 'False') (Snip $decB.Body)
            $decB2 = Invoke-Api 'POST' "$api/api/v1/organizations/$TC/reviews/$reviewB/decision" $script:reviewerTok (ToJson @{ action = 'request_changes'; justification = 'Ajustar a finalidade informada antes do uso.'; expectedVersion = $rvB; idempotencyKey = $idemB })
            $decB2j = $decB2.Body | ConvertFrom-Json
            Assert 'S5.request_changes_idempotente' ($decB2.Code -eq 200 -and (JGet $decB2j 'replayed') -eq 'True') (Snip $decB2.Body)

            $listIn2 = Invoke-Api 'GET' "$api/api/v1/organizations/$TC/studio/documents?stage=in_review" $script:tok $null
            Assert 'S5.sai_da_in_review' (-not ($listIn2.Body -match 'Val E: Revisao Ajustes')) (Snip $listIn2.Body)
            $listCr = Invoke-Api 'GET' "$api/api/v1/organizations/$TC/studio/documents?stage=changes_requested" $script:tok $null
            Assert 'S5.aparece_em_changes_requested' ($listCr.Body -match 'Val E: Revisao Ajustes') (Snip $listCr.Body)

            $dvB = Invoke-Api 'GET' ("$api/api/v1/organizations/$TC/studio/versions/$verB") $script:tok $null
            Assert 'S5.detail_changes_requested' ((JGet ($dvB.Body | ConvertFrom-Json) 'reviewStatus') -eq 'changes_requested') (Snip $dvB.Body)
            $rdB = Get-Readiness $verB
            Assert 'S5.readiness_review_changes' (@($rdB.blockers) -contains 'review.changes') ('blockers=' + (@($rdB.blockers) -join ','))
        }
        else { Assert 'S5.versao_B_gerada' $false (Snip $rg.Body) }
    }
    else { Assert 'S5.minuta_B_criada' $false (Snip $r.Body) }

    $r = New-Draft $script:informId 'Val E: Revisao Aprovada'
    if ($r.Code -eq 201) {
        $draftC = ($r.Body | ConvertFrom-Json).id
        $d = Get-Draft $draftC
        Fill-Informed $d
        $rs = Save-Draft $draftC $d
        Assert 'S5.minuta_C_salva_200' ($rs.Code -eq 200) (Snip $rs.Body)
        $d = Get-Draft $draftC
        $rg = Generate-Version $draftC $d
        if ($rg.Code -eq 201) {
            $verC = ($rg.Body | ConvertFrom-Json).id
            $sub = Submit-Review $verC $G_REVIEWER
            $subj = $sub.Body | ConvertFrom-Json
            $reviewC = (JGet $subj 'reviewId')
            $dtl = Invoke-Api 'GET' "$api/api/v1/organizations/$TC/reviews/$reviewC" $script:reviewerTok $null
            $rvC = [long](JGet ($dtl.Body | ConvertFrom-Json) 'version')
            $decC = Invoke-Api 'POST' "$api/api/v1/organizations/$TC/reviews/$reviewC/decision" $script:reviewerTok (ToJson @{ action = 'approve'; justification = 'Documento adequado para uso.'; expectedVersion = $rvC; idempotencyKey = [guid]::NewGuid().ToString() })
            $decCj = $decC.Body | ConvertFrom-Json
            Assert 'S5.aprove_200' ($decC.Code -eq 200 -and (JGet $decCj 'status') -eq 'internally_approved') (Snip $decC.Body)

            $dvC = Invoke-Api 'GET' ("$api/api/v1/organizations/$TC/studio/versions/$verC") $script:tok $null
            Assert 'S5.detail_internally_approved' ((JGet ($dvC.Body | ConvertFrom-Json) 'reviewStatus') -eq 'internally_approved') (Snip $dvC.Body)
            $listAp = Invoke-Api 'GET' "$api/api/v1/organizations/$TC/studio/documents?stage=approved" $script:tok $null
            Assert 'S5.listagem_approved' ($listAp.Body -match 'Val E: Revisao Aprovada') (Snip $listAp.Body)
            $rdC = Get-Readiness $verC
            Assert 'S5.readiness_review_aproved' (@($rdC.ok) -contains 'review.approved') ('ok=' + (@($rdC.ok) -join ','))
        }
        else { Assert 'S5.versao_C_gerada' $false (Snip $rg.Body) }
    }
    else { Assert 'S5.minuta_C_criada' $false (Snip $r.Body) }

    # ---------- S6: aprovacao ODCA presa a revisao do modelo ----------
    Write-Host '--- S6 aprovacao de modelo por revisao ---'
    $null = Sql "UPDATE odca.tenants SET activity_profile='plastic_surgery' WHERE id='$TC';"
    $inst = Sql "SELECT count(*) FROM odca.contract_templates WHERE owner_tenant_id='$TC' AND official_key='surgical-consent' AND status<>'archived';"
    if ([int]$inst -lt 1) {
        $ri = Invoke-Api 'POST' ("$api/api/v1/organizations/$TC/studio/templates/official") $script:tok ''
        Assert 'S6.biblioteca_reinstalada' ($ri.Code -eq 200) (Snip $ri.Body)
    }
    $tplRows = Sql "SELECT id||'|'||row_version FROM odca.contract_templates WHERE owner_tenant_id='$TC' AND official_key='surgical-consent' AND status<>'archived';"
    Assert 'S6.modelo_cirurgico_instalado' ($tplRows -match '[0-9a-f]{8}-') $tplRows
    $tplP1 = $tplRows.Split('|')[0]
    $tplP1Rv = [long]$tplRows.Split('|')[1]

    $r = New-Draft $tplP1 'Val E: Cirurgico Aprovado'
    Assert 'S6.draft_cirurgico_201' ($r.Code -eq 201) (Snip $r.Body)
    $verD = ''
    if ($r.Code -eq 201) {
        $dd = ($r.Body | ConvertFrom-Json).id
        $d = Get-Draft $dd
        Fill-Surgical $d
        $rsD = Save-Draft $dd $d
        Assert 'S6.minuta_D_salva_200' ($rsD.Code -eq 200) (Snip $rsD.Body)
        $d = Get-Draft $dd
        $rg = Generate-Version $dd $d
        Assert 'S6.versao_D_gerada' ($rg.Code -eq 201) (Snip $rg.Body)
        $verD = ($rg.Body | ConvertFrom-Json).id

        $rdP = Get-Readiness $verD
        Assert 'S6.blocker_approval_odca_required' (@($rdP.blockers) -contains 'approval.odca.required') ('blockers=' + (@($rdP.blockers) -join ','))

        $dq = Invoke-Api 'POST' "$api/api/v1/platform/template-approvals/$TC/surgical-consent/decision" $script:adminTok (ToJson @{ decision = 'aprovado'; note = 'Modelo revisado pelo comite clinico (suite E).' })
        $dqj = $dq.Body | ConvertFrom-Json
        $dqRev = JGet $dqj 'approvedRevision'
        Assert 'S6.decisao_aprovado_200' ($dq.Code -eq 200 -and (JGet $dqj 'decision') -eq 'aprovado' -and ($dqRev -match '^[0-9]+$')) (Snip $dq.Body)
        Write-Host ('[diag-S6] modelo apos decisao: ' + (Sql "SELECT id||' status='||status||' rv='||row_version FROM odca.contract_templates WHERE owner_tenant_id='$TC' AND official_key='surgical-consent';"))
        Write-Host ('[diag-S6] aprovacoes na BD: ' + (Sql "SELECT decision||' decided_at='||decided_at FROM odca.template_approvals WHERE tenant_id='$TC' AND official_key='surgical-consent';"))
        $queue = Invoke-Api 'GET' "$api/api/v1/platform/template-approvals" $script:adminTok $null
        $qbStr = [string]$queue.Body
        # Obs. PS 5.1: o cmdlet emite o array ja parseado como UM item do pipeline;
        # @(ConvertFrom-Json ...) gera um wrapper de 1 elemento contendo o array.
        # A atribuicao direta preserva o Object[]; o @( ) depois apenas normaliza.
        $qArr = ConvertFrom-Json $qbStr
        Write-Host ('[diag-S6] parse: bodytype=' + $queue.Body.GetType().FullName + ' len=' + $qbStr.Length + ' parsed=' + @($qArr).Count)
        $qrow = @($qArr) | Where-Object { (JGet $_ 'officialKey') -eq 'surgical-consent' -and (JGet $_ 'tenantId') -eq $TC } | Select-Object -First 1
        if ($null -eq $qrow) {
            $qBodyTxt = [string]$queue.Body
            if ($qBodyTxt.Length -gt 900) { $qBodyTxt = $qBodyTxt.Substring(0, 900) + ' ...(truncado)' }
            Write-Host ('[diag-S6] fila sem linha para TC/TC=' + $TC + ' corpo=' + $qBodyTxt)
        }
        $qrowTxt = if ($null -ne $qrow) { ($qrow | ConvertTo-Json -Compress -Depth 5) } else { 'sem-linha-para-TC' }
        Assert 'S6.fila_mostra_aprovado' (($null -ne $qrow) -and (JGet $qrow 'decision') -eq 'aprovado' -and (JGet $qrow 'approvedRevision') -eq $dqRev -and (JGet $qrow 'decidedByName') -ne '') $qrowTxt

        $rdP2 = Get-Readiness $verD
        Assert 'S6.approval_odca_granted' (@($rdP2.ok) -contains 'approval.odca.granted') ('ok=' + (@($rdP2.ok) -join ','))

        # Arquivar o modelo publicado e reinstalar a biblioteca oficial: nova linha
        # de modelo com a mesma chave (revisao instalada diferente).
        $ar = Invoke-Api 'POST' "$api/api/v1/organizations/$TC/studio/templates/$tplP1/archive?expectedVersion=$tplP1Rv" $script:tok ''
        Assert 'S6.arquivo_do_modelo_204' ($ar.Code -eq 204) ('code=' + $ar.Code + ' ' + (Snip $ar.Body))
        $ri2 = Invoke-Api 'POST' "$api/api/v1/organizations/$TC/studio/templates/official" $script:tok ''
        Assert 'S6.reinstalacao_oficial_200' ($ri2.Code -eq 200) (Snip $ri2.Body)
        $tplP2 = Sql "SELECT id FROM odca.contract_templates WHERE owner_tenant_id='$TC' AND official_key='surgical-consent' AND status<>'archived';"
        Assert 'S6.nova_linha_de_modelo' ($tplP2 -match '[0-9a-f]{8}-' -and $tplP2 -ne $tplP1) "p1=$tplP1 p2=$tplP2"

        $rE = New-Draft $tplP2 'Val E: Cirurgico Republicado'
        Assert 'S6.draft_E_201' ($rE.Code -eq 201) (Snip $rE.Body)
        $verE = ''
        if ($rE.Code -eq 201) {
            $de = ($rE.Body | ConvertFrom-Json).id
            $d = Get-Draft $de
            Fill-Surgical $d
            $rsE = Save-Draft $de $d
            Assert 'S6.minuta_E_salva_200' ($rsE.Code -eq 200) (Snip $rsE.Body)
            $d = Get-Draft $de
            $rg = Generate-Version $de $d
            Assert 'S6.versao_E_gerada' ($rg.Code -eq 201) (Snip $rg.Body)
            $verE = ($rg.Body | ConvertFrom-Json).id

            $rdE = Get-Readiness $verE
            Assert 'S6.versao_nova_fica_stale' (@($rdE.blockers) -contains 'approval.odca.stale') ('blockers=' + (@($rdE.blockers) -join ','))
            $rdD = Get-Readiness $verD
            Assert 'S6.versao_antiga_ainda_coberta' (@($rdD.ok) -contains 'approval.odca.granted') ('ok=' + (@($rdD.ok) -join ','))

            $dq2 = Invoke-Api 'POST' "$api/api/v1/platform/template-approvals/$TC/surgical-consent/decision" $script:adminTok (ToJson @{ decision = 'aprovado'; note = 'Nova analise apos republicacao do modelo.' })
            Assert 'S6.nova_decisao_200' ($dq2.Code -eq 200) (Snip $dq2.Body)
            $rdE2 = Get-Readiness $verE
            Assert 'S6.versao_nova_agora_granted' (@($rdE2.ok) -contains 'approval.odca.granted') ('ok=' + (@($rdE2.ok) -join ','))
            $rdD2 = Get-Readiness $verD
            Assert 'S6.versao_antiga_fica_stale' (@($rdD2.blockers) -contains 'approval.odca.stale') ('blockers=' + (@($rdD2.blockers) -join ','))

            $audDec = Sql "SELECT count(*) FROM odca.audit_events WHERE action='odca.template_approval.aprovado' AND metadata->>'officialKey'='surgical-consent' AND actor_user_id='$G_ADMIN';"
            Assert 'S6.auditoria_das_decisoes' ([int]$audDec -ge 2) "rows=$audDec"
        }
    }
}
catch {
    $script:unhandled = $_.Exception.Message
    Assert 'S?.execucao_sem_excecao' $false $script:unhandled
}
finally {
    Write-Host '--- cleanup final (best effort) + verificação do seed ---'
    if ($script:bcBefore) { $null = SqlOpt "UPDATE odca.tenants SET business_code='$($script:bcBefore)' WHERE id='$TC';" }
    if ($script:profileBefore) { $null = SqlOpt "UPDATE odca.tenants SET activity_profile='$($script:profileBefore)' WHERE id='$TC';" }
    if ($script:planBefore) { $null = SqlOpt "UPDATE odca.subscriptions SET plan_version_id='$($script:planBefore)' WHERE tenant_id='$TC';" }
    $null = SqlBest (Get-SuiteCleanupSql)
    $leftover = SqlOpt "SELECT count(*) FROM odca.contracts WHERE tenant_id='$TC' AND title LIKE 'Val E:%';"
    if ($leftover) { Assert 'cleanup.contratos_removidos' ([int]$leftover -eq 0) "restantes=$leftover" }
    $usersLeft = SqlOpt "SELECT count(*) FROM odca.users WHERE id IN ('$G_SIGNER','$G_RECORDER','$G_REVIEWER','$G_ADMIN');"
    if ($usersLeft) { Assert 'cleanup.usuarios_qa_removidos' ([int]$usersLeft -eq 0) "restantes=$usersLeft" }

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
Write-Host ('=== validate-e-web: PASS=' + $script:passes + ' FAIL=' + $script:failures + ' ===')
if ($script:failures -gt 0) { exit 1 } else { exit 0 }
