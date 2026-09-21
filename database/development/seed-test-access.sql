-- ODCA development-only seed for direct execution by PostgreSQL psql.
-- Never include this file in migrations or execute it outside a local Development database.
-- Passwords below are ASP.NET Identity v3 PBKDF2 hashes, never plaintext.
\set ON_ERROR_STOP on

DO $seed$
DECLARE
    v_administrator_id uuid := '10000000-0000-4000-8000-000000000001';
    v_operator_id uuid := '10000000-0000-4000-8000-000000000002';
    v_client_id uuid := '10000000-0000-4000-8000-000000000003';
    v_tenant_id uuid := '20000000-0000-4000-8000-000000000001';
    v_administrator_hash constant text := 'AQAAAAEAAYagAAAAEKOHw7HKKOUJMyS5YLtJwc0MPSI/338jNz1nPlczAAsV9KXNbHxKArzG3eVeP+Q1ng==';
    v_operator_hash constant text := 'AQAAAAEAAYagAAAAEAA+KTjOrdy+hmRV0S4UrevKoTQ748kfa818dpQEj3PI5afbLB4+7AIHYM2CIOn5Cg==';
    v_client_hash constant text := 'AQAAAAEAAYagAAAAEPrYP5+bAmzppnmcIu9rC+ZaU2VCILqcQjB6mxEZS5aVnRmdA3D086y6ftSJJEA+lg==';
    v_plan_version_id uuid;
