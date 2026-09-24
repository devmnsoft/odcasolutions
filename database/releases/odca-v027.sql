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
-- ODCA-MIGRATION 008 CHECKSUM 95e93b46aab0cff381e1ca7ea2b7a54223545551b7b87fb3686876e13571d6ab
BEGIN;
SELECT pg_advisory_xact_lock(hashtext('odca.schema.migrations'));

ALTER TABLE odca.notification_outbox 
  ADD COLUMN lease_token uuid,
  ADD COLUMN lease_owner text;

CREATE OR REPLACE FUNCTION odca.tenant_actor_has_permission(actor_id uuid, requested_tenant_id uuid, requested_permission text)
RETURNS boolean LANGUAGE sql SECURITY DEFINER STABLE SET search_path=pg_catalog,odca AS $$
 SELECT EXISTS(
  SELECT 1 FROM odca.memberships m 
  JOIN odca.users u ON u.id=m.user_id 
  JOIN odca.tenants t ON t.id=m.tenant_id
  JOIN odca.member_roles mr ON (mr.tenant_id,mr.user_id)=(m.tenant_id,m.user_id)
  JOIN odca.roles r ON r.id=mr.role_id AND r.tenant_id=mr.tenant_id 
  LEFT JOIN odca.role_permissions rp ON rp.role_id=r.id
  WHERE m.user_id=actor_id AND m.tenant_id=requested_tenant_id AND m.status='active'
  AND NOT u.is_deleted AND NOT t.is_deleted AND t.status<>'suspended'
  AND (r.code='tenant-administrator' OR rp.permission_code=requested_permission)
 );
$$;
REVOKE ALL ON FUNCTION odca.tenant_actor_has_permission(uuid,uuid,text) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION odca.tenant_actor_has_permission(uuid,uuid,text) TO odca_app;

CREATE OR REPLACE FUNCTION odca.accept_tenant_invitation(actor_id uuid, invitation_id uuid, presented_hash text)
RETURNS boolean LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,odca AS $$
DECLARE selected odca.tenant_invitations%ROWTYPE;
DECLARE locked_tenant_id uuid;
BEGIN
 SELECT tenant_id INTO locked_tenant_id FROM odca.tenant_invitations WHERE id=invitation_id;
 IF FOUND THEN
   PERFORM pg_advisory_xact_lock(hashtextextended(locked_tenant_id::text, 0));
 END IF;

 SELECT i.* INTO selected FROM odca.tenant_invitations i 
 JOIN odca.users u ON u.id=actor_id AND u.email_normalized=i.recipient_normalized AND u.email_verified_at IS NOT NULL AND NOT u.is_deleted
 WHERE i.id=invitation_id AND i.token_hash=presented_hash AND i.status IN ('pending','sent') AND i.expires_at>now() 
 FOR UPDATE OF i;

 IF NOT FOUND THEN RETURN false; END IF;

 INSERT INTO odca.memberships(tenant_id,user_id,status) VALUES(selected.tenant_id,actor_id,'active') 
 ON CONFLICT(tenant_id,user_id) DO UPDATE SET status='active',security_version=odca.memberships.security_version+1,updated_at=now();

 INSERT INTO odca.member_roles(tenant_id,user_id,role_id,assigned_by) VALUES(selected.tenant_id,actor_id,selected.role_id,actor_id) ON CONFLICT DO NOTHING;

 UPDATE odca.tenant_invitations SET status='accepted',accepted_at=now(),token_hash=encode(sha256(('consumed:' || id::text)::bytea),'hex'),protected_token=NULL,updated_at=now() WHERE id=invitation_id;

 UPDATE odca.users SET security_version=security_version+1 WHERE id=actor_id;
 UPDATE odca.sessions SET revoked_at=now() WHERE user_id=actor_id AND revoked_at IS NULL;
 INSERT INTO odca.audit_events(scope_type,tenant_id,actor_user_id,action,entity_type,entity_id,result) VALUES('tenant',selected.tenant_id,actor_id,'tenant.invitation.accepted','invitation',invitation_id,'success');
 RETURN true;
END $$;
REVOKE ALL ON FUNCTION odca.accept_tenant_invitation(uuid,uuid,text) FROM PUBLIC; 
GRANT EXECUTE ON FUNCTION odca.accept_tenant_invitation(uuid,uuid,text) TO odca_app;

DROP FUNCTION IF EXISTS odca.claim_notification(text);
CREATE OR REPLACE FUNCTION odca.claim_notification(worker_id text)
RETURNS TABLE(id uuid, tenant_id uuid, invitation_id uuid, destination text, protected_payload text, lease_token uuid) LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,odca AS $$
DECLARE
 new_lease_token uuid := gen_random_uuid();
BEGIN
 RETURN QUERY UPDATE odca.notification_outbox o SET status='leased',lease_until=now()+interval '2 minutes',attempt_count=attempt_count+1, lease_token=new_lease_token, lease_owner=worker_id
 WHERE o.id=(SELECT q.id FROM odca.notification_outbox q WHERE (q.status='pending' OR (q.status='leased' AND q.lease_until<now())) AND q.available_at<=now() AND q.attempt_count<5 ORDER BY q.created_at FOR UPDATE SKIP LOCKED LIMIT 1)
 RETURNING o.id,o.tenant_id,o.invitation_id,o.destination,o.protected_payload,o.lease_token;
END $$;
REVOKE ALL ON FUNCTION odca.claim_notification(text) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION odca.claim_notification(text) TO odca_app;

DROP FUNCTION IF EXISTS odca.complete_notification(uuid, boolean, text);
CREATE OR REPLACE FUNCTION odca.complete_notification(message_id uuid, presented_lease_token uuid, succeeded boolean, error_code text DEFAULT NULL)
RETURNS void LANGUAGE sql SECURITY DEFINER SET search_path=pg_catalog,odca AS $$
 UPDATE odca.notification_outbox SET 
  status=CASE WHEN succeeded THEN 'sent' WHEN attempt_count>=5 THEN 'failed' ELSE 'pending' END,
  sent_at=CASE WHEN succeeded THEN now() ELSE NULL END,
  lease_until=NULL, lease_token=NULL, lease_owner=NULL,
  available_at=CASE WHEN succeeded THEN available_at ELSE now()+make_interval(secs=>least(300,attempt_count*attempt_count*10)) END,
  last_error_code=error_code,
  protected_payload=CASE WHEN succeeded OR attempt_count>=5 THEN '' ELSE protected_payload END 
 WHERE id=message_id AND lease_token=presented_lease_token;

 UPDATE odca.tenant_invitations i SET 
  status=CASE WHEN succeeded THEN 'sent' WHEN (SELECT status='failed' FROM odca.notification_outbox WHERE id=message_id) THEN 'failed' ELSE i.status END,
  protected_token=CASE WHEN succeeded THEN NULL ELSE protected_token END,
  updated_at=now() 
 WHERE id=(SELECT invitation_id FROM odca.notification_outbox WHERE id=message_id)
   AND i.status NOT IN ('accepted', 'cancelled');
$$;
REVOKE ALL ON FUNCTION odca.complete_notification(uuid,uuid,boolean,text) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION odca.complete_notification(uuid,uuid,boolean,text) TO odca_app;

INSERT INTO odca.schema_migrations(version,name,checksum) VALUES(8,'S01 team management and reliable invitations','95e93b46aab0cff381e1ca7ea2b7a54223545551b7b87fb3686876e13571d6ab') ON CONFLICT(version) DO NOTHING;
COMMIT;
-- ODCA-END 008
-- ODCA Solutions migration 009: invitation lifecycle, delivery separation and last-admin guards.
-- ODCA-MIGRATION 009 CHECKSUM f2d7bc3a2ed44f1e0601b29d911cd4af4fba774a4d47a2020962519d094209f2
BEGIN;
SELECT pg_advisory_xact_lock(hashtext('odca.schema.migrations'));

-- Expand invitation status with expired; keep live recipient uniqueness on pending/sent only.
ALTER TABLE odca.tenant_invitations DROP CONSTRAINT IF EXISTS tenant_invitations_status_check;
ALTER TABLE odca.tenant_invitations
  ADD CONSTRAINT tenant_invitations_status_check
  CHECK (status IN ('pending','sent','failed','accepted','cancelled','expired'));

DROP INDEX IF EXISTS odca.tenant_invitations_live_recipient_uq;
CREATE UNIQUE INDEX tenant_invitations_live_recipient_uq
  ON odca.tenant_invitations (tenant_id, recipient_normalized)
  WHERE status IN ('pending','sent');

-- Mark pending/sent invitations past expires_at as expired and clear delivery secrets.
CREATE OR REPLACE FUNCTION odca.expire_tenant_invitations(target_tenant_id uuid DEFAULT NULL)
RETURNS integer
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, odca
AS $$
DECLARE
  affected integer := 0;
BEGIN
  UPDATE odca.tenant_invitations
     SET status = 'expired',
         protected_token = NULL,
         updated_at = now()
   WHERE status IN ('pending','sent')
     AND expires_at <= now()
     AND (target_tenant_id IS NULL OR tenant_id = target_tenant_id);
  GET DIAGNOSTICS affected = ROW_COUNT;
  RETURN affected;
END;
$$;
REVOKE ALL ON FUNCTION odca.expire_tenant_invitations(uuid) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION odca.expire_tenant_invitations(uuid) TO odca_app;

-- Accept invitation without reactivating blocked/inactive memberships.
CREATE OR REPLACE FUNCTION odca.accept_tenant_invitation(actor_id uuid, invitation_id uuid, presented_hash text)
RETURNS boolean
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, odca
AS $$
DECLARE
  selected odca.tenant_invitations%ROWTYPE;
  locked_tenant_id uuid;
  existing_status text;
BEGIN
  SELECT tenant_id INTO locked_tenant_id FROM odca.tenant_invitations WHERE id = invitation_id;
  IF FOUND THEN
    PERFORM pg_advisory_xact_lock(hashtextextended(locked_tenant_id::text, 0));
  END IF;

  PERFORM odca.expire_tenant_invitations(locked_tenant_id);

  SELECT i.* INTO selected
    FROM odca.tenant_invitations i
    JOIN odca.users u
      ON u.id = actor_id
     AND u.email_normalized = i.recipient_normalized
     AND u.email_verified_at IS NOT NULL
     AND NOT u.is_deleted
   WHERE i.id = invitation_id
     AND i.token_hash = presented_hash
     AND i.status IN ('pending','sent')
     AND i.expires_at > now()
   FOR UPDATE OF i;

  IF NOT FOUND THEN
    RETURN false;
  END IF;

  SELECT m.status INTO existing_status
    FROM odca.memberships m
   WHERE m.tenant_id = selected.tenant_id
     AND m.user_id = actor_id
   FOR UPDATE;

  IF FOUND AND existing_status IN ('blocked','inactive') THEN
    INSERT INTO odca.audit_events(scope_type,tenant_id,actor_user_id,action,entity_type,entity_id,result,metadata)
    VALUES (
      'tenant',
      selected.tenant_id,
      actor_id,
      'tenant.invitation.accept_denied',
      'invitation',
      invitation_id,
      'denied',
      jsonb_build_object('reason','membership_' || existing_status));
    RETURN false;
  END IF;

  IF NOT FOUND THEN
    INSERT INTO odca.memberships(tenant_id,user_id,status)
    VALUES (selected.tenant_id, actor_id, 'active');
  ELSIF existing_status = 'invited' THEN
    UPDATE odca.memberships
       SET status = 'active',
           security_version = security_version + 1,
           updated_at = now()
     WHERE tenant_id = selected.tenant_id
       AND user_id = actor_id;
  ELSE
    UPDATE odca.memberships
       SET updated_at = now()
     WHERE tenant_id = selected.tenant_id
       AND user_id = actor_id
       AND status = 'active';
  END IF;

  INSERT INTO odca.member_roles(tenant_id,user_id,role_id,assigned_by)
  VALUES (selected.tenant_id, actor_id, selected.role_id, actor_id)
  ON CONFLICT DO NOTHING;

  UPDATE odca.tenant_invitations
     SET status = 'accepted',
         accepted_at = now(),
         token_hash = encode(sha256(('consumed:' || id::text)::bytea),'hex'),
         protected_token = NULL,
         updated_at = now()
   WHERE id = invitation_id;

  UPDATE odca.users SET security_version = security_version + 1 WHERE id = actor_id;
  UPDATE odca.sessions SET revoked_at = now() WHERE user_id = actor_id AND revoked_at IS NULL;

  INSERT INTO odca.audit_events(scope_type,tenant_id,actor_user_id,action,entity_type,entity_id,result)
  VALUES ('tenant', selected.tenant_id, actor_id, 'tenant.invitation.accepted', 'invitation', invitation_id, 'success');

  RETURN true;
END;
$$;
REVOKE ALL ON FUNCTION odca.accept_tenant_invitation(uuid,uuid,text) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION odca.accept_tenant_invitation(uuid,uuid,text) TO odca_app;

-- Claim only when attempt_count < 5. Terminal failure is finalized by complete_notification
-- after a failed attempt that reaches attempt_count >= 5; expired leases are reclaimable
-- only while attempt_count remains below 5.
DROP FUNCTION IF EXISTS odca.claim_notification(text);
CREATE OR REPLACE FUNCTION odca.claim_notification(worker_id text)
RETURNS TABLE(id uuid, tenant_id uuid, invitation_id uuid, destination text, protected_payload text, lease_token uuid)
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, odca
AS $$
DECLARE
  new_lease_token uuid := gen_random_uuid();
BEGIN
  RETURN QUERY
  UPDATE odca.notification_outbox o
     SET status = 'leased',
         lease_until = now() + interval '2 minutes',
         attempt_count = attempt_count + 1,
         lease_token = new_lease_token,
         lease_owner = worker_id
   WHERE o.id = (
     SELECT q.id
       FROM odca.notification_outbox q
      WHERE (q.status = 'pending' OR (q.status = 'leased' AND q.lease_until < now()))
        AND q.available_at <= now()
        AND q.attempt_count < 5
      ORDER BY q.created_at
      FOR UPDATE SKIP LOCKED
      LIMIT 1)
  RETURNING o.id, o.tenant_id, o.invitation_id, o.destination, o.protected_payload, o.lease_token;
END;
$$;
REVOKE ALL ON FUNCTION odca.claim_notification(text) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION odca.claim_notification(text) TO odca_app;

-- Delivery completion is separated from invitation lifecycle terminal states.
DROP FUNCTION IF EXISTS odca.complete_notification(uuid, uuid, boolean, text);
CREATE OR REPLACE FUNCTION odca.complete_notification(
  message_id uuid,
  presented_lease_token uuid,
  succeeded boolean,
  error_code text DEFAULT NULL)
RETURNS void
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, odca
AS $$
DECLARE
  outbox_status text;
  related_invitation uuid;
BEGIN
  UPDATE odca.notification_outbox
     SET status = CASE
           WHEN succeeded THEN 'sent'
           WHEN attempt_count >= 5 THEN 'failed'
           ELSE 'pending'
         END,
         sent_at = CASE WHEN succeeded THEN now() ELSE NULL END,
         lease_until = NULL,
         lease_token = NULL,
         lease_owner = NULL,
         available_at = CASE
           WHEN succeeded THEN available_at
           ELSE now() + make_interval(secs => least(300, attempt_count * attempt_count * 10))
         END,
         last_error_code = error_code,
         protected_payload = CASE
           WHEN succeeded OR attempt_count >= 5 THEN ''
           ELSE protected_payload
         END
   WHERE id = message_id
     AND lease_token = presented_lease_token
  RETURNING status, invitation_id INTO outbox_status, related_invitation;

  IF related_invitation IS NULL THEN
    RETURN;
  END IF;

  IF succeeded THEN
    UPDATE odca.tenant_invitations i
       SET status = 'sent',
           protected_token = NULL,
           updated_at = now()
     WHERE i.id = related_invitation
       AND i.status = 'pending';
  ELSIF outbox_status = 'failed' THEN
    UPDATE odca.tenant_invitations i
       SET status = 'failed',
           updated_at = now()
     WHERE i.id = related_invitation
       AND i.status IN ('pending','sent');
  END IF;
END;
$$;
REVOKE ALL ON FUNCTION odca.complete_notification(uuid,uuid,boolean,text) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION odca.complete_notification(uuid,uuid,boolean,text) TO odca_app;

CREATE OR REPLACE FUNCTION odca.cancel_tenant_invitation(actor_id uuid, invitation_id uuid)
RETURNS boolean
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, odca
AS $$
DECLARE
  selected odca.tenant_invitations%ROWTYPE;
BEGIN
  SELECT * INTO selected FROM odca.tenant_invitations WHERE id = invitation_id FOR UPDATE;
  IF NOT FOUND THEN
    RETURN false;
  END IF;

  PERFORM pg_advisory_xact_lock(hashtextextended(selected.tenant_id::text, 0));

  IF NOT odca.tenant_actor_has_permission(actor_id, selected.tenant_id, 'tenant.team.manage') THEN
    RETURN false;
  END IF;

  IF selected.status NOT IN ('pending','sent','failed') THEN
    RETURN false;
  END IF;

  UPDATE odca.tenant_invitations
     SET status = 'cancelled',
         cancelled_at = now(),
         protected_token = NULL,
         token_hash = encode(sha256(('cancelled:' || id::text)::bytea),'hex'),
         updated_at = now()
   WHERE id = invitation_id;

  UPDATE odca.notification_outbox o
     SET status = CASE WHEN o.status IN ('pending','leased') THEN 'failed' ELSE o.status END,
         lease_until = NULL,
         lease_token = NULL,
         lease_owner = NULL,
         last_error_code = COALESCE(o.last_error_code, 'invitation_cancelled'),
         protected_payload = ''
   WHERE o.invitation_id = selected.id
     AND o.status IN ('pending','leased');

  INSERT INTO odca.audit_events(scope_type,tenant_id,actor_user_id,action,entity_type,entity_id,result)
  VALUES ('tenant', selected.tenant_id, actor_id, 'tenant.invitation.cancelled', 'invitation', selected.id, 'success');

  RETURN true;
END;
$$;
REVOKE ALL ON FUNCTION odca.cancel_tenant_invitation(uuid,uuid) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION odca.cancel_tenant_invitation(uuid,uuid) TO odca_app;

CREATE OR REPLACE FUNCTION odca.resend_tenant_invitation(
  actor_id uuid,
  invitation_id uuid,
  new_token_hash text,
  new_protected_token text)
RETURNS boolean
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, odca
AS $$
DECLARE
  selected odca.tenant_invitations%ROWTYPE;
  outbox_id uuid;
BEGIN
  SELECT * INTO selected FROM odca.tenant_invitations WHERE id = invitation_id FOR UPDATE;
  IF NOT FOUND THEN
    RETURN false;
  END IF;

  PERFORM pg_advisory_xact_lock(hashtextextended(selected.tenant_id::text, 0));
  PERFORM odca.expire_tenant_invitations(selected.tenant_id);

  SELECT * INTO selected FROM odca.tenant_invitations WHERE id = invitation_id FOR UPDATE;
  IF selected.status NOT IN ('pending','sent','failed') OR selected.expires_at <= now() THEN
    RETURN false;
  END IF;

  IF NOT odca.tenant_actor_has_permission(actor_id, selected.tenant_id, 'tenant.team.manage') THEN
    RETURN false;
  END IF;

  UPDATE odca.tenant_invitations
     SET token_hash = new_token_hash,
         protected_token = new_protected_token,
         status = 'pending',
         updated_at = now()
   WHERE id = invitation_id;

  UPDATE odca.notification_outbox o
     SET status = 'failed',
         lease_until = NULL,
         lease_token = NULL,
         lease_owner = NULL,
         last_error_code = COALESCE(o.last_error_code, 'invitation_resent'),
         protected_payload = ''
   WHERE o.invitation_id = selected.id
     AND o.status IN ('pending','leased');

  SELECT o.id INTO outbox_id
    FROM odca.notification_outbox o
   WHERE o.invitation_id = selected.id
     AND o.kind = 'invitation'
   ORDER BY o.created_at DESC
   LIMIT 1
   FOR UPDATE;

  IF FOUND THEN
    UPDATE odca.notification_outbox
       SET status = 'pending',
           attempt_count = 0,
           available_at = now(),
           lease_until = NULL,
           lease_token = NULL,
           lease_owner = NULL,
           last_error_code = NULL,
           protected_payload = new_protected_token,
           sent_at = NULL
     WHERE id = outbox_id;
  ELSE
    INSERT INTO odca.notification_outbox(id,tenant_id,invitation_id,kind,destination,protected_payload)
    VALUES (gen_random_uuid(), selected.tenant_id, invitation_id, 'invitation', selected.recipient_email, new_protected_token);
  END IF;

  INSERT INTO odca.audit_events(scope_type,tenant_id,actor_user_id,action,entity_type,entity_id,result)
  VALUES ('tenant', selected.tenant_id, actor_id, 'tenant.invitation.resent', 'invitation', invitation_id, 'success');

  RETURN true;
