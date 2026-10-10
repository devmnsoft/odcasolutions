$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }

$web = 'https://localhost:7144'
$api = 'https://localhost:7143'
$tenant = '20000000-0000-4000-8000-000000000001'
$psql = 'C:\Program Files\PostgreSQL\18\bin\psql.exe'
$db = 'odca_test_disposable'
$tmpRoot = 'C:\Users\NCELL-DEV-020\AppData\Local\Temp\opencode'

$script:failures = 0
$script:passes = 0
$script:lastUrl = ''
$script:tok = $null

function Assert([string]$name, [bool]$cond, [string]$detail) {
    if ($cond) { $script:passes++; Write-Host ('PASS  ' + $name) }
    else {
        Write-Host ('FAIL  ' + $name)
        if ($detail) { Write-Host ('      observed: ' + $detail) }
        $script:failures++
    }
}

function De([string]$h) {
    if (-not $h) { return '' }
    $s = [regex]::Replace($h, '&#[xX]([0-9a-fA-F]+);', { param($m) De-Cp ([Convert]::ToInt32($m.Groups[1].Value, 16)) })
    $s = [regex]::Replace($s, '&#(\d+);', { param($m) De-Cp ([int]$m.Groups[1].Value) })
    return ($s.Replace('&amp;', '&').Replace('&lt;', '<').Replace('&gt;', '>').Replace('&quot;', '"').Replace('&apos;', "'"))
}

# Codepoints acima de U+FFFF (emojis astrais usados como ícones de menu) exigem par de surogados.
function De-Cp([int]$cp) {
    if ($cp -le 0xFFFF) { return ([string][char]$cp) }
    $x = $cp - 0x10000
    return ([string][char](0xD800 + ($x -shr 10)) + [char](0xDC00 + ($x -band 0x3FF)))
}

# Decodes \uXXXX escapes emitted by System.Text.Json's default encoder so that
# accented substrings can be asserted against raw response bodies.
function Dec([string]$s) {
    if ([string]::IsNullOrEmpty($s)) { return '' }
    return [regex]::Replace($s, '\\u([0-9a-fA-F]{4})', { param($m) [string][char][Convert]::ToInt32($m.Groups[1].Value, 16) })
}

function Esc([string]$s) { return [Uri]::EscapeDataString($s) }

function Snip([string]$s) {
    if (-not $s) { return '' }
    $d = Dec $s
    return $d.Substring(0, [Math]::Min(420, $d.Length))
}

