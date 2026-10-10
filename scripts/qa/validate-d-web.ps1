# Secao D — Design e homologacao do MVP.
# Cria duas organizacoes fixture ("Val D:"), instala os modelos oficiais embarcados do zero
# (capa/sumario/callout so existem em instalacoes novas), percorre os 19 cenarios minimos por
# API + paginas Web e remove tudo no final, restaurando o estado dos seeds.
# Evidencias PNG de navegador ficam fora do repositorio (%TEMP%\opencode\evidencias-d\).
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }

$api    = 'https://localhost:7143'
$web    = 'https://localhost:7144'
$psql   = 'C:\Program Files\PostgreSQL\18\bin\psql.exe'
$db     = 'odca_test_disposable'
$TC     = '20000000-0000-4000-8000-000000000001'
$D1     = '20000000-0000-4000-8000-0000000000D1'   # Val D: Centro Terapeutico Aurora (therapy_clinic · BASIC)
$D2     = '20000000-0000-4000-8000-0000000000D2'   # Val D: Clinica Cirurgica Harmonia (plastic_surgery · ENTERPRISE)
$USER_ADMIN     = '10000000-0000-4000-8000-000000000001'
$USER_OPERADOR  = '10000000-0000-4000-8000-000000000002'
$USER_CLIENTE   = '10000000-0000-4000-8000-000000000003'
$QA_OP          = '4e000000-0000-4000-8000-000000000022'   # identidade propria da suita (D-A5)
$QA_PWD         = 'La@RsjC5p-hmq_JpReGW'   # mesma senha do operador (hash clonado no setup)
$ENT_ID   = '30000000-0000-0000-0000-000000000013'
$BASIC_ID = '30000000-0000-0000-0000-000000000011'
$cssPath = Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'src\Odca.Web\wwwroot\css\site.css'
if (-not (Test-Path $cssPath)) { $cssPath = 'C:\MNSOFT\odcasolutions\src\Odca.Web\wwwroot\css\site.css' }

$script:passes = 0
$script:failures = 0

function Assert([string]$name, [bool]$cond, [string]$detail) {
    if ($cond) { $script:passes++; Write-Host ('PASS  ' + $name) }
    else {
        Write-Host ('FAIL  ' + $name)
        if ($detail) { Write-Host ('      observed: ' + $detail.Substring(0, [Math]::Min(400, $detail.Length))) }
        $script:failures++
    }
}
function Snip([string]$s) { if (-not $s) { return '' } return $s.Substring(0, [Math]::Min(400, $s.Length)) }
function Dec([string]$s) { if (-not $s) { return '' } return [System.Net.WebUtility]::HtmlDecode($s) }
function Esc([string]$s) { return [uri]::EscapeDataString($s) }

function Sql([string]$sql) {
    $tmp = Join-Path $env:TEMP ('odca-d-' + [guid]::NewGuid().ToString('N') + '.sql')
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
    elseif ($method -ne 'GET' -and $method -ne 'HEAD' -and $method -ne 'DELETE') { $req.ContentLength = 0 }
    try { $resp = $req.GetResponse() } catch [System.Net.WebException] { $resp = $_.Exception.Response }
    $code = 0; $text = ''
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
        Write-Host ('      login tentativa ' + $try + ' falhou (' + $r.Code + '), aguardando rate limit...')
        Start-Sleep -Seconds 65
    }
    throw ('login ' + $loginName + ' falhou: ' + $r.Code + ' ' + (Snip $r.Body))
}

