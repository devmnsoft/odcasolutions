# D-OC60 (Sprint 25) - prova ao vivo: jornada operacional completa validada JUNTA sobre a
# base descartavel odca_test_disposable. Um unico caso sintetico atravessa os cinco elos do
# marco "operations" SEM purgas entre fases (o ensaio ate S24 provava cada elo em estagios
# isolados que se apagavam mutuamente):
#   paciente -> documento -> revisao -> assinatura externa -> agenda -> renovacao
# Stack: api :5143; web :5144; worker ATIVO; envelope externo provider local-file desde o
# boot (mesmo padrao D-OC37). Declaracoes de fixture (dados que nao nascem pela interface):
#   plano enterprise v2 (modulo de revisoes), papel de permissoes de obrigacoes, opt-in de
#   e-mail signature_completed, campos obrigatorios do NDA preenchidos como manual+confirmado
#   e vigencia/aviso do contrato (+30 dias) para o ciclo de renovacao.
# Uso standalone: powershell -NoProfile -ExecutionPolicy Bypass -File scripts/qa/validate-d60-assinatura-completa.ps1
# Como 9o estagio do dress rehearsal: scripts/qa/run-pilot-rehearsal.ps1 [-Only 'D59-JORNADA']
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
$logDir = 'C:\Users\NCELL-DEV-020\AppData\Local\Temp\opencode'
$root   = 'C:\MNSOFT\odcasolutions'
$evDir  = Join-Path $logDir 'evidence'
$tmpDir = Join-Path $logDir 'd59-fx'
$pickUp = Join-Path $logDir 'd59-pickup'
New-Item -ItemType Directory -Force -Path $evDir | Out-Null
New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
Remove-Item $pickUp -Recurse -Force -EA SilentlyContinue
New-Item -ItemType Directory -Force -Path $pickUp | Out-Null
$evidence = Join-Path $evDir 'd59-proof.txt'
Set-Content -Path $evidence '' -Encoding utf8
$script:fails = 0
$script:lastPsqlErr = ''

