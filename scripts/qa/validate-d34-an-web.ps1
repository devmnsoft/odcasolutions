# D-OC34 - prova ao vivo da jornada documental do piloto de estreia (itens A-N).
# Base descartavel odca_test_disposable; web :5144/:7144; api :5143/:7143; worker ativo.
# Fase de execucao: stack -> diagnostico superadmin (A) -> F1/B.5 -> B.6/B.7 -> B.8 ->
# jornada de assinatura completa (J+B.10+B.9) -> OCR honesto (F2/X/T/N).
param([switch]$TraceA1)
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
$logDir = 'C:\Users\NCELL-DEV-020\AppData\Local\Temp\opencode'
$root   = 'C:\MNSOFT\odcasolutions'
$evDir  = Join-Path $logDir 'evidence'
$tmpDir = Join-Path $logDir 'd34-fx'
New-Item -ItemType Directory -Force -Path $evDir | Out-Null
New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
$evidence = Join-Path $evDir 'd34-proof-a.txt'
Set-Content -Path $evidence '' -Encoding utf8
$traceFile = Join-Path $logDir 'd34-a1-trace.log'
Set-Content -Path $traceFile '' -Encoding utf8
$global:d34Ticker = Start-Job -ArgumentList $traceFile { param($tf) while ($true) { Start-Sleep -Seconds 15; Add-Content -Path $tf -Value ((Get-Date -Format 'yyyy-MM-dd HH:mm:ss.fff') + ' ALIVE-TICKER') -Encoding utf8 } }
$script:fails = 0
$script:lastPsqlErr = ''

