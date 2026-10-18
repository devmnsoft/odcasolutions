# D-OC38 - capturas visuais da Sprint 4 (atendimento, avisos e prazos): 6 telas x 4 idiomas x
# 4 viewports + zoom 200% (120 png). Estado deterministico montado localmente (idem P0c da prova):
#   cadeia de assinatura com participante vinculado ao membro do cliente, papel de obrigacoes e
#   UMA obrigacao de demonstracao (criada via API se ausente, replay idempotente se presente).
# O sino exibe exatamente um aviso signature_pending (dedup por source_id, v059); o e-mail segue
# desabilitado por padrao (outbox vazia).
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$root   = 'C:\MNSOFT\odcasolutions'
$logDir = 'C:\Users\NCELL-DEV-020\AppData\Local\Temp\opencode'
$evDir  = Join-Path $logDir 'evidence'
$outDir = Join-Path $evDir 'captures-d38'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$evidence = Join-Path $evDir 'd38-captures.txt'
Set-Content -Path $evidence '' -Encoding utf8
function LogEv([string]$m) { Add-Content -Path $evidence $m -Encoding utf8 }
function Db([string]$q) {
  $env:PGPASSWORD = '123456'; $env:PGCONNECT_TIMEOUT = '10'
  $out = & 'C:\Program Files\PostgreSQL\18\bin\psql.exe' -h 127.0.0.1 -U postgres -d odca_test_disposable -t -A -c $q 2>&1 | Out-String
  return $out.Trim()
}

$t       = '20000000-0000-4000-8000-000000000001'
$uCli    = '10000000-0000-4000-8000-000000000003'
$clientEmail = 'cliente.teste@odca.local'
$clientPass  = 'K8@wR3!nF6#zP2$m'
$tpl     = '76000000-0000-0000-0000-000000000001'
$tplVer  = '76000000-0000-0000-0000-000000000101'
$contr   = '76000000-0000-0000-0000-000000000201'
$draft   = '76000000-0000-0000-0000-000000000301'
$vid     = '76000000-0000-0000-0000-000000000401'
$prep    = '76000000-0000-0000-0000-000000000501'
$part    = '76000000-0000-4000-8000-000000000601'
$partCli = '76000000-0000-4000-8000-000000000701'
$opId    = '76000000-0000-4000-8000-000000000901'
$pdfSha  = 'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855'
$roleD38 = '77000000-0000-4000-8000-000000000001'

