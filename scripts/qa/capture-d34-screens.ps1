# D-OC34 - capturas visuais das telas afetadas: 360/768/1280/1440 + zoom 200% x 4 idiomas.
# Reusa o driver CDP d31-cdp-v2.mjs (msedge headless). Servicos devem estar no ar (apos a prova A-N).
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$root   = 'C:\MNSOFT\odcasolutions'
$logDir = 'C:\Users\NCELL-DEV-020\AppData\Local\Temp\opencode'
$evDir  = Join-Path $logDir 'evidence'
$outDir = Join-Path $evDir 'captures-d34'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$evidence = Join-Path $evDir 'd34-captures.txt'
Set-Content -Path $evidence '' -Encoding utf8
function LogEv([string]$m) { Add-Content -Path $evidence $m -Encoding utf8 }
function Db([string]$q) {
  $env:PGPASSWORD = '123456'; $env:PGCONNECT_TIMEOUT = '10'
  return ((& 'C:\Program Files\PostgreSQL\18\bin\psql.exe' -h 127.0.0.1 -U postgres -d odca_test_disposable -t -A -c $q 2>&1 | Out-String).Trim())
}
function Get-Totp([string]$b32) {
  $alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'
  $cleaned = ($b32.Trim() -replace ' ', '').ToUpper()
  $output = New-Object System.Collections.Generic.List[byte]
  $buffer = 0; $bitsLeft = 0
  foreach ($ch in $cleaned.ToCharArray()) {
    $index = $alphabet.IndexOf([string]$ch)
    if ($index -lt 0) { throw "base32 char invalido: $ch" }
    $buffer = ($buffer -shl 5) -bor $index
    $bitsLeft += 5
    if ($bitsLeft -ge 8) { $output.Add([byte](($buffer -shr ($bitsLeft - 8)) -band 0xFF)); $bitsLeft -= 8 }
  }
  $key = $output.ToArray()
  $counter = [Math]::Floor(([DateTimeOffset]::UtcNow.ToUnixTimeSeconds() / 30))
  $buf = New-Object byte[] 8
  for ($i = 7; $i -ge 0; $i--) { $buf[$i] = [byte]($counter % 256); $counter = [Math]::Floor($counter / 256) }
  $hmac = [System.Security.Cryptography.HMACSHA1]::new($key)
  $hash = $hmac.ComputeHash($buf)
  $offset = $hash[19] -band 0x0F
  $bin = ((([int]$hash[$offset] -band 0x7F) -shl 24) -bor ([int]$hash[$offset+1] -shl 16) -bor ([int]$hash[$offset+2] -shl 8) -bor [int]$hash[$offset+3])
  return ($bin % 1000000).ToString('D6')
}
function Api([string]$method, [string]$path, [string]$tok, $body = $null) {
  $url = 'http://localhost:5143/api/v1' + $path
  try {
    if ($null -ne $body) {
      $bytes = [Text.Encoding]::UTF8.GetBytes(($body | ConvertTo-Json -Depth 8))
      $resp = Invoke-WebRequest -Uri $url -Method $method -Headers @{ Authorization = "Bearer $tok" } -ContentType 'application/json; charset=utf-8' -Body $bytes -UseBasicParsing -TimeoutSec 60
    } else {
      $hdrs = @{}; if ($tok) { $hdrs['Authorization'] = "Bearer $tok" }
      $resp = Invoke-WebRequest -Uri $url -Method $method -Headers $hdrs -UseBasicParsing -TimeoutSec 60
    }
    $raw = if ($resp.Content -is [System.Byte[]]) { [Text.Encoding]::UTF8.GetString([byte[]]$resp.Content) } else { [string]$resp.Content }
    return @{ Status = [int]$resp.StatusCode; Raw = $raw }
  } catch {
    $code = 0; $raw = ''
    if ($_.Exception.Response) { $code = [int]$_.Exception.StatusCode; $reader = New-Object IO.StreamReader($_.Exception.Response.GetResponseStream(), [Text.Encoding]::UTF8); $raw = $reader.ReadToEnd() } else { $raw = $_.Exception.Message }
    return @{ Status = $code; Raw = $raw }
  }
}
function J($r) { try { return ($r.Raw | ConvertFrom-Json) } catch { return $null } }
function Api-Key([string]$email, [string]$pass) {
  $r = Api 'POST' '/auth/login' '' @{ Login = $email; Password = $pass }
  if ($r.Status -ne 200) { throw ("login falhou para " + $email + ": " + $r.Raw.Substring(0, [Math]::Min(300, $r.Raw.Length))) }
  $j = J $r
  if (($j.isPlatformAdministrator -eq $true) -and ($j.mfaVerified -ne $true)) {
    if ($j.requiresMfaEnrollment -ne $true) { throw ('mfa inesperado para ' + $email) }
    $enr = Api 'POST' '/auth/mfa/enrollment' ([string]$j.accessToken) $null
    if ($enr.Status -ne 200) { throw ('enrollment falhou: ' + $enr.Raw) }
    $key = [string](J $enr).manualKey
    Start-Sleep -Milliseconds 300
    $conf = Api 'POST' '/auth/mfa/enrollment/confirm' ([string]$j.accessToken) @{ Code = (Get-Totp $key) }
    if ($conf.Status -ne 200) { throw ('confirm falhou: ' + $conf.Raw) }
    return $key
  }
  return ''
}