function TMark([string]$tag) { $l = ((Get-Date -Format 'yyyy-MM-dd HH:mm:ss.fff') + ' ' + $tag); Add-Content -Path $traceFile $l -Encoding utf8; Write-Host $l }
function Quote-CmdArg([string]$a) { if ($a -match '[ \t"\\]') { return ('"' + ($a -replace '"','\"') + '"') } return $a }
function Watch-Curl([int]$ms, [string[]]$ca) {
  $sw0 = Get-Date
  TMark ('WC-inicio n_args=' + @($ca).Count)
  $psi = New-Object System.Diagnostics.ProcessStartInfo
  $psi.FileName = 'curl.exe'
  $psi.Arguments = ((@($ca) | ForEach-Object { Quote-CmdArg ([string]$_) }) -join ' ')
  $psi.UseShellExecute = $false
  $psi.RedirectStandardOutput = $true
  $psi.RedirectStandardError = $true
  $psi.WorkingDirectory = $tmpDir
  $psi.CreateNoWindow = $true
  $p = $null
  try { $p = [System.Diagnostics.Process]::Start($psi) } catch { TMark ('WC-START-FALHOU ' + $_.Exception.Message); return '000' }
  $deadline = $sw0.AddMilliseconds($ms + 8000)
  while (-not $p.HasExited) {
    if ((Get-Date) -ge $deadline) {
      try { $p.Kill() } catch {}
      TMark ('WC-KILL apos_ms=' + ([int]((Get-Date) - $sw0).TotalMilliseconds))
      break
    }
    Start-Sleep -Milliseconds 300
  }
  try { $so = $p.StandardOutput.ReadToEnd() } catch { $so = '' }
  try { $p.WaitForExit() } catch {}
  $ec = '?'; try { $ec = $p.ExitCode } catch {}
  TMark ('WC-done exit=' + $ec + ' apos_ms=' + ([int]((Get-Date) - $sw0).TotalMilliseconds))
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
  $f = Join-Path $tmpDir ('d34-sql-' + $tag + '.sql')
  [IO.File]::WriteAllText($f, $sql, (New-Object Text.UTF8Encoding($false)))
  $ef = Join-Path $tmpDir ('d34-sql-' + $tag + '.err')
  $out = & 'C:\Program Files\PostgreSQL\18\bin\psql.exe' -h 127.0.0.1 -U postgres -d odca_test_disposable -v ON_ERROR_STOP=1 -f $f 2>$ef | Out-String
  $script:lastPsqlErr = ((Get-Content $ef -EA SilentlyContinue) -join ' ')
  return $out.Trim()
}
function Ensure-SleepExe {
  $dir = Join-Path $logDir 'd34-bin'
  $exe = Join-Path $dir 'd34sleep.exe'
  $hangs = $false
  if (Test-Path $exe) {
    try {
      $p = Start-Process $exe -PassThru -WindowStyle Hidden
      Start-Sleep -Seconds 3
      $hangs = (-not $p.HasExited)
      Stop-Process -Id $p.Id -Force -EA SilentlyContinue
    } catch {}
  }
  if ($hangs) { return }
  LogEv 'Ensure-SleepExe: binario ausente ou sem pendurar — reconstruindo via dotnet'
  New-Item -ItemType Directory -Force -Path $dir | Out-Null
  $src = Join-Path $logDir 'd34sleep-src'
  Remove-Item -Recurse -Force $src -EA SilentlyContinue
  New-Item -ItemType Directory -Force -Path $src | Out-Null
  Set-Content (Join-Path $src 'Program.cs') 'class P { static void Main() { System.Threading.Thread.Sleep(int.MaxValue); } }' -Encoding ascii
  Set-Content (Join-Path $src 'd34sleep.csproj') '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><Nullable>disable</Nullable><ImplicitUsings>disable</ImplicitUsings><AssemblyName>d34sleep</AssemblyName></PropertyGroup></Project>' -Encoding ascii
  Push-Location $src
  try { & dotnet build -c Release -v q --nologo 2>&1 | Select-Object -Last 3 | ForEach-Object { LogEv ('  d34sleep build: ' + $_) } } finally { Pop-Location }
  foreach ($n in @('d34sleep.exe', 'd34sleep.dll', 'd34sleep.runtimeconfig.json')) {
    Copy-Item (Join-Path $src ('bin\Release\net10.0\' + $n)) (Join-Path $dir $n) -Force
  }
  LogEv 'Ensure-SleepExe: reconstruido'
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
    if ($bitsLeft -ge 8) {
      $output.Add([byte](($buffer -shr ($bitsLeft - 8)) -band 0xFF))
      $bitsLeft -= 8
    }
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
$apiBase = 'http://localhost:5143/api/v1'
function Api([string]$method, [string]$path, [string]$tok, $body = $null) {
  $url = "$apiBase$path"
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
    if ($_.Exception.Response) {
      $code = [int]$_.Exception.StatusCode
      $reader = New-Object IO.StreamReader($_.Exception.Response.GetResponseStream(), [Text.Encoding]::UTF8)
      $raw = $reader.ReadToEnd()
    } else { $code = 0; $raw = $_.Exception.Message }
    return @{ Status = $code; Raw = $raw }
  }
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
$base = 'http://localhost:5144'
function Get-Af([string]$html) {
  if ($html -match 'name="__RequestVerificationToken"[^>]*value="([^"]+)"') { return $Matches[1] }
  if ($html -match 'value="([^"]+)"[^>]*name="__RequestVerificationToken"') { return $Matches[1] }
  return ''
}
function Web-Login([string]$jar, [string]$email, [string]$pass, [string]$mfaKey) {
  TMark ('WL-inicio ' + $email)
  $fh = Join-Path $tmpDir 'login-page.html'
  for ($g = 1; $g -le 3; $g++) {
    Remove-Item $fh -EA SilentlyContinue
    $null = Watch-Curl 45000 @('-k','--connect-timeout','5','--max-time','20','-s','-c',$jar,"$base/entrar","-o",$fh)
    $sz = 0; if (Test-Path $fh) { $sz = ([IO.File]::ReadAllBytes($fh)).Length }
    if ($sz -gt 500) { TMark ('WL-GET-entrar-ok bytes=' + $sz); break }
    TMark ('WL-GET-entrar tent=' + $g + ' sem resposta; aguardando 15s')
    Start-Sleep -Seconds 15
  }
  $af = Get-Af ([IO.File]::ReadAllText($fh, [Text.Encoding]::UTF8))
  TMark ('WL-af-len af_len=' + $af.Length)
  $form = 'Login=' + [Uri]::EscapeDataString($email) + '&Password=' + [Uri]::EscapeDataString($pass) + '&ReturnUrl=&__RequestVerificationToken=' + [Uri]::EscapeDataString($af)
  $hdrf = Join-Path $tmpDir 'login-post.hdr'
  $out = '000'
  for ($po = 1; $po -le 3; $po++) {
    Remove-Item $hdrf -EA SilentlyContinue
    $out = Watch-Curl 45000 @('-k','--connect-timeout','5','--max-time','20','-s','-b',$jar,'-c',$jar,'-o','NUL','-D',$hdrf,'-w','%{http_code}','-X','POST','-d',$form,"$base/entrar")
    TMark ('WL-POST-entrar tent=' + $po + ' -> ' + ([string]$out).Trim())
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
      # aceito (a confirmacao da inscricao do A0b conta). Se inscricao e desafio caem
      # na mesma janela TOTP de 30 s, o codigo do desafio e "ja utilizado" e volta 400.
      # Apos cada falha, esperar a proxima janela (margem de 2 s) garante codigo fresco.
      if ($att -gt 1) {
        $nowS = [long][DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
        $delta = ([Math]::Floor($nowS / 30) + 1) * 30 + 2 - $nowS
        if ($delta -gt 0) { Start-Sleep -Seconds $delta }
      }
      $fm = Join-Path $tmpDir 'mfa-challenge.html'
      Remove-Item $fm -EA SilentlyContinue
      $null = Watch-Curl 45000 @('-k','--connect-timeout','5','--max-time','20','-s','-b',$jar,'-c',$jar,"$base/mfa/desafio","-o",$fm)
      $af2 = Get-Af ([IO.File]::ReadAllText($fm, [Text.Encoding]::UTF8))
      Start-Sleep -Milliseconds 300
      $form2 = 'Code=' + [Uri]::EscapeDataString((Get-Totp $mfaKey)) + '&ReturnUrl=&__RequestVerificationToken=' + [Uri]::EscapeDataString($af2)
      $out2 = Watch-Curl 45000 @('-k','--connect-timeout','5','--max-time','20','-s','-b',$jar,'-c',$jar,'-o',$mfaBody,'-w','%{http_code}','-X','POST','-d',$form2,"$base/mfa/desafio")
      TMark ('WL-desafio att=' + $att + ' -> ' + ([string]$out2).Trim())
      if ([string]$out2 -eq '302') { $ok = $true; break }
      if ([string]$out2 -eq '000') { Start-Sleep -Seconds 5; continue }
    }
    if (-not $ok) {
      $errMsg = ''
      if (Test-Path $mfaBody) {
        try {
          $rb = [IO.File]::ReadAllText($mfaBody, [Text.Encoding]::UTF8)
          $errs = [regex]::Matches($rb, 'class="[^"]*(?:error|alert)[^"]*"[^>]*>(.{5,200}?)</') | ForEach-Object { ([Net.WebUtility]::HtmlDecode($_.Groups[1].Value) -replace '<[^>]+>', '' -replace '\s+', ' ').Trim() } | Where-Object { $_ }
          $errMsg = (($errs | Select-Object -First 2) -join ' || ')
        } catch {}
      }
      TMark ('WL-desafio-erros ' + $errMsg.Substring(0, [Math]::Min(300, $errMsg.Length)))
      throw ("POST /mfa/desafio nao convergiu para " + $email + ' | ' + $errMsg.Substring(0, [Math]::Min(250, $errMsg.Length)))
    }
  } elseif (($code -eq '302') -and ($loc -like '*mfa/inscricao*')) {
    throw ("inscricao MFA exigida (esperado desafio) para " + $email)
  } elseif ($code -ne '302') {
    throw ("POST /entrar -> " + $code + ' loc=' + $loc)
  }
  TMark ('WL-fim ' + $code)
  return [pscustomobject]@{ Code = $code; Location = $loc }
}
function Web-Get([string]$jar, [string]$url) {
  $f = Join-Path $tmpDir ('pg-' + [guid]::NewGuid().ToString('N').Substring(0, 10) + '.html')
  $ca = @('-k','--connect-timeout','5','--max-time','45','-s','-L','-b',$jar,'-c',$jar,$url,'-o',$f)
  $null = Watch-Curl 90000 $ca
  $sz = 0; if (Test-Path $f) { $sz = ([IO.File]::ReadAllBytes($f)).Length }
  if ($sz -eq 0) { Start-Sleep -Seconds 5; $null = Watch-Curl 90000 $ca }
  try { return [Net.WebUtility]::HtmlDecode(([IO.File]::ReadAllText($f, [Text.Encoding]::UTF8))) } catch { return '' }
}
function Ensure-Api([string]$tag) {
  if (Wait-Ready 'http://localhost:5143/health/ready') { return }
  LogEv ('API fora do ar em ' + $tag + ' — reiniciando processo (instabilidade de ambiente compartilhado, nao defeito de produto)')
  Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -and $_.CommandLine -match $rootEsc -and $_.CommandLine -match 'Odca\.Api\.dll' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
  Start-Sleep -Seconds 2
  $script:pa = Start-Process dotnet -ArgumentList ('exec "{0}"' -f (Join-Path $root 'src\Odca.Api\bin\Debug\net10.0\Odca.Api.dll')) -WorkingDirectory (Join-Path $root 'src\Odca.Api') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $logDir 'd34-svc-api.log') -RedirectStandardError (Join-Path $logDir 'd34-svc-api-err.log')
  if (-not (Wait-Ready 'http://localhost:5143/health/ready')) { LogEv ('API nao voltou apos restart em ' + $tag) }
}
function Ensure-Web([string]$tag) {
  if (Wait-Ready 'http://localhost:5144/entrar') { return }
  LogEv ('WEB fora do ar em ' + $tag + ' — reiniciando processo (instabilidade de ambiente compartilhado, nao defeito de produto)')
  Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -and $_.CommandLine -match $rootEsc -and $_.CommandLine -match 'Odca\.Web\.dll' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
  Start-Sleep -Seconds 2
  $prevUrls = $env:ASPNETCORE_URLS
  $env:ASPNETCORE_URLS = 'http://localhost:5144'
  $script:pw = Start-Process dotnet -ArgumentList ('exec "{0}"' -f (Join-Path $root 'src\Odca.Web\bin\Debug\net10.0\Odca.Web.dll')) -WorkingDirectory (Join-Path $root 'src\Odca.Web') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $logDir 'd34-svc-web.log') -RedirectStandardError (Join-Path $logDir 'd34-svc-web-err.log')
  $env:ASPNETCORE_URLS = $prevUrls
  if (-not (Wait-Ready 'http://localhost:5144/entrar')) { LogEv ('WEB nao voltou apos restart em ' + $tag) }
}
function Ensure-Stack([string]$tag) { Ensure-Api $tag; Ensure-Web $tag }
function ReLogin-Chain([string]$jar, [string]$pageUrl) {
  # Sessao ODCA tem expiracao ABSOLUTA (Jwt:AccessTokenMinutes=15, sem sliding —
  # AuthenticationService nao renova). Sob carga, a jornada D34 pode estourar esse
  # limite entre o login do cliente e as fases OCR/T/N; entao as chamas passam a
  # responder 302 vazio para a pagina de login e a cadeia nao le nada. Recuperacao
  # de harness (nao muda as assercoes): nova sessao na mesma jar + novo token
  # antiforgery da mesma pagina.
  LogEv ('RELOGIN cadeia em ' + $pageUrl)
  $null = Web-Login $jar 'cliente.teste@odca.local' 'K8@wR3!nF6#zP2$m' ''
  return Get-Af (Web-Get $jar $pageUrl)
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
  try { $body = [IO.File]::ReadAllText($f, [Text.Encoding]::UTF8) } catch { $body = '' }
  $loc = ''
  if (Test-Path $hf) { $hm = [regex]::Match(([IO.File]::ReadAllText($hf)), '(?im)^Location:[ \t]*(\S+)'); if ($hm.Success) { $loc = $hm.Groups[1].Value } }
  return [pscustomobject]@{ Code = ([string]$out).Trim(); Location = $loc; Body = $body }
}
function Block-Near([string]$text, [string]$needle) {
  $lines = $text -split "`r?`n"; $idx = -1
  for ($i = 0; $i -lt $lines.Count; $i++) { if ($lines[$i] -like "*$needle*") { $idx = $i; break } }
  if ($idx -lt 0) { return '' }
  $from = [Math]::Max(0, $idx - 4); $to = [Math]::Min($lines.Count - 1, $idx + 6)
  return (($lines[$from..$to]) -join ' ')
}
function Wait-Ready([string]$url) {
  # 45 x 2s = teto ~90s: porta morta devolve erro instantaneo; cold start da API
  # leva dezenas de segundos. 90 iteracoes escondiam ~15 min de espera cega.
  for ($i = 1; $i -le 45; $i++) {
    Start-Sleep -Seconds 2
    try { $h = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 3; if ([int]$h.StatusCode -lt 500) { return $true } } catch {}
  }
  return $false
}

# ---------- constantes ----------
$t          = '20000000-0000-4000-8000-000000000001'
$uOperador  = '10000000-0000-4000-8000-000000000002'
$tplTherapy = 'cdca0425-b018-4033-9f59-64d133c45aad'
$tplNda     = 'f0dfb3e0-cbff-4123-9280-4e27d35ac913'
$tplArchive = 'd30353b9-797c-451d-9dca-6a195826fb9e'
$tplProfile = 'c711edc7-a0c9-471e-98b6-5a6cfe8cb0f1'
$patientA   = '75c87013-4595-41dd-93d9-ac2e9c1f8fc4'
$sheetContract = '819315d2-65e7-4304-b68b-198a4589ed1a'
$mainCfg  = Join-Path $root 'src\Odca.Api\development-runtime.json'
$rootEsc  = [Regex]::Escape($root)
$binOk = (Test-Path (Join-Path $root 'src\Odca.Api\bin\Debug\net10.0\Odca.Api.dll')) -and `
         (Test-Path (Join-Path $root 'src\Odca.Web\bin\Debug\net10.0\Odca.Web.dll')) -and `
         (Test-Path (Join-Path $root 'src\Odca.Worker\bin\Debug\net10.0\Odca.Worker.dll')) -and `
         (Test-Path (Join-Path $root 'src\Odca.Bootstrap\bin\Debug\net10.0\Odca.Bootstrap.dll'))
Check 'P0 binaries Debug presentes (api/web/worker/bootstrap)' $binOk ''

# ---------- 0b. purga deterministica dos dados da prova (base descartavel) ----------
# Rodadas anteriores deixam contratos, minutas, versoes geradas, pacientes e extracoes
# desta mesma jornada; a purga devolve a base ao estado do setup (o ODCA_SETUP_ON_START
# recria os fixtures com os ids ancorados acima no boot da stack, antes das fases).
$purgeTables = @(
  'extraction_suggestions','extraction_results','extraction_reviews','extraction_jobs',
  'contract_review_notifications','contract_review_comments','contract_review_events','contract_review_steps','contract_review_requests',
  'signature_preparation_operations','signature_preparation_events','signature_participants','signature_preparations',
  'user_notifications','obligation_evidence','obligation_events','obligation_reminders','contract_obligations','obligation_series',
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
$null = Db ("UPDATE odca.contract_templates SET status='published', archive_reason=NULL WHERE id='$tplArchive';")
if ($script:lastPsqlErr) { $purgeErr += 'tpl-archive: ' + $script:lastPsqlErr + ' | ' }
$null = Db ("UPDATE odca.contract_templates SET required_activity_profile=NULL WHERE id='$tplProfile';")
if ($script:lastPsqlErr) { $purgeErr += 'tpl-profile: ' + $script:lastPsqlErr + ' | ' }
# Paciente de fixture da jornada (criado via API em D-OC17/GATE3; a purga o remove e o
# ODCA_SETUP_ON_START nao re-cria pacientes): reinsert deterministico com dados sinteticos.
$uCli = '10000000-0000-4000-8000-000000000003'
$null = Db ("INSERT INTO odca.patients (id, tenant_id, full_name, email, identifier_type, identifier_value, identifier_normalized, row_version, created_by, updated_by) VALUES ('$patientA', '$t', 'Ana Paula Ferreira da Costa', 'ana.paula.prova@odca.local', 'cpf', '11144777735', '11144777735', 1, '$uCli', '$uCli') ON CONFLICT (id) DO NOTHING;")
if ($script:lastPsqlErr) { $purgeErr += 'patient-fixture: ' + $script:lastPsqlErr + ' | ' }
$patientFixed = [long](Db "SELECT count(*) FROM odca.patients WHERE id='$patientA' AND tenant_id='$t' AND inactive_at IS NULL;")
if ($patientFixed -ne 1) { $purgeErr += "patient-fixture-count=$patientFixed | " }
# Fixture de plano deterministico: a jornada J (revisao -> aprovacao -> assinatura) exige o
# modulo de revisoes, declarado desabilitado em basic/intermediate v2 e habilitado apenas em
# enterprise v2 (verificado em plan_entitlements; sem alterar o catalogo). Ativar plano nao
# registra pagamento (invariante ativacao != pagamento).
$null = Db ("UPDATE odca.subscriptions SET plan_version_id=(SELECT id FROM odca.plan_versions WHERE code='enterprise' AND version::text='2' AND status='published') WHERE tenant_id='$t' AND status='active';")
if ($script:lastPsqlErr) { $purgeErr += 'plan-enterprise: ' + $script:lastPsqlErr + ' | ' }
$planNow = (Db ("SELECT coalesce(pv.code||' v'||pv.version::text,'(none)') FROM odca.subscriptions s LEFT JOIN odca.plan_versions pv ON pv.id=s.plan_version_id WHERE s.tenant_id='$t' AND s.status='active';")).Trim()
if ($planNow -ne 'enterprise v2') { $purgeErr += 'plan-now=' + $planNow + ' | ' }
# Fixture deterministica de canal de notificacao: o e-mail por tipo e opt-in (o padrao da
# funcao notification_channel_state e channel_email=false). Para B.10 (a outbox recebe
# exatamente um signature_completed enderecado ao criador da preparacao) habilitamos o canal
# de e-mail do cliente para esse tipo, refletindo uma preferencia real de usuario.
$null = Db ("INSERT INTO odca.user_notification_preferences(tenant_id,user_id,kind,enabled,channel_in_app,channel_email) VALUES ('$t','$uCli','signature_completed',true,true,true) ON CONFLICT (tenant_id,user_id,kind) DO UPDATE SET enabled=true,channel_in_app=true,channel_email=true;")
if ($script:lastPsqlErr) { $purgeErr += 'prefs-signature-completed: ' + $script:lastPsqlErr + ' | ' }
$chanEmail = (Db ("SELECT odca.notification_channel_state('$t','$uCli','signature_completed')::text;")).Trim()
if ($chanEmail -ne '(t,t,t)') { $purgeErr += 'chan-email=' + $chanEmail + ' | ' }
# Transporte local de notificacao (FilePickup): a purga do banco nao alcanca o diretorio de
# coleta; limpar artefatos de rodadas anteriores para B.10 nao cascar com arquivo antigo.
try { $rtPickup = [string](((Get-Content $mainCfg -Raw) | ConvertFrom-Json).Notifications.DevelopmentPickupDirectory) } catch { $rtPickup = '' }
if ($rtPickup -and (Test-Path $rtPickup)) { Remove-Item (Join-Path $rtPickup '*.txt') -EA SilentlyContinue }
Check 'P0b purga da base descartavel (estado idem ao setup + plano enterprise v2 + opt-in e-mail signature_completed)' ($purgeErr -eq '') ($purgeErr.Trim())

# ---------- 0. stack ----------
Set-Location $root
& (Join-Path $root 'scripts\stop-local.ps1') 2>&1 | Out-Null
Start-Sleep -Seconds 3
Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -and $_.CommandLine -match $rootEsc -and $_.CommandLine -match 'Odca\.(Api|Web|Worker)\.(dll|csproj)' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
Start-Sleep -Seconds 2
$env:ODCA_RUNTIME_CONFIG = $mainCfg
$env:DOTNET_ENVIRONMENT = 'Development'
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ODCA_SETUP_ON_START = 'true'
$env:ASPNETCORE_URLS = 'http://localhost:5143'
$env:Api__BaseUrl = 'http://localhost:5143/'
$pa = Start-Process dotnet -ArgumentList ('exec "{0}"' -f (Join-Path $root 'src\Odca.Api\bin\Debug\net10.0\Odca.Api.dll')) -WorkingDirectory (Join-Path $root 'src\Odca.Api') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $logDir 'd34-svc-api.log') -RedirectStandardError (Join-Path $logDir 'd34-svc-api-err.log')
$env:ASPNETCORE_URLS = 'http://localhost:5144'
$pw = Start-Process dotnet -ArgumentList ('exec "{0}"' -f (Join-Path $root 'src\Odca.Web\bin\Debug\net10.0\Odca.Web.dll')) -WorkingDirectory (Join-Path $root 'src\Odca.Web') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $logDir 'd34-svc-web.log') -RedirectStandardError (Join-Path $logDir 'd34-svc-web-err.log')
$env:ASPNETCORE_URLS = ''
$pwk = Start-Process dotnet -ArgumentList ('exec "{0}"' -f (Join-Path $root 'src\Odca.Worker\bin\Debug\net10.0\Odca.Worker.dll')) -WorkingDirectory (Join-Path $root 'src\Odca.Worker') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $logDir 'd34-svc-worker.log') -RedirectStandardError (Join-Path $logDir 'd34-svc-worker-err.log')
$readyApi = Wait-Ready 'http://localhost:5143/health/ready'
$readyWeb = Wait-Ready 'http://localhost:5144/entrar'
Check 'S1 api pronta' $readyApi ''
Check 'S2 web pronta' $readyWeb ''
if (-not ($readyApi -and $readyWeb)) { exit 2 }
LogEv ("Fase stack ok " + (Get-Date -Format 'o'))

# ---------- A. diagnostico operacional do superadministrador ----------
$boot = Join-Path $root 'src\Odca.Bootstrap\bin\Debug\net10.0\Odca.Bootstrap.dll'
& dotnet exec $boot reset-mfa --account ADMIN@ODCA.LOCAL 2>&1 | Out-Null
& dotnet exec $boot reset-mfa --account OPERADOR@ODCA.LOCAL 2>&1 | Out-Null
Db "UPDATE odca.users SET must_change_password=false WHERE id='$uOperador';" | Out-Null
& dotnet exec $boot reset-password --account admin --allow-postgres-development 2>&1 | Out-Null
Db "UPDATE odca.users SET must_change_password=false WHERE id IN (SELECT id FROM odca.users WHERE email ILIKE 'admin@odca.local');" | Out-Null
$slLines = (& dotnet exec $boot show-login 2>&1 | Out-String) -split "`r?`n"
$hdrIdx = -1
for ($k = 0; $k -lt $slLines.Count; $k++) { if ($slLines[$k] -like '*Superadministrador*') { $hdrIdx = $k; break } }
$adminPass = ''
for ($k = $hdrIdx + 1; $k -lt [Math]::Min($hdrIdx + 8, $slLines.Count); $k++) { if ($slLines[$k] -match 'inicial local.*: (\S+)$') { $adminPass = $Matches[1]; break } }
Check 'A0a senha inicial do superadmin obtida via show-login' ($adminPass.Length -gt 8) ('len=' + $adminPass.Length)
if ($adminPass.Length -le 8) { exit 2 }
$aEnroll = Api-LoginEnroll 'admin@odca.local' $adminPass
Check 'A0b login API superadmin + MFA inscrito com TOTP local' ($aEnroll.MfaKey.Length -ge 8) ('key_len=' + $aEnroll.MfaKey.Length)
Ensure-Stack 'A1 pre-login superadmin'
$jarAdmin = Join-Path $tmpDir 'jar-admin.txt'
$la = Web-Login $jarAdmin 'admin@odca.local' $adminPass $aEnroll.MfaKey
Check 'A1 login web superadmin convergiu apos desafio MFA' ($la.Code -eq '302') ($la.Code + ' ' + $la.Location)
if ($TraceA1) { Stop-Job $global:d34Ticker -EA SilentlyContinue; LogEv 'Fase trace-only encerrada apos A1 (sem avancar para F1/B/J/X/T/N)'; exit 0 }
$diag = Web-Get $jarAdmin "$base/administracao/diagnostico"
Check 'A2 pagina de diagnostico renderiza para superadmin' (($diag -like '*Diagnóstico de dependências*')) ('len=' + $diag.Length)
if ($diag -notlike '*Diagnóstico de dependências*') {
  [IO.File]::WriteAllText((Join-Path $evDir 'diag-a2.html'), $diag, [Text.Encoding]::UTF8)
  LogEv ('A2-DIAG pagina sem titulo esperado; inicio: ' + (($diag -replace '\s+', ' ').Substring(0, [Math]::Min(400, $diag.Length))))
}
$depsOk = ($diag -like '*Renderizador PDF interno*') -and ($diag -like '*pdftotext*') -and ($diag -like '*pdftoppm*') -and (`
          ($diag -like '*tesseract*')) -and ($diag -like '*pdfinfo*') -and ($diag -like '*clamscan*') -and (`
          ($diag -like '*Transporte de notificações*')) -and ($diag -like '*Envelope de assinatura externa*')
Check 'A3 as 8 dependencias sondadas aparecem com rotulos' $depsOk ''
$envBlock = Block-Near $diag 'Envelope de assinatura externa'
Check 'A4 envelope externo marcado Indisponivel (decisao registrada, sem inventar fornecedor)' ($envBlock -like '*Indisponível*') ('bloco=' + $envBlock.Substring(0, [Math]::Min(200, $envBlock.Length)))
$blkRender = Block-Near $diag 'Renderizador PDF interno'
Check 'A5 renderer em estado honesto (Acessivel ou Comprovado, nunca Configurado)' ((($blkRender -like '*Acessível*') -or ($blkRender -like '*Comprovado*')) -and ($blkRender -notlike '*Configurado*')) ('bloco=' + $blkRender.Substring(0, [Math]::Min(200, $blkRender.Length)))
$noSecret = ($diag -notlike '*123456*') -and ($diag -notlike '*Password=*') -and ($diag -notlike '*Host=127.0.0.1*')
Check 'A6 nenhum valor de configuracao (senha/connection string) exibido na pagina' $noSecret ''
Check 'A7 resumo de filas da plataforma exibido' ($diag -like '*Filas da plataforma*') ''

# ---------- W. login do cliente ----------
Ensure-Stack 'W1 pre-login cliente'
$jarCli = Join-Path $tmpDir 'jar-cli.txt'
$lc = Web-Login $jarCli 'cliente.teste@odca.local' 'K8@wR3!nF6#zP2$m' ''
Check 'W1 login web cliente 302 sem desafio MFA' (($lc.Code -eq '302') -and ($lc.Location -notlike '*mfa*' -and $lc.Location -notlike '*entrar*')) ($lc.Code + ' ' + $lc.Location)

# ---------- F1. plano fantasma odca-pro removido ----------
$planCodes = Db "SELECT string_agg(code, '|' ORDER BY code) FROM (SELECT DISTINCT code FROM odca.plan_versions) x;"
Check 'F1 planos canonicos v2 sem plano fantasma (basic|enterprise|intermediate)' ($planCodes -eq 'basic|enterprise|intermediate') ('codes=' + $planCodes + ' psql_err=' + $script:lastPsqlErr)

# ---------- B.5. arquivar modelo exige motivo + gate de perfil ----------
$rvTpl = [long](Db "SELECT row_version FROM odca.contract_templates WHERE id='$tplArchive';")
$verCountBefore = [long](Db "SELECT count(*) FROM odca.contract_template_versions WHERE template_id='$tplArchive';")
$pageT = Web-Get $jarCli "$base/organizacoes/$t/estudio/modelos/$tplArchive/editar"
$afTpl = Get-Af $pageT
$r1 = Web-Post $jarCli "$base/organizacoes/$t/estudio/modelos/$tplArchive/arquivar" @{ rowVersion = $rvTpl; reason = ''; __RequestVerificationToken = $afTpl } $afTpl
$back1 = Web-Get $jarCli "$base/organizacoes/$t/estudio/modelos/$tplArchive/editar"
$dbArch0 = Db "SELECT status FROM odca.contract_templates WHERE id='$tplArchive';"
Check 'B5-1 arquivar sem motivo volta 302 com erro amigavel (nada mudou)' (($r1.Code -eq '302') -and ($back1 -like '*mínimo 5 caracteres*') -and ($dbArch0.Trim() -eq 'published')) ('status=' + $dbArch0)
$reason = 'Modelo substituido pelo padrao corporativo v2'
$r2 = Web-Post $jarCli "$base/organizacoes/$t/estudio/modelos/$tplArchive/arquivar" @{ rowVersion = $rvTpl; reason = $reason; __RequestVerificationToken = $afTpl } $afTpl
$back2 = Web-Get $jarCli "$base/organizacoes/$t/estudio/modelos/$tplArchive/editar"
$dbArch = Db "SELECT status||'|'||coalesce(archive_reason,'')||'|'||coalesce(current_version::text,'') FROM odca.contract_templates WHERE id='$tplArchive';"
Check 'B5-2 arquivar com motivo persiste estado + razao e avisa sucesso' (($dbArch -like 'archived|Modelo substituido pelo padrao corporativo v2|*') -and ($back2 -like '*Modelo arquivado com sucesso.*')) ('row=' + $dbArch)
$verCountAfter = [long](Db "SELECT count(*) FROM odca.contract_template_versions WHERE template_id='$tplArchive';")
Check 'B5-3 versoes publicadas permanecem imutaveis apos arquivamento' ($verCountAfter -eq $verCountBefore) ('antes=' + $verCountBefore + ' depois=' + $verCountAfter)
Db "UPDATE odca.contract_templates SET status='published', archived_at=NULL, archive_reason=NULL, published_at=now(), row_version=row_version+1 WHERE id='$tplArchive';" | Out-Null
$dbArchR = Db "SELECT status||'|'||coalesce(archive_reason,'NULL') FROM odca.contract_templates WHERE id='$tplArchive';"
Check 'B5-4 fixture restaurada (modelo publicado sem razao)' ($dbArchR -eq 'published|NULL') ('row=' + $dbArchR)
Db "UPDATE odca.contract_templates SET required_activity_profile='therapy_clinic' WHERE id='$tplProfile';" | Out-Null
$pageI = Web-Get $jarCli "$base/organizacoes/$t/estudio"
$afIdx = Get-Af $pageI
$r3 = Web-Post $jarCli "$base/organizacoes/$t/estudio/minutas" @{ templateId = $tplProfile; title = 'Prova D-OC34 perfil restrito'; __RequestVerificationToken = $afIdx } $afIdx
$pageI2 = Web-Get $jarCli "$base/organizacoes/$t/estudio"
$draftGateCount = [long](Db "SELECT count(*) FROM odca.contract_drafts d JOIN odca.contracts c ON c.id=d.contract_id AND c.tenant_id=d.tenant_id WHERE c.title='Prova D-OC34 perfil restrito';")
Check 'B5-5 gate de perfil rejeita draft com mensagem estavel' (($r3.Code -eq '302') -and ($pageI2 -like '*Altere o perfil de atuação da organização em Organização*')) ('code=' + $r3.Code)
Check 'B5-6 nenhuma minuta criada quando o perfil diverge' ($draftGateCount -eq 0) ('count=' + $draftGateCount)
Db "UPDATE odca.contract_templates SET required_activity_profile=NULL WHERE id='$tplProfile';" | Out-Null

# ---------- B.6. vincular/desvincular paciente na minuta ----------
$r4 = Web-Post $jarCli "$base/organizacoes/$t/estudio/minutas" @{ templateId = $tplTherapy; title = 'Prova D-OC34 paciente'; __RequestVerificationToken = $afIdx } $afIdx
if ($r4.Code -ne '302' -or $r4.Location -notlike '*minutas/*') { throw ("criacao da minuta A falhou: " + $r4.Code + ' ' + $r4.Location) }
$draftA = ([regex]::Match($r4.Location, 'minutas/([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})')).Groups[1].Value
$editA = "$base/organizacoes/$t/estudio/minutas/$draftA"
$listA = $editA + '/documentos'
$pageA = Web-Get $jarCli $editA
$afA = Get-Af $pageA
Check 'B6-1 pagina de edicao exibe form de vincular c/ paciente ativo listado' (($pageA -like '*Vincular paciente*') -and ($pageA -like '*Ana Paula Ferreira da Costa*')) ('len=' + $pageA.Length)
$chipOk = ($pageA -like '*data-conflict-reload*') -and ($pageA -like '*data-conflict-keep*') -and ($pageA -like '*Carregar versão atual*') -and ($pageA -like '*Manter minha versão*') -and ($pageA -like '*Checklist do documento:*')
Check 'B6-2 chip de checklist + botoes de conflito (carregar/manter) presentes no DOM' $chipOk ''
$evLinkBefore = [long](Db "SELECT count(*) FROM odca.contract_events WHERE tenant_id='$t' AND event_type='patient.linked';")
$rvA = [long](Db "SELECT row_version FROM odca.contract_drafts WHERE id='$draftA';")
$r5 = Web-Post $jarCli "$editA/vincular-paciente" @{ patientId = $patientA; expectedVersion = $rvA; __RequestVerificationToken = $afA } $afA
$cv = Db "SELECT coalesce(patient_id::text,'NULL')||'|'||(patient_selection_snapshot IS NOT NULL)::text||'|'||row_version::text FROM odca.contract_drafts WHERE id='$draftA';"
Check 'B6-3 vincular persiste patient_id + snapshot imutavel e incrementa versao otimista' ($cv -like ("$patientA|true|*")) ('row=' + $cv + ' psql_err=' + $script:lastPsqlErr)
$cidA = Db "SELECT contract_id::text FROM odca.contract_drafts WHERE id='$draftA';"
$evLink = Db "SELECT coalesce(event_type,'')||'|'||coalesce(details->>'patientId','') FROM odca.contract_events WHERE tenant_id='$t' AND contract_id='$cidA' AND event_type='patient.linked' ORDER BY occurred_at DESC LIMIT 1;"
Check 'B6-4 evento auditavel patient.linked registrado no contrato' ($evLink -like ("patient.linked|$patientA")) ('row=' + $evLink)
$autoFill = [long](Db "SELECT count(*) FROM (SELECT jsonb_array_elements(values) e FROM odca.contract_drafts WHERE id='$draftA') x WHERE e->>'source'='patient' AND e->>'confirmed'='false';")
Check 'B6-5 campos de paciente nao confirmados foram autofilhados (source=patient)' ($autoFill -ge 1) ('campos=' + $autoFill)
$rvA2 = [long](Db "SELECT row_version FROM odca.contract_drafts WHERE id='$draftA';")
$r6 = Web-Post $jarCli "$editA/vincular-paciente" @{ patientId = ''; expectedVersion = $rvA2; __RequestVerificationToken = $afA } $afA
$cv2 = Db "SELECT coalesce(patient_id::text,'NULL') FROM odca.contract_drafts WHERE id='$draftA';"
Check 'B6-6 desvincular restaura patient_id nulo (snapshot preservado)' ($cv2.Trim() -eq 'NULL') ('patient_id=' + $cv2)

# ---------- B.7. cadastro rapido de paciente no fluxo de novo documento ----------
$newDocUrl = "$base/organizacoes/$t/documentos/novo"
$pageND = Web-Get $jarCli $newDocUrl
$afND = Get-Af $pageND
Check 'B7-1 formulario de cadastro rapido presente na pagina de novo documento' (($pageND -like '*Cadastro rápido — paciente não está na lista?*') -and ($afND.Length -gt 10)) ('len=' + $pageND.Length)
$countPBefore = [long](Db "SELECT count(*) FROM odca.patients WHERE tenant_id='$t';")
$r7 = Web-Post $jarCli "$newDocUrl/paciente" @{ fullName = 'Paciente Prova D-OC34'; identifierType = 'passport'; identifierValue = 'PASS-PROVA-34'; __RequestVerificationToken = $afND } $afND
$pageND2 = Web-Get $jarCli $newDocUrl
$newPatientCount = [long](Db "SELECT count(*) FROM odca.patients WHERE tenant_id='$t' AND full_name='Paciente Prova D-OC34';")
Check 'B7-2 cadastro rapido cria paciente e devolve feedback na tela' (($pageND2 -like '*Paciente cadastrado. Os demais campos podem ser complementados depois na ficha.*') -and ($newPatientCount -eq 1)) ('criados=' + $newPatientCount)
$r7b = Web-Post $jarCli "$newDocUrl/paciente" @{ fullName = 'Paciente Prova Invalida'; identifierType = 'cpf'; identifierValue = '123'; __RequestVerificationToken = $afND } $afND
$pageND3 = Web-Get $jarCli $newDocUrl
$invalidCount = [long](Db "SELECT count(*) FROM odca.patients WHERE tenant_id='$t' AND full_name='Paciente Prova Invalida';")
$countPAfter = [long](Db "SELECT count(*) FROM odca.patients WHERE tenant_id='$t';")
Check 'B7-3 identificador invalido nao cria paciente e explica o erro' (($invalidCount -eq 0) -and ($pageND3 -like '*class="error"*')) ('invalidos=' + $invalidCount + ' total=' + $countPAfter)

# ---------- B.8. (verificado apos a jornada J, na ficha do contrato cidB com preparacao confirmada) ----------

# ---------- J. jornada guiada: gerar -> revisar -> aprovar -> preparar -> assinar ----------
$rJ = Web-Post $jarCli "$base/organizacoes/$t/estudio/minutas" @{ templateId = $tplNda; title = 'Prova D-OC34 jornada assinatura'; __RequestVerificationToken = $afIdx } $afIdx
if ($rJ.Code -ne '302' -or $rJ.Location -notlike '*minutas/*') { throw ("criacao da minuta B falhou: " + $rJ.Code + ' ' + $rJ.Location) }
$draftB = ([regex]::Match($rJ.Location, 'minutas/([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})')).Groups[1].Value
$editB = "$base/organizacoes/$t/estudio/minutas/$draftB"
$pageB = Web-Get $jarCli $editB
$afB = Get-Af $pageB
$rvB0 = [long](Db "SELECT row_version FROM odca.contract_drafts WHERE id='$draftB';")
# preparacao de fixture (base descartavel): preenche os 8 campos obrigatorios do NDA como um
# preenchimento manual confirmado — sem isso a geracao fica bloqueada por campos pendentes.
$ndaJson = '[' + ([string[]]@(
  '{"value":"Empresa Alfa de Terapias LTDA","source":"manual","fieldId":"party_a_name","confirmed":true}',
  '{"value":"12345678000195","source":"manual","fieldId":"party_a_document","confirmed":true}',
  '{"value":"Cliente Teste ODCA","source":"manual","fieldId":"party_b_name","confirmed":true}',
  '{"value":"52998224725","source":"manual","fieldId":"party_b_document","confirmed":true}',
  '{"value":"Troca de informacoes tecnicas para avaliacao conjunta","source":"manual","fieldId":"purpose","confirmed":true}',
  '{"value":"2026-11-01","source":"manual","fieldId":"effective_date","confirmed":true}',
  '{"value":"2027-10-31","source":"manual","fieldId":"end_date","confirmed":true}',
  '{"value":"Rio de Janeiro/RJ","source":"manual","fieldId":"jurisdiction","confirmed":true}'
) -join ',') + ']'
# A SQL tem aspas duplas (JSON): psql -c via & no PS 5.1 entrega o argumento corrompido
# (bug de quoting de argumentos nativos) — usa -f com arquivo escrito em disco.
$updOut = DbFile 'j0-nda' ("UPDATE odca.contract_drafts SET values='" + $ndaJson + "'::jsonb WHERE id='" + $draftB + "';")
$updErr = $script:lastPsqlErr
$prefilled = [long](Db "SELECT count(*) FROM odca.contract_drafts d, jsonb_array_elements(d.values) e WHERE d.id='$draftB' AND e->>'confirmed'='true';")
Check 'J0 fixture: campos obrigatorios do NDA preenchidos como manual+confirmado' ($prefilled -eq 8) ('campos=' + $prefilled + ' upd=' + $updOut + ' err=' + $updErr)
$rGen = Web-Post $jarCli "$editB/gerar" @{ expectedVersion = $rvB0; idempotencyKey = (New-Guid).ToString(); __RequestVerificationToken = $afB } $afB
$vidB = ''
for ($i = 0; $i -lt 20 -and $vidB.Length -ne 36; $i++) {
  Start-Sleep -Seconds 2
  $vidB = (Db "SELECT coalesce((SELECT id::text FROM odca.generated_contract_versions WHERE draft_id='$draftB' ORDER BY created_at DESC, version_number DESC LIMIT 1),'')").Trim()
}
if ($vidB.Length -ne 36) {
  $pageB2 = Web-Get $jarCli $editB
  [IO.File]::WriteAllText((Join-Path $evDir 'j1-edit-after.html'), $pageB2, [Text.Encoding]::UTF8)
  $mErr = [regex]::Match($pageB2, 'class="(error|alert[^"]*)"[^>]*>\s*([^<]{10,300})')
  LogEv ('J1-DIAG sem versao apos gerar; trecho: ' + $(if ($mErr.Success) { ($mErr.Groups[2].Value -replace '\s+', ' ').Trim() } else { 'sem bloco de erro visivel; html salvo em j1-edit-after.html' }))
}
Check 'J1 gerar versao cria registro navegavel' ($vidB.Length -eq 36) ('version=' + $vidB + ' code=' + $rGen.Code)
$rPdf = Web-Post $jarCli "$base/organizacoes/$t/estudio/versoes/$vidB/pdf" @{ __RequestVerificationToken = $afB } $afB
$pdfStatus = ''
for ($i = 0; $i -lt 30; $i++) {
  Start-Sleep -Seconds 3
  $pdfStatus = (Db "SELECT pdf_status FROM odca.generated_contract_versions WHERE id='$vidB';").Trim()
  if ($pdfStatus -eq 'completed' -or $pdfStatus -eq 'failed') { break }
}
Check 'J2 PDF da versao gerado (comprovacao visual disponivel)' ($pdfStatus -eq 'completed') ('status=' + $pdfStatus)
# revisor: operador com MFA re-inscrito para a prova (fixture reversivel, padrao D-OC31)
Ensure-Stack 'antes do login do revisor (após gerar/pdf)'
$oEnroll = Api-LoginEnroll 'operador@odca.local' 'La@RsjC5p-hmq_JpReGW'
$idem = (New-Guid).ToString()
$rRev = Web-Post $jarCli "$base/organizacoes/$t/estudio/versoes/$vidB/solicitar-revisao" @{ reviewerId = $uOperador; dueAt = ''; instructions = 'Revisao da prova D-OC34'; idempotencyKey = $idem; __RequestVerificationToken = $afB } $afB
$revM = [regex]::Match($rRev.Location, 'solicitacoes/([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})')
$reviewId = $revM.Groups[1].Value
Check 'J2b requisicao de revisao criada e atribuida ao revisor' ($reviewId.Length -eq 36) ('review=' + $reviewId + ' code=' + $rRev.Code)
$revVer = [long](Db "SELECT row_version FROM odca.contract_review_requests WHERE id='$reviewId';")
$jarOp = Join-Path $tmpDir 'jar-op.txt'
$lo = Web-Login $jarOp 'operador@odca.local' 'La@RsjC5p-hmq_JpReGW' $oEnroll.MfaKey
Check 'J3 login web do revisor (operador) apos desafio MFA' ($lo.Code -eq '302') ($lo.Code + ' ' + $lo.Location)
$pageRv = Web-Get $jarOp "$base/organizacoes/$t/solicitacoes/$reviewId"
$afRv = Get-Af $pageRv
Check 'J3b pagina da solicitacao de revisao renderiza para o revisor atribuido' (($reviewId.Length -eq 36) -and ($pageRv.Length -gt 500) -and ($afRv.Length -gt 10)) ('review=' + $reviewId + ' len=' + $pageRv.Length + ' af=' + $afRv.Length)
$rApv = Web-Post $jarOp "$base/organizacoes/$t/solicitacoes/$reviewId/decisao" @{ action = 'approve'; justification = 'Aprovado para prova D-OC34'; expectedVersion = $revVer; __RequestVerificationToken = $afRv } $afRv
$revState = (Db "SELECT review_status FROM odca.generated_contract_versions WHERE id='$vidB';").Trim()
Check 'J4 decisao do revisor aprova a versao internamente' ($revState -eq 'internally_approved') ('status=' + $revState + ' code=' + $rApv.Code)
$pageV = Web-Get $jarCli "$base/organizacoes/$t/estudio/versoes/$vidB"
$afV = Get-Af $pageV
$prepPre = Db "SELECT coalesce(status,'<none>')||'|'||coalesce(row_version::text,'0') FROM odca.signature_preparations WHERE generated_version_id='$vidB';"
$prepPreParts = [string]$prepPre -split '\|'
$prepVer = 0L
if ($prepPreParts.Count -ge 2) { try { $prepVer = [long]$prepPreParts[1] } catch {} }
$p0 = [guid]::NewGuid()
$rPart = Web-Post $jarCli "$base/organizacoes/$t/estudio/versoes/$vidB/participantes" @{
  expectedVersion = $prepVer; confirm = 'true'; operationId = (New-Guid).ToString();
  'participants[0].Id' = $p0.ToString(); 'participants[0].ParticipantType' = 'professional'; 'participants[0].SourceId' = '';
  'participants[0].Role' = 'Responsavel tecnico'; 'participants[0].Name' = 'Profissional Responsavel ODCA';
  'participants[0].Email' = 'profissional.responsavel.prova@odca.local'; 'participants[0].Phone' = ''; 'participants[0].Position' = '1';
  __RequestVerificationToken = $afV
} $afV
$prepRow = Db "SELECT sp.id||'|'||sp.status||'|'||coalesce(p.client_id::text,'NULL') FROM odca.signature_preparations sp LEFT JOIN odca.signature_participants p ON p.preparation_id=sp.id WHERE sp.generated_version_id='$vidB';"
$prepParts = [string]$prepRow -split '\|'
$prepOk = ($prepParts.Count -ge 3) -and ($prepParts[1] -eq 'confirmed') -and ($prepParts[2] -ne 'NULL')
Check 'J5 preparacao de assinatura confirmada com participante unico' $prepOk ('row=' + $prepRow + ' psql_err=' + $script:lastPsqlErr)
if (-not $prepOk) { Check 'J5a diagnostico: resposta do POST participantes' $false ('code=' + $rPart.Code + ' body=' + $rPart.Body.Substring(0, [Math]::Min(300, $rPart.Body.Length))) }
$cidB = Db "SELECT contract_id::text FROM odca.contract_drafts WHERE id='$draftB';"
$rSig = Web-Post $jarCli "$base/organizacoes/$t/contratos/$cidB/assinaturas/assinar" @{
  versionId = $vidB; preparationId = $prepParts[0]; participantClientId = $prepParts[2];
  signMode = 'evidence'; evidence = 'Assinado presencialmente durante a prova D-OC34';
  participantName = 'Profissional Responsavel ODCA'; __RequestVerificationToken = $afB
} $afB
$sheetB = Web-Get $jarCli "$base/organizacoes/$t/contratos/$cidB"
Check 'J6 assinatura em modalidade evidence registrada; todos assinaram' ($sheetB -like '*Assinatura de Profissional Responsavel ODCA registrada. Todos os participantes assinaram*') ('len=' + $sheetB.Length)
$dbSigned = Db ("SELECT coalesce(signed_through,'')||'|'||(signature_evidence IS NOT NULL)::text||'|'||(signed_at IS NOT NULL)::text FROM odca.signature_participants WHERE preparation_id='" + $prepParts[0] + "';")
Check 'J7 linha de assinatura guarda modalidade evidence + descricao (rastreavel)' ($dbSigned -eq 'evidence|true|true') ('row=' + $dbSigned)
Check 'B8 banner de modalidades na ficha apos assinatura real (nenhum envelope enviado, franquia intacta)' (($sheetB -like '*fornecedor ainda não foi escolhida para a estreia*') -and ($sheetB -like '*nenhum envelope externo é enviado*')) ('len=' + $sheetB.Length)

# ---------- B.10. notificacao de assinatura concluida ----------
# Identidade de dedup: source_id deterministico = primeiro guid de SHA256
# ('signature.completed|<preparacao>|<versao>') — NotificationEnqueue.DeterministicSource,
# reproduzido aqui para amarrar a linha da outbox a ESTE ato exato (o protected_payload e
# protegido por DataProtection e nao contem os ids em texto puro).
$hashSig = [System.Security.Cryptography.SHA256]::Create().ComputeHash([System.Text.Encoding]::UTF8.GetBytes(('signature.completed|' + $prepParts[0] + '|' + $vidB)))
$subSig = New-Object byte[] 16
[System.Array]::Copy($hashSig, 0, $subSig, 0, 16)
$srcSig = [System.Guid]::new($subSig).ToString()
$outC1 = [long](Db ("SELECT count(*) FROM odca.notification_outbox WHERE tenant_id='$t' AND kind='signature_completed' AND source_id='" + $srcSig + "';"))
$dest1 = (Db ("SELECT coalesce(destination,'') FROM odca.notification_outbox WHERE tenant_id='$t' AND kind='signature_completed' AND source_id='" + $srcSig + "' ORDER BY created_at DESC LIMIT 1;")).Trim()
Check 'B10-1 outbox recebe exatamente um signature_completed (dedup preparation+versao)' ($outC1 -eq 1) ('count=' + $outC1 + ' source_id=' + $srcSig + ' psql_err=' + $script:lastPsqlErr)
Check 'B10-2 destinatario e quem criou a preparacao (conta do cliente)' ($dest1 -eq 'cliente.teste@odca.local') ('destino=' + $dest1)
$rSig2 = Web-Post $jarCli "$base/organizacoes/$t/contratos/$cidB/assinaturas/assinar" @{
  versionId = $vidB; preparationId = $prepParts[0]; participantClientId = $prepParts[2];
  signMode = 'evidence'; evidence = 'Assinado presencialmente durante a prova D-OC34 (replay)';
  participantName = 'Profissional Responsavel ODCA'; __RequestVerificationToken = $afB
} $afB
$sheetB2 = Web-Get $jarCli "$base/organizacoes/$t/contratos/$cidB"
$outC2 = [long](Db ("SELECT count(*) FROM odca.notification_outbox WHERE tenant_id='$t' AND kind='signature_completed' AND source_id='" + $srcSig + "';"))
Check 'B10-3 replay do ato avis que ja estava registrado e nao duplica notificacao' (($sheetB2 -like '*já havia sido registrada*') -and ($outC2 -eq 1)) ('count=' + $outC2)
# Transporte local (FilePickup): o diretorio e definido pela runtime config em uso
# (development-runtime.json); fallback para o diretorio relativo do appsettings.Development.json.
$pickupDir = Join-Path $root 'src\Odca.Worker\.local\notifications'
try {
  $rtCfg = (Get-Content $mainCfg -Raw) | ConvertFrom-Json
  if ($rtCfg.Notifications.DevelopmentPickupDirectory) { $pickupDir = $rtCfg.Notifications.DevelopmentPickupDirectory }
} catch {}
$foundFile = $null
for ($i = 0; $i -lt 30; $i++) {
  Start-Sleep -Seconds 4
  $foundFile = Get-ChildItem $pickupDir -Filter 'notif-*.txt' -EA SilentlyContinue | Where-Object { ([IO.File]::ReadAllText($_.FullName, [Text.Encoding]::UTF8) -like '*Assinatura concluída*') } | Sort-Object LastWriteTime -Descending | Select-Object -First 1
  if ($foundFile) { break }
}
$ptxt = ''
if ($foundFile) { $ptxt = [IO.File]::ReadAllText($foundFile.FullName, [Text.Encoding]::UTF8) }
Check 'B10-4 transporte devolve notificacao renderizada com o assunto Assinatura concluida' ($null -ne $foundFile) ($(if ($foundFile) { ('arquivo=' + $foundFile.Name) } else { 'nao encontrado em 120s' }))
Check 'B10-5 notificacao menciona o destinatario sem expor segredos' (($ptxt -like '*cliente.teste@odca.local*') -and ($ptxt -notlike '*Password=*')) ('bytes=' + $ptxt.Length)

# ---------- B.9. proxima acao completed na central de documentos ----------
$finalRev = (Db "SELECT review_status FROM odca.generated_contract_versions WHERE id='$vidB';").Trim()
Check 'B9-1 versao marcada externamente assinada apos todos os atos' ($finalRev -eq 'externally_signed') ('status=' + $finalRev)
$pageDocs = Web-Get $jarCli "$base/organizacoes/$t/documentos"
Check 'B9-2 central de documentos mostra Concluído — disponível para download' ($pageDocs -like '*Concluído — disponível para download*') ('len=' + $pageDocs.Length)

# ---------- F2/X. OCR honesto (worker principal) ----------
# Ensure-Stack (nao so API): as subidas passam pela WEB :5144; se a web cair,
# todas as chains falham com connection refused (lixao observado na rodada 10).
Ensure-Stack 'inicio da fase OCR'
Add-Type -AssemblyName System.Drawing
$blank = Join-Path $tmpDir 'd34-blank.jpg'
$bmp = New-Object Drawing.Bitmap(1000, 1200); $g = [Drawing.Graphics]::FromImage($bmp)
$g.FillRectangle([Drawing.Brushes]::White, 0, 0, 1000, 1200)
$bmp.Save($blank, [Drawing.Imaging.ImageFormat]::Jpeg); $g.Dispose(); $bmp.Dispose()
$png = Join-Path $tmpDir 'd34-blank.png'
$bmp2 = New-Object Drawing.Bitmap(1000, 1200); $g2 = [Drawing.Graphics]::FromImage($bmp2)
$g2.FillRectangle([Drawing.Brushes]::White, 0, 0, 1000, 1200)
$bmp2.Save($png, [Drawing.Imaging.ImageFormat]::Png); $g2.Dispose(); $bmp2.Dispose()
$htmlSrc = Join-Path $tmpDir 'd34-scan25.html'
$sb = New-Object Text.StringBuilder
[void]$sb.Append('<html><head><meta charset="utf-8"></head><body>')
for ($i = 1; $i -le 25; $i++) {
  # Sem page-break no ultimo div: o wkhtmltopdf criaria uma pagina vazia extra (26 em vez de 25).
  $pb = $(if ($i -lt 25) { 'page-break-after: always;' })
  [void]$sb.Append('<div style="' + $pb + '"><img src="file:///' + ($blank -replace '\\', '/') + '"/></div>')
}
[void]$sb.Append('</body></html>')
[IO.File]::WriteAllText($htmlSrc, $sb.ToString(), [Text.UTF8Encoding]::new($false))
$wk = 'C:\Program Files (x86)\wkhtmltopdf\bin\wkhtmltopdf.exe'
$scan25 = Join-Path $tmpDir 'd34-scan25.pdf'
Remove-Item $scan25 -EA SilentlyContinue
& $wk --enable-local-file-access -q $htmlSrc $scan25 2>&1 | Out-Null
$scan25Bytes = 0; if (Test-Path $scan25) { $scan25Bytes = (Get-Item $scan25).Length }
Check 'X0 fixture PDF escaneado de 25 paginas gerada' ($scan25Bytes -gt 5000) ('bytes=' + $scan25Bytes)
# No PS 5.1, carregar apenas System.IO.Compression.FileSystem nao deixa o nome
# [IO.Compression.ZipArchiveMode] resolvel (o enum vive no assembly base).
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$docxSrc = Join-Path $tmpDir 'docx-src'
Remove-Item -Recurse -Force $docxSrc -EA SilentlyContinue
New-Item -ItemType Directory -Force -Path (Join-Path $docxSrc '_rels') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $docxSrc 'word') | Out-Null
[IO.File]::WriteAllText((Join-Path $docxSrc '[Content_Types].xml'), '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/></Types>', [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $docxSrc '_rels\.rels'), '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>', [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $docxSrc 'word\document.xml'), '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:p><w:r><w:t>Contrato de prova D-OC34 - documento de texto direto, sem camadas de varredura.</w:t></w:r></w:p></w:body></w:document>', [Text.UTF8Encoding]::new($false))
$docx = Join-Path $tmpDir 'd34-minimal.docx'
Remove-Item $docx -EA SilentlyContinue
# .NET Framework (PS 5.1) cria entradas de zip com backslash em CreateFromDirectory;
# o parser do produto espera nomes OOXML com barra. Cria as entradas explicitamente.
$fsZip = [IO.File]::Open($docx, 'Create')
$za = New-Object IO.Compression.ZipArchive($fsZip, [IO.Compression.ZipArchiveMode]::Create)
foreach ($pair in @(@{ d = '[Content_Types].xml'; z = '[Content_Types].xml' }, @{ d = '_rels\.rels'; z = '_rels/.rels' }, @{ d = 'word\document.xml'; z = 'word/document.xml' })) {
  $entry = $za.CreateEntry($pair.z)
  $es = $entry.Open()
  $bytes = [IO.File]::ReadAllBytes((Join-Path $docxSrc $pair.d))
  $es.Write($bytes, 0, $bytes.Length)
  $es.Close()
}
$za.Dispose(); $fsZip.Dispose()
Check 'X0b fixture docx minimo gerada' ((Test-Path $docx) -and ((Get-Item $docx).Length -gt 500)) ('bytes=' + $(if (Test-Path $docx) { (Get-Item $docx).Length } else { 0 }))

function Invoke-Chain([string]$file, [string]$tag, [string]$jar, [string]$editUrl, [string]$afPage, [int]$pollLimit = 120) {
  $upFile = Join-Path $tmpDir ('up-' + $tag + '.json')
  # Resiliencia a queda momentanea do ambiente compartilhado (outros checkouts da mesma
  # maquina podem derrubar o dotnet local): se o upload nao conecta, garante a stack e
  # tenta uma unica vez antes de desistir (mesmo padrao de Ensure-Stack ja usado na prova).
  $mkUp = { param($tok) @('-k','--connect-timeout','5','--max-time','60','-s','-b',$jar,'-c',$jar,'-H',('RequestVerificationToken: ' + $tok),'-F',('file=@' + $file),'-o',$upFile,'-w','%{http_code}',($editUrl + '/documentos')) }
  $code = Watch-Curl 120000 (& $mkUp $afPage)
  if ([string]$code -eq '000') { Ensure-Stack ('upload-' + $tag); $code = Watch-Curl 120000 (& $mkUp $afPage) }
  if ([string]$code -match '^3') { $afPage = ReLogin-Chain $jar $editUrl; $code = Watch-Curl 120000 (& $mkUp $afPage) }
  $upJson = $null; try { $upJson = (Get-Content $upFile -Raw) | ConvertFrom-Json } catch {}
  if ($code -ne '200' -or $null -eq $upJson) { return [PSCustomObject]@{ ok = $false; uploadCode = $code; raw = (Get-Content $upFile -Raw -EA SilentlyContinue); jobStatus = 'n/a'; Job = $null } }
  $status = ''
  for ($i = 0; $i -lt 60; $i++) {
    Start-Sleep -Seconds 3
    $null = Watch-Curl 90000 @('-k','--connect-timeout','5','--max-time','45','-s','-b',$jar,'-c',$jar,'-o',(Join-Path $tmpDir 'list.json'),($editUrl + '/documentos'))
    $rows = @(); try { $p = (Get-Content (Join-Path $tmpDir 'list.json') -Raw) | ConvertFrom-Json; $rows = @($p) } catch {}
    $row = @($rows | Where-Object { $_.id -eq $upJson.documentId }) | Select-Object -First 1
    if ($row) { $status = [string]$row.security_status; if ($status -eq 'safe' -or $status -eq 'failed') { break } }
  }
  if ($status -ne 'safe') { return [PSCustomObject]@{ ok = $false; uploadCode = '200'; raw = ('scan status=' + $status); jobStatus = 'n/a'; documentId = $upJson.documentId; Job = $null } }
  $body = '{"documentId":"' + $upJson.documentId + '","versionId":"' + $upJson.versionId + '"}'
  $bodyFile = Join-Path $tmpDir ('exb-' + $tag + '.json')
  [IO.File]::WriteAllText($bodyFile, $body, [Text.UTF8Encoding]::new($false))
  $exFile = Join-Path $tmpDir ('exr-' + $tag + '.json')
  $mkEx = { param($tok) @('-k','--connect-timeout','5','--max-time','60','-s','-b',$jar,'-c',$jar,'-X','POST','-H','Content-Type: application/json','-H',('RequestVerificationToken: ' + $tok),'-d',('@' + $bodyFile),'-o',$exFile,'-w','%{http_code}',($editUrl + '/extrair')) }
  $exCode = Watch-Curl 120000 (& $mkEx $afPage)
  if ([string]$exCode -match '^3') { $afPage = ReLogin-Chain $jar $editUrl; $exCode = Watch-Curl 120000 (& $mkEx $afPage) }
  $exJson = $null; try { $exJson = (Get-Content $exFile -Raw) | ConvertFrom-Json } catch {}
  if ($exCode -ne '202' -or -not $exJson.extractionId) { return [PSCustomObject]@{ ok = $false; uploadCode = '200'; raw = ('extrair ' + $exCode + ' ' + (Get-Content $exFile -Raw)); jobStatus = 'n/a'; documentId = $upJson.documentId; Job = $null } }
  $pollUrl = $editUrl + '/extracao/' + $exJson.extractionId
  $job = $null; $jobStatus = 'unknown'; $downStreak = 0; $relogged = $false
  # +30 de margem de tolerancia: sob carga, o status terminal pode chegar no limiar do
  # pollLimit (no ensaio de 2026-10-18 a conclusao chegou no ultimo poll). As assercoes
  # nao mudam — continua exigindo status terminal + codigo estavel quando houver falha.
  for ($i = 0; $i -lt ($pollLimit + 30); $i++) {
    Start-Sleep -Seconds 3
    $jobFile = Join-Path $tmpDir ('job-' + $tag + '.json')
    $pc = Watch-Curl 90000 @('-k','--connect-timeout','5','--max-time','45','-s','-b',$jar,'-c',$jar,'-o',$jobFile,'-w','%{http_code}',$pollUrl)
    if ([string]$pc -eq '000') {
      # Servico fora do ar durante o poll (instabilidade de ambiente compartilhado, nao
      # defeito de produto): apos 3 falhas seguidas garante a stack e segue o poll — o
      # resultado do job permanece no banco e o codigo estavel e o mesmo ao ser lido depois.
      $downStreak++
      if ($downStreak -ge 3) { Ensure-Stack ('poll-' + $tag); $downStreak = 0 }
      continue
    }
    if ([string]$pc -match '^3' -and (-not $relogged)) {
      # 3xx no GET do poll = sessao expirada (limite absoluto de 15 min, sem sliding,
      # estourado sob carga): relogin unico da cadeia e segue o poll.
      $null = ReLogin-Chain $jar $editUrl; $relogged = $true
      continue
    }
    $downStreak = 0
    try { $job = (Get-Content $jobFile -Raw) | ConvertFrom-Json } catch { continue }
    $jobStatus = [string]$job.job.status
    if ($jobStatus -eq 'ready_for_review' -or $jobStatus -eq 'failed') { break }
  }
  return [PSCustomObject]@{ ok = ($jobStatus -eq 'ready_for_review'); jobStatus = $jobStatus; failureCode = [string]$job.job.failure_code; suggestions = @($job.suggestions); extractionId = $exJson.extractionId; documentId = $upJson.documentId; versionId = $upJson.versionId; Job = $job }
}
$cX1 = Invoke-Chain $scan25 'x1scan25' $jarCli $editA $afA
$j1total = ''; $j1proc = -1; $j1trunc = $false
if ($null -ne $cX1.Job) { $j1total = [string]$cX1.Job.job.pages_total; try { $j1proc = [int]$cX1.Job.job.pages_processed } catch {}; $j1trunc = [bool]$cX1.Job.job.truncated }
Check 'X1-1 PDF escaneado 25 paginas: pronto com truncacao honesta (20 processadas)' (($cX1.ok -eq $true) -and ($j1proc -eq 20) -and ($j1trunc -eq $true)) ('total=' + $j1total + ' proc=' + $j1proc + ' trunc=' + $j1trunc + ' status=' + $cX1.jobStatus)
$dbX1 = Db ("SELECT coalesce(pages_total::text,'null')||'|'||coalesce(pages_processed::text,'null')||'|'||truncated::text FROM odca.extraction_results WHERE job_id='" + $cX1.extractionId + "';")
Check 'X1-2 banco e API concordam sobre o escopo (total|20|true)' ($dbX1 -eq ('{0}|20|true' -f $j1total)) ('row=' + $dbX1 + ' api_total=' + $j1total + ' psql_err=' + $script:lastPsqlErr)
LogEv ('X1-3 sugestoes retornadas para o escopo truncado: ' + @($cX1.suggestions).Count)
$cX2 = Invoke-Chain $docx 'x2docx' $jarCli $editA $afA
$j2total = ''; $j2proc = ''; $j2trunc = $true
if ($null -ne $cX2.Job) { $j2total = [string]$cX2.Job.job.pages_total; $j2proc = [string]$cX2.Job.job.pages_processed; $j2trunc = [bool]$cX2.Job.job.truncated }
Check 'X2-1 docx chega a ready_for_review (texto direto, sem fabricar paginas)' ($cX2.ok -eq $true) ('status=' + $cX2.jobStatus + ' code=' + $cX2.failureCode)
Check 'X2-2 docx nao fabrica paginas (total/processado ausentes, truncado false)' (($j2total -eq '') -and ($j2proc -eq '') -and ($j2trunc -eq $false)) ('total=[' + $j2total + '] proc=[' + $j2proc + '] trunc=' + $j2trunc)
$cX3 = Invoke-Chain $png 'x3png' $jarCli $editA $afA
$j3total = ''; $j3proc = -1; $j3trunc = $true
if ($null -ne $cX3.Job) { $j3total = [string]$cX3.Job.job.pages_total; try { $j3proc = [int]$cX3.Job.job.pages_processed } catch {}; $j3trunc = [bool]$cX3.Job.job.truncated }
Check 'X3 imagem unica reporta escopo 1 de 1 (sem truncacao)' (($cX3.ok -eq $true) -and ($j3total -eq '1') -and ($j3proc -eq 1) -and ($j3trunc -eq $false)) ('total=' + $j3total + ' proc=' + $j3proc + ' trunc=' + $j3trunc)

# ---------- T. timeout de OCR (executavel pendurado) ----------
Ensure-Stack 'inicio da fase T'
$altTimeout = Join-Path $logDir 'd34-runtime-timeout.json'
$cfgObj = Get-Content $mainCfg -Raw | ConvertFrom-Json
$cfgObj.Documents.OcrExecutable = 'C:\Users\NCELL-DEV-020\AppData\Local\Temp\opencode\d34-bin\d34sleep.exe'
[IO.File]::WriteAllText($altTimeout, ($cfgObj | ConvertTo-Json -Depth 20), [Text.UTF8Encoding]::new($false))
$wp = @(Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -and $_.CommandLine -match $rootEsc -and $_.CommandLine -match 'Odca\.Worker\.dll' })
foreach ($p in $wp) { Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue }
Start-Sleep -Seconds 3
$env:ODCA_RUNTIME_CONFIG = $altTimeout
$wT = Start-Process dotnet -ArgumentList ('exec "{0}"' -f (Join-Path $root 'src\Odca.Worker\bin\Debug\net10.0\Odca.Worker.dll')) -WorkingDirectory (Join-Path $root 'src\Odca.Worker') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $logDir 'd34-svc-wtimeout.log') -RedirectStandardError (Join-Path $logDir 'd34-svc-wtimeout-err.log')
Start-Sleep -Seconds 8
Check 'T0 worker alternativo (OCR pendurado) vivo' (-not $wT.HasExited) ('pid=' + $wT.Id)
Ensure-SleepExe
$cT = Invoke-Chain $png 't1timeout' $jarCli $editA $afA 170
Check 'T1 OCR pendurado falha como extraction-timeout (distinto de extractor-failed)' (($cT.jobStatus -eq 'failed') -and ($cT.failureCode -eq 'extraction-timeout')) ('status=' + $cT.jobStatus + ' code=' + $cT.failureCode)
Get-Process d34sleep -EA SilentlyContinue | Stop-Process -Force -EA SilentlyContinue

# ---------- N. executaveis ausentes (config alternativa sem OCR/renderer) ----------
Ensure-Stack 'inicio da fase N'
$altNoOcr = Join-Path $logDir 'd34-runtime-no-ocr.json'
$docsAlt = @{}
foreach ($prop in $cfgObj.Documents.PSObject.Properties) { if ($prop.Name -ne 'OcrExecutable' -and $prop.Name -ne 'PdfRenderExecutable') { $docsAlt[$prop.Name] = $prop.Value } }
$cfgObj.Documents = [PSCustomObject]$docsAlt
[IO.File]::WriteAllText($altNoOcr, ($cfgObj | ConvertTo-Json -Depth 20), [Text.UTF8Encoding]::new($false))
$wn1 = @(Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -and $_.CommandLine -match $rootEsc -and $_.CommandLine -match 'Odca\.Worker\.dll' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue; $null })
Start-Sleep -Seconds 3
$env:ODCA_RUNTIME_CONFIG = $altNoOcr
$wN = Start-Process dotnet -ArgumentList ('exec "{0}"' -f (Join-Path $root 'src\Odca.Worker\bin\Debug\net10.0\Odca.Worker.dll')) -WorkingDirectory (Join-Path $root 'src\Odca.Worker') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $logDir 'd34-svc-wnoocr.log') -RedirectStandardError (Join-Path $logDir 'd34-svc-wnoocr-err.log')
Start-Sleep -Seconds 8
Check 'N0 worker alternativo (sem OCR/renderer) vivo' (-not $wN.HasExited) ('pid=' + $wN.Id)
$cN1 = Invoke-Chain $scan25 'n1norender' $jarCli $editA $afA
Check 'N1 PDF escaneado sem renderer falha com codigo estavel pdf-no-text-layer' (($cN1.jobStatus -eq 'failed') -and ($cN1.failureCode -eq 'pdf-no-text-layer')) ('status=' + $cN1.jobStatus + ' code=' + $cN1.failureCode)
$cN2 = Invoke-Chain $png 'n2noocr' $jarCli $editA $afA
Check 'N2 imagem sem OcrExecutable falha com codigo estavel extractor-not-configured' (($cN2.jobStatus -eq 'failed') -and ($cN2.failureCode -eq 'extractor-not-configured')) ('status=' + $cN2.jobStatus + ' code=' + $cN2.failureCode)

# ---------- restaurar worker principal ----------
$wf = @(Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -and $_.CommandLine -match $rootEsc -and $_.CommandLine -match 'Odca\.Worker\.dll' })
foreach ($p in $wf) { Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue }
Start-Sleep -Seconds 3
$env:ODCA_RUNTIME_CONFIG = $mainCfg
$wMain = Start-Process dotnet -ArgumentList ('exec "{0}"' -f (Join-Path $root 'src\Odca.Worker\bin\Debug\net10.0\Odca.Worker.dll')) -WorkingDirectory (Join-Path $root 'src\Odca.Worker') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $logDir 'd34-svc-worker-final.log') -RedirectStandardError (Join-Path $logDir 'd34-svc-worker-final-err.log')
Start-Sleep -Seconds 5
Check 'N3 worker principal de volta (config completa)' (-not $wMain.HasExited) ('pid=' + $wMain.Id)

$summary = if ($script:fails -eq 0) { 'RESULTADO: TODOS OS CHECKS PASSARAM' } else { ('RESULTADO: {0} CHECK(S) FALHARAM' -f $script:fails) }
Write-Host $summary
LogEv $summary
('PROVA-FIM fails=' + $script:fails + ' ts=' + (Get-Date -Format 'o')) | Set-Content -Encoding utf8 (Join-Path $logDir 'd34-proof-fim.txt')
Stop-Job $global:d34Ticker -EA SilentlyContinue
exit $(if ($script:fails -eq 0) { 0 } else { 1 })