# ---------- estado: cadeia de assinatura + papel + obrigacao (idempotente) ----------
$cntV = [long](Db "SELECT count(*) FROM odca.generated_contract_versions WHERE id='$vid' AND tenant_id='$t';")
if ($cntV -ne 1) {
  $seedSql = @"
INSERT INTO odca.contract_templates(id, owner_tenant_id, name, description, contract_type, scope, status, published_at, author_id, current_version)
VALUES ('$tpl', '$t', 'Modelo Prova D-OC38', 'Modelo sintetico da prova ao vivo', 'service', 'private', 'published', now(), '$uCli', 1);
INSERT INTO odca.contract_template_versions(id, template_id, version_number, content, fields, created_by, published_at)
VALUES ('$tplVer', '$tpl', 1, '{"type":"document","content":[]}'::jsonb, '[]'::jsonb, '$uCli', now());
INSERT INTO odca.contracts(id, tenant_id, title, reference) VALUES ('$contr', '$t', 'Contrato Prova D-OC38', 'S4-D38-OBRIGACOES');
INSERT INTO odca.contract_drafts(id, tenant_id, contract_id, source_template_id, source_template_version_id, content, fields, created_by, updated_by)
VALUES ('$draft', '$t', '$contr', '$tpl', '$tplVer', '{"type":"document","content":[]}'::jsonb, '[]'::jsonb, '$uCli', '$uCli');
INSERT INTO odca.generated_contract_versions(id, tenant_id, contract_id, draft_id, version_number, content_schema_version, content, fields, values, source_template_id, source_template_version_id, canonical_sha256, storage_key, byte_size, created_by, review_status, idempotency_key, draft_row_version, pdf_status, pdf_storage_key, pdf_sha256, pdf_byte_size, pdf_renderer_version, pdf_completed_at)
VALUES ('$vid', '$t', '$contr', '$draft', 1, 1, '{"type":"document","content":[]}'::jsonb, '[]'::jsonb, '[]'::jsonb, '$tpl', '$tplVer', '$pdfSha', 'studio/prova-d38-final.pdf', 1024, '$uCli', 'internally_approved', gen_random_uuid(), 1, 'completed', 'studio/prova-d38-final.pdf', '$pdfSha', 1024, 'odca-pdf-3', now());
INSERT INTO odca.signature_preparations(id, tenant_id, generated_version_id, status, row_version, created_by, updated_by, composition_revision, confirmed_revision, confirmed_pdf_storage_key, confirmed_pdf_sha256, confirmed_at, confirmed_by, confirmation_operation_id)
VALUES ('$prep', '$t', '$vid', 'confirmed', 1, '$uCli', '$uCli', 1, 1, 'studio/prova-d38-final.pdf', '$pdfSha', now(), '$uCli', '$opId');
INSERT INTO odca.signature_participants(id, client_id, tenant_id, preparation_id, composition_revision, participant_type, role, name, email, position, recorded_by, identity_membership_id)
VALUES ('$part', '$partCli', '$t', '$prep', 1, 'patient', 'Paciente', 'Paciente Prova D-OC38', 'sintetica@prova.odca.local', 1, '$uCli',
        (SELECT m.id FROM odca.memberships m WHERE m.tenant_id='$t' AND m.user_id='$uCli'));
DELETE FROM odca.member_roles WHERE tenant_id='$t' AND user_id='$uCli' AND role_id='$roleD38';
DELETE FROM odca.role_permissions WHERE role_id='$roleD38';
DELETE FROM odca.roles WHERE id='$roleD38';
INSERT INTO odca.roles(id, scope_type, tenant_id, code, display_name, is_system)
VALUES ('$roleD38', 'tenant', '$t', 'prova-obligacoes-d38', 'Prova Obrigacoes D-OC38', true);
INSERT INTO odca.role_permissions(role_id, permission_code)
SELECT '$roleD38', code FROM odca.permissions
WHERE code IN ('tenant.obligations.read','tenant.obligations.read_all','tenant.obligations.manage','tenant.obligations.fulfill','tenant.obligations.cancel','tenant.obligations.reopen','tenant.obligations.assign');
INSERT INTO odca.member_roles(tenant_id, user_id, role_id, assigned_by) VALUES ('$t', '$uCli', '$roleD38', '$uCli');
"@
  $seedFile = Join-Path $logDir 'd38-seed-capture.sql'
  [IO.File]::WriteAllText($seedFile, $seedSql, [Text.Encoding]::UTF8)
  $env:PGPASSWORD = '123456'
  & 'C:\Program Files\PostgreSQL\18\bin\psql.exe' -h 127.0.0.1 -U postgres -d odca_test_disposable -v ON_ERROR_STOP=1 -f $seedFile 2>&1 | ForEach-Object { LogEv ('SEED| ' + $_) }
  if ($LASTEXITCODE -ne 0) { Write-Host 'SEED-D38-FALHOU'; exit 2 }
}

$cntObl = [long](Db "SELECT count(*) FROM odca.contract_obligations WHERE tenant_id='$t' AND contract_id='$contr';")
if ($cntObl -eq 0) {
  $oblSql = @"
INSERT INTO odca.contract_obligations(id, tenant_id, contract_id, title, description, category, obligated_party, owner_id, due_date, priority, origin, created_by)
VALUES ('77000000-0000-4000-8000-000000000a01', '$t', '$contr', 'Prova obrigacao D-OC38', 'Demonstracao do centro de obrigacoes (Sprint 4)', 'other', 'Contratada', '$uCli', '2026-12-15', 'normal', 'manual', '$uCli');
INSERT INTO odca.obligation_events(tenant_id, obligation_id, actor_id, event_type, details)
VALUES ('$t', '77000000-0000-4000-8000-000000000a01', '$uCli', 'created', '{"dueDate":"2026-12-15"}'::jsonb);
"@
  $oblFile = Join-Path $logDir 'd38-seed-obligation.sql'
  [IO.File]::WriteAllText($oblFile, $oblSql, [Text.Encoding]::UTF8)
  & 'C:\Program Files\PostgreSQL\18\bin\psql.exe' -h 127.0.0.1 -U postgres -d odca_test_disposable -v ON_ERROR_STOP=1 -f $oblFile 2>&1 | ForEach-Object { LogEv ('SEED-OBL| ' + $_) }
  if ($LASTEXITCODE -ne 0) { Write-Host 'SEED-OBLIGACAO-FALHOU'; exit 2 }
}
$oblId = Db ("SELECT id FROM odca.contract_obligations WHERE tenant_id='$t' AND contract_id='$contr' ORDER BY due_date LIMIT 1;")
LogEv ("estado: obrigacao-exibicao=" + $oblId)