BEGIN
    SELECT id INTO v_administrator_id FROM odca.users WHERE email_normalized='ADMIN@ODCA.LOCAL';
    v_administrator_id := COALESCE(v_administrator_id, '10000000-0000-4000-8000-000000000001'::uuid);
    SELECT id INTO v_operator_id FROM odca.users WHERE email_normalized='OPERADOR@ODCA.LOCAL';
    v_operator_id := COALESCE(v_operator_id, '10000000-0000-4000-8000-000000000002'::uuid);
    SELECT id INTO v_client_id FROM odca.users WHERE email_normalized='CLIENTE.TESTE@ODCA.LOCAL';
    v_client_id := COALESCE(v_client_id, '10000000-0000-4000-8000-000000000003'::uuid);
    SELECT id INTO v_tenant_id FROM odca.tenants WHERE business_code='ODCA-DEMO-LOCAL';
    v_tenant_id := COALESCE(v_tenant_id, '20000000-0000-4000-8000-000000000001'::uuid);

    IF inet_server_addr() IS NOT NULL AND NOT (inet_server_addr() << inet '127.0.0.0/8' OR inet_server_addr() = inet '::1') THEN
        RAISE EXCEPTION 'Development seed refused: PostgreSQL server is not local (%).', inet_server_addr();
    END IF;
    IF current_database() NOT IN ('postgres', 'odca') THEN
        RAISE EXCEPTION 'Development seed refused for database %.', current_database();
    END IF;

    SELECT id INTO v_plan_version_id FROM odca.plan_versions
     WHERE code = 'basic' AND status = 'published' AND effective_from <= now()
       AND (effective_until IS NULL OR effective_until > now())
     ORDER BY version DESC LIMIT 1;
    IF v_plan_version_id IS NULL THEN
        RAISE EXCEPTION 'No published, current Basic plan version exists. Run migrations first.';
    END IF;

    INSERT INTO odca.users (id,email,email_normalized,login_normalized,display_name,password_hash,must_change_password,is_platform_administrator,email_verified_at)
    VALUES
      (v_administrator_id,'admin@odca.local','ADMIN@ODCA.LOCAL','ADMIN@ODCA.LOCAL','Administrador da plataforma',v_administrator_hash,true,true,now()),
      (v_operator_id,'operador@odca.local','OPERADOR@ODCA.LOCAL','OPERADOR@ODCA.LOCAL','Operador da organização',v_operator_hash,true,false,now()),
      (v_client_id,'cliente.teste@odca.local','CLIENTE.TESTE@ODCA.LOCAL','CLIENTE.TESTE@ODCA.LOCAL','Cliente de demonstração',v_client_hash,true,false,now())
    ON CONFLICT (email_normalized) DO NOTHING;

    IF NOT EXISTS (SELECT 1 FROM odca.users WHERE email_normalized='ADMIN@ODCA.LOCAL' AND is_platform_administrator AND NOT is_deleted) OR
       NOT EXISTS (SELECT 1 FROM odca.users WHERE email_normalized='CLIENTE.TESTE@ODCA.LOCAL' AND NOT is_platform_administrator AND NOT is_deleted) THEN
        RAISE EXCEPTION 'A reserved local identity already exists with an incompatible id or privilege.';
    END IF;

    -- Direct psql execution restores the two documented credentials. The Bootstrap sets
    -- this transaction-local marker because it supplies freshly generated Identity hashes itself.
    IF current_setting('odca.bootstrap_seed', true) IS DISTINCT FROM 'on' THEN
        UPDATE odca.users
           SET password_hash = CASE id WHEN v_administrator_id THEN v_administrator_hash ELSE v_client_hash END,
               must_change_password=true, security_version=security_version+1,
               failed_login_count=0, locked_until=NULL, password_changed_at=NULL,
               email_verified_at=COALESCE(email_verified_at,now()), updated_at=now()
         WHERE id IN (v_administrator_id,v_client_id)
           AND password_hash IS DISTINCT FROM CASE id WHEN v_administrator_id THEN v_administrator_hash ELSE v_client_hash END;
        UPDATE odca.sessions SET revoked_at=COALESCE(revoked_at,now())
         WHERE user_id IN (v_administrator_id,v_client_id) AND revoked_at IS NULL;
    END IF;
    UPDATE odca.users SET failed_login_count=0, locked_until=NULL, email_verified_at=COALESCE(email_verified_at,now()), updated_at=now()
     WHERE id IN (v_administrator_id,v_operator_id,v_client_id);
    INSERT INTO odca.tenants (id,business_code,display_name,status) VALUES (v_tenant_id,'ODCA-DEMO-LOCAL','ODCA Cliente de Demonstração','active')
    ON CONFLICT (business_code) DO NOTHING;
    IF NOT EXISTS (SELECT 1 FROM odca.tenants WHERE business_code='ODCA-DEMO-LOCAL' AND status='active' AND NOT is_deleted) THEN
        RAISE EXCEPTION 'The reserved demo tenant exists with incompatible state or id.';
    END IF;

    INSERT INTO odca.memberships (tenant_id,user_id,status)
    SELECT v_tenant_id,u.id,'active' FROM odca.users u WHERE u.id IN (v_administrator_id,v_operator_id,v_client_id)
    ON CONFLICT (tenant_id,user_id) DO UPDATE SET status='active',updated_at=now();

    INSERT INTO odca.roles (scope_type,tenant_id,code,display_name,is_system) VALUES
      ('tenant',v_tenant_id,'tenant-administrator','Administrador da organização',true),
      ('tenant',v_tenant_id,'tenant-operator','Operador da organização',true),
      ('tenant',v_tenant_id,'tenant-client','Cliente da organização',true)
    ON CONFLICT DO NOTHING;

    INSERT INTO odca.role_permissions (role_id,permission_code)
    SELECT r.id,p.code FROM odca.roles r CROSS JOIN odca.permissions p
     WHERE r.tenant_id=v_tenant_id AND ((r.code IN ('tenant-administrator','tenant-client') AND p.code LIKE 'tenant.%') OR
       (r.code='tenant-operator' AND p.code IN ('tenant.obligations.read','tenant.obligations.read_all','tenant.obligations.manage','tenant.obligations.fulfill','tenant.obligations.reopen','tenant.obligations.cancel','tenant.obligations.assign','tenant.documents.download','tenant.renewals.read','tenant.renewals.prepare','tenant.templates.read','tenant.templates.manage','tenant.contract_drafts.read','tenant.contract_drafts.manage','tenant.reviews.read','tenant.reviews.manage','tenant.reviews.request','tenant.reviews.decide','tenant.imports.read','tenant.imports.manage','tenant.imports.confirm','tenant.saved_views.manage')))
    ON CONFLICT (role_id,permission_code) DO NOTHING;

    INSERT INTO odca.member_roles (tenant_id,user_id,role_id,assigned_by)
    SELECT v_tenant_id,x.user_id,r.id,v_administrator_id FROM (VALUES
      (v_administrator_id,'tenant-administrator'),(v_operator_id,'tenant-operator'),(v_client_id,'tenant-client')) x(user_id,role_code)
      JOIN odca.roles r ON r.tenant_id=v_tenant_id AND r.code=x.role_code
    ON CONFLICT (tenant_id,user_id,role_id) DO NOTHING;

    INSERT INTO odca.subscriptions (tenant_id,plan_version_id,commercial_state,status,manual_grant_reason,created_by)
    SELECT v_tenant_id,v_plan_version_id,'active','active','development-demo-access',v_client_id
    WHERE NOT EXISTS (SELECT 1 FROM odca.subscriptions WHERE tenant_id=v_tenant_id);

    INSERT INTO odca.audit_events (scope_type,actor_user_id,action,entity_type,entity_id,result,metadata)
    SELECT 'platform',v_administrator_id,'development.test_access.provisioned','user',v_administrator_id,'success',jsonb_build_object('source','psql-development-seed')
    WHERE NOT EXISTS (SELECT 1 FROM odca.audit_events WHERE actor_user_id=v_administrator_id AND action='development.test_access.provisioned');
    INSERT INTO odca.audit_events (scope_type,tenant_id,actor_user_id,action,entity_type,entity_id,result,metadata)
    SELECT 'tenant',v_tenant_id,v_client_id,'development.demo_access.granted','tenant',v_tenant_id,'success',jsonb_build_object('grant','development-demo-access')
    WHERE NOT EXISTS (SELECT 1 FROM odca.audit_events WHERE tenant_id=v_tenant_id AND action='development.demo_access.granted');
END
$seed$;
