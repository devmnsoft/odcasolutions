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

-- ODCA-MIGRATION 003 CHECKSUM 7bdf3117534e3489746639d2665f53f84408d9da2547a34aa31f49aed09b681f
BEGIN;
SELECT pg_advisory_xact_lock(hashtext('odca.schema.migrations'));

DO $odca$
DECLARE
    expected_checksum constant char(64) := '7bdf3117534e3489746639d2665f53f84408d9da2547a34aa31f49aed09b681f';
    recorded_checksum char(64);
BEGIN
    SELECT checksum INTO recorded_checksum
      FROM odca.schema_migrations
     WHERE version = 3;

    IF recorded_checksum IS NOT NULL AND recorded_checksum <> expected_checksum THEN
        RAISE EXCEPTION 'ODCA migration 003 checksum mismatch: stored %, expected %',
            recorded_checksum, expected_checksum;
    END IF;
END
$odca$;

CREATE TABLE IF NOT EXISTS odca.privacy_requests
(
    id uuid PRIMARY KEY,
    public_protocol char(24) NOT NULL UNIQUE,
    requester_email text NOT NULL,
    request_type text NOT NULL CHECK
        (request_type IN ('access', 'correction', 'sharing', 'portability', 'blocking', 'deletion', 'revocation', 'review')),
    details varchar(2000),
    status text NOT NULL DEFAULT 'received' CHECK
        (status IN ('received', 'identity_verification', 'triage', 'analysis', 'execution', 'answered', 'completed')),
    verification_state text NOT NULL DEFAULT 'pending' CHECK
        (verification_state IN ('pending', 'verified', 'failed')),
    tenant_id uuid REFERENCES odca.tenants(id),
    version integer NOT NULL DEFAULT 1 CHECK (version > 0),
    received_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CHECK (public_protocol ~ '^[A-F0-9]{24}$')
);

CREATE TABLE IF NOT EXISTS odca.privacy_request_events
(
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    privacy_request_id uuid NOT NULL REFERENCES odca.privacy_requests(id),
    event_type text NOT NULL,
    occurred_at timestamptz NOT NULL DEFAULT now(),
    actor_user_id uuid REFERENCES odca.users(id),
    metadata jsonb NOT NULL DEFAULT '{}'::jsonb
);

CREATE INDEX IF NOT EXISTS privacy_requests_status_time_ix
    ON odca.privacy_requests (status, received_at);
CREATE INDEX IF NOT EXISTS privacy_request_events_request_time_ix
    ON odca.privacy_request_events (privacy_request_id, occurred_at);

ALTER TABLE odca.privacy_requests ENABLE ROW LEVEL SECURITY;
ALTER TABLE odca.privacy_request_events ENABLE ROW LEVEL SECURITY;

REVOKE ALL ON odca.privacy_requests, odca.privacy_request_events FROM odca_app;
REVOKE ALL ON SEQUENCE odca.privacy_request_events_id_seq FROM odca_app;

CREATE OR REPLACE FUNCTION odca.submit_privacy_request(
    request_id uuid,
    protocol char(24),
    email text,
    kind text,
    request_details varchar(2000))
RETURNS void
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, odca
AS $function$
BEGIN
    IF protocol !~ '^[A-F0-9]{24}$'
       OR length(email) NOT BETWEEN 3 AND 254
       OR email !~ '^[^[:space:]@]+@[^[:space:]@]+\.[^[:space:]@]+$'
       OR kind NOT IN ('access', 'correction', 'sharing', 'portability', 'blocking', 'deletion', 'revocation', 'review')
       OR length(COALESCE(request_details, '')) > 2000 THEN
        RAISE EXCEPTION 'invalid privacy request' USING ERRCODE = '22023';
    END IF;

    INSERT INTO odca.privacy_requests
        (id, public_protocol, requester_email, request_type, details)
    VALUES (request_id, protocol, lower(email), kind, NULLIF(request_details, ''));

    INSERT INTO odca.privacy_request_events
        (privacy_request_id, event_type)
    VALUES (request_id, 'received');
END
$function$;

REVOKE ALL ON FUNCTION odca.submit_privacy_request(uuid, char, text, text, varchar) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION odca.submit_privacy_request(uuid, char, text, text, varchar) TO odca_app;

INSERT INTO odca.schema_migrations (version, name, checksum)
VALUES (3, 'S01 public privacy request intake', '7bdf3117534e3489746639d2665f53f84408d9da2547a34aa31f49aed09b681f')
ON CONFLICT (version) DO NOTHING;