$t = '20000000-0000-4000-8000-000000000001'
$draftA = Db "SELECT d.id::text FROM odca.contract_drafts d JOIN odca.contracts c ON c.id=d.contract_id AND c.tenant_id=d.tenant_id WHERE c.title='Prova D-OC34 paciente' ORDER BY d.created_at DESC LIMIT 1;"
$cidB = Db "SELECT d.contract_id::text FROM odca.contract_drafts d JOIN odca.contracts c ON c.id=d.contract_id AND c.tenant_id=d.tenant_id WHERE c.title='Prova D-OC34 jornada assinatura' ORDER BY d.created_at DESC LIMIT 1;"
LogEv ("draftA=" + $draftA + " cidB=" + $cidB)
if (-not $draftA -or -not $cidB) { Write-Host 'FIXTURES-AUSENTES: rode d34-proof.ps1 antes'; exit 2 }

# ---------- stack http-pura (servicos nao sobrevivem ao fim do shell) ----------
$rootEsc = [Regex]::Escape($root)
Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -and $_.CommandLine -match $rootEsc -and $_.CommandLine -match 'Odca\.(Api|Web|Worker)\.(dll|csproj)' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
Start-Sleep -Seconds 2
$env:ODCA_RUNTIME_CONFIG = Join-Path $root 'src\Odca.Api\development-runtime.json'
$env:DOTNET_ENVIRONMENT = 'Development'
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ODCA_SETUP_ON_START = 'true'
function Wait-Ready([string]$url) {
  for ($i = 1; $i -le 90; $i++) {
    Start-Sleep -Seconds 2
    try { $h = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 3; if ([int]$h.StatusCode -lt 500) { return $true } } catch {}
  }
  return $false
}
$env:ASPNETCORE_URLS = 'http://localhost:5143'; $env:Api__BaseUrl = 'http://localhost:5143/'
$pa = Start-Process dotnet -ArgumentList ('exec "{0}"' -f (Join-Path $root 'src\Odca.Api\bin\Debug\net10.0\Odca.Api.dll')) -WorkingDirectory (Join-Path $root 'src\Odca.Api') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $logDir 'd34-cap-api.log') -RedirectStandardError (Join-Path $logDir 'd34-cap-api-err.log')
$env:ASPNETCORE_URLS = 'http://localhost:5144'
$pw = Start-Process dotnet -ArgumentList ('exec "{0}"' -f (Join-Path $root 'src\Odca.Web\bin\Debug\net10.0\Odca.Web.dll')) -WorkingDirectory (Join-Path $root 'src\Odca.Web') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $logDir 'd34-cap-web.log') -RedirectStandardError (Join-Path $logDir 'd34-cap-web-err.log')
$stackPids = @($pa.Id, $pw.Id)
if (-not (Wait-Ready 'http://localhost:5143/health/ready')) { Write-Host 'API-NUNCA-PRONTA'; exit 2 }
if (-not (Wait-Ready 'http://localhost:5144/entrar')) { Write-Host 'WEB-NUNCA-PRONTA'; exit 2 }
LogEv 'STACK pronta (http puro)'