END;
$$;
REVOKE ALL ON FUNCTION odca.resend_tenant_invitation(uuid,uuid,text,text) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION odca.resend_tenant_invitation(uuid,uuid,text,text) TO odca_app;

CREATE OR REPLACE FUNCTION odca.count_active_tenant_administrators(target_tenant_id uuid)
RETURNS integer
LANGUAGE sql
SECURITY DEFINER
STABLE
SET search_path = pg_catalog, odca
AS $$
  SELECT count(*)::integer
    FROM odca.memberships m
    JOIN odca.member_roles mr ON (mr.tenant_id, mr.user_id) = (m.tenant_id, m.user_id)
    JOIN odca.roles r ON r.id = mr.role_id AND r.tenant_id = mr.tenant_id
    JOIN odca.users u ON u.id = m.user_id AND NOT u.is_deleted
   WHERE m.tenant_id = target_tenant_id
     AND m.status = 'active'
     AND r.code = 'tenant-administrator';
$$;
REVOKE ALL ON FUNCTION odca.count_active_tenant_administrators(uuid) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION odca.count_active_tenant_administrators(uuid) TO odca_app;

CREATE OR REPLACE FUNCTION odca.member_has_tenant_administrator_role(target_tenant_id uuid, target_user_id uuid)
RETURNS boolean
LANGUAGE sql
SECURITY DEFINER
STABLE
SET search_path = pg_catalog, odca
AS $$
  SELECT EXISTS (
    SELECT 1
      FROM odca.member_roles mr
      JOIN odca.roles r ON r.id = mr.role_id AND r.tenant_id = mr.tenant_id
     WHERE mr.tenant_id = target_tenant_id
       AND mr.user_id = target_user_id
       AND r.code = 'tenant-administrator');
$$;
REVOKE ALL ON FUNCTION odca.member_has_tenant_administrator_role(uuid,uuid) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION odca.member_has_tenant_administrator_role(uuid,uuid) TO odca_app;

-- Returns false when the action would leave the tenant without an active administrator.
CREATE OR REPLACE FUNCTION odca.ensure_not_removing_last_admin(
  target_tenant_id uuid,
  target_user_id uuid,
  removing_admin_role boolean,
  changing_membership_away_from_active boolean)
RETURNS boolean
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, odca
AS $$
DECLARE
  admin_count integer;
  is_admin boolean;
BEGIN
  PERFORM pg_advisory_xact_lock(hashtextextended(target_tenant_id::text, 0));
  admin_count := odca.count_active_tenant_administrators(target_tenant_id);
  is_admin := odca.member_has_tenant_administrator_role(target_tenant_id, target_user_id);

  IF NOT is_admin THEN
    RETURN true;
  END IF;

  IF (removing_admin_role OR changing_membership_away_from_active) AND admin_count <= 1 THEN
    RETURN false;
  END IF;

  RETURN true;
END;
$$;
REVOKE ALL ON FUNCTION odca.ensure_not_removing_last_admin(uuid,uuid,boolean,boolean) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION odca.ensure_not_removing_last_admin(uuid,uuid,boolean,boolean) TO odca_app;

CREATE OR REPLACE FUNCTION odca.preview_tenant_invitation(p_invitation_id uuid, presented_hash text)
RETURNS TABLE(
  invitation_id uuid,
  organization_name text,
  role_name text,
  recipient_email text,
  expires_at timestamptz,
  status text)
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, odca
AS $$
DECLARE
  locked_tenant_id uuid;
BEGIN
  SELECT i.tenant_id INTO locked_tenant_id FROM odca.tenant_invitations i WHERE i.id = p_invitation_id;
  IF FOUND THEN
    PERFORM odca.expire_tenant_invitations(locked_tenant_id);
  END IF;

  RETURN QUERY
  SELECT i.id,
         t.display_name,
         r.display_name,
         i.recipient_email,
         i.expires_at,
         i.status
    FROM odca.tenant_invitations i
    JOIN odca.tenants t ON t.id = i.tenant_id
    JOIN odca.roles r ON r.id = i.role_id AND r.tenant_id = i.tenant_id
   WHERE i.id = p_invitation_id
     AND i.token_hash = presented_hash
     AND i.status IN ('pending','sent')
     AND i.expires_at > now();
END;
$$;
REVOKE ALL ON FUNCTION odca.preview_tenant_invitation(uuid,text) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION odca.preview_tenant_invitation(uuid,text) TO odca_app;

INSERT INTO odca.schema_migrations(version,name,checksum)
VALUES (9,'S01 invitation lifecycle and last-admin guards','f2d7bc3a2ed44f1e0601b29d911cd4af4fba774a4d47a2020962519d094209f2')
ON CONFLICT (version) DO NOTHING;
COMMIT;
-- ODCA-END 009

-- ODCA-MIGRATION 010 CHECKSUM 0bd3989b888803ad8bb9ae366ebc4db12969c3b9db2728073e0927401122f3a3
BEGIN;
SELECT pg_advisory_xact_lock(hashtext('odca.schema.migrations'));

-- Records the v009 package repair. The immutable v009 release retains the
-- defective input name; the canonical v009 block is corrected so fresh
-- installations can reach this migration.
COMMENT ON FUNCTION odca.preview_tenant_invitation(uuid,text) IS
  'Previews an invitation after token verification; repaired in package v010 (v009 PostgreSQL 42P13).';

INSERT INTO odca.schema_migrations(version,name,checksum)
VALUES (10,'Repair v009 invitation preview parameter collision','0bd3989b888803ad8bb9ae366ebc4db12969c3b9db2728073e0927401122f3a3')
ON CONFLICT (version) DO NOTHING;
COMMIT;
-- ODCA-END 010


-- ODCA-MIGRATION 011 CHECKSUM f0395f88337735cd4096a54450cb4ea7502f506456d94c90a4478ef87dceda1c
BEGIN;
SELECT pg_advisory_xact_lock(hashtext('odca.schema.migrations'));

INSERT INTO odca.permissions(code,description,delegable) VALUES
 ('tenant.contracts.read','Consultar contratos',true),
 ('tenant.documents.manage','Gerenciar documentos',true),
 ('tenant.documents.download','Baixar documentos',true),
 ('tenant.extractions.request','Solicitar extração',true),
 ('tenant.extractions.review','Revisar dados extraídos',true),
 ('tenant.extractions.apply','Aplicar revisão ao contrato',true),
 ('tenant.contracts.history','Consultar histórico contratual',true)
ON CONFLICT(code) DO UPDATE SET description=EXCLUDED.description,delegable=EXCLUDED.delegable;
INSERT INTO odca.role_permissions(role_id,permission_code)
SELECT r.id,p.code FROM odca.roles r CROSS JOIN odca.permissions p
WHERE r.scope_type='tenant' AND r.code='tenant-administrator' AND p.code IN
 ('tenant.contracts.read','tenant.documents.manage','tenant.documents.download','tenant.extractions.request','tenant.extractions.review','tenant.extractions.apply','tenant.contracts.history')
ON CONFLICT DO NOTHING;

CREATE TABLE odca.contracts (
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(), tenant_id uuid NOT NULL REFERENCES odca.tenants(id),
 title text NOT NULL CHECK(length(btrim(title)) BETWEEN 2 AND 160), reference text,
 start_date date, end_date date, value numeric(18,2), currency char(3), renewal_notice_days integer,
 version bigint NOT NULL DEFAULT 1, created_at timestamptz NOT NULL DEFAULT now(), updated_at timestamptz NOT NULL DEFAULT now(),
 UNIQUE(tenant_id,id), CHECK(end_date IS NULL OR start_date IS NULL OR end_date>=start_date));
CREATE TABLE odca.contract_documents (
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(), tenant_id uuid NOT NULL, contract_id uuid NOT NULL,
 title text NOT NULL, created_by uuid NOT NULL REFERENCES odca.users(id), created_at timestamptz NOT NULL DEFAULT now(),
 deleted_at timestamptz, deleted_by uuid REFERENCES odca.users(id), deletion_reason text,
 UNIQUE(tenant_id,id), FOREIGN KEY(tenant_id,contract_id) REFERENCES odca.contracts(tenant_id,id));
CREATE TABLE odca.document_versions (
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(), tenant_id uuid NOT NULL, contract_id uuid NOT NULL, document_id uuid NOT NULL,
 version_number integer NOT NULL CHECK(version_number>0), uploaded_by uuid NOT NULL REFERENCES odca.users(id), uploaded_at timestamptz NOT NULL DEFAULT now(),
 display_name text NOT NULL, detected_type text NOT NULL CHECK(detected_type IN('pdf','png','jpeg','docx')),
 byte_size bigint NOT NULL CHECK(byte_size>0 AND byte_size<=26214400), sha256 char(64) NOT NULL,
 storage_key text NOT NULL UNIQUE, security_status text NOT NULL DEFAULT 'pending' CHECK(security_status IN('pending','scanning','safe','rejected','scan_failed')),
 security_checked_at timestamptz, security_engine text, UNIQUE(document_id,version_number), UNIQUE(tenant_id,id),
 FOREIGN KEY(tenant_id,contract_id) REFERENCES odca.contracts(tenant_id,id), FOREIGN KEY(tenant_id,document_id) REFERENCES odca.contract_documents(tenant_id,id));
CREATE TABLE odca.extraction_jobs (
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(), tenant_id uuid NOT NULL, contract_id uuid NOT NULL, version_id uuid NOT NULL,
 requested_by uuid NOT NULL REFERENCES odca.users(id), requested_at timestamptz NOT NULL DEFAULT now(),
 status text NOT NULL DEFAULT 'queued' CHECK(status IN('queued','processing','ready_for_review','failed','cancelled','superseded')),
 attempt_count integer NOT NULL DEFAULT 0, max_attempts integer NOT NULL DEFAULT 3,
 available_at timestamptz NOT NULL DEFAULT now(), lease_token uuid, lease_expires_at timestamptz,
 completed_at timestamptz, failure_code text, UNIQUE(tenant_id,id),
 FOREIGN KEY(tenant_id,contract_id) REFERENCES odca.contracts(tenant_id,id), FOREIGN KEY(tenant_id,version_id) REFERENCES odca.document_versions(tenant_id,id));
CREATE TABLE odca.extraction_results (
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(), tenant_id uuid NOT NULL, job_id uuid NOT NULL UNIQUE, version_id uuid NOT NULL,
 method text NOT NULL CHECK(method IN('pdf-native','ocr','docx-structure')), raw_text text NOT NULL,
 created_at timestamptz NOT NULL DEFAULT now(), UNIQUE(tenant_id,id),
 FOREIGN KEY(tenant_id,job_id) REFERENCES odca.extraction_jobs(tenant_id,id), FOREIGN KEY(tenant_id,version_id) REFERENCES odca.document_versions(tenant_id,id));
CREATE TABLE odca.extraction_suggestions (
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(), tenant_id uuid NOT NULL, result_id uuid NOT NULL, version_id uuid NOT NULL,
 field_name text NOT NULL, extracted_value text NOT NULL, normalized_value text, evidence text NOT NULL,
 page_number integer, location text, method text NOT NULL,
 review_status text NOT NULL DEFAULT 'pending' CHECK(review_status IN('pending','accepted','edited','rejected','conflicting')),
 reviewed_value text, reviewed_by uuid REFERENCES odca.users(id), reviewed_at timestamptz,
 UNIQUE(tenant_id,id), FOREIGN KEY(tenant_id,result_id) REFERENCES odca.extraction_results(tenant_id,id), FOREIGN KEY(tenant_id,version_id) REFERENCES odca.document_versions(tenant_id,id));
CREATE TABLE odca.extraction_reviews (
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(), tenant_id uuid NOT NULL, contract_id uuid NOT NULL, job_id uuid NOT NULL,
 reviewed_by uuid NOT NULL REFERENCES odca.users(id), contract_version bigint NOT NULL, idempotency_key uuid NOT NULL,
 status text NOT NULL DEFAULT 'draft' CHECK(status IN('draft','applied','conflicting')), applied_at timestamptz,
 UNIQUE(tenant_id,id), UNIQUE(tenant_id,idempotency_key), FOREIGN KEY(tenant_id,contract_id) REFERENCES odca.contracts(tenant_id,id), FOREIGN KEY(tenant_id,job_id) REFERENCES odca.extraction_jobs(tenant_id,id));
CREATE TABLE odca.contract_events (
 id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY, tenant_id uuid NOT NULL, contract_id uuid NOT NULL,
 actor_id uuid NOT NULL REFERENCES odca.users(id), event_type text NOT NULL, occurred_at timestamptz NOT NULL DEFAULT now(), details jsonb NOT NULL DEFAULT '{}'::jsonb,
 FOREIGN KEY(tenant_id,contract_id) REFERENCES odca.contracts(tenant_id,id));
CREATE TABLE odca.tenant_storage_usage (
 tenant_id uuid PRIMARY KEY REFERENCES odca.tenants(id), used_bytes bigint NOT NULL DEFAULT 0 CHECK(used_bytes>=0), reserved_bytes bigint NOT NULL DEFAULT 0 CHECK(reserved_bytes>=0), quota_bytes bigint NOT NULL DEFAULT 1073741824 CHECK(quota_bytes>0));

CREATE INDEX ix_document_versions_contract ON odca.document_versions(tenant_id,contract_id,uploaded_at DESC);
CREATE INDEX ix_extraction_jobs_claim ON odca.extraction_jobs(status,available_at,lease_expires_at);
CREATE INDEX ix_suggestions_result ON odca.extraction_suggestions(tenant_id,result_id,review_status);
CREATE INDEX ix_contract_events_contract ON odca.contract_events(tenant_id,contract_id,occurred_at DESC);

ALTER TABLE odca.contracts ENABLE ROW LEVEL SECURITY; ALTER TABLE odca.contracts FORCE ROW LEVEL SECURITY;
ALTER TABLE odca.contract_documents ENABLE ROW LEVEL SECURITY; ALTER TABLE odca.contract_documents FORCE ROW LEVEL SECURITY;
ALTER TABLE odca.document_versions ENABLE ROW LEVEL SECURITY; ALTER TABLE odca.document_versions FORCE ROW LEVEL SECURITY;
ALTER TABLE odca.extraction_jobs ENABLE ROW LEVEL SECURITY; ALTER TABLE odca.extraction_jobs FORCE ROW LEVEL SECURITY;
ALTER TABLE odca.extraction_results ENABLE ROW LEVEL SECURITY; ALTER TABLE odca.extraction_results FORCE ROW LEVEL SECURITY;
ALTER TABLE odca.extraction_suggestions ENABLE ROW LEVEL SECURITY; ALTER TABLE odca.extraction_suggestions FORCE ROW LEVEL SECURITY;
ALTER TABLE odca.extraction_reviews ENABLE ROW LEVEL SECURITY; ALTER TABLE odca.extraction_reviews FORCE ROW LEVEL SECURITY;
ALTER TABLE odca.contract_events ENABLE ROW LEVEL SECURITY; ALTER TABLE odca.contract_events FORCE ROW LEVEL SECURITY;
ALTER TABLE odca.tenant_storage_usage ENABLE ROW LEVEL SECURITY; ALTER TABLE odca.tenant_storage_usage FORCE ROW LEVEL SECURITY;
DO $policy$ DECLARE n text; BEGIN FOREACH n IN ARRAY ARRAY['contracts','contract_documents','document_versions','extraction_jobs','extraction_results','extraction_suggestions','extraction_reviews','contract_events','tenant_storage_usage'] LOOP
 EXECUTE format('CREATE POLICY tenant_isolation ON odca.%I TO odca_app USING (tenant_id = nullif(current_setting(''odca.tenant_id'',true),'''')::uuid) WITH CHECK (tenant_id = nullif(current_setting(''odca.tenant_id'',true),'''')::uuid)',n);
END LOOP; END $policy$;

CREATE OR REPLACE FUNCTION odca.claim_document_scan()
RETURNS TABLE(id uuid,tenant_id uuid,storage_key text,detected_type text)
LANGUAGE sql SECURITY DEFINER SET search_path=pg_catalog,odca AS $$
 UPDATE odca.document_versions SET security_status='scanning'
 WHERE document_versions.id=(SELECT v.id FROM odca.document_versions v WHERE v.security_status IN('pending','scan_failed') ORDER BY v.uploaded_at FOR UPDATE SKIP LOCKED LIMIT 1)
 RETURNING document_versions.id,document_versions.tenant_id,document_versions.storage_key,document_versions.detected_type;
$$;
CREATE OR REPLACE FUNCTION odca.claim_extraction_job(requested_lease uuid)
RETURNS TABLE(id uuid,tenant_id uuid,contract_id uuid,version_id uuid,lease_token uuid)
LANGUAGE sql SECURITY DEFINER SET search_path=pg_catalog,odca AS $$
 UPDATE odca.extraction_jobs SET status='processing',attempt_count=attempt_count+1,lease_token=requested_lease,lease_expires_at=now()+interval '5 minutes'
 WHERE extraction_jobs.id=(SELECT j.id FROM odca.extraction_jobs j JOIN odca.document_versions v ON v.id=j.version_id AND v.tenant_id=j.tenant_id
  WHERE (j.status='queued' OR (j.status='processing' AND j.lease_expires_at<now())) AND j.available_at<=now() AND j.attempt_count<j.max_attempts AND v.security_status='safe'
  ORDER BY j.requested_at FOR UPDATE OF j SKIP LOCKED LIMIT 1)
 RETURNING extraction_jobs.id,extraction_jobs.tenant_id,extraction_jobs.contract_id,extraction_jobs.version_id,extraction_jobs.lease_token;
$$;
REVOKE ALL ON FUNCTION odca.claim_document_scan() FROM PUBLIC;
REVOKE ALL ON FUNCTION odca.claim_extraction_job(uuid) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION odca.claim_document_scan(),odca.claim_extraction_job(uuid) TO odca_app;

GRANT SELECT,INSERT,UPDATE ON odca.contracts,odca.contract_documents,odca.document_versions,odca.extraction_jobs,odca.extraction_results,odca.extraction_suggestions,odca.extraction_reviews,odca.contract_events,odca.tenant_storage_usage TO odca_app;
GRANT USAGE,SELECT ON SEQUENCE odca.contract_events_id_seq TO odca_app;

INSERT INTO odca.schema_migrations(version,name,checksum)
VALUES(11,'S02 immutable documents assisted extraction and review','f0395f88337735cd4096a54450cb4ea7502f506456d94c90a4478ef87dceda1c') ON CONFLICT(version) DO NOTHING;
COMMIT;
-- ODCA-END 011

-- ODCA-MIGRATION 012 CHECKSUM 1487904825f3c0468b37217e9810128fc189d388390ea3c94671830b74ef57aa
BEGIN;
SELECT pg_advisory_xact_lock(hashtext('odca.schema.migrations'));

