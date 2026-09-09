-- ODCA Solutions canonical PostgreSQL 18 schema.
-- Execute against an existing database as a role allowed to create schemas and roles.
-- This is plain SQL for pgAdmin Query Tool and the Odca.Bootstrap runner.
-- Never run this file with the production application login.

-- ODCA-MIGRATION 001 CHECKSUM a99390078481a4fc3fe91f038e1c71995f6908bac115f83a66c8c1ac1ffcb518
BEGIN;
SELECT pg_advisory_xact_lock(hashtext('odca.schema.migrations'));

CREATE SCHEMA IF NOT EXISTS odca;

DO $odca$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'odca_app') THEN
        CREATE ROLE odca_app NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOBYPASSRLS;
    END IF;
END
$odca$;

CREATE TABLE IF NOT EXISTS odca.schema_migrations
(
    version integer PRIMARY KEY,
    name text NOT NULL,
    checksum char(64) NOT NULL,
    applied_at timestamptz NOT NULL DEFAULT now()
);

DO $odca$
DECLARE
    expected_checksum constant char(64) := 'a99390078481a4fc3fe91f038e1c71995f6908bac115f83a66c8c1ac1ffcb518';
    recorded_checksum char(64);
BEGIN
    SELECT checksum INTO recorded_checksum
      FROM odca.schema_migrations
     WHERE version = 1;

    IF recorded_checksum IS NOT NULL AND recorded_checksum <> expected_checksum THEN
        RAISE EXCEPTION 'ODCA migration 001 checksum mismatch: stored %, expected %',
            recorded_checksum, expected_checksum;
    END IF;
END
$odca$;

CREATE TABLE IF NOT EXISTS odca.users
(
    id uuid PRIMARY KEY,
    email text NOT NULL,
    email_normalized text NOT NULL UNIQUE,
    login_normalized text NOT NULL UNIQUE,
    display_name text NOT NULL,
    password_hash text NOT NULL,
    security_version integer NOT NULL DEFAULT 1 CHECK (security_version > 0),
    must_change_password boolean NOT NULL DEFAULT true,
    is_platform_administrator boolean NOT NULL DEFAULT false,
    email_verified_at timestamptz,
    failed_login_count integer NOT NULL DEFAULT 0 CHECK (failed_login_count >= 0),
    locked_until timestamptz,
    last_login_at timestamptz,
    password_changed_at timestamptz,
    is_deleted boolean NOT NULL DEFAULT false,
    deleted_at timestamptz,
    deleted_by uuid REFERENCES odca.users(id),
    deletion_reason text,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT users_deletion_state_ck CHECK
    (
        (NOT is_deleted AND deleted_at IS NULL AND deleted_by IS NULL AND deletion_reason IS NULL)
        OR (is_deleted AND deleted_at IS NOT NULL AND deleted_by IS NOT NULL AND deletion_reason IS NOT NULL)
    )
);

CREATE TABLE IF NOT EXISTS odca.tenants
(
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    business_code text NOT NULL UNIQUE,
    display_name text NOT NULL,
    timezone text NOT NULL DEFAULT 'America/Sao_Paulo',
    status text NOT NULL DEFAULT 'active' CHECK (status IN ('pending', 'active', 'suspended')),
    is_deleted boolean NOT NULL DEFAULT false,
    deleted_at timestamptz,
    deleted_by uuid REFERENCES odca.users(id),
    deletion_reason text,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT tenants_deletion_state_ck CHECK
    (
        (NOT is_deleted AND deleted_at IS NULL AND deleted_by IS NULL AND deletion_reason IS NULL)
        OR (is_deleted AND deleted_at IS NOT NULL AND deleted_by IS NOT NULL AND deletion_reason IS NOT NULL)
    )
);

CREATE TABLE IF NOT EXISTS odca.memberships
(
    tenant_id uuid NOT NULL REFERENCES odca.tenants(id),
    user_id uuid NOT NULL REFERENCES odca.users(id),
    status text NOT NULL DEFAULT 'active' CHECK (status IN ('invited', 'active', 'blocked', 'inactive')),
    security_version integer NOT NULL DEFAULT 1 CHECK (security_version > 0),
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (tenant_id, user_id)
);

