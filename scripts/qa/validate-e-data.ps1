# Bloco E - Data and Operations Validation Script
# Run: powershell -ExecutionPolicy Bypass -File validate-e-data.ps1
$ErrorActionPreference = 'Stop'
$env:PGPASSWORD = '123456'
$psql = 'C:\Program Files\PostgreSQL\18\bin\psql.exe'
$srv = @('-h','127.0.0.1','-p','5432','-U','postgres','-d','odca_test_disposable')
$pass = 0; $fail = 0

function RunCheck([string]$name, [string]$query, [bool]$expectZero) {
    $result = & $psql $srv -t -c $query 2>&1
    $val = ($result | Select-Object -First 1).ToString().Trim()
    if ($expectZero) {
        if ($val -eq '0') {
            $script:pass++
            Write-Host "PASS  $name" -ForegroundColor Green
        } else {
            $script:fail++
            Write-Host "FAIL  $name ($val)" -ForegroundColor Red
        }
    } else {
        Write-Host "INFO  $name = $val" -ForegroundColor Cyan
    }
}

Write-Host ""
Write-Host "=== BLOCO E: DATA INTEGRITY VALIDATION ===" -ForegroundColor White
Write-Host ""
Write-Host "[Referential Integrity]" -ForegroundColor Yellow

RunCheck "orphan_memberships" "SELECT count(*) FROM odca.memberships m WHERE NOT EXISTS (SELECT 1 FROM odca.tenants t WHERE t.id = m.tenant_id);" $true
RunCheck "orphan_subscriptions" "SELECT count(*) FROM odca.subscriptions s WHERE NOT EXISTS (SELECT 1 FROM odca.tenants t WHERE t.id = s.tenant_id);" $true
RunCheck "orphan_contracts" "SELECT count(*) FROM odca.contracts c WHERE NOT EXISTS (SELECT 1 FROM odca.tenants t WHERE t.id = c.tenant_id AND NOT t.is_deleted);" $true
RunCheck "orphan_obligations" "SELECT count(*) FROM odca.contract_obligations o WHERE NOT EXISTS (SELECT 1 FROM odca.contracts c WHERE c.id = o.contract_id);" $true
RunCheck "orphan_versions" "SELECT count(*) FROM odca.generated_contract_versions dv WHERE NOT EXISTS (SELECT 1 FROM odca.contract_drafts cd WHERE cd.id = dv.draft_id);" $true
RunCheck "orphan_sigpreps" "SELECT count(*) FROM odca.signature_preparations sp WHERE sp.generated_version_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM odca.generated_contract_versions dv WHERE dv.id = sp.generated_version_id);" $true
RunCheck "storage_grants_orphan" "SELECT count(*) FROM odca.storage_capacity_grants g WHERE NOT EXISTS (SELECT 1 FROM odca.tenants t WHERE t.id = g.tenant_id);" $true
RunCheck "audit_orphans" "SELECT count(*) FROM odca.audit_events ae WHERE ae.scope_type='tenant' AND ae.tenant_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM odca.tenants t WHERE t.id = ae.tenant_id AND NOT t.is_deleted);" $true

Write-Host ""
Write-Host "[State Consistency]" -ForegroundColor Yellow

RunCheck "null_commercial_state" "SELECT count(*) FROM odca.subscriptions WHERE commercial_state IS NULL;" $true
RunCheck "contracts_version_0" "SELECT count(*) FROM odca.contracts WHERE version = 0;" $true
RunCheck "cycles_bad_number" "SELECT count(*) FROM odca.contract_renewal_cycles WHERE cycle_number < 1;" $true
RunCheck "changes_formalized_no_date" "SELECT count(*) FROM odca.contract_change_requests ccr WHERE ccr.status='formalized' AND ccr.formalized_at IS NULL;" $true
RunCheck "changes_applied_no_effective" "SELECT count(*) FROM odca.contract_change_requests ccr WHERE ccr.application_status='applied' AND ccr.effective_on IS NULL;" $true
RunCheck "solicitations_bad_status" "SELECT count(*) FROM odca.solicitations s WHERE s.status NOT IN ('open','in_progress','resolved','closed','paused');" $true

Write-Host ""
Write-Host "[Idempotency]" -ForegroundColor Yellow

RunCheck "dup_formalization_key" "SELECT count(*) FROM (SELECT formalization_idempotency_key FROM odca.contract_change_requests WHERE formalization_idempotency_key IS NOT NULL GROUP BY formalization_idempotency_key HAVING count(*) > 1) x;" $true

Write-Host ""
Write-Host "[Outbox Health]" -ForegroundColor Yellow

RunCheck "stuck_outbox" "SELECT count(*) FROM odca.notification_outbox WHERE attempt_count >= 5 AND sent_at IS NULL;" $true

Write-Host ""
Write-Host "[User Membership]" -ForegroundColor Yellow

RunCheck "deleted_user_active_member" "SELECT count(*) FROM odca.memberships m JOIN odca.users u ON u.id=m.user_id WHERE u.is_deleted AND m.status='active';" $true

Write-Host ""
Write-Host "[Informational]" -ForegroundColor Yellow

RunCheck "total_tenants" "SELECT count(*) || ' total, ' || count(*) FILTER (WHERE NOT is_deleted) || ' active' FROM odca.tenants;" $false
RunCheck "audit_events_total" "SELECT count(*) FROM odca.audit_events;" $false
RunCheck "audit_last_24h" "SELECT count(*) FROM odca.audit_events WHERE occurred_at > now() - interval '24 hours';" $false
RunCheck "tenants_pending_no_sub" "SELECT count(*) FROM odca.tenants t WHERE NOT t.is_deleted AND t.status='pending' AND NOT EXISTS (SELECT 1 FROM odca.subscriptions s WHERE s.tenant_id=t.id);" $false
RunCheck "schema_version" "SELECT max(version)::text FROM odca.schema_migrations;" $false

Write-Host ""
$color = if ($fail -eq 0) { 'Green' } else { 'Red' }
Write-Host ("=== RESULT: PASS={0} FAIL={1} ===" -f $pass, $fail) -ForegroundColor $color
if ($fail -gt 0) { exit 1 }