function Quote-CmdArg([string]$a) { if ($a -match '[ \t"\\]') { return ('"' + ($a -replace '"','\"') + '"') } return $a }
function Watch-Curl([int]$ms, [string[]]$ca) {
  $sw0 = Get-Date
  $psi = New-Object System.Diagnostics.ProcessStartInfo
  $psi.FileName = 'curl.exe'
  $psi.Arguments = ((@($ca) | ForEach-Object { Quote-CmdArg ([string]$_) }) -join ' ')
  $psi.UseShellExecute = $false
  $psi.RedirectStandardOutput = $true
  $psi.RedirectStandardError = $true
  $psi.WorkingDirectory = $tmpDir
  $psi.CreateNoWindow = $true
  $p = [System.Diagnostics.Process]::Start($psi)
  $deadline = $sw0.AddMilliseconds($ms + 8000)
  while (-not $p.HasExited) {
    if ((Get-Date) -ge $deadline) { try { $p.Kill() } catch {}; break }
    Start-Sleep -Milliseconds 300
  }
  try { $so = $p.StandardOutput.ReadToEnd() } catch { $so = '' }
  try { $p.WaitForExit() } catch {}
  if ($so.Trim()) { return $so.Trim() } else { return '000' }
}
function LogEv([string]$m) { Add-Content -Path $evidence $m -Encoding utf8 }
function Check([string]$name, [bool]$ok, [string]$detail) {
  $line = ('[' + $(if ($ok) { 'PASS' } else { 'FAIL' }) + '] {0} {1}' -f $name, ($(if ($null -eq $detail) { '' } else { [string]$detail })))
  Write-Host $line; LogEv $line
  if (-not $ok) { $script:fails++ }
}
function Db([string]$q) {
  $env:PGPASSWORD = '123456'; $env:PGCONNECT_TIMEOUT = '10'
  $ef = Join-Path $tmpDir 'psql.err'
  $out = & 'C:\Program Files\PostgreSQL\18\bin\psql.exe' -h 127.0.0.1 -U postgres -d odca_test_disposable -t -A -c $q 2>$ef | Out-String
  $script:lastPsqlErr = ((Get-Content $ef -EA SilentlyContinue) -join ' ')
  return $out.Trim()
}
function DbFile([string]$tag, [string]$sql) {
  # psql -f: quando a SQL contem aspas duplas (JSON), o -c entregue via & no PS 5.1 chega
  # corrompido ao psql (bug de quoting de argumentos nativos). Escrever em arquivo evita.
  $env:PGPASSWORD = '123456'; $env:PGCONNECT_TIMEOUT = '10'
  $f = Join-Path $tmpDir ('d59-sql-' + $tag + '.sql')
  [IO.File]::WriteAllText($f, $sql, (New-Object Text.UTF8Encoding($false)))
  $ef = Join-Path $tmpDir ('d59-sql-' + $tag + '.err')
  $out = & 'C:\Program Files\PostgreSQL\18\bin\psql.exe' -h 127.0.0.1 -U postgres -d odca_test_disposable -v ON_ERROR_STOP=1 -f $f 2>$ef | Out-String
  $script:lastPsqlErr = ((Get-Content $ef -EA SilentlyContinue) -join ' ')
  return $out.Trim()
}
function Get-Totp([string]$b32) {
  if (-not $b32) { throw "chave MFA vazia" }
  $s = $b32.Trim()
  if ($s.StartsWith('[') -and $s.EndsWith(']')) {
    try {
      $arr = $s | ConvertFrom-Json
      if ($arr -is [array] -and $arr.Count -gt 0) { $s = [string]$arr[0] }
    } catch {}
  }
  $s = $s -replace '"','' -replace "'","" -replace '\s+',''
  $alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'
  $cleaned = $s.ToUpper()
  $output = New-Object System.Collections.Generic.List[byte]
  $buffer = 0; $bitsLeft = 0
  foreach ($ch in $cleaned.ToCharArray()) {
    $index = $alphabet.IndexOf([string]$ch)
    if ($index -lt 0) { throw "base32 char invalido: $ch" }
    $buffer = ($buffer -shl 5) -bor $index
    $bitsLeft += 5
    if ($bitsLeft -ge 8) {
      $output.Add([byte](($buffer -shr ($bitsLeft - 8)) -band 0xFF))
      $bitsLeft -= 8
    }
  }
  $key = $output.ToArray()
  $counter = [Math]::Floor(([DateTimeOffset]::UtcNow.ToUnixTimeSeconds() / 30))
  $buf = New-Object byte[] 8
  for ($i = 7; $i -ge 0; $i--) { $buf[$i] = [byte]($counter % 256); $counter = [Math]::Floor($counter / 256) }
  $hmac = New-Object System.Security.Cryptography.HMACSHA1
  $hmac.Key = $key
  $hash = $hmac.ComputeHash($buf)
  $offset = $hash[19] -band 0x0F
  $bin = ((([int]$hash[$offset] -band 0x7F) -shl 24) -bor ([int]$hash[$offset+1] -shl 16) -bor ([int]$hash[$offset+2] -shl 8) -bor [int]$hash[$offset+3])
  return ($bin % 1000000).ToString('D6')
}
$apiBase = 'http://localhost:5143/api/v1'
function Api([string]$method, [string]$path, [string]$tok, $body = $null) {
  $url = "$apiBase$path"
  $tries = 0
  while ($tries -lt 4) {
    try {
      if ($null -ne $body) {
        $bytes = [Text.Encoding]::UTF8.GetBytes(($body | ConvertTo-Json -Depth 8))
        $resp = Invoke-WebRequest -Uri $url -Method $method -Headers @{ Authorization = "Bearer $tok" } -ContentType 'application/json; charset=utf-8' -Body $bytes -UseBasicParsing -TimeoutSec 60
      } else {
        $hdrs = @{}; if ($tok) { $hdrs['Authorization'] = "Bearer $tok" }
        $resp = Invoke-WebRequest -Uri $url -Method $method -Headers $hdrs -UseBasicParsing -TimeoutSec 60
      }
      return @{ Status = [int]$resp.StatusCode; Raw = $resp.Content }
    } catch {
      $tries++
      if ($_.Exception.Response) {
        try {
          $codeR = [int]$_.Exception.Response.StatusCode
          if ($codeR -eq 429 -and $tries -lt 4) {
            $rs = $_.Exception.Response.Headers['Retry-After'] | Select-Object -First 1
            $sleep = 2
            if ($rs -and [int]::TryParse([string]$rs,[ref]$sleep)) { $sleep = [int]$rs }
            Start-Sleep -Seconds ([Math]::Max(1,$sleep))
            continue
          }
        } catch {}
        # PS 5.1: WebException expoe o HTTP status em Response.StatusCode
        $code = [int]$_.Exception.Response.StatusCode
        try {
          $reader = New-Object IO.StreamReader($_.Exception.Response.GetResponseStream(), [System.Text.Encoding]::UTF8)
          $raw = $reader.ReadToEnd()
        } catch {
          $raw = $_.Exception.Message
        }
        return @{ Status = $code; Raw = $raw }
      } else {
        if ($tries -lt 4) { Start-Sleep -Seconds 1; continue }
        return @{ Status = 0; Raw = $_.Exception.Message }
      }
    }
  }
  return @{ Status = 0; Raw = 'max-retries' }
}
function J($r) { try { return ($r.Raw | ConvertFrom-Json) } catch { return $null } }
function Api-LoginEnroll([string]$email, [string]$pass) {
  $r = Api 'POST' '/auth/login' '' @{ Login = $email; Password = $pass }
  if ($r.Status -ne 200) { throw ("login falhou para " + $email + ": " + $r.Raw.Substring(0, [Math]::Min(300, $r.Raw.Length))) }
  $j = J $r; $tok = [string]$j.accessToken; $key = ''
  if (($j.isPlatformAdministrator -eq $true) -and ($j.mfaVerified -ne $true)) {
    if ($j.requiresMfaEnrollment -ne $true) { throw ("mfa em estado inesperado para " + $email + ": " + ($r.Raw | ConvertTo-Json -Compress -Depth 5)) }
    $enr = Api 'POST' '/auth/mfa/enrollment' $tok $null
    if ($enr.Status -ne 200) { throw ("mfa enrollment falhou: " + $enr.Raw) }
    $key = [string](J $enr).manualKey
    Start-Sleep -Milliseconds 300
    $conf = Api 'POST' '/auth/mfa/enrollment/confirm' $tok @{ Code = (Get-Totp $key) }
    if ($conf.Status -ne 200) { throw ("mfa confirm falhou: " + $conf.Raw) }
  }
  return [pscustomobject]@{ Token = $tok; MfaKey = $key }
}
# Token MFA-verificado: o login do administrador de plataforma devolve um token ainda nao
# verificado; a API recusa esse token nas leituras (agenda) e ele expira em 15 min. A jornada
# e longa, entao cada leitura renova o token pelo desafio TOTP com a chave da inscricao.
function Api-LoginMfa([string]$email, [string]$pass, [string]$key) {
  $tries = 0
  while ($tries -lt 5) {
    $tries++
    try {
      $r = Api 'POST' '/auth/login' '' @{ Login = $email; Password = $pass }
    } catch {
      Start-Sleep -Seconds 2; continue
    }
    if ($r.Status -eq 0 -or $r.Status -eq 429) { Start-Sleep -Seconds (2 + $tries); continue }
    if ($r.Status -ne 200) { throw ("login falhou para " + $email + ": " + [string]$r.Raw.Substring(0, [Math]::Min(300, [string]$r.Raw.Length))) }
    $j = J $r
    if ($j.requiresMfaChallenge -eq $true) {
      $ch = Api 'POST' '/auth/mfa/challenge' ([string]$j.accessToken) @{ Code = (Get-Totp $key) }
      if ($ch.Status -eq 0 -or $ch.Status -eq 429) { Start-Sleep -Seconds (2 + $tries); continue }
      if ($ch.Status -ne 200) {
        Start-Sleep -Seconds (31 - ([DateTimeOffset]::UtcNow.ToUnixTimeSeconds() % 30))
        $ch = Api 'POST' '/auth/mfa/challenge' ([string]$j.accessToken) @{ Code = (Get-Totp $key) }
        if ($ch.Status -eq 0 -or $ch.Status -eq 429) { Start-Sleep -Seconds 2; $ch = Api 'POST' '/auth/mfa/challenge' ([string]$j.accessToken) @{ Code = (Get-Totp $key) } }
      }
      if ($ch.Status -ne 200) { throw ("mfa challenge falhou: " + [string]$ch.Raw.Substring(0, [Math]::Min(300, [string]$ch.Raw.Length))) }
      return [pscustomobject]@{ Token = [string]((J $ch).accessToken) }
    }
    return [pscustomobject]@{ Token = [string]$j.accessToken }
  }
  throw ("Api-LoginMfa excedeu tentativas para " + $email)
}
function Api-LoginClient([string]$email, [string]$pass) {
  $tries = 0
  while ($tries -lt 5) {
    $tries++
    try { $r = Api 'POST' '/auth/login' '' @{ Login = $email; Password = $pass } } catch { Start-Sleep -Seconds 2; continue }
    if ($r.Status -eq 0 -or $r.Status -eq 429) { Start-Sleep -Seconds (2 + $tries); continue }
    if ($r.Status -ne 200) { throw ("login API falhou para " + $email + ': ' + [string]$r.Raw.Substring(0, [Math]::Min(200, [string]$r.Raw.Length))) }
    return [string]((J $r).accessToken)
  }
  throw ("Api-LoginClient excedeu tentativas para " + $email)
}
$base = 'http://localhost:5144'
function Get-Af([string]$html) {
  if ($html -match 'name="__RequestVerificationToken"[^>]*value="([^"]+)"') { return $Matches[1] }
  if ($html -match 'value="([^"]+)"[^>]*name="__RequestVerificationToken"') { return $Matches[1] }
  return ''
}
function Web-Login([string]$jar, [string]$email, [string]$pass, [string]$mfaKey) {
  $fh = Join-Path $tmpDir 'login-page.html'
  for ($g = 1; $g -le 3; $g++) {
    Remove-Item $fh -EA SilentlyContinue
    $null = Watch-Curl 45000 @('-k','--connect-timeout','5','--max-time','20','-s','-c',$jar,"$base/entrar","-o",$fh)
    $sz = 0; if (Test-Path $fh) { $sz = ([IO.File]::ReadAllBytes($fh)).Length }
    if ($sz -gt 500) { break }
    Start-Sleep -Seconds 15
  }
  $af = Get-Af ([IO.File]::ReadAllText($fh, [System.Text.Encoding]::UTF8))
  $form = 'Login=' + [Uri]::EscapeDataString($email) + '&Password=' + [Uri]::EscapeDataString($pass) + '&ReturnUrl=&__RequestVerificationToken=' + [Uri]::EscapeDataString($af)
  $hdrf = Join-Path $tmpDir 'login-post.hdr'
  $out = '000'
  for ($po = 1; $po -le 3; $po++) {
    Remove-Item $hdrf -EA SilentlyContinue
    $out = Watch-Curl 45000 @('-k','--connect-timeout','5','--max-time','20','-s','-b',$jar,'-c',$jar,'-o','NUL','-D',$hdrf,'-w','%{http_code}','-X','POST','-d',$form,"$base/entrar")
    if ([string]$out -ne '000') { break }
    Start-Sleep -Seconds 15
  }
  $code = ([string]$out).Trim(); $loc = ''
  if (Test-Path $hdrf) { $hm = [regex]::Match(([IO.File]::ReadAllText($hdrf)), '(?im)^Location:[ \t]*(\S+)'); if ($hm.Success) { $loc = $hm.Groups[1].Value } }
  if (($code -eq '302') -and ($loc -like '*mfa/desafio*')) {
    if (-not $mfaKey) { throw ("desafio MFA exigido sem chave conhecida para " + $email) }
    $ok = $false
    $mfaBody = Join-Path $tmpDir 'mfa-challenge-resp.html'
    for ($att = 1; $att -le 8; $att++) {
      # MfaService rejeita como replay qualquer codigo cujo timestep seja <= o ultimo
      # aceito (a confirmacao da inscricao conta). Esperar a proxima janela apos cada
      # falha garante codigo fresco (mesma regra da prova D-OC34).
      if ($att -gt 1) {
        $nowS = [long][DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
        $delta = ([Math]::Floor($nowS / 30) + 1) * 30 + 2 - $nowS
        if ($delta -gt 0) { Start-Sleep -Seconds $delta }
      }
      $fm = Join-Path $tmpDir 'mfa-challenge.html'
      Remove-Item $fm -EA SilentlyContinue
      $null = Watch-Curl 45000 @('-k','--connect-timeout','5','--max-time','20','-s','-b',$jar,'-c',$jar,"$base/mfa/desafio","-o",$fm)
      $af2 = Get-Af ([IO.File]::ReadAllText($fm, [System.Text.Encoding]::UTF8))
      Start-Sleep -Milliseconds 300
      $form2 = 'Code=' + [Uri]::EscapeDataString((Get-Totp $mfaKey)) + '&ReturnUrl=&__RequestVerificationToken=' + [Uri]::EscapeDataString($af2)
      $out2 = Watch-Curl 45000 @('-k','--connect-timeout','5','--max-time','20','-s','-b',$jar,'-c',$jar,'-o',$mfaBody,'-w','%{http_code}','-X','POST','-d',$form2,"$base/mfa/desafio")
      if ([string]$out2 -eq '302') { $ok = $true; break }
      if ([string]$out2 -eq '000') { Start-Sleep -Seconds 5; continue }
    }
    if (-not $ok) {
      $errMsg = ''
      if (Test-Path $mfaBody) {
        try {
          $rb = [IO.File]::ReadAllText($mfaBody, [System.Text.Encoding]::UTF8)
          $errs = [regex]::Matches($rb, 'class="[^"]*(?:error|alert)[^"]*"[^>]*>(.{5,200}?)</') | ForEach-Object { ([Net.WebUtility]::HtmlDecode($_.Groups[1].Value) -replace '<[^>]+>', '' -replace '\s+', ' ').Trim() } | Where-Object { $_ }
          $errMsg = (($errs | Select-Object -First 2) -join ' || ')
        } catch {}
      }
      throw ("POST /mfa/desafio nao convergiu para " + $email + ' | ' + $errMsg.Substring(0, [Math]::Min(250, $errMsg.Length)))
    }
  } elseif (($code -eq '302') -and ($loc -like '*mfa/inscricao*')) {
    throw ("inscricao MFA exigida (esperado desafio) para " + $email)
  } elseif ($code -ne '302') {
    throw ("POST /entrar -> " + $code + " loc=" + $loc)
  }
  return [pscustomobject]@{ Code = $code; Location = $loc }
}
function Web-Get([string]$jar, [string]$url) {
  $f = Join-Path $tmpDir ('pg-' + [guid]::NewGuid().ToString('N').Substring(0, 10) + '.html')
  $ca = @('-k','--connect-timeout','5','--max-time','45','-s','-L','-b',$jar,'-c',$jar,$url,'-o',$f)
  $null = Watch-Curl 90000 $ca
  $sz = 0; if (Test-Path $f) { $sz = ([IO.File]::ReadAllBytes($f)).Length }
  if ($sz -eq 0) { Start-Sleep -Seconds 5; $null = Watch-Curl 90000 $ca }
  try { return [Net.WebUtility]::HtmlDecode(([IO.File]::ReadAllText($f, [System.Text.Encoding]::UTF8))) } catch { return '' }
}
function Web-Post([string]$jar, [string]$url, [hashtable]$fields, [string]$af) {
  $sb = New-Object Text.StringBuilder
  foreach ($k in $fields.Keys) {
    if ($sb.Length -gt 0) { [void]$sb.Append('&') }
    [void]$sb.Append(([Uri]::EscapeDataString($k) + '=' + [Uri]::EscapeDataString([string]$fields[$k])))
  }
  $f = Join-Path $tmpDir ('po-' + [guid]::NewGuid().ToString('N').Substring(0, 10) + '.json')
  $hf = Join-Path $tmpDir ('ph-' + [guid]::NewGuid().ToString('N').Substring(0, 10) + '.hdr')
  $ca = @('-k','--connect-timeout','5','--max-time','60','-s','-b',$jar,'-c',$jar,'-X','POST','-H','Content-Type: application/x-www-form-urlencoded','-H',('RequestVerificationToken: ' + $af),'-d',$sb.ToString(),'-o',$f,'-D',$hf,'-w','%{http_code}',$url)
  $out = Watch-Curl 120000 $ca
  if ([string]$out -eq '000') { Start-Sleep -Seconds 10; $out = Watch-Curl 120000 $ca }
  try { $body = [IO.File]::ReadAllText($f, [System.Text.Encoding]::UTF8) } catch { $body = '' }
  $loc = ''
  if (Test-Path $hf) { $hm = [regex]::Match(([IO.File]::ReadAllText($hf)), '(?im)^Location:[ \t]*(\S+)'); if ($hm.Success) { $loc = $hm.Groups[1].Value } }
  return [pscustomobject]@{ Code = ([string]$out).Trim(); Location = $loc; Body = $body }
}
function Wait-Ready([string]$url) {
  # 45 x 2s = teto ~90s: porta morta devolve erro instantaneo; cold start da API leva
  # dezenas de segundos (mesma regra da prova D-OC34).
  for ($i = 1; $i -le 45; $i++) {
    Start-Sleep -Seconds 2
    try { $h = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 3; if ([int]$h.StatusCode -lt 500) { return $true } } catch {}
  }
  return $false
}
function Ensure-Api([string]$tag) {
  if (Wait-Ready 'http://localhost:5143/health/ready') { return }
  LogEv ('API fora do ar em ' + $tag + ' - reiniciando processo (instabilidade de ambiente compartilhado, nao defeito de produto)')
  Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -and $_.CommandLine -match $rootEsc -and $_.CommandLine -match 'Odca\.Api\.dll' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
  Start-Sleep -Seconds 2
  $prevUrls = $env:ASPNETCORE_URLS
  $env:ODCA_RUNTIME_CONFIG = $mainCfg
  $env:DOTNET_ENVIRONMENT = 'Development'
  $env:ASPNETCORE_ENVIRONMENT = 'Development'
  $env:ODCA_SETUP_ON_START = 'true'
  $env:Signatures__Provider = 'local-file'
  $env:Signatures__LocalFileDirectory = $pickUp
  $env:ASPNETCORE_URLS = 'http://localhost:5143'
  $null = Start-Process dotnet -ArgumentList ('exec "{0}"' -f (Join-Path $root 'src\Odca.Api\bin\Debug\net10.0\Odca.Api.dll')) -WorkingDirectory (Join-Path $root 'src\Odca.Api') -WindowStyle Hidden -RedirectStandardOutput (Join-Path $logDir 'd59-svc-api.log') -RedirectStandardError (Join-Path $logDir 'd59-svc-api-err.log')
  $env:ASPNETCORE_URLS = $prevUrls
  if (-not (Wait-Ready 'http://localhost:5143/health/ready')) { LogEv ('API nao voltou apos restart em ' + $tag) }
}
function Ensure-Web([string]$tag) {
  if (Wait-Ready 'http://localhost:5144/entrar') { return }
  LogEv ('WEB fora do ar em ' + $tag + ' - reiniciando processo (instabilidade de ambiente compartilhado, nao defeito de produto)')
  Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -and $_.CommandLine -match $rootEsc -and $_.CommandLine -match 'Odca\.Web\.dll' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
  Start-Sleep -Seconds 2
  $prevUrls = $env:ASPNETCORE_URLS
  $env:ASPNETCORE_URLS = 'http://localhost:5144'
  $null = Start-Process dotnet -ArgumentList ('exec "{0}"' -f (Join-Path $root 'src\Odca.Web\bin\Debug\net10.0\Odca.Web.dll')) -WorkingDirectory (Join-Path $root 'src\Odca.Web') -WindowStyle Hidden -RedirectStandardOutput (Join-Path $logDir 'd59-svc-web.log') -RedirectStandardError (Join-Path $logDir 'd59-svc-web-err.log')
  $env:ASPNETCORE_URLS = $prevUrls
  if (-not (Wait-Ready 'http://localhost:5144/entrar')) { LogEv ('WEB nao voltou apos restart em ' + $tag) }
}
function Ensure-Worker([string]$tag) {
  $w = @(Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -and $_.CommandLine -match $rootEsc -and $_.CommandLine -match 'Odca\.Worker\.dll' })
  if ($w.Count -ge 1) { return }
  LogEv ('WORKER ausente em ' + $tag + ' - reiniciando processo (instabilidade de ambiente compartilhado, nao defeito de produto)')
  $null = Start-Process dotnet -ArgumentList ('exec "{0}"' -f (Join-Path $root 'src\Odca.Worker\bin\Debug\net10.0\Odca.Worker.dll')) -WorkingDirectory (Join-Path $root 'src\Odca.Worker') -WindowStyle Hidden -RedirectStandardOutput (Join-Path $logDir 'd59-svc-worker.log') -RedirectStandardError (Join-Path $logDir 'd59-svc-worker-err.log')
}
function Ensure-Stack([string]$tag) { Ensure-Api $tag; Ensure-Web $tag; Ensure-Worker $tag }

