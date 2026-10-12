[CmdletBinding()]
param(
  [string]$Login = 'operador@odca.local',
  [string]$Password = 'La@RsjC5p-hmq_JpReGW',
  # Codigo de recuperacao MFA (fluxo /mfa/challenge) para superadministradores ja matriculados.
  # Exemplo: 'ODCA-OPERADOR-QA-RECOVERY-A'. Sem o codigo, tenta o fluxo enrollment regenerate+TOTP.
  [string]$MfaCode = '',
  # IMPORTANTE: usar https direto. PS 5.1 segue o redirect 307 http->https e REMOVE o header
  # Authorization na troca de authority, fazendo toda chamada autenticada voltar 401.
  [string]$Base = 'https://localhost:7143',
  [string]$TenantId = '20000000-0000-4000-8000-000000000001',
  [int]$PageSize = 100
)
$ErrorActionPreference = 'Stop'
$alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'

function Write-OdcaBody([string]$json, [string]$file) {
  $json | Out-File $file -Encoding ascii -NoNewline
}

function Invoke-OdcaApi([string]$method, [string]$url, [hashtable]$headers, [string]$bodyFile) {
  $argList = @('-sS', '-k', '-o', "$env:TEMP\odca-qa-last.json", '-X', $method, $url)
  foreach ($k in $headers.Keys) { $argList += @('-H', "$k : $($headers[$k])".Replace(' : ', ': ')) }
  if ($bodyFile) { $argList += @('-H', 'Content-Type: application/json', '--data-binary', "@$bodyFile") }
  & curl.exe @argList 2>&1 | Out-Null
  return (Get-Content "$env:TEMP\odca-qa-last.json" -Raw | ConvertFrom-Json)
}

function Get-Totp([string]$manualKey) {
  $clean = $manualKey.ToUpper().Replace('=', '').Replace(' ', '')
  $bits = [System.Collections.Generic.List[int]]::new()
  foreach ($ch in $clean.ToCharArray()) {
    $idx = [array]::IndexOf([char[]]$alphabet, $ch)
    if ($idx -lt 0) { throw "caractere base32 invalido: $ch" }
    for ($b = 4; $b -ge 0; $b--) { $bits.Add(($idx -shr $b) -band 1) }
  }
  $bytes = New-Object byte[] ([Math]::Floor($bits.Count / 8))
  for ($i = 0; $i -lt $bytes.Length; $i++) {
    $v = 0
    for ($j = 0; $j -lt 8; $j++) { $v = ($v -shl 1) -bor $bits[$i * 8 + $j] }
    $bytes[$i] = [byte]$v
  }
  $epoch = [Math]::Floor(((Get-Date).ToUniversalTime() - [DateTime]::new(1970,1,1,[DateTimeKind]::Utc)).TotalSeconds)
  $counter = [Math]::Floor($epoch / 30)
  $cb = New-Object byte[] 8
  for ($i = 7; $i -ge 0; $i--) { $cb[$i] = [byte]($counter -band 0xFF); $counter = [Math]::Floor($counter / 256) }
  $hmac = New-Object System.Security.Cryptography.HMACSHA1
  $hmac.Key = $bytes
  $hash = $hmac.ComputeHash($cb)
  $off = $hash[19] -band 0x0F
  $val = (([uint32]($hash[$off] -band 0x7F) -shl 24) -bor ([uint32]($hash[$off+1]) -shl 16) -bor ([uint32]($hash[$off+2]) -shl 8) -bor [uint32]($hash[$off+3])) % 1000000
  return ('{0}' -f $val).PadLeft(6, '0')
}

# 1) login
Write-OdcaBody (@{ login = $Login; password = $Password } | ConvertTo-Json) "$env:TEMP\odca-qa-body.json"
$resp = Invoke-OdcaApi 'POST' "$Base/api/v1/auth/login" @{} "$env:TEMP\odca-qa-body.json"
if (-not $resp.accessToken) { throw "login nao retornou token: $($resp | ConvertTo-Json -Depth 5)" }
$token = $resp.accessToken
$headers = @{ Authorization = "Bearer $token" }
Write-Host ("LOGIN {0}: mfaVerified={1} requiresMfaChallenge={2}" -f $Login, $resp.mfaVerified, $resp.requiresMfaChallenge)