INSERT INTO odca.permissions(code,description,delegable) VALUES
 ('tenant.reviews.request','Solicitar revisão interna',true),
 ('tenant.reviews.read','Consultar revisão interna',true),
 ('tenant.reviews.decide','Decidir etapa de revisão interna',true),
 ('tenant.reviews.cancel','Cancelar revisão interna',true),
 ('tenant.reviews.reassign','Reatribuir etapa de revisão interna',true),
 ('tenant.reviews.history','Consultar histórico da revisão',true)
ON CONFLICT(code) DO UPDATE SET description=EXCLUDED.description,delegable=EXCLUDED.delegable;
INSERT INTO odca.role_permissions(role_id,permission_code)
SELECT r.id,p.code FROM odca.roles r CROSS JOIN odca.permissions p
WHERE r.scope_type='tenant' AND r.code='tenant-administrator' AND p.code LIKE 'tenant.reviews.%'
ON CONFLICT DO NOTHING;

CREATE TABLE odca.contract_review_requests (
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(), tenant_id uuid NOT NULL, contract_id uuid NOT NULL,
 document_version_id uuid NOT NULL, requested_by uuid NOT NULL, due_at timestamptz, instructions varchar(2000),
 status text NOT NULL DEFAULT 'in_review' CHECK(status IN('in_review','changes_requested','internally_approved','cancelled','superseded')),
 content_snapshot jsonb NOT NULL, document_sha256 char(64) NOT NULL CHECK(document_sha256 ~ '^[a-f0-9]{64}$'),
 idempotency_key uuid NOT NULL, row_version bigint NOT NULL DEFAULT 1 CHECK(row_version>0),
 opened_at timestamptz NOT NULL DEFAULT now(), completed_at timestamptz, updated_at timestamptz NOT NULL DEFAULT now(),
 UNIQUE(tenant_id,id), UNIQUE(tenant_id,idempotency_key),
 FOREIGN KEY(tenant_id,contract_id) REFERENCES odca.contracts(tenant_id,id),
 FOREIGN KEY(tenant_id,document_version_id) REFERENCES odca.document_versions(tenant_id,id),
 FOREIGN KEY(tenant_id,requested_by) REFERENCES odca.memberships(tenant_id,user_id),
 CHECK((status IN('in_review','changes_requested') AND completed_at IS NULL) OR
       (status IN('internally_approved','cancelled','superseded') AND completed_at IS NOT NULL))
);
CREATE TABLE odca.contract_review_steps (
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(), tenant_id uuid NOT NULL, review_id uuid NOT NULL,
 sequence integer NOT NULL CHECK(sequence>0), reviewer_id uuid NOT NULL,
 status text NOT NULL CHECK(status IN('waiting','current','approved','changes_requested','reassigned')),
 decided_by uuid, decided_at timestamptz, justification varchar(2000), assignment_version integer NOT NULL DEFAULT 1,
 UNIQUE(tenant_id,id), UNIQUE(review_id,sequence,assignment_version),
 FOREIGN KEY(tenant_id,review_id) REFERENCES odca.contract_review_requests(tenant_id,id),
 FOREIGN KEY(tenant_id,reviewer_id) REFERENCES odca.memberships(tenant_id,user_id),
 FOREIGN KEY(tenant_id,decided_by) REFERENCES odca.memberships(tenant_id,user_id),
 CHECK((status IN('waiting','current') AND decided_at IS NULL AND decided_by IS NULL) OR
       (status NOT IN('waiting','current') AND decided_at IS NOT NULL AND decided_by IS NOT NULL))
);
CREATE UNIQUE INDEX contract_review_one_current_uq ON odca.contract_review_steps(review_id) WHERE status='current';
CREATE UNIQUE INDEX contract_review_active_reviewer_uq ON odca.contract_review_steps(review_id,reviewer_id) WHERE status<>'reassigned';
CREATE TABLE odca.contract_review_comments (
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(), tenant_id uuid NOT NULL, review_id uuid NOT NULL, document_version_id uuid NOT NULL,
 author_id uuid NOT NULL, body varchar(4000) NOT NULL CHECK(length(btrim(body))>0), reference varchar(120),
 created_at timestamptz NOT NULL DEFAULT now(), resolved_by uuid, resolved_at timestamptz,
 UNIQUE(tenant_id,id), FOREIGN KEY(tenant_id,review_id) REFERENCES odca.contract_review_requests(tenant_id,id),
 FOREIGN KEY(tenant_id,document_version_id) REFERENCES odca.document_versions(tenant_id,id),
 FOREIGN KEY(tenant_id,author_id) REFERENCES odca.memberships(tenant_id,user_id),
 FOREIGN KEY(tenant_id,resolved_by) REFERENCES odca.memberships(tenant_id,user_id),
 CHECK((resolved_at IS NULL AND resolved_by IS NULL) OR (resolved_at IS NOT NULL AND resolved_by IS NOT NULL))
);
CREATE TABLE odca.contract_review_events (
 id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY, tenant_id uuid NOT NULL, review_id uuid NOT NULL,
 actor_id uuid NOT NULL, event_type varchar(80) NOT NULL, details jsonb NOT NULL DEFAULT '{}'::jsonb,
 occurred_at timestamptz NOT NULL DEFAULT now(),
 FOREIGN KEY(tenant_id,review_id) REFERENCES odca.contract_review_requests(tenant_id,id),
 FOREIGN KEY(tenant_id,actor_id) REFERENCES odca.memberships(tenant_id,user_id)
);
CREATE TABLE odca.contract_review_notifications (
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(), tenant_id uuid NOT NULL, review_id uuid NOT NULL, recipient_id uuid NOT NULL,
 kind varchar(80) NOT NULL, deduplication_key varchar(160) NOT NULL, available_at timestamptz NOT NULL DEFAULT now(),
 status text NOT NULL DEFAULT 'pending' CHECK(status IN('pending','leased','delivered','failed')),
 attempt_count integer NOT NULL DEFAULT 0, lease_until timestamptz, delivered_at timestamptz, created_at timestamptz NOT NULL DEFAULT now(),
 UNIQUE(tenant_id,deduplication_key), FOREIGN KEY(tenant_id,review_id) REFERENCES odca.contract_review_requests(tenant_id,id),
 FOREIGN KEY(tenant_id,recipient_id) REFERENCES odca.memberships(tenant_id,user_id)
);
CREATE INDEX contract_reviews_mine_ix ON odca.contract_review_requests(tenant_id,status,due_at,updated_at DESC);
CREATE INDEX contract_review_steps_reviewer_ix ON odca.contract_review_steps(tenant_id,reviewer_id,status,review_id);
CREATE INDEX contract_review_comments_pending_ix ON odca.contract_review_comments(tenant_id,review_id,created_at) WHERE resolved_at IS NULL;
CREATE INDEX contract_review_notifications_claim_ix ON odca.contract_review_notifications(status,available_at,lease_until);

ALTER TABLE odca.contract_review_requests ENABLE ROW LEVEL SECURITY; ALTER TABLE odca.contract_review_requests FORCE ROW LEVEL SECURITY;
ALTER TABLE odca.contract_review_steps ENABLE ROW LEVEL SECURITY; ALTER TABLE odca.contract_review_steps FORCE ROW LEVEL SECURITY;
ALTER TABLE odca.contract_review_comments ENABLE ROW LEVEL SECURITY; ALTER TABLE odca.contract_review_comments FORCE ROW LEVEL SECURITY;
ALTER TABLE odca.contract_review_events ENABLE ROW LEVEL SECURITY; ALTER TABLE odca.contract_review_events FORCE ROW LEVEL SECURITY;
ALTER TABLE odca.contract_review_notifications ENABLE ROW LEVEL SECURITY; ALTER TABLE odca.contract_review_notifications FORCE ROW LEVEL SECURITY;
DO $policy$ DECLARE n text; BEGIN FOREACH n IN ARRAY ARRAY['contract_review_requests','contract_review_steps','contract_review_comments','contract_review_events','contract_review_notifications'] LOOP
 EXECUTE format('CREATE POLICY tenant_isolation ON odca.%I TO odca_app USING (tenant_id = nullif(current_setting(''odca.tenant_id'',true),'''')::uuid) WITH CHECK (tenant_id = nullif(current_setting(''odca.tenant_id'',true),'''')::uuid)',n);
END LOOP; END $policy$;
GRANT SELECT,INSERT,UPDATE ON odca.contract_review_requests,odca.contract_review_steps,odca.contract_review_comments,odca.contract_review_events,odca.contract_review_notifications TO odca_app;
GRANT USAGE,SELECT ON SEQUENCE odca.contract_review_events_id_seq TO odca_app;

INSERT INTO odca.schema_migrations(version,name,checksum)
VALUES(12,'S02 sequential internal contract review','1487904825f3c0468b37217e9810128fc189d388390ea3c94671830b74ef57aa') ON CONFLICT(version) DO NOTHING;
COMMIT;
-- ODCA-END 012

-- ODCA-MIGRATION 013 CHECKSUM 422b0f76fdbd605421026c864f12a2c19a0c3a82033df5b6af5efa7646239860
BEGIN;
SELECT pg_advisory_xact_lock(hashtext('odca.schema.migrations'));

INSERT INTO odca.permissions(code,description,delegable) VALUES
 ('tenant.obligations.read','Consultar obrigações',true),('tenant.obligations.read_all','Consultar obrigações da organização',true),
 ('tenant.obligations.manage','Criar e editar obrigações',true),('tenant.obligations.assign','Atribuir responsável por obrigação',true),
 ('tenant.obligations.fulfill','Registrar cumprimento',true),('tenant.obligations.reopen','Reabrir obrigação',true),
 ('tenant.obligations.cancel','Cancelar obrigação',true),('tenant.obligations.recurrence','Gerenciar recorrência',true),
 ('tenant.renewals.decide','Decidir renovação',true),('tenant.renewals.register','Registrar renovação',true)
ON CONFLICT(code) DO UPDATE SET description=EXCLUDED.description,delegable=EXCLUDED.delegable;
INSERT INTO odca.role_permissions(role_id,permission_code) SELECT r.id,p.code FROM odca.roles r CROSS JOIN odca.permissions p
 WHERE r.scope_type='tenant' AND r.code='tenant-administrator' AND (p.code LIKE 'tenant.obligations.%' OR p.code LIKE 'tenant.renewals.%') ON CONFLICT DO NOTHING;

CREATE TABLE odca.obligation_series(
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(),tenant_id uuid NOT NULL,contract_id uuid NOT NULL,base_date date NOT NULL,intended_day smallint NOT NULL CHECK(intended_day BETWEEN 1 AND 31),
 ends_on date,occurrence_count integer CHECK(occurrence_count BETWEEN 1 AND 120),timezone text NOT NULL,policy text NOT NULL DEFAULT 'last_valid_day' CHECK(policy='last_valid_day'),row_version bigint NOT NULL DEFAULT 1,
 created_by uuid NOT NULL,created_at timestamptz NOT NULL DEFAULT now(),updated_at timestamptz NOT NULL DEFAULT now(),UNIQUE(tenant_id,id),
 FOREIGN KEY(tenant_id,contract_id) REFERENCES odca.contracts(tenant_id,id),FOREIGN KEY(tenant_id,created_by) REFERENCES odca.memberships(tenant_id,user_id),CHECK(ends_on IS NOT NULL OR occurrence_count IS NOT NULL));
CREATE TABLE odca.contract_obligations(
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(),tenant_id uuid NOT NULL,contract_id uuid NOT NULL,title varchar(160) NOT NULL CHECK(length(btrim(title))>0),description varchar(4000),
 category text NOT NULL CHECK(category IN('delivery','document','renewal','communication','financial','other')),obligated_party varchar(200) NOT NULL,owner_id uuid NOT NULL,due_date date NOT NULL,
 priority text NOT NULL CHECK(priority IN('low','normal','high','critical')),status text NOT NULL DEFAULT 'open' CHECK(status IN('open','in_progress','fulfilled','cancelled')),
 origin text NOT NULL CHECK(origin IN('manual','reviewed_suggestion')),amount numeric(18,2),currency char(3),clause_document_version_id uuid,evidence_required boolean NOT NULL DEFAULT false,
 post_term_reason varchar(1000),series_id uuid,occurrence_index integer,fulfilled_at timestamptz,fulfilled_by uuid,fulfillment_note varchar(2000),row_version bigint NOT NULL DEFAULT 1,
 created_by uuid NOT NULL,created_at timestamptz NOT NULL DEFAULT now(),updated_at timestamptz NOT NULL DEFAULT now(),deleted_at timestamptz,deleted_by uuid,deletion_reason varchar(1000),UNIQUE(tenant_id,id),
 FOREIGN KEY(tenant_id,contract_id) REFERENCES odca.contracts(tenant_id,id),FOREIGN KEY(tenant_id,owner_id) REFERENCES odca.memberships(tenant_id,user_id),FOREIGN KEY(tenant_id,created_by) REFERENCES odca.memberships(tenant_id,user_id),
 FOREIGN KEY(tenant_id,fulfilled_by) REFERENCES odca.memberships(tenant_id,user_id),FOREIGN KEY(tenant_id,deleted_by) REFERENCES odca.memberships(tenant_id,user_id),FOREIGN KEY(tenant_id,clause_document_version_id) REFERENCES odca.document_versions(tenant_id,id),
 FOREIGN KEY(tenant_id,series_id) REFERENCES odca.obligation_series(tenant_id,id),UNIQUE(series_id,occurrence_index),
 CHECK((category='financial' AND amount>0 AND currency ~ '^[A-Z]{3}$') OR (category<>'financial' AND amount IS NULL AND currency IS NULL)),
 CHECK((status='fulfilled' AND fulfilled_at IS NOT NULL AND fulfilled_by IS NOT NULL) OR (status<>'fulfilled' AND fulfilled_at IS NULL AND fulfilled_by IS NULL)),CHECK((deleted_at IS NULL AND deleted_by IS NULL AND deletion_reason IS NULL) OR (deleted_at IS NOT NULL AND deleted_by IS NOT NULL AND length(btrim(deletion_reason))>0)));
CREATE TABLE odca.obligation_evidence(tenant_id uuid NOT NULL,obligation_id uuid NOT NULL,document_version_id uuid NOT NULL,linked_by uuid NOT NULL,linked_at timestamptz NOT NULL DEFAULT now(),PRIMARY KEY(obligation_id,document_version_id),
 FOREIGN KEY(tenant_id,obligation_id) REFERENCES odca.contract_obligations(tenant_id,id),FOREIGN KEY(tenant_id,document_version_id) REFERENCES odca.document_versions(tenant_id,id),FOREIGN KEY(tenant_id,linked_by) REFERENCES odca.memberships(tenant_id,user_id));
CREATE TABLE odca.obligation_events(id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,tenant_id uuid NOT NULL,obligation_id uuid NOT NULL,actor_id uuid NOT NULL,event_type varchar(80) NOT NULL,details jsonb NOT NULL DEFAULT '{}',occurred_at timestamptz NOT NULL DEFAULT now(),
 FOREIGN KEY(tenant_id,obligation_id) REFERENCES odca.contract_obligations(tenant_id,id),FOREIGN KEY(tenant_id,actor_id) REFERENCES odca.memberships(tenant_id,user_id));
CREATE TABLE odca.obligation_reminders(id uuid PRIMARY KEY DEFAULT gen_random_uuid(),tenant_id uuid NOT NULL,obligation_id uuid NOT NULL,recipient_id uuid,days_before integer NOT NULL CHECK(days_before BETWEEN 0 AND 365),scheduled_for date NOT NULL,
 deduplication_key varchar(160) NOT NULL,status text NOT NULL DEFAULT 'pending' CHECK(status IN('pending','leased','delivered','obsolete','failed')),attempt_count integer NOT NULL DEFAULT 0 CHECK(attempt_count<=5),lease_owner uuid,lease_token uuid,lease_until timestamptz,delivered_at timestamptz,created_at timestamptz NOT NULL DEFAULT now(),
 UNIQUE(tenant_id,deduplication_key),FOREIGN KEY(tenant_id,obligation_id) REFERENCES odca.contract_obligations(tenant_id,id),FOREIGN KEY(tenant_id,recipient_id) REFERENCES odca.memberships(tenant_id,user_id));
CREATE TABLE odca.user_notifications(id uuid PRIMARY KEY DEFAULT gen_random_uuid(),tenant_id uuid NOT NULL,user_id uuid NOT NULL,kind varchar(80) NOT NULL,title varchar(200) NOT NULL,body varchar(1000) NOT NULL,obligation_id uuid,created_at timestamptz NOT NULL DEFAULT now(),read_at timestamptz,UNIQUE(tenant_id,id),FOREIGN KEY(tenant_id,user_id) REFERENCES odca.memberships(tenant_id,user_id),FOREIGN KEY(tenant_id,obligation_id) REFERENCES odca.contract_obligations(tenant_id,id));
CREATE TABLE odca.contract_renewal_cycles(id uuid PRIMARY KEY DEFAULT gen_random_uuid(),tenant_id uuid NOT NULL,contract_id uuid NOT NULL,cycle_number integer NOT NULL,starts_on date,ends_on date,notice_due_on date,decision_owner_id uuid,
 decision text NOT NULL DEFAULT 'pending' CHECK(decision IN('pending','intent_to_renew','negotiating','not_renewing','renewed')),justification varchar(2000),decided_by uuid,decided_at timestamptz,previous_cycle_id uuid,row_version bigint NOT NULL DEFAULT 1,created_at timestamptz NOT NULL DEFAULT now(),
 UNIQUE(tenant_id,id),UNIQUE(contract_id,cycle_number),FOREIGN KEY(tenant_id,contract_id) REFERENCES odca.contracts(tenant_id,id),FOREIGN KEY(tenant_id,decision_owner_id) REFERENCES odca.memberships(tenant_id,user_id),FOREIGN KEY(tenant_id,decided_by) REFERENCES odca.memberships(tenant_id,user_id),FOREIGN KEY(tenant_id,previous_cycle_id) REFERENCES odca.contract_renewal_cycles(tenant_id,id),CHECK(ends_on IS NULL OR starts_on IS NULL OR ends_on>=starts_on));