COMMIT;
-- ODCA-END 003

-- ODCA-MIGRATION 004 CHECKSUM f7c73bdf0606cd34bd9e73be107d9fb3364ffc6f6a4b02e6e024695414f6f811
BEGIN;
SELECT pg_advisory_xact_lock(hashtext('odca.schema.migrations'));

DO $odca$
DECLARE
    expected_checksum constant char(64) := 'f7c73bdf0606cd34bd9e73be107d9fb3364ffc6f6a4b02e6e024695414f6f811';
    recorded_checksum char(64);
BEGIN
    SELECT checksum INTO recorded_checksum
      FROM odca.schema_migrations
     WHERE version = 4;

    IF recorded_checksum IS NOT NULL AND recorded_checksum <> expected_checksum THEN
        RAISE EXCEPTION 'ODCA migration 004 checksum mismatch: stored %, expected %',
            recorded_checksum, expected_checksum;
    END IF;
END
$odca$;

ALTER TABLE odca.users
    ADD COLUMN IF NOT EXISTS mfa_secret_protected text,
    ADD COLUMN IF NOT EXISTS mfa_confirmed_at timestamptz,
    ADD COLUMN IF NOT EXISTS mfa_last_accepted_time_step bigint;

ALTER TABLE odca.users
    DROP CONSTRAINT IF EXISTS users_mfa_state_ck;
ALTER TABLE odca.users
    ADD CONSTRAINT users_mfa_state_ck CHECK
    (
        (mfa_secret_protected IS NULL AND mfa_confirmed_at IS NULL AND mfa_last_accepted_time_step IS NULL)
        OR mfa_secret_protected IS NOT NULL
    );

ALTER TABLE odca.sessions
    ADD COLUMN IF NOT EXISTS authentication_level text NOT NULL DEFAULT 'password',
    ADD COLUMN IF NOT EXISTS mfa_completed_at timestamptz,
    ADD COLUMN IF NOT EXISTS mfa_failed_attempts integer NOT NULL DEFAULT 0;

ALTER TABLE odca.sessions
    DROP CONSTRAINT IF EXISTS sessions_authentication_level_ck;
ALTER TABLE odca.sessions
    ADD CONSTRAINT sessions_authentication_level_ck CHECK
    (
        authentication_level IN ('password', 'mfa')
        AND (authentication_level = 'password' OR mfa_completed_at IS NOT NULL)
        AND mfa_failed_attempts >= 0
    );

CREATE TABLE IF NOT EXISTS odca.mfa_recovery_codes
(
    user_id uuid NOT NULL REFERENCES odca.users(id),
    code_hash char(64) NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    consumed_at timestamptz,
    consumed_session_id uuid REFERENCES odca.sessions(id),
    PRIMARY KEY (user_id, code_hash),
    CHECK (code_hash ~ '^[a-f0-9]{64}$'),
    CHECK
    (
        (consumed_at IS NULL AND consumed_session_id IS NULL)
        OR (consumed_at IS NOT NULL AND consumed_session_id IS NOT NULL)
    )
);

CREATE INDEX IF NOT EXISTS mfa_recovery_codes_available_ix
    ON odca.mfa_recovery_codes (user_id, created_at)
    WHERE consumed_at IS NULL;

GRANT SELECT, INSERT, UPDATE, DELETE ON odca.mfa_recovery_codes TO odca_app;

INSERT INTO odca.processing_activities
    (id, version, activity_code, activity_name, purpose, data_subject_categories,
     data_categories, data_origin, necessity, proposed_legal_basis,
     legal_validation_status, responsible_role, systems, recipients, countries,
     retention_reference, controls)
VALUES
    ('10000000-0000-0000-0000-000000000002', 1, 'superadmin-mfa',
     'MFA de superadministrador',
     'Confirmar segundo fator antes de poderes administrativos e registrar eventos de segurança.',
     ARRAY['superadministradores'], ARRAY['segredo TOTP protegido', 'hashes de códigos de recuperação', 'eventos de autenticação'],
     'Inscrição pelo próprio superadministrador',
     'Necessário para reduzir risco de acesso privilegiado indevido.',
     'Legítimo interesse de segurança e execução de contrato — validação jurídica pendente',
     'pending', 'Segurança', ARRAY['PostgreSQL', 'API ODCA'],
     ARRAY['Equipe autorizada da plataforma'], ARRAY['Brasil'],
     'RETENTION_POLICY.md#identidade-e-seguranca',
     ARRAY['Data Protection', 'hash de recovery code', 'uso único', 'revogação por tentativas'])