# 2) MFA real: para superadmin ja matriculado usa /mfa/challenge com codigo de recuperacao;
#    caso contrario tenta o fluxo enrollment (regenerate -> TOTP -> confirm).
if ($resp.requiresMfaChallenge) {
  if ($MfaCode) {
    Write-OdcaBody (@{ code = $MfaCode } | ConvertTo-Json) "$env:TEMP\odca-qa-body.json"
    $conf = Invoke-OdcaApi 'POST' "$Base/api/v1/auth/mfa/challenge" $headers "$env:TEMP\odca-qa-body.json"
    if (-not $conf.accessToken) { throw "desafio MFA falhou: $($conf | ConvertTo-Json -Depth 5)" }
    if (-not $conf.mfaVerified) { throw 'token MFA nao veio com mfaVerified=true' }
    $token = $conf.accessToken
    $headers = @{ Authorization = "Bearer $token" }
    Write-Host ("MFA challenge: mfaVerified={0}" -f $conf.mfaVerified)
  }
  else {
    $reg = Invoke-OdcaApi 'POST' "$Base/api/v1/auth/mfa/enrollment/regenerate" $headers $null
    if (-not $reg.manualKey) { throw "regenerate MFA falhou: $($reg | ConvertTo-Json -Depth 5)" }
    Write-Host ("MFA regenerate: manualKey presente=true")
    Write-OdcaBody (@{ code = (Get-Totp $reg.manualKey) } | ConvertTo-Json) "$env:TEMP\odca-qa-body.json"
    $conf = Invoke-OdcaApi 'POST' "$Base/api/v1/auth/mfa/enrollment/confirm" $headers "$env:TEMP\odca-qa-body.json"
    if (-not $conf.accessToken) { throw "confirmacao MFA falhou: $($conf | ConvertTo-Json -Depth 5)" }
    $token = $conf.accessToken
    $headers = @{ Authorization = "Bearer $token" }
    Write-Host ("MFA confirmada: mfaVerified={0}" -f $conf.mfaVerified)
  }
}

# 3) inbox: escopo organization (visibilidade total dentro das permissoes)
$inbox = Invoke-OdcaApi 'GET' "$Base/api/v1/organizations/$TenantId/inbox?scope=organization&pageSize=$PageSize" $headers $null
Write-Host ("INBOX organization total={0}" -f $inbox.total)
$all = @($inbox.items)
Write-Host ("KINDS organization: " + (($all | Group-Object kind | ForEach-Object { "{0}={1}" -f $_.Name, $_.Count }) -join ', '))
Write-Host '--- amostras Solicitation:'
@($all | Where-Object { $_.kind -eq 'Solicitation' } | Select-Object -First 3) | ForEach-Object { Write-Host ("  [{0}] {1} (status={2})" -f $_.urgency, $_.title, $_.status) }
Write-Host '--- amostras Signature:'
@($all | Where-Object { $_.kind -eq 'Signature' } | Select-Object -First 3) | ForEach-Object { Write-Host ("  [{0}] {1} (status={2})" -f $_.urgency, $_.title, $_.status) }

# 4) inbox: escopo mine (participacao/abertura do usuario)
$mine = Invoke-OdcaApi 'GET' "$Base/api/v1/organizations/$TenantId/inbox?scope=mine&pageSize=$PageSize" $headers $null
Write-Host ("INBOX mine total={0} KINDS: {1}" -f $mine.total, ((@($mine.items) | Group-Object kind | ForEach-Object { "{0}={1}" -f $_.Name, $_.Count }) -join ', '))

# 5) filtros por kind: nenhum item pode vir de outro tipo
$onlySol = Invoke-OdcaApi 'GET' "$Base/api/v1/organizations/$TenantId/inbox?scope=organization&kind=Solicitation&pageSize=$PageSize" $headers $null
$badSol = @(@($onlySol.items) | Where-Object { $_.kind -ne 'Solicitation' }).Count
$onlySig = Invoke-OdcaApi 'GET' "$Base/api/v1/organizations/$TenantId/inbox?scope=organization&kind=Signature&pageSize=$PageSize" $headers $null
$badSig = @(@($onlySig.items) | Where-Object { $_.kind -ne 'Signature' }).Count
Write-Host ("FILTER kind=Solicitation total={0} incorretos={1}; kind=Signature total={2} incorretos={3}" -f $onlySol.total, $badSol, $onlySig.total, $badSig)

# 6) action center: contagem por fonte (visao complementar de acoes)
try {
  $ac = Invoke-OdcaApi 'GET' "$Base/api/v1/organizations/$TenantId/operations/action-center?scope=organization&pageSize=100" $headers $null
  $bySource = @{}
  foreach ($cat in @('myAction','waitingOther','upcomingDeadlines','overdue','conflict','info')) {
    foreach ($item in @($ac.$cat)) {
      $src = [string]$item.sourceType
      if ($bySource.ContainsKey($src)) { $bySource[$src]++ } else { $bySource[$src] = 1 }
    }
  }
  $parts = @()
  foreach ($src in @('renewal','signature','obligation','solicitation','contract')) {
    $parts += ("{0}={1}" -f $src, $(if ($bySource.ContainsKey($src)) { $bySource[$src] } else { 0 }))
  }
  Write-Host ("ACTION-CENTER total={0}: {1}" -f $ac.total, ($parts -join ', '))
} catch {
  Write-Host ("ACTION-CENTER falhou: " + $_.Exception.Message)
}
exit 0