CREATE INDEX obligation_operational_ix ON odca.contract_obligations(tenant_id,status,due_date,id) WHERE deleted_at IS NULL;
CREATE INDEX obligation_owner_ix ON odca.contract_obligations(tenant_id,owner_id,status,due_date) WHERE deleted_at IS NULL;
CREATE INDEX obligation_reminder_claim_ix ON odca.obligation_reminders(status,scheduled_for,lease_until) WHERE status IN('pending','leased');
CREATE INDEX renewal_decision_ix ON odca.contract_renewal_cycles(tenant_id,decision,notice_due_on);
ALTER TABLE odca.obligation_series ENABLE ROW LEVEL SECURITY;ALTER TABLE odca.obligation_series FORCE ROW LEVEL SECURITY;ALTER TABLE odca.contract_obligations ENABLE ROW LEVEL SECURITY;ALTER TABLE odca.contract_obligations FORCE ROW LEVEL SECURITY;ALTER TABLE odca.obligation_evidence ENABLE ROW LEVEL SECURITY;ALTER TABLE odca.obligation_evidence FORCE ROW LEVEL SECURITY;ALTER TABLE odca.obligation_events ENABLE ROW LEVEL SECURITY;ALTER TABLE odca.obligation_events FORCE ROW LEVEL SECURITY;ALTER TABLE odca.obligation_reminders ENABLE ROW LEVEL SECURITY;ALTER TABLE odca.obligation_reminders FORCE ROW LEVEL SECURITY;ALTER TABLE odca.contract_renewal_cycles ENABLE ROW LEVEL SECURITY;ALTER TABLE odca.contract_renewal_cycles FORCE ROW LEVEL SECURITY;ALTER TABLE odca.user_notifications ENABLE ROW LEVEL SECURITY;ALTER TABLE odca.user_notifications FORCE ROW LEVEL SECURITY;
DO $policy$ DECLARE n text;BEGIN FOREACH n IN ARRAY ARRAY['obligation_series','contract_obligations','obligation_evidence','obligation_events','obligation_reminders','contract_renewal_cycles','user_notifications'] LOOP EXECUTE format('CREATE POLICY tenant_isolation ON odca.%I TO odca_app USING (tenant_id = nullif(current_setting(''odca.tenant_id'',true),'''')::uuid) WITH CHECK (tenant_id = nullif(current_setting(''odca.tenant_id'',true),'''')::uuid)',n);END LOOP;END $policy$;
CREATE OR REPLACE FUNCTION odca.claim_obligation_reminder(p_owner uuid,p_token uuid) RETURNS TABLE(id uuid,tenant_id uuid,obligation_id uuid,recipient_id uuid) LANGUAGE sql SECURITY DEFINER SET search_path=pg_catalog,odca AS $$
 UPDATE odca.obligation_reminders r SET status='leased',attempt_count=attempt_count+1,lease_owner=p_owner,lease_token=p_token,lease_until=now()+interval '2 minutes',recipient_id=o.owner_id
 FROM odca.contract_obligations o JOIN odca.memberships m ON m.tenant_id=o.tenant_id AND m.user_id=o.owner_id
 WHERE r.id=(SELECT x.id FROM odca.obligation_reminders x JOIN odca.contract_obligations co ON co.id=x.obligation_id AND co.tenant_id=x.tenant_id JOIN odca.tenants t ON t.id=x.tenant_id
 WHERE (x.status='pending' OR (x.status='leased' AND x.lease_until<now())) AND x.attempt_count<5 AND x.scheduled_for BETWEEN ((now() AT TIME ZONE t.timezone)::date-1) AND (now() AT TIME ZONE t.timezone)::date AND co.status IN('open','in_progress') ORDER BY x.scheduled_for,x.id FOR UPDATE OF x SKIP LOCKED LIMIT 1)
 AND o.id=r.obligation_id AND o.tenant_id=r.tenant_id AND m.status='active' RETURNING r.id,r.tenant_id,r.obligation_id,r.recipient_id;$$;
CREATE OR REPLACE FUNCTION odca.complete_obligation_reminder(p_id uuid,p_token uuid) RETURNS boolean LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,odca AS $$ DECLARE changed integer;BEGIN INSERT INTO odca.user_notifications(tenant_id,user_id,kind,title,body,obligation_id) SELECT r.tenant_id,r.recipient_id,'obligation_due','Prazo contratual: '||o.title,'Vencimento em '||to_char(o.due_date,'DD/MM/YYYY')||'.',o.id FROM odca.obligation_reminders r JOIN odca.contract_obligations o ON o.id=r.obligation_id AND o.tenant_id=r.tenant_id WHERE r.id=p_id AND r.lease_token=p_token AND r.status='leased' ON CONFLICT DO NOTHING;UPDATE odca.obligation_reminders SET status='delivered',delivered_at=now(),lease_until=NULL WHERE id=p_id AND lease_token=p_token AND status='leased';GET DIAGNOSTICS changed=ROW_COUNT;RETURN changed=1;END;$$;
REVOKE ALL ON FUNCTION odca.complete_obligation_reminder(uuid,uuid) FROM PUBLIC;GRANT EXECUTE ON FUNCTION odca.complete_obligation_reminder(uuid,uuid) TO odca_app;
REVOKE ALL ON FUNCTION odca.claim_obligation_reminder(uuid,uuid) FROM PUBLIC;GRANT EXECUTE ON FUNCTION odca.claim_obligation_reminder(uuid,uuid) TO odca_app;
GRANT SELECT,INSERT,UPDATE ON odca.obligation_series,odca.contract_obligations,odca.obligation_evidence,odca.obligation_events,odca.obligation_reminders,odca.contract_renewal_cycles,odca.user_notifications TO odca_app;GRANT USAGE,SELECT ON SEQUENCE odca.obligation_events_id_seq TO odca_app;
INSERT INTO odca.schema_migrations(version,name,checksum) VALUES(13,'S03 contractual obligations and renewal cycles','422b0f76fdbd605421026c864f12a2c19a0c3a82033df5b6af5efa7646239860') ON CONFLICT(version) DO NOTHING;
COMMIT;
-- ODCA-END 013
-- ODCA-MIGRATION 014 CHECKSUM 2cc024fc41b0cb12adbaac3db2fa480a33989f3a4b061c01203d5838fcf930d1
BEGIN;
SELECT pg_advisory_xact_lock(hashtext('odca.schema.migrations'));

INSERT INTO odca.permissions(code,description,delegable)
VALUES ('tenant.saved_views.manage','Gerenciar vistas pessoais de trabalho',true)
ON CONFLICT(code) DO UPDATE SET description=EXCLUDED.description,delegable=EXCLUDED.delegable;
INSERT INTO odca.role_permissions(role_id,permission_code)
SELECT id,'tenant.saved_views.manage' FROM odca.roles
WHERE scope_type='tenant' AND code='tenant-administrator'
ON CONFLICT DO NOTHING;

CREATE TABLE odca.saved_work_views(
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
 tenant_id uuid NOT NULL,
 owner_id uuid NOT NULL,
 name varchar(80) NOT NULL CHECK(length(btrim(name)) BETWEEN 1 AND 80),
 listing_type text NOT NULL CHECK(listing_type IN('obligations','reviews','contracts')),
 filters jsonb NOT NULL DEFAULT '{}'::jsonb CHECK(jsonb_typeof(filters)='object'),
 sort varchar(40) NOT NULL,
 is_default boolean NOT NULL DEFAULT false,
 row_version bigint NOT NULL DEFAULT 1,
 created_at timestamptz NOT NULL DEFAULT now(),
 updated_at timestamptz NOT NULL DEFAULT now(),
 inactive_at timestamptz,
 UNIQUE(tenant_id,id),
 FOREIGN KEY(tenant_id,owner_id) REFERENCES odca.memberships(tenant_id,user_id)
);
CREATE UNIQUE INDEX saved_work_views_active_name_uq ON odca.saved_work_views(tenant_id,owner_id,listing_type,lower(name)) WHERE inactive_at IS NULL;
CREATE UNIQUE INDEX saved_work_views_default_uq ON odca.saved_work_views(tenant_id,owner_id,listing_type) WHERE is_default AND inactive_at IS NULL;
CREATE INDEX saved_work_views_list_ix ON odca.saved_work_views(tenant_id,owner_id,listing_type,name,id) WHERE inactive_at IS NULL;
ALTER TABLE odca.saved_work_views ENABLE ROW LEVEL SECURITY;
ALTER TABLE odca.saved_work_views FORCE ROW LEVEL SECURITY;
CREATE POLICY saved_work_views_owner_isolation ON odca.saved_work_views TO odca_app
 USING (tenant_id=nullif(current_setting('odca.tenant_id',true),'')::uuid AND owner_id=nullif(current_setting('odca.actor_id',true),'')::uuid)
 WITH CHECK (tenant_id=nullif(current_setting('odca.tenant_id',true),'')::uuid AND owner_id=nullif(current_setting('odca.actor_id',true),'')::uuid);
GRANT SELECT,INSERT,UPDATE ON odca.saved_work_views TO odca_app;
INSERT INTO odca.schema_migrations(version,name,checksum)
VALUES(14,'Personal saved work views','2cc024fc41b0cb12adbaac3db2fa480a33989f3a4b061c01203d5838fcf930d1') ON CONFLICT(version) DO NOTHING;
COMMIT;
-- ODCA-END 014

-- Development identities are intentionally excluded from the consolidated installer.
-- Their explicit parameterized seed is database/development/seed-test-access.sql.

-- ODCA-MIGRATION 015 CHECKSUM 845441501da6d68347804efb0507e15c0a20cbeeba20c46f5b442cd23e093f62
BEGIN;
SELECT pg_advisory_xact_lock(hashtext('odca.schema.migrations'));

INSERT INTO odca.permissions(code,description,delegable) VALUES
 ('tenant.templates.read','Consultar modelos contratuais',true),
 ('tenant.templates.manage','Gerenciar modelos particulares',true),
 ('tenant.contract_drafts.read','Consultar minutas contratuais',true),
 ('tenant.contract_drafts.manage','Editar e versionar minutas contratuais',true),
 ('tenant.contract_copies.issue','Emitir cópias rastreáveis',true)
ON CONFLICT(code) DO UPDATE SET description=EXCLUDED.description,delegable=EXCLUDED.delegable;
INSERT INTO odca.role_permissions(role_id,permission_code)
SELECT r.id,p.code FROM odca.roles r CROSS JOIN odca.permissions p
WHERE r.scope_type='tenant' AND r.code='tenant-administrator' AND p.code IN
 ('tenant.templates.read','tenant.templates.manage','tenant.contract_drafts.read','tenant.contract_drafts.manage','tenant.contract_copies.issue')
ON CONFLICT DO NOTHING;

CREATE TABLE odca.contract_templates(
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(), owner_tenant_id uuid REFERENCES odca.tenants(id),
 name varchar(160) NOT NULL, description varchar(1000), contract_type varchar(80) NOT NULL,
 scope text NOT NULL CHECK(scope IN('private','consultancy','global')), status text NOT NULL DEFAULT 'draft' CHECK(status IN('draft','published','archived')),
 current_version integer NOT NULL DEFAULT 1 CHECK(current_version>0), row_version bigint NOT NULL DEFAULT 1 CHECK(row_version>0),
 author_id uuid NOT NULL REFERENCES odca.users(id), created_at timestamptz NOT NULL DEFAULT now(), published_at timestamptz, archived_at timestamptz,
 CHECK((scope='global' AND owner_tenant_id IS NULL) OR (scope<>'global' AND owner_tenant_id IS NOT NULL)),
 CHECK((status='published' AND published_at IS NOT NULL) OR status<>'published')
);
CREATE TABLE odca.contract_template_versions(
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(), template_id uuid NOT NULL REFERENCES odca.contract_templates(id), version_number integer NOT NULL CHECK(version_number>0),
 content_schema_version integer NOT NULL DEFAULT 1 CHECK(content_schema_version=1), content jsonb NOT NULL CHECK(jsonb_typeof(content)='object'),
 fields jsonb NOT NULL CHECK(jsonb_typeof(fields)='array'), created_by uuid NOT NULL REFERENCES odca.users(id), created_at timestamptz NOT NULL DEFAULT now(),
 published_at timestamptz, UNIQUE(template_id,version_number), UNIQUE(template_id,id)
);
CREATE TABLE odca.contract_template_access(
 template_id uuid NOT NULL REFERENCES odca.contract_templates(id), tenant_id uuid NOT NULL REFERENCES odca.tenants(id), granted_by uuid NOT NULL REFERENCES odca.users(id),
 granted_at timestamptz NOT NULL DEFAULT now(), revoked_at timestamptz, revoked_by uuid REFERENCES odca.users(id), PRIMARY KEY(template_id,tenant_id)
);
CREATE TABLE odca.contract_drafts(
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(), tenant_id uuid NOT NULL, contract_id uuid NOT NULL,
 source_template_id uuid NOT NULL, source_template_version_id uuid NOT NULL, content_schema_version integer NOT NULL DEFAULT 1 CHECK(content_schema_version=1),
 content jsonb NOT NULL CHECK(jsonb_typeof(content)='object'), fields jsonb NOT NULL CHECK(jsonb_typeof(fields)='array'), values jsonb NOT NULL DEFAULT '[]'::jsonb CHECK(jsonb_typeof(values)='array'),
 row_version bigint NOT NULL DEFAULT 1 CHECK(row_version>0), last_client_revision uuid, created_by uuid NOT NULL, updated_by uuid NOT NULL,
 created_at timestamptz NOT NULL DEFAULT now(), updated_at timestamptz NOT NULL DEFAULT now(),
 UNIQUE(tenant_id,id), UNIQUE(tenant_id,contract_id), FOREIGN KEY(tenant_id,contract_id) REFERENCES odca.contracts(tenant_id,id),
 FOREIGN KEY(source_template_id,source_template_version_id) REFERENCES odca.contract_template_versions(template_id,id),
 FOREIGN KEY(tenant_id,created_by) REFERENCES odca.memberships(tenant_id,user_id), FOREIGN KEY(tenant_id,updated_by) REFERENCES odca.memberships(tenant_id,user_id)
);
CREATE TABLE odca.generated_contract_versions(
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(), tenant_id uuid NOT NULL, contract_id uuid NOT NULL, draft_id uuid NOT NULL,
 version_number integer NOT NULL CHECK(version_number>0), content_schema_version integer NOT NULL, content jsonb NOT NULL, fields jsonb NOT NULL, values jsonb NOT NULL,
 source_template_id uuid NOT NULL, source_template_version_id uuid NOT NULL, canonical_sha256 char(64) NOT NULL CHECK(canonical_sha256 ~ '^[a-f0-9]{64}$'),
 storage_key text NOT NULL UNIQUE, byte_size bigint NOT NULL CHECK(byte_size>0), created_by uuid NOT NULL, created_at timestamptz NOT NULL DEFAULT now(),
 review_status text NOT NULL DEFAULT 'generated' CHECK(review_status IN('generated','submitted','internally_approved','externally_signed')),
 UNIQUE(tenant_id,id), UNIQUE(draft_id,version_number), FOREIGN KEY(tenant_id,contract_id) REFERENCES odca.contracts(tenant_id,id),
 FOREIGN KEY(tenant_id,draft_id) REFERENCES odca.contract_drafts(tenant_id,id), FOREIGN KEY(source_template_id,source_template_version_id) REFERENCES odca.contract_template_versions(template_id,id),
 FOREIGN KEY(tenant_id,created_by) REFERENCES odca.memberships(tenant_id,user_id)
);
ALTER TABLE odca.contract_review_requests ALTER COLUMN document_version_id DROP NOT NULL;
ALTER TABLE odca.contract_review_requests ADD COLUMN generated_version_id uuid;
ALTER TABLE odca.contract_review_requests ADD CONSTRAINT contract_review_generated_version_fk FOREIGN KEY(tenant_id,generated_version_id) REFERENCES odca.generated_contract_versions(tenant_id,id);
ALTER TABLE odca.contract_review_requests ADD CONSTRAINT contract_review_exact_version_ck CHECK((document_version_id IS NULL) <> (generated_version_id IS NULL));

CREATE TABLE odca.contract_copy_issuances(
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(), serial char(26) NOT NULL UNIQUE, tenant_id uuid NOT NULL, generated_version_id uuid NOT NULL,
 requested_by uuid NOT NULL, requested_at timestamptz NOT NULL DEFAULT now(), status text NOT NULL DEFAULT 'queued' CHECK(status IN('queued','processing','completed','failed')),
 idempotency_key uuid NOT NULL, storage_key text UNIQUE, sha256 char(64), byte_size bigint, completed_at timestamptz, failure_code varchar(120),
 UNIQUE(tenant_id,id), UNIQUE(tenant_id,idempotency_key), FOREIGN KEY(tenant_id,generated_version_id) REFERENCES odca.generated_contract_versions(tenant_id,id),
 FOREIGN KEY(tenant_id,requested_by) REFERENCES odca.memberships(tenant_id,user_id),
 CHECK((status='completed' AND storage_key IS NOT NULL AND sha256 IS NOT NULL AND byte_size>0 AND completed_at IS NOT NULL) OR status<>'completed')
);
CREATE INDEX contract_templates_catalog_ix ON odca.contract_templates(status,scope,contract_type,name,id);
CREATE INDEX contract_template_access_tenant_ix ON odca.contract_template_access(tenant_id,template_id) WHERE revoked_at IS NULL;
CREATE INDEX generated_contract_versions_contract_ix ON odca.generated_contract_versions(tenant_id,contract_id,version_number DESC);
CREATE INDEX contract_copy_issuances_lookup_ix ON odca.contract_copy_issuances(tenant_id,serial);

ALTER TABLE odca.contract_drafts ENABLE ROW LEVEL SECURITY; ALTER TABLE odca.contract_drafts FORCE ROW LEVEL SECURITY;
ALTER TABLE odca.generated_contract_versions ENABLE ROW LEVEL SECURITY; ALTER TABLE odca.generated_contract_versions FORCE ROW LEVEL SECURITY;
ALTER TABLE odca.contract_copy_issuances ENABLE ROW LEVEL SECURITY; ALTER TABLE odca.contract_copy_issuances FORCE ROW LEVEL SECURITY;
DO $policy$ DECLARE n text; BEGIN FOREACH n IN ARRAY ARRAY['contract_drafts','generated_contract_versions','contract_copy_issuances'] LOOP
 EXECUTE format('CREATE POLICY tenant_isolation ON odca.%I TO odca_app USING (tenant_id = nullif(current_setting(''odca.tenant_id'',true),'''')::uuid) WITH CHECK (tenant_id = nullif(current_setting(''odca.tenant_id'',true),'''')::uuid)',n);
END LOOP; END $policy$;
GRANT SELECT,INSERT,UPDATE ON odca.contract_templates,odca.contract_template_versions,odca.contract_template_access,odca.contract_drafts,odca.generated_contract_versions,odca.contract_copy_issuances TO odca_app;

INSERT INTO odca.schema_migrations(version,name,checksum)
VALUES(15,'Contract template library and drafting studio','845441501da6d68347804efb0507e15c0a20cbeeba20c46f5b442cd23e093f62') ON CONFLICT(version) DO NOTHING;
COMMIT;
-- ODCA-END 015
-- ODCA-MIGRATION 016 CHECKSUM e0ee0334cc899fc23bcc2d9f127b152fb54e5cd746f408512805483fb12757a7
BEGIN;
SELECT pg_advisory_xact_lock(hashtext('odca.schema.migrations'));

ALTER TABLE odca.generated_contract_versions
 ADD COLUMN idempotency_key uuid,
 ADD COLUMN draft_row_version bigint;
UPDATE odca.generated_contract_versions SET idempotency_key=id,draft_row_version=1 WHERE idempotency_key IS NULL;
ALTER TABLE odca.generated_contract_versions ALTER COLUMN idempotency_key SET NOT NULL;
ALTER TABLE odca.generated_contract_versions ALTER COLUMN draft_row_version SET NOT NULL;
ALTER TABLE odca.generated_contract_versions ADD CONSTRAINT generated_contract_versions_idempotency_uq UNIQUE(tenant_id,draft_id,idempotency_key);
ALTER TABLE odca.generated_contract_versions ADD CONSTRAINT generated_contract_versions_draft_row_version_ck CHECK(draft_row_version>0);

CREATE TABLE odca.draft_save_receipts(
 tenant_id uuid NOT NULL,draft_id uuid NOT NULL,client_revision uuid NOT NULL,saved_version bigint NOT NULL CHECK(saved_version>0),saved_at timestamptz NOT NULL,
 PRIMARY KEY(tenant_id,draft_id,client_revision),FOREIGN KEY(tenant_id,draft_id) REFERENCES odca.contract_drafts(tenant_id,id));
CREATE INDEX draft_save_receipts_cleanup_ix ON odca.draft_save_receipts(saved_at);