function Login-MfaAdmin([string]$loginName, [string]$pwd) {
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
    $html = Dec([Text.Encoding]::UTF8.GetString($ms.ToArray()))
    Write-Host ('  page ' + $method + ' ' + $url.Substring(0, [Math]::Min(95, $url.Length)) + ' -> ' + [int]$resp.StatusCode + ' | last=' + $script:lastUrl.Substring(0, [Math]::Min(95, $script:lastUrl.Length)))
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

# ---------- helpers de studio (parameterizados por tenant/token) ----------
function New-Draft([string]$T, [string]$Tok, [string]$templateId, [string]$title) {
    $body = ConvertTo-Json -InputObject ([pscustomobject]@{ title = $title; templateId = $templateId }) -Depth 5
    return Invoke-Api 'POST' ("$api/api/v1/organizations/$T/studio/drafts") $Tok $body
}
function Get-Draft([string]$T, [string]$Tok, [string]$draftId) {
    $r = Invoke-Api 'GET' ("$api/api/v1/organizations/$T/studio/drafts/$draftId") $Tok $null
    if ($r.Code -ne 200) { throw ('GET minuta falhou: ' + $r.Code + ' ' + (Snip $r.Body)) }
    $d = $r.Body | ConvertFrom-Json
    $list = New-Object System.Collections.ArrayList
    foreach ($v in @($d.values)) { if ($null -ne $v) { [void]$list.Add($v) } }
    $d.values = $list
    return $d
}
function Save-Draft([string]$T, [string]$Tok, [string]$draftId, $d) {
    $payload = [pscustomobject]@{
        content = $d.content
        fields = $d.fields
        values = @($d.values)
        expectedVersion = [int64]$d.version
        clientRevision = [guid]::NewGuid().ToString()
    }
    $body = ConvertTo-Json -InputObject $payload -Depth 40
    return Invoke-Api 'PUT' ("$api/api/v1/organizations/$T/studio/drafts/$draftId") $Tok $body
}
function Get-Val($values, [string]$fieldId) {
    foreach ($v in @($values)) { if ($v.fieldId -eq $fieldId) { return $v } }
    return $null
}
function Set-Val($values, [string]$fieldId, [string]$value) {
    $e = Get-Val $values $fieldId
    if ($null -eq $e) {
        $e = [pscustomobject]@{ fieldId = $fieldId; value = $value; confirmed = $true; updatedAt = $null }
        [void]$values.Add($e)
    } else {
        $e.value = $value
        $e.confirmed = $true
    }
}
function Confirm-Values($values) {
    foreach ($v in @($values)) { if ($null -ne $v) { $v.confirmed = $true } }
}
# Preenche campos obrigatorios vazios com valores deterministicos por tipo (varredura em 2 passes
# para respeitar dependencias condicionais tipo has_representative -> legal_representative_info).
function Auto-Fill($d, [hashtable]$overrides) {
    foreach ($pass in 1..2) {
        foreach ($f in @($d.fields)) {
            $skip = $false
            $whenField = JGet $f 'requiredWhenFieldId'
            if ($whenField) {
                $anyOf = @($f.requiredWhenAnyOf | ForEach-Object { '' + $_ })
                $pe = Get-Val $d.values $whenField
                $pv = ''
                if ($null -ne $pe) { $pv = '' + $pe.value }
                if ($anyOf -notcontains $pv) { $skip = $true }
            }
            if ($skip -and -not $overrides.ContainsKey($f.id)) { continue }
            if ($overrides.ContainsKey($f.id)) {
                Set-Val $d.values $f.id ([string]$overrides[$f.id])
                continue
            }
            $e = Get-Val $d.values $f.id
            if ($null -ne $e -and -not [string]::IsNullOrWhiteSpace([string]$e.value)) { $e.confirmed = $true; continue }
            $lab = '' + (JGet $f 'label') + ' ' + (JGet $f 'name') + ' ' + $f.id
            $t = [int]$f.type
            if ($t -eq 7) { continue }   # formula: servidor calcula quando vazio
            if ($t -eq 6) {
                $opts = @($f.choices | ForEach-Object { '' + $_ })
                $pick = $opts | Where-Object { $_ -eq 'Não' -or $_ -eq 'Nao' } | Select-Object -First 1
                if (-not $pick -and @($opts).Count -gt 0) { $pick = $opts[0] }
                if ($pick) { Set-Val $d.values $f.id $pick }
            }
            elseif ($t -eq 2) { Set-Val $d.values $f.id ([DateTime]::UtcNow.AddDays(30).ToString('yyyy-MM-dd')) }
            elseif ($t -eq 3) { Set-Val $d.values $f.id '4' }
            elseif ($t -eq 4) { Set-Val $d.values $f.id '350.00' }
            elseif ($t -eq 5) {
                if ($lab -match '(?i)cnpj|razao|razão|jur') { Set-Val $d.values $f.id '11222333000181' }
                else { Set-Val $d.values $f.id '11144477735' }
            }
            else {
                if ($lab -match '(?i)e-?mail') { Set-Val $d.values $f.id 'contato@vald.local' }
                elseif ($lab -match '(?i)telefone|celular|whats') { Set-Val $d.values $f.id '(11) 98765-4321' }
                elseif ($lab -match '(?i)cep') { Set-Val $d.values $f.id '01310-100' }
                elseif ($lab -match '(?i)cidade|comarca|local') { Set-Val $d.values $f.id 'São Paulo/SP' }
                else { Set-Val $d.values $f.id 'Dado de homologacao Val D' }
            }
        }
    }
    Confirm-Values $d.values
}
function Generate-PdfAndWait([string]$T, [string]$Tok, [string]$verId) {
    $null = Invoke-Api 'POST' ("$api/api/v1/organizations/$T/studio/versions/$verId/pdf") $Tok $null
    for ($i = 1; $i -le 45; $i++) {
        $st = Sql "SELECT coalesce(pdf_status,'') FROM odca.generated_contract_versions WHERE id='$verId';"
        if ($st -eq 'completed') {
            $g = Invoke-Api 'GET' ("$api/api/v1/organizations/$T/studio/versions/$verId/pdf") $Tok $null
            if ($g.Code -eq 200 -and $g.Body.Length -gt 500) { return $true }
        }
        Start-Sleep -Seconds 2
    }
    return $false
}
function Get-RowState([string]$propId) {
    return (Sql "SELECT status||'|'||row_version::text||'|'||application_status FROM odca.contract_change_requests WHERE id='$propId';")
}

# ---------- cleanup idempotente por tenant fixture (ordem de dependência das FKs) ----------
function Clean-Tenant([string]$T) {
    SqlOpt "DELETE FROM odca.signature_participants WHERE preparation_id IN (SELECT id FROM odca.signature_preparations WHERE tenant_id='$T');" | Out-Null
    SqlOpt "DELETE FROM odca.signature_preparation_events WHERE preparation_id IN (SELECT id FROM odca.signature_preparations WHERE tenant_id='$T');" | Out-Null
    SqlOpt "DELETE FROM odca.signature_preparation_operations WHERE preparation_id IN (SELECT id FROM odca.signature_preparations WHERE tenant_id='$T');" | Out-Null
    SqlOpt "DELETE FROM odca.signature_preparations WHERE tenant_id='$T';" | Out-Null
    SqlOpt "DELETE FROM odca.draft_save_receipts WHERE tenant_id='$T';" | Out-Null
    SqlOpt "DELETE FROM odca.template_approvals WHERE tenant_id='$T';" | Out-Null
    SqlOpt "DELETE FROM odca.contract_change_applications WHERE request_id IN (SELECT id FROM odca.contract_change_requests WHERE tenant_id='$T');" | Out-Null
    SqlOpt "DELETE FROM odca.contract_change_events WHERE request_id IN (SELECT id FROM odca.contract_change_requests WHERE tenant_id='$T');" | Out-Null
    SqlOpt "DELETE FROM odca.contract_change_events WHERE tenant_id='$T';" | Out-Null
    SqlOpt "DELETE FROM odca.contract_review_requests WHERE tenant_id='$T';" | Out-Null
    SqlOpt "DELETE FROM odca.contract_change_requests WHERE tenant_id='$T';" | Out-Null
    SqlOpt "DELETE FROM odca.generated_contract_versions WHERE tenant_id='$T';" | Out-Null
    SqlOpt "DELETE FROM odca.contract_drafts WHERE tenant_id='$T';" | Out-Null
    SqlOpt "DELETE FROM odca.document_versions WHERE tenant_id='$T';" | Out-Null
    SqlOpt "DELETE FROM odca.contract_documents WHERE tenant_id='$T';" | Out-Null
    SqlOpt "DELETE FROM odca.contract_events WHERE contract_id IN (SELECT id FROM odca.contracts WHERE tenant_id='$T');" | Out-Null
    SqlOpt "DELETE FROM odca.contracts WHERE tenant_id='$T';" | Out-Null
    SqlOpt "DELETE FROM odca.contract_template_versions WHERE template_id IN (SELECT id FROM odca.contract_templates WHERE owner_tenant_id='$T');" | Out-Null
    SqlOpt "DELETE FROM odca.contract_templates WHERE owner_tenant_id='$T';" | Out-Null
    SqlOpt "DELETE FROM odca.solicitation_events WHERE solicitation_id IN (SELECT id FROM odca.solicitations WHERE tenant_id='$T');" | Out-Null
    SqlOpt "DELETE FROM odca.solicitation_messages WHERE solicitation_id IN (SELECT id FROM odca.solicitations WHERE tenant_id='$T');" | Out-Null
    SqlOpt "DELETE FROM odca.solicitation_pauses WHERE solicitation_id IN (SELECT id FROM odca.solicitations WHERE tenant_id='$T');" | Out-Null
    SqlOpt "DELETE FROM odca.solicitations WHERE tenant_id='$T';" | Out-Null
    SqlOpt "DELETE FROM odca.audit_events WHERE tenant_id='$T';" | Out-Null
    SqlOpt "DELETE FROM odca.member_roles WHERE tenant_id='$T';" | Out-Null
    SqlOpt "DELETE FROM odca.memberships WHERE tenant_id='$T';" | Out-Null
    SqlOpt "DELETE FROM odca.role_permissions WHERE role_id IN (SELECT id FROM odca.roles WHERE tenant_id='$T');" | Out-Null
    SqlOpt "DELETE FROM odca.roles WHERE tenant_id='$T';" | Out-Null
    SqlOpt "DELETE FROM odca.subscriptions WHERE tenant_id='$T';" | Out-Null
    SqlOpt "DELETE FROM odca.tenant_storage_usage WHERE tenant_id='$T';" | Out-Null
    SqlOpt "DELETE FROM odca.resource_movements WHERE tenant_id='$T';" | Out-Null
    SqlOpt "DELETE FROM odca.storage_reservations WHERE tenant_id='$T';" | Out-Null
    SqlOpt "DELETE FROM odca.storage_capacity_grants WHERE tenant_id='$T';" | Out-Null
    SqlOpt "DELETE FROM odca.notification_outbox WHERE tenant_id='$T';" | Out-Null
    SqlOpt "DELETE FROM odca.organization_feature_blocks WHERE tenant_id='$T';" | Out-Null
    SqlOpt "DELETE FROM odca.contract_template_access WHERE tenant_id='$T';" | Out-Null
    SqlOpt "DELETE FROM odca.tenant_invitations WHERE tenant_id='$T';" | Out-Null
    SqlOpt "DELETE FROM odca.support_sessions WHERE tenant_id='$T';" | Out-Null
    SqlOpt "DELETE FROM odca.financial_audit_events WHERE tenant_id='$T';" | Out-Null
    SqlOpt "DELETE FROM odca.invoices WHERE tenant_id='$T';" | Out-Null
    SqlOpt "DELETE FROM odca.patient_representatives WHERE tenant_id='$T';" | Out-Null
    SqlOpt "DELETE FROM odca.patients WHERE tenant_id='$T';" | Out-Null
    SqlOpt "DELETE FROM odca.tenants WHERE id='$T';" | Out-Null
}

# ---------- Setup ----------
try {
Write-Host '--- Setup fixtures Val D ---'

    $dbName = Sql "SELECT current_database();"
    Assert 'D00.db_disponivel' ($dbName -eq 'odca_test_disposable') ("current_database=$dbName")

    # D-A5: fingerprint dos tres usuarios-semente e plano do TC antes de qualquer mutacao;
    # o finally assertera que o seed permanece intacto e o plano restaurado.
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
    Assert 'D00.plano_capturado' ($script:planBefore -match '[0-9a-f]{8}-') "plan=$script:planBefore"

    Clean-Tenant $D1
    Clean-Tenant $D2

    # Identidade propria da suita (D-A5): nenhum usuario-semente e mutado; a senha e o
    # hash clonado do operador apenas para permitir o login da nova conta.
    SqlOpt @"
    DELETE FROM odca.member_roles WHERE user_id='$QA_OP';
    DELETE FROM odca.memberships WHERE user_id='$QA_OP';
    DELETE FROM odca.sessions WHERE user_id='$QA_OP';
    DELETE FROM odca.mfa_recovery_codes WHERE user_id='$QA_OP';
    DELETE FROM odca.resource_movements WHERE actor_user_id='$QA_OP';
    DELETE FROM odca.audit_events WHERE actor_user_id='$QA_OP';
    UPDATE odca.users SET deleted_by=NULL WHERE id='$QA_OP';
    DELETE FROM odca.users WHERE id='$QA_OP';
"@ | Out-Null
    Assert 'D00.limpeza_qa_anterior' $true ''
    Sql @"
    INSERT INTO odca.tenants (id,business_code,display_name,status) VALUES
      ('$D1','45987654000118','Val D: Centro Terapeutico Aurora','active'),
      ('$D2','45987654000126','Val D: Clinica Cirurgica Harmonia','active')
    ON CONFLICT (id) DO UPDATE SET display_name=EXCLUDED.display_name,status='active',is_deleted=false,updated_at=now();
    UPDATE odca.tenants SET activity_profile='therapy_clinic'  WHERE id='$D1';
    UPDATE odca.tenants SET activity_profile='plastic_surgery' WHERE id='$D2';

    INSERT INTO odca.subscriptions (tenant_id,plan_version_id,commercial_state,status,manual_grant_reason,created_by) VALUES
      ('$D1','$BASIC_ID','active','active','val-d-fixture','$USER_CLIENTE'),
      ('$D2','$ENT_ID','active','active','val-d-fixture','$USER_CLIENTE');

    INSERT INTO odca.roles (scope_type,tenant_id,code,display_name,is_system) VALUES
      ('tenant','$D1','tenant-administrator','Administrador da organizacao',true),
      ('tenant','$D1','tenant-operator','Operador da organizacao',true),
      ('tenant','$D1','tenant-client','Cliente da organizacao',true),
      ('tenant','$D2','tenant-administrator','Administrador da organizacao',true),
      ('tenant','$D2','tenant-operator','Operador da organizacao',true),
      ('tenant','$D2','tenant-client','Cliente da organizacao',true)
    ON CONFLICT DO NOTHING;

    INSERT INTO odca.role_permissions (role_id,permission_code)
    SELECT nr.id,rp.permission_code
    FROM odca.role_permissions rp
    JOIN odca.roles tr ON tr.id=rp.role_id AND tr.tenant_id='$TC'
    JOIN odca.roles nr ON nr.tenant_id IN ('$D1','$D2') AND nr.code=tr.code
    ON CONFLICT DO NOTHING;

    -- O usuario QA e criado ANTES dos memberships/roles (FK memberships_user_id_fkey).
    INSERT INTO odca.users(id,email,email_normalized,login_normalized,display_name,password_hash,must_change_password,is_platform_administrator)
    SELECT '$QA_OP','qa-d-op@odca.local','QA-D-OP@ODCA.LOCAL','qa-d-op','QA D Operador',h.password_hash,false,true
    FROM odca.users h WHERE h.email_normalized='OPERADOR@ODCA.LOCAL'
    AND NOT EXISTS(SELECT 1 FROM odca.users u WHERE u.id='$QA_OP');

    INSERT INTO odca.memberships (tenant_id,user_id,status) VALUES
      ('$D1','$USER_CLIENTE','active'),('$D1','$USER_OPERADOR','active'),('$D1','$QA_OP','active'),
      ('$D2','$USER_CLIENTE','active'),('$D2','$USER_OPERADOR','active'),('$D2','$QA_OP','active')
    ON CONFLICT (tenant_id,user_id) DO UPDATE SET status='active',updated_at=now();

    INSERT INTO odca.member_roles (tenant_id,user_id,role_id,assigned_by)
    SELECT x.tenant::uuid, x.user_id::uuid, r.id, '$USER_ADMIN'::uuid FROM (VALUES
      ('$D1','$USER_CLIENTE','tenant-administrator'),('$D1','$USER_OPERADOR','tenant-operator'),
      ('$D2','$USER_CLIENTE','tenant-administrator'),('$D2','$USER_OPERADOR','tenant-operator'),
      ('$D1','$QA_OP','tenant-operator'),('$D2','$QA_OP','tenant-operator')) x(tenant,user_id,role_code)
    JOIN odca.roles r ON r.tenant_id=x.tenant::uuid AND r.code=x.role_code
    ON CONFLICT (tenant_id,user_id,role_id) DO NOTHING;

    UPDATE odca.subscriptions SET plan_version_id='$BASIC_ID' WHERE tenant_id='$TC';
    DELETE FROM odca.template_approvals WHERE tenant_id='$TC' AND official_key='surgical-consent';
"@ | Out-Null
    Assert 'D00.setup_fixtures' $true ''

    $shaType = Sql "SELECT data_type FROM information_schema.columns WHERE table_schema='odca' AND table_name='document_versions' AND column_name='sha256';"
    $shaLit = '9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08'
    if ($shaType -eq 'bytea') { $shaSql = "E'\\x$shaLit'" } else { $shaSql = "'$shaLit'" }

    $opTok = Login-MfaAdmin 'qa-d-op@odca.local' $QA_PWD
    $cliR  = Login-Plain 'cliente.teste@odca.local' 'K8@wR3!nF6#zP2$m'
    $cliTok = (JBody $cliR.Body).accessToken
    if (-not $cliTok) { throw 'login cliente sem accessToken' }
    $cliWeb = Login-Web 'cliente.teste@odca.local' 'K8@wR3!nF6#zP2$m'
    Assert 'D00.logins' $true ''

    # ---------- D01: menu do perfil cliente + area da plataforma vedada (TC) ----------
    Write-Host '--- D01 menu/perfil ---'
    $hHome = Invoke-Page 'GET' ($web + '/') '' $cliWeb.Jar
    Assert 'D01.menu_contratos_visivel' ($hHome.Contains('Contratos')) ('len=' + $hHome.Length)
    $hAdm = Invoke-Page 'GET' ($web + '/administracao/clientes') '' $cliWeb.Jar
    Assert 'D01.cliente_sem_administracao' ($script:lastUrl.Contains('acesso-negado') -or $hAdm.Contains('Acesso negado') -or $hAdm.Contains('acesso-negado')) ('last=' + $script:lastUrl)

    # ---------- D02: instalacao limpa + wizard + overlay terapias (D1) ----------
    Write-Host '--- D02 instalacao + overlay ---'
    # D-OC1: o catalogo oficial e publicado pela plataforma; um tenant novo ve os
    # modelos publicados sem instalar nada (linha global, sem copia propria).
    $catPre = Invoke-Api 'GET' ("$api/api/v1/organizations/$D1/studio/templates?pageSize=50") $cliTok $null
    Assert 'D02.catalogo_publicado_visivel_sem_instalacao' ($catPre.Code -eq 200 -and [int](JGet ($catPre.Body | ConvertFrom-Json) 'total') -ge 9) ('code=' + $catPre.Code + ' ' + (Snip $catPre.Body))
    $r = Invoke-Api 'POST' ("$api/api/v1/organizations/$D1/studio/templates/official") $cliTok '{}'
    Assert 'D02.instalar_oficiais_D1_200' ($r.Code -eq 200) (Snip $r.Body)
    $r = Invoke-Api 'POST' ("$api/api/v1/organizations/$D2/studio/templates/official") $cliTok '{}'
    Assert 'D02.instalar_oficiais_D2_200' ($r.Code -eq 200) (Snip $r.Body)
    $mtId = Sql "SELECT id FROM odca.contract_templates WHERE owner_tenant_id='$D1' AND official_key='multiple-therapies';"
    $sgD1 = Sql "SELECT id FROM odca.contract_templates WHERE owner_tenant_id='$D1' AND official_key='surgical-consent';"
    $sgD2 = Sql "SELECT id FROM odca.contract_templates WHERE owner_tenant_id='$D2' AND official_key='surgical-consent';"
    Assert 'D02.templates_instalados' ($mtId.Length -eq 36 -and $sgD2.Length -eq 36) ('mt=' + $mtId + ' sg2=' + $sgD2)
    $wiz = Invoke-Page 'GET' ($web + "/organizacoes/$D1/documentos/novo") '' $cliWeb.Jar
    Assert 'D02.wizard_abre_D1' ($wiz.Contains('Elaborar a partir de modelo')) ('len=' + $wiz.Length)
    $r = New-Draft $D1 $cliTok $mtId 'Val D: Multiplas Terapias Aurora'
    Assert 'D02.minuta_terapia_201' ($r.Code -eq 201) (Snip $r.Body)
    $hDraft = (JBody $r.Body).id
    $hd = Get-Draft $D1 $cliTok $hDraft
    $contentU = [regex]::Unescape(($(($hd.content) | ConvertTo-Json -Depth 60 -Compress)))
    Assert 'D02.overlay_clausula_2A' ($contentU.Contains('CLÁUSULA 2ª-A') -or $contentU.Contains('CLÁUSULA 2')) (Snip $contentU)
    $sessF = @(@($hd.fields) | Where-Object { (JGet $_ 'id') -eq 'sessions_psychology' })
    $feeF  = @(@($hd.fields) | Where-Object { (JGet $_ 'id') -eq 'fee_neuropsychology' })
    $totF  = @(@($hd.fields) | Where-Object { $null -ne $_.PSObject.Properties['formulaTerms'] })
    Assert 'D02.overlay_campo_sessoes' (@($sessF).Count -eq 1) ('n=' + @($sessF).Count)
    Assert 'D02.overlay_campo_mensalidade' (@($feeF).Count -eq 1) ('n=' + @($feeF).Count)
    Assert 'D02.formula_recalc_presente' (@($totF).Count -ge 1) ('n=' + @($totF).Count)
    Assert 'D02.terapia_sem_aprovacao_odca' ((JGet $hd 'requiresOdcaApproval') -ne 'True') (Snip $r.Body)
    $hEd = Invoke-Page 'GET' ($web + "/organizacoes/$D1/estudio/minutas/$hDraft") '' $cliWeb.Jar
    Assert 'D02.editor_estudio_abre' ($hEd.Contains('data-fields')) ('len=' + $hEd.Length)

    # ---------- D03: regras de preenchimento (D1) ----------
    Write-Host '--- D03 preenchimento canonico ---'
    $hd = Get-Draft $D1 $cliTok $hDraft
    Auto-Fill $hd @{}
    Set-Val $hd.values 'therapy_psychology' 'Sim'
    Set-Val $hd.values 'therapy_neuropsychology' 'Não'
    Set-Val $hd.values 'therapy_occupational' 'Não'
    Set-Val $hd.values 'therapy_speech' 'Não'
    Auto-Fill $hd @{ 'fee_psychology' = '200.00'; 'sessions_psychology' = '4'; 'monthly_fee_total' = '' }
    $r = Save-Draft $D1 $cliTok $hDraft $hd
    Assert 'D03.salvar_completo_200' ($r.Code -eq 200) (Snip $r.Body)
    $hd = Get-Draft $D1 $cliTok $hDraft
    $tot = Get-Val $hd.values 'monthly_fee_total'
    Assert 'D03.formula_recalculada_800' ((($null -ne $tot)) -and (('' + $tot.value) -eq '800.00')) $(if ($tot) { '' + $tot.value } else { 'ausente' })
    $fee = Get-Val $hd.values 'fee_psychology'
    Assert 'D03.moeda_canonica_200' ((($null -ne $fee)) -and (('' + $fee.value) -eq '200.00')) $(if ($fee) { '' + $fee.value } else { 'ausente' })
    $dbCol = Sql "SELECT quote_ident(column_name) FROM information_schema.columns WHERE table_schema='odca' AND table_name='contract_drafts' AND column_name IN ('values','values_jsonb','values_json') ORDER BY CASE column_name WHEN 'values' THEN 0 ELSE 1 END LIMIT 1;"
    $dbTot = Sql "SELECT count(*) FROM odca.contract_drafts WHERE id='$hDraft' AND $dbCol::text LIKE '%800.00%';"
    Assert 'D03.total_persistido_no_banco' ([int]$dbTot -eq 1) ('rows=' + $dbTot)

    # ---------- D04: validacoes server-side (D1) ----------
    Write-Host '--- D04 validacoes ---'
    $hd = Get-Draft $D1 $cliTok $hDraft
    Set-Val $hd.values 'contractor_document' '11144477734'
    $r = Save-Draft $D1 $cliTok $hDraft $hd
    Assert 'D04.cpf_invalido_400' ($r.Code -eq 400) (Snip $r.Body)
    $hd = Get-Draft $D1 $cliTok $hDraft
    Set-Val $hd.values 'contractor_name' ''
    $r = Save-Draft $D1 $cliTok $hDraft $hd
    Assert 'D04.obrigatorio_vazio_salvar_rascunho_200' ($r.Code -eq 200) (Snip $r.Body)
    $genTry = ConvertTo-Json -InputObject ([pscustomobject]@{ idempotencyKey = [guid]::NewGuid().ToString(); expectedVersion = [int64](Get-Draft $D1 $cliTok $hDraft).version }) -Depth 5
    $rgTry = Invoke-Api 'POST' ("$api/api/v1/organizations/$D1/studio/drafts/$hDraft/versions") $cliTok $genTry
    $rgTryText = Dec $rgTry.Body
    Assert 'D04.obrigatorio_vazio_geracao_400_pontua_campo' ($rgTry.Code -eq 400 -and ($rgTryText.Contains('obrigat') -or $rgTryText.Contains('pendente'))) (Snip $rgTry.Body)
    $hd = Get-Draft $D1 $cliTok $hDraft
    Set-Val $hd.values 'contractor_document' '11144477735'
    Set-Val $hd.values 'contractor_name' 'Cliente Homologacao Val D'
    $r = Save-Draft $D1 $cliTok $hDraft $hd
    Assert 'D04.corrigido_200' ($r.Code -eq 200) (Snip $r.Body)

    # ---------- D10: conflito de aba com versionamento otimista (D1) ----------
    Write-Host '--- D10 conflito de aba ---'
    $cur = Get-Draft $D1 $cliTok $hDraft
    $stale = Get-Draft $D1 $cliTok $hDraft
    $stale.version = [int64]$cur.version + 5
    $sv = Get-Val $stale.values 'jurisdiction_city'
    if ($sv) { $sv.value = 'Rio de Janeiro/RJ' }
    $r = Save-Draft $D1 $cliTok $hDraft $stale
    Assert 'D10.versao_obsoleta_409' ($r.Code -eq 409) (Snip $r.Body)
    $after = Get-Draft $D1 $cliTok $hDraft
    $jc = Get-Val $after.values 'jurisdiction_city'
    Assert 'D10.conteudo_preservado' ((('' + $jc.value) -ne 'Rio de Janeiro/RJ') -and ([int64]$after.version -eq [int64]$cur.version)) ('v=' + $after.version + ' city=' + ('' + $jc.value))

    # ---------- D05: geracao sem variaveis + capa/sumario/callout (D1) ----------
    Write-Host '--- D05 geracao final ---'
    $pre = Get-Draft $D1 $cliTok $hDraft
    $tmpEntry = Get-Val $pre.values 'witnesses_identification'
    if ($null -eq $tmpEntry) { Set-Val $pre.values 'witnesses_identification' 'x' } else { $tmpWasEmpty = [string]::IsNullOrWhiteSpace('' + $tmpEntry.value) }
    Set-Val $pre.values 'witnesses_identification' ''
    $r = Save-Draft $D1 $cliTok $hDraft $pre
    $genBody1 = ConvertTo-Json -InputObject ([pscustomobject]@{ idempotencyKey = [guid]::NewGuid().ToString(); expectedVersion = [int64](Get-Draft $D1 $cliTok $hDraft).version }) -Depth 5
    $rgPre = Invoke-Api 'POST' ("$api/api/v1/organizations/$D1/studio/drafts/$hDraft/versions") $cliTok $genBody1
    $hd = Get-Draft $D1 $cliTok $hDraft
    Set-Val $hd.values 'witnesses_identification' 'Maria Testemunha CPF 390.533.447-05 e Jose Testemunha CPF 111.056.254-42'
    $r = Save-Draft $D1 $cliTok $hDraft $hd
    Assert 'D05.restaurar_testemunhas_200' ($r.Code -eq 200) (Snip $r.Body)

    $hd = Get-Draft $D1 $cliTok $hDraft
    $genBody = ConvertTo-Json -InputObject ([pscustomobject]@{ idempotencyKey = [guid]::NewGuid().ToString(); expectedVersion = [int64]$hd.version }) -Depth 5
    $rg = Invoke-Api 'POST' ("$api/api/v1/organizations/$D1/studio/drafts/$hDraft/versions") $cliTok $genBody
    Assert 'D05.gerar_versao_201' ($rg.Code -eq 201) (Snip $rg.Body)
    $hVer = ''
    if ($rg.Code -eq 201) { $hVer = (JBody $rg.Body).id }
    Assert 'D05.id_da_versao' ([bool]$hVer) (Snip $rg.Body)
    $rv = Invoke-Api 'GET' ("$api/api/v1/organizations/$D1/studio/versions/$hVer") $cliTok $null
    $vd = JBody $rv.Body
    $htmlDoc = JGet $vd 'renderedHtml'
    Assert 'D05.capa_presente' ($htmlDoc.Contains('document-cover')) ('len=' + $htmlDoc.Length)
    Assert 'D05.sumario_presente' ($htmlDoc.Contains('document-toc')) ('len=' + $htmlDoc.Length)
    Assert 'D05.capa_com_organizacao' ($htmlDoc.Contains('Val D: Centro Terapeutico Aurora') -and ($htmlDoc.Contains('45.987.654/0001-18') -or $htmlDoc.Contains('45987654000118'))) ('len=' + $htmlDoc.Length)
    Assert 'D05.sem_variavel_nao_resolvida' (-not $htmlDoc.Contains('Não informado') -and -not $htmlDoc.Contains('document-field-empty')) ('idx=' + $htmlDoc.IndexOf('informado'))
    $hContract = Sql "SELECT contract_id FROM odca.generated_contract_versions WHERE id='$hVer';"
    $printHtml = Invoke-Page 'GET' ($web + "/organizacoes/$D1/contratos/$hContract/versoes/$hVer/imprimir") '' $cliWeb.Jar
    Assert 'D05.impressao_renderiza_capa' ($printHtml.Contains('document-cover') -and $printHtml.Contains('document-toc')) ('last=' + $script:lastUrl)
    $pdfOk = Generate-PdfAndWait $D1 $cliTok $hVer
    Assert 'D05.pdf_gerado' $pdfOk ''

    # ---------- D06: gate de perfil no wizard (D1) ----------
    Write-Host '--- D06 gate de perfil ---'
    $r = New-Draft $D1 $cliTok $sgD1 'Val D: Cirurgica na Terapeutica'
    Assert 'D06.cirurgico_fora_do_perfil_409' ($r.Code -eq 409 -and ((Dec $r.Body).Contains('profile') -or (Dec $r.Body).Contains('perfil'))) ('code=' + $r.Code + ' ' + (Snip $r.Body))

    # ---------- D07: modelo cirurgico pendente (D2) ----------
    Write-Host '--- D07 aprovacao ODCA pendente ---'
    $r = New-Draft $D2 $cliTok $sgD2 'Val D: Consentimento Cirurgico Harmonia'
    Assert 'D07.minuta_cirurgica_201' ($r.Code -eq 201) (Snip $r.Body)
    $sDraft = (JBody $r.Body).id
    $sd = Get-Draft $D2 $cliTok $sDraft
    Assert 'D07.requiresOdcaApproval_true' ((JGet $sd 'requiresOdcaApproval') -eq 'True') (Snip $r.Body)
    $hBanner = Invoke-Page 'GET' ($web + "/organizacoes/$D2/estudio/minutas/$sDraft") '' $cliWeb.Jar
    Assert 'D07.banner_pendente_na_web' ($hBanner.Contains('data-pending-odca-approval')) ('len=' + $hBanner.Length)
    $sgFill = @{
        'surgeon_name'          = 'Dra. Marina Cirurgia Val D'
        'surgeon_document'      = '11144477735'
        'patient_name'          = 'Paciente Teste Val D'
        'patient_document'      = '52998224725'
        'procedure_description' = 'Rinoplastia funcional com septoplastia.'
        'surgery_date'          = '2027-03-15'
        'facility_name'         = 'Hospital Central Val D'
        'anesthesia_type'       = 'Anestesia geral'
        'has_representative'    = 'Não'
        'procedure_fee'         = '1234.56'
        'payment_terms'         = 'À vista'
        'risks_acknowledgement' = 'Li e compreendi os riscos descritos'
        'signing_date'          = '2026-10-15'
        'jurisdiction_city'     = 'Belém/PA'
        'organization_name'     = Sql "SELECT display_name FROM odca.tenants WHERE id='$D2';"
    }
    $sd = Get-Draft $D2 $cliTok $sDraft
    Auto-Fill $sd $sgFill
    $r = Save-Draft $D2 $cliTok $sDraft $sd
    Assert 'D07.salvar_cirurgico_200' ($r.Code -eq 200) (Snip $r.Body)
    $sd = Get-Draft $D2 $cliTok $sDraft
    $genBody = ConvertTo-Json -InputObject ([pscustomobject]@{ idempotencyKey = [guid]::NewGuid().ToString(); expectedVersion = [int64]$sd.version }) -Depth 5
    $rg = Invoke-Api 'POST' ("$api/api/v1/organizations/$D2/studio/drafts/$sDraft/versions") $cliTok $genBody
    Assert 'D07.gerar_cirurgico_201' ($rg.Code -eq 201) (Snip $rg.Body)
    $sVer = ''
    if ($rg.Code -eq 201) { $sVer = (JBody $rg.Body).id }
    $sv2 = Invoke-Api 'GET' ("$api/api/v1/organizations/$D2/studio/versions/$sVer") $cliTok $null
    $sHtml = JGet (JBody $sv2.Body) 'renderedHtml'
    Assert 'D07.callout_atencao_no_html' ($sHtml.Contains('document-callout')) ('len=' + $sHtml.Length)
    Assert 'D07.sem_variavel_cirurgico' (-not $sHtml.Contains('Não informado')) ('idx=' + $sHtml.IndexOf('informado'))
    $rr = Invoke-Api 'GET' ("$api/api/v1/organizations/$D2/studio/versions/$sVer/signature-preparation/readiness") $cliTok $null
    $rd = JBody $rr.Body
    $blockCodes = @(@($rd.blockers) | ForEach-Object { $_.code })
    Assert 'D07.blocker_aprovacao_odca' (@($blockCodes) -contains 'approval.odca.required') ('codes=' + ($blockCodes -join ','))
    Assert 'D07.canConfirm_false' ((JGet $rd 'canConfirm') -eq 'False') ('canConfirm=' + (JGet $rd 'canConfirm'))

    # ---------- D08: decisao ODCA libera o preparativo (D2) ----------
    Write-Host '--- D08 aprovacao ODCA ---'
    $q = Invoke-Api 'GET' ($api + '/api/v1/platform/template-approvals') $opTok $null
    $row = @((JBody $q.Body)) | Where-Object { (JGet $_ 'officialKey') -eq 'surgical-consent' -and (JGet $_ 'tenantId') -eq $D2 } | Select-Object -First 1
    Assert 'D08.fila_contem_harmonia' ($null -ne $row -and (JGet $row 'decision') -eq '') (Snip ($row | ConvertTo-Json -Compress))
    $null = Generate-PdfAndWait $D2 $cliTok $sVer
    $r = Invoke-Api 'POST' ($api + "/api/v1/platform/template-approvals/$D2/surgical-consent/decision") $cliTok (ToJson @{ decision = 'aprovado'; note = 'Tentativa do cliente aprovar sozinho.' })
    Assert 'D08.cliente_nao_decide_403' ($r.Code -eq 403 -or $r.Code -eq 401) ('code=' + $r.Code)
    $r = Invoke-Api 'POST' ($api + "/api/v1/platform/template-approvals/$D2/surgical-consent/decision") $opTok (ToJson @{ decision = 'aprovado'; note = 'Modelo homologado pelo comite clinico da ODCA na secao D.' })
    Assert 'D08.decisao_aprovado_200' ($r.Code -eq 200) (Snip $r.Body)
    $row2 = @((JBody (Invoke-Api 'GET' ($api + '/api/v1/platform/template-approvals') $opTok $null).Body)) | Where-Object { (JGet $_ 'officialKey') -eq 'surgical-consent' -and (JGet $_ 'tenantId') -eq $D2 } | Select-Object -First 1
    Assert 'D08.decisao_persistida' ((JGet $row2 'decision') -eq 'aprovado') (Snip ($row2 | ConvertTo-Json -Compress))
    $rr = Invoke-Api 'GET' ("$api/api/v1/organizations/$D2/studio/versions/$sVer/signature-preparation/readiness") $cliTok $null
    $rd = JBody $rr.Body
    $blockCodes2 = @(@($rd.blockers) | ForEach-Object { $_.code })
    Assert 'D08.blocker_removido' (-not (@($blockCodes2) -contains 'approval.odca.required')) ('codes=' + ($blockCodes2 -join ','))
    $opId = [guid]::NewGuid().ToString()
    $confBody = ConvertTo-Json -InputObject ([pscustomobject]@{
        expectedVersion = 0
        confirm = $true
        operationId = $opId
        participants = @([pscustomobject]@{
            id = [guid]::NewGuid().ToString(); participantType = 'professional'; sourceId = $null
            role = 'Cirurgião responsável'; name = 'Dra. Val D Teste'
            email = 'cirurgiao.vald@odca.local'; phone = $null; position = 1
            signedAt = $null; signedBy = $null
        })
    }) -Depth 8
    $rc = Invoke-Api 'PUT' ("$api/api/v1/organizations/$D2/studio/versions/$sVer/signature-preparation") $cliTok $confBody
    Assert 'D08.confirmar_preparativo_200' ($rc.Code -eq 200) (Snip $rc.Body)
    $prepState = Sql "SELECT coalesce(status,'') FROM odca.signature_preparations WHERE generated_version_id='$sVer' ORDER BY created_at DESC LIMIT 1;"
    Assert 'D08.preparativo_confirmado_no_db' ($prepState -eq 'confirmed') ('status=' + $prepState)

    # ---------- D09: reverter decisao re-bloqueia (D2) ----------
    Write-Host '--- D09 reverter decisao ---'
    $r = Invoke-Api 'POST' ($api + "/api/v1/platform/template-approvals/$D2/surgical-consent/decision") $opTok (ToJson @{ decision = 'reprovado'; note = 'oi' })
    Assert 'D09.reprovacao_curta_400' ($r.Code -eq 400) ('code=' + $r.Code)
    $r = Invoke-Api 'POST' ($api + "/api/v1/platform/template-approvals/$D2/surgical-consent/decision") $opTok (ToJson @{ decision = 'reprovado'; note = 'Cláusula de risco sem adequacao ao CFM.' })
    Assert 'D09.reprovado_200' ($r.Code -eq 200) (Snip $r.Body)
    $rr = Invoke-Api 'GET' ("$api/api/v1/organizations/$D2/studio/versions/$sVer/signature-preparation/readiness") $cliTok $null
    $blockCodes3 = @(@((JBody $rr.Body).blockers) | ForEach-Object { $_.code })
    Assert 'D09.blocker_volta' ((@($blockCodes3) -contains 'approval.odca.required') -or (@($blockCodes3) -contains 'approval.odca.rejected')) ('codes=' + ($blockCodes3 -join ','))
    $r = Invoke-Api 'POST' ($api + "/api/v1/platform/template-approvals/$D2/surgical-consent/decision") $opTok (ToJson @{ decision = 'aprovado'; note = 'Liberacao final apos checagem do gate secao D.' })
    Assert 'D09.reaprovacao_final_200' ($r.Code -eq 200) (Snip $r.Body)

    # ---------- D11: preparativo completo com participantes (D1) ----------
    Write-Host '--- D11 preparativo de assinatura ---'
    $rr = Invoke-Api 'GET' ("$api/api/v1/organizations/$D1/studio/versions/$hVer/signature-preparation/readiness") $cliTok $null
    $rd = JBody $rr.Body
    Assert 'D11.readiness_200' ($rr.Code -eq 200) ('code=' + $rr.Code)
    $opId2 = [guid]::NewGuid().ToString()
    $confBody2 = ConvertTo-Json -InputObject ([pscustomobject]@{
        expectedVersion = 0
        confirm = $true
        operationId = $opId2
        participants = @(
            [pscustomobject]@{ id = [guid]::NewGuid().ToString(); participantType = 'professional'; sourceId = $null
                role = 'Responsável técnico'; name = 'Terapeuta Val D'
                email = 'terapeuta.vald@odca.local'; phone = $null; position = 1; signedAt = $null; signedBy = $null },
            [pscustomobject]@{ id = [guid]::NewGuid().ToString(); participantType = 'organization_representative'; sourceId = $null
                role = 'Contratante'; name = 'Cliente Homologacao Val D'
                email = 'paciente.vald@odca.local'; phone = $null; position = 2; signedAt = $null; signedBy = $null }
        )
    }) -Depth 8
    $rc2 = Invoke-Api 'PUT' ("$api/api/v1/organizations/$D1/studio/versions/$hVer/signature-preparation") $cliTok $confBody2
    Assert 'D11.confirmar_preparativo_D1_200' ($rc2.Code -eq 200) (Snip $rc2.Body)
    $parts = Sql "SELECT count(*) FROM odca.signature_participants p JOIN odca.signature_preparations pr ON pr.id=p.preparation_id WHERE pr.generated_version_id='$hVer' AND pr.status='confirmed';"
    Assert 'D11.dois_participantes_confirmados' ([int]$parts -eq 2) ('rows=' + $parts)

    # ---------- D12: proposta de renovacao na central (D1) ----------
    Write-Host '--- D12 proposta de renovacao ---'
    $today = Sql "SELECT (now() AT TIME ZONE timezone)::date::text FROM odca.tenants WHERE id='$D1';"
    Sql @"
    INSERT INTO odca.contracts (tenant_id,title,start_date,end_date,value,currency)
    VALUES ('$D1','Val D: Contrato Renovacao Aurora','2025-10-01','2026-12-31',5000.00,'BRL');
"@ | Out-Null
    $cE1 = Sql "SELECT id FROM odca.contracts WHERE tenant_id='$D1' AND title='Val D: Contrato Renovacao Aurora' LIMIT 1;"
    $r = Invoke-Api 'POST' ($api + "/api/v1/organizations/$D1/renewals/contracts/$cE1") $cliTok (ToJson @{
        kind = 'renewal'; reason = 'Renovacao anual com reajuste pactuado.'; priority = 'critical'
        proposedEndDate = '2027-12-31'; proposedValue = 500; valueChangeMode = 'increase'; currency = 'BRL'
        effectiveOn = $today; contractVersion = 1; responsibleId = $USER_CLIENTE; idempotencyKey = [guid]::NewGuid().ToString() })
    Assert 'D12.proposta_201' ($r.Code -eq 201) (Snip $r.Body)
    $propId = [regex]::Match($r.Body, '"id"\s*:\s*"([0-9a-fA-F-]{36})"').Groups[1].Value
    if ($propId.Length -ne 36) { $propId = Sql "SELECT id FROM odca.contract_change_requests WHERE contract_id='$cE1' ORDER BY created_at DESC LIMIT 1;" }
    Assert 'D12.id_da_proposta' ($propId.Length -eq 36) ('propId=' + $propId)
    $list = Invoke-Api 'GET' ($api + "/api/v1/organizations/$D1/renewals") $cliTok $null
    Assert 'D12.central_lista_200' ($list.Code -eq 200) ('code=' + $list.Code)
    $state = Get-RowState $propId
    Assert 'D12.status_draft_e_prioridade' ($state.StartsWith('draft|')) ('state=' + $state)
    $pri = Sql "SELECT priority FROM odca.contract_change_requests WHERE id='$propId';"
    $pv = Sql "SELECT coalesce(proposed_value::text,'-') FROM odca.contract_change_requests WHERE id='$propId';"
    $pe = Sql "SELECT coalesce(proposed_end_date::text,'-') FROM odca.contract_change_requests WHERE id='$propId';"
    Assert 'D12.proposta_persistida' ($pri -eq 'critical' -and $pv -eq '5500.00' -and $pe -eq '2027-12-31') ("pri=$pri pv=$pv pe=$pe")
    $renWeb = Invoke-Page 'GET' ($web + "/organizacoes/$D1/renovacoes") '' $cliWeb.Jar
    Assert 'D12.pagina_renovacoes_mostra_critica' ($renWeb.Contains('Crítica') -or $renWeb.Contains('crítica') -or $renWeb.Contains('Renovação Aurora')) ('len=' + $renWeb.Length)

    # ---------- D13: submeter -> formalizar (evidencia) -> aplicar (D1) ----------
    Write-Host '--- D13 formalizacao com evidencia ---'
    $stateParts = (Get-RowState $propId) -split '\|'
    $r = Invoke-Api 'POST' ($api + "/api/v1/organizations/$D1/renewals/$propId/submit") $cliTok (ToJson @{ rowVersion = [long]$stateParts[1] })
    Assert 'D13.submeter_200' ($r.Code -eq 200) ('code=' + $r.Code + ' ' + (Snip $r.Body))
    $stateParts = (Get-RowState $propId) -split '\|'
    Assert 'D13.status_in_review' ($stateParts[0] -eq 'in_review') ('state=' + ($stateParts -join '|'))
    $todayD2 = Sql "SELECT (now() AT TIME ZONE timezone)::date::text FROM odca.tenants WHERE id='$D1';"
    Sql @"
    INSERT INTO odca.contract_documents (id,tenant_id,contract_id,title,created_by)
    VALUES ('60000000-0000-4000-8000-0000000000D1','$D1','$cE1','Evidencia de formalizacao Val D','$USER_CLIENTE');
    INSERT INTO odca.document_versions (id,tenant_id,document_id,contract_id,version_number,display_name,storage_key,byte_size,sha256,detected_type,uploaded_by,security_status)
    VALUES ('60000000-0000-4000-8000-0000000000D2','$D1','60000000-0000-4000-8000-0000000000D1','$cE1',1,'Evidencia de formalizacao Val D.pdf','val-d/formalizacao-aurora.pdf',4096,$shaSql,'pdf','$USER_CLIENTE','safe');
"@ | Out-Null
    $stateParts = (Get-RowState $propId) -split '\|'
    $r = Invoke-Api 'POST' ($api + "/api/v1/organizations/$D1/renewals/$propId/formalization") $cliTok (ToJson @{
        rowVersion = [long]$stateParts[1]; evidenceVersionId = '60000000-0000-4000-8000-0000000000D2'
        formalizedOn = $todayD2; justification = 'Formalizacao homologada na secao D com termo assinado anexado.' })
    Assert 'D13.formalizar_200' ($r.Code -eq 200) ('code=' + $r.Code + ' ' + (Snip $r.Body))
    $stateParts = (Get-RowState $propId) -split '\|'
    Assert 'D13.status_formalized' ($stateParts[0] -eq 'formalized') ('state=' + ($stateParts -join '|'))
    $r = Invoke-Api 'POST' ($api + "/api/v1/organizations/$D1/renewals/$propId/apply") $cliTok (ToJson @{ rowVersion = [long]$stateParts[1] })
    Assert 'D13.aplicar_200' ($r.Code -eq 200 -or $r.Code -eq 204) ('code=' + $r.Code + ' ' + (Snip $r.Body))
    $contr = Sql "SELECT coalesce(end_date::text,'-')||'|'||coalesce(value::text,'-')||'|'||version::text FROM odca.contracts WHERE id='$cE1';"
    Assert 'D13.contrato_atualizado' ($contr.StartsWith('2027-12-31|5500.00')) ('contr=' + $contr)
    $app = Sql "SELECT application_status FROM odca.contract_change_requests WHERE id='$propId';"
    Assert 'D13.aplication_applied' ($app -eq 'applied') ('app=' + $app)
    $stateParts = (Get-RowState $propId) -split '\|'
    $r = Invoke-Api 'POST' ($api + "/api/v1/organizations/$D1/renewals/$propId/apply") $cliTok (ToJson @{ rowVersion = [long]$stateParts[1] })
    Assert 'D13.reaplicar_409' ($r.Code -eq 409) ('code=' + $r.Code)

    # ---------- D14: vigencia indeterminada segue sem data (D2) ----------
    Write-Host '--- D14 vigencia indeterminada ---'
    Sql "INSERT INTO odca.contracts (tenant_id,title,start_date,end_date,value,currency) VALUES ('$D2','Val D: Contrato Indeterminado Harmonia','2025-06-01',NULL,2400.00,'BRL');" | Out-Null
    $cE2 = Sql "SELECT id FROM odca.contracts WHERE tenant_id='$D2' AND title='Val D: Contrato Indeterminado Harmonia' LIMIT 1;"
    $todayD2b = Sql "SELECT (now() AT TIME ZONE timezone)::date::text FROM odca.tenants WHERE id='$D2';"
    $r = Invoke-Api 'POST' ($api + "/api/v1/organizations/$D2/renewals/contracts/$cE2") $cliTok (ToJson @{
        kind = 'amendment'; reason = 'Aditivo de reajuste em contrato por tempo indeterminado.'; priority = 'normal'
        proposedValue = 1200; valueChangeMode = 'increase'; currency = 'BRL'
        effectiveOn = $todayD2b; contractVersion = 1; responsibleId = $USER_CLIENTE; idempotencyKey = [guid]::NewGuid().ToString() })
    Assert 'D14.proposta_indeterminado_201' ($r.Code -eq 201) (Snip $r.Body)
    $prop2 = [regex]::Match($r.Body, '"id"\s*:\s*"([0-9a-fA-F-]{36})"').Groups[1].Value
    $p2 = (Get-RowState $prop2) -split '\|'
    $null = Invoke-Api 'POST' ($api + "/api/v1/organizations/$D2/renewals/$prop2/submit") $cliTok (ToJson @{ rowVersion = [long]$p2[1] })
    $p2 = (Get-RowState $prop2) -split '\|'
    Sql @"
    INSERT INTO odca.contract_documents (id,tenant_id,contract_id,title,created_by)
    VALUES ('60000000-0000-4000-8000-0000000000D3','$D2','$cE2','Evidencia aditivo Harmonia','$USER_CLIENTE');
    INSERT INTO odca.document_versions (id,tenant_id,document_id,contract_id,version_number,display_name,storage_key,byte_size,sha256,detected_type,uploaded_by,security_status)
    VALUES ('60000000-0000-4000-8000-0000000000D4','$D2','60000000-0000-4000-8000-0000000000D3','$cE2',1,'Evidencia aditivo Harmonia.pdf','val-d/formalizacao-harmonia.pdf',4096,$shaSql,'pdf','$USER_CLIENTE','safe');
"@ | Out-Null
    $p2 = (Get-RowState $prop2) -split '\|'
    $null = Invoke-Api 'POST' ($api + "/api/v1/organizations/$D2/renewals/$prop2/formalization") $cliTok (ToJson @{
        rowVersion = [long]$p2[1]; evidenceVersionId = '60000000-0000-4000-8000-0000000000D4'
        formalizedOn = $todayD2b; justification = 'Aditivo formalizado na homologacao da secao D.' })
    $p2 = (Get-RowState $prop2) -split '\|'
    $r = Invoke-Api 'POST' ($api + "/api/v1/organizations/$D2/renewals/$prop2/apply") $cliTok (ToJson @{ rowVersion = [long]$p2[1] })
    Assert 'D14.aplicar_sem_data_200' ($r.Code -eq 200 -or $r.Code -eq 204) ('code=' + $r.Code + ' ' + (Snip $r.Body))
    $contr2 = Sql "SELECT coalesce(end_date::text,'NULL')||'|'||coalesce(value::text,'-') FROM odca.contracts WHERE id='$cE2';"
    Assert 'D14.data_continua_nula_valor_novo' ($contr2.StartsWith('NULL|3600.00')) ('contr=' + $contr2)

    # ---------- D15: lembretes configuraveis 45/20/5 (D1) ----------
    Write-Host '--- D15 lembretes ---'
    $r = Invoke-Api 'PUT' ($api + "/api/v1/organizations/$D1/renewals/config") $cliTok (ToJson @{ days = @(45, 20, 5) })
    Assert 'D15.config_45_20_5_200' ($r.Code -eq 200) (Snip $r.Body)
    $g = Invoke-Api 'GET' ($api + "/api/v1/organizations/$D1/renewals/config") $cliTok $null
    Assert 'D15.config_lida_de_volta' ($g.Code -eq 200 -and $g.Body.Contains('45') -and $g.Body.Contains('20') -and $g.Body.Contains('5')) ('body=' + (Snip $g.Body))
    $dbs = Sql "SELECT array_to_string(renewal_reminder_days,',') FROM odca.tenants WHERE id='$D1';"
    Assert 'D15.config_persistida_45_20_5' ($dbs -eq '45,20,5') ('days=' + $dbs)
    $r = Invoke-Api 'PUT' ($api + "/api/v1/organizations/$D1/renewals/config") $cliTok (ToJson @{ days = @(5, 20, 45) })
    Assert 'D15.config_ordem_invalida_400' ($r.Code -eq 400) ('code=' + $r.Code)

    # ---------- D16: operador nao decide renovacoes (D1) ----------
    Write-Host '--- D16 limites do operador ---'
    $cv1 = Sql "SELECT version FROM odca.contracts WHERE id='$cE1';"
    $r = Invoke-Api 'POST' ($api + "/api/v1/organizations/$D1/renewals/contracts/$cE1") $opTok (ToJson @{
        kind = 'renewal'; reason = 'Segunda proposta preparada pelo operador.'; priority = 'normal'
        proposedEndDate = '2028-12-31'; currency = 'BRL'; effectiveOn = $today; contractVersion = [long]$cv1
        responsibleId = $USER_OPERADOR; idempotencyKey = [guid]::NewGuid().ToString() })
    Assert 'D16.operador_prepara_201' ($r.Code -eq 201) (Snip $r.Body)
    $opProp = [regex]::Match($r.Body, '"id"\s*:\s*"([0-9a-fA-F-]{36})"').Groups[1].Value
    $p3 = (Get-RowState $opProp) -split '\|'
    $r = Invoke-Api 'POST' ($api + "/api/v1/organizations/$D1/renewals/$opProp/submit") $opTok (ToJson @{ rowVersion = [long]$p3[1] })
    Assert 'D16.operador_submit_403' ($r.Code -eq 403) ('code=' + $r.Code)
    $p3 = (Get-RowState $opProp) -split '\|'
    Assert 'D16.proposta_segue_draft' ($p3[0] -eq 'draft') ('state=' + ($p3 -join '|'))
    $r = Invoke-Api 'PUT' ($api + "/api/v1/organizations/$D1/renewals/config") $opTok (ToJson @{ days = @(60, 30, 10) })
    Assert 'D16.operador_config_403' ($r.Code -eq 403) ('code=' + $r.Code)
    $cliWebRen = Invoke-Page 'GET' ($web + "/organizacoes/$D1/renovacoes") '' $cliWeb.Jar
    Assert 'D16.web_renovacoes_ok_para_admin' ($cliWebRen.Contains('Renova') -or -not $cliWebRen.Contains('Acesso negado')) ('len=' + $cliWebRen.Length)

    # ---------- D17/D18: solicitacoes Enterprise com pausa, msg ODCA e reabertura (D2) ----------
    Write-Host '--- D17/D18 solicitacoes Enterprise ---'
    $idem = [guid]::NewGuid().ToString()
    $r = Invoke-Api 'POST' ($api + "/api/v1/organizations/$D2/solicitations") $cliTok (ToJson @{
        service = 'esclarecimento'; priority = 'normal'; subject = 'Val D: duvida sobre parametros cirurgicos'
        body = 'Cliente Enterprise abre solicitacao na homologacao da secao D.'; idempotencyKey = $idem })
    Assert 'D17.abrir_enterprise_200' ($r.Code -eq 200) (Snip $r.Body)
    $solId = JGet (JBody $r.Body) 'id'
    $rvS = [long](JGet (JBody $r.Body) 'rowVersion')
    $qp = @(JBody (Invoke-Api 'GET' ($api + "/api/v1/platform/solicitations?tenantId=$D2") $opTok $null).Body)
    Assert 'D17.fila_odca_recebe' (@($qp | Where-Object { (JGet $_ 'id') -eq $solId }).Count -eq 1) (Snip ($qp | ConvertTo-Json -Compress))
    $r = Invoke-Api 'POST' ($api + "/api/v1/platform/solicitations/$solId/actions") $opTok (ToJson @{ action = 'triar'; priority = 'alta'; rowVersion = $rvS; tenantId = $D2 })
    Assert 'D17.triar_200' ($r.Code -eq 200) (Snip $r.Body)
    $rvS = [long](JGet (JBody $r.Body) 'rowVersion')

    $r = Invoke-Api 'POST' ($api + "/api/v1/platform/solicitations/$solId/actions") $opTok (ToJson @{ action = 'pausar'; rowVersion = $rvS; tenantId = $D2 })
    Assert 'D18.pausar_sem_texto_400' ($r.Code -eq 400) ('code=' + $r.Code)
    $r = Invoke-Api 'POST' ($api + "/api/v1/platform/solicitations/$solId/actions") $opTok (ToJson @{ action = 'pausar'; text = 'Aguardando laudo do cirurgiao.'; rowVersion = $rvS; tenantId = $D2 })
    Assert 'D18.pausar_200' ($r.Code -eq 200) (Snip $r.Body)
    $rvS = [long](JGet (JBody $r.Body) 'rowVersion')
    Assert 'D18.paused_flag' ((JGet (JBody $r.Body) 'paused') -eq 'True') (Snip $r.Body)
    $trail = Sql "SELECT CASE WHEN count(*)>=1 THEN 'ok' ELSE 'bad' END FROM odca.solicitation_pauses WHERE solicitation_id='$solId';"
    Assert 'D18.pause_trail' ($trail -eq 'ok') ('trail=' + $trail)
    $r = Invoke-Api 'POST' ($api + "/api/v1/platform/solicitations/$solId/messages") $opTok (ToJson @{ body = 'ODCA: equipe cirurgica revisando os parametros.'; rowVersion = $rvS; tenantId = $D2 })
    Assert 'D18.msg_odca_200' ($r.Code -eq 200) ('code=' + $r.Code + ' ' + (Snip $r.Body))
    $rvS = [long](JGet (JBody $r.Body) 'rowVersion')
    $d2sol = JBody (Invoke-Api 'GET' ($api + "/api/v1/organizations/$D2/solicitations/$solId") $cliTok $null).Body
    $msgTexts = @($d2sol.messages | ForEach-Object { JGet $_ 'body' })
    Assert 'D18.msg_odca_visivel_ao_cliente' (@($msgTexts | Where-Object { $_ -like '*ODCA*' }).Count -ge 1) ('msgs=' + ($msgTexts -join ' | '))
    $null = Invoke-Api 'POST' ($api + "/api/v1/platform/solicitations/$solId/actions") $opTok (ToJson @{ action = 'retomar'; rowVersion = $rvS; tenantId = $D2 })
    $d2sol = JBody (Invoke-Api 'GET' ($api + "/api/v1/organizations/$D2/solicitations/$solId") $cliTok $null).Body
    Assert 'D18.retomada_paused_false' ((JGet $d2sol 'paused') -eq 'False') ('paused=' + (JGet $d2sol 'paused'))

    # ---------- D19: isolamento entre organizacoes ----------
    Write-Host '--- D19 isolamento ---'
    $r = Invoke-Api 'GET' ($api + "/api/v1/organizations/$D1/studio/drafts/$sDraft") $cliTok $null
    Assert 'D19.minuta_D2_rota_D1_404' ($r.Code -eq 404) ('code=' + $r.Code)
    $r = Invoke-Api 'GET' ($api + "/api/v1/organizations/$D2/studio/drafts/$hDraft") $cliTok $null
    Assert 'D19.minuta_D1_rota_D2_404' ($r.Code -eq 404) ('code=' + $r.Code)
    $r = Invoke-Api 'GET' ($api + "/api/v1/organizations/$D1/renewals/$propId") $cliTok $null
    Assert 'D19.renovacao_D1_rota_D1_200' ($r.Code -eq 200) ('code=' + $r.Code)
    $r = Invoke-Api 'GET' ($api + "/api/v1/organizations/$D2/renewals/$propId") $cliTok $null
    Assert 'D19.renovacao_D1_rota_D2_404' ($r.Code -eq 404) ('code=' + $r.Code)
    $r = Invoke-Api 'GET' ($api + "/api/v1/organizations/$D1/solicitations/$solId") $cliTok $null
    Assert 'D19.solicitacao_D2_rota_D1_404' ($r.Code -eq 404) ('code=' + $r.Code)

    # ---------- D20: identidade visual SPEC §8 + contraste WCAG ----------
    Write-Host '--- D20 tokens e contraste ---'
    $css = [IO.File]::ReadAllText($cssPath, [Text.Encoding]::UTF8)
    function Tok([string]$name) {
        $m = [regex]::Match($css, ('--' + $name + '\s*:\s*(#[0-9a-fA-F]{6})'))
        if (-not $m.Success) { return '' }
        return $m.Groups[1].Value
    }
    function Chan([string]$hex, [int]$i) {
        $v = [convert]::ToInt32($hex.Substring($i, 2), 16) / 255.0
        if ($v -le 0.04045) { return $v / 12.92 }
        return [Math]::Pow((($v + 0.055) / 1.055), 2.4)
    }
    function Lum([string]$hex) { return (0.2126 * (Chan $hex 1)) + (0.7152 * (Chan $hex 3)) + (0.0722 * (Chan $hex 5)) }
    function Ratio([string]$a, [string]$b) {
        $la = Lum $a; $lb = Lum $b
        $hi = [Math]::Max($la, $lb); $lo = [Math]::Min($la, $lb)
        return (($hi + 0.05) / ($lo + 0.05))
    }
    $tNavy900 = Tok 'navy-900'; $tBg = Tok 'background'; $tText = Tok 'text'; $tAction = Tok 'action-600'
    $tField = Tok 'field'; $tFocus = Tok 'focus-ring'; $tOnNavy = Tok 'on-navy'; $tNavy950 = Tok 'navy-950'
    Assert 'D20.paleta_SPEC_presente' ($tNavy900 -eq '#102a43' -and $tBg -eq '#f4f7fb' -and $tAction -eq '#245bdb' -and $tText -eq '#243746' -and $tField -eq '#fff3bf') "navy=$tNavy900 bg=$tBg action=$tAction text=$tText field=$tField"
    $rTextBg = Ratio $tText $tBg
    Assert 'D20.contraste_texto_sobre_fundo_45' ($rTextBg -ge 4.5) ('ratio=' + [Math]::Round($rTextBg, 2))
    $rTextField = Ratio $tText $tField
    Assert 'D20.contraste_texto_sobre_campo_45' ($rTextField -ge 4.5) ('ratio=' + [Math]::Round($rTextField, 2))
    $rActionBg = Ratio $tAction $tBg
    Assert 'D20.contraste_acao_sobre_fundo_45' ($rActionBg -ge 4.5) ('ratio=' + [Math]::Round($rActionBg, 2))
    $rOnNavy = Ratio $tOnNavy $tNavy900
    Assert 'D20.contraste_texto_sobre_navy_45' ($rOnNavy -ge 4.5) ('ratio=' + [Math]::Round($rOnNavy, 2))
    $rFocusW = Ratio $tFocus '#ffffff'
    Assert 'D20.foco_visivel_sobre_branco_30' ($rFocusW -ge 3.0) ('ratio=' + [Math]::Round($rFocusW, 2))
    $rFocusN = Ratio $tFocus $tNavy950
    Assert 'D20.foco_visivel_sobre_navy_30' ($rFocusW -ge 3.0 -and $rFocusN -ge 3.0) ('w=' + [Math]::Round($rFocusW, 2) + ' n=' + [Math]::Round($rFocusN, 2))
    $cssServed = Invoke-Api 'GET' ($web + '/css/site.css') $null $null
    Assert 'D20.site_css_servida_com_tokens' ($cssServed.Code -eq 200 -and $cssServed.Body.Contains('--focus-ring')) ('code=' + $cssServed.Code)
    $loginHtml = $cliWeb.Html
    $lg2 = New-CookieJar
    $loginAnon = Invoke-Page 'GET' ($web + '/entrar') '' $lg2
    Assert 'D20.login_usa_site_css' ($loginAnon.Contains('/css/site.css')) ('len=' + $loginAnon.Length)
    $idxHtml = Invoke-Page 'GET' ($web + "/organizacoes/$D1/contratos") '' $cliWeb.Jar
    Assert 'D20.tabelas_responsivas_sem_bootstrap' ($idxHtml.Contains('responsive-table') -and -not $idxHtml.Contains('table-responsive')) ('len=' + $idxHtml.Length)

# ---------- Cleanup ----------
} catch {
    $script:unhandled = $_.Exception.Message
    Assert 'D?.execucao_sem_excecao' $false $script:unhandled
} finally {
    if (-not $env:ODCA_D_KEEP) {
        Write-Host '--- Cleanup fixtures Val D ---'
        Clean-Tenant $D1
        Clean-Tenant $D2
        SqlOpt "UPDATE odca.subscriptions SET plan_version_id='$($script:planBefore)' WHERE tenant_id='$TC';" | Out-Null
        SqlOpt "DELETE FROM odca.template_approvals WHERE tenant_id='$TC' AND official_key='surgical-consent';" | Out-Null
        SqlOpt @"
DELETE FROM odca.member_roles WHERE user_id='$QA_OP';
DELETE FROM odca.memberships WHERE user_id='$QA_OP';
DELETE FROM odca.sessions WHERE user_id='$QA_OP';
DELETE FROM odca.mfa_recovery_codes WHERE user_id='$QA_OP';
DELETE FROM odca.resource_movements WHERE actor_user_id='$QA_OP';
DELETE FROM odca.audit_events WHERE actor_user_id='$QA_OP';
UPDATE odca.users SET deleted_by=NULL WHERE id='$QA_OP';
DELETE FROM odca.users WHERE id='$QA_OP';
"@ | Out-Null
        $left = SqlOpt "SELECT count(*) FROM odca.tenants WHERE display_name LIKE 'Val D:%';"
        if ($left -ne '') { Assert 'D99.fixtures_removidas' ([int]$left -eq 0) ('left=' + $left) }
        if ($script:planBefore) {
            $tcPv = SqlOpt "SELECT plan_version_id FROM odca.subscriptions s WHERE s.tenant_id='$TC' AND s.status='active';"
            if ($tcPv -ne '') { Assert 'D99.plano_tc_restaurado' ($tcPv -eq $script:planBefore) ('plan=' + $tcPv) }
        }
    } else {
        Write-Host '--- Cleanup SUPRIMIDO (ODCA_D_KEEP=1): fixtures Val D: permanecem para capturas de navegador ---'
    }
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
Write-Host ('RESULTADO validate-d-web: ' + $script:passes + ' PASS / ' + $script:failures + ' FAIL')
if ($script:failures -gt 0) { exit 1 }
exit 0
