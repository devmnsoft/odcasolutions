# D-OC40 - Sprint 6: ENSAIO DE PILOTO. Reexecuta em sequencia, sobre a MESMA base descartavel
# (odca_test_disposable), as provas ao vivo versionadas que definem a candidata ao piloto:
#   C-SLA          validate-c-web.ps1                 (solicitacoes, SLA, aprovacoes - 93 checks)
#   ACEITE         validate-acceptance-matrix.ps1     (matriz comportamental de aceite)
#   D34-JORNADA    validate-d34-an-web.ps1            (jornada documental A-N do D-OC34)
#   D37-ASSINATURA validate-d37-signature-web.ps1     (assinatura com envelope externo - S3)
#   D38-ATENDIMENTO validate-d38-s4-web.ps1           (atendimento/avisos/prazos - S4)
#   D39-SEGURANCA  validate-d39-s5-web.ps1            (reautenticacao MFA + suporte - S5)
# Cada estagio sobe/derra a propria stack e grava o proprio transcript em evidence/; uma falha de
# estagio NAO interrompe o ensaio (objetivo: fotografia completa para classificacao). O critério
# por estagio e o proprio exit code (aceite da matriz: linha de resumo PASS/FAIL/BLOCKED).
# Estagos legados (C-SLA, ACEITE) NAO gerenciam a propria stack: exigem a stack dev padrao
# (scripts\run-local.ps1: API https :7143 + Web https :7144 + Worker sobre odca_test_disposable).
# O orquestrador sobe essa stack antes do primeiro estagio legado e a derruba apos o ultimo.
# Os estagos autocontidos (D34/D37/D38/D39) sobem/derrubam a propria stack http :5143/:5144.
# Uso: powershell -NoProfile -ExecutionPolicy Bypass -File scripts/qa/run-pilot-rehearsal.ps1 [-Only 'C-SLA;ACEITE']
param([string]$Only)
[Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
$root   = 'C:\MNSOFT\odcasolutions'
$logDir = 'C:\Users\NCELL-DEV-020\AppData\Local\Temp\opencode'
$evDir  = Join-Path $logDir 'evidence'
New-Item -ItemType Directory -Force -Path $evDir | Out-Null
$rehearsal = Join-Path $evDir 'd40-rehearsal.txt'
Set-Content -Path $rehearsal '' -Encoding utf8

function RLog([string]$m) {
  $l = ((Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + ' ' + $m)
  Write-Host $l
  Add-Content -Path $rehearsal $l -Encoding utf8
}

# ---------- P0. binarios Debug atuais sobre o SHA ----------
Set-Location $root
dotnet build .\Odca.sln -c Debug --nologo -v m | ForEach-Object { }
if ($LASTEXITCODE -ne 0) { RLog 'P0 BUILD-DEBUG FALHOU'; exit 2 }
RLog 'P0 build Debug ok (binarios atuais)'

# ---------- estagios ----------
$stages = @(
  @{ Id = 'C-SLA';         Script = 'validate-c-web.ps1';             Mode = 'exit' },
  @{ Id = 'ACEITE';        Script = 'validate-acceptance-matrix.ps1'; Mode = 'parse' },
  @{ Id = 'D34-JORNADA';   Script = 'validate-d34-an-web.ps1';        Mode = 'exit' },
  @{ Id = 'D37-ASSINATURA'; Script = 'validate-d37-signature-web.ps1'; Mode = 'exit' },
  @{ Id = 'D38-ATENDIMENTO'; Script = 'validate-d38-s4-web.ps1';      Mode = 'exit' },
  @{ Id = 'D39-SEGURANCA'; Script = 'validate-d39-s5-web.ps1';        Mode = 'exit' }
)

if ($Only) {
  $wanted = @($Only -split ';' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
  $stages = @($stages | Where-Object { $wanted -contains $_.Id })
  if ($stages.Count -eq 0) { RLog 'SEM-ESTAGIOS: nenhum ID reconhecido em -Only'; exit 2 }
}

# ---------- stack dev para os estagos legados (C-SLA, ACEITE) ----------
$legacyIds    = @('C-SLA', 'ACEITE')
$legacyLastId = (@($stages | Where-Object { $legacyIds -contains $_.Id }) | Select-Object -Last 1).Id
function Test-LegacyPortFree() {
  return ($null -eq (Get-NetTCPConnection -LocalPort 7143 -State Listen -EA SilentlyContinue))
}
function Start-LegacyStack() {
  if (-not (Test-LegacyPortFree)) { RLog 'LEGACY: porta 7143 ja ocupada antes de subir a stack dev'; return }
  $null = Start-Process powershell -ArgumentList ('-NoProfile -ExecutionPolicy Bypass -File "{0}" -NonInteractive' -f (Join-Path $root 'scripts\run-local.ps1')) -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $evDir 'd40-runlocal.log') -RedirectStandardError (Join-Path $evDir 'd40-runlocal-err.log')
  # Janela generosa (420s): sob carga de maquina compartilhada (outros checkouts compilando em
  # paralelo) a stack legada demorou >180s para ficar pronta e derrubou os estagios C-SLA/ACEITE
  # (falha de conexao, nao de produto) em 2026-10-18.
  # Probe com curl -k: Invoke-WebRequest no PS 5.1 falha no certificado self-signed da stack dev e
  # jamais reportava prontidao (todas as runs de 18/10 queimaram os 420s e os estagios seguiram ok).
  for ($i = 0; $i -lt 210; $i++) {
    Start-Sleep -Seconds 2
    try { $code = & curl.exe -k -s -o NUL -w '%{http_code}' --max-time 5 'https://localhost:7143/health/ready' 2>$null; if ([string]$code -eq '200') { RLog ('LEGACY: stack dev pronta em https://localhost:7143 (apos ~' + ($i * 2) + 's)'); return } } catch {}
  }
  RLog 'LEGACY: API nao ficou pronta em 420s'
}
function Stop-LegacyStack() {
  & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'scripts\stop-local.ps1') -Force 2>&1 | Out-Null
  for ($i = 0; $i -lt 30 -and -not (Test-LegacyPortFree); $i++) { Start-Sleep -Seconds 2 }
  RLog ('LEGACY: stack dev derrubada (porta livre=' + (Test-LegacyPortFree) + ')')
}
function Db([string]$q) {
  $env:PGPASSWORD = '123456'; $env:PGCONNECT_TIMEOUT = '10'
  $out = & 'C:\Program Files\PostgreSQL\18\bin\psql.exe' -h 127.0.0.1 -U postgres -d odca_test_disposable -t -A -c $q 2>&1 | Out-String
  return $out.Trim()
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

# Sessao MFA real e deterministica para a matriz de aceite (S11/S12): reinicia o MFA do
# OPERADOR, reinscreve com chave conhecida via API (mesmo fluxo da prova D-OC39) e planta um
# recovery code sintetico (hash = SHA256 "{userId:N}:{codigo}" por MfaService.HashRecoveryCode).
function Preseed-AcceptanceMfa {
  $api     = 'https://localhost:7143/api/v1'
  $opEmail = 'operador@odca.local'
  $opPass  = 'La@RsjC5p-hmq_JpReGW'
  $qaCode  = 'ODCA-D40-QA-MFA'
  try {
    $uOp = Db "SELECT id::text FROM odca.users WHERE email_normalized='OPERADOR@ODCA.LOCAL';"
    if (-not $uOp) { RLog 'MFA-SEED: OPERADOR ausente'; return }
    Db "DELETE FROM odca.sessions WHERE user_id='$uOp';" | Out-Null
    Db "UPDATE odca.users SET mfa_confirmed_at=NULL, mfa_pending_since=NULL, mfa_last_accepted_time_step=NULL, mfa_secret_protected=NULL WHERE id='$uOp';" | Out-Null
    $login = Invoke-RestMethod -Uri "$api/auth/login" -Method Post -ContentType 'application/json; charset=utf-8' -Body ('{"login":"' + $opEmail + '","password":"' + $opPass + '"}')
    $hdr   = @{ Authorization = 'Bearer ' + $login.accessToken }
    $enr   = Invoke-RestMethod -Uri "$api/auth/mfa/enrollment" -Method Post -Headers $hdr
    $manualKey = [string]$enr.manualKey
    $confirmed = $false
    for ($t = 1; $t -le 3 -and -not $confirmed; $t++) {
      # Anti-replay: so posta no meio do timestep (pelo menos 5s restantes no passo de 30s).
      while ([DateTimeOffset]::UtcNow.ToUnixTimeSeconds() % 30 -gt 25) { Start-Sleep -Milliseconds 500 }
      $code = Get-TotpCode $manualKey
      try {
        $conf = Invoke-RestMethod -Uri "$api/auth/mfa/enrollment/confirm" -Method Post -Headers $hdr -ContentType 'application/json; charset=utf-8' -Body ('{"code":"' + $code + '"}')
        if ([bool]$conf.mfaVerified) { $confirmed = $true }
      } catch { }
      if (-not $confirmed) { Start-Sleep -Seconds ($t * 31) }
    }
    if (-not $confirmed) { RLog 'MFA-SEED: confirmacao TOTP nao confirmou em 3 tentativas'; return }
    $norm    = (($qaCode.ToCharArray() | Where-Object { [char]::IsLetterOrDigit($_) }) -join '').ToUpperInvariant()
    $uOpN    = $uOp -replace '-', ''
    $sha     = [System.Security.Cryptography.SHA256]::Create()
    $hbytes  = $sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($uOpN + ':' + $norm))
    $hashHex = ([BitConverter]::ToString($hbytes) -replace '-', '').ToLowerInvariant()
    Db "DELETE FROM odca.mfa_recovery_codes WHERE user_id='$uOp';" | Out-Null
    Db "INSERT INTO odca.mfa_recovery_codes (user_id, code_hash, created_at) VALUES ('$uOp','$hashHex', now());" | Out-Null
    $env:ODCA_QA_MFA_CODE = $qaCode
    RLog ('MFA-SEED: OPERADOR reinscrito; recovery code sintetico pronto (' + $qaCode + ')')
  } catch { RLog ('MFA-SEED-ERRO: ' + $_.Exception.Message) }
}
if ($legacyLastId) { Start-LegacyStack }

$stageFails = 0
foreach ($s in $stages) {
  if ($s.Id -eq 'ACEITE') { Preseed-AcceptanceMfa }
  $sw = [Diagnostics.Stopwatch]::StartNew()
  $stageLog = Join-Path $evDir ('d40-stage-' + $s.Id + '.log')
  Remove-Item $stageLog -EA SilentlyContinue
  RLog ('STAGE-START ' + $s.Id + ' script=' + $s.Script)
  # O proprio filho redireciona o console para o arquivo de estagio ('*>'): com
  # -RedirectStandardOutput/-Error no Start-Process do orquestrador, o WaitForExit espera EOF no
  # stream redirecionado e processos netos deixados pelo estagio (a stack que um script deixa
  # rodando para o proximo) herdam o handle do arquivo e seguram o EOF para sempre (visto na
  # pratica no estagio D34). Sem redirecao no pai, os netos seguem gravando no arquivo, o que
  # e inofensivo.
  # PS 5.1: o proprio '-Wait' tambem pendura quando o filho deixa processos netos (o mesmo
  # padrao D34, provado em reproducao minima em 2026-10-18); polling de HasExited NAO tem esse
  # comportamento (mesma reproducao retorna em ms). Prazo por estagio: 40 min.
  $stageOut = Join-Path $evDir ('d40-stage-' + $s.Id + '.out')
  Remove-Item $stageOut -EA SilentlyContinue
  $cmdLine = "& '" + (Join-Path $root ('scripts\qa\' + $s.Script)) + "' *>" + $stageOut + '; exit $LASTEXITCODE'
  $sp = Start-Process powershell -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', $cmdLine) -WindowStyle Hidden -PassThru
  $deadline = (Get-Date).AddMinutes(40)
  while (-not $sp.HasExited -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 500 }
  if (-not $sp.HasExited) {
    RLog ('STAGE-TIMEOUT ' + $s.Id + ' apos 40min - matando filho')
    try { $sp.Kill() } catch {}
  }
  $code = if ($sp.HasExited) { $sp.ExitCode } else { 1 }
  $sw.Stop()
  @(Get-Content (Join-Path $evDir ('d40-stage-' + $s.Id + '.out')) -EA SilentlyContinue) + @(Get-Content (Join-Path $evDir ('d40-stage-' + $s.Id + '.err')) -EA SilentlyContinue) | Set-Content -Path $stageLog -Encoding utf8
  $ok = ($code -eq 0)
  $summaryExtra = ''
  if ($s.Mode -eq 'parse') {
    $sumLine = (Get-Content $stageLog -EA SilentlyContinue | Select-String 'PASS: \d+ \| FAIL: \d+ \| BLOCKED: \d+' | Select-Object -Last 1)
    if ($null -ne $sumLine) {
      $summaryExtra = [string]$sumLine
      $ok = ($code -eq 0) -and [bool]($summaryExtra -match 'FAIL: 0\b')
    } else {
      $ok = $false
    }
  }
  $tail = ((Get-Content $stageLog -Tail 6 -EA SilentlyContinue) | Where-Object { $_ }) -join ' || '
  $verdict = 'PASS'
  if (-not $ok) { $verdict = 'FAIL'; $stageFails++ }
  RLog (('STAGE-' + $verdict + ' ' + $s.Id + ' exit=' + $code + ' dur_s=' + [int]$sw.Elapsed.TotalSeconds + ' ' + $summaryExtra + ' tail=' + $tail))
  if ($s.Id -eq $legacyLastId) { Stop-LegacyStack }
}

RLog ('REHEARSAL-FIM stage_fails=' + $stageFails + ' ts=' + (Get-Date -Format 'o'))
Write-Host ('=== PILOT REHEARSAL: stage_fails=' + $stageFails + ' (transcript: ' + $rehearsal + ') ===')
if ($stageFails -gt 0) { exit 1 }
exit 0