# ---------- stack (local-file para materializar o aviso no sino) ----------
$rootEsc = [Regex]::Escape($root)
Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -and $_.CommandLine -match $rootEsc -and $_.CommandLine -match 'Odca\.(Api|Web|Worker)\.(dll|csproj)' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
Start-Sleep -Seconds 2
$env:ODCA_RUNTIME_CONFIG = Join-Path $root 'src\Odca.Api\development-runtime.json'
$env:DOTNET_ENVIRONMENT = 'Development'
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ODCA_SETUP_ON_START = 'true'
$pickUp = Join-Path $logDir 'd38-pickup'
Remove-Item $pickUp -Recurse -Force -EA SilentlyContinue
New-Item -ItemType Directory -Force -Path $pickUp | Out-Null
$env:Signatures__Provider = 'local-file'
$env:Signatures__LocalFileDirectory = $pickUp
function Wait-Ready([string]$url) {
  for ($i = 1; $i -le 90; $i++) {
    Start-Sleep -Seconds 2
    try { $h = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 3; if ([int]$h.StatusCode -lt 500) { return $true } } catch {}
  }
  return $false
}
$apiBase = 'http://localhost:5143/api/v1'
$env:ASPNETCORE_URLS = 'http://localhost:5143'; $env:Api__BaseUrl = 'http://localhost:5143/'
$pa = Start-Process dotnet -ArgumentList ('exec "{0}"' -f (Join-Path $root 'src\Odca.Api\bin\Debug\net10.0\Odca.Api.dll')) -WorkingDirectory (Join-Path $root 'src\Odca.Api') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $logDir 'd38-cap-api.log') -RedirectStandardError (Join-Path $logDir 'd38-cap-api-err.log')
$env:ASPNETCORE_URLS = 'http://localhost:5144'
$pw = Start-Process dotnet -ArgumentList ('exec "{0}"' -f (Join-Path $root 'src\Odca.Web\bin\Debug\net10.0\Odca.Web.dll')) -WorkingDirectory (Join-Path $root 'src\Odca.Web') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $logDir 'd38-cap-web.log') -RedirectStandardError (Join-Path $logDir 'd38-cap-web-err.log')
$stackPids = @($pa.Id, $pw.Id)
if (-not (Wait-Ready 'http://localhost:5143/health/ready')) { Write-Host 'API-NUNCA-PRONTA'; exit 2 }
if (-not (Wait-Ready 'http://localhost:5144/entrar')) { Write-Host 'WEB-NUNCA-PRONTA'; exit 2 }
LogEv 'STACK pronta (local-file)'

# ---------- aviso no sino (envio do envelope; replay idempotente) ----------
try {
  $body = '{"Login":"' + $clientEmail + '","Password":"' + $clientPass + '"}'
  $login = Invoke-RestMethod -Uri "$apiBase/auth/login" -Method Post -ContentType 'application/json' -Body $body
  $hdr = @{ Authorization = 'Bearer ' + $login.accessToken }
  $null = Invoke-RestMethod -Uri "$apiBase/organizations/$t/studio/versions/$vid/signature-envelope" -Method Post -Headers $hdr
  $bell = [long](Db "SELECT count(*) FROM odca.user_notifications WHERE tenant_id='$t' AND user_id='$uCli' AND kind='signature_pending';")
  LogEv ("sino: signature_pending=$bell (esperado 1 por dedup de source_id)")
  if ($bell -lt 1) { Write-Host 'SINO-AUSENTE bell=' + $bell }
} catch { LogEv ('ENVIO-ENVELOPE-ERRO: ' + $_.Exception.Message) }

Db "UPDATE odca.users SET must_change_password=false WHERE email ILIKE '$clientEmail';" | Out-Null

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
    [ordered]@{ id = 'd38-central-solicitacoes'; session = 'cliente'; path = "/organizacoes/$t/central-solicitacoes" },
    [ordered]@{ id = 'd38-nova-solicitacao'; session = 'cliente'; path = "/organizacoes/$t/central-solicitacoes/nova" },
    [ordered]@{ id = 'd38-notificacoes'; session = 'cliente'; path = '/minha-conta/notificacoes' },
    [ordered]@{ id = 'd38-obrigacoes'; session = 'cliente'; path = "/organizacoes/$t/obrigacoes" },
    [ordered]@{ id = 'd38-obrigacoes-nova'; session = 'cliente'; path = "/organizacoes/$t/obrigacoes/nova" },
    [ordered]@{ id = 'd38-obrigacoes-acoes'; session = 'cliente'; path = "/organizacoes/$t/obrigacoes/$oblId/acoes" }
  )
}
$cfgFile = Join-Path $logDir 'd38-cfg.json'
Set-Content -Path $cfgFile -Value ($cfgObj | ConvertTo-Json -Depth 10) -Encoding utf8
LogEv ('CAPTURES: telas=6 langs=4 viewports=4+zoom2x => 120 png em ' + $outDir)
$driver = Join-Path $root 'scripts\qa\cdp-capture-v2.mjs'
& node $driver $cfgFile 2>&1 | ForEach-Object { LogEv ('CDP| ' + $_); Write-Host $_ }
foreach ($id in $stackPids) { Stop-Process -Id $id -Force -ErrorAction SilentlyContinue }
exit $LASTEXITCODE
