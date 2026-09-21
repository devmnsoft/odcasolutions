-- ODCA development-only test access seed.
-- This is not a migration and is never executed by database/odca.sql.
-- Execute only through Odca.Bootstrap, which supplies every @parameter with Dapper,
-- validates Development/DatabaseAdmin, takes a transaction lock, and verifies all
-- post-conditions before commit. Password parameters are ASP.NET Identity hashes;
-- plaintext passwords must never be added to this file.

INSERT INTO odca.users
    (id, email, email_normalized, login_normalized, display_name, password_hash,
     must_change_password, is_platform_administrator, email_verified_at)
SELECT @AdministratorId, @AdministratorEmail, @AdministratorNormalized,
       @AdministratorNormalized, @AdministratorName, @AdministratorHash,
       @AdministratorMustChangePassword, true, now()
WHERE NOT EXISTS (SELECT 1 FROM odca.users WHERE email_normalized=@AdministratorNormalized);

UPDATE odca.users
   SET password_hash=@AdministratorHash, must_change_password=@AdministratorMustChangePassword,
       security_version=security_version+1, failed_login_count=0,
       locked_until=NULL, password_changed_at=NULL, updated_at=now()
 WHERE id=@AdministratorId AND @RotateAdministrator;
UPDATE odca.sessions SET revoked_at=COALESCE(revoked_at, now())
 WHERE user_id=@AdministratorId AND @RotateAdministrator;

INSERT INTO odca.users
    (id, email, email_normalized, login_normalized, display_name, password_hash,
     must_change_password, is_platform_administrator, email_verified_at)
SELECT @OperatorId, @OperatorEmail, @OperatorNormalized, @OperatorNormalized,
       @OperatorName, @OperatorHash, @OperatorMustChangePassword, false, now()
WHERE NOT EXISTS (SELECT 1 FROM odca.users WHERE email_normalized=@OperatorNormalized);

UPDATE odca.users
   SET password_hash=@OperatorHash, must_change_password=@OperatorMustChangePassword,
       security_version=security_version+1, failed_login_count=0,
       locked_until=NULL, password_changed_at=NULL, updated_at=now()
 WHERE id=@OperatorId AND @RotateOperator;
UPDATE odca.sessions SET revoked_at=COALESCE(revoked_at, now())
 WHERE user_id=@OperatorId AND @RotateOperator;

INSERT INTO odca.users
    (id, email, email_normalized, login_normalized, display_name, password_hash,
     must_change_password, is_platform_administrator, email_verified_at)
SELECT @ClientId, @ClientEmail, @ClientNormalized, @ClientNormalized,
       @ClientName, @ClientHash, @ClientMustChangePassword, false, now()
WHERE NOT EXISTS (SELECT 1 FROM odca.users WHERE email_normalized=@ClientNormalized);

UPDATE odca.users
   SET password_hash=@ClientHash, must_change_password=@ClientMustChangePassword,
       security_version=security_version+1, failed_login_count=0,
       locked_until=NULL, password_changed_at=NULL, updated_at=now()
 WHERE id=@ClientId AND @RotateClient;
UPDATE odca.sessions SET revoked_at=COALESCE(revoked_at, now())
 WHERE user_id=@ClientId AND @RotateClient;

INSERT INTO odca.tenants (id, business_code, display_name, status)
SELECT @TenantId, @TenantCode, @TenantName, 'active'
WHERE NOT EXISTS (SELECT 1 FROM odca.tenants WHERE business_code=@TenantCode);

INSERT INTO odca.memberships (tenant_id, user_id, status)
SELECT @TenantId, @AdministratorId, 'active'
WHERE NOT EXISTS (SELECT 1 FROM odca.memberships WHERE tenant_id=@TenantId AND user_id=@AdministratorId);

INSERT INTO odca.memberships (tenant_id, user_id, status)
SELECT @TenantId, @OperatorId, 'active'
WHERE NOT EXISTS (SELECT 1 FROM odca.memberships WHERE tenant_id=@TenantId AND user_id=@OperatorId);

INSERT INTO odca.memberships (tenant_id, user_id, status)
SELECT @TenantId, @ClientId, 'active'
WHERE NOT EXISTS (SELECT 1 FROM odca.memberships WHERE tenant_id=@TenantId AND user_id=@ClientId);

INSERT INTO odca.roles (scope_type, tenant_id, code, display_name, is_system)
SELECT 'tenant', @TenantId, 'tenant-administrator', 'Administrador da organização', true
WHERE NOT EXISTS (SELECT 1 FROM odca.roles WHERE tenant_id=@TenantId AND code='tenant-administrator');

INSERT INTO odca.role_permissions (role_id, permission_code)
SELECT r.id, p.code
  FROM odca.roles r CROSS JOIN odca.permissions p
 WHERE r.tenant_id=@TenantId AND r.code='tenant-administrator' AND p.code LIKE 'tenant.%'
ON CONFLICT (role_id, permission_code) DO NOTHING;

INSERT INTO odca.roles (scope_type, tenant_id, code, display_name, is_system)
SELECT 'tenant', @TenantId, 'tenant-operator', 'Operador da organização', true
WHERE NOT EXISTS (SELECT 1 FROM odca.roles WHERE tenant_id=@TenantId AND code='tenant-operator');