CREATE TABLE odca.studio_comments(
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(),tenant_id uuid NOT NULL,contract_id uuid NOT NULL,draft_id uuid NOT NULL,generated_version_id uuid,
 draft_revision bigint NOT NULL CHECK(draft_revision>0),author_id uuid NOT NULL,parent_id uuid,reference varchar(200) NOT NULL,body varchar(4000) NOT NULL CHECK(length(btrim(body))>0),
 reference_located boolean NOT NULL DEFAULT true,created_at timestamptz NOT NULL DEFAULT now(),edited_at timestamptz,edited_by uuid,resolved_at timestamptz,resolved_by uuid,deleted_at timestamptz,deleted_by uuid,
 UNIQUE(tenant_id,id),FOREIGN KEY(tenant_id,contract_id) REFERENCES odca.contracts(tenant_id,id),FOREIGN KEY(tenant_id,draft_id) REFERENCES odca.contract_drafts(tenant_id,id),
 FOREIGN KEY(tenant_id,generated_version_id) REFERENCES odca.generated_contract_versions(tenant_id,id),FOREIGN KEY(tenant_id,author_id) REFERENCES odca.memberships(tenant_id,user_id),
 FOREIGN KEY(tenant_id,resolved_by) REFERENCES odca.memberships(tenant_id,user_id),FOREIGN KEY(tenant_id,edited_by) REFERENCES odca.memberships(tenant_id,user_id),FOREIGN KEY(tenant_id,deleted_by) REFERENCES odca.memberships(tenant_id,user_id),
 FOREIGN KEY(tenant_id,parent_id) REFERENCES odca.studio_comments(tenant_id,id),CHECK((resolved_at IS NULL)=(resolved_by IS NULL)),CHECK((deleted_at IS NULL)=(deleted_by IS NULL)));
CREATE TABLE odca.studio_comment_events(
 id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,tenant_id uuid NOT NULL,comment_id uuid NOT NULL,actor_id uuid NOT NULL,event_type varchar(40) NOT NULL CHECK(event_type IN('resolve','reopen','edit','delete')),
 occurred_at timestamptz NOT NULL DEFAULT now(),FOREIGN KEY(tenant_id,comment_id) REFERENCES odca.studio_comments(tenant_id,id),FOREIGN KEY(tenant_id,actor_id) REFERENCES odca.memberships(tenant_id,user_id));
CREATE INDEX studio_comments_context_ix ON odca.studio_comments(tenant_id,draft_id,generated_version_id,created_at) WHERE deleted_at IS NULL;

ALTER TABLE odca.draft_save_receipts ENABLE ROW LEVEL SECURITY; ALTER TABLE odca.draft_save_receipts FORCE ROW LEVEL SECURITY;
ALTER TABLE odca.studio_comments ENABLE ROW LEVEL SECURITY; ALTER TABLE odca.studio_comments FORCE ROW LEVEL SECURITY;
ALTER TABLE odca.studio_comment_events ENABLE ROW LEVEL SECURITY; ALTER TABLE odca.studio_comment_events FORCE ROW LEVEL SECURITY;
DO $policy$ DECLARE n text; BEGIN FOREACH n IN ARRAY ARRAY['draft_save_receipts','studio_comments','studio_comment_events'] LOOP
 EXECUTE format('CREATE POLICY tenant_isolation ON odca.%I TO odca_app USING (tenant_id = nullif(current_setting(''odca.tenant_id'',true),'''')::uuid) WITH CHECK (tenant_id = nullif(current_setting(''odca.tenant_id'',true),'''')::uuid)',n);
END LOOP; END $policy$;
GRANT SELECT,INSERT ON odca.draft_save_receipts TO odca_app;
GRANT SELECT,INSERT,UPDATE ON odca.studio_comments TO odca_app;
GRANT SELECT,INSERT ON odca.studio_comment_events TO odca_app;
GRANT USAGE,SELECT ON SEQUENCE odca.studio_comment_events_id_seq TO odca_app;
INSERT INTO odca.schema_migrations(version,name,checksum)
VALUES(16,'Reliable studio saves comparisons and contextual review','e0ee0334cc899fc23bcc2d9f127b152fb54e5cd746f408512805483fb12757a7') ON CONFLICT(version) DO NOTHING;
COMMIT;
-- ODCA-END 016
-- ODCA-MIGRATION 017 CHECKSUM d5e4db204a03a645d715bc0dbb22738155cea02267699387843e2c925c67285c
BEGIN;
SELECT pg_advisory_xact_lock(hashtext('odca.schema.migrations'));

INSERT INTO odca.permissions(code,description,delegable) VALUES
 ('tenant.renewals.read','Consultar renovações e aditivos',true),
 ('tenant.renewals.prepare','Preparar renovação ou aditivo',true),
 ('tenant.renewals.submit','Encaminhar renovação à revisão',true),
 ('tenant.renewals.formalize','Registrar formalização manual',true),
 ('tenant.renewals.apply','Aplicar alteração formalizada',true),
 ('tenant.renewals.cancel','Cancelar renovação ou aditivo',true)
ON CONFLICT(code) DO UPDATE SET description=EXCLUDED.description,delegable=EXCLUDED.delegable;
INSERT INTO odca.role_permissions(role_id,permission_code)
SELECT r.id,p.code FROM odca.roles r CROSS JOIN odca.permissions p
WHERE r.scope_type='tenant' AND r.code='tenant-administrator' AND p.code LIKE 'tenant.renewals.%'
ON CONFLICT DO NOTHING;

ALTER TABLE odca.contracts
 ADD COLUMN counterparty varchar(200), ADD COLUMN contract_type varchar(80), ADD COLUMN owner_id uuid,
 ADD COLUMN renewal_policy text NOT NULL DEFAULT 'not_defined' CHECK(renewal_policy IN('not_defined','not_provided','decision_required','automatic_clause')),
 ADD COLUMN renewal_notice_amount integer CHECK(renewal_notice_amount BETWEEN 0 AND 1200),
 ADD COLUMN renewal_notice_unit text CHECK(renewal_notice_unit IN('calendar_days','calendar_months')),
 ADD COLUMN renewal_decision_owner_id uuid, ADD COLUMN renewal_policy_notes varchar(2000), ADD COLUMN renewal_policy_reference varchar(300),
 ADD CONSTRAINT contracts_owner_fk FOREIGN KEY(tenant_id,owner_id) REFERENCES odca.memberships(tenant_id,user_id),
 ADD CONSTRAINT contracts_renewal_owner_fk FOREIGN KEY(tenant_id,renewal_decision_owner_id) REFERENCES odca.memberships(tenant_id,user_id),
 ADD CONSTRAINT contracts_notice_complete_ck CHECK((renewal_notice_amount IS NULL)=(renewal_notice_unit IS NULL));

CREATE TABLE odca.contract_change_requests(
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(), tenant_id uuid NOT NULL, contract_id uuid NOT NULL,
 kind text NOT NULL CHECK(kind IN('renewal','amendment')), status text NOT NULL DEFAULT 'draft' CHECK(status IN('draft','in_review','internally_approved','awaiting_formalization','formalized','cancelled','conflict')),
 application_status text NOT NULL DEFAULT 'not_applied' CHECK(application_status IN('not_applied','scheduled','applied','failed')),
 author_id uuid NOT NULL, responsible_id uuid NOT NULL, reason varchar(2000) NOT NULL CHECK(length(btrim(reason))>0),
 current_start_date date, current_end_date date, proposed_start_date date, proposed_end_date date,
 current_value numeric(18,2), proposed_value numeric(18,2), currency char(3), current_scope text, proposed_scope text,
 current_operational_owner_id uuid, proposed_operational_owner_id uuid, other_changes jsonb NOT NULL DEFAULT '[]'::jsonb CHECK(jsonb_typeof(other_changes)='array'),
 effective_on date NOT NULL, source_document_version_id uuid, draft_id uuid, generated_version_id uuid, review_id uuid,
 evidence_version_id uuid, formalized_on date, formalization_justification varchar(2000), formalized_by uuid, formalized_at timestamptz,
 base_contract_version bigint NOT NULL CHECK(base_contract_version>0), row_version bigint NOT NULL DEFAULT 1 CHECK(row_version>0),
 idempotency_key uuid NOT NULL, created_at timestamptz NOT NULL DEFAULT now(), updated_at timestamptz NOT NULL DEFAULT now(), cancelled_at timestamptz,
 UNIQUE(tenant_id,id), UNIQUE(tenant_id,idempotency_key),
 FOREIGN KEY(tenant_id,contract_id) REFERENCES odca.contracts(tenant_id,id), FOREIGN KEY(tenant_id,author_id) REFERENCES odca.memberships(tenant_id,user_id),
 FOREIGN KEY(tenant_id,responsible_id) REFERENCES odca.memberships(tenant_id,user_id), FOREIGN KEY(tenant_id,current_operational_owner_id) REFERENCES odca.memberships(tenant_id,user_id),
 FOREIGN KEY(tenant_id,proposed_operational_owner_id) REFERENCES odca.memberships(tenant_id,user_id), FOREIGN KEY(tenant_id,source_document_version_id) REFERENCES odca.document_versions(tenant_id,id),
 FOREIGN KEY(tenant_id,draft_id) REFERENCES odca.contract_drafts(tenant_id,id), FOREIGN KEY(tenant_id,generated_version_id) REFERENCES odca.generated_contract_versions(tenant_id,id),
 FOREIGN KEY(tenant_id,review_id) REFERENCES odca.contract_review_requests(tenant_id,id), FOREIGN KEY(tenant_id,evidence_version_id) REFERENCES odca.document_versions(tenant_id,id),
 FOREIGN KEY(tenant_id,formalized_by) REFERENCES odca.memberships(tenant_id,user_id),
 CHECK(proposed_end_date IS NULL OR proposed_start_date IS NULL OR proposed_end_date>=proposed_start_date),
 CHECK((formalized_at IS NULL AND formalized_by IS NULL AND formalized_on IS NULL AND evidence_version_id IS NULL) OR
       (formalized_at IS NOT NULL AND formalized_by IS NOT NULL AND formalized_on IS NOT NULL AND evidence_version_id IS NOT NULL AND length(btrim(formalization_justification))>0))
);
CREATE UNIQUE INDEX contract_change_one_active_uq ON odca.contract_change_requests(tenant_id,contract_id) WHERE status NOT IN('cancelled','formalized','conflict') OR (status='formalized' AND application_status IN('not_applied','scheduled','failed'));
CREATE INDEX contract_change_central_ix ON odca.contract_change_requests(tenant_id,status,application_status,effective_on,id);
CREATE INDEX contracts_renewal_central_ix ON odca.contracts(tenant_id,end_date,owner_id) WHERE end_date IS NOT NULL;

CREATE TABLE odca.contract_change_events(
 id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY, tenant_id uuid NOT NULL, request_id uuid NOT NULL, actor_id uuid NOT NULL,
 event_type varchar(80) NOT NULL, occurred_at timestamptz NOT NULL DEFAULT now(), details jsonb NOT NULL DEFAULT '{}'::jsonb,
 FOREIGN KEY(tenant_id,request_id) REFERENCES odca.contract_change_requests(tenant_id,id), FOREIGN KEY(tenant_id,actor_id) REFERENCES odca.memberships(tenant_id,user_id));
CREATE TABLE odca.contract_change_applications(
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(), tenant_id uuid NOT NULL, request_id uuid NOT NULL, contract_id uuid NOT NULL,
 applied_by uuid, applied_at timestamptz NOT NULL DEFAULT now(), before_data jsonb NOT NULL, after_data jsonb NOT NULL,
 UNIQUE(tenant_id,request_id), FOREIGN KEY(tenant_id,request_id) REFERENCES odca.contract_change_requests(tenant_id,id),
 FOREIGN KEY(tenant_id,contract_id) REFERENCES odca.contracts(tenant_id,id));

ALTER TABLE odca.contract_change_requests ENABLE ROW LEVEL SECURITY; ALTER TABLE odca.contract_change_requests FORCE ROW LEVEL SECURITY;
ALTER TABLE odca.contract_change_events ENABLE ROW LEVEL SECURITY; ALTER TABLE odca.contract_change_events FORCE ROW LEVEL SECURITY;
ALTER TABLE odca.contract_change_applications ENABLE ROW LEVEL SECURITY; ALTER TABLE odca.contract_change_applications FORCE ROW LEVEL SECURITY;
DO $policy$ DECLARE n text; BEGIN FOREACH n IN ARRAY ARRAY['contract_change_requests','contract_change_events','contract_change_applications'] LOOP
 EXECUTE format('CREATE POLICY tenant_isolation ON odca.%I TO odca_app USING (tenant_id=nullif(current_setting(''odca.tenant_id'',true),'''')::uuid) WITH CHECK (tenant_id=nullif(current_setting(''odca.tenant_id'',true),'''')::uuid)',n);
END LOOP; END $policy$;
GRANT SELECT,INSERT,UPDATE ON odca.contract_change_requests,odca.contract_change_events,odca.contract_change_applications TO odca_app;
GRANT USAGE,SELECT ON SEQUENCE odca.contract_change_events_id_seq TO odca_app;

CREATE OR REPLACE FUNCTION odca.protect_formalized_evidence() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 IF OLD.status='formalized' AND (NEW.evidence_version_id IS DISTINCT FROM OLD.evidence_version_id OR NEW.generated_version_id IS DISTINCT FROM OLD.generated_version_id) THEN
  RAISE EXCEPTION 'formalized_document_is_immutable';
 END IF; RETURN NEW;
END $$;
CREATE TRIGGER protect_formalized_evidence BEFORE UPDATE ON odca.contract_change_requests FOR EACH ROW EXECUTE FUNCTION odca.protect_formalized_evidence();

CREATE OR REPLACE FUNCTION odca.claim_due_contract_change()
RETURNS TABLE("Id" uuid,"TenantId" uuid,"RowVersion" bigint,"Today" date)
LANGUAGE sql SECURITY DEFINER SET search_path=pg_catalog,odca AS $$
 SELECT r.id,r.tenant_id,r.row_version,(now() AT TIME ZONE t.timezone)::date
 FROM odca.contract_change_requests r JOIN odca.tenants t ON t.id=r.tenant_id
 WHERE r.status='formalized' AND r.application_status='scheduled' AND r.effective_on<=(now() AT TIME ZONE t.timezone)::date
 ORDER BY r.effective_on,r.id LIMIT 1;
$$;
REVOKE ALL ON FUNCTION odca.claim_due_contract_change() FROM PUBLIC;
GRANT EXECUTE ON FUNCTION odca.claim_due_contract_change() TO odca_app;

INSERT INTO odca.schema_migrations(version,name,checksum)
VALUES(17,'Renewal and amendment center','d5e4db204a03a645d715bc0dbb22738155cea02267699387843e2c925c67285c') ON CONFLICT(version) DO NOTHING;
COMMIT;
-- ODCA-END 017

-- ODCA-MIGRATION 018 CHECKSUM fc18f7b2eb1e4948a57e045ea54f692a04ee565341ba47465a41af974cdfe9db
BEGIN;
SELECT pg_advisory_xact_lock(hashtext('odca.schema.migrations'));

INSERT INTO odca.permissions(code,description,delegable) VALUES
 ('tenant.billing.read','Consultar plano, limites, consumo e solicitações.',true),
 ('tenant.billing.manage','Solicitar adicionais e mudanças comerciais.',true)
ON CONFLICT(code) DO NOTHING;

-- Existing tenant administrators receive the billing capabilities through their
-- existing role; this does not create a parallel authorization model.
INSERT INTO odca.role_permissions(role_id,permission_code)
SELECT r.id,p.code FROM odca.roles r CROSS JOIN odca.permissions p
WHERE r.scope_type='tenant' AND r.code='tenant-administrator' AND p.code IN('tenant.billing.read','tenant.billing.manage')
ON CONFLICT DO NOTHING;

ALTER TABLE odca.subscriptions ADD COLUMN period_start timestamptz;
ALTER TABLE odca.subscriptions ADD COLUMN period_end timestamptz;
ALTER TABLE odca.subscriptions ADD CONSTRAINT subscriptions_period_ck CHECK(period_end IS NULL OR period_start IS NULL OR period_end>period_start);

CREATE TABLE odca.storage_package_versions(
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(), code varchar(80) NOT NULL, version integer NOT NULL CHECK(version>0),
 name varchar(120) NOT NULL, quantity_bytes bigint NOT NULL CHECK(quantity_bytes>0), unit_price numeric(18,2), currency char(3),
 terms varchar(2000) NOT NULL, status text NOT NULL DEFAULT 'draft' CHECK(status IN('draft','published','retired')),
 effective_from timestamptz NOT NULL, effective_until timestamptz, created_by uuid REFERENCES odca.users(id), created_at timestamptz NOT NULL DEFAULT now(),
 UNIQUE(code,version), CHECK((unit_price IS NULL AND currency IS NULL) OR (unit_price>=0 AND currency IS NOT NULL)),
 CHECK(effective_until IS NULL OR effective_until>effective_from));

-- Proposals are deliberately drafts: commercial staff must configure and publish
-- them; installations never invent prices or expose a fictitious checkout.
INSERT INTO odca.storage_package_versions(id,code,version,name,quantity_bytes,terms,status,effective_from)
VALUES
 ('38000000-0000-0000-0000-000000000001','storage-small',1,'Armazenamento adicional P',10737418240,'Concessão comercial manual; cobrança e vigência devem ser confirmadas pela equipe ODCA.','draft',now()),
 ('38000000-0000-0000-0000-000000000002','storage-medium',1,'Armazenamento adicional M',53687091200,'Concessão comercial manual; cobrança e vigência devem ser confirmadas pela equipe ODCA.','draft',now())
ON CONFLICT(code,version) DO NOTHING;

CREATE TABLE odca.additional_storage_requests(
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(), tenant_id uuid NOT NULL, requested_by uuid NOT NULL, package_version_id uuid NOT NULL REFERENCES odca.storage_package_versions(id),
 package_code varchar(80) NOT NULL, package_version integer NOT NULL, package_name varchar(120) NOT NULL, quantity integer NOT NULL CHECK(quantity BETWEEN 1 AND 100),
 unit text NOT NULL CHECK(unit='bytes'), bytes_per_unit bigint NOT NULL CHECK(bytes_per_unit>0), unit_price numeric(18,2), currency char(3), terms_snapshot varchar(2000) NOT NULL,
 status text NOT NULL DEFAULT 'pending' CHECK(status IN('pending','approved','rejected','cancelled')), idempotency_key uuid NOT NULL,
 requested_at timestamptz NOT NULL DEFAULT now(), decided_by uuid, decided_at timestamptz, decision_reason varchar(2000),
 UNIQUE(tenant_id,id), UNIQUE(tenant_id,idempotency_key), FOREIGN KEY(tenant_id,requested_by) REFERENCES odca.memberships(tenant_id,user_id),
 FOREIGN KEY(decided_by) REFERENCES odca.users(id), CHECK((status='pending' AND decided_by IS NULL AND decided_at IS NULL) OR (status<>'pending' AND decided_by IS NOT NULL AND decided_at IS NOT NULL)),
 CHECK(status<>'rejected' OR length(btrim(decision_reason))>0));
CREATE INDEX additional_storage_pending_ix ON odca.additional_storage_requests(status,requested_at) WHERE status='pending';

CREATE TABLE odca.storage_capacity_grants(
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(), tenant_id uuid NOT NULL, request_id uuid, quantity_bytes bigint NOT NULL CHECK(quantity_bytes>0),
 granted_by uuid NOT NULL REFERENCES odca.users(id), reason varchar(2000) NOT NULL CHECK(length(btrim(reason))>0), idempotency_key uuid NOT NULL,
 granted_at timestamptz NOT NULL DEFAULT now(), valid_until timestamptz, revoked_at timestamptz, revoked_by uuid REFERENCES odca.users(id), revocation_reason varchar(2000),
 UNIQUE(tenant_id,id), UNIQUE(tenant_id,idempotency_key), UNIQUE(tenant_id,request_id), FOREIGN KEY(tenant_id) REFERENCES odca.tenants(id),
 FOREIGN KEY(tenant_id,request_id) REFERENCES odca.additional_storage_requests(tenant_id,id), CHECK(valid_until IS NULL OR valid_until>granted_at));