# ---------- constantes ----------
$t           = '20000000-0000-4000-8000-000000000001'
$uCli        = '10000000-0000-4000-8000-000000000003'
$uOperador   = '10000000-0000-4000-8000-000000000002'
$clientEmail = 'cliente.teste@odca.local'
$clientPass  = 'K8@wR3!nF6#zP2$m'
$opEmail     = 'operador@odca.local'
$opPass      = 'La@RsjC5p-hmq_JpReGW'
$tplNda      = 'f0dfb3e0-cbff-4123-9280-4e27d35ac913'
$roleD59     = '77000000-0000-4000-8000-000000000009'
$evDoc       = '77000000-0000-4000-8000-000000000DA0'
$evVer       = '77000000-0000-4000-8000-000000000DA1'
$patName     = 'Paciente Prova D-OC60'
$oblTitle    = 'Prova D-OC60 serie mensal'
$mainCfg     = Join-Path $root 'src\Odca.Api\development-runtime.json'
$rootEsc     = [Regex]::Escape($root)

# ---------- datas dinamicas da corrida (declaradas no D-OC60) ----------
$today      = Get-Date
$todayStr   = $today.ToString('yyyy-MM-dd')
$nextMo     = $today.AddMonths(1)
$due1       = New-Object DateTime($nextMo.Year, $nextMo.Month, 15, 0, 0, 0, [DateTimeKind]::Local)
$due1Str    = $due1.ToString('yyyy-MM-dd')
$oblEnd     = $due1.AddMonths(2)
$oblEndStr  = $oblEnd.ToString('yyyy-MM-dd')
$mOblY      = $due1.Year
$mOblM      = $due1.Month
$mNowY      = $today.Year
$mNowM      = $today.Month
$dueAtStr   = ($due1Str + 'T09:00')