INSERT INTO odca.role_permissions (role_id, permission_code)
SELECT r.id, p.code
  FROM odca.roles r CROSS JOIN odca.permissions p
 WHERE r.tenant_id=@TenantId AND r.code='tenant-operator'
   AND p.code IN (
       'tenant.obligations.read',
       'tenant.obligations.read_all',
       'tenant.obligations.manage',
       'tenant.obligations.fulfill',
       'tenant.obligations.reopen',
       'tenant.obligations.cancel',
       'tenant.obligations.assign',
       'tenant.documents.download',
       'tenant.renewals.read',
       'tenant.renewals.prepare',
       'tenant.templates.read',
       'tenant.templates.manage',
       'tenant.contract_drafts.read',
       'tenant.contract_drafts.manage',
       'tenant.reviews.read',
       'tenant.reviews.manage',
       'tenant.reviews.request',
       'tenant.reviews.decide',
       'tenant.imports.read',
       'tenant.imports.manage',
       'tenant.imports.confirm',
       'tenant.saved_views.manage'
   )
ON CONFLICT (role_id, permission_code) DO NOTHING;

INSERT INTO odca.roles (scope_type, tenant_id, code, display_name, is_system)
SELECT 'tenant', @TenantId, 'tenant-client', 'Cliente da organização', true
WHERE NOT EXISTS (SELECT 1 FROM odca.roles WHERE tenant_id=@TenantId AND code='tenant-client');

INSERT INTO odca.role_permissions (role_id, permission_code)
SELECT r.id, p.code
  FROM odca.roles r CROSS JOIN odca.permissions p
 WHERE r.tenant_id=@TenantId AND r.code='tenant-client'
   AND p.code LIKE 'tenant.%'
ON CONFLICT (role_id, permission_code) DO NOTHING;

INSERT INTO odca.member_roles (tenant_id, user_id, role_id, assigned_by)
SELECT @TenantId, @AdministratorId, r.id, @AdministratorId
  FROM odca.roles r
 WHERE r.tenant_id=@TenantId AND r.code='tenant-administrator'
   AND NOT EXISTS (SELECT 1 FROM odca.member_roles mr WHERE mr.tenant_id=@TenantId
                    AND mr.user_id=@AdministratorId AND mr.role_id=r.id);

INSERT INTO odca.member_roles (tenant_id, user_id, role_id, assigned_by)
SELECT @TenantId, @OperatorId, r.id, @AdministratorId
  FROM odca.roles r
 WHERE r.tenant_id=@TenantId AND r.code='tenant-operator'
   AND NOT EXISTS (SELECT 1 FROM odca.member_roles mr WHERE mr.tenant_id=@TenantId
                    AND mr.user_id=@OperatorId AND mr.role_id=r.id);

INSERT INTO odca.member_roles (tenant_id, user_id, role_id, assigned_by)
SELECT @TenantId, @ClientId, r.id, @AdministratorId
  FROM odca.roles r
 WHERE r.tenant_id=@TenantId AND r.code='tenant-client'
   AND NOT EXISTS (SELECT 1 FROM odca.member_roles mr WHERE mr.tenant_id=@TenantId
                    AND mr.user_id=@ClientId AND mr.role_id=r.id);

INSERT INTO odca.subscriptions
    (tenant_id, plan_version_id, commercial_state, status, manual_grant_reason, created_by)
SELECT @TenantId, @PlanVersionId, 'active', 'active', @GrantReason, @ClientId
WHERE NOT EXISTS (SELECT 1 FROM odca.subscriptions WHERE tenant_id=@TenantId);

INSERT INTO odca.audit_events
    (scope_type, actor_user_id, action, entity_type, entity_id, result, metadata)
VALUES ('platform', @AdministratorId, 'development.test_access.provisioned',
        'user', @AdministratorId, 'success',
        jsonb_build_object('created', NOT EXISTS (
            SELECT 1 FROM odca.audit_events WHERE actor_user_id=@AdministratorId
              AND action='development.test_access.provisioned')));

INSERT INTO odca.audit_events
    (scope_type, tenant_id, actor_user_id, action, entity_type, entity_id, result, metadata)
VALUES ('tenant', @TenantId, @ClientId, 'development.demo_access.granted',
        'tenant', @TenantId, 'success', jsonb_build_object('grant', @GrantReason));

INSERT INTO odca.audit_events
    (scope_type, actor_user_id, action, entity_type, entity_id, result, metadata)
SELECT 'platform', @AdministratorId, 'development.password.reset', 'user',
       @AdministratorId, 'success', jsonb_build_object('mustChangePassword', true)
 WHERE @RotateAdministrator;

INSERT INTO odca.audit_events
    (scope_type, tenant_id, actor_user_id, action, entity_type, entity_id, result, metadata)
SELECT 'tenant', @TenantId, @OperatorId, 'development.password.reset', 'user',
       @OperatorId, 'success', jsonb_build_object('mustChangePassword', true)
 WHERE @RotateOperator;

INSERT INTO odca.audit_events
    (scope_type, tenant_id, actor_user_id, action, entity_type, entity_id, result, metadata)
SELECT 'tenant', @TenantId, @ClientId, 'development.password.reset', 'user',
       @ClientId, 'success', jsonb_build_object('mustChangePassword', true)
 WHERE @RotateClient;