ON CONFLICT (activity_code, version) DO NOTHING;

INSERT INTO odca.schema_migrations (version, name, checksum)
VALUES (4, 'S01 superadmin MFA', 'f7c73bdf0606cd34bd9e73be107d9fb3364ffc6f6a4b02e6e024695414f6f811')
ON CONFLICT (version) DO NOTHING;

COMMIT;
-- ODCA-END 004

-- ODCA-MIGRATION 005 CHECKSUM 678c273e7240c253b4118a06c6d01b49d8b86de4b327c2fe603a21f0d264ecc4
BEGIN;
SELECT pg_advisory_xact_lock(hashtext('odca.schema.migrations'));

DO $odca$
DECLARE
    expected_checksum constant char(64) := '678c273e7240c253b4118a06c6d01b49d8b86de4b327c2fe603a21f0d264ecc4';
    recorded_checksum char(64);
BEGIN
    SELECT checksum INTO recorded_checksum
      FROM odca.schema_migrations
     WHERE version = 5;

    IF recorded_checksum IS NOT NULL AND recorded_checksum <> expected_checksum THEN
        RAISE EXCEPTION 'ODCA migration 005 checksum mismatch: stored %, expected %',
            recorded_checksum, expected_checksum;
    END IF;
END
$odca$;

CREATE TABLE IF NOT EXISTS odca.customer_registration_requests
(
    id uuid PRIMARY KEY,
    idempotency_key char(64) NOT NULL UNIQUE,
    user_id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    plan_version_id uuid NOT NULL REFERENCES odca.plan_versions(id),
    plan_code text NOT NULL,
    plan_version integer NOT NULL CHECK (plan_version > 0),
    document_type text NOT NULL CHECK (document_type IN ('cpf', 'cnpj')),
    document_normalized text NOT NULL,
    responsible_name text NOT NULL,
    email text NOT NULL,
    email_normalized text NOT NULL,
    password_hash text NOT NULL,
    marketing_consent boolean NOT NULL DEFAULT false,
    confirmation_token_hash char(64),
    development_confirmation_token text,
    status text NOT NULL DEFAULT 'email_pending' CHECK (status IN ('email_pending', 'confirmed', 'expired')),
    expires_at timestamptz NOT NULL,
    confirmed_at timestamptz,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CHECK (idempotency_key ~ '^[a-f0-9]{64}$'),
    CHECK (confirmation_token_hash IS NULL OR confirmation_token_hash ~ '^[a-f0-9]{64}$'),
    CHECK (confirmed_at IS NULL OR status = 'confirmed')
);

CREATE INDEX IF NOT EXISTS customer_registration_document_ix
    ON odca.customer_registration_requests (document_normalized, created_at DESC);

CREATE TABLE IF NOT EXISTS odca.subscriptions
(
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL REFERENCES odca.tenants(id),
    plan_version_id uuid NOT NULL REFERENCES odca.plan_versions(id),
    commercial_state text NOT NULL CHECK
        (commercial_state IN ('email_pending', 'commercial_pending', 'active', 'suspended')),
    status text NOT NULL CHECK (status IN ('pending', 'active', 'suspended', 'cancelled')),
    manual_grant_reason text,
    created_by uuid NOT NULL REFERENCES odca.users(id),
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    UNIQUE (tenant_id),
    CHECK ((commercial_state = 'active') = (status = 'active') OR commercial_state <> 'active')
);

CREATE TABLE IF NOT EXISTS odca.customer_registration_outbox
(
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    registration_id uuid NOT NULL REFERENCES odca.customer_registration_requests(id),
    message_type text NOT NULL CHECK (message_type IN ('email_confirmation')),
    destination text NOT NULL,
    status text NOT NULL CHECK (status IN ('local_development', 'provider_required', 'sent', 'failed')),
    payload jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_at timestamptz NOT NULL DEFAULT now(),
    sent_at timestamptz
);

ALTER TABLE odca.subscriptions ENABLE ROW LEVEL SECURITY;
ALTER TABLE odca.subscriptions FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS tenant_isolation ON odca.subscriptions;
CREATE POLICY tenant_isolation ON odca.subscriptions TO odca_app
    USING (tenant_id = NULLIF(current_setting('odca.tenant_id', true), '')::uuid)
    WITH CHECK (tenant_id = NULLIF(current_setting('odca.tenant_id', true), '')::uuid);