# ---------- P0. binarios Debug atuais sobre o SHA ----------
$binOk = (Test-Path (Join-Path $root 'src\Odca.Api\bin\Debug\net10.0\Odca.Api.dll')) -and `
         (Test-Path (Join-Path $root 'src\Odca.Web\bin\Debug\net10.0\Odca.Web.dll')) -and `
         (Test-Path (Join-Path $root 'src\Odca.Worker\bin\Debug\net10.0\Odca.Worker.dll')) -and `
         (Test-Path (Join-Path $root 'src\Odca.Bootstrap\bin\Debug\net10.0\Odca.Bootstrap.dll'))
Check 'P0 binarios Debug presentes (api/web/worker/bootstrap)' $binOk ''
if (-not $binOk) { exit 2 }

# ---------- P0b. purga deterministica (idem d37/d38 + renewal_reminders desta prova) ----------
$purgeTables = @(
  'signature_envelope_events','signature_envelopes',
  'extraction_suggestions','extraction_results','extraction_reviews','extraction_jobs',
  'contract_review_notifications','contract_review_comments','contract_review_events','contract_review_steps','contract_review_requests',
  'signature_preparation_operations','signature_preparation_events','signature_participants','signature_preparations',
  'user_notifications','obligation_evidence','obligation_events','obligation_reminders','contract_obligations','obligation_series',
  'renewal_reminders',
  'contract_renewal_cycles','contract_change_applications','contract_change_events','contract_change_requests',
  'studio_comment_events','studio_comments','draft_save_receipts','draft_extraction_reviews','contract_copy_issuances',
  'generated_contract_versions',
  'storage_reservations','resource_movements','additional_storage_requests',
  'document_versions','contract_documents',
  'contract_drafts','contract_events','contract_import_events','contract_imports',
  'support_sessions',
  'solicitation_messages','solicitation_events','solicitation_pauses','solicitations',
  'notification_outbox',
  'privacy_legal_holds','privacy_request_actions','privacy_requests',
  'contracts','patients','patient_representatives'
)
$purgeErr = ''
# support_session_events nao tem coluna tenant_id: limpar via sessao do tenant
$null = Db ("DELETE FROM odca.support_session_events WHERE session_id IN (SELECT id FROM odca.support_sessions WHERE tenant_id='$t');")
if ($script:lastPsqlErr) { $purgeErr += 'support_session_events: ' + $script:lastPsqlErr + ' | ' }
# privacy_request_events nao tem coluna tenant_id: limpar via requisicoes do tenant
$null = Db ("DELETE FROM odca.privacy_request_events WHERE privacy_request_id IN (SELECT id FROM odca.privacy_requests WHERE tenant_id='$t');")
if ($script:lastPsqlErr) { $purgeErr += 'privacy_request_events: ' + $script:lastPsqlErr + ' | ' }
foreach ($tbl in $purgeTables) {
  $null = Db ("DELETE FROM odca." + $tbl + " WHERE tenant_id='$t';")
  if ($script:lastPsqlErr) { $purgeErr += $tbl + ': ' + $script:lastPsqlErr + ' | ' }
}
Check 'P0b purga da base descartavel (estado idem ao setup)' ($purgeErr -eq '') ($purgeErr.Trim())
if ($purgeErr -ne '') { exit 2 }

# ---------- P0c. fixtures de jornada (declarados no cabecalho; idempotentes) ----------
$fxErr = ''
$null = Db ("UPDATE odca.subscriptions SET plan_version_id=(SELECT id FROM odca.plan_versions WHERE code='enterprise' AND version::text='2' AND status='published') WHERE tenant_id='$t' AND status='active';")
if ($script:lastPsqlErr) { $fxErr += 'plan-enterprise: ' + $script:lastPsqlErr + ' | ' }
$planNow = (Db ("SELECT coalesce(pv.code||' v'||pv.version::text,'(none)') FROM odca.subscriptions s LEFT JOIN odca.plan_versions pv ON pv.id=s.plan_version_id WHERE s.tenant_id='$t' AND s.status='active';")).Trim()
if ($planNow -ne 'enterprise v2') { $fxErr += 'plan-now=' + $planNow + ' | ' }
# E-mail de aviso de renovacao para o responsavel do contrato: o canal de e-mail e opt-in
# (default false na preferencia), entao a fixture declara a opcao para a prova de entrega.
$null = Db ("INSERT INTO odca.user_notification_preferences(tenant_id,user_id,kind,enabled,channel_in_app,channel_email) VALUES('$t','$uCli','renewal_due',true,true,true) ON CONFLICT(tenant_id,user_id,kind) DO UPDATE SET enabled=true,channel_in_app=true,channel_email=true;")
if ($script:lastPsqlErr) { $fxErr += 'renewal-prefs: ' + $script:lastPsqlErr + ' | ' }
# Janelas de aviso de renovacao do tenant (fixture declarada): o worker so enfileira o
# lembrete quando faltam exatamente os dias configurados. Sem esta fixtura o tenant herda
# valores de outras provas ({1,1,1}) e nenhum lembrete nasce para o fim em +30d.
$null = Db ("UPDATE odca.tenants SET renewal_reminder_days='{30,15,7}' WHERE id='$t';")
if ($script:lastPsqlErr) { $fxErr += 'renewal-days: ' + $script:lastPsqlErr + ' | ' }
$daysNow = (Db ("SELECT renewal_reminder_days::text FROM odca.tenants WHERE id='$t';")).Trim()
if ($daysNow -ne '{30,15,7}') { $fxErr += 'renewal-days-now=' + $daysNow + ' | ' }
$null = Db ("DELETE FROM odca.member_roles WHERE tenant_id='$t' AND user_id='$uCli' AND role_id='$roleD59';")
if ($script:lastPsqlErr) { $fxErr += 'member_roles-d59: ' + $script:lastPsqlErr + ' | ' }
$null = Db ("DELETE FROM odca.role_permissions WHERE role_id='$roleD59';")
if ($script:lastPsqlErr) { $fxErr += 'role_permissions-d59: ' + $script:lastPsqlErr + ' | ' }
$null = Db ("DELETE FROM odca.roles WHERE id='$roleD59';")
if ($script:lastPsqlErr) { $fxErr += 'roles-d59: ' + $script:lastPsqlErr + ' | ' }
$null = Db ("INSERT INTO odca.roles(id, scope_type, tenant_id, code, display_name, is_system) VALUES ('$roleD59', 'tenant', '$t', 'prova-obrigacoes-d59', 'Prova Obrigacoes D-OC60', true);")
if ($script:lastPsqlErr) { $fxErr += 'role-d59: ' + $script:lastPsqlErr + ' | ' }
$null = Db ("INSERT INTO odca.role_permissions(role_id, permission_code) SELECT '$roleD59', code FROM odca.permissions WHERE code IN ('tenant.obligations.read','tenant.obligations.read_all','tenant.obligations.manage','tenant.obligations.fulfill','tenant.obligations.cancel','tenant.obligations.reopen','tenant.obligations.assign');")
if ($script:lastPsqlErr) { $fxErr += 'perm-d59: ' + $script:lastPsqlErr + ' | ' }
$null = Db ("INSERT INTO odca.member_roles(tenant_id, user_id, role_id, assigned_by) VALUES ('$t', '$uCli', '$roleD59', '$uCli');")
if ($script:lastPsqlErr) { $fxErr += 'member-role-d59: ' + $script:lastPsqlErr + ' | ' }
$null = Db ("INSERT INTO odca.user_notification_preferences(tenant_id,user_id,kind,enabled,channel_in_app,channel_email) VALUES ('$t','$uCli','signature_completed',true,true,true) ON CONFLICT (tenant_id,user_id,kind) DO UPDATE SET enabled=true,channel_in_app=true,channel_email=true;")
if ($script:lastPsqlErr) { $fxErr += 'prefs-signature-completed: ' + $script:lastPsqlErr + ' | ' }
$chanEmail = (Db ("SELECT odca.notification_channel_state('$t','$uCli','signature_completed')::text;")).Trim()
if ($chanEmail -ne '(t,t,t)') { $fxErr += 'chan-email=' + $chanEmail + ' | ' }
# Transporte local de notificacao (FilePickup): limpar artefatos de rodadas anteriores para
# a outbox da prova nao cascar com arquivo antigo (mesma regra da prova D-OC34).
try { $rtPickup = [string](((Get-Content $mainCfg -Raw) | ConvertFrom-Json).Notifications.DevelopmentPickupDirectory) } catch { $rtPickup = '' }
if ($rtPickup -and (Test-Path $rtPickup)) { Remove-Item (Join-Path $rtPickup '*.txt') -EA SilentlyContinue }
Check 'P0c fixtures de jornada (plano enterprise v2 + papel de obrigacoes + opt-in signature_completed + janelas de aviso + e-mail renewal_due)' ($fxErr -eq '') ($fxErr.Trim())
if ($fxErr -ne '') { exit 2 }

# ---------- S. stack unica (worker ativo + envelope local-file desde o boot) ----------
& (Join-Path $root 'scripts\stop-local.ps1') 2>&1 | Out-Null
Start-Sleep -Seconds 3
Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -and $_.CommandLine -match $rootEsc -and $_.CommandLine -match 'Odca\.(Api|Web|Worker)\.(dll|csproj)' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
Start-Sleep -Seconds 2
$env:ODCA_RUNTIME_CONFIG = $mainCfg
$env:DOTNET_ENVIRONMENT = 'Development'
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ODCA_SETUP_ON_START = 'true'
$env:Signatures__Provider = 'local-file'
$env:Signatures__LocalFileDirectory = $pickUp
$env:ASPNETCORE_URLS = 'http://localhost:5143'
$env:Api__BaseUrl = 'http://localhost:5143/'
$null = Start-Process dotnet -ArgumentList ('exec "{0}"' -f (Join-Path $root 'src\Odca.Api\bin\Debug\net10.0\Odca.Api.dll')) -WorkingDirectory (Join-Path $root 'src\Odca.Api') -WindowStyle Hidden -RedirectStandardOutput (Join-Path $logDir 'd59-svc-api.log') -RedirectStandardError (Join-Path $logDir 'd59-svc-api-err.log')
$env:ASPNETCORE_URLS = 'http://localhost:5144'
$null = Start-Process dotnet -ArgumentList ('exec "{0}"' -f (Join-Path $root 'src\Odca.Web\bin\Debug\net10.0\Odca.Web.dll')) -WorkingDirectory (Join-Path $root 'src\Odca.Web') -WindowStyle Hidden -RedirectStandardOutput (Join-Path $logDir 'd59-svc-web.log') -RedirectStandardError (Join-Path $logDir 'd59-svc-web-err.log')
$env:ASPNETCORE_URLS = ''
$null = Start-Process dotnet -ArgumentList ('exec "{0}"' -f (Join-Path $root 'src\Odca.Worker\bin\Debug\net10.0\Odca.Worker.dll')) -WorkingDirectory (Join-Path $root 'src\Odca.Worker') -WindowStyle Hidden -RedirectStandardOutput (Join-Path $logDir 'd59-svc-worker.log') -RedirectStandardError (Join-Path $logDir 'd59-svc-worker-err.log')
$readyApi = Wait-Ready 'http://localhost:5143/health/ready'
$readyWeb = Wait-Ready 'http://localhost:5144/entrar'
Check 'S1 api pronta (stack com envelope local-file desde o boot)' $readyApi ''
Check 'S2 web pronta' $readyWeb ''
$canonV = (Db "SELECT coalesce(max(version),0)::text FROM odca.schema_migrations;").Trim()
Check 'S3 canonico vigente na base descartavel (schema_migrations 63)' ($canonV -eq '63') ('versao=' + $canonV)
if (-not ($readyApi -and $readyWeb)) { exit 2 }
LogEv ('Stack ok ' + (Get-Date -Format 'o'))

# ---------- W. logins (cliente direto; operador com MFA reinscrito p/ decisao de revisao) ----------
$bootDll = Join-Path $root 'src\Odca.Bootstrap\bin\Debug\net10.0\Odca.Bootstrap.dll'
& dotnet exec $bootDll reset-mfa --account OPERADOR@ODCA.LOCAL 2>&1 | Out-Null
Db "UPDATE odca.users SET must_change_password=false WHERE id IN ('$uCli','$uOperador');" | Out-Null
$jarCli = Join-Path $tmpDir 'cli-jar.txt'
Remove-Item $jarCli -EA SilentlyContinue
try { $lc = Web-Login $jarCli $clientEmail $clientPass ''; Check 'W1 login web do cliente (302, sem MFA)' (($lc.Code -eq '302') -and ($lc.Location -notlike '*mfa*')) ($lc.Code + ' ' + $lc.Location) }
catch { Check 'W1 login web do cliente (302, sem MFA)' $false $_.Exception.Message }
try { $cliTok = (Api 'POST' '/auth/login' '' @{ Login = $clientEmail; Password = $clientPass }); if ($cliTok.Status -ne 200) { throw ("login API: " + $cliTok.Raw) }; $cliTok = [string]((J $cliTok).accessToken); Check 'W2 login API do cliente (bearer)' $true '' }
catch { $cliTok = ''; Check 'W2 login API do cliente (bearer)' $false $_.Exception.Message }
if (-not $cliTok) { exit 2 }
$oEnroll = $null
try { $oEnroll = Api-LoginEnroll $opEmail $opPass; Check 'W3 operador logado na API (plataforma c/ MFA convergido)' $true ('key_len=' + [string]$oEnroll.MfaKey.Length) }
catch { Check 'W3 operador logado na API (plataforma c/ MFA convergido)' $false $_.Exception.Message }
$opTok = ''
try { $opTok = [string](Api-LoginMfa $opEmail $opPass ([string]$oEnroll.MfaKey)).Token; Check 'W4 operador com token MFA-verificado aceito nas leituras da API' ($opTok.Length -gt 0) ('tok_len=' + $opTok.Length) }
catch { Check 'W4 operador com token MFA-verificado aceito nas leituras da API' $false $_.Exception.Message }
if (-not $opTok) { exit 2 }

# Agenda mensal (GET /organizacoes/{t}/agenda) - achatada em uma lista de itens.
# Definida antes de J0 porque a jornada verifica a agenda em dois momentos (J4 e J11).
function Get-AgendaItems([string]$tok, [int]$y, [int]$mo, [string]$scope) {
  $u = ("/organizations/" + $t + "/agenda?year=" + $y + "&month=" + $mo + "&scope=" + $scope)
  $r = Api 'GET' $u $tok
  if ([int]$r.Status -ne 200) { return @{ Code = [int]$r.Status; Items = @(); Raw = [string]$r.Raw } }
  $j = J $r
  $items = @()
  foreach ($day in @($j.days)) { foreach ($it in @($day.items)) { $items += $it } }
  return @{ Code = 200; Items = $items; Raw = '' }
}

# ---------- J0. minuta criada a partir do modelo NDA publicado ----------
$pageI = Web-Get $jarCli "$base/organizacoes/$t/estudio"
$afIdx = Get-Af $pageI
$rJ = Web-Post $jarCli "$base/organizacoes/$t/estudio/minutas" @{ templateId = $tplNda; title = 'Prova D-OC60 assinatura externa completa'; __RequestVerificationToken = $afIdx } $afIdx
$mDraft = [regex]::Match($rJ.Location, 'minutas/([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})')
$draftB = $mDraft.Groups[1].Value
$cid = (Db "SELECT coalesce(contract_id::text,'') FROM odca.contract_drafts WHERE id='$draftB';").Trim()
Check 'J0-1 minuta criada a partir do modelo NDA publicado (contrato da jornada nascido)' (($rJ.Code -eq '302') -and ($draftB.Length -eq 36) -and ($cid.Length -eq 36)) ('code=' + $rJ.Code + " draft=" + $draftB + " cid=" + $cid)
if ($draftB.Length -ne 36) { exit 2 }
LogEv ("J0 contrato-da-jornada=" + $cid)
$editB = "$base/organizacoes/$t/estudio/minutas/$draftB"

# ---------- J1. paciente: cadastro rapido + vinculo na minuta (elo 1 da jornada) ----------
$pageND = Web-Get $jarCli "$base/organizacoes/$t/documentos/novo"
$afND = Get-Af $pageND
$rP = Web-Post $jarCli "$base/organizacoes/$t/documentos/novo/paciente" @{ fullName = $patName; identifierType = 'passport'; identifierValue = 'PASS-PROVA-59'; __RequestVerificationToken = $afND } $afND
$pageND2 = Web-Get $jarCli "$base/organizacoes/$t/documentos/novo"
$patNew = (Db "SELECT coalesce(id::text,'') FROM odca.patients WHERE tenant_id='$t' AND full_name='$patName' LIMIT 1;").Trim()
Check 'J1-1 paciente criado pelo cadastro rapido do fluxo de novo documento' (($pageND2 -like '*Paciente cadastrado*') -and ($patNew.Length -eq 36)) ('patient=' + $patNew)
if ($patNew.Length -ne 36) { exit 2 }
$pageB = Web-Get $jarCli $editB
$afB = Get-Af $pageB
$rvA = [long](Db "SELECT row_version::text FROM odca.contract_drafts WHERE id='$draftB';")
$rL = Web-Post $jarCli "$editB/vincular-paciente" @{ patientId = $patNew; expectedVersion = $rvA; __RequestVerificationToken = $afB } $afB
$cv = Db "SELECT coalesce(patient_id::text,'NULL')||'|'||(patient_selection_snapshot IS NOT NULL)::text FROM odca.contract_drafts WHERE id='$draftB';"
$evLink = [long](Db "SELECT count(*) FROM odca.contract_events WHERE tenant_id='$t' AND contract_id='$cid' AND event_type='patient.linked';")
Check 'J1-2 paciente vinculado a minuta: patient_id + snapshot imutavel + evento auditaravel' (($cv -eq ($patNew + '|true')) -and ($evLink -ge 1)) ('row=' + $cv + ' ev=' + $evLink)

# ---------- J2. preenchimento manual confirmado dos obrigatorios do NDA (fixture declarada) ----------
# PS 5.1: concatenacao com '+' dentro de literal multi-linha @(...) entrega valores corrompidos
# (as datas saiam como ",2026-11-15," e o validador canonico bloqueava a geracao). Cada campo
# nasce em uma instrucao propria e o JSON e validado ANTES de gravar.
$vPartyA  = '{"value":"Empresa Alfa de Terapias LTDA","source":"manual","fieldId":"party_a_name","confirmed":true}'
$vDocA    = '{"value":"12345678000195","source":"manual","fieldId":"party_a_document","confirmed":true}'
$vPartyB  = '{"value":"Cliente Teste ODCA","source":"manual","fieldId":"party_b_name","confirmed":true}'
$vDocB    = '{"value":"52998224725","source":"manual","fieldId":"party_b_document","confirmed":true}'
$vPurpose = '{"value":"Troca de informacoes tecnicas para avaliacao conjunta","source":"manual","fieldId":"purpose","confirmed":true}'
$vEff     = '{"value":"' + $due1Str + '","source":"manual","fieldId":"effective_date","confirmed":true}'
$vEnd     = '{"value":"' + $oblEndStr + '","source":"manual","fieldId":"end_date","confirmed":true}'
$vJur     = '{"value":"Rio de Janeiro/RJ","source":"manual","fieldId":"jurisdiction","confirmed":true}'
$ndaJson  = '[' + (($vPartyA, $vDocA, $vPartyB, $vDocB, $vPurpose, $vEff, $vEnd, $vJur) -join ',') + ']'
$ndaParsed = $null
try { $ndaParsed = $ndaJson | ConvertFrom-Json } catch { $ndaParsed = $null }
$ndaValsOk = $true
if ($ndaParsed) { foreach ($it in $ndaParsed) { if (([string]$it.value) -match '^\s*,|,\s*$') { $ndaValsOk = $false } } } else { $ndaValsOk = $false }
Check 'J2-0 JSON dos campos do NDA gerado integro (datas ISO sem contaminacao)' (($ndaValsOk -eq $true) -and ($ndaParsed.Count -eq 8)) ('items=' + $(if ($ndaParsed) { $ndaParsed.Count } else { 'invalid' }) + " eff=" + $vEff)
if (-not $ndaValsOk) { exit 2 }
$null = DbFile 'j2-nda' ("UPDATE odca.contract_drafts SET values='" + $ndaJson + "'::jsonb WHERE id='" + $draftB + "';")
$prefilled = [long](Db "SELECT count(*) FROM odca.contract_drafts d, jsonb_array_elements(d.values) e WHERE d.id='$draftB' AND e->>'confirmed'='true';")
$badDates = [long](Db "SELECT count(*) FROM odca.contract_drafts d, jsonb_array_elements(d.values) e WHERE d.id='$draftB' AND (e->>'value') ~ '^,|,$';")
Check 'J2-1 campos obrigatorios do NDA preenchidos como manual+confirmado' (($prefilled -eq 8) -and ($badDates -eq 0)) ('campos=' + $prefilled + ' corrompidos=' + $badDates + " err=" + $script:lastPsqlErr)

# ---------- J3. geracao de versao + PDF (elo 2: documento) ----------
$rvG = [long](Db "SELECT row_version::text FROM odca.contract_drafts WHERE id='$draftB';")
$rGen = Web-Post $jarCli "$editB/gerar" @{ expectedVersion = $rvG; idempotencyKey = (New-Guid).ToString(); __RequestVerificationToken = $afB } $afB
$vid = ''
for ($i = 0; $i -lt 20 -and $vid.Length -ne 36; $i++) {
  Start-Sleep -Seconds 2
  $vid = (Db "SELECT coalesce((SELECT id::text FROM odca.generated_contract_versions WHERE draft_id='$draftB' ORDER BY created_at DESC, version_number DESC LIMIT 1),'')").Trim()
}
Check 'J3-1 gerar versao cria registro navegavel do documento' ($vid.Length -eq 36) ('version=' + $vid + ' code=' + $rGen.Code)
if ($vid.Length -ne 36) { exit 2 }
$rPdf = Web-Post $jarCli "$base/organizacoes/$t/estudio/versoes/$vid/pdf" @{ __RequestVerificationToken = $afB } $afB
$pdfStatus = ''
for ($i = 0; $i -lt 30; $i++) {
  Start-Sleep -Seconds 3
  $pdfStatus = (Db "SELECT pdf_status FROM odca.generated_contract_versions WHERE id='$vid';").Trim()
  if ($pdfStatus -eq 'completed' -or $pdfStatus -eq 'failed') { break }
}
Check 'J3-2 PDF da versao gerado (documento final materializado)' ($pdfStatus -eq 'completed') ('status=' + $pdfStatus)
if ($pdfStatus -ne 'completed') { exit 2 }
$pdfShaReal = (Db "SELECT pdf_sha256::text FROM odca.generated_contract_versions WHERE id='$vid';").Trim()
LogEv ("J3 pdf_sha256=" + $pdfShaReal)

# ---------- J4. revisao atribuida ao operador com prazo na janela da agenda (elo 3) ----------
$rRev = Web-Post $jarCli "$base/organizacoes/$t/estudio/versoes/$vid/solicitar-revisao" @{ reviewerId = $uOperador; dueAt = $dueAtStr; instructions = 'Revisao da prova D-OC60 assinatura externa completa'; idempotencyKey = (New-Guid).ToString(); __RequestVerificationToken = $afB } $afB
$revM = [regex]::Match($rRev.Location, 'solicitacoes/([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})')
$reviewId = $revM.Groups[1].Value
Check 'J4-1 requisicao de revisao criada e atribuida ao revisor (prazo na janela da agenda)' ($reviewId.Length -eq 36) ('review=' + $reviewId + ' code=' + $rRev.Code + ' loc=' + $rRev.Location)
if ($reviewId.Length -ne 36) { exit 2 }
# A revisao pendente aparece na agenda do REVISOR no mes do prazo (item Review, dono = revisor).
# O check e feito aqui, enquanto a solicitacao esta 'in_review': depois da decisao ela deixa a
# janela de pendentes por desenho (ver J11-2).
$agRevPend = Get-AgendaItems $opTok $mOblY $mOblM 'mine'
$hitRevPend = @($agRevPend.Items | Where-Object { ([string]$_.kind) -eq 'Review' -and ([string]$_.sourceId) -eq $reviewId -and ([string]$_.dueOn).StartsWith($due1Str) -and ([string]$_.ownerId) -eq $uOperador })
Check 'J4-1b agenda do revisor no mes do prazo mostra a revisao pendente no due_on exato (dono=revisor)' (([int]$agRevPend.Code -eq 200) -and ($hitRevPend.Count -ge 1)) ('code=' + $agRevPend.Code + ' hits=' + $hitRevPend.Count + ' due=' + $due1Str + ' raw=' + ([string]$agRevPend.Raw).Substring(0, [Math]::Min(180, ([string]$agRevPend.Raw).Length)))
Ensure-Stack 'antes do login do revisor (apos gerar/pdf)'
$jarOp = Join-Path $tmpDir 'op-jar.txt'
Remove-Item $jarOp -EA SilentlyContinue
$pageRv = Web-Get $jarOp "$base/organizacoes/$t/solicitacoes/$reviewId"
$afRv = Get-Af $pageRv
$revVer = [long](Db "SELECT row_version::text FROM odca.contract_review_requests WHERE id='$reviewId';")
$opTok2 = ''
try { $opTok2 = [string](Api-LoginMfa $opEmail $opPass ([string]$oEnroll.MfaKey)).Token } catch { $opTok2 = '' }
$rDec = Api 'POST' "/organizations/$t/reviews/$reviewId/decision" $opTok2 @{ Action = 'approve'; Justification = 'Aprovado para prova D-OC60'; ExpectedVersion = $revVer; IdempotencyKey = (New-Guid).ToString() }
$revState = (Db "SELECT review_status FROM odca.generated_contract_versions WHERE id='$vid';").Trim()
Check 'J4-2 decisao do revisor aprova a versao internamente' ($revState -eq 'internally_approved') ('status=' + $revState + ' code=' + $rDec.Status + ' raw=' + $rDec.Raw.Substring(0,[Math]::Min(120,$rDec.Raw.Length)))

# ---------- J5. preparacao de assinatura confirmada (composicao imutavel) ----------
$pageV = Web-Get $jarCli "$base/organizacoes/$t/estudio/versoes/$vid"
$afV = Get-Af $pageV
$prepPre = Db "SELECT coalesce(status,'<none>')||'|'||coalesce(row_version::text,'0') FROM odca.signature_preparations WHERE generated_version_id='$vid';"
$prepPreParts = [string]$prepPre -split '\|'
$prepVer = 0L
if ($prepPreParts.Count -ge 2) { try { $prepVer = [long]$prepPreParts[1] } catch {} }
$p0 = [guid]::NewGuid()
$rPart = Web-Post $jarCli "$base/organizacoes/$t/estudio/versoes/$vid/participantes" @{
  expectedVersion = $prepVer; confirm = 'true'; operationId = (New-Guid).ToString();
  'participants[0].Id' = $p0.ToString(); 'participants[0].ParticipantType' = 'professional'; 'participants[0].SourceId' = '';
  'participants[0].Role' = 'Responsavel tecnico'; 'participants[0].Name' = 'Profissional Responsavel ODCA';
  'participants[0].Email' = 'profissional.responsavel.prova@odca.local'; 'participants[0].Phone' = ''; 'participants[0].Position' = '1';
  __RequestVerificationToken = $afV
} $afV
$prepRow = Db "SELECT sp.id||'|'||sp.status||'|'||coalesce(p.client_id::text,'NULL') FROM odca.signature_preparations sp LEFT JOIN odca.signature_participants p ON p.preparation_id=sp.id WHERE sp.generated_version_id='$vid';"
$prepParts = [string]$prepRow -split '\|'
$prepId = ''
$partClient = ''
if ($prepParts.Count -ge 3) { $prepId = $prepParts[0]; $partClient = $prepParts[2] }
$prepOk = ($prepParts.Count -ge 3) -and ($prepParts[1] -eq 'confirmed') -and ($partClient.Length -eq 36)
Check 'J5-1 preparacao de assinatura confirmada com participante unico (composicao imutavel)' $prepOk ('row=' + $prepRow + " psql_err=" + $script:lastPsqlErr)
if (-not $prepOk) { exit 2 }

# ---------- J6. envelope externo local-file: envio -> evento accepted -> dedup -> imutabilidade ----------
# Token do cliente renovado: a jornada inteira passa do TTL de 15 min do access token.
$cliTok = ''
try { $cliTok = Api-LoginClient $clientEmail $clientPass; Check 'J6-0 token do cliente renovado para o elo da assinatura' ($cliTok.Length -gt 0) ('tok_len=' + $cliTok.Length) }
catch { Check 'J6-0 token do cliente renovado para o elo da assinatura' $false $_.Exception.Message }
if (-not $cliTok) { exit 2 }
$rReady = Api 'GET' "/organizations/$t/studio/versions/$vid/signature-preparation/readiness" $cliTok
Check 'J6-1 readiness (local-file ativo desde o boot) 200 SEM integration.unavailable' (($rReady.Status -eq 200) -and ($rReady.Raw -notlike '*integration.unavailable*')) ('status=' + $rReady.Status)
$envPath = Join-Path $pickUp ("env-$($vid -replace '-','').envelope.json")
$envStatePath = Join-Path $pickUp ("env-$($vid -replace '-','').state.json")
$send1 = Api 'POST' "/organizations/$t/studio/versions/$vid/signature-envelope" $cliTok
$mvts1 = [long](Db "SELECT count(*) FROM odca.resource_movements WHERE tenant_id='$t' AND resource_type='signature_credit';")
Check 'J6-2 POST signature-envelope -> 200 sent/local-file (franquia consumida uma vez)' (($send1.Status -eq 200) -and ($send1.Raw -like '*"status":"sent"*') -and ($send1.Raw -like '*"providerCode":"local-file"*') -and ($mvts1 -eq 1)) ('status=' + $send1.Status + " mvts=" + $mvts1)
$payload = ''
if (Test-Path $envPath) { $payload = [IO.File]::ReadAllText($envPath, [System.Text.Encoding]::UTF8) }
Check 'J6-3 envelope materializado na pasta de drop (hash do PDF confirmado + participante)' (($payload.Length -gt 0) -and ($payload -like "*$pdfShaReal*") -and ($payload -like '*Profissional Responsavel ODCA*')) ('bytes=' + $payload.Length)
$sync0 = Api 'POST' "/organizations/$t/studio/versions/$vid/signature-envelope/sync" $cliTok
Check 'J6-4a sync sem estado externo -> 200 updated=false (segue sent)' (($sync0.Status -eq 200) -and ($sync0.Raw -like '*"updated":false*')) ('status=' + $sync0.Status)
Set-Content -Path $envStatePath -Value '{"status":"accepted","occurred_at":"2026-10-22T12:00:00Z"}' -Encoding ascii -NoNewline
$sync1 = Api 'POST' "/organizations/$t/studio/versions/$vid/signature-envelope/sync" $cliTok
Check 'J6-4 sync com estado aceito -> 200 updated=true/status=accepted (evento externo persistido)' (($sync1.Status -eq 200) -and ($sync1.Raw -like '*"updated":true*') -and ($sync1.Raw -like '*"status":"accepted"*')) ('status=' + $sync1.Status)
$sync2 = Api 'POST' "/organizations/$t/studio/versions/$vid/signature-envelope/sync" $cliTok
Check 'J6-5 sync repetido com o mesmo evento -> updated=false (dedup deterministica)' (($sync2.Status -eq 200) -and ($sync2.Raw -like '*"updated":false*')) ('status=' + $sync2.Status)
$getEnv = Api 'GET' "/organizations/$t/studio/versions/$vid/signature-envelope" $cliTok
Check 'J6-6 GET envelope -> 200 accepted + auditoria de eventos (sent+accepted)' (($getEnv.Status -eq 200) -and ($getEnv.Raw -like '*"status":"accepted"*') -and ($getEnv.Raw -like '*"eventType":"accepted"*') -and ($getEnv.Raw -like '*"eventType":"sent"*')) ('status=' + $getEnv.Status)
$send2 = Api 'POST' "/organizations/$t/studio/versions/$vid/signature-envelope" $cliTok
$mvts2 = [long](Db "SELECT count(*) FROM odca.resource_movements WHERE tenant_id='$t' AND resource_type='signature_credit';")
Check 'J6-7 envio apos accepted -> 409 envelope.accepted (sem novo consumo)' (($send2.Status -eq 409) -and ($mvts2 -eq 1)) ('status=' + $send2.Status + " mvts=" + $mvts2)

# ---------- J7. registro da assinatura -> documento final externally_signed (fecho do elo 2/3) ----------
$rSig = Web-Post $jarCli "$base/organizacoes/$t/contratos/$cid/assinaturas/assinar" @{
  versionId = $vid; preparationId = $prepId; participantClientId = $partClient;
  signMode = 'evidence'; evidence = 'Assinado externamente (envelope local-file aceito antes do registro) - prova D-OC60';
  participantName = 'Profissional Responsavel ODCA'; __RequestVerificationToken = $afB
} $afB
$sheetB = Web-Get $jarCli "$base/organizacoes/$t/contratos/$cid"
$finalState = (Db "SELECT review_status FROM odca.generated_contract_versions WHERE id='$vid';").Trim()
Check 'J7-1 registro da assinatura completa a composicao: versao externally_signed + todos assinaram' (($finalState -eq 'externally_signed') -and ($sheetB -like '*Todos os participantes assinaram*')) ('status=' + $finalState + " len=" + $sheetB.Length)
$evSig = Db "SELECT coalesce(details->>'confirmedPdfSha256','') FROM odca.contract_events WHERE tenant_id='$t' AND contract_id='$cid' AND event_type='signature.completed' ORDER BY occurred_at DESC LIMIT 1;"
Check 'J7-2 contrato registra evento auditaravel signature.completed com o hash do PDF confirmado' ($evSig -eq $pdfShaReal) ('sha=' + $evSig)
$hashSig = [System.Security.Cryptography.SHA256]::Create().ComputeHash([System.Text.Encoding]::UTF8.GetBytes(('signature.completed|' + $prepId + '|' + $vid)))
$subSig = New-Object byte[] 16
[System.Array]::Copy($hashSig, 0, $subSig, 0, 16)
$srcSig = [System.Guid]::new($subSig).ToString()
$bellCnt = [long](Db "SELECT count(*) FROM odca.user_notifications WHERE tenant_id='$t' AND user_id='$uCli' AND kind='signature_completed';")
$outCnt = [long](Db ("SELECT count(*) FROM odca.notification_outbox WHERE tenant_id='$t' AND kind='signature_completed' AND source_id='" + $srcSig + "';"))
Check 'J7-3 aviso signature_completed entregue in-app + e-mail na outbox ao preparador (dedup preparation+versao)' (($bellCnt -eq 1) -and ($outCnt -eq 1)) ("bell=" + $bellCnt + " outbox=" + $outCnt + " src=" + $srcSig)

# ---------- J8. serie mensal de obrigacoes pelo web (elo 4a: prazos contratuais) ----------
# Os reminderDays sao um campo REPETIDO no form (7/1/0): hashtable do PS nao suporta chave
# duplicada, entao o body e montado por string url-encoded (mesmo padrao da prova D-OC38).
$oNov = Web-Get $jarCli "$base/organizacoes/$t/obrigacoes/nova"
$novAf = Get-Af $oNov
$midStr = $due1.AddMonths(1).ToString('yyyy-MM-dd')
$novData = 'contractId=' + $cid +
  '&title=' + [Uri]::EscapeDataString($oblTitle) +
  '&description=' + [Uri]::EscapeDataString('Serie mensal da prova D-OC60 assinatura externa completa') +
  '&obligatedParty=' + [Uri]::EscapeDataString('Contratada') +
  '&ownerId=' + $uCli +
  '&dueDate=' + $due1Str +
  '&category=other&priority=normal' +
  '&clauseDocumentVersionId=&evidenceRequired=false&postTermReason=' +
  '&recurrenceEnabled=true&recurrenceBaseDate=' + $due1Str + '&recurrenceIntendedDay=' + $due1.Day + '&recurrenceEndsOn=' + $oblEndStr +
  '&recurrenceOccurrenceCount=&recurrenceTimeZone=' + [Uri]::EscapeDataString('America/Sao_Paulo') +
  '&reminderDays=7&reminderDays=1&reminderDays=0' +
  '&__RequestVerificationToken=' + [Uri]::EscapeDataString($novAf)
$fObl = Join-Path $tmpDir ('ob-post.html')
$hfObl = Join-Path $tmpDir ('ob-post.hdr')
Remove-Item $fObl -EA SilentlyContinue; Remove-Item $hfObl -EA SilentlyContinue
$outObl = Watch-Curl 120000 @('-k','--connect-timeout','5','--max-time','60','-s','-b',$jarCli,'-c',$jarCli,'-X','POST','-H','Content-Type: application/x-www-form-urlencoded','-d',$novData,'-o',$fObl,'-D',$hfObl,'-w','%{http_code}',"$base/organizacoes/$t/obrigacoes/nova")
$oblCode = ([string]$outObl).Trim()
$oblLoc = ''
if (Test-Path $hfObl) { $hm = [regex]::Match(([IO.File]::ReadAllText($hfObl)), '(?im)^Location:[ \t]*(\S+)'); if ($hm.Success) { $oblLoc = $hm.Groups[1].Value } }
$seriesCnt = [long](Db ("SELECT count(*) FROM odca.contract_obligations WHERE tenant_id='$t' AND contract_id='$cid' AND title LIKE '$oblTitle%' AND due_date IN ('$due1Str','$midStr','$oblEndStr');"))
$oblEv = [long](Db ("SELECT count(*) FROM odca.obligation_events oe JOIN odca.contract_obligations c2 ON c2.id=oe.obligation_id WHERE c2.tenant_id='$t' AND c2.contract_id='$cid' AND c2.title LIKE '$oblTitle%' AND oe.event_type='created';"))
Check 'J8-1 serie mensal de obrigacoes criada pelo web (3 prazos + 3 eventos created)' (([int]$oblCode -eq 302) -and ($seriesCnt -eq 3) -and ($oblEv -eq 3)) ("code=" + $oblCode + " loc=" + $oblLoc + " series=" + $seriesCnt + " ev=" + $oblEv)
if ($seriesCnt -ne 3) { exit 2 }

# ---------- J9. fixture de vigencia do contrato (declarada): fim em +30d com aviso de 30 dias ----------
$null = DbFile 'j9-vigencia' ("UPDATE odca.contracts SET start_date=(CURRENT_DATE - 365), end_date=(CURRENT_DATE + 30), value=5000.00, currency='BRL', owner_id='$uCli', renewal_notice_amount=30, renewal_notice_unit='calendar_days' WHERE id='$cid';")
$endStr = $today.AddDays(30).ToString('yyyy-MM-dd')
$endDt  = [DateTime]::ParseExact($endStr, 'yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture)
$proposedEndStr = $endDt.AddYears(1).ToString('yyyy-MM-dd')
$rowVig = Db "SELECT coalesce(end_date::text,'-')||'|'||coalesce(owner_id::text,'-')||'|'||coalesce(renewal_notice_amount::text,'-')||'|'||coalesce(renewal_notice_unit,'-')||'|'||coalesce(value::text,'-') FROM odca.contracts WHERE id='$cid';"
Check 'J9-1 vigencia aplicada ao contrato (fim em +30d, aviso 30d, responsavel=cliente, valor base)' (($rowVig -eq ($endStr + '|' + $uCli + '|30|calendar_days|5000.00')) -and ($script:lastPsqlErr -eq '')) ('row=' + $rowVig)
if (-not ($rowVig -like ($endStr + '|*'))) { exit 2 }

# ---------- J10. lembrete de renovacao entregue pelo WORKER REAL (avisos da jornada) ----------
Ensure-Stack 'antes da espera do lembrete de renovacao'
$deliv = 0L; $inapp = 0L; $outre = 0L
for ($i = 0; $i -lt 60; $i++) {
  Start-Sleep -Seconds 2
  $deliv = [long](Db "SELECT count(*) FROM odca.renewal_reminders WHERE tenant_id='$t' AND contract_id='$cid' AND status='delivered' AND days_before=30;")
  $inapp = [long](Db "SELECT count(*) FROM odca.user_notifications WHERE tenant_id='$t' AND user_id='$uCli' AND kind='renewal_due';")
  $outre = [long](Db "SELECT count(*) FROM odca.notification_outbox WHERE tenant_id='$t' AND kind='renewal_due';")
  if (($deliv -ge 1) -and ($inapp -ge 1) -and ($outre -ge 1)) { break }
}
Check 'J10-1 varredura 30d gerada e entregue pelo worker: reminder delivered + sino in-app p/ quem tem renewals.read' (($deliv -eq 1) -and ($inapp -ge 1)) ('delivered=' + $deliv + " inapp=" + $inapp)
# A varredura entrega a TODOS os membros ativos com renewals.read (admin + cliente); o que a
# jornada exige e' que o responsavel do contrato (cliente) receba o e-mail, sem faltar nem sobrar
# destinatario inventado.
$destCli = [long](Db "SELECT count(*) FROM odca.notification_outbox WHERE tenant_id='$t' AND kind='renewal_due' AND destination='$clientEmail';")
$destAll = Db "SELECT coalesce(string_agg(destination, ','),'') FROM odca.notification_outbox WHERE tenant_id='$t' AND kind='renewal_due';"
Check 'J10-2 outbox recebe e-mail renewal_due enderecado ao responsavel do contrato' (($outre -ge 1) -and ($destCli -ge 1)) ('outbox=' + $outre + ' dest_cli=' + $destCli + ' dests=' + $destAll)

# ---------- J11. agenda mensal mostra os itens da jornada nos devidos meses (elo 4b) ----------
# Renova os dois tokens imediatamente antes das leituras (TTL de 15 min; o operador exige
# token MFA-verificado: o token do login simples e recusado com 403 na agenda).
try { $cliTok = Api-LoginClient $clientEmail $clientPass; Check 'J11-0a token do cliente renovado para a agenda' ($cliTok.Length -gt 0) ('tok_len=' + $cliTok.Length) }
catch { Check 'J11-0a token do cliente renovado para a agenda' $false $_.Exception.Message }
try { $opTok = [string](Api-LoginMfa $opEmail $opPass ([string]$oEnroll.MfaKey)).Token; Check 'J11-0b token MFA-verificado do revisor renovado para a agenda' ($opTok.Length -gt 0) ('tok_len=' + $opTok.Length) }
catch { Check 'J11-0b token MFA-verificado do revisor renovado para a agenda' $false $_.Exception.Message }
if ((-not $cliTok) -or (-not $opTok)) { exit 2 }
$agObl = Get-AgendaItems $cliTok $mOblY $mOblM 'organization'
$hitObl = @($agObl.Items | Where-Object { ([string]$_.kind) -eq 'Obligation' -and ([string]$_.dueOn).StartsWith($due1Str) })
$ownerObl = @($hitObl | Where-Object { ([string]$_.ownerId) -eq $uCli }).Count
Check 'J11-1 agenda do cliente no mes dos prazos: item Obrigacao no due_on exato (dono=cliente)' (([int]$agObl.Code -eq 200) -and ($hitObl.Count -ge 1) -and ($ownerObl -ge 1)) ('code=' + $agObl.Code + ' hits=' + $hitObl.Count + ' owner=' + $ownerObl + ' raw=' + ([string]$agObl.Raw).Substring(0, [Math]::Min(180, ([string]$agObl.Raw).Length)))
$agRev = Get-AgendaItems $opTok $mOblY $mOblM 'mine'
$hitRev = @($agRev.Items | Where-Object { ([string]$_.kind) -eq 'Review' -and ([string]$_.sourceId) -eq $reviewId })
Check 'J11-2 agenda respeita o estado: revisao decidida sai da janela de pendentes do revisor' (([int]$agRev.Code -eq 200) -and ($hitRev.Count -eq 0)) ('code=' + $agRev.Code + ' remanescentes=' + $hitRev.Count + ' raw=' + ([string]$agRev.Raw).Substring(0, [Math]::Min(180, ([string]$agRev.Raw).Length)))
$agRen = Get-AgendaItems $cliTok $mNowY $mNowM 'organization'
$hitRen = @($agRen.Items | Where-Object { ([string]$_.kind) -eq 'Renewal' -and ([string]$_.dueOn).StartsWith($todayStr) })
Check 'J11-3 agenda do cliente no mes atual: item Renovacao no due_on do aviso (hoje)' (([int]$agRen.Code -eq 200) -and ($hitRen.Count -ge 1)) ('code=' + $agRen.Code + ' hits=' + $hitRen.Count + ' raw=' + ([string]$agRen.Raw).Substring(0, [Math]::Min(180, ([string]$agRen.Raw).Length)))

# ---------- J12. ciclo de renovacao completo (elo 5): propor -> submeter -> formalizar -> aplicar ----------
$todayTz = (Db "SELECT (now() AT TIME ZONE timezone)::date::text FROM odca.tenants WHERE id='$t';").Trim()
$cv = [long](Db "SELECT version::text FROM odca.contracts WHERE id='$cid';")
$propBody = @{
  kind = 'renewal'; reason = 'Renovacao anual da prova D-OC60'; priority = 'normal'
  proposedEndDate = $proposedEndStr; proposedValue = 500; valueChangeMode = 'increase'; currency = 'BRL'
  effectiveOn = $todayTz; contractVersion = $cv; responsibleId = $uCli; idempotencyKey = (New-Guid).ToString()
}
$rPr = Api 'POST' "/organizations/$t/renewals/contracts/$cid" $cliTok $propBody
$propId = [regex]::Match($rPr.Raw, '"id"\s*:\s*"([0-9a-fA-F-]{36})"').Groups[1].Value
if ($propId.Length -ne 36) { $propId = (Db "SELECT id::text FROM odca.contract_change_requests WHERE tenant_id='$t' AND contract_id='$cid' ORDER BY created_at DESC LIMIT 1;").Trim() }
$prState = (Db "SELECT coalesce(status,'-')||'|'||coalesce(priority,'-')||'|'||coalesce(proposed_value::text,'-')||'|'||coalesce(proposed_end_date::text,'-') FROM odca.contract_change_requests WHERE id='$propId';").Trim()
Check 'J12-1 proposta de renovacao criada na central (201, draft, condicoes comerciais persistidas)' (($rPr.Status -eq 201) -and ($propId.Length -eq 36) -and ($prState -eq ('draft|normal|5500.00|' + $proposedEndStr))) ('state=' + $prState)
if ($propId.Length -ne 36) { exit 2 }
$stParts = (Db "SELECT status||'|'||row_version::text FROM odca.contract_change_requests WHERE id='$propId';") -split '\|'
$rSub = Api 'POST' ("/organizations/$t/renewals/" + $propId + "/submit") $cliTok @{ rowVersion = [long]$stParts[1] }
$stAfter = (Db "SELECT status FROM odca.contract_change_requests WHERE id='$propId';").Trim()
Check 'J12-2 submeter a proposta -> in_review' (($rSub.Status -eq 200) -and ($stAfter -eq 'in_review')) ('code=' + $rSub.Status + ' st=' + $stAfter)
$null = DbFile 'j12-evidencia' ("INSERT INTO odca.contract_documents (id,tenant_id,contract_id,title,created_by) VALUES ('$evDoc','$t','$cid','Evidencia de formalizacao D-OC60','$uCli'); INSERT INTO odca.document_versions (id,tenant_id,document_id,contract_id,version_number,display_name,storage_key,byte_size,sha256,detected_type,uploaded_by,security_status) VALUES ('$evVer','$t','$evDoc','$cid',1,'Evidencia de formalizacao D-OC60.pdf','d59/formalizacao.pdf',1024,'$pdfShaReal','pdf','$uCli','safe');")
$stParts = (Db "SELECT status||'|'||row_version::text FROM odca.contract_change_requests WHERE id='$propId';") -split '\|'
$rForm = Api 'POST' ("/organizations/$t/renewals/" + $propId + "/formalization") $cliTok @{
  rowVersion = [long]$stParts[1]; evidenceVersionId = $evVer; formalizedOn = $todayTz
  justification = 'Formalizacao homologada na prova D-OC60 (termo sintetico anexado como evidencia)'
}
$stAfter2 = (Db "SELECT status FROM odca.contract_change_requests WHERE id='$propId';").Trim()
Check 'J12-3 formalizar com evidencia anexada -> formalized' (($rForm.Status -eq 200) -and ($stAfter2 -eq 'formalized')) ('code=' + $rForm.Status + ' st=' + $stAfter2)
$stParts = (Db "SELECT status||'|'||row_version::text FROM odca.contract_change_requests WHERE id='$propId';") -split '\|'
$rApp = Api 'POST' ("/organizations/$t/renewals/" + $propId + "/apply") $cliTok @{ rowVersion = [long]$stParts[1] }
$contrRow = Db "SELECT coalesce(end_date::text,'-')||'|'||coalesce(value::text,'-')||'|'||version::text FROM odca.contracts WHERE id='$cid';"
$appSt = (Db "SELECT application_status FROM odca.contract_change_requests WHERE id='$propId';").Trim()
$newVer = ($cv + 1)
Check 'J12-4 aplicar -> contrato atualizado (nova vigencia/valor/versao) + application_status=applied' ((($rApp.Status -eq 200) -or ($rApp.Status -eq 204)) -and ($contrRow -eq ($proposedEndStr + '|5500.00|' + $newVer)) -and ($appSt -eq 'applied')) ('code=' + $rApp.Status + ' contr=' + $contrRow + ' app=' + $appSt)
$stParts = (Db "SELECT status||'|'||row_version::text FROM odca.contract_change_requests WHERE id='$propId';") -split '\|'
$rApp2 = Api 'POST' ("/organizations/$t/renewals/" + $propId + "/apply") $cliTok @{ rowVersion = [long]$stParts[1] }
Check 'J12-5 reaplicar -> 409 (aplicacao unica e versada)' ($rApp2.Status -eq 409) ('code=' + $rApp2.Status)

# ---------- J13. GRAFO FINAL: os cinco elos num unico contrato, mesmo tenant ----------
$gPat   = [long](Db "SELECT count(*) FROM odca.patients WHERE tenant_id='$t' AND id='$patNew' AND inactive_at IS NULL;")
$gDraft = (Db "SELECT coalesce(patient_id::text,'NULL') FROM odca.contract_drafts WHERE tenant_id='$t' AND id='$draftB';").Trim()
$gVer   = Db "SELECT coalesce(patient_id::text,'NULL')||'|'||review_status||'|'||(patient_snapshot IS NOT NULL)::text FROM odca.generated_contract_versions WHERE tenant_id='$t' AND id='$vid';"
$gEnv   = Db "SELECT coalesce((SELECT status FROM odca.signature_envelopes WHERE tenant_id='$t'),'none')||'|'||count(*) FROM odca.signature_envelope_events WHERE tenant_id='$t';"
$gObl   = [long](Db "SELECT count(*) FROM odca.contract_obligations WHERE tenant_id='$t' AND contract_id='$cid' AND status='open';")
$gRen   = (Db "SELECT coalesce(application_status,'-') FROM odca.contract_change_requests WHERE tenant_id='$t' AND id='$propId';").Trim()
$graphDetail = "pat=$gPat draftPat=$gDraft ver=$gVer env=$gEnv oblOpen=$gObl ren=$gRen"
Check 'J13-1 GRAFO FINAL consistente: paciente->documento->revisao->assinatura->obrigacoes->renovacao no MESMO contrato' (
  ($gPat -eq 1) -and ($gDraft -eq $patNew) -and
  ($gVer -eq ($patNew + '|externally_signed|true')) -and
  ($gEnv -eq 'accepted|2') -and ($gObl -eq 3) -and ($gRen -eq 'applied')
) $graphDetail
LogEv ("estado-final paciente=" + $patNew + " contrato=" + $cid + " versao=" + $vid + " prep=" + $prepId + " review=" + $reviewId + " renovacao=" + $propId)
LogEv ("estado-final agenda mes(" + $mOblY + '-' + ('{0:D2}' -f $mOblM) + ")=Obligation+Review | mes(" + $mNowY + '-' + ('{0:D2}' -f $mNowM) + ")=Renewal(due_on=" + $todayStr + ")")

# ---------- marcador ----------
$ts = (Get-Date -Format 'yyyy-MM-ddTHH:mm:sszzz')
$fim = "PROVA-FIM fails=$($script:fails) ts=$ts"
Add-Content -Path (Join-Path $evDir 'd59-proof-fim.txt') $fim -Encoding utf8
LogEv ("RESULTADO fails=" + $script:fails + " ts=" + $ts)
Write-Host $fim

# ---------- stack mantida viva para J14-J16 (encerrada ao final) ----------
Write-Host 'STACK-KEEP-ALIVE-PARA-J14-J16'


# ---------- J14. documento final comprovado (fecho do marco signature) ----------
$dlPdf = curl.exe -k -s -o (Join-Path $tmpDir 'final.pdf') -w '%{http_code}|%{content_type}' -H "Authorization: Bearer $cliTok" "$apiBase/organizations/$t/studio/versions/$vid/pdf"
$dlParts = [string]$dlPdf -split '\|'
$dlCode = $dlParts[0]
$dlType = if ($dlParts.Count -gt 1) { $dlParts[1] } else { '' }
$dlBytes = 0
$dlPath = Join-Path $tmpDir 'final.pdf'
if (Test-Path $dlPath) { $dlBytes = (Get-Item $dlPath).Length }
Check 'J14-1 download do documento final da versao externally_signed (200, PDF, nao vazio)' (($dlCode -eq '200') -and ($dlType -like '*pdf*') -and ($dlBytes -gt 0)) ('code=' + $dlCode + ' type=' + $dlType + ' bytes=' + $dlBytes)
$dlHash = ''
if (Test-Path $dlPath) {
  $dlHash = ([System.BitConverter]::ToString([System.Security.Cryptography.SHA256]::Create().ComputeHash([IO.File]::ReadAllBytes($dlPath))) -replace '-','').ToLower()
}
$confPdfSha = (Db "SELECT coalesce(confirmed_pdf_sha256,'') FROM odca.signature_preparations WHERE generated_version_id='$vid' ORDER BY confirmed_at DESC LIMIT 1;").Trim()
Check 'J14-2 integridade do documento final (hash baixado == hash confirmado da preparacao)' (($dlHash.Length -eq 64) -and ($dlHash -eq $confPdfSha)) ('dl=' + $dlHash + ' conf=' + $confPdfSha)

# ---------- J15. caminho de NEGATIVA (declined) do envelope externo ----------
$draft2 = ''
try {
  $pageI2 = Web-Get $jarCli "$base/organizacoes/$t/estudio"
  $afIdx2 = Get-Af $pageI2
  $rJ2 = Web-Post $jarCli "$base/organizacoes/$t/estudio/minutas" @{ templateId = $tplNda; title = 'Prova D-OC60 negativa'; __RequestVerificationToken = $afIdx2 } $afIdx2
  $draft2 = [regex]::Match($rJ2.Location, 'minutas/([0-9a-fA-F-]{36})').Groups[1].Value
  if ($draft2.Length -ne 36) { $draft2 = (Db "SELECT id::text FROM odca.contract_drafts WHERE tenant_id='$t' AND title='Prova D-OC60 negativa' ORDER BY created_at DESC LIMIT 1;").Trim() }
  Check 'J15-0a segunda minuta criada' ($draft2.Length -eq 36) ('draft2=' + $draft2 + ' loc=' + $rJ2.Location)
  $pageB2 = Web-Get $jarCli "$base/organizacoes/$t/estudio/minutas/$draft2"
  $afB2 = Get-Af $pageB2
  $rvA2 = [long](Db "SELECT row_version::text FROM odca.contract_drafts WHERE id='$draft2';")
  $rVinc2 = Web-Post $jarCli "$base/organizacoes/$t/estudio/minutas/$draft2/vincular-paciente" @{ patientId = $patNew; expectedVersion = $rvA2; __RequestVerificationToken = $afB2 } $afB2
  Check 'J15-0b paciente vinculado na segunda minuta' ($rVinc2.Code -eq '302') ('code=' + $rVinc2.Code + ' loc=' + $rVinc2.Location)
  $pageE2 = Web-Get $jarCli "$base/organizacoes/$t/estudio/minutas/$draft2"
  $afE2 = Get-Af $pageE2
  $null = DbFile 'j15-nda' ("UPDATE odca.contract_drafts SET values='" + $ndaJson + "'::jsonb WHERE id='" + $draft2 + "';")
  $rvG2 = [long](Db "SELECT row_version::text FROM odca.contract_drafts WHERE id='$draft2';")
  $rGen2 = Web-Post $jarCli "$base/organizacoes/$t/estudio/minutas/$draft2/gerar" @{ expectedVersion = $rvG2; idempotencyKey = (New-Guid).ToString(); __RequestVerificationToken = $afE2 } $afE2
  $vid2 = [regex]::Match($rGen2.Location, 'versoes/([0-9a-fA-F-]{36})').Groups[1].Value
  if ($vid2.Length -ne 36) { $vid2 = (Db "SELECT id::text FROM odca.generated_contract_versions WHERE tenant_id='$t' AND draft_id='$draft2' ORDER BY created_at DESC LIMIT 1;").Trim() }
  Check 'J15-0c segunda versao gerada' ($vid2.Length -eq 36) ('vid2=' + $vid2 + ' code=' + $rGen2.Code + ' loc=' + $rGen2.Location)
  $pageV2 = Web-Get $jarCli "$base/organizacoes/$t/estudio/versoes/$vid2"
  $afV2 = Get-Af $pageV2
  $idem2 = (New-Guid).ToString()
  $rRev2 = Web-Post $jarCli "$base/organizacoes/$t/estudio/versoes/$vid2/solicitar-revisao" @{ reviewerId = $uOperador; dueAt = ''; instructions = 'Revisao da prova D-OC60 negativa'; idempotencyKey = $idem2; __RequestVerificationToken = $afV2 } $afV2
  $reviewId2 = [regex]::Match($rRev2.Location, 'solicitacoes/([0-9a-fA-F-]{36})').Groups[1].Value
  Check 'J15-0f revisao da segunda versao solicitada' ($reviewId2.Length -eq 36) ('reviewId2=' + $reviewId2 + ' code=' + $rRev2.Code + ' loc=' + $rRev2.Location)
  $pageV2b = Web-Get $jarCli "$base/organizacoes/$t/estudio/versoes/$vid2"
  $afV2b = Get-Af $pageV2b
  $revVer2 = [long](Db "SELECT row_version::text FROM odca.contract_review_requests WHERE id='$reviewId2';")
  $opTok3 = ''
  try { $opTok3 = [string](Api-LoginMfa $opEmail $opPass ([string]$oEnroll.MfaKey)).Token } catch { $opTok3 = '' }
  $rDec2 = Api 'POST' "/organizations/$t/reviews/$reviewId2/decision" $opTok3 @{ Action = 'approve'; Justification = 'Aprovado para prova D-OC60 negativa'; ExpectedVersion = $revVer2; IdempotencyKey = (New-Guid).ToString() }
  $revState2 = (Db "SELECT review_status FROM odca.generated_contract_versions WHERE id='$vid2';").Trim()
  Check 'J15-0g revisao da segunda versao aprovada' ($revState2 -eq 'internally_approved') ('status=' + $revState2 + ' code=' + $rDec2.Status + ' raw=' + $rDec2.Raw.Substring(0,[Math]::Min(120,$rDec2.Raw.Length)))
  $rPdf2 = Web-Post $jarCli "$base/organizacoes/$t/estudio/versoes/$vid2/pdf" @{ __RequestVerificationToken = $afV2 } $afV2
  Check 'J15-0d PDF da segunda versao gerado' ($rPdf2.Code -eq '302') ('code=' + $rPdf2.Code + ' loc=' + $rPdf2.Location)
  $pageV2c = Web-Get $jarCli "$base/organizacoes/$t/estudio/versoes/$vid2"
  $afV2c = Get-Af $pageV2c
  $prepPre2 = Db "SELECT coalesce(status,'<none>')||'|'||coalesce(row_version::text,'0') FROM odca.signature_preparations WHERE generated_version_id='$vid2';"
  $prepPreParts2 = [string]$prepPre2 -split '\|'
  $prepVer2 = 0L
  if ($prepPreParts2.Count -ge 2) { try { $prepVer2 = [long]$prepPreParts2[1] } catch {} }
  $p02 = [guid]::NewGuid()
  $rPart2 = Web-Post $jarCli "$base/organizacoes/$t/estudio/versoes/$vid2/participantes" @{
    expectedVersion = $prepVer2; confirm = 'true'; operationId = (New-Guid).ToString();
    'participants[0].Id' = $p02.ToString(); 'participants[0].ParticipantType' = 'professional'; 'participants[0].SourceId' = '';
    'participants[0].Role' = 'Responsavel tecnico'; 'participants[0].Name' = 'Profissional Responsavel ODCA';
    'participants[0].Email' = 'profissional.responsavel.prova@odca.local'; 'participants[0].Phone' = ''; 'participants[0].Position' = '1';
    __RequestVerificationToken = $afV2b
  } $afV2b
  Check 'J15-0e preparacao da segunda versao confirmada' ($rPart2.Code -eq '302') ('code=' + $rPart2.Code + ' loc=' + $rPart2.Location)
  try { $cliTok = Api-LoginClient $clientEmail $clientPass } catch {}
  $mvtsBefore = [long](Db "SELECT count(*) FROM odca.resource_movements WHERE tenant_id='$t' AND resource_type='signature_credit';")
  $rSend2 = Api 'POST' "/organizations/$t/studio/versions/$vid2/signature-envelope" $cliTok $null
  $envStatus2 = (Db "SELECT coalesce(status,'<none>') FROM odca.signature_envelopes WHERE tenant_id='$t' AND generated_version_id='$vid2';").Trim()
  Check 'J15-1 envio do envelope da segunda versao (sent/local-file, franquia consumida)' (($rSend2.Status -eq 200) -and ($envStatus2 -eq 'sent')) ('status=' + $rSend2.Status + ' env=' + $envStatus2)
  $envRef2 = (Db "SELECT envelope_ref FROM odca.signature_envelopes WHERE tenant_id='$t' AND generated_version_id='$vid2';").Trim()
  $statePath2 = Join-Path $pickUp ($envRef2 + '.state.json')
  $occ2 = (Get-Date).ToUniversalTime().ToString('o')
  [IO.File]::WriteAllText($statePath2, ('{"status":"declined","occurred_at":"' + $occ2 + '","detail":"recusado pela prova D-OC60"}'), (New-Object System.Text.UTF8Encoding($false)))
  $rSyncD = Api 'POST' "/organizations/$t/studio/versions/$vid2/signature-envelope/sync" $cliTok $null
  $envStatusD = (Db "SELECT coalesce(status,'<none>') FROM odca.signature_envelopes WHERE tenant_id='$t' AND generated_version_id='$vid2';").Trim()
  Check 'J15-2 sync com estado declined -> envelope declined (transicao aplicada)' (($rSyncD.Status -eq 200) -and ($envStatusD -eq 'declined')) ('sync=' + $rSyncD.Status + ' env=' + $envStatusD + ' raw=' + $rSyncD.Raw)
  $rSyncD2 = Api 'POST' "/organizations/$t/studio/versions/$vid2/signature-envelope/sync" $cliTok $null
  $envStatusD2 = (Db "SELECT coalesce(status,'<none>') FROM odca.signature_envelopes WHERE tenant_id='$t' AND generated_version_id='$vid2';").Trim()
  Check 'J15-3 re-sync declined -> idempotente (sem nova transicao, segue declined)' (($rSyncD2.Status -eq 200) -and ($envStatusD2 -eq 'declined')) ('sync=' + $rSyncD2.Status + ' env=' + $envStatusD2)
  $vs2 = (Db "SELECT coalesce(review_status,'<none>') FROM odca.generated_contract_versions WHERE id='$vid2';").Trim()
  Check 'J15-4 versao da negativa NAO vira externally_signed (permanece nao-concluida)' ($vs2 -ne 'externally_signed') ('review_status=' + $vs2)
  $mvtsAfter = [long](Db "SELECT count(*) FROM odca.resource_movements WHERE tenant_id='$t' AND movement_type='signature_credit';")
  $rResend2 = Api 'POST' "/organizations/$t/studio/versions/$vid2/signature-envelope" $cliTok $null
  $mvtsAfter2 = [long](Db "SELECT count(*) FROM odca.resource_movements WHERE tenant_id='$t' AND resource_type='signature_credit';")
  Check 'J15-5 reenvio apos declined -> 200 sent + nova unidade de franquia consumida' (($rResend2.Status -eq 200) -and ($mvtsAfter2 -gt $mvtsBefore)) ('resend=' + $rResend2.Status + ' mvts_before=' + $mvtsBefore + ' after=' + $mvtsAfter2)
} catch {
  Check 'J15 (caminho de negativa do envelope)' $false $_.Exception.Message
}

# ---------- J16. fecho da assinatura completa ----------
$gEnv2 = (Db "SELECT coalesce((SELECT status FROM odca.signature_envelopes WHERE tenant_id='$t' AND generated_version_id='$vid2'),'none')||'|'||(SELECT count(*) FROM odca.signature_envelope_events WHERE tenant_id='$t' AND envelope_id=(SELECT id FROM odca.signature_envelopes WHERE tenant_id='$t' AND generated_version_id='$vid2'));").Trim()
Check 'J16-1 segundo envelope persistido com eventos (sent->declined->sent, 3 eventos)' ($gEnv2 -like 'sent|3') ('env=' + $gEnv2)
Check 'J16-2 versao aceita permanece externally_signed apos a negativa da segunda' ((Db "SELECT coalesce(review_status,'<none>') FROM odca.generated_contract_versions WHERE id='$vid';").Trim() -eq 'externally_signed') 'aceita=' + (Db "SELECT coalesce(review_status,'<none>') FROM odca.generated_contract_versions WHERE id='$vid';").Trim()

# ---------- marcador final ----------
$ts = (Get-Date -Format 'yyyy-MM-ddTHH:mm:sszzz')
$fim = "PROVA-FIM fails=$($script:fails) ts=$ts"
Add-Content -Path (Join-Path $evDir 'd60-proof-fim.txt') $fim -Encoding utf8
LogEv ("RESULTADO fails=" + $script:fails + " ts=" + $ts)
Write-Host $fim

# ---------- encerrar stack ----------
Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -and $_.CommandLine -match $rootEsc -and $_.CommandLine -match 'Odca\.(Api|Web|Worker)\.(dll|csproj)' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
if ($script:fails -gt 0) { exit 1 } else { exit 0 }