CREATE TABLE odca.resource_movements(
 id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY, tenant_id uuid NOT NULL REFERENCES odca.tenants(id), resource_type text NOT NULL CHECK(resource_type IN('storage_capacity','storage_usage','signature_credit','ocr_credit')),
 movement_type text NOT NULL CHECK(movement_type IN('grant','reserve','consume','release','expire','reversal','adjustment')), quantity bigint NOT NULL CHECK(quantity<>0),
 unit text NOT NULL CHECK(unit IN('bytes','envelopes','pages')), source_type varchar(80) NOT NULL, source_id uuid, idempotency_key text NOT NULL,
 actor_user_id uuid REFERENCES odca.users(id), actor_process varchar(120), occurred_at timestamptz NOT NULL DEFAULT now(), reason varchar(2000),
 UNIQUE(tenant_id,idempotency_key), CHECK((actor_user_id IS NULL)<>(actor_process IS NULL)));
CREATE INDEX resource_movements_tenant_ix ON odca.resource_movements(tenant_id,occurred_at DESC,id DESC);

CREATE TABLE odca.storage_reservations(
 id uuid PRIMARY KEY, tenant_id uuid NOT NULL REFERENCES odca.tenants(id), operation_key text NOT NULL, requested_bytes bigint NOT NULL CHECK(requested_bytes>0),
 status text NOT NULL CHECK(status IN('reserved','confirmed','released','expired')), created_by uuid REFERENCES odca.users(id), created_at timestamptz NOT NULL DEFAULT now(),
 expires_at timestamptz NOT NULL, finalized_at timestamptz, UNIQUE(tenant_id,operation_key), UNIQUE(tenant_id,id));
CREATE INDEX storage_reservations_expiry_ix ON odca.storage_reservations(expires_at) WHERE status='reserved';

ALTER TABLE odca.additional_storage_requests ENABLE ROW LEVEL SECURITY; ALTER TABLE odca.additional_storage_requests FORCE ROW LEVEL SECURITY;
ALTER TABLE odca.storage_capacity_grants ENABLE ROW LEVEL SECURITY; ALTER TABLE odca.storage_capacity_grants FORCE ROW LEVEL SECURITY;
ALTER TABLE odca.resource_movements ENABLE ROW LEVEL SECURITY; ALTER TABLE odca.resource_movements FORCE ROW LEVEL SECURITY;
ALTER TABLE odca.storage_reservations ENABLE ROW LEVEL SECURITY; ALTER TABLE odca.storage_reservations FORCE ROW LEVEL SECURITY;
DO $policy$ DECLARE n text; BEGIN FOREACH n IN ARRAY ARRAY['additional_storage_requests','storage_capacity_grants','resource_movements','storage_reservations'] LOOP
 EXECUTE format('CREATE POLICY tenant_isolation ON odca.%I TO odca_app USING (tenant_id=nullif(current_setting(''odca.tenant_id'',true),'''')::uuid) WITH CHECK (tenant_id=nullif(current_setting(''odca.tenant_id'',true),'''')::uuid)',n);
END LOOP; END $policy$;

CREATE OR REPLACE FUNCTION odca.assert_platform_actor(actor uuid) RETURNS void LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,odca AS $$
BEGIN IF actor IS DISTINCT FROM nullif(current_setting('odca.user_id',true),'')::uuid OR NOT EXISTS(SELECT 1 FROM odca.users WHERE id=actor AND is_platform_administrator AND NOT is_deleted) THEN RAISE EXCEPTION 'platform administrator required' USING ERRCODE='42501'; END IF; END $$;

CREATE OR REPLACE FUNCTION odca.grant_storage_capacity(requested_tenant uuid,actor uuid,bytes bigint,justification text,request_key uuid,valid_until timestamptz DEFAULT NULL) RETURNS uuid
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,odca AS $$ DECLARE grant_id uuid; BEGIN
 PERFORM odca.assert_platform_actor(actor); IF bytes<=0 OR length(btrim(justification))=0 THEN RAISE EXCEPTION 'invalid grant'; END IF;
 SELECT id INTO grant_id FROM odca.storage_capacity_grants WHERE tenant_id=requested_tenant AND idempotency_key=request_key;
 IF grant_id IS NOT NULL THEN RETURN grant_id; END IF;
 INSERT INTO odca.storage_capacity_grants(tenant_id,quantity_bytes,granted_by,reason,idempotency_key,valid_until) VALUES(requested_tenant,bytes,actor,btrim(justification),request_key,valid_until) RETURNING id INTO grant_id;
 INSERT INTO odca.resource_movements(tenant_id,resource_type,movement_type,quantity,unit,source_type,source_id,idempotency_key,actor_user_id,reason) VALUES(requested_tenant,'storage_capacity','grant',bytes,'bytes','manual_grant',grant_id,'grant:'||grant_id,actor,btrim(justification));
 INSERT INTO odca.audit_events(scope_type,tenant_id,actor_user_id,action,entity_type,entity_id,result,metadata) VALUES('tenant',requested_tenant,actor,'billing.storage.granted','storage_capacity_grant',grant_id,'success',jsonb_build_object('bytes',bytes)); RETURN grant_id;
END $$;

CREATE OR REPLACE FUNCTION odca.decide_storage_request(requested_tenant uuid,request_id uuid,actor uuid,decision text,justification text) RETURNS boolean
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,odca AS $$ DECLARE item odca.additional_storage_requests%ROWTYPE; BEGIN
 PERFORM odca.assert_platform_actor(actor); IF decision NOT IN('approved','rejected') OR (decision='rejected' AND length(btrim(coalesce(justification,'')))=0) THEN RAISE EXCEPTION 'invalid decision'; END IF;
 SELECT * INTO item FROM odca.additional_storage_requests WHERE tenant_id=requested_tenant AND id=request_id FOR UPDATE; IF NOT FOUND THEN RETURN false; END IF;
 IF item.status=decision THEN RETURN true; ELSIF item.status<>'pending' THEN RAISE EXCEPTION 'request already decided' USING ERRCODE='40001'; END IF;
 UPDATE odca.additional_storage_requests SET status=decision,decided_by=actor,decided_at=now(),decision_reason=nullif(btrim(justification),'') WHERE tenant_id=requested_tenant AND id=request_id;
 IF decision='approved' THEN
  INSERT INTO odca.storage_capacity_grants(tenant_id,request_id,quantity_bytes,granted_by,reason,idempotency_key) VALUES(requested_tenant,request_id,item.bytes_per_unit*item.quantity,actor,'Solicitação comercial aprovada',request_id) ON CONFLICT(tenant_id,request_id) DO NOTHING;
  INSERT INTO odca.resource_movements(tenant_id,resource_type,movement_type,quantity,unit,source_type,source_id,idempotency_key,actor_user_id,reason) VALUES(requested_tenant,'storage_capacity','grant',item.bytes_per_unit*item.quantity,'bytes','additional_storage_request',request_id,'request-grant:'||request_id,actor,'Solicitação comercial aprovada') ON CONFLICT(tenant_id,idempotency_key) DO NOTHING;
 END IF;
 INSERT INTO odca.audit_events(scope_type,tenant_id,actor_user_id,action,entity_type,entity_id,result,metadata) VALUES('tenant',requested_tenant,actor,'billing.storage.'||decision,'additional_storage_request',request_id,'success',jsonb_build_object('reason',justification)); RETURN true;
END $$;

CREATE OR REPLACE FUNCTION odca.platform_consumption_customers(actor uuid,search text DEFAULT NULL)
RETURNS TABLE("TenantId" uuid,"Name" text,"MaskedDocument" text,"PlanName" text,"TenantStatus" text,"SubscriptionStatus" text,"ActiveUsers" integer,"UsedBytes" bigint,"LimitBytes" bigint,"PendingRequests" integer,"LastActivity" timestamptz)
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,odca AS $$ BEGIN PERFORM odca.assert_platform_actor(actor); RETURN QUERY
 SELECT t.id,t.display_name,CASE WHEN length(coalesce(t.business_code,''))>4 THEN repeat('*',length(t.business_code)-4)||right(t.business_code,4) ELSE '****' END,p.display_name,t.status,s.status,
 (SELECT count(*)::int FROM odca.memberships m WHERE m.tenant_id=t.id AND m.status='active'),coalesce(u.used_bytes,0),
 (coalesce(max(e.limit_value) FILTER(WHERE e.entitlement_code='storage_bytes'),0)+coalesce((SELECT sum(g.quantity_bytes) FROM odca.storage_capacity_grants g WHERE g.tenant_id=t.id AND g.revoked_at IS NULL AND(g.valid_until IS NULL OR g.valid_until>now())),0))::bigint,
 (SELECT count(*)::int FROM odca.additional_storage_requests r WHERE r.tenant_id=t.id AND r.status='pending'),greatest(t.updated_at,max(a.occurred_at))
 FROM odca.tenants t JOIN odca.subscriptions s ON s.tenant_id=t.id JOIN odca.plan_versions p ON p.id=s.plan_version_id JOIN odca.plan_entitlements e ON e.plan_version_id=p.id LEFT JOIN odca.tenant_storage_usage u ON u.tenant_id=t.id LEFT JOIN odca.audit_events a ON a.tenant_id=t.id
 WHERE NOT t.is_deleted AND(search IS NULL OR t.display_name ILIKE '%'||search||'%' OR t.business_code ILIKE '%'||search||'%') GROUP BY t.id,p.id,s.id,u.tenant_id ORDER BY t.display_name; END $$;

GRANT SELECT ON odca.storage_package_versions TO odca_app;
GRANT SELECT,INSERT,UPDATE ON odca.additional_storage_requests,odca.storage_capacity_grants,odca.resource_movements,odca.storage_reservations TO odca_app;
GRANT USAGE,SELECT ON SEQUENCE odca.resource_movements_id_seq TO odca_app;
REVOKE ALL ON FUNCTION odca.assert_platform_actor(uuid),odca.grant_storage_capacity(uuid,uuid,bigint,text,uuid,timestamptz),odca.decide_storage_request(uuid,uuid,uuid,text,text),odca.platform_consumption_customers(uuid,text) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION odca.grant_storage_capacity(uuid,uuid,bigint,text,uuid,timestamptz),odca.decide_storage_request(uuid,uuid,uuid,text,text),odca.platform_consumption_customers(uuid,text) TO odca_app;

INSERT INTO odca.schema_migrations(version,name,checksum) VALUES(18,'Customer plan consumption and audited storage grants','fc18f7b2eb1e4948a57e045ea54f692a04ee565341ba47465a41af974cdfe9db') ON CONFLICT(version) DO NOTHING;
COMMIT;
-- ODCA-END 018

-- ODCA-MIGRATION 019 CHECKSUM ee8db403f4e6f6937263277402ebc85119be0e1d849ac5f87720f8e1f4d3eb9c
BEGIN;
SELECT pg_advisory_xact_lock(hashtext('odca.schema.migrations'));

INSERT INTO odca.permissions(code,description,delegable) VALUES
 ('tenant.imports.read','Consultar importações assistidas',true),
 ('tenant.imports.manage','Criar, cancelar e reprocessar importações',true),
 ('tenant.imports.confirm','Confirmar importações revisadas',true)
ON CONFLICT(code) DO UPDATE SET description=EXCLUDED.description,delegable=EXCLUDED.delegable;
INSERT INTO odca.role_permissions(role_id,permission_code)
SELECT r.id,p.code FROM odca.roles r CROSS JOIN odca.permissions p
WHERE r.scope_type='tenant' AND r.code='tenant-administrator' AND p.code LIKE 'tenant.imports.%'
ON CONFLICT DO NOTHING;

CREATE TABLE odca.contract_imports(
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(), tenant_id uuid NOT NULL, requested_by uuid NOT NULL,
 document_version_id uuid NOT NULL, extraction_job_id uuid, result_contract_id uuid,
 file_sha256 char(64) NOT NULL, processing_version integer NOT NULL DEFAULT 1 CHECK(processing_version>0),
 status text NOT NULL DEFAULT 'received' CHECK(status IN('received','security_review','queued','processing','awaiting_review','confirmed','failed','cancelled')),
 current_step text NOT NULL DEFAULT 'document', attempt_count integer NOT NULL DEFAULT 0 CHECK(attempt_count>=0),
 safe_diagnostic_code varchar(120), review_version bigint NOT NULL DEFAULT 1 CHECK(review_version>0),
 confirmed_by uuid, confirmed_at timestamptz, cancelled_by uuid, cancelled_at timestamptz,
 created_at timestamptz NOT NULL DEFAULT now(), updated_at timestamptz NOT NULL DEFAULT now(),
 UNIQUE(tenant_id,id), UNIQUE(tenant_id,document_version_id),
 FOREIGN KEY(tenant_id,requested_by) REFERENCES odca.memberships(tenant_id,user_id),
 FOREIGN KEY(tenant_id,document_version_id) REFERENCES odca.document_versions(tenant_id,id),
 FOREIGN KEY(tenant_id,extraction_job_id) REFERENCES odca.extraction_jobs(tenant_id,id),
 FOREIGN KEY(tenant_id,result_contract_id) REFERENCES odca.contracts(tenant_id,id),
 FOREIGN KEY(tenant_id,confirmed_by) REFERENCES odca.memberships(tenant_id,user_id),
 FOREIGN KEY(tenant_id,cancelled_by) REFERENCES odca.memberships(tenant_id,user_id),
 CHECK((status='confirmed')=(result_contract_id IS NOT NULL AND confirmed_by IS NOT NULL AND confirmed_at IS NOT NULL)),
 CHECK(status<>'cancelled' OR (cancelled_by IS NOT NULL AND cancelled_at IS NOT NULL)));
CREATE INDEX contract_imports_tenant_status_ix ON odca.contract_imports(tenant_id,status,created_at DESC);
CREATE INDEX contract_imports_requester_ix ON odca.contract_imports(tenant_id,requested_by,created_at DESC);
CREATE INDEX contract_imports_hash_ix ON odca.contract_imports(tenant_id,file_sha256) WHERE status<>'cancelled';

CREATE TABLE odca.contract_import_events(
 id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY, tenant_id uuid NOT NULL, import_id uuid NOT NULL,
 actor_id uuid, event_type varchar(100) NOT NULL, details jsonb NOT NULL DEFAULT '{}', occurred_at timestamptz NOT NULL DEFAULT now(),
 FOREIGN KEY(tenant_id,import_id) REFERENCES odca.contract_imports(tenant_id,id),
 FOREIGN KEY(tenant_id,actor_id) REFERENCES odca.memberships(tenant_id,user_id));
CREATE INDEX contract_import_events_ix ON odca.contract_import_events(tenant_id,import_id,occurred_at DESC,id DESC);

ALTER TABLE odca.contract_imports ENABLE ROW LEVEL SECURITY; ALTER TABLE odca.contract_imports FORCE ROW LEVEL SECURITY;
ALTER TABLE odca.contract_import_events ENABLE ROW LEVEL SECURITY; ALTER TABLE odca.contract_import_events FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON odca.contract_imports TO odca_app USING(tenant_id=nullif(current_setting('odca.tenant_id',true),'')::uuid) WITH CHECK(tenant_id=nullif(current_setting('odca.tenant_id',true),'')::uuid);
CREATE POLICY tenant_isolation ON odca.contract_import_events TO odca_app USING(tenant_id=nullif(current_setting('odca.tenant_id',true),'')::uuid) WITH CHECK(tenant_id=nullif(current_setting('odca.tenant_id',true),'')::uuid);
GRANT SELECT,INSERT,UPDATE ON odca.contract_imports,odca.contract_import_events TO odca_app;
GRANT USAGE,SELECT ON SEQUENCE odca.contract_import_events_id_seq TO odca_app;

INSERT INTO odca.schema_migrations(version,name,checksum) VALUES(19,'Assisted contract import tracking and audit','ee8db403f4e6f6937263277402ebc85119be0e1d849ac5f87720f8e1f4d3eb9c') ON CONFLICT(version) DO NOTHING;
COMMIT;
-- ODCA-END 019
-- ODCA-MIGRATION 020 CHECKSUM f98faef3dc88e163bf04e7afb3a5d64c9f3566a4294d6b7145e8c5d037f5feb8
BEGIN;
SELECT pg_advisory_xact_lock(hashtext('odca.schema.migrations'));

DO $odca$
DECLARE
    expected_checksum constant char(64) := 'f98faef3dc88e163bf04e7afb3a5d64c9f3566a4294d6b7145e8c5d037f5feb8';
    recorded_checksum char(64);
BEGIN
    SELECT checksum INTO recorded_checksum FROM odca.schema_migrations WHERE version = 20;
    IF recorded_checksum IS NOT NULL AND recorded_checksum <> expected_checksum THEN
        RAISE EXCEPTION 'ODCA migration 020 checksum mismatch: stored %, expected %', recorded_checksum, expected_checksum;
    END IF;
END
$odca$;

-- ============================================================
-- SUPPORT SESSIONS
-- ============================================================
CREATE TABLE odca.support_sessions(
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
 superadmin_user_id uuid NOT NULL REFERENCES odca.users(id),
 tenant_id uuid NOT NULL REFERENCES odca.tenants(id),
 reason varchar(1000) NOT NULL CHECK(length(btrim(reason))>0),
 status text NOT NULL DEFAULT 'active' CHECK(status IN('active','ended','revoked')),
 started_at timestamptz NOT NULL DEFAULT now(),
 expires_at timestamptz NOT NULL CHECK(expires_at > started_at),
 ended_at timestamptz,
 ended_by uuid REFERENCES odca.users(id),
 notes varchar(2000),
 CHECK((status='active')=(ended_at IS NULL)),
 CHECK(status='active' OR ended_by IS NOT NULL)
);
CREATE INDEX support_sessions_admin_ix ON odca.support_sessions(superadmin_user_id, started_at DESC);
CREATE INDEX support_sessions_tenant_ix ON odca.support_sessions(tenant_id, started_at DESC);
CREATE INDEX support_sessions_active_ix ON odca.support_sessions(tenant_id) WHERE status='active';

CREATE TABLE odca.support_session_events(
 id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
 session_id uuid NOT NULL REFERENCES odca.support_sessions(id),
 actor_user_id uuid NOT NULL REFERENCES odca.users(id),
 event_type varchar(60) NOT NULL CHECK(event_type IN('started','action_performed','ended','revoked','extended')),
 occurred_at timestamptz NOT NULL DEFAULT now(),
 details jsonb NOT NULL DEFAULT '{}'
);
CREATE INDEX support_session_events_session_ix ON odca.support_session_events(session_id, occurred_at DESC);

-- Support sessions: platform-scope, NOT tenant-isolated via RLS.
-- Only superadmins (is_platform_administrator=true) can INSERT/SELECT via security-definer functions.
-- Revoke default and grant only to functions.
REVOKE ALL ON odca.support_sessions, odca.support_session_events FROM odca_app;
REVOKE ALL ON SEQUENCE odca.support_session_events_id_seq FROM odca_app;

CREATE OR REPLACE FUNCTION odca.open_support_session(
    p_admin_id uuid, p_tenant_id uuid, p_reason varchar, p_duration_minutes integer DEFAULT 60)
RETURNS uuid
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, odca
AS $$
DECLARE
    v_is_admin boolean;
    v_session_id uuid;
