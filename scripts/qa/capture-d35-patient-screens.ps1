# D-OC36 - capturas visuais das telas da area de paciente: 360/768/1280/1440 + zoom 200% x 4 idiomas.
# Sessao cliente.teste@odca.local (papel tenant-client; pacientes.read+manage; sem MFA).
# Requer que a prova d35 tenha rodado (ou insere o fixture do paciente A se ausente).
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$root   = 'C:\MNSOFT\odcasolutions'
$logDir = 'C:\Users\NCELL-DEV-020\AppData\Local\Temp\opencode'
$evDir  = Join-Path $logDir 'evidence'
$outDir = Join-Path $evDir 'captures-d35'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$evidence = Join-Path $evDir 'd35-captures.txt'
Set-Content -Path $evidence '' -Encoding utf8
function LogEv([string]$m) { Add-Content -Path $evidence $m -Encoding utf8 }
function Db([string]$q) {
  $env:PGPASSWORD = '123456'; $env:PGCONNECT_TIMEOUT = '10'
  $out = & 'C:\Program Files\PostgreSQL\18\bin\psql.exe' -h 127.0.0.1 -U postgres -d odca_test_disposable -t -A -c $q 2>&1 | Out-String
  return $out.Trim()
}

$t        = '20000000-0000-4000-8000-000000000001'
$patientA = '75c87013-4595-41dd-93d9-ac2e9c1f8fc4'
$uCli     = '10000000-0000-4000-8000-000000000003'
$cntA = [long](Db "SELECT count(*) FROM odca.patients WHERE id='$patientA' AND tenant_id='$t' AND inactive_at IS NULL;")
if ($cntA -ne 1) {
  LogEv 'fixture do paciente A ausente — inserindo estado deterministico'
  $null = Db ("INSERT INTO odca.patients (id, tenant_id, full_name, email, identifier_type, identifier_value, identifier_normalized, row_version, created_by, updated_by) VALUES ('$patientA', '$t', 'Ana Paula Ferreira da Costa', 'ana.paula.prova@odca.local', 'cpf', '11144777735', '11144777735', 1, '$uCli', '$uCli') ON CONFLICT (id) DO NOTHING;")
}
LogEv ("patientA count=" + $cntA)

# ---------- stack http-pura (api+web; worker nao exigido pelas telas da S2) ----------
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
$pa = Start-Process dotnet -ArgumentList ('exec "{0}"' -f (Join-Path $root 'src\Odca.Api\bin\Debug\net10.0\Odca.Api.dll')) -WorkingDirectory (Join-Path $root 'src\Odca.Api') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $logDir 'd35-cap-api.log') -RedirectStandardError (Join-Path $logDir 'd35-cap-api-err.log')
$env:ASPNETCORE_URLS = 'http://localhost:5144'
$pw = Start-Process dotnet -ArgumentList ('exec "{0}"' -f (Join-Path $root 'src\Odca.Web\bin\Debug\net10.0\Odca.Web.dll')) -WorkingDirectory (Join-Path $root 'src\Odca.Web') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $logDir 'd35-cap-web.log') -RedirectStandardError (Join-Path $logDir 'd35-cap-web-err.log')
$stackPids = @($pa.Id, $pw.Id)
if (-not (Wait-Ready 'http://localhost:5143/health/ready')) { Write-Host 'API-NUNCA-PRONTA'; exit 2 }
if (-not (Wait-Ready 'http://localhost:5144/entrar')) { Write-Host 'WEB-NUNCA-PRONTA'; exit 2 }
LogEv 'STACK pronta (http puro)'

# Cliente nao tem MFA: sem bootstrap/reset-mfa nesta etapa.
Db "UPDATE odca.users SET must_change_password=false WHERE email ILIKE 'cliente.teste@odca.local';" | Out-Null

$cfgObj = [ordered]@{
  base = 'http://localhost:5144'
  outDir = $outDir
  edge = 'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe'
  langs = @('pt-BR', 'en-US', 'es-ES', 'fr-FR')
  viewports = @( @(360, 780), @(768, 1024), @(1280, 800), @(1440, 900) )
  zoomAt = @(1280, 800)
  creds = [ordered]@{
    cliente = [ordered]@{ email = 'cliente.teste@odca.local'; pass = 'K8@wR3!nF6#zP2$m' }
  }
  screens = @(
    [ordered]@{ id = 'd35-pacientes-list';  session = 'cliente'; path = "/organizacoes/$t/pacientes" },
    [ordered]@{ id = 'd35-paciente-novo';   session = 'cliente'; path = "/organizacoes/$t/pacientes/novo" },
    [ordered]@{ id = 'd35-paciente-ficha';  session = 'cliente'; path = "/organizacoes/$t/pacientes/$patientA" }
  )
}
$cfgFile = Join-Path $logDir 'd35-cfg.json'
Set-Content -Path $cfgFile -Value ($cfgObj | ConvertTo-Json -Depth 10) -Encoding utf8
LogEv ('CAPTURES: telas=3 langs=4 viewports=4+zoom2x => 60 png em ' + $outDir)
$driver = Join-Path $root 'scripts\qa\cdp-capture-v2.mjs'
if (-not (Test-Path $driver)) { $driver = Join-Path $logDir 'd31-cdp-v2.mjs' }
& node $driver $cfgFile 2>&1 | ForEach-Object { LogEv ('CDP| ' + $_); Write-Host $_ }
foreach ($id in $stackPids) { Stop-Process -Id $id -Force -ErrorAction SilentlyContinue }
exit $LASTEXITCODE