CREATE OR REPLACE FUNCTION odca.customer_home_snapshot(requesting_user_id uuid, requested_tenant_id uuid DEFAULT NULL)
RETURNS TABLE(
    tenant_id uuid,
    organization_name text,
    tenant_status text,
    commercial_state text,
    plan_code text,
    plan_name text,
    plan_version integer,
    active_seats integer,
    storage_bytes bigint,
    user_storage_bytes bigint,
    file_bytes bigint,
    ocr_pages_monthly integer,
    signature_envelopes_monthly integer)
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
           AND NOT u.is_deleted
    ) THEN
        RAISE EXCEPTION 'authenticated user required' USING ERRCODE = '42501';
    END IF;

    RETURN QUERY
    SELECT t.id,
           t.display_name,
           t.status,
           s.commercial_state,
           p.code,
           p.display_name,
           p.version,
           max(e.limit_value) FILTER (WHERE e.entitlement_code = 'active_seats')::integer,
           max(e.limit_value) FILTER (WHERE e.entitlement_code = 'storage_bytes'),
           max(e.limit_value) FILTER (WHERE e.entitlement_code = 'user_storage_bytes'),
           max(e.limit_value) FILTER (WHERE e.entitlement_code = 'file_bytes'),
           max(e.limit_value) FILTER (WHERE e.entitlement_code = 'ocr_pages_monthly')::integer,
           max(e.limit_value) FILTER (WHERE e.entitlement_code = 'signature_envelopes_monthly')::integer
      FROM odca.memberships m
      JOIN odca.tenants t ON t.id = m.tenant_id
      JOIN odca.subscriptions s ON s.tenant_id = t.id
      JOIN odca.plan_versions p ON p.id = s.plan_version_id
      JOIN odca.plan_entitlements e ON e.plan_version_id = p.id AND e.enabled
     WHERE m.user_id = requesting_user_id
       AND m.status = 'active'
       AND NOT t.is_deleted
       AND (requested_tenant_id IS NULL OR t.id = requested_tenant_id)
     GROUP BY t.id, t.display_name, t.status, s.commercial_state, p.code, p.display_name, p.version
     ORDER BY t.display_name
     LIMIT 1;
END
$function$;
REVOKE ALL ON FUNCTION odca.customer_home_snapshot(uuid, uuid) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION odca.customer_home_snapshot(uuid, uuid) TO odca_app;

GRANT SELECT, INSERT, UPDATE ON odca.customer_registration_requests,
    odca.customer_registration_outbox, odca.subscriptions TO odca_app;

INSERT INTO odca.processing_activities
    (id, version, activity_code, activity_name, purpose, data_subject_categories,
     data_categories, data_origin, necessity, proposed_legal_basis,
     legal_validation_status, responsible_role, systems, recipients, countries,
     retention_reference, controls)
VALUES
    ('10000000-0000-0000-0000-000000000003', 1, 'customer-onboarding',
     'Cadastro do primeiro cliente',
     'Criar identidade, organização e assinatura comercial pendente após confirmação de e-mail.',
     ARRAY['responsáveis de clientes'], ARRAY['identificação', 'CPF/CNPJ', 'contato', 'aceites', 'estado comercial'],
     'Cadastro público pelo responsável',
     'Necessário para vincular a organização contratante ao plano escolhido.',
     'Execução de contrato — validação jurídica pendente',
     'pending', 'Produto e Privacidade', ARRAY['PostgreSQL', 'API ODCA'],
     ARRAY['Equipe autorizada da plataforma'], ARRAY['Brasil'],
     'RETENTION_POLICY.md#cadastro-pendente',
     ARRAY['token hashado', 'uso único', 'idempotência', 'outbox transacional', 'logs minimizados'])
ON CONFLICT (activity_code, version) DO NOTHING;

INSERT INTO odca.schema_migrations (version, name, checksum)
VALUES (5, 'S01 customer onboarding', '678c273e7240c253b4118a06c6d01b49d8b86de4b327c2fe603a21f0d264ecc4')
ON CONFLICT (version) DO NOTHING;

COMMIT;
-- ODCA-END 005