CREATE TABLE IF NOT EXISTS odca.permissions
(
    code text PRIMARY KEY,
    description text NOT NULL,
    delegable boolean NOT NULL DEFAULT false,
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS odca.roles
(
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    scope_type text NOT NULL CHECK (scope_type IN ('platform', 'tenant')),
    tenant_id uuid REFERENCES odca.tenants(id),
    code text NOT NULL,
    display_name text NOT NULL,
    is_system boolean NOT NULL DEFAULT false,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT roles_scope_ck CHECK
    (
        (scope_type = 'platform' AND tenant_id IS NULL)
        OR (scope_type = 'tenant' AND tenant_id IS NOT NULL)
    )
);

CREATE UNIQUE INDEX IF NOT EXISTS roles_platform_code_uq
    ON odca.roles (code) WHERE scope_type = 'platform';
CREATE UNIQUE INDEX IF NOT EXISTS roles_tenant_code_uq
    ON odca.roles (tenant_id, code) WHERE scope_type = 'tenant';

CREATE TABLE IF NOT EXISTS odca.role_permissions
(
    role_id uuid NOT NULL REFERENCES odca.roles(id),
    permission_code text NOT NULL REFERENCES odca.permissions(code),
    created_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (role_id, permission_code)
);

CREATE TABLE IF NOT EXISTS odca.member_roles
(
    tenant_id uuid NOT NULL,
    user_id uuid NOT NULL,
    role_id uuid NOT NULL REFERENCES odca.roles(id),
    assigned_by uuid NOT NULL REFERENCES odca.users(id),
    assigned_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (tenant_id, user_id, role_id),
    FOREIGN KEY (tenant_id, user_id) REFERENCES odca.memberships(tenant_id, user_id)
);

CREATE TABLE IF NOT EXISTS odca.sessions
(
    id uuid PRIMARY KEY,
    user_id uuid NOT NULL REFERENCES odca.users(id),
    security_version integer NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    expires_at timestamptz NOT NULL,
    revoked_at timestamptz,
    ip_address inet,
    user_agent varchar(512),
    CHECK (expires_at > created_at),
    CHECK (revoked_at IS NULL OR revoked_at >= created_at)
);

CREATE INDEX IF NOT EXISTS sessions_user_active_ix
    ON odca.sessions (user_id, expires_at) WHERE revoked_at IS NULL;

CREATE TABLE IF NOT EXISTS odca.audit_events
(
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    scope_type text NOT NULL CHECK (scope_type IN ('platform', 'tenant')),
    tenant_id uuid REFERENCES odca.tenants(id),
    actor_user_id uuid REFERENCES odca.users(id),
    support_session_id uuid,
    action text NOT NULL,
    entity_type text NOT NULL,
    entity_id uuid,
    occurred_at timestamptz NOT NULL DEFAULT now(),
    result text NOT NULL CHECK (result IN ('success', 'denied', 'failed')),
    correlation_id uuid,
    metadata jsonb NOT NULL DEFAULT '{}'::jsonb,
    CONSTRAINT audit_scope_ck CHECK
    (
        (scope_type = 'platform' AND tenant_id IS NULL)
        OR (scope_type = 'tenant' AND tenant_id IS NOT NULL)
    )
);

CREATE INDEX IF NOT EXISTS audit_events_tenant_time_ix
    ON odca.audit_events (tenant_id, occurred_at DESC) WHERE tenant_id IS NOT NULL;

CREATE TABLE IF NOT EXISTS odca.processing_activities
(
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    version integer NOT NULL CHECK (version > 0),
    activity_code text NOT NULL,
    activity_name text NOT NULL,
    purpose text NOT NULL,
    data_subject_categories text[] NOT NULL,
    data_categories text[] NOT NULL,
    data_origin text NOT NULL,
    necessity text NOT NULL,
    proposed_legal_basis text NOT NULL,
    legal_validation_status text NOT NULL CHECK
        (legal_validation_status IN ('pending', 'approved', 'rejected')),
    responsible_role text NOT NULL,
    systems text[] NOT NULL,
    recipients text[] NOT NULL,
    countries text[] NOT NULL,
    retention_reference text NOT NULL,
    controls text[] NOT NULL,
    reviewed_at date,
    created_at timestamptz NOT NULL DEFAULT now(),
    UNIQUE (activity_code, version)
);

CREATE TABLE IF NOT EXISTS odca.privacy_contacts
(
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    scope_type text NOT NULL CHECK (scope_type IN ('platform', 'tenant')),
    tenant_id uuid REFERENCES odca.tenants(id),
    contact_role text NOT NULL,
    public_email text,
    public_url text,
    is_published boolean NOT NULL DEFAULT false,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT privacy_contacts_scope_ck CHECK
    (
        (scope_type = 'platform' AND tenant_id IS NULL)
        OR (scope_type = 'tenant' AND tenant_id IS NOT NULL)
    ),
    CONSTRAINT privacy_contacts_destination_ck CHECK (public_email IS NOT NULL OR public_url IS NOT NULL)
);

ALTER TABLE odca.tenants ENABLE ROW LEVEL SECURITY;
ALTER TABLE odca.tenants FORCE ROW LEVEL SECURITY;
ALTER TABLE odca.memberships ENABLE ROW LEVEL SECURITY;
ALTER TABLE odca.memberships FORCE ROW LEVEL SECURITY;
ALTER TABLE odca.roles ENABLE ROW LEVEL SECURITY;
ALTER TABLE odca.roles FORCE ROW LEVEL SECURITY;
ALTER TABLE odca.member_roles ENABLE ROW LEVEL SECURITY;
ALTER TABLE odca.member_roles FORCE ROW LEVEL SECURITY;
ALTER TABLE odca.privacy_contacts ENABLE ROW LEVEL SECURITY;
ALTER TABLE odca.privacy_contacts FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS tenant_isolation ON odca.tenants;
CREATE POLICY tenant_isolation ON odca.tenants TO odca_app
    USING (id = NULLIF(current_setting('odca.tenant_id', true), '')::uuid)
    WITH CHECK (id = NULLIF(current_setting('odca.tenant_id', true), '')::uuid);

DROP POLICY IF EXISTS tenant_isolation ON odca.memberships;
CREATE POLICY tenant_isolation ON odca.memberships TO odca_app
    USING (tenant_id = NULLIF(current_setting('odca.tenant_id', true), '')::uuid)
    WITH CHECK (tenant_id = NULLIF(current_setting('odca.tenant_id', true), '')::uuid);

DROP POLICY IF EXISTS tenant_isolation ON odca.roles;
CREATE POLICY tenant_isolation ON odca.roles TO odca_app
    USING (scope_type = 'tenant' AND tenant_id = NULLIF(current_setting('odca.tenant_id', true), '')::uuid)
    WITH CHECK (scope_type = 'tenant' AND tenant_id = NULLIF(current_setting('odca.tenant_id', true), '')::uuid);

DROP POLICY IF EXISTS tenant_isolation ON odca.member_roles;
CREATE POLICY tenant_isolation ON odca.member_roles TO odca_app
    USING (tenant_id = NULLIF(current_setting('odca.tenant_id', true), '')::uuid)
    WITH CHECK (tenant_id = NULLIF(current_setting('odca.tenant_id', true), '')::uuid);

DROP POLICY IF EXISTS tenant_isolation ON odca.privacy_contacts;
CREATE POLICY tenant_isolation ON odca.privacy_contacts TO odca_app
    USING (scope_type = 'tenant' AND tenant_id = NULLIF(current_setting('odca.tenant_id', true), '')::uuid)
    WITH CHECK (scope_type = 'tenant' AND tenant_id = NULLIF(current_setting('odca.tenant_id', true), '')::uuid);

INSERT INTO odca.permissions (code, description, delegable)
VALUES
    ('platform.dashboard.read', 'Consultar indicadores operacionais mínimos da plataforma.', false),
    ('platform.identity.manage', 'Administrar identidades da plataforma.', false),
    ('platform.audit.read', 'Consultar auditoria da plataforma.', false)
ON CONFLICT (code) DO NOTHING;

INSERT INTO odca.roles (id, scope_type, tenant_id, code, display_name, is_system)
VALUES ('00000000-0000-0000-0000-000000000001', 'platform', NULL,
        'super-administrator', 'SuperAdministrador', true)
ON CONFLICT DO NOTHING;

INSERT INTO odca.role_permissions (role_id, permission_code)
SELECT '00000000-0000-0000-0000-000000000001'::uuid, p.code
  FROM odca.permissions p
 WHERE p.code LIKE 'platform.%'
ON CONFLICT DO NOTHING;

INSERT INTO odca.processing_activities
    (id, version, activity_code, activity_name, purpose, data_subject_categories,
     data_categories, data_origin, necessity, proposed_legal_basis,
     legal_validation_status, responsible_role, systems, recipients, countries,
     retention_reference, controls)
VALUES
    ('10000000-0000-0000-0000-000000000001', 1, 'identity-access',
     'Identidade e controle de acesso',
     'Autenticar usuários, proteger contas e registrar eventos de segurança.',
     ARRAY['usuários da plataforma'], ARRAY['identificação', 'contato', 'credenciais derivadas', 'eventos de segurança'],
     'Cadastro direto e uso da plataforma',
     'Sem estes dados não é possível autenticar individualmente nem revogar acessos.',
     'Execução de contrato e legítimo interesse de segurança — validação jurídica pendente',
     'pending', 'Privacidade e Segurança', ARRAY['PostgreSQL', 'API ODCA'],
     ARRAY['Equipe autorizada da plataforma'], ARRAY['Brasil'],
     'RETENTION_POLICY.md#identidade-e-seguranca',
     ARRAY['hash de senha', 'bloqueio por tentativas', 'sessão revogável', 'logging minimizado'])
ON CONFLICT (activity_code, version) DO NOTHING;

INSERT INTO odca.schema_migrations (version, name, checksum)
VALUES (1, 'S00 foundation', 'a99390078481a4fc3fe91f038e1c71995f6908bac115f83a66c8c1ac1ffcb518')
ON CONFLICT (version) DO NOTHING;

GRANT USAGE ON SCHEMA odca TO odca_app;
GRANT SELECT, INSERT, UPDATE ON odca.users, odca.sessions TO odca_app;
GRANT SELECT ON odca.permissions, odca.processing_activities TO odca_app;
GRANT SELECT, INSERT ON odca.audit_events TO odca_app;
GRANT SELECT, INSERT, UPDATE ON odca.tenants, odca.memberships, odca.roles,
    odca.role_permissions, odca.member_roles, odca.privacy_contacts TO odca_app;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA odca TO odca_app;

COMMIT;
-- ODCA-END 001

-- ODCA-MIGRATION 002 CHECKSUM d2396cee28c324a2ce89681d6cf236c1cf9f608ecfc3af4a65156c1d594b90e0
BEGIN;
SELECT pg_advisory_xact_lock(hashtext('odca.schema.migrations'));

DO $odca$
DECLARE
    expected_checksum constant char(64) := 'd2396cee28c324a2ce89681d6cf236c1cf9f608ecfc3af4a65156c1d594b90e0';
    recorded_checksum char(64);
BEGIN
    SELECT checksum INTO recorded_checksum
      FROM odca.schema_migrations
     WHERE version = 2;

    IF recorded_checksum IS NOT NULL AND recorded_checksum <> expected_checksum THEN
        RAISE EXCEPTION 'ODCA migration 002 checksum mismatch: stored %, expected %',
            recorded_checksum, expected_checksum;
    END IF;
END
$odca$;

DO $odca$
BEGIN
    IF EXISTS
    (
        SELECT 1
          FROM odca.member_roles mr
          JOIN odca.roles r ON r.id = mr.role_id
         WHERE r.scope_type <> 'tenant' OR r.tenant_id IS DISTINCT FROM mr.tenant_id
    ) THEN
        RAISE EXCEPTION 'ODCA migration 002 found member_roles linked to a role from another scope or tenant.';
    END IF;

    IF NOT EXISTS
    (
        SELECT 1
          FROM pg_constraint
         WHERE conrelid = 'odca.roles'::regclass
           AND conname = 'roles_tenant_id_id_uq'
    ) THEN
        ALTER TABLE odca.roles
            ADD CONSTRAINT roles_tenant_id_id_uq UNIQUE (tenant_id, id);
    END IF;

    ALTER TABLE odca.member_roles DROP CONSTRAINT IF EXISTS member_roles_role_id_fkey;
    IF NOT EXISTS
    (
        SELECT 1
          FROM pg_constraint
         WHERE conrelid = 'odca.member_roles'::regclass
           AND conname = 'member_roles_tenant_role_fk'
    ) THEN
        ALTER TABLE odca.member_roles
            ADD CONSTRAINT member_roles_tenant_role_fk
            FOREIGN KEY (tenant_id, role_id) REFERENCES odca.roles(tenant_id, id);
    END IF;
END
$odca$;

ALTER TABLE odca.role_permissions ENABLE ROW LEVEL SECURITY;
ALTER TABLE odca.role_permissions FORCE ROW LEVEL SECURITY;
ALTER TABLE odca.audit_events ENABLE ROW LEVEL SECURITY;
ALTER TABLE odca.audit_events FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS tenant_isolation ON odca.role_permissions;
CREATE POLICY tenant_isolation ON odca.role_permissions TO odca_app
    USING
    (
        EXISTS
        (
            SELECT 1
              FROM odca.roles r
             WHERE r.id = role_permissions.role_id
               AND r.scope_type = 'tenant'
               AND r.tenant_id = NULLIF(current_setting('odca.tenant_id', true), '')::uuid
        )
    )
    WITH CHECK
    (
        EXISTS
        (
            SELECT 1
              FROM odca.roles r
             WHERE r.id = role_permissions.role_id
               AND r.scope_type = 'tenant'
               AND r.tenant_id = NULLIF(current_setting('odca.tenant_id', true), '')::uuid
        )
    );

DROP POLICY IF EXISTS tenant_read ON odca.audit_events;
DROP POLICY IF EXISTS tenant_insert ON odca.audit_events;
DROP POLICY IF EXISTS platform_security_insert ON odca.audit_events;
CREATE POLICY tenant_read ON odca.audit_events FOR SELECT TO odca_app
    USING
    (
        scope_type = 'tenant'
        AND tenant_id = NULLIF(current_setting('odca.tenant_id', true), '')::uuid
    );
CREATE POLICY tenant_insert ON odca.audit_events FOR INSERT TO odca_app
    WITH CHECK
    (
        scope_type = 'tenant'
        AND tenant_id = NULLIF(current_setting('odca.tenant_id', true), '')::uuid
    );
CREATE POLICY platform_security_insert ON odca.audit_events FOR INSERT TO odca_app
    WITH CHECK
    (
        scope_type = 'platform'
        AND tenant_id IS NULL
        AND action LIKE 'identity.%'
    );

CREATE OR REPLACE FUNCTION odca.platform_dashboard_snapshot(requesting_user_id uuid)
RETURNS TABLE(active_tenants integer, active_users integer, pending_privacy_items integer)
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, odca
AS $function$
BEGIN
    IF requesting_user_id IS DISTINCT FROM
           NULLIF(current_setting('odca.user_id', true), '')::uuid
       OR NOT EXISTS
    (
        SELECT 1
          FROM odca.users u
         WHERE u.id = requesting_user_id
           AND u.is_platform_administrator
           AND NOT u.is_deleted
    ) THEN
        RAISE EXCEPTION 'platform administrator required' USING ERRCODE = '42501';
    END IF;

    RETURN QUERY
    SELECT
        (SELECT count(*)::integer FROM odca.tenants t WHERE t.status = 'active' AND NOT t.is_deleted),
        (SELECT count(*)::integer FROM odca.users u WHERE NOT u.is_deleted),
        (SELECT count(*)::integer FROM odca.processing_activities p WHERE p.legal_validation_status = 'pending');
END
$function$;
REVOKE ALL ON FUNCTION odca.platform_dashboard_snapshot(uuid) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION odca.platform_dashboard_snapshot(uuid) TO odca_app;

CREATE TABLE IF NOT EXISTS odca.plan_versions
(
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    code text NOT NULL,
    version integer NOT NULL CHECK (version > 0),
    display_name text NOT NULL,
    status text NOT NULL CHECK (status IN ('draft', 'published', 'retired')),
    effective_from timestamptz NOT NULL,
    effective_until timestamptz,
    created_at timestamptz NOT NULL DEFAULT now(),
    UNIQUE (code, version),
    CHECK (effective_until IS NULL OR effective_until > effective_from)
);

CREATE TABLE IF NOT EXISTS odca.plan_entitlements
(
    plan_version_id uuid NOT NULL REFERENCES odca.plan_versions(id),
    entitlement_code text NOT NULL,
    limit_value bigint,
    enabled boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (plan_version_id, entitlement_code),
    CHECK (limit_value IS NULL OR limit_value >= 0)
);

INSERT INTO odca.plan_versions (id, code, version, display_name, status, effective_from)
VALUES
    ('30000000-0000-0000-0000-000000000001', 'basic', 1, 'Basic', 'published', '2026-09-09T00:00:00Z'),
    ('30000000-0000-0000-0000-000000000002', 'intermediate', 1, 'Intermediário', 'published', '2026-09-09T00:00:00Z'),
    ('30000000-0000-0000-0000-000000000003', 'enterprise', 1, 'Enterprise', 'published', '2026-09-09T00:00:00Z')
ON CONFLICT (code, version) DO NOTHING;

INSERT INTO odca.plan_entitlements (plan_version_id, entitlement_code, limit_value, enabled)
VALUES
    ('30000000-0000-0000-0000-000000000001', 'active_seats', 3, true),
    ('30000000-0000-0000-0000-000000000001', 'storage_bytes', 10000000000, true),
    ('30000000-0000-0000-0000-000000000001', 'user_storage_bytes', 5000000000, true),
    ('30000000-0000-0000-0000-000000000001', 'file_bytes', 25000000, true),
    ('30000000-0000-0000-0000-000000000001', 'ocr_pages_monthly', 300, true),
    ('30000000-0000-0000-0000-000000000001', 'signature_envelopes_monthly', 10, true),
    ('30000000-0000-0000-0000-000000000002', 'active_seats', 10, true),
    ('30000000-0000-0000-0000-000000000002', 'storage_bytes', 100000000000, true),
    ('30000000-0000-0000-0000-000000000002', 'user_storage_bytes', 25000000000, true),
    ('30000000-0000-0000-0000-000000000002', 'file_bytes', 100000000, true),
    ('30000000-0000-0000-0000-000000000002', 'ocr_pages_monthly', 3000, true),
    ('30000000-0000-0000-0000-000000000002', 'signature_envelopes_monthly', 50, true),
    ('30000000-0000-0000-0000-000000000003', 'active_seats', 30, true),
    ('30000000-0000-0000-0000-000000000003', 'storage_bytes', 500000000000, true),
    ('30000000-0000-0000-0000-000000000003', 'user_storage_bytes', 100000000000, true),
    ('30000000-0000-0000-0000-000000000003', 'file_bytes', 250000000, true),
    ('30000000-0000-0000-0000-000000000003', 'ocr_pages_monthly', 15000, true),
    ('30000000-0000-0000-0000-000000000003', 'signature_envelopes_monthly', 200, true)
ON CONFLICT (plan_version_id, entitlement_code) DO NOTHING;

GRANT SELECT ON odca.plan_versions, odca.plan_entitlements TO odca_app;

INSERT INTO odca.schema_migrations (version, name, checksum)
VALUES (2, 'S00 isolation fixes and S01 plan catalog', 'd2396cee28c324a2ce89681d6cf236c1cf9f608ecfc3af4a65156c1d594b90e0')
ON CONFLICT (version) DO NOTHING;

COMMIT;
-- ODCA-END 002