BEGIN
    SELECT is_platform_administrator INTO v_is_admin FROM odca.users WHERE id = p_admin_id AND NOT is_deleted;
    IF NOT COALESCE(v_is_admin, false) THEN
        RAISE EXCEPTION 'not a platform administrator' USING ERRCODE = '42501';
    END IF;
    IF p_duration_minutes NOT BETWEEN 1 AND 480 THEN
        RAISE EXCEPTION 'duration must be between 1 and 480 minutes' USING ERRCODE = '22023';
    END IF;
    IF length(btrim(COALESCE(p_reason,''))) < 5 THEN
        RAISE EXCEPTION 'reason must have at least 5 non-blank characters' USING ERRCODE = '22023';
    END IF;
    -- Only one active session per admin per tenant
    IF EXISTS(SELECT 1 FROM odca.support_sessions WHERE superadmin_user_id=p_admin_id AND tenant_id=p_tenant_id AND status='active' AND expires_at>now()) THEN
        RAISE EXCEPTION 'active support session already exists' USING ERRCODE = '23505';
    END IF;

    v_session_id := gen_random_uuid();
    INSERT INTO odca.support_sessions(id, superadmin_user_id, tenant_id, reason, expires_at)
    VALUES(v_session_id, p_admin_id, p_tenant_id, p_reason, now() + (p_duration_minutes || ' minutes')::interval);

    INSERT INTO odca.support_session_events(session_id, actor_user_id, event_type)
    VALUES(v_session_id, p_admin_id, 'started');

    INSERT INTO odca.audit_events(scope_type, actor_user_id, action, entity_type, entity_id, result, metadata)
    VALUES('platform', p_admin_id, 'superadmin.support_session.opened', 'support_session', v_session_id, 'success',
           jsonb_build_object('tenant_id', p_tenant_id, 'duration_minutes', p_duration_minutes, 'reason', p_reason));

    RETURN v_session_id;
END
$$;

CREATE OR REPLACE FUNCTION odca.close_support_session(
    p_actor_id uuid, p_session_id uuid, p_revoke boolean DEFAULT false)
RETURNS void
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, odca
AS $$
DECLARE
    v_session odca.support_sessions;
    v_is_admin boolean;
    v_event text;
BEGIN
    SELECT * INTO v_session FROM odca.support_sessions WHERE id = p_session_id FOR UPDATE;
    IF NOT FOUND OR v_session.status <> 'active' THEN
        RAISE EXCEPTION 'session not found or not active' USING ERRCODE = '02000';
    END IF;
    SELECT is_platform_administrator INTO v_is_admin FROM odca.users WHERE id = p_actor_id AND NOT is_deleted;
    -- Only the owning admin can end; any admin can revoke
    IF p_revoke THEN
        IF NOT COALESCE(v_is_admin, false) THEN
            RAISE EXCEPTION 'not a platform administrator' USING ERRCODE = '42501';
        END IF;
        v_event := 'revoked';
    ELSE
        IF p_actor_id <> v_session.superadmin_user_id AND NOT COALESCE(v_is_admin, false) THEN
            RAISE EXCEPTION 'not authorized to end this session' USING ERRCODE = '42501';
        END IF;
        v_event := 'ended';
    END IF;

    UPDATE odca.support_sessions
    SET status = CASE WHEN p_revoke THEN 'revoked' ELSE 'ended' END,
        ended_at = now(), ended_by = p_actor_id
    WHERE id = p_session_id;

    INSERT INTO odca.support_session_events(session_id, actor_user_id, event_type)
    VALUES(p_session_id, p_actor_id, v_event);

    INSERT INTO odca.audit_events(scope_type, actor_user_id, action, entity_type, entity_id, result)
    VALUES('platform', p_actor_id, 'superadmin.support_session.' || v_event, 'support_session', p_session_id, 'success');
END
$$;

REVOKE ALL ON FUNCTION odca.open_support_session(uuid,uuid,varchar,integer) FROM PUBLIC;
REVOKE ALL ON FUNCTION odca.close_support_session(uuid,uuid,boolean) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION odca.open_support_session(uuid,uuid,varchar,integer) TO odca_app;
GRANT EXECUTE ON FUNCTION odca.close_support_session(uuid,uuid,boolean) TO odca_app;
-- Allow superadmin to read sessions via direct SELECT (bypasses RLS since no RLS on this table)
GRANT SELECT ON odca.support_sessions, odca.support_session_events TO odca_app;
GRANT INSERT ON odca.support_session_events TO odca_app;

-- ============================================================
-- BILLING: INVOICES & PAYMENTS
-- ============================================================
CREATE TABLE odca.invoices(
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
 tenant_id uuid NOT NULL REFERENCES odca.tenants(id),
 subscription_id uuid NOT NULL REFERENCES odca.subscriptions(id),
 reference_code varchar(60) NOT NULL,
 status text NOT NULL DEFAULT 'open' CHECK(status IN('open','paid','overdue','cancelled','written_off')),
 amount numeric(18,2) NOT NULL CHECK(amount >= 0),
 currency char(3) NOT NULL DEFAULT 'BRL',
 period_start date NOT NULL,
 period_end date NOT NULL CHECK(period_end >= period_start),
 due_date date NOT NULL,
 issued_at timestamptz NOT NULL DEFAULT now(),
 paid_at timestamptz,
 cancelled_at timestamptz,
 notes varchar(2000),
 created_by uuid NOT NULL REFERENCES odca.users(id),
 created_at timestamptz NOT NULL DEFAULT now(),
 updated_at timestamptz NOT NULL DEFAULT now(),
 UNIQUE(tenant_id, reference_code),
 UNIQUE(tenant_id, id),
 CHECK((status='paid')=(paid_at IS NOT NULL)),
 CHECK(status <> 'cancelled' OR cancelled_at IS NOT NULL)
);
CREATE INDEX invoices_tenant_status_ix ON odca.invoices(tenant_id, status, due_date);
CREATE INDEX invoices_overdue_ix ON odca.invoices(due_date) WHERE status IN('open','overdue');

CREATE TABLE odca.invoice_payments(
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
 tenant_id uuid NOT NULL,
 invoice_id uuid NOT NULL,
 amount numeric(18,2) NOT NULL CHECK(amount > 0),
 payment_method text NOT NULL CHECK(payment_method IN('bank_transfer','pix','boleto','card','manual_adjustment','other')),
 payment_date date NOT NULL,
 reference_code varchar(120),
 registered_by uuid NOT NULL REFERENCES odca.users(id),
 notes varchar(1000),
 registered_at timestamptz NOT NULL DEFAULT now(),
 FOREIGN KEY(tenant_id, invoice_id) REFERENCES odca.invoices(tenant_id, id)
);
CREATE INDEX invoice_payments_invoice_ix ON odca.invoice_payments(tenant_id, invoice_id);

CREATE TABLE odca.financial_audit_events(
 id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
 tenant_id uuid NOT NULL REFERENCES odca.tenants(id),
 actor_user_id uuid REFERENCES odca.users(id),
 entity_type varchar(60) NOT NULL CHECK(entity_type IN('invoice','invoice_payment','subscription')),
 entity_id uuid NOT NULL,
 event_type varchar(80) NOT NULL,
 occurred_at timestamptz NOT NULL DEFAULT now(),
 details jsonb NOT NULL DEFAULT '{}'
);
CREATE INDEX financial_audit_events_tenant_ix ON odca.financial_audit_events(tenant_id, occurred_at DESC);
CREATE INDEX financial_audit_events_entity_ix ON odca.financial_audit_events(entity_id, occurred_at DESC);

-- Billing tables: tenant-isolated RLS
ALTER TABLE odca.invoices ENABLE ROW LEVEL SECURITY; ALTER TABLE odca.invoices FORCE ROW LEVEL SECURITY;
ALTER TABLE odca.invoice_payments ENABLE ROW LEVEL SECURITY; ALTER TABLE odca.invoice_payments FORCE ROW LEVEL SECURITY;
ALTER TABLE odca.financial_audit_events ENABLE ROW LEVEL SECURITY; ALTER TABLE odca.financial_audit_events FORCE ROW LEVEL SECURITY;

DO $policy$ DECLARE n text; BEGIN FOREACH n IN ARRAY ARRAY['invoices','invoice_payments','financial_audit_events'] LOOP
 EXECUTE format('CREATE POLICY tenant_isolation ON odca.%I TO odca_app USING (tenant_id = nullif(current_setting(''odca.tenant_id'',true),'''')::uuid) WITH CHECK (tenant_id = nullif(current_setting(''odca.tenant_id'',true),'''')::uuid)',n);
END LOOP; END $policy$;

GRANT SELECT,INSERT,UPDATE ON odca.invoices TO odca_app;
GRANT SELECT,INSERT ON odca.invoice_payments TO odca_app;
GRANT SELECT,INSERT ON odca.financial_audit_events TO odca_app;
GRANT USAGE,SELECT ON SEQUENCE odca.financial_audit_events_id_seq TO odca_app;

-- ============================================================
-- LGPD OPERATIONAL: PRIVACY REQUEST ACTIONS & LEGAL HOLDS
-- ============================================================
-- Extend privacy_requests with operational fields via ALTER (non-destructive)
ALTER TABLE odca.privacy_requests
 ADD COLUMN IF NOT EXISTS assigned_to uuid REFERENCES odca.users(id),
 ADD COLUMN IF NOT EXISTS deadline_at timestamptz,
 ADD COLUMN IF NOT EXISTS triage_notes varchar(2000),
 ADD COLUMN IF NOT EXISTS answer_summary varchar(4000),
 ADD COLUMN IF NOT EXISTS closed_at timestamptz;

-- Grant odca_app write access to privacy tables (previously REVOKE ALL – now needs writes for triage)
GRANT SELECT,INSERT,UPDATE ON odca.privacy_requests TO odca_app;
GRANT SELECT,INSERT ON odca.privacy_request_events TO odca_app;