-- ODCA-MIGRATION 006 CHECKSUM e93d475a96feea63b680616f7ae54f11fdea870a6ad505e6360db4d91f987aea
BEGIN;

DO $odca$
DECLARE
    recorded_checksum text;
    expected_checksum constant text := 'e93d475a96feea63b680616f7ae54f11fdea870a6ad505e6360db4d91f987aea';
BEGIN
    SELECT checksum INTO recorded_checksum
      FROM odca.schema_migrations
     WHERE version = 6;

    IF recorded_checksum IS NOT NULL AND recorded_checksum <> expected_checksum THEN
        RAISE EXCEPTION 'ODCA migration 006 checksum mismatch: stored %, expected %',
            recorded_checksum, expected_checksum;
    END IF;
END
$odca$;

-- One contracting document owns at most one live registration/organization. Expired
-- drafts are deliberately excluded so that the owner can safely resume onboarding.
CREATE UNIQUE INDEX customer_registration_live_document_uq
    ON odca.customer_registration_requests (document_type, document_normalized)
    WHERE status IN ('email_pending', 'confirmed');

ALTER TABLE odca.customer_registration_requests
    ADD COLUMN terms_version text,
    ADD COLUMN terms_accepted_at timestamptz,
    ADD COLUMN privacy_notice_version text,
    ADD COLUMN privacy_notice_acknowledged_at timestamptz;

INSERT INTO odca.schema_migrations (version, name, checksum)
VALUES (6, 'S01 onboarding identity integrity', 'e93d475a96feea63b680616f7ae54f11fdea870a6ad505e6360db4d91f987aea')
ON CONFLICT (version) DO NOTHING;

COMMIT;
-- ODCA-END 006
-- ODCA Solutions migration 007: explicit tenant context, administration and durable invitations.
-- ODCA-MIGRATION 007 CHECKSUM 57ade8deecaea3fad7fbabab59d3f7081ee2684fb75bd76f5d756d44cd523bd9
BEGIN;
SELECT pg_advisory_xact_lock(hashtext('odca.schema.migrations'));

ALTER TABLE odca.tenants ADD COLUMN IF NOT EXISTS version bigint NOT NULL DEFAULT 1 CHECK (version > 0);

INSERT INTO odca.permissions (code, description, delegable) VALUES
 ('tenant.organization.read', 'Consultar cadastro da organização.', true),
 ('tenant.organization.manage', 'Editar cadastro permitido da organização.', true),
 ('tenant.team.read', 'Consultar equipe e perfis.', true),
 ('tenant.team.manage', 'Gerenciar equipe, perfis e convites.', true)
ON CONFLICT (code) DO UPDATE SET description=EXCLUDED.description, delegable=EXCLUDED.delegable;

CREATE TABLE IF NOT EXISTS odca.tenant_invitations
(
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(), tenant_id uuid NOT NULL REFERENCES odca.tenants(id),
 recipient_email text NOT NULL, recipient_normalized text NOT NULL, role_id uuid NOT NULL,
 token_hash char(64) NOT NULL UNIQUE, protected_token text,
 status text NOT NULL DEFAULT 'pending' CHECK(status IN ('pending','sent','failed','accepted','cancelled')),
 idempotency_key text NOT NULL, expires_at timestamptz NOT NULL,
 accepted_at timestamptz, cancelled_at timestamptz, created_by uuid NOT NULL REFERENCES odca.users(id),
 created_at timestamptz NOT NULL DEFAULT now(), updated_at timestamptz NOT NULL DEFAULT now(),
 UNIQUE(tenant_id,idempotency_key), FOREIGN KEY(tenant_id,role_id) REFERENCES odca.roles(tenant_id,id)
);
CREATE UNIQUE INDEX IF NOT EXISTS tenant_invitations_live_recipient_uq ON odca.tenant_invitations(tenant_id,recipient_normalized) WHERE status IN ('pending','sent');
CREATE TABLE IF NOT EXISTS odca.notification_outbox
(
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(), tenant_id uuid NOT NULL REFERENCES odca.tenants(id),
 invitation_id uuid NOT NULL REFERENCES odca.tenant_invitations(id), kind text NOT NULL,
 destination text NOT NULL, protected_payload text NOT NULL, status text NOT NULL DEFAULT 'pending' CHECK(status IN ('pending','leased','sent','failed')),
 attempt_count integer NOT NULL DEFAULT 0, available_at timestamptz NOT NULL DEFAULT now(), lease_until timestamptz,
 last_error_code text, created_at timestamptz NOT NULL DEFAULT now(), sent_at timestamptz,
 UNIQUE(invitation_id,kind,attempt_count)
);
ALTER TABLE odca.tenant_invitations ENABLE ROW LEVEL SECURITY;
ALTER TABLE odca.tenant_invitations FORCE ROW LEVEL SECURITY;
ALTER TABLE odca.notification_outbox ENABLE ROW LEVEL SECURITY;
ALTER TABLE odca.notification_outbox FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON odca.tenant_invitations TO odca_app USING(tenant_id=NULLIF(current_setting('odca.tenant_id',true),'')::uuid) WITH CHECK(tenant_id=NULLIF(current_setting('odca.tenant_id',true),'')::uuid);
CREATE POLICY tenant_isolation ON odca.notification_outbox TO odca_app USING(tenant_id=NULLIF(current_setting('odca.tenant_id',true),'')::uuid) WITH CHECK(tenant_id=NULLIF(current_setting('odca.tenant_id',true),'')::uuid);
GRANT SELECT,INSERT,UPDATE ON odca.tenant_invitations,odca.notification_outbox TO odca_app;