function Sql([string]$sql) {
    $tmp = Join-Path $env:TEMP ('odca-sql-' + [guid]::NewGuid().ToString('N') + '.sql')
    [IO.File]::WriteAllText($tmp, $sql, (New-Object System.Text.UTF8Encoding($false)))
    $prev = $env:PGPASSWORD; $env:PGPASSWORD = '123456'
    $out = & $psql -U postgres -d $db -P pager=off -t -A -q -f $tmp 2>&1
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

function Get-Org {
    $r = Invoke-Api 'GET' ("$api/api/v1/organizations/$tenant") $script:tok $null
    if ($r.Code -ne 200) { throw ('GET organizacao falhou: ' + $r.Code + ' ' + (Snip $r.Body)) }
    return ($r.Body | ConvertFrom-Json)
}

function Put-Org($org, [string]$profile, $version) {
    $body = ConvertTo-Json -InputObject ([pscustomobject]@{
        name = $org.name; timezone = $org.timezone; version = [int64]$version; activityProfile = $profile
    }) -Depth 5
    return Invoke-Api 'PUT' ("$api/api/v1/organizations/$tenant") $script:tok $body
}

function Get-Draft([string]$draftId) {
    $r = Invoke-Api 'GET' ("$api/api/v1/organizations/$tenant/studio/drafts/$draftId") $script:tok $null
    if ($r.Code -ne 200) { throw ('GET minuta falhou: ' + $r.Code + ' ' + (Snip $r.Body)) }
    $d = $r.Body | ConvertFrom-Json
    $list = New-Object System.Collections.ArrayList
    foreach ($v in @($d.values)) { if ($null -ne $v) { [void]$list.Add($v) } }
    $d.values = $list
    return $d
}

function Save-Draft([string]$draftId, $d) {
    $payload = [pscustomobject]@{
        content = $d.content
        fields = $d.fields
        values = @($d.values)
        expectedVersion = [int64]$d.version
        clientRevision = [guid]::NewGuid().ToString()
    }
    $body = ConvertTo-Json -InputObject $payload -Depth 40
    return Invoke-Api 'PUT' ("$api/api/v1/organizations/$tenant/studio/drafts/$draftId") $script:tok $body
}

function New-Draft([string]$templateId, [string]$title) {
    $body = ConvertTo-Json -InputObject ([pscustomobject]@{
        templateId = $templateId; title = $title; reference = $null
        patientId = $null; contractId = $null; changeRequestId = $null
        purpose = $null; contractorSource = $null
    }) -Depth 5
    return Invoke-Api 'POST' ("$api/api/v1/organizations/$tenant/studio/drafts") $script:tok $body
}

function Get-Val($vals, [string]$id) {
    foreach ($v in $vals) { if ($v.fieldId -eq $id) { return $v } }
    return $null
}

function Set-Val($vals, [string]$id, [string]$value) {
    $e = Get-Val $vals $id
    if ($null -eq $e) {
        $vals.Add([pscustomobject]@{ fieldId = $id; value = $value; confirmed = $true; source = 'manual' }) | Out-Null
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

function Text-Of($node) {
    $acc = New-Object System.Collections.Generic.List[string]
    $stack = New-Object System.Collections.Stack
    $stack.Push($node)
    while ($stack.Count -gt 0) {
        $cur = $stack.Pop()
        if ($null -eq $cur) { continue }
        if ($cur -is [System.Array]) { foreach ($x in $cur) { $stack.Push($x) }; continue }
        if ($cur -is [string]) { $acc.Add($cur); continue }
        $txt = $null
        try { $txt = $cur.PSObject.Properties['text'] } catch { $txt = $null }
        if ($txt -and ($txt.Value -is [string])) { $acc.Add([string]$txt.Value) }
        foreach ($p in $cur.PSObject.Properties) {
            if ($p.Name -eq 'text') { continue }
            $v = $p.Value
            if ($v -is [System.Array] -or $v -is [pscustomobject]) { $stack.Push($v) }
        }
    }
    return ($acc -join ' ')
}

Write-Host '=== B.3.4 CAMPOS POR PERFIL DE ATUACAO (WEB + API) ==='

# ---------- T0: gate, limpeza e baseline ----------
$hGate = Invoke-Page 'GET' ($web + '/entrar') '' (New-CookieJar)
Assert 'T0 web /entrar acessivel c/ token' $hGate.Contains('__RequestVerificationToken') $script:lastUrl

$rh = Invoke-Api 'GET' ("$api/health/ready") $null $null
Assert 'T0 api health ready=200' ($rh.Code -eq 200) (Snip $rh.Body)

Write-Host '--- baseline: limpeza de execucoes anteriores (best effort) ---'
$clean = @"
DELETE FROM odca.signature_participants WHERE preparation_id IN (SELECT p.id FROM odca.signature_preparations p JOIN odca.generated_contract_versions v ON v.tenant_id=p.tenant_id AND v.id=p.generated_version_id JOIN odca.contracts c ON c.id=v.contract_id WHERE c.title LIKE 'B34 %');
DELETE FROM odca.signature_preparation_operations WHERE preparation_id IN (SELECT p.id FROM odca.signature_preparations p JOIN odca.generated_contract_versions v ON v.tenant_id=p.tenant_id AND v.id=p.generated_version_id JOIN odca.contracts c ON c.id=v.contract_id WHERE c.title LIKE 'B34 %');
DELETE FROM odca.signature_preparation_events WHERE preparation_id IN (SELECT p.id FROM odca.signature_preparations p JOIN odca.generated_contract_versions v ON v.tenant_id=p.tenant_id AND v.id=p.generated_version_id JOIN odca.contracts c ON c.id=v.contract_id WHERE c.title LIKE 'B34 %');
DELETE FROM odca.signature_preparations p USING odca.generated_contract_versions v, odca.contracts c WHERE p.tenant_id=v.tenant_id AND p.generated_version_id=v.id AND v.contract_id=c.id AND c.title LIKE 'B34 %';
DELETE FROM odca.generated_contract_versions v USING odca.contracts c WHERE v.contract_id=c.id AND c.title LIKE 'B34 %';
DELETE FROM odca.draft_save_receipts r USING odca.contract_drafts d, odca.contracts c WHERE r.draft_id=d.id AND d.contract_id=c.id AND c.title LIKE 'B34 %';
DELETE FROM odca.contract_drafts d USING odca.contracts c WHERE d.contract_id=c.id AND c.title LIKE 'B34 %';
DELETE FROM odca.contract_events e USING odca.contracts c WHERE e.contract_id=c.id AND c.title LIKE 'B34 %';
DELETE FROM odca.contracts WHERE title LIKE 'B34 %';
"@
$null = SqlOpt $clean
$bp = Sql "UPDATE odca.tenants SET activity_profile='general' WHERE id='$tenant'; SELECT activity_profile FROM odca.tenants WHERE id='$tenant';"
Assert 'T0 perfil baseline=general' ($bp -eq 'general') $bp

Write-Host '--- login API (cliente.teste, 1x por causa do rate limit) ---'
$loginBody = '{"login":"cliente.teste@odca.local","password":"K8@wR3!nF6#zP2$m"}'
$rl = Invoke-Api 'POST' ("$api/api/v1/auth/login") $null $loginBody
Assert 'T0 api login=200' ($rl.Code -eq 200) (Snip $rl.Body)
if ($rl.Code -eq 200) { $script:tok = ($rl.Body | ConvertFrom-Json).accessToken }
Assert 'T0 accessToken recebido' ([bool]$script:tok) ''

$org = Get-Org
Assert 'T0 GET organizacao activityProfile=general' ($org.activityProfile -eq 'general') ([string]$org.activityProfile)
Assert 'T0 GET organizacao versao>0' ([int64]$org.version -gt 0) ([string]$org.version)

# ---------- T1: regras de perfil na API (D1) ----------
Write-Host '--- T1 regras de perfil (API) ---'
$org = Get-Org
$r = Put-Org $org 'voador' $org.version
$rb = $r.Body | ConvertFrom-Json
Assert 'T1.1 perfil invalido -> 400' ($r.Code -eq 400) (Snip $r.Body)
Assert 'T1.1 erro em activityProfile' ((Dec $r.Body).Contains('Perfil de atuação inválido') -or ($null -ne $rb.errors.activityProfile)) (Snip $r.Body)

$org = Get-Org
$v0 = $org.version
$r = Put-Org $org 'therapy_clinic' $org.version
Assert 'T1.2 therapy_clinic -> 204' ($r.Code -eq 204) (Snip $r.Body)
$org = Get-Org
Assert 'T1.2 persistiu therapy_clinic' ($org.activityProfile -eq 'therapy_clinic') ([string]$org.activityProfile)
$v1 = $org.version

$r = Put-Org $org 'plastic_surgery' $v1
$rb = $r.Body | ConvertFrom-Json
Assert 'T1.3 plastic_surgery sem Enterprise -> 409' ($r.Code -eq 409) (Snip $r.Body)
Assert 'T1.3 code=organization.profile.plan_required' ($rb.code -eq 'organization.profile.plan_required') (Snip $r.Body)
$org = Get-Org
Assert 'T1.3 gate nao alterou o perfil' ($org.activityProfile -eq 'therapy_clinic') ([string]$org.activityProfile)
Assert 'T1.3 gate nao consumiu versao' ([string]$org.version -eq [string]$v1) ('v=' + $org.version + ' esperado=' + $v1)

$r = Put-Org $org 'general' $v0   # versao obsoleta: v0 foi consumida com sucesso no T1.2
$rb = $r.Body | ConvertFrom-Json
Assert 'T1.4 versao obsoleta -> 409 de versao' ($r.Code -eq 409) (Snip $r.Body)
Assert 'T1.4 sem code de plano' ($rb.code -ne 'organization.profile.plan_required') (Snip $r.Body)
Assert 'T1.4 mensagem de outra sessao' ((Dec $r.Body).Contains('outra sessao') -or (Dec $r.Body).Contains('outra sess')) (Snip $r.Body)

$org = Get-Org
$r = Put-Org $org 'general' $org.version
Assert 'T1.5 general valido -> 204' ($r.Code -eq 204) (Snip $r.Body)
$org = Get-Org
Assert 'T1.5 perfil general restaurado' ($org.activityProfile -eq 'general') ([string]$org.activityProfile)

# ---------- T2: pagina Web de organizacao + gate Web (D1) ----------
Write-Host '--- T2 tela Web de perfil ---'
$cli = Login-Web 'cliente.teste@odca.local' 'K8@wR3!nF6#zP2$m'
$hEdit = Invoke-Page 'GET' ("$web/organizacoes/$tenant/editar") '' $cli.Jar
Assert 'T2.1 label Perfil de atuação' $hEdit.Contains('Perfil de atuação') $script:lastUrl
Assert 'T2.1 opcao general selecionada' ([regex]::IsMatch($hEdit, 'value="general"\s+selected')) ''
Assert 'T2.1 opcao therapy_clinic' $hEdit.Contains('value="therapy_clinic"') ''
Assert 'T2.1 opcao plastic_surgery (Enterprise)' $hEdit.Contains('value="plastic_surgery"') ''

# Bloco B: registro central de navegacao (D-OC4) — itens novos com as rotas corretas.
$hHome = Invoke-Page 'GET' "$web/" '' $cli.Jar
Assert 'T2.9 menu_exibe_arquivados' ($hHome.Contains('Arquivados') -and $hHome.Contains('status=arquivados')) $script:lastUrl
Assert 'T2.9 menu_exibe_suporte_tecnico' ($hHome.Contains('Suporte técnico') -and $hHome.Contains('service=suporte_tecnico')) $script:lastUrl
Assert 'T2.9 menu_exibe_minha_conta' ($hHome.Contains('Minha conta') -and $hHome.Contains('/minha-conta')) $script:lastUrl
$hConta = Invoke-Page 'GET' "$web/minha-conta" '' $cli.Jar
Assert 'T2.9 pagina_minha_conta_renderiza' ($hConta.Contains('Segurança da conta')) $script:lastUrl
Assert 'T2.9 menu_sem_publicacao_versoes' (-not $hHome.Contains('Publicação e versões')) ''

$org = Get-Org
$vS = [string]$org.version
$sS = [string]$org.status
$nS = [string]$org.name
$tzS = [string]$org.timezone
$t2Tok = Get-Token $hEdit
$form = 'TenantId=' + (Esc $tenant) + '&Version=' + (Esc $vS) + '&Status=' + (Esc $sS) +
    '&Name=' + (Esc $nS) + '&Timezone=' + (Esc $tzS) +
    '&ActivityProfile=' + (Esc 'plastic_surgery') + '&__RequestVerificationToken=' + (Esc $t2Tok)
$hDenied = Invoke-Page 'POST' ("$web/organizacoes/$tenant/editar") $form $cli.Jar
Assert 'T2.2 Web gate mostra erro Enterprise' $hDenied.Contains('exige plano Enterprise') ('last=' + $script:lastUrl)
$bp = Sql "SELECT activity_profile FROM odca.tenants WHERE id='$tenant';"
Assert 'T2.2 perfil inalterado no banco' ($bp -eq 'general') $bp

$t2Tok2 = Get-Token $hDenied
$form = 'TenantId=' + (Esc $tenant) + '&Version=' + (Esc $vS) + '&Status=' + (Esc $sS) +
    '&Name=' + (Esc $nS) + '&Timezone=' + (Esc $tzS) +
    '&ActivityProfile=' + (Esc 'therapy_clinic') + '&__RequestVerificationToken=' + (Esc $t2Tok2)
$hSaved = Invoke-Page 'POST' ("$web/organizacoes/$tenant/editar") $form $cli.Jar
Assert 'T2.3 Web salvar com sucesso (toast)' $hSaved.Contains('Organização atualizada.') ('last=' + $script:lastUrl)
$bp = Sql "SELECT activity_profile FROM odca.tenants WHERE id='$tenant';"
Assert 'T2.3 perfil therapy_clinic via Web' ($bp -eq 'therapy_clinic') $bp

# ---------- T3: regras de cliente no studio.js ----------
Write-Host '--- T3 studio.js (cliente) ---'
$js = Invoke-Page 'GET' ($web + '/js/studio.js') '' (New-CookieJar)
Assert 'T3 tipo Formula no mapa' $js.Contains('"Formula"') ''
Assert 'T3 validador de documento BR' ($js.Contains('brDocumentValid') -and $js.Contains('maskBrDocument')) ''
Assert 'T3 recalculo da formula' ($js.Contains('recomputeFormulas') -and $js.Contains('formulaTotal')) ''
Assert 'T3 rejeicao de minuta invalida' ($js.Contains('rejectInvalidDrafts') -and $js.Contains('BrazilianDocument')) ''

# ---------- T4: biblioteca oficial (inclui modelo cirurgico) ----------
Write-Host '--- T4 instalacao da biblioteca oficial ---'
$r = Invoke-Api 'POST' ("$api/api/v1/organizations/$tenant/studio/templates/official") $script:tok ''
$inst = $r.Body | ConvertFrom-Json
Assert 'T4.1 install=200' ($r.Code -eq 200) (Snip $r.Body)
Assert 'T4.1 total de modelos oficiais=9' (($inst.installed + $inst.alreadyPresent) -eq 9) ('installed=' + $inst.installed + ' present=' + $inst.alreadyPresent)

$ids = Sql @"
SELECT official_key || '|' || id || '|' || status FROM odca.contract_templates
 WHERE owner_tenant_id='$tenant' AND official_key IN ('multiple-therapies','surgical-consent') AND status='published';
"@
$mtId = $null
$sgId = $null
foreach ($line in ($ids -split "`n")) {
    $p = $line.Trim() -split '\|'
    if ($p.Count -eq 3 -and $p[0] -eq 'multiple-therapies') { $mtId = $p[1] }
    if ($p.Count -eq 3 -and $p[0] -eq 'surgical-consent') { $sgId = $p[1] }
}
Assert 'T4.2 multiple-therapies instalado' ([bool]$mtId) $ids
Assert 'T4.2 surgical-consent instalado' ([bool]$sgId) $ids

$hStudio = Invoke-Page 'GET' ("$web/organizacoes/$tenant/estudio") '' $cli.Jar
Assert 'T4.3 catalogo mostra modelo cirurgico' $hStudio.Contains('Termo de Consentimento para Procedimento Cirúrgico') $script:lastUrl
Assert 'T4.3 descricao cita aprovacao ODCA' $hStudio.Contains('aprovação da ODCA') ''

# ---------- T4.4: dedup do catalogo global (D-OC1) ----------
Write-Host '--- T4.4 dedup catalogo global ---'
$globCount = Sql "SELECT count(*) FROM odca.contract_templates WHERE owner_tenant_id IS NULL AND scope='global' AND official_key IS NOT NULL AND status<>'archived';"
Assert 'T4.4.1 linhas_globais_publicadas=9' ([int]$globCount -eq 9) ("global=" + $globCount)
$pubVis = Sql @"
SELECT count(*) FROM odca.contract_templates t
 WHERE t.status='published' AND t.official_key IS NOT NULL
   AND (t.scope='global' OR t.owner_tenant_id='$tenant')
   AND (t.scope<>'global' OR NOT EXISTS(
        SELECT 1 FROM odca.contract_templates o
         WHERE o.official_key=t.official_key AND o.owner_tenant_id='$tenant' AND o.status<>'archived'));
"@
Assert 'T4.4 catalogo_sem_duplicata_dedup_global' ([int]$pubVis -eq 9) ("publicados_visiveis=" + $pubVis + " globais=" + $globCount)

# ---------- T5: gate do modelo cirurgico + estado pendente (D3) ----------
Write-Host '--- T5 gate cirurgico ---'
$r = New-Draft $sgId 'B34 Termo Cirurgico'
$rb = $r.Body | ConvertFrom-Json
Assert 'T5.1 draft cirurgico fora do perfil -> 409' ($r.Code -eq 409) (Snip $r.Body)
Assert 'T5.1 code=draft.profile.surgical_required' ($rb.code -eq 'draft.profile.surgical_required') (Snip $r.Body)

$null = Sql "UPDATE odca.tenants SET activity_profile='plastic_surgery' WHERE id='$tenant';"  # fixture: simulando tenant Enterprise aprovado
$r = New-Draft $sgId 'B34 Termo Cirurgico'
Assert 'T5.3 draft cirurgico no perfil -> 201' ($r.Code -eq 201) (Snip $r.Body)
$sDraft = ''
if ($r.Code -eq 201) { $sDraft = ($r.Body | ConvertFrom-Json).id }
Assert 'T5.3 id da minuta cirurgica' ([bool]$sDraft) (Snip $r.Body)

if ($sDraft) {
    $sd = Get-Draft $sDraft
    Assert 'T5.4 requiresOdcaApproval=true' ($sd.requiresOdcaApproval -eq $true) ([string]$sd.requiresOdcaApproval)
    $fSurgeon = @($sd.fields) | Where-Object { $_.Id -eq 'surgeon_document' } | Select-Object -First 1
    $fSurgeryDate = @($sd.fields) | Where-Object { $_.Id -eq 'surgery_date' } | Select-Object -First 1
    $fFee = @($sd.fields) | Where-Object { $_.Id -eq 'procedure_fee' } | Select-Object -First 1
    $fRep = @($sd.fields) | Where-Object { $_.Id -eq 'legal_representative_info' } | Select-Object -First 1
    $fHasRep = @($sd.fields) | Where-Object { $_.Id -eq 'has_representative' } | Select-Object -First 1
    Assert 'T5.4 surgeon_document e BrazilianDocument' (($null -ne $fSurgeon) -and ($fSurgeon.Type -eq 5 -or $fSurgeon.Type -eq 'BrazilianDocument')) ''
    Assert 'T5.4 surgery_date e Date' (($null -ne $fSurgeryDate) -and ($fSurgeryDate.Type -eq 2 -or $fSurgeryDate.Type -eq 'Date')) ''
    Assert 'T5.4 procedure_fee e Currency' (($null -ne $fFee) -and ($fFee.Type -eq 4 -or $fFee.Type -eq 'Currency')) ''
    Assert 'T5.4 responsavel legal condicional' (($null -ne $fRep) -and ($fRep.RequiredWhenFieldId -eq 'has_representative') -and (@($fRep.RequiredWhenAnyOf) -contains 'Sim') -and ($fRep.Origin -eq 'representative')) ''
    Assert 'T5.4 has_representative Choice Sim/Nao' (($null -ne $fHasRep) -and (@($fHasRep.Choices) -contains 'Sim') -and (@($fHasRep.Choices) -contains 'Nao' -or @($fHasRep.Choices) -contains 'Não')) ''

    $hDraft = Invoke-Page 'GET' ("$web/organizacoes/$tenant/estudio/minutas/$sDraft") '' $cli.Jar
    Assert 'T5.5 banner pendente de aprovacao ODCA na minuta' ($hDraft.Contains('data-pending-odca-approval') -and $hDraft.Contains('pendente de aprovação ODCA')) $script:lastUrl
}

# ---------- T6: validacao de documentos/datas/valores no servidor ----------
Write-Host '--- T6 validacao servidor (cirurgico) ---'
if ($sDraft) {
    $sd = Get-Draft $sDraft
    Set-Val $sd.values 'surgeon_document' '52998224720'   # digito verificador invalido
    $r = Save-Draft $sDraft $sd
    Assert 'T6.1 CPF invalido -> 400' ($r.Code -eq 400) (Snip $r.Body)
    Assert 'T6.1 aponta Documento do cirurgiao' ((Dec $r.Body).Contains('Documento do cirurgiao') -or (Dec $r.Body).Contains('Documento do cirurgião')) (Snip $r.Body)
    Assert 'T6.1 mensagem de valor invalido' ((Dec $r.Body).Contains('invalido') -or (Dec $r.Body).Contains('inválido')) (Snip $r.Body)

    $sd = Get-Draft $sDraft
    Set-Val $sd.values 'surgeon_document' '11144477735'   # CPF valido
    $r = Save-Draft $sDraft $sd
    Assert 'T6.2 CPF valido -> 200' ($r.Code -eq 200) (Snip $r.Body)

    $sd = Get-Draft $sDraft
    Set-Val $sd.values 'surgery_date' '2026-13-45'        # data impossivel
    $r = Save-Draft $sDraft $sd
    Assert 'T6.3 data invalida -> 400' ($r.Code -eq 400) (Snip $r.Body)
    Assert 'T6.3 aponta Data do procedimento' ((Dec $r.Body).Contains('Data do procedimento')) (Snip $r.Body)

    $sd = Get-Draft $sDraft
    Set-Val $sd.values 'surgery_date' '2027-03-15'
    Set-Val $sd.values 'procedure_fee' '1.234,56'          # formato pt-BR canonizado no servidor
    $r = Save-Draft $sDraft $sd
    Assert 'T6.4 data valida + moeda pt-BR -> 200' ($r.Code -eq 200) (Snip $r.Body)
    $sd = Get-Draft $sDraft
    $fee = Get-Val $sd.values 'procedure_fee'
    Assert 'T6.4 moeda canonizada 1234.56' (($null -ne $fee) -and ($fee.value -eq '1234.56')) $(if ($fee) { $fee.value } else { 'ausente' })

    $sd = Get-Draft $sDraft
    Set-Val $sd.values 'procedure_fee' 'abc'
    $r = Save-Draft $sDraft $sd
    Assert 'T6.5 moeda com texto -> 400' ($r.Code -eq 400) (Snip $r.Body)
    Assert 'T6.5 aponta Honorarios' ((Dec $r.Body).Contains('Honorarios') -or (Dec $r.Body).Contains('Honorários')) (Snip $r.Body)
}

# ---------- T7: overlay por perfil + mensalidade em formula explicita (D1/D2) ----------
Write-Host '--- T7 overlay therapy_clinic + formula ---'
$org = Get-Org
$r = Put-Org $org 'therapy_clinic' $org.version
Assert 'T7.0 volta para therapy_clinic -> 204' ($r.Code -eq 204) (Snip $r.Body)

$r = New-Draft $mtId 'B34 Multiplas Terapias'
Assert 'T7.1 draft terapias -> 201' ($r.Code -eq 201) (Snip $r.Body)
$tDraft = ''
if ($r.Code -eq 201) { $tDraft = ($r.Body | ConvertFrom-Json).id }

if ($tDraft) {
    $td = Get-Draft $tDraft
    $sessions = @(@($td.fields) | Where-Object { $_.Id -like 'sessions_*' })
    $formula = @($td.fields) | Where-Object { $_.Id -eq 'monthly_fee_total' } | Select-Object -First 1
    Assert 'T7.2 quatro campos de sessoes' (@($sessions).Count -eq 4) ('count=' + @($sessions).Count)
    $sPsy = @($sessions) | Where-Object { $_.Id -eq 'sessions_psychology' } | Select-Object -First 1
    Assert 'T7.2 sessions_psychology Number obrigatorio condicional' (($null -ne $sPsy) -and ($sPsy.Type -eq 3 -or $sPsy.Type -eq 'Number') -and ($sPsy.Required -eq $true) -and ($sPsy.RequiredWhenFieldId -eq 'therapy_psychology') -and (@($sPsy.RequiredWhenAnyOf) -contains 'Sim')) ''
    Assert 'T7.2 monthly_fee_total e Formula' (($null -ne $formula) -and ($formula.Type -eq 7 -or $formula.Type -eq 'Formula')) ''
    Assert 'T7.2 formula opcional (nao obrigatoria)' (($null -ne $formula) -and ($formula.Required -eq $false)) ''
    Assert 'T7.2 formula com 4 termos' (($null -ne $formula) -and (@($formula.FormulaTerms).Count -eq 4)) ''
    $docText = Text-Of $td.content
    Assert 'T7.2 clausula de sessoes inserida' ($docText.Contains('CLÁUSULA 2ª-A') -or $docText.Contains('CLAUSULA 2-A')) ($docText.Substring(0, [Math]::Min(260, $docText.Length)))
    Assert 'T7.2 clausula de mensalidade total' $docText.Contains('MENSALIDADE TOTAL') ''
    $mtot = Get-Val $td.values 'monthly_fee_total'
    Assert 'T7.2 total semeado=0.00 na criacao' (($null -ne $mtot) -and ($mtot.value -eq '0.00')) $(if ($mtot) { $mtot.value } else { 'ausente' })
    Assert 'T7.2 minuta comum nao exige aprovacao ODCA' ($td.requiresOdcaApproval -eq $false) ([string]$td.requiresOdcaApproval)

    # T7.2i autofill conferivel: contratada preenchida pela organizacao com origem automatica
    $disp = Sql "SELECT display_name FROM odca.tenants WHERE id='$tenant';"
    $ctName = Get-Val $td.values 'contracted_name'
    Assert 'T7.2i autofill contratada pela organizacao' (($null -ne $ctName) -and ($ctName.value -eq $disp) -and ($ctName.source -eq 'organization')) $(if ($ctName) { ($ctName.value + '/' + $ctName.source) } else { 'ausente' })

    # T7.3 total divergente da formula -> rejeitado
    $td = Get-Draft $tDraft
    Set-Val $td.values 'therapy_psychology' 'Sim'
    Set-Val $td.values 'fee_psychology' '200.00'
    Set-Val $td.values 'sessions_psychology' '4'
    Set-Val $td.values 'monthly_fee_total' '999.00'
    $r = Save-Draft $tDraft $td
    Assert 'T7.3 total divergente -> 400' ($r.Code -eq 400) (Snip $r.Body)
    Assert 'T7.3 mensagem nao confere com a formula' ((Dec $r.Body).Contains('nao confere com a formula') -or (Dec $r.Body).Contains('não confere com a fórmula')) (Snip $r.Body)
    Assert 'T7.3 informa o esperado 800.00' ((Dec $r.Body).Contains('esperado: 800.00')) (Snip $r.Body)

    # T7.4 total vazio -> servidor preenche (autofill do calculo)
    $td = Get-Draft $tDraft
    Set-Val $td.values 'therapy_psychology' 'Sim'
    Set-Val $td.values 'fee_psychology' '200.00'
    Set-Val $td.values 'sessions_psychology' '4'
    Set-Val $td.values 'monthly_fee_total' ''
    $r = Save-Draft $tDraft $td
    Assert 'T7.4 total vazio -> 200' ($r.Code -eq 200) (Snip $r.Body)
    $td = Get-Draft $tDraft
    $mtot = Get-Val $td.values 'monthly_fee_total'
    Assert 'T7.4 servidor preencheu 800.00' (($null -ne $mtot) -and ($mtot.value -eq '800.00')) $(if ($mtot) { $mtot.value } else { 'ausente' })

    # T7.5 total correto -> aceito
    $td = Get-Draft $tDraft
    Set-Val $td.values 'therapy_psychology' 'Sim'
    Set-Val $td.values 'fee_psychology' '200.00'
    Set-Val $td.values 'sessions_psychology' '4'
    Set-Val $td.values 'monthly_fee_total' '800.00'
    $r = Save-Draft $tDraft $td
    Assert 'T7.5 total correto -> 200' ($r.Code -eq 200) (Snip $r.Body)

    # T7.6 dependencia muda com total antigo -> rejeitado
    $td = Get-Draft $tDraft
    Set-Val $td.values 'sessions_psychology' '5'
    Set-Val $td.values 'monthly_fee_total' '800.00'
    $r = Save-Draft $tDraft $td
    Assert 'T7.6 total obsoleto -> 400' ($r.Code -eq 400) (Snip $r.Body)
    Assert 'T7.6 informa o esperado 1000.00' ((Dec $r.Body).Contains('esperado: 1000.00')) (Snip $r.Body)

    # T7.7 total recalculado -> aceito
    $td = Get-Draft $tDraft
    Set-Val $td.values 'sessions_psychology' '5'
    Set-Val $td.values 'monthly_fee_total' '1000.00'
    $r = Save-Draft $tDraft $td
    Assert 'T7.7 total recalculado -> 200' ($r.Code -eq 200) (Snip $r.Body)
}

# T7.8/T7.9: sem overlay no perfil geral (mesmo modelo)
$org = Get-Org
$r = Put-Org $org 'general' $org.version
Assert 'T7.8 perfil general -> 204' ($r.Code -eq 204) (Snip $r.Body)
$r = New-Draft $mtId 'B34 Terapias Perfil Geral'
Assert 'T7.9 draft terapias no perfil geral -> 201' ($r.Code -eq 201) (Snip $r.Body)
if ($r.Code -eq 201) {
    $gDraft = ($r.Body | ConvertFrom-Json).id
    $gd = Get-Draft $gDraft
    $gSessions = @(@($gd.fields) | Where-Object { $_.Id -like 'sessions_*' })
    Assert 'T7.9 sem campos de sessoes no perfil geral' ($gSessions.Count -eq 0) ('count=' + $gSessions.Count)
    $gFormula = @(@($gd.fields) | Where-Object { $_.Id -eq 'monthly_fee_total' })
    Assert 'T7.9 sem formula no perfil geral' ($gFormula.Count -eq 0) ('count=' + $gFormula.Count)
}

# ---------- T8: aprovacao ODCA bloqueia a emissao (D3) ----------
Write-Host '--- T8 bloqueio de aprovacao ODCA ---'
if ($sDraft) {
    $sd = Get-Draft $sDraft
    $fill = @{
        'surgeon_name'        = 'Dra. Marina Cirurgia B34'
        'surgeon_document'    = '11144477735'
        'patient_name'        = 'Paciente Teste B34'
        'patient_document'    = '52998224725'
        'procedure_description' = 'Rinoplastia funcional com septoplastia.'
        'surgery_date'        = '2027-03-15'
        'facility_name'       = 'Hospital Central B34'
        'anesthesia_type'     = 'Anestesia geral'
        'has_representative'  = 'Não'
        'procedure_fee'       = '1234.56'
        'payment_terms'       = 'À vista'
        'risks_acknowledgement' = 'Li e compreendi os riscos descritos'
        'signing_date'        = '2026-10-10'
        'jurisdiction_city'   = 'Belém/PA'
    }
    $fill['organization_name'] = Sql "SELECT display_name FROM odca.tenants WHERE id='$tenant';"
    foreach ($k in $fill.Keys) {
        $e = Get-Val $sd.values $k
        if ($null -eq $e -or [string]::IsNullOrWhiteSpace([string]$e.value)) { Set-Val $sd.values $k $fill[$k] }
    }
    Confirm-Values $sd.values
    $r = Save-Draft $sDraft $sd
    Assert 'T8.1 minuta cirurgica completa salva -> 200' ($r.Code -eq 200) (Snip $r.Body)

    $sd = Get-Draft $sDraft
    $genBody = ConvertTo-Json -InputObject ([pscustomobject]@{
        idempotencyKey = [guid]::NewGuid().ToString()
        expectedVersion = [int64]$sd.version
    }) -Depth 5
    $rg = Invoke-Api 'POST' ("$api/api/v1/organizations/$tenant/studio/drafts/$sDraft/versions") $script:tok $genBody
    Assert 'T8.2 geracao da versao -> 201' ($rg.Code -eq 201) (Snip $rg.Body)
    $sVer = ''
    if ($rg.Code -eq 201) { $sVer = ($rg.Body | ConvertFrom-Json).id }
    Assert 'T8.2 id da versao cirurgica' ([bool]$sVer) (Snip $rg.Body)

    if ($sVer) {
        $rr = Invoke-Api 'GET' ("$api/api/v1/organizations/$tenant/studio/versions/$sVer/signature-preparation/readiness") $script:tok $null
        Assert 'T8.3 readiness -> 200' ($rr.Code -eq 200) (Snip $rr.Body)
        $rd = $rr.Body | ConvertFrom-Json
        $approval = @(@($rd.blockers) | Where-Object { $_.code -eq 'approval.odca.required' })
        Assert 'T8.3 blocker approval.odca.required' (@($approval).Count -ge 1) (('blockers=' + ((@($rd.blockers) | ForEach-Object { $_.code }) -join ',')))
        Assert 'T8.3 canConfirm=false' ($rd.canConfirm -eq $false) ([string]$rd.canConfirm)

        $opId = [guid]::NewGuid().ToString()
        $partId = [guid]::NewGuid().ToString()
        $confBody = ConvertTo-Json -InputObject ([pscustomobject]@{
            expectedVersion = 0
            confirm = $true
            operationId = $opId
            participants = @([pscustomobject]@{
                id = $partId; participantType = 'professional'; sourceId = $null
                role = 'Cirurgião responsável'; name = 'Dra. B34 Teste'
                email = 'cirurgiao.b34@odca.local'; phone = $null; position = 1
                signedAt = $null; signedBy = $null
            })
        }) -Depth 8
        $rc = Invoke-Api 'PUT' ("$api/api/v1/organizations/$tenant/studio/versions/$sVer/signature-preparation") $script:tok $confBody
        Assert 'T8.4 confirmacao rejeitada -> 400' ($rc.Code -eq 400) (Snip $rc.Body)
        Assert 'T8.4 motivos citam aprovacao ODCA' ((Dec $rc.Body).Contains('pendente de aprovacao ODCA') -or (Dec $rc.Body).Contains('pendente de aprovação ODCA')) (Snip $rc.Body)

        # controle: versao nao cirurgica nao recebe o blocker
        $ctrl = Sql "SELECT id FROM odca.generated_contract_versions WHERE tenant_id='$tenant' AND source_template_id <> '$sgId' LIMIT 1;"
        if ($ctrl -match '[0-9a-f]{8}-') {
            $rcv = Invoke-Api 'GET' ("$api/api/v1/organizations/$tenant/studio/versions/$ctrl/signature-preparation/readiness") $script:tok $null
            if ($rcv.Code -eq 200) {
                $rdc = $rcv.Body | ConvertFrom-Json
                $ctrlApproval = @(@($rdc.blockers) | Where-Object { $_.code -eq 'approval.odca.required' })
                Assert 'T8.5 versao nao cirurgica sem blocker ODCA' (@($ctrlApproval).Count -eq 0) ('ctrl=' + $ctrl)
            }
            else { Assert 'T8.5 readiness do controle -> 200' ($rcv.Code -eq 200) (Snip $rcv.Body) }
        }
        else { Write-Host 'SKIP  T8.5 controle: nenhuma versao nao cirurgica existente no banco' }
    }
}

Write-Host ''
Write-Host '=== BLOCO C: centro de notificacoes + tipo revisao administrativa + comparacao de versoes ==='
try {
    $cli = Login-Web 'cliente.teste@odca.local' 'K8@wR3!nF6#zP2$m'
    $cliUser = (Sql "SELECT id FROM odca.users WHERE email_normalized=upper('cliente.teste@odca.local')").Trim()
    Assert 'C.setup usuario cliente' ($cliUser -match '^[0-9a-f]{8}-') $cliUser

    # A5: centro de notificacoes (nav + pagina + semear + listar + marcar-lidas)
    $hNav = Invoke-Page 'GET' "$web/minha-conta" '' $cli.Jar
    Assert 'C.notif menu exibe item' ($hNav.Contains('/minha-conta/notificacoes')) $script:lastUrl
    SqlOpt "DELETE FROM odca.user_notifications WHERE kind='qa-bc-notif'"
    $hN0 = Invoke-Page 'GET' "$web/minha-conta/notificacoes" '' $cli.Jar
    Assert 'C.notif pagina renderiza autenticada' ($script:lastUrl -notlike '*entrar*') $script:lastUrl
    Sql "INSERT INTO odca.user_notifications(tenant_id,user_id,kind,title,body,created_at) VALUES('$tenant','$cliUser','qa-bc-notif','QA BC Notif Titulo','QA BC Notif Corpo',now())"
    $hN1 = Invoke-Page 'GET' "$web/minha-conta/notificacoes" '' $cli.Jar
    Assert 'C.notif lista exibe notificacao semeada' ($hN1.Contains('QA BC Notif Titulo')) $script:lastUrl
    Assert 'C.notif item marcado como nao lida' ($hN1.Contains('notification-unread')) $script:lastUrl
    try {
        $ntok = Get-Token $hN1
        Invoke-Page 'POST' "$web/minha-conta/notificacoes/marcar-lidas" ('__RequestVerificationToken=' + (Esc $ntok)) $cli.Jar | Out-Null
        $hN2 = Invoke-Page 'GET' "$web/minha-conta/notificacoes" '' $cli.Jar
        Assert 'C.notif marcar-lidas zera nao lidas' (-not $hN2.Contains('notification-unread')) $script:lastUrl
    } catch { Assert 'C.notif marcar-lidas zera nao lidas' $false ('erro: ' + $_.Exception.Message) }
    SqlOpt "DELETE FROM odca.user_notifications WHERE kind='qa-bc-notif'"

    # A3: terceiro tipo de alteracao (revisao administrativa) na listagem e no detalhe
    # escolhe contrato sem alteracao ativa (evita violar contract_change_one_active_uq)
    $bcContract = (Sql "SELECT c.id FROM odca.contracts c WHERE c.tenant_id='$tenant' AND NOT EXISTS (SELECT 1 FROM odca.contract_change_requests r WHERE r.tenant_id=c.tenant_id AND r.contract_id=c.id AND r.status NOT IN ('cancelled','conflict')) ORDER BY c.created_at DESC LIMIT 1").Trim()
    if ($bcContract -match '[0-9a-f]{8}-') {
        SqlOpt "DELETE FROM odca.contract_change_requests WHERE tenant_id='$tenant' AND reason='QA Bloco C revisao administrativa'"
        $bcReq = (Sql "INSERT INTO odca.contract_change_requests(tenant_id,contract_id,kind,status,author_id,responsible_id,reason,effective_on,other_changes,base_contract_version,idempotency_key) VALUES('$tenant','$bcContract','revision','draft','$cliUser','$cliUser','QA Bloco C revisao administrativa',CURRENT_DATE,'[]',1,gen_random_uuid()) RETURNING id").Trim()
        $hRen = Invoke-Page 'GET' "$web/organizacoes/$tenant/renovacoes" '' $cli.Jar
        Assert 'C.renov lista exibe tipo revisao' ($hRen.Contains('Revisão administrativa')) $script:lastUrl
        $hDet = Invoke-Page 'GET' "$web/organizacoes/$tenant/renovacoes/$bcReq" '' $cli.Jar
        Assert 'C.renov detalhe rotula revisao' ($hDet.Contains('Revisão administrativa')) $script:lastUrl
        SqlOpt "DELETE FROM odca.contract_change_requests WHERE id='$bcReq'"
    } else {
        Write-Host 'SKIP  C.renov: nenhum contrato sem alteracao ativa no tenant para teste de tipo'
    }

    # A2: pagina de comparacao de versoes (render)
    $bcDraft = (Sql "SELECT d.id FROM odca.contract_drafts d WHERE d.tenant_id='$tenant' ORDER BY d.updated_at DESC LIMIT 1").Trim()
    if ($bcDraft -match '[0-9a-f]{8}-') {
        $hCmp = Invoke-Page 'GET' ("$web/organizacoes/$tenant/estudio/comparar-versoes?draftId=" + $bcDraft) '' $cli.Jar
        Assert 'C.compare pagina renderiza autenticada' ($script:lastUrl -notlike '*entrar*' -and $hCmp.Contains('comparar-versoes')) $script:lastUrl
    } else {
        Write-Host 'SKIP  C.compare: nenhum rascunho no tenant'
    }
} catch {
    Assert 'C.bloco_c executou sem erro fatal' $false ('excecao: ' + $_.Exception.Message)
}

Write-Host ('=== RESULTADO B34: PASS=' + $script:passes + ' FAIL=' + $script:failures + ' ===')
if ($script:failures -gt 0) { exit 1 } else { exit 0 }