CREATE TABLE odca.privacy_request_actions(
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
 privacy_request_id uuid NOT NULL REFERENCES odca.privacy_requests(id),
 tenant_id uuid REFERENCES odca.tenants(id),
 actor_user_id uuid NOT NULL REFERENCES odca.users(id),
 action_type text NOT NULL CHECK(action_type IN('assign','triage','advance_status','request_info','register_answer','close','reopen')),
 notes varchar(2000),
 from_status text,
 to_status text,
 performed_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX privacy_request_actions_request_ix ON odca.privacy_request_actions(privacy_request_id, performed_at DESC);

CREATE TABLE odca.privacy_legal_holds(
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
 tenant_id uuid NOT NULL REFERENCES odca.tenants(id),
 privacy_request_id uuid REFERENCES odca.privacy_requests(id),
 entity_type varchar(80) NOT NULL,
 entity_id uuid NOT NULL,
 reason varchar(1000) NOT NULL CHECK(length(btrim(reason))>0),
 status text NOT NULL DEFAULT 'active' CHECK(status IN('active','released')),
 placed_by uuid NOT NULL REFERENCES odca.users(id),
 placed_at timestamptz NOT NULL DEFAULT now(),
 expires_at timestamptz,
 released_by uuid REFERENCES odca.users(id),
 released_at timestamptz,
 release_notes varchar(1000),
 UNIQUE(tenant_id, id),
 CHECK((status='released')=(released_at IS NOT NULL AND released_by IS NOT NULL))
);
CREATE INDEX privacy_legal_holds_tenant_status_ix ON odca.privacy_legal_holds(tenant_id, status);
CREATE INDEX privacy_legal_holds_entity_ix ON odca.privacy_legal_holds(entity_id) WHERE status='active';

ALTER TABLE odca.privacy_request_actions ENABLE ROW LEVEL SECURITY; ALTER TABLE odca.privacy_request_actions FORCE ROW LEVEL SECURITY;
ALTER TABLE odca.privacy_legal_holds ENABLE ROW LEVEL SECURITY; ALTER TABLE odca.privacy_legal_holds FORCE ROW LEVEL SECURITY;

-- privacy_request_actions: platform scope (no tenant filter) – accessible when tenant_id matches OR null
CREATE POLICY platform_or_tenant ON odca.privacy_request_actions TO odca_app
 USING(tenant_id IS NULL OR tenant_id = nullif(current_setting('odca.tenant_id',true),'')::uuid)
 WITH CHECK(tenant_id IS NULL OR tenant_id = nullif(current_setting('odca.tenant_id',true),'')::uuid);

DO $policy$ DECLARE n text; BEGIN FOREACH n IN ARRAY ARRAY['privacy_legal_holds'] LOOP
 EXECUTE format('CREATE POLICY tenant_isolation ON odca.%I TO odca_app USING (tenant_id = nullif(current_setting(''odca.tenant_id'',true),'''')::uuid) WITH CHECK (tenant_id = nullif(current_setting(''odca.tenant_id'',true),'''')::uuid)',n);
END LOOP; END $policy$;

GRANT SELECT,INSERT ON odca.privacy_request_actions TO odca_app;
GRANT SELECT,INSERT,UPDATE ON odca.privacy_legal_holds TO odca_app;

-- ============================================================
-- NEW PERMISSIONS
-- ============================================================
INSERT INTO odca.permissions(code,description,delegable) VALUES
 ('superadmin.support_sessions.open','Iniciar sessão de suporte em tenant',false),
 ('superadmin.support_sessions.revoke','Revogar sessão de suporte ativa',false),
 ('superadmin.billing.invoices.manage','Criar e gerenciar faturas manuais',false),
 ('superadmin.billing.payments.register','Registrar pagamentos de faturas',false),
 ('tenant.privacy.requests.triage','Triar e encaminhar solicitações LGPD',true),
 ('tenant.privacy.requests.respond','Registrar resposta a solicitações LGPD',true),
 ('tenant.privacy.legal_holds.manage','Gerenciar bloqueios legais LGPD',true)
ON CONFLICT(code) DO UPDATE SET description=EXCLUDED.description,delegable=EXCLUDED.delegable;

INSERT INTO odca.role_permissions(role_id,permission_code)
SELECT r.id,p.code FROM odca.roles r CROSS JOIN odca.permissions p
WHERE r.scope_type='platform' AND r.code='platform-administrator'
  AND p.code LIKE 'superadmin.%'
ON CONFLICT DO NOTHING;

INSERT INTO odca.role_permissions(role_id,permission_code)
SELECT r.id,p.code FROM odca.roles r CROSS JOIN odca.permissions p
WHERE r.scope_type='tenant' AND r.code='tenant-administrator'
  AND p.code LIKE 'tenant.privacy.%'
ON CONFLICT DO NOTHING;

INSERT INTO odca.schema_migrations(version,name,checksum)
VALUES(20,'Support sessions, billing invoices, and operational LGPD','f98faef3dc88e163bf04e7afb3a5d64c9f3566a4294d6b7145e8c5d037f5feb8') ON CONFLICT(version) DO NOTHING;
COMMIT;
-- ODCA-END 020

-- ODCA-MIGRATION 021 CHECKSUM 240d4cd9383736e1948607454be3d4e443122d62df3d5b7f25605278ce32425a
BEGIN;
SELECT pg_advisory_xact_lock(hashtext('odca.schema.migrations'));

DROP FUNCTION odca.platform_dashboard_snapshot(uuid);
CREATE FUNCTION odca.platform_dashboard_snapshot(requesting_user_id uuid)
RETURNS TABLE(total_tenants integer, active_tenants integer, blocked_tenants integer, inactive_tenants integer,
 active_users integer, contracts integer, contracts_expiring integer, open_obligations integer,
 overdue_obligations integer, upcoming_renewals integer, pending_invoices integer, overdue_invoices integer,
 storage_bytes bigint, pending_privacy_items integer)
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,odca AS $function$
BEGIN
 IF requesting_user_id IS DISTINCT FROM NULLIF(current_setting('odca.user_id',true),'')::uuid
    OR NOT EXISTS(SELECT 1 FROM odca.users WHERE id=requesting_user_id AND is_platform_administrator AND NOT is_deleted)
 THEN RAISE EXCEPTION 'platform administrator required' USING ERRCODE='42501'; END IF;
 RETURN QUERY SELECT
  (SELECT count(*)::integer FROM odca.tenants),
  (SELECT count(*)::integer FROM odca.tenants WHERE status='active' AND NOT is_deleted),
  (SELECT count(*)::integer FROM odca.tenants WHERE status='suspended' AND NOT is_deleted),
  (SELECT count(*)::integer FROM odca.tenants WHERE is_deleted),
  (SELECT count(*)::integer FROM odca.users WHERE NOT is_deleted),
  (SELECT count(*)::integer FROM odca.contracts),
  (SELECT count(*)::integer FROM odca.contracts WHERE end_date BETWEEN current_date AND current_date+30),
  (SELECT count(*)::integer FROM odca.contract_obligations WHERE status IN('open','in_progress') AND deleted_at IS NULL),
  (SELECT count(*)::integer FROM odca.contract_obligations WHERE status IN('open','in_progress') AND due_date<current_date AND deleted_at IS NULL),
  (SELECT count(*)::integer FROM odca.contract_renewal_cycles WHERE decision IN('pending','intent_to_renew','negotiating') AND notice_due_on BETWEEN current_date AND current_date+90),
  (SELECT count(*)::integer FROM odca.invoices WHERE status='open' AND due_date>=current_date),
  (SELECT count(*)::integer FROM odca.invoices WHERE status='overdue' OR (status='open' AND due_date<current_date)),
  (SELECT COALESCE(sum(byte_size),0)::bigint FROM odca.document_versions WHERE security_status<>'rejected'),
  (SELECT count(*)::integer FROM odca.processing_activities WHERE legal_validation_status='pending');
END $function$;
REVOKE ALL ON FUNCTION odca.platform_dashboard_snapshot(uuid) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION odca.platform_dashboard_snapshot(uuid) TO odca_app;

CREATE FUNCTION odca.platform_dashboard_recent_audit(requesting_user_id uuid, item_limit integer DEFAULT 8)
RETURNS TABLE(occurred_at timestamptz, action text, entity_type text, result text, actor_name text, tenant_name text)
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,odca AS $function$
BEGIN
 IF requesting_user_id IS DISTINCT FROM NULLIF(current_setting('odca.user_id',true),'')::uuid
    OR NOT EXISTS(SELECT 1 FROM odca.users WHERE id=requesting_user_id AND is_platform_administrator AND NOT is_deleted)
 THEN RAISE EXCEPTION 'platform administrator required' USING ERRCODE='42501'; END IF;
 RETURN QUERY SELECT a.occurred_at,a.action,a.entity_type,a.result,u.display_name,t.display_name
 FROM odca.audit_events a LEFT JOIN odca.users u ON u.id=a.actor_user_id LEFT JOIN odca.tenants t ON t.id=a.tenant_id
 ORDER BY a.occurred_at DESC,a.id DESC LIMIT LEAST(GREATEST(item_limit,0),25);
END $function$;
REVOKE ALL ON FUNCTION odca.platform_dashboard_recent_audit(uuid,integer) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION odca.platform_dashboard_recent_audit(uuid,integer) TO odca_app;

INSERT INTO odca.schema_migrations(version,name,checksum)
VALUES(21,'Complete platform dashboard','240d4cd9383736e1948607454be3d4e443122d62df3d5b7f25605278ce32425a') ON CONFLICT(version) DO NOTHING;
COMMIT;
-- ODCA-END 021

-- ODCA-MIGRATION 022 CHECKSUM 4b0e7719cfb8192eea9f3242d63a6c58649a392fc27a6fa89db4c18f555f1e12
BEGIN;
SELECT pg_advisory_xact_lock(hashtext('odca.schema.migrations'));

-- Comments now follow either immutable source accepted by review_requests.  Client
-- visibility is explicit and defaults to the historical behaviour.
ALTER TABLE odca.contract_review_comments ALTER COLUMN document_version_id DROP NOT NULL;
ALTER TABLE odca.contract_review_comments ADD COLUMN generated_version_id uuid;
ALTER TABLE odca.contract_review_comments ADD COLUMN visibility text NOT NULL DEFAULT 'client'
  CHECK(visibility IN('client','internal'));
ALTER TABLE odca.contract_review_comments ADD CONSTRAINT contract_review_comment_generated_version_fk
  FOREIGN KEY(tenant_id,generated_version_id) REFERENCES odca.generated_contract_versions(tenant_id,id);
ALTER TABLE odca.contract_review_comments ADD CONSTRAINT contract_review_comment_exact_version_ck
  CHECK((document_version_id IS NULL) <> (generated_version_id IS NULL));
CREATE INDEX contract_review_comments_visibility_ix
  ON odca.contract_review_comments(tenant_id,review_id,visibility,created_at);

INSERT INTO odca.schema_migrations(version,name,checksum)
VALUES(22,'Consultancy review queue and explicit message visibility','4b0e7719cfb8192eea9f3242d63a6c58649a392fc27a6fa89db4c18f555f1e12') ON CONFLICT(version) DO NOTHING;
COMMIT;
-- ODCA-END 022

-- ODCA-MIGRATION 023 CHECKSUM 56d02d48804725d94c683171d7f9288aa4c6ff81b70e5c350eaaeb803aaaeddc
BEGIN;
SELECT pg_advisory_xact_lock(hashtext('odca.schema.migrations'));

INSERT INTO odca.permissions(code,description,delegable) VALUES
 ('tenant.patients.read','Consultar pacientes',true),
 ('tenant.patients.manage','Cadastrar e manter pacientes',true),
 ('tenant.patients.documents.read','Consultar o acervo documental do paciente',true)
ON CONFLICT(code) DO UPDATE SET description=EXCLUDED.description,delegable=EXCLUDED.delegable;
INSERT INTO odca.role_permissions(role_id,permission_code)
SELECT r.id,p.code FROM odca.roles r CROSS JOIN odca.permissions p
WHERE r.scope_type='tenant' AND r.code='tenant-administrator' AND p.code LIKE 'tenant.patients.%'
ON CONFLICT DO NOTHING;

CREATE TABLE odca.patient_representatives(
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(), tenant_id uuid NOT NULL, full_name varchar(160) NOT NULL,
 identifier_type varchar(30), identifier_value varchar(80), relationship varchar(80) NOT NULL,
 created_by uuid NOT NULL, created_at timestamptz NOT NULL DEFAULT now(),
 UNIQUE(tenant_id,id), FOREIGN KEY(tenant_id) REFERENCES odca.tenants(id),
 FOREIGN KEY(tenant_id,created_by) REFERENCES odca.memberships(tenant_id,user_id),
 CHECK(length(btrim(full_name))>=2), CHECK(length(btrim(relationship))>0),
 CHECK((identifier_type IS NULL)=(identifier_value IS NULL))
);
CREATE TABLE odca.patients(
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(), tenant_id uuid NOT NULL, full_name varchar(160) NOT NULL,
 preferred_name varchar(120), birth_date date, email varchar(254), phone varchar(40), address varchar(500),
 identifier_type varchar(30), identifier_value varchar(80), identifier_normalized varchar(80), representative_id uuid,
 row_version bigint NOT NULL DEFAULT 1 CHECK(row_version>0), created_by uuid NOT NULL, updated_by uuid NOT NULL,
 created_at timestamptz NOT NULL DEFAULT now(), updated_at timestamptz NOT NULL DEFAULT now(),
 inactive_at timestamptz, inactivated_by uuid,
 UNIQUE(tenant_id,id), FOREIGN KEY(tenant_id) REFERENCES odca.tenants(id),
 FOREIGN KEY(tenant_id,representative_id) REFERENCES odca.patient_representatives(tenant_id,id),
 FOREIGN KEY(tenant_id,created_by) REFERENCES odca.memberships(tenant_id,user_id),
 FOREIGN KEY(tenant_id,updated_by) REFERENCES odca.memberships(tenant_id,user_id),
 FOREIGN KEY(tenant_id,inactivated_by) REFERENCES odca.memberships(tenant_id,user_id),
 CHECK(length(btrim(full_name))>=2), CHECK(birth_date IS NULL OR birth_date<=current_date),
 CHECK((identifier_type IS NULL AND identifier_value IS NULL AND identifier_normalized IS NULL) OR
       (identifier_type IS NOT NULL AND identifier_value IS NOT NULL AND identifier_normalized IS NOT NULL))
);
CREATE UNIQUE INDEX patients_live_identifier_uq ON odca.patients(tenant_id,identifier_type,identifier_normalized) WHERE inactive_at IS NULL AND identifier_normalized IS NOT NULL;
CREATE INDEX patients_search_ix ON odca.patients(tenant_id,full_name,id);
ALTER TABLE odca.patient_representatives ENABLE ROW LEVEL SECURITY; ALTER TABLE odca.patient_representatives FORCE ROW LEVEL SECURITY;
ALTER TABLE odca.patients ENABLE ROW LEVEL SECURITY; ALTER TABLE odca.patients FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON odca.patient_representatives TO odca_app USING(tenant_id=nullif(current_setting('odca.tenant_id',true),'')::uuid) WITH CHECK(tenant_id=nullif(current_setting('odca.tenant_id',true),'')::uuid);
CREATE POLICY tenant_isolation ON odca.patients TO odca_app USING(tenant_id=nullif(current_setting('odca.tenant_id',true),'')::uuid) WITH CHECK(tenant_id=nullif(current_setting('odca.tenant_id',true),'')::uuid);
GRANT SELECT,INSERT ON odca.patient_representatives TO odca_app;
GRANT SELECT,INSERT,UPDATE ON odca.patients TO odca_app;

ALTER TABLE odca.contract_drafts ADD COLUMN patient_id uuid;
ALTER TABLE odca.contract_drafts ADD CONSTRAINT contract_drafts_patient_fk FOREIGN KEY(tenant_id,patient_id) REFERENCES odca.patients(tenant_id,id);
ALTER TABLE odca.generated_contract_versions ADD COLUMN patient_id uuid;
ALTER TABLE odca.generated_contract_versions ADD COLUMN patient_snapshot jsonb;
ALTER TABLE odca.generated_contract_versions ADD CONSTRAINT generated_versions_patient_fk FOREIGN KEY(tenant_id,patient_id) REFERENCES odca.patients(tenant_id,id);
ALTER TABLE odca.generated_contract_versions ADD CONSTRAINT generated_versions_patient_snapshot_ck CHECK((patient_id IS NULL)=(patient_snapshot IS NULL));
CREATE INDEX generated_versions_patient_ix ON odca.generated_contract_versions(tenant_id,patient_id,created_at DESC) WHERE patient_id IS NOT NULL;

INSERT INTO odca.schema_migrations(version,name,checksum)
VALUES(23,'Patient registry and linked document archive','56d02d48804725d94c683171d7f9288aa4c6ff81b70e5c350eaaeb803aaaeddc') ON CONFLICT(version) DO NOTHING;
COMMIT;
-- ODCA-END 023

-- ODCA-MIGRATION 024 CHECKSUM 9c9a67713215145f150d3e6d4fd7b152eb4346889c9699440555339d936b5691
BEGIN;
SELECT pg_advisory_xact_lock(hashtext('odca.schema.migrations'));

ALTER TABLE odca.contract_drafts ADD COLUMN patient_row_version bigint;
UPDATE odca.contract_drafts d SET patient_row_version=p.row_version
  FROM odca.patients p WHERE (p.tenant_id,p.id)=(d.tenant_id,d.patient_id);
ALTER TABLE odca.contract_drafts ADD CONSTRAINT contract_drafts_patient_version_ck
  CHECK((patient_id IS NULL)=(patient_row_version IS NULL));

INSERT INTO odca.schema_migrations(version,name,checksum)
VALUES(24,'Patient version confirmation before document generation','9c9a67713215145f150d3e6d4fd7b152eb4346889c9699440555339d936b5691') ON CONFLICT(version) DO NOTHING;
COMMIT;
-- ODCA-END 024

-- ODCA-MIGRATION 025 CHECKSUM 3ee7e7640c0d8ee8df4bfcfbde5ce4268db74738da9677cea3f9c7f9e5705d65
BEGIN;
SELECT pg_advisory_xact_lock(hashtext('odca.schema.migrations'));

CREATE FUNCTION odca.patient_document_snapshot(p_tenant_id uuid,p_patient_id uuid)
RETURNS jsonb LANGUAGE sql STABLE SET search_path=pg_catalog,odca AS $function$
 SELECT CASE WHEN p.id IS NULL THEN NULL ELSE jsonb_build_object(
   'id',p.id,'fullName',p.full_name,'preferredName',p.preferred_name,'birthDate',p.birth_date,
   'email',p.email,'phone',p.phone,'address',p.address,'identifierType',p.identifier_type,
   'identifierValue',p.identifier_value,'representative',CASE WHEN r.id IS NULL THEN NULL ELSE jsonb_build_object(
     'fullName',r.full_name,'identifierType',r.identifier_type,'identifierValue',r.identifier_value,'relationship',r.relationship) END)
 END
 FROM (SELECT p.* FROM odca.patients p WHERE p.tenant_id=p_tenant_id AND p.id=p_patient_id) p
 LEFT JOIN odca.patient_representatives r ON (r.tenant_id,r.id)=(p.tenant_id,p.representative_id)
$function$;
REVOKE ALL ON FUNCTION odca.patient_document_snapshot(uuid,uuid) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION odca.patient_document_snapshot(uuid,uuid) TO odca_app;

ALTER TABLE odca.contract_drafts ADD COLUMN patient_selection_snapshot jsonb;
UPDATE odca.contract_drafts d SET patient_selection_snapshot=odca.patient_document_snapshot(d.tenant_id,d.patient_id)
 WHERE d.patient_id IS NOT NULL;
ALTER TABLE odca.contract_drafts ADD CONSTRAINT contract_drafts_patient_snapshot_ck
 CHECK((patient_id IS NULL)=(patient_selection_snapshot IS NULL));

INSERT INTO odca.schema_migrations(version,name,checksum)
VALUES(25,'Recoverable patient document conference','3ee7e7640c0d8ee8df4bfcfbde5ce4268db74738da9677cea3f9c7f9e5705d65') ON CONFLICT(version) DO NOTHING;
COMMIT;-- ODCA-END 025

-- ODCA-MIGRATION 026 CHECKSUM 8100e40a0155473f1f8fb70076fc76adb25b9a7ce785bbb94407fb7d3ffd6f7d
BEGIN;
SELECT pg_advisory_xact_lock(hashtext('odca.schema.migrations'));

ALTER TABLE odca.generated_contract_versions
 ADD COLUMN emission_metadata jsonb,
 ADD COLUMN pdf_status text NOT NULL DEFAULT 'pending' CHECK(pdf_status IN('pending','completed','failed')),
 ADD COLUMN pdf_storage_key text UNIQUE,
 ADD COLUMN pdf_sha256 char(64),
 ADD COLUMN pdf_byte_size bigint CHECK(pdf_byte_size>0),
 ADD COLUMN pdf_renderer_version varchar(80),
 ADD COLUMN pdf_completed_at timestamptz,
 ADD COLUMN pdf_failure_code varchar(120),
 ADD CONSTRAINT generated_version_pdf_state_ck CHECK(
   (pdf_status='completed' AND pdf_storage_key IS NOT NULL AND pdf_sha256 IS NOT NULL AND pdf_byte_size IS NOT NULL AND pdf_renderer_version IS NOT NULL AND pdf_completed_at IS NOT NULL)
   OR (pdf_status<>'completed' AND pdf_storage_key IS NULL AND pdf_sha256 IS NULL AND pdf_byte_size IS NULL AND pdf_completed_at IS NULL));

CREATE TABLE odca.signature_preparations(
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(), tenant_id uuid NOT NULL, generated_version_id uuid NOT NULL,
 status text NOT NULL DEFAULT 'draft' CHECK(status IN('draft','confirmed')), row_version bigint NOT NULL DEFAULT 1 CHECK(row_version>0),
 created_by uuid NOT NULL, updated_by uuid NOT NULL, created_at timestamptz NOT NULL DEFAULT now(), updated_at timestamptz NOT NULL DEFAULT now(),
 UNIQUE(tenant_id,id), UNIQUE(tenant_id,generated_version_id),
 FOREIGN KEY(tenant_id,generated_version_id) REFERENCES odca.generated_contract_versions(tenant_id,id),
 FOREIGN KEY(tenant_id,created_by) REFERENCES odca.memberships(tenant_id,user_id),
 FOREIGN KEY(tenant_id,updated_by) REFERENCES odca.memberships(tenant_id,user_id));
CREATE TABLE odca.signature_participants(
 id uuid PRIMARY KEY, tenant_id uuid NOT NULL, preparation_id uuid NOT NULL,
 participant_type text NOT NULL CHECK(participant_type IN('patient','representative','professional','organization_representative')),
 source_id uuid, role varchar(120) NOT NULL, name varchar(160) NOT NULL, email varchar(254), phone varchar(40), position integer NOT NULL CHECK(position>0),
 UNIQUE(tenant_id,preparation_id,id), UNIQUE(tenant_id,preparation_id,position),
 FOREIGN KEY(tenant_id,preparation_id) REFERENCES odca.signature_preparations(tenant_id,id) ON DELETE CASCADE,
 CHECK(length(btrim(role))>0 AND length(btrim(name))>=2 AND (email IS NOT NULL OR phone IS NOT NULL)));
CREATE INDEX signature_preparations_version_ix ON odca.signature_preparations(tenant_id,generated_version_id);
ALTER TABLE odca.signature_preparations ENABLE ROW LEVEL SECURITY; ALTER TABLE odca.signature_preparations FORCE ROW LEVEL SECURITY;
ALTER TABLE odca.signature_participants ENABLE ROW LEVEL SECURITY; ALTER TABLE odca.signature_participants FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON odca.signature_preparations TO odca_app USING(tenant_id=nullif(current_setting('odca.tenant_id',true),'')::uuid) WITH CHECK(tenant_id=nullif(current_setting('odca.tenant_id',true),'')::uuid);
CREATE POLICY tenant_isolation ON odca.signature_participants TO odca_app USING(tenant_id=nullif(current_setting('odca.tenant_id',true),'')::uuid) WITH CHECK(tenant_id=nullif(current_setting('odca.tenant_id',true),'')::uuid);
GRANT SELECT,INSERT,UPDATE ON odca.signature_preparations,odca.signature_participants TO odca_app;

INSERT INTO odca.schema_migrations(version,name,checksum)
VALUES(26,'Immutable PDF and signature participant preparation','8100e40a0155473f1f8fb70076fc76adb25b9a7ce785bbb94407fb7d3ffd6f7d') ON CONFLICT(version) DO NOTHING;
COMMIT;
-- ODCA-END 026

-- ODCA-MIGRATION 027 CHECKSUM ad707eeb447350489a75d60a36530b0ebe7da857ce6991f7161ca462fc332afa
BEGIN;
SELECT pg_advisory_xact_lock(hashtext('odca.schema.migrations'));

ALTER TABLE odca.signature_preparations
 ADD COLUMN composition_revision integer NOT NULL DEFAULT 1 CHECK(composition_revision>0),
 ADD COLUMN confirmed_revision integer,
 ADD COLUMN confirmed_pdf_storage_key text,
 ADD COLUMN confirmed_pdf_sha256 char(64),
 ADD COLUMN confirmed_at timestamptz,
 ADD COLUMN confirmed_by uuid,
 ADD COLUMN confirmation_operation_id uuid,
 ADD CONSTRAINT signature_preparations_confirmation_ck CHECK(
   (status='confirmed' AND ((confirmed_revision IS NULL AND confirmed_pdf_storage_key IS NULL AND confirmed_pdf_sha256 IS NULL AND confirmed_at IS NULL AND confirmed_by IS NULL) OR (confirmed_revision=composition_revision AND confirmed_pdf_storage_key IS NOT NULL AND confirmed_pdf_sha256 IS NOT NULL AND confirmed_at IS NOT NULL AND confirmed_by IS NOT NULL AND confirmation_operation_id IS NOT NULL)))
   OR (status='draft' AND confirmed_revision IS NULL)),
 ADD CONSTRAINT signature_preparations_confirmation_operation_uq UNIQUE(tenant_id,confirmation_operation_id),
 ADD CONSTRAINT signature_preparations_confirmed_by_fk FOREIGN KEY(tenant_id,confirmed_by) REFERENCES odca.memberships(tenant_id,user_id);

ALTER TABLE odca.signature_participants
 DROP CONSTRAINT signature_participants_tenant_id_preparation_id_position_key,
 ADD COLUMN client_id uuid,
 ADD COLUMN composition_revision integer NOT NULL DEFAULT 1 CHECK(composition_revision>0),
 ADD COLUMN recorded_by uuid,
 ADD COLUMN recorded_at timestamptz NOT NULL DEFAULT now();
UPDATE odca.signature_participants p SET client_id=p.id,recorded_by=s.updated_by
 FROM odca.signature_preparations s WHERE (s.tenant_id,s.id)=(p.tenant_id,p.preparation_id);
ALTER TABLE odca.signature_participants
 ALTER COLUMN client_id SET NOT NULL,
 ALTER COLUMN recorded_by SET NOT NULL,
 ADD CONSTRAINT signature_participants_recorder_fk FOREIGN KEY(tenant_id,recorded_by) REFERENCES odca.memberships(tenant_id,user_id),
 ADD CONSTRAINT signature_participants_revision_fk FOREIGN KEY(tenant_id,preparation_id) REFERENCES odca.signature_preparations(tenant_id,id),
 ADD CONSTRAINT signature_participants_revision_position_uq UNIQUE(tenant_id,preparation_id,composition_revision,position),
 ADD CONSTRAINT signature_participants_revision_client_uq UNIQUE(tenant_id,preparation_id,composition_revision,client_id);
CREATE INDEX signature_participants_current_ix ON odca.signature_participants(tenant_id,preparation_id,composition_revision,position);

CREATE TABLE odca.signature_preparation_events(
 id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY, tenant_id uuid NOT NULL, preparation_id uuid NOT NULL,
 composition_revision integer NOT NULL CHECK(composition_revision>0), actor_user_id uuid NOT NULL,
 event_type text NOT NULL CHECK(event_type IN('created','updated','participant_added','participant_changed','participant_removed','participant_reordered','confirmed','reopened')),
 details jsonb NOT NULL DEFAULT '{}'::jsonb, occurred_at timestamptz NOT NULL DEFAULT now(),
 FOREIGN KEY(tenant_id,preparation_id) REFERENCES odca.signature_preparations(tenant_id,id),
 FOREIGN KEY(tenant_id,actor_user_id) REFERENCES odca.memberships(tenant_id,user_id));
CREATE INDEX signature_preparation_events_history_ix ON odca.signature_preparation_events(tenant_id,preparation_id,occurred_at,id);
ALTER TABLE odca.signature_preparation_events ENABLE ROW LEVEL SECURITY; ALTER TABLE odca.signature_preparation_events FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON odca.signature_preparation_events TO odca_app USING(tenant_id=nullif(current_setting('odca.tenant_id',true),'')::uuid) WITH CHECK(tenant_id=nullif(current_setting('odca.tenant_id',true),'')::uuid);
GRANT SELECT,INSERT ON odca.signature_preparation_events TO odca_app;

CREATE OR REPLACE FUNCTION odca.patient_document_snapshot(p_tenant_id uuid,p_patient_id uuid)
RETURNS jsonb LANGUAGE sql STABLE SET search_path=pg_catalog,odca AS $function$
 SELECT CASE WHEN p.id IS NULL THEN NULL ELSE jsonb_build_object(
   'id',p.id,'fullName',p.full_name,'preferredName',p.preferred_name,'birthDate',p.birth_date,
   'email',p.email,'phone',p.phone,'address',p.address,'identifierType',p.identifier_type,
   'identifierValue',p.identifier_value,'representative',CASE WHEN r.id IS NULL THEN NULL ELSE jsonb_build_object(
     'id',r.id,'fullName',r.full_name,'identifierType',r.identifier_type,'identifierValue',r.identifier_value,'relationship',r.relationship) END)
 END
 FROM (SELECT p.* FROM odca.patients p WHERE p.tenant_id=p_tenant_id AND p.id=p_patient_id) p
 LEFT JOIN odca.patient_representatives r ON (r.tenant_id,r.id)=(p.tenant_id,p.representative_id)
$function$;

INSERT INTO odca.schema_migrations(version,name,checksum)
VALUES(27,'Traceable signature preparation and confirmation evidence','ad707eeb447350489a75d60a36530b0ebe7da857ce6991f7161ca462fc332afa') ON CONFLICT(version) DO NOTHING;
COMMIT;
-- ODCA-END 027