CREATE OR REPLACE FUNCTION odca.user_organizations(requesting_user_id uuid)
RETURNS TABLE(id uuid,name text,status text,version bigint,permissions text[]) LANGUAGE sql SECURITY DEFINER SET search_path=pg_catalog,odca AS $$
 SELECT t.id,t.display_name,t.status,t.version,COALESCE(array_agg(DISTINCT rp.permission_code) FILTER(WHERE rp.permission_code IS NOT NULL),ARRAY[]::text[])
 FROM odca.memberships m JOIN odca.users u ON u.id=m.user_id AND NOT u.is_deleted JOIN odca.tenants t ON t.id=m.tenant_id
 LEFT JOIN odca.member_roles mr ON mr.tenant_id=m.tenant_id AND mr.user_id=m.user_id
 LEFT JOIN odca.role_permissions rp ON rp.role_id=mr.role_id
 WHERE m.user_id=requesting_user_id AND m.status='active' AND NOT t.is_deleted AND t.status<>'suspended'
 GROUP BY t.id,t.display_name,t.status,t.version ORDER BY t.display_name;
$$;
REVOKE ALL ON FUNCTION odca.user_organizations(uuid) FROM PUBLIC; GRANT EXECUTE ON FUNCTION odca.user_organizations(uuid) TO odca_app;

CREATE OR REPLACE FUNCTION odca.tenant_actor_has_permission(actor_id uuid, requested_tenant_id uuid, requested_permission text)
RETURNS boolean LANGUAGE sql SECURITY DEFINER STABLE SET search_path=pg_catalog,odca AS $$
 SELECT EXISTS(SELECT 1 FROM odca.memberships m JOIN odca.member_roles mr ON (mr.tenant_id,mr.user_id)=(m.tenant_id,m.user_id)
 JOIN odca.roles r ON r.id=mr.role_id AND r.tenant_id=mr.tenant_id LEFT JOIN odca.role_permissions rp ON rp.role_id=r.id
 WHERE m.user_id=actor_id AND m.tenant_id=requested_tenant_id AND m.status='active'
 AND (r.code='tenant-administrator' OR rp.permission_code=requested_permission));
$$;
REVOKE ALL ON FUNCTION odca.tenant_actor_has_permission(uuid,uuid,text) FROM PUBLIC; GRANT EXECUTE ON FUNCTION odca.tenant_actor_has_permission(uuid,uuid,text) TO odca_app;

-- Existing tenants gain explicit administrator capabilities without converting ordinary memberships into administrators.
INSERT INTO odca.role_permissions(role_id,permission_code)
SELECT r.id,p.code FROM odca.roles r CROSS JOIN odca.permissions p WHERE r.scope_type='tenant' AND r.code='tenant-administrator' AND p.code LIKE 'tenant.%' ON CONFLICT DO NOTHING;