# MFA fresco para as contas de plataforma (fixture reversivel, padrao D-OC31)
$boot = Join-Path $root 'src\Odca.Bootstrap\bin\Debug\net10.0\Odca.Bootstrap.dll'
& dotnet exec $boot reset-mfa --account ADMIN@ODCA.LOCAL 2>&1 | Out-Null
& dotnet exec $boot reset-mfa --account OPERADOR@ODCA.LOCAL 2>&1 | Out-Null
Db "UPDATE odca.users SET must_change_password=false WHERE email ILIKE 'operador@odca.local' OR email ILIKE 'admin@odca.local';" | Out-Null
& dotnet exec $boot reset-password --account admin --allow-postgres-development 2>&1 | Out-Null
Db "UPDATE odca.users SET must_change_password=false WHERE email ILIKE 'admin@odca.local';" | Out-Null
$slLines = (& dotnet exec $boot show-login 2>&1 | Out-String) -split "`r?`n"
$hdrIdx = -1
for ($k = 0; $k -lt $slLines.Count; $k++) { if ($slLines[$k] -like '*Superadministrador*') { $hdrIdx = $k; break } }
$adminPass = ''
for ($k = $hdrIdx + 1; $k -lt [Math]::Min($hdrIdx + 8, $slLines.Count); $k++) { if ($slLines[$k] -match 'inicial local.*: (\S+)$') { $adminPass = $Matches[1]; break } }
if ($adminPass.Length -le 8) { Write-Host 'ADM-SEM-SENHA'; exit 2 }
$adKey = Api-Key 'admin@odca.local' $adminPass
$opKey = Api-Key 'operador@odca.local' 'La@RsjC5p-hmq_JpReGW'
LogEv ("MFA chaves frescas admin/op (len " + $adKey.Length + '/' + $opKey.Length + ')')

$cfgObj = [ordered]@{
  base = 'http://localhost:5144'
  outDir = $outDir
  edge = 'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe'
  langs = @('pt-BR', 'en-US', 'es-ES', 'fr-FR')
  viewports = @( @(360, 780), @(768, 1024), @(1280, 800), @(1440, 900) )
  zoomAt = @(1280, 800)
  creds = [ordered]@{
    cliente  = [ordered]@{ email = 'cliente.teste@odca.local'; pass = 'K8@wR3!nF6#zP2$m' }
    operador = [ordered]@{ email = 'operador@odca.local'; pass = 'La@RsjC5p-hmq_JpReGW'; mfaKey = $opKey }
    admin    = [ordered]@{ email = 'admin@odca.local'; pass = $adminPass; mfaKey = $adKey }
  }
  screens = @(
    [ordered]@{ id = 'd34-edit-paciente'; session = 'cliente'; path = "/organizacoes/$t/estudio/minutas/$draftA" },
    [ordered]@{ id = 'd34-sheet-banner'; session = 'cliente'; path = "/organizacoes/$t/contratos/$cidB" },
    [ordered]@{ id = 'd34-documentos'; session = 'cliente'; path = "/organizacoes/$t/documentos" },
    [ordered]@{ id = 'd34-novo-doc'; session = 'cliente'; path = "/organizacoes/$t/documentos/novo" },
    [ordered]@{ id = 'd34-diagnostico'; session = 'admin'; path = '/administracao/diagnostico' }
  )
}
$cfgFile = Join-Path $logDir 'd34-cfg.json'
Set-Content -Path $cfgFile -Value ($cfgObj | ConvertTo-Json -Depth 10) -Encoding utf8
LogEv ('CAPTURES: telas=5 langs=4 viewports=4+zoom2x => 100 png em ' + $outDir)
$driver = Join-Path $root 'scripts\qa\cdp-capture-v2.mjs'
if (-not (Test-Path $driver)) { $driver = Join-Path $logDir 'd31-cdp-v2.mjs' }
& node $driver $cfgFile 2>&1 | ForEach-Object { LogEv ('CDP| ' + $_); Write-Host $_ }
foreach ($id in $stackPids) { Stop-Process -Id $id -Force -ErrorAction SilentlyContinue }
exit $LASTEXITCODE