CREATE OR REPLACE FUNCTION odca.accept_tenant_invitation(actor_id uuid,invitation_id uuid,presented_hash text)
RETURNS boolean LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,odca AS $$
DECLARE selected odca.tenant_invitations%ROWTYPE;
BEGIN
 SELECT i.* INTO selected FROM odca.tenant_invitations i JOIN odca.users u ON u.id=actor_id AND u.email_normalized=i.recipient_normalized AND u.email_verified_at IS NOT NULL AND NOT u.is_deleted
 WHERE i.id=invitation_id AND i.token_hash=presented_hash AND i.status IN ('pending','sent') AND i.expires_at>now() FOR UPDATE OF i;
 IF NOT FOUND THEN RETURN false; END IF;
 INSERT INTO odca.memberships(tenant_id,user_id,status) VALUES(selected.tenant_id,actor_id,'active') ON CONFLICT(tenant_id,user_id) DO UPDATE SET status='active',security_version=odca.memberships.security_version+1,updated_at=now();
 INSERT INTO odca.member_roles(tenant_id,user_id,role_id,assigned_by) VALUES(selected.tenant_id,actor_id,selected.role_id,actor_id) ON CONFLICT DO NOTHING;
 UPDATE odca.tenant_invitations SET status='accepted',accepted_at=now(),token_hash=encode(sha256(gen_random_bytes(32)),'hex'),protected_token=NULL,updated_at=now() WHERE id=invitation_id;
 UPDATE odca.users SET security_version=security_version+1 WHERE id=actor_id;
 UPDATE odca.sessions SET revoked_at=now() WHERE user_id=actor_id AND revoked_at IS NULL;
 INSERT INTO odca.audit_events(scope_type,tenant_id,actor_user_id,action,entity_type,entity_id,result) VALUES('tenant',selected.tenant_id,actor_id,'tenant.invitation.accepted','invitation',invitation_id,'success');
 RETURN true;
END $$;
REVOKE ALL ON FUNCTION odca.accept_tenant_invitation(uuid,uuid,text) FROM PUBLIC; GRANT EXECUTE ON FUNCTION odca.accept_tenant_invitation(uuid,uuid,text) TO odca_app;

CREATE OR REPLACE FUNCTION odca.claim_notification(worker_id text)
RETURNS TABLE(id uuid,tenant_id uuid,invitation_id uuid,destination text,protected_payload text) LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,odca AS $$
BEGIN
 RETURN QUERY UPDATE odca.notification_outbox o SET status='leased',lease_until=now()+interval '2 minutes',attempt_count=attempt_count+1
 WHERE o.id=(SELECT q.id FROM odca.notification_outbox q WHERE (q.status='pending' OR (q.status='leased' AND q.lease_until<now())) AND q.available_at<=now() AND q.attempt_count<5 ORDER BY q.created_at FOR UPDATE SKIP LOCKED LIMIT 1)
 RETURNING o.id,o.tenant_id,o.invitation_id,o.destination,o.protected_payload;
END $$;
CREATE OR REPLACE FUNCTION odca.complete_notification(message_id uuid,succeeded boolean,error_code text DEFAULT NULL)
RETURNS void LANGUAGE sql SECURITY DEFINER SET search_path=pg_catalog,odca AS $$
 UPDATE odca.notification_outbox SET status=CASE WHEN succeeded THEN 'sent' WHEN attempt_count>=5 THEN 'failed' ELSE 'pending' END,
 sent_at=CASE WHEN succeeded THEN now() ELSE NULL END,lease_until=NULL,available_at=CASE WHEN succeeded THEN available_at ELSE now()+make_interval(secs=>least(300,attempt_count*attempt_count*10)) END,last_error_code=error_code,protected_payload=CASE WHEN succeeded OR attempt_count>=5 THEN '' ELSE protected_payload END WHERE id=message_id;
 UPDATE odca.tenant_invitations i SET status=CASE WHEN succeeded THEN 'sent' WHEN (SELECT status='failed' FROM odca.notification_outbox WHERE id=message_id) THEN 'failed' ELSE i.status END,protected_token=CASE WHEN succeeded THEN NULL ELSE protected_token END,updated_at=now() WHERE id=(SELECT invitation_id FROM odca.notification_outbox WHERE id=message_id);
$$;
REVOKE ALL ON FUNCTION odca.claim_notification(text),odca.complete_notification(uuid,boolean,text) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION odca.claim_notification(text),odca.complete_notification(uuid,boolean,text) TO odca_app;

INSERT INTO odca.schema_migrations(version,name,checksum) VALUES(7,'S01 tenant context and administration','57ade8deecaea3fad7fbabab59d3f7081ee2684fb75bd76f5d756d44cd523bd9') ON CONFLICT(version) DO NOTHING;
COMMIT;
-- ODCA-END 007
