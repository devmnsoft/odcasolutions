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

-- ODCA-MIGRATION 028 CHECKSUM 1fa0b11b1f2728c7d1d868d9a230a0113d68c9e4a9d24de140d484b6bb456453
BEGIN;
SELECT pg_advisory_xact_lock(hashtext('odca.schema.migrations'));

CREATE TABLE odca.signature_preparation_operations(
 tenant_id uuid NOT NULL, preparation_id uuid NOT NULL, operation_id uuid NOT NULL,
 operation_type text NOT NULL CHECK(operation_type IN('confirm','reopen')),
 command_sha256 char(64) NOT NULL, response jsonb NOT NULL, actor_user_id uuid NOT NULL,
 created_at timestamptz NOT NULL DEFAULT now(),
 PRIMARY KEY(tenant_id,operation_id),
 FOREIGN KEY(tenant_id,preparation_id) REFERENCES odca.signature_preparations(tenant_id,id),
 FOREIGN KEY(tenant_id,actor_user_id) REFERENCES odca.memberships(tenant_id,user_id));
CREATE INDEX signature_preparation_operations_preparation_ix
 ON odca.signature_preparation_operations(tenant_id,preparation_id,created_at);
ALTER TABLE odca.signature_preparation_operations ENABLE ROW LEVEL SECURITY;
ALTER TABLE odca.signature_preparation_operations FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON odca.signature_preparation_operations TO odca_app
 USING(tenant_id=nullif(current_setting('odca.tenant_id',true),'')::uuid)
 WITH CHECK(tenant_id=nullif(current_setting('odca.tenant_id',true),'')::uuid);
GRANT SELECT,INSERT ON odca.signature_preparation_operations TO odca_app;

INSERT INTO odca.schema_migrations(version,name,checksum)
VALUES(28,'Idempotent signature preparation operations','1fa0b11b1f2728c7d1d868d9a230a0113d68c9e4a9d24de140d484b6bb456453') ON CONFLICT(version) DO NOTHING;
COMMIT;
-- ODCA-END 028

-- ODCA-MIGRATION 029 CHECKSUM a836449137e4f20c2ac6eebbb76fda782f20f623e7382593db0756c645f47451
BEGIN;
SELECT pg_advisory_xact_lock(hashtext('odca.schema.migrations'));

CREATE OR REPLACE FUNCTION odca.user_has_active_access(p_user_id uuid)
RETURNS boolean LANGUAGE sql SECURITY DEFINER STABLE SET search_path=pg_catalog,odca AS $$
    SELECT EXISTS (
        SELECT 1
          FROM odca.memberships m
          JOIN odca.tenants t ON t.id = m.tenant_id
         WHERE m.user_id = p_user_id
           AND m.status = 'active'
           AND (
               t.status = 'active'
               OR (
                   t.status = 'pending'
                   AND EXISTS (
                       SELECT 1 FROM odca.subscriptions s
                        WHERE s.tenant_id = t.id
                          AND s.commercial_state = 'commercial_pending')
                   AND EXISTS (
                       SELECT 1 FROM odca.member_roles mr
                       JOIN odca.roles r ON r.id = mr.role_id
                        WHERE mr.tenant_id = m.tenant_id
                          AND mr.user_id = m.user_id
                          AND r.tenant_id = m.tenant_id
                          AND r.code = 'tenant-administrator')
               ))
           AND NOT t.is_deleted
    );
$$;
REVOKE ALL ON FUNCTION odca.user_has_active_access(uuid) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION odca.user_has_active_access(uuid) TO odca_app;

INSERT INTO odca.schema_migrations(version,name,checksum)
VALUES(29,'Security definer user active access lookup','a836449137e4f20c2ac6eebbb76fda782f20f623e7382593db0756c645f47451') ON CONFLICT(version) DO NOTHING;
COMMIT;
-- ODCA-END 029

-- ODCA-MIGRATION 030 CHECKSUM 415ce319252332151f3e954cc373bbd417636653d57d36e55e80b036d5a29ddc
BEGIN;
SELECT pg_advisory_xact_lock(hashtext('odca.schema.migrations'));

GRANT DELETE ON odca.role_permissions, odca.member_roles TO odca_app;

INSERT INTO odca.schema_migrations(version,name,checksum)
VALUES(30,'Grant delete on role_permissions and member_roles to odca_app','415ce319252332151f3e954cc373bbd417636653d57d36e55e80b036d5a29ddc') ON CONFLICT(version) DO NOTHING;
COMMIT;
-- ODCA-END 030

-- ODCA-MIGRATION 031 CHECKSUM 35ded679d82757ff6ffb64e16c38abfc89834209672264041c292362f6f8ad31
BEGIN;
SELECT pg_advisory_xact_lock(hashtext('odca.schema.migrations'));

ALTER TABLE odca.contract_templates
    ADD COLUMN IF NOT EXISTS official_key text,
    ADD COLUMN IF NOT EXISTS official_revision integer,
    ADD COLUMN IF NOT EXISTS document_purpose text;

ALTER TABLE odca.contract_templates
    DROP CONSTRAINT IF EXISTS contract_templates_official_key_ck;
ALTER TABLE odca.contract_templates
    ADD CONSTRAINT contract_templates_official_key_ck
    CHECK (official_key IS NULL OR official_key ~ '^[a-z0-9]+(-[a-z0-9]+)*$');

ALTER TABLE odca.contract_templates
    DROP CONSTRAINT IF EXISTS contract_templates_official_revision_ck;
ALTER TABLE odca.contract_templates
    ADD CONSTRAINT contract_templates_official_revision_ck
    CHECK (
        (official_key IS NULL AND official_revision IS NULL)
        OR (official_key IS NOT NULL AND official_revision IS NOT NULL AND official_revision > 0));

ALTER TABLE odca.contract_templates
    DROP CONSTRAINT IF EXISTS contract_templates_document_purpose_ck;
ALTER TABLE odca.contract_templates
    ADD CONSTRAINT contract_templates_document_purpose_ck
    CHECK (document_purpose IS NULL OR document_purpose IN (
        'service_agreement', 'care_agreement', 'consent', 'confidentiality', 'amendment', 'supply', 'lease'));

CREATE UNIQUE INDEX IF NOT EXISTS contract_templates_official_key_ux
    ON odca.contract_templates (owner_tenant_id, official_key)
    WHERE official_key IS NOT NULL AND status <> 'archived';

CREATE UNIQUE INDEX IF NOT EXISTS contract_templates_global_official_key_ux
    ON odca.contract_templates (official_key)
    WHERE official_key IS NOT NULL AND owner_tenant_id IS NULL AND status <> 'archived';

UPDATE odca.contract_templates AS template
   SET official_key = installed.key,
       official_revision = 1
  FROM (
        SELECT DISTINCT ON (event.tenant_id, event.metadata->>'key')
               event.entity_id AS template_id,
               event.tenant_id,
               event.metadata->>'key' AS key
          FROM odca.audit_events AS event
         WHERE event.action = 'template.official_installed'
           AND event.result = 'success'
           AND event.entity_id IS NOT NULL
           AND event.metadata ? 'key'
           AND event.metadata->>'key' ~ '^[a-z0-9]+(-[a-z0-9]+)*$'
         ORDER BY event.tenant_id, event.metadata->>'key', event.occurred_at
       ) AS installed
 WHERE template.id = installed.template_id
   AND template.owner_tenant_id = installed.tenant_id
   AND template.official_key IS NULL
   AND template.status <> 'archived'
   AND NOT EXISTS (
        SELECT 1
          FROM odca.contract_templates AS other
         WHERE other.owner_tenant_id = template.owner_tenant_id
           AND other.official_key = installed.key
           AND other.status <> 'archived'
           AND other.id <> template.id);

INSERT INTO odca.schema_migrations(version,name,checksum)
VALUES(31,'Official template key and document purpose','35ded679d82757ff6ffb64e16c38abfc89834209672264041c292362f6f8ad31')
ON CONFLICT(version) DO NOTHING;
COMMIT;
-- ODCA-END 031

-- ODCA-MIGRATION 032 CHECKSUM fdb533b97b4a754ff5fceaf1d8e06d7602a0e7a792d6d48154b1d2c99556f891
BEGIN;
SELECT pg_advisory_xact_lock(hashtext('odca.schema.migrations'));

CREATE TABLE IF NOT EXISTS odca.organization_feature_blocks
(
    tenant_id uuid NOT NULL REFERENCES odca.tenants(id),
    feature_code text NOT NULL,
    reason text NOT NULL,
    actor_user_id uuid,
    blocked_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (tenant_id, feature_code),
    CONSTRAINT organization_feature_blocks_code_ck CHECK (feature_code IN (
        'patients', 'contract_drafts', 'templates', 'documents', 'reviews', 'signatures', 'imports')),
    CONSTRAINT organization_feature_blocks_reason_ck CHECK (char_length(btrim(reason)) BETWEEN 5 AND 500)
);

ALTER TABLE odca.organization_feature_blocks ENABLE ROW LEVEL SECURITY;
ALTER TABLE odca.organization_feature_blocks FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_read ON odca.organization_feature_blocks;
CREATE POLICY tenant_read ON odca.organization_feature_blocks
    FOR SELECT TO odca_app
    USING (tenant_id = nullif(current_setting('odca.tenant_id', true), '')::uuid);
GRANT SELECT ON odca.organization_feature_blocks TO odca_app;

CREATE OR REPLACE FUNCTION odca.organization_feature_state(requested_tenant_id uuid, requested_feature text)
RETURNS text
LANGUAGE plpgsql
STABLE
SECURITY DEFINER
SET search_path = pg_catalog, odca
AS $$
DECLARE
    entitlement text;
BEGIN
    IF requested_feature IS NULL OR requested_feature NOT IN (
        'patients', 'contract_drafts', 'templates', 'documents', 'reviews', 'signatures', 'imports') THEN
        RETURN 'unknown_feature';
    END IF;
    IF EXISTS (
        SELECT 1 FROM odca.organization_feature_blocks AS block
         WHERE block.tenant_id = requested_tenant_id
           AND block.feature_code = requested_feature) THEN
        RETURN 'administratively_blocked';
    END IF;
    entitlement := CASE requested_feature
        WHEN 'signatures' THEN 'signature_envelopes_monthly'
        WHEN 'imports' THEN 'ocr_pages_monthly'
        ELSE NULL
    END;
    IF entitlement IS NOT NULL AND NOT EXISTS (
        SELECT 1
          FROM odca.subscriptions AS subscription
          JOIN odca.plan_entitlements AS entitlement_row
            ON entitlement_row.plan_version_id = subscription.plan_version_id
         WHERE subscription.tenant_id = requested_tenant_id
           AND entitlement_row.entitlement_code = entitlement
           AND entitlement_row.enabled
           AND COALESCE(entitlement_row.limit_value, 0) > 0) THEN
        RETURN 'plan_restricted';
    END IF;
    RETURN 'allowed';
END;
$$;

CREATE OR REPLACE FUNCTION odca.organization_operation_gate(actor uuid, requested_tenant uuid, requested_feature text)
RETURNS TABLE(state text, reason text)
LANGUAGE plpgsql
STABLE
SECURITY DEFINER
SET search_path = pg_catalog, odca
AS $$
DECLARE
    current_status text;
    allowed_reader boolean;
    feature_state text;
BEGIN
    SELECT tenant.status INTO current_status
      FROM odca.tenants AS tenant
     WHERE tenant.id = requested_tenant
       AND NOT tenant.is_deleted;
    IF current_status IS NULL THEN
        state := 'skip';
        reason := NULL;
        RETURN NEXT;
        RETURN;
    END IF;
    allowed_reader := EXISTS (
        SELECT 1 FROM odca.users AS platform_user
         WHERE platform_user.id = actor
           AND platform_user.is_platform_administrator
           AND NOT platform_user.is_deleted)
        OR EXISTS (
        SELECT 1 FROM odca.memberships AS membership
         WHERE membership.user_id = actor
           AND membership.tenant_id = requested_tenant
           AND membership.status = 'active');
    IF NOT allowed_reader THEN
        state := 'skip';
        reason := NULL;
        RETURN NEXT;
        RETURN;
    END IF;
    IF current_status = 'suspended' THEN
        state := 'organization_suspended';
        reason := NULL;
        RETURN NEXT;
        RETURN;
    END IF;
    IF requested_feature IS NULL OR btrim(requested_feature) = '' THEN
        state := 'allowed';
        reason := NULL;
        RETURN NEXT;
        RETURN;
    END IF;
    feature_state := odca.organization_feature_state(requested_tenant, requested_feature);
    state := feature_state;
    IF feature_state = 'administratively_blocked' THEN
        SELECT block.reason INTO reason
          FROM odca.organization_feature_blocks AS block
         WHERE block.tenant_id = requested_tenant
           AND block.feature_code = requested_feature;
    ELSE
        reason := NULL;
    END IF;
    RETURN NEXT;
END;
$$;

CREATE OR REPLACE FUNCTION odca.list_organization_features(actor uuid, requested_tenant uuid)
RETURNS TABLE(feature_code text, state text, reason text, blocked_at timestamptz, plan_entitlement text, tenant_status text)
LANGUAGE plpgsql
STABLE
SECURITY DEFINER
SET search_path = pg_catalog, odca
AS $$
DECLARE
    current_status text;
BEGIN
    IF actor IS DISTINCT FROM nullif(current_setting('odca.user_id', true), '')::uuid THEN
        RAISE EXCEPTION 'actor mismatch' USING ERRCODE = '42501';
    END IF;
    SELECT tenant.status INTO current_status
      FROM odca.tenants AS tenant
     WHERE tenant.id = requested_tenant
       AND NOT tenant.is_deleted;
    IF current_status IS NULL THEN
        RAISE EXCEPTION 'organization missing' USING ERRCODE = 'P0002';
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM odca.users AS platform_user
         WHERE platform_user.id = actor
           AND platform_user.is_platform_administrator
           AND NOT platform_user.is_deleted)
       AND NOT EXISTS (
        SELECT 1 FROM odca.memberships AS membership
         WHERE membership.user_id = actor
           AND membership.tenant_id = requested_tenant
           AND membership.status = 'active') THEN
        RAISE EXCEPTION 'feature catalog forbidden' USING ERRCODE = '42501';
    END IF;
    RETURN QUERY
    SELECT item.code,
           odca.organization_feature_state(requested_tenant, item.code),
           block.reason,
           block.blocked_at,
           item.entitlement,
           current_status
      FROM (VALUES
            ('patients', NULL::text),
            ('contract_drafts', NULL::text),
            ('templates', NULL::text),
            ('documents', NULL::text),
            ('reviews', NULL::text),
            ('signatures', 'signature_envelopes_monthly'),
            ('imports', 'ocr_pages_monthly')) AS item(code, entitlement)
      LEFT JOIN odca.organization_feature_blocks AS block
        ON block.tenant_id = requested_tenant
       AND block.feature_code = item.code;
END;
$$;

CREATE OR REPLACE FUNCTION odca.set_organization_feature_block(
    actor uuid,
    requested_tenant uuid,
    requested_feature text,
    should_block boolean,
    justification text)
RETURNS text
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, odca
AS $$
DECLARE
    trimmed text := btrim(coalesce(justification, ''));
    previous text;
BEGIN
    PERFORM odca.assert_platform_actor(actor);
    IF requested_feature NOT IN (
        'patients', 'contract_drafts', 'templates', 'documents', 'reviews', 'signatures', 'imports') THEN
        RAISE EXCEPTION 'unknown feature' USING ERRCODE = '22023';
    END IF;
    IF char_length(trimmed) < 5 OR char_length(trimmed) > 500 THEN
        RAISE EXCEPTION 'invalid reason' USING ERRCODE = '22023';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM odca.tenants WHERE id = requested_tenant AND NOT is_deleted) THEN
        RETURN 'missing';
    END IF;
    previous := odca.organization_feature_state(requested_tenant, requested_feature);
    IF should_block THEN
        INSERT INTO odca.organization_feature_blocks(tenant_id, feature_code, reason, actor_user_id)
        VALUES (requested_tenant, requested_feature, trimmed, actor)
        ON CONFLICT (tenant_id, feature_code) DO UPDATE
            SET reason = EXCLUDED.reason,
                actor_user_id = EXCLUDED.actor_user_id,
                blocked_at = now();
    ELSE
        DELETE FROM odca.organization_feature_blocks
         WHERE tenant_id = requested_tenant
           AND feature_code = requested_feature;
    END IF;
    INSERT INTO odca.audit_events(
        scope_type, tenant_id, actor_user_id, action, entity_type, entity_id, result, metadata)
    VALUES (
        'tenant',
        requested_tenant,
        actor,
        CASE WHEN should_block THEN 'organization.feature_blocked' ELSE 'organization.feature_released' END,
        'organization_feature',
        requested_tenant,
        'success',
        jsonb_build_object('feature', requested_feature, 'reason', trimmed, 'previous_state', previous));
    RETURN odca.organization_feature_state(requested_tenant, requested_feature);
END;
$$;

REVOKE ALL ON FUNCTION odca.organization_feature_state(uuid, text) FROM PUBLIC;
REVOKE ALL ON FUNCTION odca.organization_operation_gate(uuid, uuid, text) FROM PUBLIC;
REVOKE ALL ON FUNCTION odca.list_organization_features(uuid, uuid) FROM PUBLIC;
REVOKE ALL ON FUNCTION odca.set_organization_feature_block(uuid, uuid, text, boolean, text) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION odca.organization_feature_state(uuid, text) TO odca_app;
GRANT EXECUTE ON FUNCTION odca.organization_operation_gate(uuid, uuid, text) TO odca_app;
GRANT EXECUTE ON FUNCTION odca.list_organization_features(uuid, uuid) TO odca_app;
GRANT EXECUTE ON FUNCTION odca.set_organization_feature_block(uuid, uuid, text, boolean, text) TO odca_app;

CREATE OR REPLACE FUNCTION odca.claim_document_scan()
RETURNS TABLE(id uuid, tenant_id uuid, storage_key text, detected_type text)
LANGUAGE sql
SECURITY DEFINER
SET search_path = pg_catalog, odca
AS $$
    UPDATE odca.document_versions SET security_status = 'scanning'
     WHERE document_versions.id = (
        SELECT version.id
          FROM odca.document_versions AS version
         WHERE version.security_status IN ('pending', 'scan_failed')
           AND odca.organization_feature_state(version.tenant_id, 'imports') = 'allowed'
         ORDER BY version.uploaded_at
         FOR UPDATE SKIP LOCKED
         LIMIT 1)
    RETURNING document_versions.id, document_versions.tenant_id, document_versions.storage_key, document_versions.detected_type;
$$;

CREATE OR REPLACE FUNCTION odca.claim_extraction_job(requested_lease uuid)
RETURNS TABLE(id uuid, tenant_id uuid, contract_id uuid, version_id uuid, lease_token uuid)
LANGUAGE sql
SECURITY DEFINER
SET search_path = pg_catalog, odca
AS $$
    UPDATE odca.extraction_jobs SET status = 'processing', attempt_count = attempt_count + 1, lease_token = requested_lease, lease_expires_at = now() + interval '5 minutes'
     WHERE extraction_jobs.id = (
        SELECT job.id
          FROM odca.extraction_jobs AS job
          JOIN odca.document_versions AS version
            ON version.id = job.version_id
           AND version.tenant_id = job.tenant_id
         WHERE (job.status = 'queued' OR (job.status = 'processing' AND job.lease_expires_at < now()))
           AND job.available_at <= now()
           AND job.attempt_count < job.max_attempts
           AND version.security_status = 'safe'
           AND odca.organization_feature_state(job.tenant_id, 'imports') = 'allowed'
         ORDER BY job.requested_at
         FOR UPDATE OF job SKIP LOCKED
         LIMIT 1)
    RETURNING extraction_jobs.id, extraction_jobs.tenant_id, extraction_jobs.contract_id, extraction_jobs.version_id, extraction_jobs.lease_token;
$$;

INSERT INTO odca.schema_migrations(version,name,checksum)
VALUES(32,'Audited organization feature blocks','fdb533b97b4a754ff5fceaf1d8e06d7602a0e7a792d6d48154b1d2c99556f891')
ON CONFLICT(version) DO NOTHING;
COMMIT;
-- ODCA-END 032
-- ODCA-MIGRATION 033 CHECKSUM 9e4ded3308d5ed28970a82784facb34cc046d71d41730f2445cd539e840b50b5
BEGIN;
SELECT pg_advisory_xact_lock(hashtext('odca.schema.migrations'));

CREATE INDEX IF NOT EXISTS audit_events_platform_time_ix
    ON odca.audit_events (occurred_at DESC, id DESC);

CREATE OR REPLACE FUNCTION odca.platform_audit_page(
    requesting_user_id uuid,
    requested_page integer,
    requested_page_size integer,
    search text,
    requested_tenant_id uuid)
RETURNS TABLE(
    id bigint,
    scope_type text,
    tenant_id uuid,
    tenant_name text,
    actor_user_id uuid,
    actor_name text,
    action text,
    entity_type text,
    entity_id uuid,
    occurred_at timestamptz,
    result text,
    metadata jsonb)
LANGUAGE plpgsql
STABLE
SECURITY DEFINER
SET search_path = pg_catalog, odca
AS $$
DECLARE
    page integer := GREATEST(COALESCE(requested_page, 1), 1);
    page_size integer := LEAST(GREATEST(COALESCE(requested_page_size, 25), 1), 100);
    pattern text;
BEGIN
    IF requesting_user_id IS DISTINCT FROM nullif(current_setting('odca.user_id', true), '')::uuid
       OR NOT EXISTS (
            SELECT 1 FROM odca.users AS platform_user
             WHERE platform_user.id = requesting_user_id
               AND platform_user.is_platform_administrator
               AND NOT platform_user.is_deleted) THEN
        RAISE EXCEPTION 'platform administrator required' USING ERRCODE = '42501';
    END IF;
    pattern := btrim(coalesce(search, ''));
    IF pattern <> '' THEN
        pattern := '%' || replace(replace(pattern, '%', '\%'), '_', '\_') || '%';
    END IF;
    RETURN QUERY
    SELECT event.id, event.scope_type, event.tenant_id, tenant.display_name,
           event.actor_user_id, actor.display_name, event.action, event.entity_type,
           event.entity_id, event.occurred_at, event.result, event.metadata
      FROM odca.audit_events AS event
      LEFT JOIN odca.tenants AS tenant ON tenant.id = event.tenant_id
      LEFT JOIN odca.users AS actor ON actor.id = event.actor_user_id
     WHERE (requested_tenant_id IS NULL OR event.tenant_id = requested_tenant_id)
       AND (pattern = '' OR event.action ILIKE pattern OR event.entity_type ILIKE pattern
            OR COALESCE(tenant.display_name, '') ILIKE pattern
            OR COALESCE(actor.display_name, '') ILIKE pattern)
     ORDER BY event.occurred_at DESC, event.id DESC
     LIMIT page_size OFFSET (page - 1) * page_size;
END;
$$;

CREATE OR REPLACE FUNCTION odca.platform_audit_count(requesting_user_id uuid, search text, requested_tenant_id uuid)
RETURNS bigint
LANGUAGE plpgsql
STABLE
SECURITY DEFINER
SET search_path = pg_catalog, odca
AS $$
DECLARE
    pattern text;
BEGIN
    IF requesting_user_id IS DISTINCT FROM nullif(current_setting('odca.user_id', true), '')::uuid
       OR NOT EXISTS (
            SELECT 1 FROM odca.users AS platform_user
             WHERE platform_user.id = requesting_user_id
               AND platform_user.is_platform_administrator
               AND NOT platform_user.is_deleted) THEN
        RAISE EXCEPTION 'platform administrator required' USING ERRCODE = '42501';
    END IF;
    pattern := btrim(coalesce(search, ''));
    IF pattern <> '' THEN
        pattern := '%' || replace(replace(pattern, '%', '\%'), '_', '\_') || '%';
    END IF;
    RETURN (
        SELECT count(*)
          FROM odca.audit_events AS event
          LEFT JOIN odca.tenants AS tenant ON tenant.id = event.tenant_id
          LEFT JOIN odca.users AS actor ON actor.id = event.actor_user_id
         WHERE (requested_tenant_id IS NULL OR event.tenant_id = requested_tenant_id)
           AND (pattern = '' OR event.action ILIKE pattern OR event.entity_type ILIKE pattern
                OR COALESCE(tenant.display_name, '') ILIKE pattern
                OR COALESCE(actor.display_name, '') ILIKE pattern)
    );
END;
$$;

REVOKE ALL ON FUNCTION odca.platform_audit_page(uuid, integer, integer, text, uuid) FROM PUBLIC;
REVOKE ALL ON FUNCTION odca.platform_audit_count(uuid, text, uuid) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION odca.platform_audit_page(uuid, integer, integer, text, uuid) TO odca_app;
GRANT EXECUTE ON FUNCTION odca.platform_audit_count(uuid, text, uuid) TO odca_app;

INSERT INTO odca.schema_migrations(version,name,checksum)
VALUES(33,'Platform-wide audit search','9e4ded3308d5ed28970a82784facb34cc046d71d41730f2445cd539e840b50b5')
ON CONFLICT(version) DO NOTHING;
COMMIT;
-- ODCA-END 033
-- ODCA-MIGRATION 034 CHECKSUM 721ec0b23d2d64526d14be9cb0b02225ead097c169671c3e09b10ef91da81aee
BEGIN;
SELECT pg_advisory_xact_lock(hashtext('odca.schema.migrations'));

-- Version 2 publishes the same quantitative limits as version 1 and adds explicit
-- module enablement. Existing subscriptions keep plan_version_id. Version 1 stays
-- published only until the cutover, so contracts already stored are not rewritten.
UPDATE odca.plan_versions
   SET effective_until = timestamptz '2026-10-05T00:00:00Z'
 WHERE code IN ('basic', 'intermediate', 'enterprise')
   AND version = 1
   AND status = 'published'
   AND effective_until IS NULL;

INSERT INTO odca.plan_versions (id, code, version, display_name, status, effective_from)
VALUES
    ('30000000-0000-0000-0000-000000000011', 'basic', 2, 'Basic', 'published', timestamptz '2026-10-05T00:00:00Z'),
    ('30000000-0000-0000-0000-000000000012', 'intermediate', 2, 'Intermediário', 'published', timestamptz '2026-10-05T00:00:00Z'),
    ('30000000-0000-0000-0000-000000000013', 'enterprise', 2, 'Enterprise', 'published', timestamptz '2026-10-05T00:00:00Z')
ON CONFLICT (code, version) DO NOTHING;

INSERT INTO odca.plan_entitlements (plan_version_id, entitlement_code, limit_value, enabled)
SELECT newer.id, entitlement.entitlement_code, entitlement.limit_value, entitlement.enabled
  FROM odca.plan_versions AS previous
  JOIN odca.plan_versions AS newer
    ON newer.code = previous.code
   AND newer.version = 2
  JOIN odca.plan_entitlements AS entitlement
    ON entitlement.plan_version_id = previous.id
 WHERE previous.version = 1
   AND previous.code IN ('basic', 'intermediate', 'enterprise')
ON CONFLICT (plan_version_id, entitlement_code) DO NOTHING;

INSERT INTO odca.plan_entitlements (plan_version_id, entitlement_code, limit_value, enabled)
SELECT plan.id, module.code, NULL, true
  FROM odca.plan_versions AS plan
 CROSS JOIN (VALUES
    ('module.patients'),
    ('module.contract_drafts'),
    ('module.templates'),
    ('module.documents'),
    ('module.reviews'),
    ('module.signatures'),
    ('module.imports')) AS module(code)
 WHERE plan.version = 2
   AND plan.code IN ('basic', 'intermediate', 'enterprise')
ON CONFLICT (plan_version_id, entitlement_code) DO NOTHING;

CREATE OR REPLACE FUNCTION odca.organization_feature_state(requested_tenant_id uuid, requested_feature text)
RETURNS text
LANGUAGE plpgsql
STABLE
SECURITY DEFINER
SET search_path = pg_catalog, odca
AS $$
DECLARE
    quota_code text;
    plan_id uuid;
    declares_modules boolean;
BEGIN
    IF requested_feature IS NULL OR requested_feature NOT IN (
        'patients', 'contract_drafts', 'templates', 'documents', 'reviews', 'signatures', 'imports') THEN
        RETURN 'unknown_feature';
    END IF;
    IF EXISTS (
        SELECT 1 FROM odca.organization_feature_blocks AS block
         WHERE block.tenant_id = requested_tenant_id
           AND block.feature_code = requested_feature) THEN
        RETURN 'administratively_blocked';
    END IF;

    SELECT subscription.plan_version_id INTO plan_id
      FROM odca.subscriptions AS subscription
     WHERE subscription.tenant_id = requested_tenant_id
       AND subscription.status = 'active'
     LIMIT 1;

    declares_modules := plan_id IS NOT NULL AND EXISTS (
        SELECT 1 FROM odca.plan_entitlements AS entitlement_row
         WHERE entitlement_row.plan_version_id = plan_id
           AND entitlement_row.entitlement_code LIKE 'module.%');

    IF declares_modules AND NOT EXISTS (
        SELECT 1 FROM odca.plan_entitlements AS entitlement_row
         WHERE entitlement_row.plan_version_id = plan_id
           AND entitlement_row.entitlement_code = 'module.' || requested_feature
           AND entitlement_row.enabled) THEN
        RETURN 'plan_restricted';
    END IF;

    quota_code := CASE requested_feature
        WHEN 'signatures' THEN 'signature_envelopes_monthly'
        WHEN 'imports' THEN 'ocr_pages_monthly'
        ELSE NULL
    END;
    IF quota_code IS NULL THEN
        RETURN 'allowed';
    END IF;
    IF EXISTS (
        SELECT 1
          FROM odca.subscriptions AS subscription
          JOIN odca.plan_entitlements AS entitlement_row
            ON entitlement_row.plan_version_id = subscription.plan_version_id
         WHERE subscription.tenant_id = requested_tenant_id
           AND subscription.status = 'active'
           AND entitlement_row.entitlement_code = quota_code
           AND entitlement_row.enabled
           AND COALESCE(entitlement_row.limit_value, 0) > 0) THEN
        RETURN 'allowed';
    END IF;
    IF declares_modules THEN
        RETURN 'quota_exhausted';
    END IF;
    RETURN 'plan_restricted';
END;
$$;

CREATE OR REPLACE FUNCTION odca.organization_operation_gate(actor uuid, requested_tenant uuid, requested_feature text)
RETURNS TABLE(state text, reason text)
LANGUAGE plpgsql
STABLE
SECURITY DEFINER
SET search_path = pg_catalog, odca
AS $$
DECLARE
    current_status text;
    member_status text;
    platform_admin boolean;
    feature_state text;
BEGIN
    SELECT tenant.status INTO current_status
      FROM odca.tenants AS tenant
     WHERE tenant.id = requested_tenant
       AND NOT tenant.is_deleted;
    IF current_status IS NULL THEN
        state := 'skip';
        reason := NULL;
        RETURN NEXT;
        RETURN;
    END IF;
    platform_admin := EXISTS (
        SELECT 1 FROM odca.users AS platform_user
         WHERE platform_user.id = actor
           AND platform_user.is_platform_administrator
           AND NOT platform_user.is_deleted);
    SELECT membership.status INTO member_status
      FROM odca.memberships AS membership
     WHERE membership.user_id = actor
       AND membership.tenant_id = requested_tenant;
    IF NOT platform_admin AND member_status IS NOT NULL AND member_status <> 'active' THEN
        state := 'membership_blocked';
        reason := NULL;
        RETURN NEXT;
        RETURN;
    END IF;
    IF NOT platform_admin AND member_status IS DISTINCT FROM 'active' THEN
        state := 'skip';
        reason := NULL;
        RETURN NEXT;
        RETURN;
    END IF;
    IF current_status = 'suspended' THEN
        state := 'organization_suspended';
        reason := NULL;
        RETURN NEXT;
        RETURN;
    END IF;
    IF requested_feature IS NULL OR btrim(requested_feature) = '' THEN
        state := 'allowed';
        reason := NULL;
        RETURN NEXT;
        RETURN;
    END IF;
    feature_state := odca.organization_feature_state(requested_tenant, requested_feature);
    state := feature_state;
    IF feature_state = 'administratively_blocked' THEN
        SELECT block.reason INTO reason
          FROM odca.organization_feature_blocks AS block
         WHERE block.tenant_id = requested_tenant
           AND block.feature_code = requested_feature;
    ELSE
        reason := NULL;
    END IF;
    RETURN NEXT;
END;
$$;

CREATE OR REPLACE FUNCTION odca.ensure_tenant_standard_roles(target_tenant uuid)
RETURNS void
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, odca
AS $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM odca.tenants WHERE id = target_tenant AND NOT is_deleted) THEN
        RETURN;
    END IF;
    INSERT INTO odca.roles(scope_type, tenant_id, code, display_name, is_system)
    VALUES
        ('tenant', target_tenant, 'tenant-delegated-administrator', 'Administrador delegado', true),
        ('tenant', target_tenant, 'tenant-coordinator', 'Coordenador', true),
        ('tenant', target_tenant, 'tenant-member', 'Usuário', true)
    ON CONFLICT DO NOTHING;

    INSERT INTO odca.role_permissions(role_id, permission_code)
    SELECT role.id, permission.code
      FROM odca.roles AS role
      JOIN odca.permissions AS permission
        ON permission.code = ANY (
            CASE role.code
                WHEN 'tenant-delegated-administrator' THEN ARRAY[
                    'tenant.organization.read', 'tenant.team.read', 'tenant.team.manage',
                    'tenant.patients.read', 'tenant.templates.read', 'tenant.contract_drafts.read',
                    'tenant.documents.download', 'tenant.reviews.read', 'tenant.imports.read', 'tenant.billing.read']
                WHEN 'tenant-coordinator' THEN ARRAY[
                    'tenant.patients.read', 'tenant.templates.read', 'tenant.contract_drafts.read',
                    'tenant.reviews.read', 'tenant.reviews.decide', 'tenant.reviews.history',
                    'tenant.obligations.read', 'tenant.obligations.read_all', 'tenant.obligations.assign']
                WHEN 'tenant-member' THEN ARRAY[
                    'tenant.patients.read', 'tenant.patients.manage', 'tenant.templates.read',
                    'tenant.contract_drafts.read', 'tenant.contract_drafts.manage',
                    'tenant.documents.download', 'tenant.reviews.read', 'tenant.reviews.request']
                ELSE ARRAY[]::text[]
            END)
     WHERE role.tenant_id = target_tenant
       AND role.code IN ('tenant-delegated-administrator', 'tenant-coordinator', 'tenant-member')
    ON CONFLICT DO NOTHING;
END;
$$;

CREATE OR REPLACE FUNCTION odca.reject_platform_permission_on_tenant_role()
RETURNS trigger
LANGUAGE plpgsql
SET search_path = pg_catalog, odca
AS $$
DECLARE
    role_scope text;
BEGIN
    SELECT role.scope_type INTO role_scope FROM odca.roles AS role WHERE role.id = NEW.role_id;
    IF role_scope = 'tenant' AND (NEW.permission_code LIKE 'platform.%' OR NEW.permission_code LIKE 'superadmin.%') THEN
        RAISE EXCEPTION 'tenant role cannot receive platform permission' USING ERRCODE = '42501';
    END IF;
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS role_permissions_no_platform ON odca.role_permissions;
CREATE TRIGGER role_permissions_no_platform
    BEFORE INSERT OR UPDATE ON odca.role_permissions
    FOR EACH ROW EXECUTE FUNCTION odca.reject_platform_permission_on_tenant_role();

CREATE OR REPLACE FUNCTION odca.transfer_principal_administration(
    actor uuid,
    requested_tenant uuid,
    target_user uuid,
    justification text)
RETURNS text
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, odca
AS $$
DECLARE
    trimmed text := btrim(coalesce(justification, ''));
    principal_role uuid;
BEGIN
    IF char_length(trimmed) < 5 OR char_length(trimmed) > 500 THEN
        RETURN 'invalid';
    END IF;
    IF actor = target_user THEN
        RETURN 'invalid';
    END IF;
    PERFORM pg_advisory_xact_lock(hashtextextended(requested_tenant::text, 0));
    IF NOT odca.member_has_tenant_administrator_role(requested_tenant, actor) THEN
        RETURN 'forbidden';
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM odca.memberships AS membership
         WHERE membership.tenant_id = requested_tenant
           AND membership.user_id = target_user
           AND membership.status = 'active') THEN
        RETURN 'not_found';
    END IF;
    SELECT role.id INTO principal_role
      FROM odca.roles AS role
     WHERE role.tenant_id = requested_tenant
       AND role.code = 'tenant-administrator';
    IF principal_role IS NULL THEN
        RETURN 'not_found';
    END IF;
    INSERT INTO odca.member_roles(tenant_id, user_id, role_id, assigned_by)
    VALUES (requested_tenant, target_user, principal_role, actor)
    ON CONFLICT DO NOTHING;
    DELETE FROM odca.member_roles
     WHERE tenant_id = requested_tenant
       AND user_id = actor
       AND role_id = principal_role;
    IF odca.count_active_tenant_administrators(requested_tenant) < 1 THEN
        RAISE EXCEPTION 'principal administration lost' USING ERRCODE = '40001';
    END IF;
    UPDATE odca.memberships
       SET security_version = security_version + 1,
           updated_at = now()
     WHERE tenant_id = requested_tenant
       AND user_id IN (actor, target_user);
    UPDATE odca.users
       SET security_version = security_version + 1
     WHERE id IN (actor, target_user);
    UPDATE odca.sessions
       SET revoked_at = now()
     WHERE user_id IN (actor, target_user)
       AND revoked_at IS NULL;
    INSERT INTO odca.audit_events(
        scope_type, tenant_id, actor_user_id, action, entity_type, entity_id, result, metadata)
    VALUES (
        'tenant', requested_tenant, actor, 'tenant.administration.transferred', 'membership', target_user, 'success',
        jsonb_build_object('fromUserId', actor, 'toUserId', target_user, 'reason', trimmed));
    RETURN 'transferred';
END;
$$;

CREATE OR REPLACE FUNCTION odca.preview_organization_plan_change(actor uuid, requested_tenant uuid, requested_plan text)
RETURNS TABLE(
    current_code text,
    current_version integer,
    current_plan_version_id uuid,
    target_code text,
    target_version integer,
    target_plan_version_id uuid,
    active_members integer,
    reserved_invitations integer,
    current_seat_limit bigint,
    target_seat_limit bigint,
    seats_over_limit boolean,
    patient_count bigint,
    document_count bigint,
    policy text)
LANGUAGE plpgsql
STABLE
SECURITY DEFINER
SET search_path = pg_catalog, odca
AS $$
BEGIN
    PERFORM odca.assert_platform_actor(actor);
    RETURN QUERY
    SELECT current_plan.code,
           current_plan.version,
           current_plan.id,
           target_plan.code,
           target_plan.version,
           target_plan.id,
           (SELECT count(*)::integer FROM odca.memberships AS membership
             WHERE membership.tenant_id = requested_tenant AND membership.status = 'active'),
           (SELECT count(*)::integer FROM odca.tenant_invitations AS invitation
             WHERE invitation.tenant_id = requested_tenant
               AND invitation.status IN ('pending', 'sent')
               AND invitation.expires_at > now()),
           COALESCE((SELECT entitlement.limit_value FROM odca.plan_entitlements AS entitlement
              WHERE entitlement.plan_version_id = current_plan.id AND entitlement.entitlement_code = 'active_seats'), 0),
           COALESCE((SELECT entitlement.limit_value FROM odca.plan_entitlements AS entitlement
              WHERE entitlement.plan_version_id = target_plan.id AND entitlement.entitlement_code = 'active_seats'), 0),
           (
             (SELECT count(*) FROM odca.memberships AS membership
               WHERE membership.tenant_id = requested_tenant AND membership.status = 'active')
             + (SELECT count(*) FROM odca.tenant_invitations AS invitation
                 WHERE invitation.tenant_id = requested_tenant
                   AND invitation.status IN ('pending', 'sent')
                   AND invitation.expires_at > now())
           ) > COALESCE((SELECT entitlement.limit_value FROM odca.plan_entitlements AS entitlement
              WHERE entitlement.plan_version_id = target_plan.id AND entitlement.entitlement_code = 'active_seats'), 0),
           (SELECT count(*) FROM odca.patients AS patient WHERE patient.tenant_id = requested_tenant),
           (SELECT count(*) FROM odca.contract_documents AS document WHERE document.tenant_id = requested_tenant),
           'keep_members_and_documents'::text
      FROM odca.subscriptions AS subscription
      JOIN odca.plan_versions AS current_plan ON current_plan.id = subscription.plan_version_id
      JOIN odca.plan_versions AS target_plan
        ON target_plan.code = requested_plan
       AND target_plan.status = 'published'
       AND target_plan.effective_from <= now()
       AND (target_plan.effective_until IS NULL OR target_plan.effective_until > now())
     WHERE subscription.tenant_id = requested_tenant;
END;
$$;

CREATE OR REPLACE FUNCTION odca.apply_organization_plan_change(
    actor uuid,
    requested_tenant uuid,
    requested_plan text,
    justification text)
RETURNS text
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, odca
AS $$
DECLARE
    trimmed text := btrim(coalesce(justification, ''));
    current_version uuid;
    target_version uuid;
    over_limit boolean;
    patients bigint;
    documents bigint;
BEGIN
    PERFORM odca.assert_platform_actor(actor);
    IF char_length(trimmed) < 5 OR char_length(trimmed) > 500 THEN
        RETURN 'invalid';
    END IF;
    PERFORM pg_advisory_xact_lock(hashtextextended(requested_tenant::text, 0));
    SELECT subscription.plan_version_id INTO current_version
      FROM odca.subscriptions AS subscription
     WHERE subscription.tenant_id = requested_tenant
     FOR UPDATE;
    IF current_version IS NULL THEN
        RETURN 'missing';
    END IF;
    SELECT plan.id INTO target_version
      FROM odca.plan_versions AS plan
     WHERE plan.code = requested_plan
       AND plan.status = 'published'
       AND plan.effective_from <= now()
       AND (plan.effective_until IS NULL OR plan.effective_until > now());
    IF target_version IS NULL THEN
        RETURN 'missing_plan';
    END IF;
    IF (SELECT count(*) FROM odca.plan_versions AS plan
         WHERE plan.code = requested_plan
           AND plan.status = 'published'
           AND plan.effective_from <= now()
           AND (plan.effective_until IS NULL OR plan.effective_until > now())) > 1 THEN
        RAISE EXCEPTION 'conflicting published plan versions' USING ERRCODE = '23505';
    END IF;
    IF target_version = current_version THEN
        RETURN 'unchanged';
    END IF;
    SELECT preview.seats_over_limit, preview.patient_count, preview.document_count
      INTO over_limit, patients, documents
      FROM odca.preview_organization_plan_change(actor, requested_tenant, requested_plan) AS preview;
    UPDATE odca.subscriptions
       SET plan_version_id = target_version,
           updated_at = now()
     WHERE tenant_id = requested_tenant;
    INSERT INTO odca.audit_events(
        scope_type, tenant_id, actor_user_id, action, entity_type, entity_id, result, metadata)
    VALUES (
        'tenant', requested_tenant, actor, 'organization.plan_changed', 'subscription', requested_tenant, 'success',
        jsonb_build_object(
            'reason', trimmed,
            'previousPlanVersionId', current_version,
            'newPlanVersionId', target_version,
            'policy', 'keep_members_and_documents',
            'seatsOverLimit', COALESCE(over_limit, false),
            'patientsPreserved', patients,
            'documentsPreserved', documents,
            'paymentRecorded', false));
    RETURN 'applied';
END;
$$;

REVOKE ALL ON FUNCTION odca.ensure_tenant_standard_roles(uuid) FROM PUBLIC;
REVOKE ALL ON FUNCTION odca.reject_platform_permission_on_tenant_role() FROM PUBLIC;
REVOKE ALL ON FUNCTION odca.transfer_principal_administration(uuid, uuid, uuid, text) FROM PUBLIC;
REVOKE ALL ON FUNCTION odca.preview_organization_plan_change(uuid, uuid, text) FROM PUBLIC;
REVOKE ALL ON FUNCTION odca.apply_organization_plan_change(uuid, uuid, text, text) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION odca.ensure_tenant_standard_roles(uuid) TO odca_app;
GRANT EXECUTE ON FUNCTION odca.transfer_principal_administration(uuid, uuid, uuid, text) TO odca_app;
GRANT EXECUTE ON FUNCTION odca.preview_organization_plan_change(uuid, uuid, text) TO odca_app;
GRANT EXECUTE ON FUNCTION odca.apply_organization_plan_change(uuid, uuid, text, text) TO odca_app;

DO $$
DECLARE
    tenant_row record;
BEGIN
    IF EXISTS (
        SELECT 1
          FROM odca.plan_versions AS left_plan
          JOIN odca.plan_versions AS right_plan
            ON right_plan.code = left_plan.code
           AND right_plan.id <> left_plan.id
         WHERE left_plan.status = 'published'
           AND right_plan.status = 'published'
           AND left_plan.effective_from < COALESCE(right_plan.effective_until, 'infinity'::timestamptz)
           AND right_plan.effective_from < COALESCE(left_plan.effective_until, 'infinity'::timestamptz)
    ) THEN
        RAISE EXCEPTION 'conflicting published plan versions';
    END IF;
    IF EXISTS (
        SELECT 1
          FROM odca.plan_versions AS plan
         WHERE plan.version = 2
           AND plan.code IN ('basic', 'intermediate', 'enterprise')
           AND (
                SELECT count(*)
                  FROM odca.plan_entitlements AS entitlement
                 WHERE entitlement.plan_version_id = plan.id
                   AND entitlement.enabled
                   AND entitlement.entitlement_code IN (
                        'active_seats', 'storage_bytes', 'user_storage_bytes', 'file_bytes',
                        'ocr_pages_monthly', 'signature_envelopes_monthly')
                   AND entitlement.limit_value IS NOT NULL) < 6
    ) THEN
        RAISE EXCEPTION 'published plan missing mandatory limits';
    END IF;
    IF EXISTS (
        SELECT 1
          FROM odca.plan_versions AS plan
         WHERE plan.version = 2
           AND plan.code IN ('basic', 'intermediate', 'enterprise')
           AND (
                SELECT count(*)
                  FROM odca.plan_entitlements AS entitlement
                 WHERE entitlement.plan_version_id = plan.id
                   AND entitlement.enabled
                   AND entitlement.entitlement_code IN (
                        'module.patients', 'module.contract_drafts', 'module.templates', 'module.documents',
                        'module.reviews', 'module.signatures', 'module.imports')) < 7
    ) THEN
        RAISE EXCEPTION 'published plan missing module enablement';
    END IF;
    FOR tenant_row IN SELECT tenant.id FROM odca.tenants AS tenant WHERE NOT tenant.is_deleted LOOP
        PERFORM odca.ensure_tenant_standard_roles(tenant_row.id);
    END LOOP;
END $$;

INSERT INTO odca.schema_migrations(version, name, checksum)
VALUES (34, 'Hierarchical roles, explicit plan modules and plan change', '721ec0b23d2d64526d14be9cb0b02225ead097c169671c3e09b10ef91da81aee')
ON CONFLICT (version) DO NOTHING;
COMMIT;
-- ODCA-END 034
-- ODCA-MIGRATION 035 CHECKSUM 35164be04924df0aa9f98c1d3f4c032e18621331893d6e0d4115c0193888ac03
BEGIN;
SELECT pg_advisory_xact_lock(hashtext('odca.schema.migrations'));

CREATE OR REPLACE FUNCTION odca.franchise_period_start()
RETURNS timestamptz
LANGUAGE sql
STABLE
AS $$
    SELECT (date_trunc('month', now() AT TIME ZONE 'America/Sao_Paulo') AT TIME ZONE 'America/Sao_Paulo');
$$;

CREATE OR REPLACE FUNCTION odca.monthly_franchise_used(requested_tenant uuid, requested_resource text)
RETURNS bigint
LANGUAGE sql
STABLE
SECURITY DEFINER
SET search_path = pg_catalog, odca
AS $$
    SELECT COALESCE(SUM(
        CASE movement.movement_type
            WHEN 'consume' THEN movement.quantity
            WHEN 'reserve' THEN movement.quantity
            WHEN 'release' THEN -movement.quantity
            WHEN 'reversal' THEN -movement.quantity
            ELSE 0
        END), 0)::bigint
      FROM odca.resource_movements AS movement
     WHERE movement.tenant_id = requested_tenant
       AND movement.resource_type = requested_resource
       AND movement.movement_type IN ('consume', 'reserve', 'release', 'reversal')
       AND movement.occurred_at >= odca.franchise_period_start()
       AND movement.occurred_at < odca.franchise_period_start() + interval '1 month';
$$;

CREATE OR REPLACE FUNCTION odca.consume_monthly_franchise(
    requested_tenant uuid,
    requested_resource text,
    requested_quantity bigint,
    requested_key text,
    requested_source_type text,
    requested_source_id uuid,
    requested_actor text)
RETURNS text
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, odca
AS $$
DECLARE
    unit_name text;
    entitlement_name text;
    contracted bigint;
    used bigint;
BEGIN
    IF requested_quantity IS NULL OR requested_quantity <= 0
       OR requested_key IS NULL OR btrim(requested_key) = ''
       OR requested_source_type IS NULL OR btrim(requested_source_type) = ''
       OR requested_actor IS NULL OR btrim(requested_actor) = '' THEN
        RETURN 'invalid';
    END IF;
    IF requested_resource = 'ocr_credit' THEN
        unit_name := 'pages';
        entitlement_name := 'ocr_pages_monthly';
    ELSIF requested_resource = 'signature_credit' THEN
        unit_name := 'envelopes';
        entitlement_name := 'signature_envelopes_monthly';
    ELSE
        RETURN 'invalid';
    END IF;

    PERFORM pg_advisory_xact_lock(hashtextextended(requested_tenant::text || ':' || requested_resource, 0));
    IF EXISTS (
        SELECT 1 FROM odca.resource_movements AS movement
         WHERE movement.tenant_id = requested_tenant
           AND movement.idempotency_key = requested_key) THEN
        RETURN 'duplicate';
    END IF;

    SELECT entitlement.limit_value INTO contracted
      FROM odca.subscriptions AS subscription
      JOIN odca.plan_entitlements AS entitlement
        ON entitlement.plan_version_id = subscription.plan_version_id
     WHERE subscription.tenant_id = requested_tenant
       AND subscription.status = 'active'
       AND entitlement.entitlement_code = entitlement_name
       AND entitlement.enabled
     LIMIT 1;
    IF contracted IS NULL OR contracted <= 0 THEN
        RETURN 'not_contracted';
    END IF;
    used := odca.monthly_franchise_used(requested_tenant, requested_resource);
    IF used + requested_quantity > contracted THEN
        RETURN 'exhausted';
    END IF;

    INSERT INTO odca.resource_movements(
        tenant_id, resource_type, movement_type, quantity, unit, source_type, source_id,
        idempotency_key, actor_process, reason)
    VALUES (
        requested_tenant, requested_resource, 'consume', requested_quantity, unit_name, btrim(requested_source_type), requested_source_id,
        requested_key, btrim(requested_actor),
        CASE requested_resource
            WHEN 'ocr_credit' THEN 'Páginas de OCR processadas'
            ELSE 'Envelope de assinatura aceito pelo provedor'
        END);
    RETURN 'consumed';
END;
$$;

CREATE OR REPLACE FUNCTION odca.organization_feature_state(requested_tenant_id uuid, requested_feature text)
RETURNS text
LANGUAGE plpgsql
STABLE
SECURITY DEFINER
SET search_path = pg_catalog, odca
AS $$
DECLARE
    quota_code text;
    resource_name text;
    plan_id uuid;
    declares_modules boolean;
    contracted bigint;
    used bigint;
BEGIN
    IF requested_feature IS NULL OR requested_feature NOT IN (
        'patients', 'contract_drafts', 'templates', 'documents', 'reviews', 'signatures', 'imports') THEN
        RETURN 'unknown_feature';
    END IF;
    IF EXISTS (
        SELECT 1 FROM odca.organization_feature_blocks AS block
         WHERE block.tenant_id = requested_tenant_id
           AND block.feature_code = requested_feature) THEN
        RETURN 'administratively_blocked';
    END IF;

    SELECT subscription.plan_version_id INTO plan_id
      FROM odca.subscriptions AS subscription
     WHERE subscription.tenant_id = requested_tenant_id
       AND subscription.status = 'active'
     LIMIT 1;

    declares_modules := plan_id IS NOT NULL AND EXISTS (
        SELECT 1 FROM odca.plan_entitlements AS entitlement_row
         WHERE entitlement_row.plan_version_id = plan_id
           AND entitlement_row.entitlement_code LIKE 'module.%');

    IF declares_modules AND NOT EXISTS (
        SELECT 1 FROM odca.plan_entitlements AS entitlement_row
         WHERE entitlement_row.plan_version_id = plan_id
           AND entitlement_row.entitlement_code = 'module.' || requested_feature
           AND entitlement_row.enabled) THEN
        RETURN 'plan_restricted';
    END IF;

    quota_code := CASE requested_feature
        WHEN 'signatures' THEN 'signature_envelopes_monthly'
        WHEN 'imports' THEN 'ocr_pages_monthly'
        ELSE NULL
    END;
    IF quota_code IS NULL THEN
        RETURN 'allowed';
    END IF;
    resource_name := CASE quota_code
        WHEN 'ocr_pages_monthly' THEN 'ocr_credit'
        ELSE 'signature_credit'
    END;
    SELECT entitlement_row.limit_value INTO contracted
      FROM odca.subscriptions AS subscription
      JOIN odca.plan_entitlements AS entitlement_row
        ON entitlement_row.plan_version_id = subscription.plan_version_id
     WHERE subscription.tenant_id = requested_tenant_id
       AND subscription.status = 'active'
       AND entitlement_row.entitlement_code = quota_code
       AND entitlement_row.enabled
     LIMIT 1;
    IF contracted IS NULL OR contracted <= 0 THEN
        IF declares_modules THEN
            RETURN 'quota_exhausted';
        END IF;
        RETURN 'plan_restricted';
    END IF;
    used := odca.monthly_franchise_used(requested_tenant_id, resource_name);
    IF used >= contracted THEN
        RETURN 'quota_exhausted';
    END IF;
    RETURN 'allowed';
END;
$$;

CREATE OR REPLACE FUNCTION odca.claim_document_scan()
RETURNS TABLE(id uuid, tenant_id uuid, storage_key text, detected_type text)
LANGUAGE sql
SECURITY DEFINER
SET search_path = pg_catalog, odca
AS $$
    UPDATE odca.document_versions SET security_status = 'scanning'
     WHERE document_versions.id = (
        SELECT version.id
          FROM odca.document_versions AS version
          JOIN odca.tenants AS tenant ON tenant.id = version.tenant_id
         WHERE version.security_status IN ('pending', 'scan_failed')
           AND NOT tenant.is_deleted
           AND tenant.status <> 'suspended'
           AND odca.organization_feature_state(version.tenant_id, 'imports') = 'allowed'
         ORDER BY version.uploaded_at
         FOR UPDATE SKIP LOCKED
         LIMIT 1)
    RETURNING document_versions.id, document_versions.tenant_id, document_versions.storage_key, document_versions.detected_type;
$$;

CREATE OR REPLACE FUNCTION odca.claim_extraction_job(requested_lease uuid)
RETURNS TABLE(id uuid, tenant_id uuid, contract_id uuid, version_id uuid, lease_token uuid)
LANGUAGE sql
SECURITY DEFINER
SET search_path = pg_catalog, odca
AS $$
    UPDATE odca.extraction_jobs SET status = 'processing', attempt_count = attempt_count + 1, lease_token = requested_lease, lease_expires_at = now() + interval '5 minutes'
     WHERE extraction_jobs.id = (
        SELECT job.id
          FROM odca.extraction_jobs AS job
          JOIN odca.document_versions AS version
            ON version.id = job.version_id
           AND version.tenant_id = job.tenant_id
          JOIN odca.tenants AS tenant ON tenant.id = job.tenant_id
         WHERE (job.status = 'queued' OR (job.status = 'processing' AND job.lease_expires_at < now()))
           AND job.available_at <= now()
           AND job.attempt_count < job.max_attempts
           AND version.security_status = 'safe'
           AND NOT tenant.is_deleted
           AND tenant.status <> 'suspended'
           AND odca.organization_feature_state(job.tenant_id, 'imports') = 'allowed'
         ORDER BY job.requested_at
         FOR UPDATE OF job SKIP LOCKED
         LIMIT 1)
    RETURNING extraction_jobs.id, extraction_jobs.tenant_id, extraction_jobs.contract_id, extraction_jobs.version_id, extraction_jobs.lease_token;
$$;

CREATE OR REPLACE FUNCTION odca.platform_franchise_balances(actor uuid)
RETURNS TABLE(
    tenant_id uuid,
    resource text,
    unit text,
    contracted bigint,
    consumed bigint,
    reserved bigint,
    available bigint)
LANGUAGE plpgsql
STABLE
SECURITY DEFINER
SET search_path = pg_catalog, odca
AS $$
BEGIN
    PERFORM odca.assert_platform_actor(actor);
    RETURN QUERY
    SELECT subscription.tenant_id,
           entitlement.entitlement_code,
           CASE entitlement.entitlement_code
               WHEN 'ocr_pages_monthly' THEN 'pages'
               ELSE 'envelopes'
           END,
           COALESCE(entitlement.limit_value, 0),
           COALESCE(usage.consumed, 0),
           COALESCE(usage.reserved, 0),
           GREATEST(0, COALESCE(entitlement.limit_value, 0) - COALESCE(usage.consumed, 0) - COALESCE(usage.reserved, 0))
      FROM odca.subscriptions AS subscription
      JOIN odca.plan_entitlements AS entitlement
        ON entitlement.plan_version_id = subscription.plan_version_id
       AND entitlement.enabled
       AND entitlement.entitlement_code IN ('ocr_pages_monthly', 'signature_envelopes_monthly')
      LEFT JOIN LATERAL (
            SELECT COALESCE(SUM(movement.quantity) FILTER (WHERE movement.movement_type = 'consume'), 0)::bigint AS consumed,
                   GREATEST(0, COALESCE(SUM(movement.quantity) FILTER (WHERE movement.movement_type = 'reserve'), 0)
                       - COALESCE(SUM(movement.quantity) FILTER (WHERE movement.movement_type = 'release'), 0))::bigint AS reserved
              FROM odca.resource_movements AS movement
             WHERE movement.tenant_id = subscription.tenant_id
               AND movement.resource_type = CASE entitlement.entitlement_code
                       WHEN 'ocr_pages_monthly' THEN 'ocr_credit'
                       ELSE 'signature_credit'
                   END
               AND movement.occurred_at >= odca.franchise_period_start()
               AND movement.occurred_at < odca.franchise_period_start() + interval '1 month'
      ) AS usage ON true
     WHERE subscription.status = 'active';
END;
$$;

REVOKE ALL ON FUNCTION odca.franchise_period_start() FROM PUBLIC;
REVOKE ALL ON FUNCTION odca.monthly_franchise_used(uuid, text) FROM PUBLIC;
REVOKE ALL ON FUNCTION odca.consume_monthly_franchise(uuid, text, bigint, text, text, uuid, text) FROM PUBLIC;
REVOKE ALL ON FUNCTION odca.platform_franchise_balances(uuid) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION odca.franchise_period_start() TO odca_app;
GRANT EXECUTE ON FUNCTION odca.monthly_franchise_used(uuid, text) TO odca_app;
GRANT EXECUTE ON FUNCTION odca.consume_monthly_franchise(uuid, text, bigint, text, text, uuid, text) TO odca_app;
GRANT EXECUTE ON FUNCTION odca.platform_franchise_balances(uuid) TO odca_app;

INSERT INTO odca.schema_migrations(version, name, checksum)
VALUES (35, 'Monthly OCR and signature franchise metering', '35164be04924df0aa9f98c1d3f4c032e18621331893d6e0d4115c0193888ac03')
ON CONFLICT (version) DO NOTHING;
COMMIT;
-- ODCA-END 035

-- ODCA-MIGRATION 036 CHECKSUM dee99c2bd58a4ab786e306bdb11f4c0adfe9b78123a04edb4e63b19ee06fc230
BEGIN;
SELECT pg_advisory_xact_lock(hashtext('odca.schema.migrations'));

ALTER TABLE odca.users
    ADD COLUMN IF NOT EXISTS mfa_pending_since timestamptz;

ALTER TABLE odca.users
    DROP CONSTRAINT IF EXISTS users_mfa_state_ck;
ALTER TABLE odca.users
    ADD CONSTRAINT users_mfa_state_ck CHECK
    (
        (mfa_secret_protected IS NULL AND mfa_confirmed_at IS NULL AND mfa_last_accepted_time_step IS NULL AND mfa_pending_since IS NULL)
        OR (mfa_secret_protected IS NOT NULL AND (mfa_pending_since IS NULL OR mfa_confirmed_at IS NULL))
    );

UPDATE odca.users
   SET mfa_pending_since = updated_at
 WHERE mfa_secret_protected IS NOT NULL
   AND mfa_confirmed_at IS NULL
   AND mfa_pending_since IS NULL;

INSERT INTO odca.schema_migrations(version, name, checksum)
VALUES (36, 'MFA pending enrollment timestamp', 'dee99c2bd58a4ab786e306bdb11f4c0adfe9b78123a04edb4e63b19ee06fc230')
ON CONFLICT (version) DO NOTHING;
COMMIT;
-- ODCA-END 036
-- ODCA-MIGRATION 037 CHECKSUM c4ad0048ab5d93f6d831ecb500a32cdfa7eebb8607591c19c6316804ccf54a84
BEGIN;
SELECT pg_advisory_xact_lock(hashtext('odca.schema.migrations'));

CREATE UNIQUE INDEX IF NOT EXISTS contract_change_one_active_uq ON odca.contract_change_requests(tenant_id,contract_id) WHERE status NOT IN('cancelled','formalized','conflict') OR (status='formalized' AND application_status IN('not_applied','scheduled','failed'));

INSERT INTO odca.schema_migrations(version, name, checksum)
VALUES (37, 'Single active change request per contract', 'c4ad0048ab5d93f6d831ecb500a32cdfa7eebb8607591c19c6316804ccf54a84')
ON CONFLICT (version) DO NOTHING;
COMMIT;
-- ODCA-END 037
-- ODCA-MIGRATION 038 CHECKSUM 6db2939ff8aaa65cb313eb05b67944f93bdf2cdc7f4dc6ee13ca280c260ff203
BEGIN;
SELECT pg_advisory_xact_lock(hashtext('odca.schema.migrations'));

-- Contract lifecycle: archiving and closing are state transitions only.
-- Issued or signed records are never physically deleted.
ALTER TABLE odca.contracts
  ADD COLUMN archived_at timestamptz,
  ADD COLUMN archived_by uuid,
  ADD COLUMN archive_reason varchar(500),
  ADD COLUMN closed_at timestamptz,
  ADD COLUMN closed_by uuid,
  ADD COLUMN closed_on date,
  ADD COLUMN closure_reason varchar(2000),
  ADD CONSTRAINT contracts_archived_by_fk FOREIGN KEY(tenant_id,archived_by) REFERENCES odca.memberships(tenant_id,user_id),
  ADD CONSTRAINT contracts_closed_by_fk FOREIGN KEY(tenant_id,closed_by) REFERENCES odca.memberships(tenant_id,user_id),
  ADD CONSTRAINT contracts_archive_state_ck CHECK((archived_at IS NULL)=(archived_by IS NULL)),
  ADD CONSTRAINT contracts_closure_state_ck CHECK(
    closed_at IS NULL
    OR (closed_by IS NOT NULL AND closed_on IS NOT NULL AND closure_reason IS NOT NULL AND length(btrim(closure_reason)) BETWEEN 5 AND 2000));

CREATE INDEX IF NOT EXISTS contracts_lifecycle_ix ON odca.contracts(tenant_id,updated_at DESC);

-- Permissions for the consolidated contract list and lifecycle management.
INSERT INTO odca.permissions(code,description,delegable) VALUES
 ('tenant.contracts.read','Consultar contratos',true),
 ('tenant.contracts.manage','Gerenciar ciclo de vida de contratos',true)
ON CONFLICT(code) DO UPDATE SET description=EXCLUDED.description,delegable=EXCLUDED.delegable;

-- Existing tenants: roles that already read drafts can read the consolidated
-- list; roles that manage drafts (plus tenant-administrator/tenant-coordinator)
-- can manage the lifecycle.
INSERT INTO odca.role_permissions(role_id,permission_code)
SELECT r.id,'tenant.contracts.read'
  FROM odca.roles AS r
 WHERE r.scope_type='tenant'
   AND (r.code IN('tenant-administrator','tenant-coordinator')
        OR EXISTS(SELECT 1 FROM odca.role_permissions rp WHERE rp.role_id=r.id AND rp.permission_code='tenant.contract_drafts.read'))
ON CONFLICT DO NOTHING;
INSERT INTO odca.role_permissions(role_id,permission_code)
SELECT r.id,'tenant.contracts.manage'
  FROM odca.roles AS r
 WHERE r.scope_type='tenant'
   AND (r.code IN('tenant-administrator','tenant-coordinator')
        OR EXISTS(SELECT 1 FROM odca.role_permissions rp WHERE rp.role_id=r.id AND rp.permission_code='tenant.contract_drafts.manage'))
ON CONFLICT DO NOTHING;

-- Renewal priority uses the same vocabulary as contractual obligations.
ALTER TABLE odca.contract_change_requests
  ADD COLUMN priority varchar(12) NOT NULL DEFAULT 'normal' CHECK(priority IN('low','normal','high','critical'));
CREATE INDEX IF NOT EXISTS contract_change_priority_ix ON odca.contract_change_requests(tenant_id,effective_on,id) WHERE status NOT IN('cancelled','formalized','conflict');

-- In-house signature completion: participant signing and reminder resend.
ALTER TABLE odca.signature_preparations
  ADD COLUMN last_reminded_at timestamptz,
  ADD COLUMN last_reminded_by uuid,
  ADD CONSTRAINT signature_preparations_reminder_ck CHECK((last_reminded_at IS NULL)=(last_reminded_by IS NULL)),
  ADD CONSTRAINT signature_preparations_reminder_by_fk FOREIGN KEY(tenant_id,last_reminded_by) REFERENCES odca.memberships(tenant_id,user_id);

ALTER TABLE odca.signature_participants
  ADD COLUMN signed_at timestamptz,
  ADD COLUMN signed_by uuid,
  ADD CONSTRAINT signature_participants_signed_ck CHECK((signed_at IS NULL)=(signed_by IS NULL)),
  ADD CONSTRAINT signature_participants_signed_by_fk FOREIGN KEY(tenant_id,signed_by) REFERENCES odca.memberships(tenant_id,user_id);

ALTER TABLE odca.signature_preparation_events DROP CONSTRAINT IF EXISTS signature_preparation_events_event_type_check;
ALTER TABLE odca.signature_preparation_events
  ADD CONSTRAINT signature_preparation_events_event_type_ck CHECK(event_type IN('created','updated','participant_added','participant_changed','participant_removed','participant_reordered','confirmed','reopened','participant_signed','reminded'));

-- ODCA requests become an Enterprise capability from this version forward.
UPDATE odca.plan_entitlements SET enabled=false
 WHERE entitlement_code='module.reviews' AND enabled
   AND plan_version_id IN ('30000000-0000-0000-0000-000000000011','30000000-0000-0000-0000-000000000012');

-- New tenants inherit the same defaults as the backfill above.
CREATE OR REPLACE FUNCTION odca.ensure_tenant_standard_roles(target_tenant uuid)
RETURNS void
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, odca
AS $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM odca.tenants WHERE id = target_tenant AND NOT is_deleted) THEN
        RETURN;
    END IF;
    INSERT INTO odca.roles(scope_type, tenant_id, code, display_name, is_system)
    VALUES
        ('tenant', target_tenant, 'tenant-delegated-administrator', 'Administrador delegado', true),
        ('tenant', target_tenant, 'tenant-coordinator', 'Coordenador', true),
        ('tenant', target_tenant, 'tenant-member', 'Usuário', true)
    ON CONFLICT DO NOTHING;

    INSERT INTO odca.role_permissions(role_id, permission_code)
    SELECT role.id, permission.code
      FROM odca.roles AS role
      JOIN odca.permissions AS permission
        ON permission.code = ANY (
            CASE role.code
                WHEN 'tenant-delegated-administrator' THEN ARRAY[
                    'tenant.organization.read', 'tenant.team.read', 'tenant.team.manage',
                    'tenant.patients.read', 'tenant.templates.read', 'tenant.contract_drafts.read',
                    'tenant.contracts.read',
                    'tenant.documents.download', 'tenant.reviews.read', 'tenant.imports.read', 'tenant.billing.read']
                WHEN 'tenant-coordinator' THEN ARRAY[
                    'tenant.patients.read', 'tenant.templates.read', 'tenant.contract_drafts.read',
                    'tenant.contracts.read', 'tenant.contracts.manage',
                    'tenant.reviews.read', 'tenant.reviews.decide', 'tenant.reviews.history',
                    'tenant.obligations.read', 'tenant.obligations.read_all', 'tenant.obligations.assign']
                WHEN 'tenant-member' THEN ARRAY[
                    'tenant.patients.read', 'tenant.patients.manage', 'tenant.templates.read',
                    'tenant.contract_drafts.read', 'tenant.contract_drafts.manage',
                    'tenant.contracts.read',
                    'tenant.documents.download', 'tenant.reviews.read', 'tenant.reviews.request']
                ELSE ARRAY[]::text[]
            END)
     WHERE role.tenant_id = target_tenant
       AND role.code IN ('tenant-delegated-administrator', 'tenant-coordinator', 'tenant-member')
    ON CONFLICT DO NOTHING;
END;
$$;

INSERT INTO odca.schema_migrations(version,name,checksum)
VALUES (38,'Contract lifecycle, in-house signature completion and enterprise requests','6db2939ff8aaa65cb313eb05b67944f93bdf2cdc7f4dc6ee13ca280c260ff203')
ON CONFLICT (version) DO NOTHING;
COMMIT;
-- ODCA-END 038
-- ODCA-MIGRATION 039 CHECKSUM d2df475b1a17bb10a0c987f5e60160979a310449a247c382b88c9aba7efe7496
BEGIN;
SELECT pg_advisory_xact_lock(hashtext('odca.schema.migrations'));

-- Approved B.3 matrix: the operator executes draft work, signatures and
-- document downloads, but does not own the contract lifecycle. v038 derived
-- tenant.contracts.read/manage from contract_drafts permissions; remove them
-- from the standard tenant operator role. Administrator and client roles
-- keep their grants.
DELETE FROM odca.role_permissions AS rp
  USING odca.roles AS r
 WHERE rp.role_id = r.id
   AND r.scope_type = 'tenant'
   AND r.code = 'tenant-operator'
   AND rp.permission_code IN ('tenant.contracts.read','tenant.contracts.manage');

INSERT INTO odca.schema_migrations(version,name,checksum)
VALUES (39,'Operator contract-lifecycle permissions restricted per approved B.3 matrix','d2df475b1a17bb10a0c987f5e60160979a310449a247c382b88c9aba7efe7496')
ON CONFLICT (version) DO NOTHING;
COMMIT;
-- ODCA-END 039
-- ODCA-MIGRATION 040 CHECKSUM 6b44ba8430ca7e38e40f54d37c25b3e08f4059e1acc89c632618bd7a474c8d9a
BEGIN;
SELECT pg_advisory_xact_lock(hashtext('odca.schema.migrations'));

-- B.3.3 (G5): configurable renewal reminder cadence per tenant (default
-- 30/15/7). Three day offsets (1..365) used by the renewal center to surface
-- the active reminder window. Additive column; no permission changes (D4).
ALTER TABLE odca.tenants
  ADD COLUMN renewal_reminder_days text[] NOT NULL DEFAULT '{30,15,7}';

ALTER TABLE odca.tenants
  ADD CONSTRAINT tenants_renewal_reminder_days_ck
  CHECK (cardinality(renewal_reminder_days) = 3
    AND renewal_reminder_days[1] ~ '^[0-9]{1,3}$'
    AND renewal_reminder_days[2] ~ '^[0-9]{1,3}$'
    AND renewal_reminder_days[3] ~ '^[0-9]{1,3}$'
    AND renewal_reminder_days[1]::int BETWEEN 1 AND 365
    AND renewal_reminder_days[2]::int BETWEEN 1 AND 365
    AND renewal_reminder_days[3]::int BETWEEN 1 AND 365);

INSERT INTO odca.schema_migrations(version,name,checksum)
VALUES (40,'Configurable renewal reminder cadence on tenants','6b44ba8430ca7e38e40f54d37c25b3e08f4059e1acc89c632618bd7a474c8d9a')
ON CONFLICT (version) DO NOTHING;
COMMIT;
-- ODCA-END 040
-- ODCA-MIGRATION 041 CHECKSUM ef78cb212164f5d1ec3261c0b4424b86a4d8fc48cd68c579f9213b8e8a5e411d
BEGIN;
SELECT pg_advisory_xact_lock(hashtext('odca.schema.migrations'));

-- B.3.4 (D1): per-tenant activity profile driving contract field overlays
-- (general / therapy_clinic / plastic_surgery). The plastic_surgery profile is
-- gated to the Enterprise plan at the application layer; ODCA approval of
-- surgical models lands with section C. Additive column; no permission changes.
ALTER TABLE odca.tenants
    ADD COLUMN IF NOT EXISTS activity_profile text NOT NULL DEFAULT 'general';

ALTER TABLE odca.tenants
    DROP CONSTRAINT IF EXISTS tenants_activity_profile_ck;
ALTER TABLE odca.tenants
    ADD CONSTRAINT tenants_activity_profile_ck
    CHECK (activity_profile IN ('general', 'therapy_clinic', 'plastic_surgery'));

INSERT INTO odca.schema_migrations(version,name,checksum)
VALUES (41,'Per-tenant activity profile for contract field rules','ef78cb212164f5d1ec3261c0b4424b86a4d8fc48cd68c579f9213b8e8a5e411d')
ON CONFLICT (version) DO NOTHING;
COMMIT;
-- ODCA-END 041

-- ODCA-MIGRATION 042 CHECKSUM 901a50fc9b4cf02bc18189b602df973102ae754f27926511700281eadb75ee9e
BEGIN;
SELECT pg_advisory_xact_lock(hashtext('odca.schema.migrations'));

-- Seção C (D-C1/D-C2/D-C3): solicitações cliente→ODCA com SLA por plano/serviço/
-- prioridade, máquina de estados (aberta→triagem→em_atendimento→aguardando_cliente→
-- resolvida→encerrada + cancelamento com motivo), pausa/retomada justificada do
-- relógio, reatribuição sem reiniciar o relógio e fila de aprovação ODCA dos modelos
-- cirúrgicos (substitui o bloqueio interino approval.odca.required da seção B.3.4).
-- Vocabulário de códigos em pt-BR (statuses/serviços/ações) por ser a palavra literal
-- do plano de execução; UI exibe rótulos com acentos.

-- Permissões do tenant sobre a central de solicitações à ODCA.
INSERT INTO odca.permissions(code,description,delegable) VALUES
 ('tenant.solicitations.read','Consultar solicitações à ODCA e prazos de SLA',true),
 ('tenant.solicitations.manage','Abrir, responder, cancelar e encerrar solicitações à ODCA',true)
ON CONFLICT(code) DO UPDATE SET description=EXCLUDED.description,delegable=EXCLUDED.delegable;
INSERT INTO odca.role_permissions(role_id,permission_code)
SELECT r.id,p.code FROM odca.roles r CROSS JOIN odca.permissions p
WHERE r.scope_type='tenant' AND (r.code IN('tenant-administrator','tenant-client')
   OR EXISTS(SELECT 1 FROM odca.role_permissions held WHERE held.role_id=r.id AND held.permission_code='tenant.reviews.request'))
  AND p.code LIKE 'tenant.solicitations.%'
ON CONFLICT DO NOTHING;

-- Políticas de SLA por plano/serviço/prioridade (fuso e calendário explícitos;
-- calendário dias_uteis = seg–sex na janela comercial do fuso indicado).
CREATE TABLE IF NOT EXISTS odca.sla_policies(
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
 plan_code text NOT NULL CHECK(plan_code IN('basic','intermediate','enterprise')),
 service text NOT NULL CHECK(service IN('revisao','adaptacao','esclarecimento')),
 priority text NOT NULL CHECK(priority IN('baixa','normal','alta','critica')),
 timezone text NOT NULL DEFAULT 'America/Sao_Paulo',
 calendar text NOT NULL DEFAULT 'dias_uteis' CHECK(calendar IN('dias_uteis','continuo')),
 business_start time NOT NULL DEFAULT '09:00',
 business_end time NOT NULL DEFAULT '18:00',
 first_response_minutes integer NOT NULL CHECK(first_response_minutes>0),
 resolution_minutes integer NOT NULL CHECK(resolution_minutes>=first_response_minutes),
 enabled boolean NOT NULL DEFAULT true,
 created_at timestamptz NOT NULL DEFAULT now(),
 CHECK(business_start<business_end),
 UNIQUE(plan_code,service,priority));
INSERT INTO odca.sla_policies(plan_code,service,priority,timezone,calendar,business_start,business_end,first_response_minutes,resolution_minutes)
SELECT pl.plan_code,b.service,b.priority,'America/Sao_Paulo','dias_uteis','09:00','18:00',
 b.fr * CASE pl.plan_code WHEN 'enterprise' THEN 1 WHEN 'intermediate' THEN 2 ELSE 4 END,
 b.res * CASE pl.plan_code WHEN 'enterprise' THEN 1 WHEN 'intermediate' THEN 2 ELSE 4 END
FROM (VALUES ('enterprise'),('intermediate'),('basic')) pl(plan_code)
CROSS JOIN (VALUES
 ('revisao','baixa',1440,4320),('revisao','normal',480,1440),('revisao','alta',240,720),('revisao','critica',60,240),
 ('adaptacao','baixa',2880,10080),('adaptacao','normal',960,2880),('adaptacao','alta',480,1440),('adaptacao','critica',120,480),
 ('esclarecimento','baixa',1440,2880),('esclarecimento','normal',240,720),('esclarecimento','alta',120,480),('esclarecimento','critica',60,180)
) b(service,priority,fr,res)
ON CONFLICT(plan_code,service,priority) DO NOTHING;

-- Solicitações e suas correntes (mensagens, eventos, pausas do relógio de SLA).
CREATE TABLE odca.solicitations(
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(), tenant_id uuid NOT NULL REFERENCES odca.tenants(id),
 number bigint NOT NULL CHECK(number>0),
 service text NOT NULL CHECK(service IN('revisao','adaptacao','esclarecimento')),
 priority text NOT NULL CHECK(priority IN('baixa','normal','alta','critica')),
 status text NOT NULL DEFAULT 'aberta' CHECK(status IN('aberta','triagem','em_atendimento','aguardando_cliente','resolvida','encerrada','cancelada')),
 subject varchar(200) NOT NULL, body varchar(8000) NOT NULL,
 opened_by uuid NOT NULL, assignee_user_id uuid REFERENCES odca.users(id),
 row_version bigint NOT NULL DEFAULT 1 CHECK(row_version>0), idempotency_key uuid NOT NULL,
 opened_at timestamptz NOT NULL DEFAULT now(), updated_at timestamptz NOT NULL DEFAULT now(),
 triaged_at timestamptz, first_response_at timestamptz, resolved_at timestamptz, closed_at timestamptz, cancelled_at timestamptz,
 cancellation_reason varchar(2000),
 sla_policy_id uuid REFERENCES odca.sla_policies(id),
 sla_timezone text NOT NULL DEFAULT 'America/Sao_Paulo',
 sla_calendar text NOT NULL DEFAULT 'dias_uteis',
 sla_business_start time NOT NULL DEFAULT '09:00', sla_business_end time NOT NULL DEFAULT '18:00',
 first_response_due_at timestamptz, resolution_due_at timestamptz,
 resume_status text, paused_at timestamptz, pause_reason varchar(2000),
 first_response_breach_at timestamptz, resolution_breach_at timestamptz,
 UNIQUE(tenant_id,id),UNIQUE(tenant_id,number),UNIQUE(tenant_id,idempotency_key),
 FOREIGN KEY(tenant_id,opened_by) REFERENCES odca.memberships(tenant_id,user_id),
 CONSTRAINT solicitations_cancel_ck CHECK(status<>'cancelada' OR (cancelled_at IS NOT NULL AND length(btrim(coalesce(cancellation_reason,'')))>0)),
 CONSTRAINT solicitations_resolved_ck CHECK(status NOT IN('resolvida','encerrada') OR resolved_at IS NOT NULL),
 CONSTRAINT solicitations_encerrada_ck CHECK(status<>'encerrada' OR closed_at IS NOT NULL),
 CONSTRAINT solicitations_await_ck CHECK(status<>'aguardando_cliente' OR (paused_at IS NOT NULL AND resume_status IS NOT NULL)),
 CONSTRAINT solicitations_pause_ck CHECK(paused_at IS NULL OR status IN('aberta','triagem','em_atendimento','aguardando_cliente')),
 CONSTRAINT solicitations_resume_ck CHECK(resume_status IS NULL OR resume_status IN('aberta','triagem','em_atendimento')),
 CONSTRAINT solicitations_clock_ck CHECK(resolution_due_at IS NULL OR first_response_due_at IS NULL OR resolution_due_at>=first_response_due_at));
CREATE TABLE odca.solicitation_messages(
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(), tenant_id uuid NOT NULL, solicitation_id uuid NOT NULL,
 author_user_id uuid NOT NULL REFERENCES odca.users(id),
 author_kind text NOT NULL CHECK(author_kind IN('cliente','odca')),
 body varchar(8000) NOT NULL CHECK(length(btrim(body))>0),
 created_at timestamptz NOT NULL DEFAULT now(),
 UNIQUE(tenant_id,id),FOREIGN KEY(tenant_id,solicitation_id) REFERENCES odca.solicitations(tenant_id,id));
CREATE TABLE odca.solicitation_events(
 id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY, tenant_id uuid NOT NULL, solicitation_id uuid NOT NULL,
 actor_user_id uuid REFERENCES odca.users(id),
 event_type varchar(60) NOT NULL, details jsonb NOT NULL DEFAULT '{}'::jsonb,
 occurred_at timestamptz NOT NULL DEFAULT now(),
 FOREIGN KEY(tenant_id,solicitation_id) REFERENCES odca.solicitations(tenant_id,id));
CREATE TABLE odca.solicitation_pauses(
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(), tenant_id uuid NOT NULL, solicitation_id uuid NOT NULL,
 started_at timestamptz NOT NULL DEFAULT now(), ended_at timestamptz,
 reason varchar(2000) NOT NULL CHECK(length(btrim(reason))>0),
 started_by uuid NOT NULL REFERENCES odca.users(id), ended_by uuid REFERENCES odca.users(id),
 UNIQUE(tenant_id,id),FOREIGN KEY(tenant_id,solicitation_id) REFERENCES odca.solicitations(tenant_id,id),
 CHECK(ended_at IS NULL OR ended_at>=started_at));
CREATE UNIQUE INDEX solicitation_pauses_open_ux ON odca.solicitation_pauses(solicitation_id) WHERE ended_at IS NULL;
CREATE INDEX solicitations_tenant_status_ix ON odca.solicitations(tenant_id,status,opened_at DESC);
CREATE INDEX solicitations_queue_ix ON odca.solicitations(status,resolution_due_at) WHERE status IN('aberta','triagem','em_atendimento','aguardando_cliente');
CREATE INDEX solicitation_messages_solicitation_ix ON odca.solicitation_messages(solicitation_id,created_at);
CREATE INDEX solicitation_events_solicitation_ix ON odca.solicitation_events(solicitation_id,occurred_at);

-- Decisão ODCA sobre modelos oficiais que exigem aprovação (ex.: cirúrgicos).
-- Unidade: organização × chave oficial; 'aprovado' libera o bloqueio de prontidão.
CREATE TABLE odca.template_approvals(
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(), tenant_id uuid NOT NULL REFERENCES odca.tenants(id),
 official_key text NOT NULL CHECK(official_key ~ '^[a-z0-9]+(-[a-z0-9]+)*$'),
 decision text NOT NULL CHECK(decision IN('aprovado','reprovado')),
 note varchar(2000) NOT NULL DEFAULT '',
 decided_by uuid NOT NULL REFERENCES odca.users(id),
 decided_at timestamptz NOT NULL DEFAULT now(), updated_at timestamptz NOT NULL DEFAULT now(),
 UNIQUE(tenant_id,id),UNIQUE(tenant_id,official_key),
 CHECK(decision='aprovado' OR length(btrim(note))>=5));

ALTER TABLE odca.solicitations ENABLE ROW LEVEL SECURITY; ALTER TABLE odca.solicitations FORCE ROW LEVEL SECURITY;
ALTER TABLE odca.solicitation_messages ENABLE ROW LEVEL SECURITY; ALTER TABLE odca.solicitation_messages FORCE ROW LEVEL SECURITY;
ALTER TABLE odca.solicitation_events ENABLE ROW LEVEL SECURITY; ALTER TABLE odca.solicitation_events FORCE ROW LEVEL SECURITY;
ALTER TABLE odca.solicitation_pauses ENABLE ROW LEVEL SECURITY; ALTER TABLE odca.solicitation_pauses FORCE ROW LEVEL SECURITY;
ALTER TABLE odca.template_approvals ENABLE ROW LEVEL SECURITY; ALTER TABLE odca.template_approvals FORCE ROW LEVEL SECURITY;
DO $policy$ DECLARE n text; BEGIN FOREACH n IN ARRAY ARRAY['solicitations','solicitation_messages','solicitation_events','solicitation_pauses','template_approvals'] LOOP
 EXECUTE format('CREATE POLICY tenant_isolation ON odca.%I TO odca_app USING (tenant_id=nullif(current_setting(''odca.tenant_id'',true),'''')::uuid) WITH CHECK (tenant_id=nullif(current_setting(''odca.tenant_id'',true),'''')::uuid)',n);
END LOOP; END $policy$;

GRANT SELECT ON odca.sla_policies,odca.solicitations,odca.solicitation_messages,odca.solicitation_events,odca.solicitation_pauses,odca.template_approvals TO odca_app;

-- Relógio de SLA: soma minutos comerciais no fuso/calendário da política
-- (dias_uteis pula fins de semana; horario fora da janela avanca para a janela).
CREATE OR REPLACE FUNCTION odca.sla_add_business_minutes(t_from timestamptz,p_minutes integer,p_tz text,p_calendar text,p_start time,p_end time)
RETURNS timestamptz LANGUAGE plpgsql STABLE SET search_path=pg_catalog,odca AS $fn$
DECLARE v timestamptz:=t_from; rest integer:=greatest(coalesce(p_minutes,0),0);
 wall timestamp; tod integer; start_min integer; end_min integer; avail integer;
BEGIN
 IF rest<=0 THEN RETURN t_from; END IF;
 IF p_calendar='continuo' THEN RETURN t_from + make_interval(mins=>rest); END IF;
 start_min := extract(hour from p_start)::int*60 + extract(minute from p_start)::int;
 end_min := extract(hour from p_end)::int*60 + extract(minute from p_end)::int;
 WHILE rest>0 LOOP
  wall := v AT TIME ZONE p_tz;
  IF extract(isodow from wall)::int>=6 THEN v := ((wall::date+1)+p_start) AT TIME ZONE p_tz; CONTINUE; END IF;
  tod := extract(hour from wall)::int*60 + extract(minute from wall)::int;
  IF tod<start_min THEN v := (wall::date+p_start) AT TIME ZONE p_tz; CONTINUE; END IF;
  IF tod>=end_min THEN v := ((wall::date+1)+p_start) AT TIME ZONE p_tz; CONTINUE; END IF;
  avail := end_min - tod;
  IF rest<=avail THEN RETURN v + make_interval(mins=>rest); END IF;
  rest := rest - avail;
  v := ((wall::date+1)+p_start) AT TIME ZONE p_tz;
 END LOOP;
 RETURN v;
END $fn$;

-- Snapshot jsonb canonico de uma solicitacao (payload de todas as mutacoes).
CREATE OR REPLACE FUNCTION odca.solicitation_payload(p_solicitation uuid)
RETURNS jsonb LANGUAGE sql STABLE SECURITY DEFINER SET search_path=pg_catalog,odca AS $$
 SELECT jsonb_build_object(
  'id',s.id,'tenantId',s.tenant_id,'protocol','SOL-'||lpad(s.number::text,6,'0'),
  'service',s.service,'priority',s.priority,'status',s.status,'subject',s.subject,'body',s.body,
  'openedBy',s.opened_by,'openedByName',op.display_name,
  'assigneeUserId',s.assignee_user_id,'assigneeName',asg.display_name,
  'rowVersion',s.row_version,'openedAt',s.opened_at,'updatedAt',s.updated_at,
  'triagedAt',s.triaged_at,'firstResponseAt',s.first_response_at,'resolvedAt',s.resolved_at,
  'closedAt',s.closed_at,'cancelledAt',s.cancelled_at,'cancellationReason',s.cancellation_reason,
  'paused',s.paused_at IS NOT NULL,'pausedAt',s.paused_at,'pauseReason',s.pause_reason,
  'sla',jsonb_build_object('timezone',s.sla_timezone,'calendar',s.sla_calendar,
   'businessStart',to_char(s.sla_business_start,'HH24:MI'),'businessEnd',to_char(s.sla_business_end,'HH24:MI'),
   'firstResponseDueAt',s.first_response_due_at,'resolutionDueAt',s.resolution_due_at,
   'firstResponseBreachAt',s.first_response_breach_at,'resolutionBreachAt',s.resolution_breach_at))
 FROM odca.solicitations s
 LEFT JOIN odca.users op ON op.id=s.opened_by
 LEFT JOIN odca.users asg ON asg.id=s.assignee_user_id
 WHERE s.id=p_solicitation;
$$;

-- Abertura (D-C3): apenas planos Enterprise; chave de idempotencia por organizacao;
-- numeracao sequencial por organizacao; snapshot da politica e vencimentos calculados.
CREATE OR REPLACE FUNCTION odca.solicitations_open(p_tenant uuid,p_actor uuid,p_service text,p_priority text,p_subject text,p_body text,p_idempotency uuid)
RETURNS jsonb LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,odca AS $fn$
DECLARE v_subject text:=btrim(coalesce(p_subject,'')); v_body text:=btrim(coalesce(p_body,''));
 v_plan text; v_policy odca.sla_policies%rowtype; v_number bigint; v_existing uuid; v_id uuid;
BEGIN
 IF NOT EXISTS(SELECT 1 FROM odca.users WHERE id=p_actor AND is_platform_administrator AND NOT is_deleted)
  AND odca.tenant_actor_has_permission(p_actor,p_tenant,'tenant.solicitations.manage') IS DISTINCT FROM true
 THEN RETURN jsonb_build_object('error','Permissao insuficiente.','code','solicitations.permission'); END IF;
 IF length(v_subject)<3 OR length(v_subject)>200 THEN RETURN jsonb_build_object('error','Assunto deve ter entre 3 e 200 caracteres.','code','solicitations.validation_subject'); END IF;
 IF length(v_body)<10 OR length(v_body)>8000 THEN RETURN jsonb_build_object('error','Descricao deve ter entre 10 e 8000 caracteres.','code','solicitations.validation_body'); END IF;
 IF p_service IS NULL OR p_service NOT IN('revisao','adaptacao','esclarecimento')
  OR p_priority IS NULL OR p_priority NOT IN('baixa','normal','alta','critica') OR p_idempotency IS NULL
 THEN RETURN jsonb_build_object('error','Servico, prioridade ou chave de idempotencia invalidos.','code','solicitations.validation_fields'); END IF;
 SELECT pv.code INTO v_plan FROM odca.subscriptions s JOIN odca.plan_versions pv ON pv.id=s.plan_version_id
  WHERE s.tenant_id=p_tenant AND s.status='active' AND pv.code='enterprise';
 IF v_plan IS NULL THEN RETURN jsonb_build_object('error','Central de solicitações disponível apenas no plano Enterprise.','code','solicitations.plan.required'); END IF;
 SELECT id INTO v_existing FROM odca.solicitations WHERE tenant_id=p_tenant AND idempotency_key=p_idempotency;
 IF v_existing IS NOT NULL THEN RETURN odca.solicitation_payload(v_existing); END IF;
 PERFORM pg_advisory_xact_lock(hashtext('odca.solicitations'),hashtext(p_tenant::text));
 SELECT coalesce(max(number),0)+1 INTO v_number FROM odca.solicitations WHERE tenant_id=p_tenant;
 SELECT * INTO STRICT v_policy FROM odca.sla_policies
  WHERE plan_code=v_plan AND service=p_service AND priority=p_priority AND enabled;
 INSERT INTO odca.solicitations(tenant_id,number,service,priority,subject,body,opened_by,idempotency_key,
  sla_policy_id,sla_timezone,sla_calendar,sla_business_start,sla_business_end,first_response_due_at,resolution_due_at)
 VALUES(p_tenant,v_number,p_service,p_priority,v_subject,v_body,p_actor,p_idempotency,
  v_policy.id,v_policy.timezone,v_policy.calendar,v_policy.business_start,v_policy.business_end,
  odca.sla_add_business_minutes(now(),v_policy.first_response_minutes,v_policy.timezone,v_policy.calendar,v_policy.business_start,v_policy.business_end),
  odca.sla_add_business_minutes(now(),v_policy.resolution_minutes,v_policy.timezone,v_policy.calendar,v_policy.business_start,v_policy.business_end))
 RETURNING id INTO v_id;
 INSERT INTO odca.solicitation_events(tenant_id,solicitation_id,actor_user_id,event_type,details)
 VALUES(p_tenant,v_id,p_actor,'abertura',jsonb_build_object('servico',p_service,'prioridade',p_priority));
 RETURN odca.solicitation_payload(v_id);
EXCEPTION WHEN unique_violation THEN
 SELECT id INTO v_existing FROM odca.solicitations WHERE tenant_id=p_tenant AND idempotency_key=p_idempotency;
 IF v_existing IS NOT NULL THEN RETURN odca.solicitation_payload(v_existing); END IF; RAISE;
END $fn$;

-- Maquina de estados + relógio de SLA. Acoes ODCA: triar, iniciar, aguardar, pausar,
-- retomar, resolver, reatribuir. Ambos os lados: encerrar (resolvida), cancelar (motivo).
-- Primeira resposta: qualquer movimentacao ODCA apos a triagem (iniciar/aguardar/
-- pausar/resolver ou mensagem). Pausa congela o relógio; retomada soma o tempo real
-- decorrido aos vencimentos pendentes; reatribuicao jamais toca no relógio.
CREATE OR REPLACE FUNCTION odca.solicitations_transition(p_tenant uuid,p_actor uuid,p_solicitation uuid,p_action text,p_text text DEFAULT NULL,p_priority text DEFAULT NULL,p_assignee uuid DEFAULT NULL,p_expected_version bigint DEFAULT NULL)
RETURNS jsonb LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,odca AS $fn$
DECLARE s odca.solicitations%rowtype; v_platform boolean; v_text text:=nullif(btrim(coalesce(p_text,'')),'');
 v_status text; v_priority text; v_assignee uuid; v_resume text; v_paused_at timestamptz; v_pause_reason text;
 v_triaged_at timestamptz; v_first_response_at timestamptz; v_resolved_at timestamptz; v_closed_at timestamptz;
 v_cancelled_at timestamptz; v_cancellation_reason text; v_fr_due timestamptz; v_res_due timestamptz; v_policy_id uuid;
 v_event text; v_details jsonb:='{}'::jsonb; v_touch_first boolean:=false; v_end_pause boolean:=false;
 v_shift interval; v_new text; v_plan text; v_newpolicy odca.sla_policies%rowtype; v_found boolean;
BEGIN
 SELECT * INTO s FROM odca.solicitations WHERE tenant_id=p_tenant AND id=p_solicitation FOR UPDATE;
 IF NOT FOUND THEN RETURN jsonb_build_object('error','Solicitacao nao encontrada.','code','solicitations.not_found'); END IF;
 v_platform := EXISTS(SELECT 1 FROM odca.users WHERE id=p_actor AND is_platform_administrator AND NOT is_deleted);
 IF NOT v_platform AND odca.tenant_actor_has_permission(p_actor,p_tenant,'tenant.solicitations.manage') IS DISTINCT FROM true
 THEN RETURN jsonb_build_object('error','Permissao insuficiente.','code','solicitations.permission'); END IF;
 IF p_expected_version IS NULL OR p_expected_version<>s.row_version
 THEN RETURN jsonb_build_object('error','A solicitacao foi atualizada por outro usuario.','code','solicitations.stale','currentRowVersion',s.row_version); END IF;
 v_status:=s.status; v_priority:=s.priority; v_assignee:=s.assignee_user_id; v_resume:=s.resume_status;
 v_paused_at:=s.paused_at; v_pause_reason:=s.pause_reason; v_triaged_at:=s.triaged_at;
 v_first_response_at:=s.first_response_at; v_resolved_at:=s.resolved_at; v_closed_at:=s.closed_at;
 v_cancelled_at:=s.cancelled_at; v_cancellation_reason:=s.cancellation_reason;
 v_fr_due:=s.first_response_due_at; v_res_due:=s.resolution_due_at; v_policy_id:=s.sla_policy_id;

 IF p_action='triar' THEN
  IF NOT v_platform THEN RETURN jsonb_build_object('error','Acao exclusiva da ODCA.','code','solicitations.permission_odca'); END IF;
  IF s.status<>'aberta' THEN RETURN jsonb_build_object('error','Somente solicitacoes abertas podem ir para triagem.','code','solicitations.transition'); END IF;
  v_status:='triagem'; v_triaged_at:=now(); v_assignee:=coalesce(p_assignee,p_actor);
  IF p_assignee IS NOT NULL AND NOT EXISTS(SELECT 1 FROM odca.users WHERE id=p_assignee AND is_platform_administrator AND NOT is_deleted)
  THEN RETURN jsonb_build_object('error','Responsavel deve ser administrador da plataforma.','code','solicitations.assignee_invalid'); END IF;
  v_details:=jsonb_build_object('responsavel',v_assignee);
  v_new:=nullif(p_priority,'');
  IF v_new IS NOT NULL THEN
   IF v_new NOT IN('baixa','normal','alta','critica') THEN RETURN jsonb_build_object('error','Prioridade invalida.','code','solicitations.validation_priority'); END IF;
   IF v_new<>s.priority THEN
    v_details:=v_details||jsonb_build_object('prioridadeAnterior',s.priority,'prioridadeNova',v_new);
    SELECT pv.code INTO v_plan FROM odca.subscriptions sb JOIN odca.plan_versions pv ON pv.id=sb.plan_version_id
     WHERE sb.tenant_id=p_tenant AND sb.status='active';
    v_found:=false;
    IF v_plan IS NOT NULL THEN
     SELECT * INTO v_newpolicy FROM odca.sla_policies
      WHERE plan_code=v_plan AND service=s.service AND priority=v_new AND enabled;
     v_found:=FOUND;
    END IF;
    IF v_found AND v_first_response_at IS NULL AND v_paused_at IS NULL
       AND s.first_response_breach_at IS NULL AND s.resolution_breach_at IS NULL THEN
     v_priority:=v_new; v_policy_id:=v_newpolicy.id;
     v_fr_due:=odca.sla_add_business_minutes(s.opened_at,v_newpolicy.first_response_minutes,v_newpolicy.timezone,v_newpolicy.calendar,v_newpolicy.business_start,v_newpolicy.business_end);
     v_res_due:=odca.sla_add_business_minutes(s.opened_at,v_newpolicy.resolution_minutes,v_newpolicy.timezone,v_newpolicy.calendar,v_newpolicy.business_start,v_newpolicy.business_end);
     v_details:=v_details||jsonb_build_object('relogioRecalculado',true);
    ELSE
     v_details:=v_details||jsonb_build_object('relogioRecalculado',false);
    END IF;
   END IF;
  END IF;
  v_event:='triagem';
 ELSIF p_action='iniciar' THEN
  IF NOT v_platform THEN RETURN jsonb_build_object('error','Acao exclusiva da ODCA.','code','solicitations.permission_odca'); END IF;
  IF s.status<>'triagem' THEN RETURN jsonb_build_object('error','Somente solicitacoes em triagem iniciam atendimento.','code','solicitations.transition'); END IF;
  v_status:='em_atendimento'; v_event:='atendimento_inicio'; v_touch_first:=true;
 ELSIF p_action='aguardar' THEN
  IF NOT v_platform THEN RETURN jsonb_build_object('error','Acao exclusiva da ODCA.','code','solicitations.permission_odca'); END IF;
  IF s.status NOT IN('triagem','em_atendimento') OR s.paused_at IS NOT NULL THEN RETURN jsonb_build_object('error','Aguardando cliente exige triagem ou atendimento em curso sem pausa ativa.','code','solicitations.transition'); END IF;
  IF v_text IS NULL THEN RETURN jsonb_build_object('error','Informe a justificativa da espera.','code','solicitations.validation_reason'); END IF;
  v_resume:=s.status; v_status:='aguardando_cliente'; v_paused_at:=now(); v_pause_reason:=v_text;
  v_event:='aguardando_cliente'; v_details:=jsonb_build_object('justificativa',v_text); v_touch_first:=true;
 ELSIF p_action='pausar' THEN
  IF NOT v_platform THEN RETURN jsonb_build_object('error','Acao exclusiva da ODCA.','code','solicitations.permission_odca'); END IF;
  IF s.status NOT IN('aberta','triagem','em_atendimento') OR s.paused_at IS NOT NULL THEN RETURN jsonb_build_object('error','Relógio já pausado ou estado não permite pausa.','code','solicitations.transition'); END IF;
  IF v_text IS NULL THEN RETURN jsonb_build_object('error','Toda pausa exige justificativa.','code','solicitations.validation_reason'); END IF;
  v_resume:=s.status; v_paused_at:=now(); v_pause_reason:=v_text;
  v_event:='pausa_sl'; v_details:=jsonb_build_object('justificativa',v_text); v_touch_first:=true;
 ELSIF p_action='retomar' THEN
  IF NOT v_platform THEN RETURN jsonb_build_object('error','Acao exclusiva da ODCA.','code','solicitations.permission_odca'); END IF;
  IF s.paused_at IS NULL THEN RETURN jsonb_build_object('error','Nenhum relógio pausado.','code','solicitations.transition'); END IF;
  v_shift:=now()-s.paused_at;
  IF v_first_response_at IS NULL AND v_fr_due IS NOT NULL THEN v_fr_due:=v_fr_due+v_shift; END IF;
  IF v_res_due IS NOT NULL THEN v_res_due:=v_res_due+v_shift; END IF;
  IF s.status='aguardando_cliente' THEN v_status:=coalesce(s.resume_status,'em_atendimento'); END IF;
  v_resume:=NULL; v_paused_at:=NULL; v_pause_reason:=NULL; v_end_pause:=true;
  v_event:='retomada_sl'; v_details:=jsonb_build_object('duracaoMinutos',floor(extract(epoch from v_shift)/60)::int);
 ELSIF p_action='resolver' THEN
  IF NOT v_platform THEN RETURN jsonb_build_object('error','Acao exclusiva da ODCA.','code','solicitations.permission_odca'); END IF;
  IF s.status NOT IN('triagem','em_atendimento','aguardando_cliente') THEN RETURN jsonb_build_object('error','Estado atual nao permite resolucao.','code','solicitations.transition'); END IF;
  IF v_text IS NULL THEN RETURN jsonb_build_object('error','A resolucao exige resumo da solucao.','code','solicitations.validation_resolution'); END IF;
  IF s.paused_at IS NOT NULL THEN
   v_shift:=now()-s.paused_at;
   IF v_first_response_at IS NULL AND v_fr_due IS NOT NULL THEN v_fr_due:=v_fr_due+v_shift; END IF;
   IF v_res_due IS NOT NULL THEN v_res_due:=v_res_due+v_shift; END IF;
  END IF;
  v_status:='resolvida'; v_resolved_at:=now(); v_resume:=NULL; v_paused_at:=NULL; v_pause_reason:=NULL; v_end_pause:=true;
  v_event:='resolucao'; v_details:=jsonb_build_object('solucao',v_text); v_touch_first:=true;
 ELSIF p_action='reatribuir' THEN
  IF NOT v_platform THEN RETURN jsonb_build_object('error','Acao exclusiva da ODCA.','code','solicitations.permission_odca'); END IF;
  IF s.status NOT IN('triagem','em_atendimento','aguardando_cliente') THEN RETURN jsonb_build_object('error','Reatribuicao indisponivel neste estado.','code','solicitations.transition'); END IF;
  IF p_assignee IS NULL OR NOT EXISTS(SELECT 1 FROM odca.users WHERE id=p_assignee AND is_platform_administrator AND NOT is_deleted)
  THEN RETURN jsonb_build_object('error','Informe um responsavel valido da plataforma.','code','solicitations.assignee_invalid'); END IF;
  v_details:=jsonb_build_object('de',s.assignee_user_id,'para',p_assignee); v_assignee:=p_assignee; v_event:='reatribuicao';
 ELSIF p_action='encerrar' THEN
  IF s.status<>'resolvida' THEN RETURN jsonb_build_object('error','Somente solicitacoes resolvidas sao encerradas.','code','solicitations.transition'); END IF;
  v_status:='encerrada'; v_closed_at:=now(); v_event:='encerramento';
  IF v_text IS NOT NULL THEN v_details:=jsonb_build_object('observacao',v_text); END IF;
 ELSIF p_action='cancelar' THEN
  IF s.status NOT IN('aberta','triagem','em_atendimento','aguardando_cliente') THEN RETURN jsonb_build_object('error','Estado atual nao permite cancelamento.','code','solicitations.transition'); END IF;
  IF v_text IS NULL THEN RETURN jsonb_build_object('error','O cancelamento exige motivo.','code','solicitations.validation_reason'); END IF;
  v_status:='cancelada'; v_cancelled_at:=now(); v_cancellation_reason:=v_text;
  v_resume:=NULL; v_paused_at:=NULL; v_pause_reason:=NULL; v_end_pause:=true;
  v_event:='cancelamento'; v_details:=jsonb_build_object('motivo',v_text);
 ELSE
  RETURN jsonb_build_object('error','Acao desconhecida.','code','solicitations.validation_action');
 END IF;

 UPDATE odca.solicitations SET
  status=v_status,priority=v_priority,assignee_user_id=v_assignee,triaged_at=v_triaged_at,
  first_response_at=CASE WHEN v_touch_first AND v_first_response_at IS NULL THEN now() ELSE v_first_response_at END,
  resolved_at=v_resolved_at,closed_at=v_closed_at,cancelled_at=v_cancelled_at,cancellation_reason=v_cancellation_reason,
  resume_status=v_resume,paused_at=v_paused_at,pause_reason=v_pause_reason,
  first_response_due_at=v_fr_due,resolution_due_at=v_res_due,sla_policy_id=v_policy_id,
  row_version=row_version+1,updated_at=now()
  WHERE tenant_id=p_tenant AND id=p_solicitation;
 IF v_end_pause THEN
  UPDATE odca.solicitation_pauses SET ended_at=now(),ended_by=p_actor
   WHERE solicitation_id=p_solicitation AND ended_at IS NULL;
 END IF;
 IF p_action IN('aguardar','pausar') THEN
  INSERT INTO odca.solicitation_pauses(tenant_id,solicitation_id,reason,started_by)
  VALUES(p_tenant,p_solicitation,v_text,p_actor);
 END IF;
 INSERT INTO odca.solicitation_events(tenant_id,solicitation_id,actor_user_id,event_type,details)
 VALUES(p_tenant,p_solicitation,p_actor,v_event,v_details);
 RETURN odca.solicitation_payload(p_solicitation);
END $fn$;

-- Mensagem na corrente; mensagem do cliente em aguardando_cliente reabre o fluxo
-- (retorna ao estado anterior, fecha a pausa e desloca os vencimentos).
CREATE OR REPLACE FUNCTION odca.solicitations_message(p_tenant uuid,p_actor uuid,p_solicitation uuid,p_body text,p_expected_version bigint DEFAULT NULL)
RETURNS jsonb LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,odca AS $fn$
DECLARE s odca.solicitations%rowtype; v_body text:=btrim(coalesce(p_body,''));
 v_platform boolean; v_kind text; v_shift interval; v_status text;
 v_resume text; v_fr_due timestamptz; v_res_due timestamptz; v_event text; v_auto_resume boolean:=false;
BEGIN
 SELECT * INTO s FROM odca.solicitations WHERE tenant_id=p_tenant AND id=p_solicitation FOR UPDATE;
 IF NOT FOUND THEN RETURN jsonb_build_object('error','Solicitacao nao encontrada.','code','solicitations.not_found'); END IF;
 IF length(v_body)=0 OR length(v_body)>8000 THEN RETURN jsonb_build_object('error','Mensagem vazia ou muito longa.','code','solicitations.validation_body'); END IF;
 v_platform := EXISTS(SELECT 1 FROM odca.users WHERE id=p_actor AND is_platform_administrator AND NOT is_deleted);
 IF NOT v_platform AND odca.tenant_actor_has_permission(p_actor,p_tenant,'tenant.solicitations.manage') IS DISTINCT FROM true
 THEN RETURN jsonb_build_object('error','Permissao insuficiente.','code','solicitations.permission'); END IF;
 IF p_expected_version IS NULL OR p_expected_version<>s.row_version
 THEN RETURN jsonb_build_object('error','A solicitacao foi atualizada por outro usuario.','code','solicitations.stale','currentRowVersion',s.row_version); END IF;
 IF s.status IN('encerrada','cancelada') THEN RETURN jsonb_build_object('error','Solicitacao encerrada nao aceita mensagens.','code','solicitations.transition'); END IF;
 IF v_platform AND s.status='aberta' THEN RETURN jsonb_build_object('error','Triagem obrigatoria antes de responder.','code','solicitations.transition'); END IF;
 v_kind:=CASE WHEN v_platform THEN 'odca' ELSE 'cliente' END;
 v_status:=s.status; v_resume:=s.resume_status; v_fr_due:=s.first_response_due_at; v_res_due:=s.resolution_due_at;
 IF NOT v_platform AND s.status='aguardando_cliente' THEN
  v_shift:=now()-s.paused_at;
  IF s.first_response_at IS NULL AND v_fr_due IS NOT NULL THEN v_fr_due:=v_fr_due+v_shift; END IF;
  IF v_res_due IS NOT NULL THEN v_res_due:=v_res_due+v_shift; END IF;
  v_status:=coalesce(s.resume_status,'em_atendimento'); v_resume:=NULL;
  UPDATE odca.solicitation_pauses SET ended_at=now(),ended_by=p_actor WHERE solicitation_id=p_solicitation AND ended_at IS NULL;
  v_auto_resume:=true;
 END IF;
 INSERT INTO odca.solicitation_messages(tenant_id,solicitation_id,author_user_id,author_kind,body)
 VALUES(p_tenant,p_solicitation,p_actor,v_kind,v_body);
 UPDATE odca.solicitations SET
  status=v_status,resume_status=v_resume,
  paused_at=CASE WHEN v_auto_resume THEN NULL ELSE paused_at END,
  pause_reason=CASE WHEN v_auto_resume THEN NULL ELSE pause_reason END,
  first_response_at=CASE WHEN v_platform AND first_response_at IS NULL THEN now() ELSE first_response_at END,
  first_response_due_at=v_fr_due,resolution_due_at=v_res_due,
  row_version=row_version+1,updated_at=now()
  WHERE tenant_id=p_tenant AND id=p_solicitation;
 v_event:=CASE WHEN v_platform THEN 'mensagem_odca' ELSE 'mensagem_cliente' END;
 INSERT INTO odca.solicitation_events(tenant_id,solicitation_id,actor_user_id,event_type,details)
 VALUES(p_tenant,p_solicitation,p_actor,v_event,jsonb_build_object('antecipouRetomada',v_auto_resume));
 IF v_auto_resume THEN
  INSERT INTO odca.solicitation_events(tenant_id,solicitation_id,actor_user_id,event_type,details)
  VALUES(p_tenant,p_solicitation,p_actor,'retorno_cliente',jsonb_build_object('duracaoMinutos',floor(extract(epoch from v_shift)/60)::int));
 END IF;
 RETURN odca.solicitation_payload(p_solicitation);
END $fn$;

-- Leitura unificada (tenant filtra pela propria organizacao; plataforma ve tudo).
CREATE OR REPLACE FUNCTION odca.solicitations_list(requesting_user_id uuid,p_tenant uuid DEFAULT NULL,p_service text DEFAULT NULL,p_status text DEFAULT NULL,p_search text DEFAULT NULL)
RETURNS TABLE("Id" uuid,"TenantId" uuid,"OrganizationName" text,"Protocol" text,"Service" text,"Priority" text,"Status" text,
 "Subject" text,"OpenedAt" timestamptz,"UpdatedAt" timestamptz,"AssigneeName" text,
 "FirstResponseDueAt" timestamptz,"ResolutionDueAt" timestamptz,
 "FirstResponseBreachAt" timestamptz,"ResolutionBreachAt" timestamptz,
 "Paused" boolean,"ResolvedAt" timestamptz,"ClosedAt" timestamptz,"CancelledAt" timestamptz,"CancellationReason" text)
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,odca AS $fn$
DECLARE v_platform boolean;
BEGIN
 v_platform := EXISTS(SELECT 1 FROM odca.users WHERE id=requesting_user_id AND is_platform_administrator AND NOT is_deleted);
 IF NOT v_platform THEN
  IF p_tenant IS NULL OR odca.tenant_actor_has_permission(requesting_user_id,p_tenant,'tenant.solicitations.read') IS DISTINCT FROM true
  THEN RAISE EXCEPTION 'tenant access required' USING ERRCODE='42501'; END IF;
 END IF;
 RETURN QUERY
 SELECT s.id,s.tenant_id,t.display_name,'SOL-'||lpad(s.number::text,6,'0'),s.service,s.priority,s.status,
  s.subject::text,s.opened_at,s.updated_at,a.display_name,
  s.first_response_due_at,s.resolution_due_at,s.first_response_breach_at,s.resolution_breach_at,
  s.paused_at IS NOT NULL,s.resolved_at,s.closed_at,s.cancelled_at,s.cancellation_reason::text
 FROM odca.solicitations s
 JOIN odca.tenants t ON t.id=s.tenant_id
 LEFT JOIN odca.users a ON a.id=s.assignee_user_id
 WHERE (p_tenant IS NULL OR s.tenant_id=p_tenant)
   AND (p_service IS NULL OR s.service=p_service)
   AND (p_status IS NULL OR s.status=p_status)
   AND (p_search IS NULL OR s.subject ILIKE '%'||p_search||'%' OR s.number::text LIKE '%'||p_search||'%')
 ORDER BY CASE WHEN s.status IN('aberta','triagem','em_atendimento','aguardando_cliente') THEN 0 ELSE 1 END,s.updated_at DESC;
END $fn$;

CREATE OR REPLACE FUNCTION odca.solicitation_detail(requesting_user_id uuid,p_solicitation uuid)
RETURNS jsonb LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,odca AS $fn$
DECLARE s odca.solicitations%rowtype; v_org text; v_msgs jsonb; v_events jsonb;
BEGIN
 SELECT * INTO s FROM odca.solicitations WHERE id=p_solicitation;
 IF NOT FOUND THEN RETURN jsonb_build_object('error','Solicitacao nao encontrada.','code','solicitations.not_found'); END IF;
 IF NOT EXISTS(SELECT 1 FROM odca.users WHERE id=requesting_user_id AND is_platform_administrator AND NOT is_deleted)
  AND odca.tenant_actor_has_permission(requesting_user_id,s.tenant_id,'tenant.solicitations.read') IS DISTINCT FROM true
 THEN RETURN jsonb_build_object('error','Permissao insuficiente.','code','solicitations.permission'); END IF;
 SELECT t.display_name INTO v_org FROM odca.tenants t WHERE t.id=s.tenant_id;
 SELECT COALESCE(jsonb_agg(jsonb_build_object('id',m.id,'authorKind',m.author_kind,'authorName',u.display_name,
   'body',m.body,'createdAt',m.created_at) ORDER BY m.created_at,m.id),'[]'::jsonb)
  INTO v_msgs FROM odca.solicitation_messages m JOIN odca.users u ON u.id=m.author_user_id WHERE m.solicitation_id=p_solicitation;
 SELECT COALESCE(jsonb_agg(jsonb_build_object('eventType',e.event_type,'actorName',u.display_name,
   'details',e.details,'occurredAt',e.occurred_at) ORDER BY e.occurred_at,e.id),'[]'::jsonb)
  INTO v_events FROM odca.solicitation_events e LEFT JOIN odca.users u ON u.id=e.actor_user_id WHERE e.solicitation_id=p_solicitation;
 RETURN odca.solicitation_payload(p_solicitation)||jsonb_build_object(
  'organizationName',v_org,'messages',v_msgs,'events',v_events);
END $fn$;

-- Varredura de violacoes (Worker): marca cada violacao uma unica vez em solicitacoes
-- ativas sem pausa; a de resolucao tambem alcanca resolvidas ainda nao encerradas.
CREATE OR REPLACE FUNCTION odca.sla_scan_violations() RETURNS integer
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,odca AS $fn$
DECLARE r record; v_count integer:=0;
BEGIN
 FOR r IN SELECT * FROM odca.solicitations
  WHERE status IN('aberta','triagem','em_atendimento','aguardando_cliente') AND paused_at IS NULL FOR UPDATE SKIP LOCKED LOOP
  IF r.first_response_at IS NULL AND r.first_response_breach_at IS NULL AND r.first_response_due_at<=now() THEN
   UPDATE odca.solicitations SET first_response_breach_at=now() WHERE id=r.id;
   INSERT INTO odca.solicitation_events(tenant_id,solicitation_id,actor_user_id,event_type,details)
   VALUES(r.tenant_id,r.id,NULL,'violacao_primeira_resposta',jsonb_build_object('vencimento',r.first_response_due_at));
   v_count:=v_count+1;
  END IF;
  IF r.resolution_breach_at IS NULL AND r.resolution_due_at<=now() THEN
   UPDATE odca.solicitations SET resolution_breach_at=now() WHERE id=r.id;
   INSERT INTO odca.solicitation_events(tenant_id,solicitation_id,actor_user_id,event_type,details)
   VALUES(r.tenant_id,r.id,NULL,'violacao_resolucao',jsonb_build_object('vencimento',r.resolution_due_at));
   v_count:=v_count+1;
  END IF;
 END LOOP;
 FOR r IN SELECT * FROM odca.solicitations
  WHERE status='resolvida' AND resolution_breach_at IS NULL AND resolution_due_at<=resolved_at FOR UPDATE SKIP LOCKED LOOP
  UPDATE odca.solicitations SET resolution_breach_at=now() WHERE id=r.id;
  INSERT INTO odca.solicitation_events(tenant_id,solicitation_id,actor_user_id,event_type,details)
  VALUES(r.tenant_id,r.id,NULL,'violacao_resolucao',jsonb_build_object('vencimento',r.resolution_due_at));
  v_count:=v_count+1;
 END LOOP;
 RETURN v_count;
END $fn$;

-- Fila ODCA de modelos que exigem aprovacao: instalacoes oficiais por organizacao.
CREATE OR REPLACE FUNCTION odca.template_approvals_queue(actor uuid,p_keys text[])
RETURNS TABLE("TenantId" uuid,"OrganizationName" text,"ActivityProfile" text,"OfficialKey" text,
 "Decision" text,"DecisionNote" text,"DecidedAt" timestamptz,"DecidedByName" text,
 "GeneratedCount" bigint,"LastGeneratedAt" timestamptz)
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,odca AS $fn$
BEGIN
 PERFORM odca.assert_platform_actor(actor);
 RETURN QUERY
 SELECT t.id,t.display_name,t.activity_profile,ct.official_key,ta.decision,nullif(ta.note,''),ta.decided_at,du.display_name,
  count(gv.id),max(gv.created_at)
 FROM odca.contract_templates ct
 JOIN odca.tenants t ON t.id=ct.owner_tenant_id AND NOT t.is_deleted
 LEFT JOIN odca.generated_contract_versions gv ON gv.source_template_id=ct.id
 LEFT JOIN odca.template_approvals ta ON ta.tenant_id=ct.owner_tenant_id AND ta.official_key=ct.official_key
 LEFT JOIN odca.users du ON du.id=ta.decided_by
 WHERE ct.official_key = ANY(p_keys) AND ct.owner_tenant_id IS NOT NULL AND ct.status<>'archived'
 GROUP BY t.id,t.display_name,t.activity_profile,ct.official_key,ta.decision,ta.note,ta.decided_at,du.display_name
 ORDER BY (ta.decision IS NULL) DESC,max(gv.created_at) DESC NULLS LAST;
END $fn$;

CREATE OR REPLACE FUNCTION odca.template_approval_decide(actor uuid,requested_tenant uuid,requested_key text,decision text,note text DEFAULT NULL)
RETURNS jsonb LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,odca AS $fn$
DECLARE v_note text:=nullif(btrim(coalesce(note,'')),'');
BEGIN
 PERFORM odca.assert_platform_actor(actor);
 IF decision NOT IN('aprovado','reprovado') THEN RETURN jsonb_build_object('error','Decisao invalida.','code','approvals.validation_decision'); END IF;
 IF decision='reprovado' AND length(coalesce(v_note,''))<5 THEN RETURN jsonb_build_object('error','Reprovacao exige justificativa de ao menos 5 caracteres.','code','approvals.validation_note'); END IF;
 IF NOT EXISTS(SELECT 1 FROM odca.contract_templates ct WHERE ct.owner_tenant_id=requested_tenant AND ct.official_key=requested_key AND ct.status<>'archived')
 THEN RETURN jsonb_build_object('error','Modelo oficial nao instalado nesta organizacao.','code','approvals.not_installed'); END IF;
 INSERT INTO odca.template_approvals(tenant_id,official_key,decision,note,decided_by)
 VALUES(requested_tenant,requested_key,decision,coalesce(v_note,''),actor)
 ON CONFLICT(tenant_id,official_key) DO UPDATE
  SET decision=EXCLUDED.decision,note=EXCLUDED.note,decided_by=EXCLUDED.decided_by,decided_at=now(),updated_at=now();
 INSERT INTO odca.audit_events(scope_type,tenant_id,actor_user_id,action,entity_type,result,metadata)
 VALUES('tenant',requested_tenant,actor,'odca.template_approval.'||decision,'contract_template','success',
  jsonb_build_object('officialKey',requested_key,'decision',decision,'note',coalesce(v_note,'')));
 RETURN jsonb_build_object('ok',true,'tenantId',requested_tenant,'officialKey',requested_key,'decision',decision);
END $fn$;

REVOKE ALL ON FUNCTION odca.sla_add_business_minutes(timestamptz,integer,text,text,time,time),odca.solicitation_payload(uuid),
 odca.solicitations_open(uuid,uuid,text,text,text,text,uuid),odca.solicitations_transition(uuid,uuid,uuid,text,text,text,uuid,bigint),
 odca.solicitations_message(uuid,uuid,uuid,text,bigint),odca.solicitations_list(uuid,uuid,text,text,text),
 odca.solicitation_detail(uuid,uuid),odca.sla_scan_violations(),odca.template_approvals_queue(uuid,text[]),
 odca.template_approval_decide(uuid,uuid,text,text,text) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION odca.sla_add_business_minutes(timestamptz,integer,text,text,time,time),odca.solicitation_payload(uuid),
 odca.solicitations_open(uuid,uuid,text,text,text,text,uuid),odca.solicitations_transition(uuid,uuid,uuid,text,text,text,uuid,bigint),
 odca.solicitations_message(uuid,uuid,uuid,text,bigint),odca.solicitations_list(uuid,uuid,text,text,text),
 odca.solicitation_detail(uuid,uuid),odca.sla_scan_violations(),odca.template_approvals_queue(uuid,text[]),
 odca.template_approval_decide(uuid,uuid,text,text,text) TO odca_app;

INSERT INTO odca.schema_migrations(version,name,checksum)
VALUES(42,'Enterprise ODCA: solicitacoes, SLA por plano e aprovacao de modelos cirurgicos','901a50fc9b4cf02bc18189b602df973102ae754f27926511700281eadb75ee9e')
ON CONFLICT(version) DO NOTHING;
COMMIT;
-- ODCA-END 042
-- ODCA-MIGRATION 043 CHECKSUM b879b4e9c174596316db3ec8ea5e8aff1eabf1d291d1629b4dccda62dfe79245
BEGIN;
SELECT pg_advisory_xact_lock(hashtext('odca.schema.migrations'));

-- Estabilização A (docs/execution/AUDITORIA_ESTABILIZACAO_A.md, decisões D-A1..D-A5):
-- rastreabilidade de modalidade de assinatura (session/evidence), vinculação de
-- identidade própria ao participante, permissões por modalidade, aprovação de
-- modelos cirúrgicos presa à revisão instalada e tenant.reviews.read no trio padrão.

-- Chave substituta em memberships para referência por vínculo de identidade
-- (a chave primária composta já existe e continua valendo).
ALTER TABLE odca.memberships ADD COLUMN id uuid NOT NULL DEFAULT gen_random_uuid();
CREATE UNIQUE INDEX memberships_id_uq ON odca.memberships(id);

-- D-A1: modalidade de assinatura. Linhas anteriores à v043 permanecem com
-- signed_through NULL ("registro anterior à rastreabilidade de modalidade").
ALTER TABLE odca.signature_participants
  ADD COLUMN signed_through text CHECK(signed_through IS NULL OR signed_through IN('session','evidence')),
  ADD COLUMN signed_session_id uuid REFERENCES odca.sessions(id),
  ADD COLUMN signature_evidence varchar(2000),
  ADD COLUMN identity_membership_id uuid REFERENCES odca.memberships(id),
  ADD CONSTRAINT signature_participants_mode_session_ck CHECK(signed_through IS DISTINCT FROM 'session' OR signed_session_id IS NOT NULL),
  ADD CONSTRAINT signature_participants_session_only_ck CHECK(signed_through = 'session' OR signed_session_id IS NULL),
  ADD CONSTRAINT signature_participants_evidence_len_ck CHECK(signed_through IS DISTINCT FROM 'evidence' OR length(btrim(coalesce(signature_evidence,'')))>=5),
  ADD CONSTRAINT signature_participants_evidence_only_ck CHECK(signed_through = 'evidence' OR signature_evidence IS NULL);

CREATE INDEX signature_participants_identity_ix ON odca.signature_participants(identity_membership_id) WHERE identity_membership_id IS NOT NULL;

-- D-A2: permissões por modalidade. contract_drafts.manage segue governando
-- preparar/confirmar/reabrir/lembrar, mas deixa de, sozinha, declarar assinaturas.
INSERT INTO odca.permissions(code,description,delegable) VALUES
 ('tenant.signature_participants.sign_self','Assinar como participante vinculado à própria conta',true),
 ('tenant.signature_participants.record','Registrar assinatura de terceiro por evidência',true)
ON CONFLICT(code) DO UPDATE SET description=EXCLUDED.description,delegable=EXCLUDED.delegable;
INSERT INTO odca.role_permissions(role_id,permission_code)
SELECT r.id,p.code FROM odca.roles r CROSS JOIN odca.permissions p
WHERE r.scope_type='tenant' AND p.code IN('tenant.signature_participants.sign_self','tenant.signature_participants.record')
  AND (r.code IN('tenant-administrator','tenant-coordinator','tenant-member')
       OR EXISTS(SELECT 1 FROM odca.role_permissions held WHERE held.role_id=r.id AND held.permission_code='tenant.contract_drafts.manage'))
ON CONFLICT DO NOTHING;

-- tenant.reviews.read para o trio padrão e tenants existentes (roles que já
-- participam de revisão): encerra o aviso permanente review.separate.
INSERT INTO odca.role_permissions(role_id,permission_code)
SELECT r.id,'tenant.reviews.read' FROM odca.roles r
WHERE r.scope_type='tenant' AND (r.code IN('tenant-administrator','tenant-delegated-administrator','tenant-coordinator','tenant-member','tenant-operator','tenant-client')
  OR EXISTS(SELECT 1 FROM odca.role_permissions held WHERE held.role_id=r.id AND held.permission_code IN('tenant.reviews.request','tenant.reviews.decide','tenant.reviews.history')))
ON CONFLICT DO NOTHING;

-- Novos tenants herdam as permissões de assinatura no papel membro.
CREATE OR REPLACE FUNCTION odca.ensure_tenant_standard_roles(target_tenant uuid)
RETURNS void
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, odca
AS $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM odca.tenants WHERE id = target_tenant AND NOT is_deleted) THEN
        RETURN;
    END IF;
    INSERT INTO odca.roles(scope_type, tenant_id, code, display_name, is_system)
    VALUES
        ('tenant', target_tenant, 'tenant-delegated-administrator', 'Administrador delegado', true),
        ('tenant', target_tenant, 'tenant-coordinator', 'Coordenador', true),
        ('tenant', target_tenant, 'tenant-member', 'Usuário', true)
    ON CONFLICT DO NOTHING;

    INSERT INTO odca.role_permissions(role_id, permission_code)
    SELECT role.id, permission.code
      FROM odca.roles AS role
      JOIN odca.permissions AS permission
        ON permission.code = ANY (
            CASE role.code
                WHEN 'tenant-delegated-administrator' THEN ARRAY[
                    'tenant.organization.read', 'tenant.team.read', 'tenant.team.manage',
                    'tenant.patients.read', 'tenant.templates.read', 'tenant.contract_drafts.read',
                    'tenant.contracts.read',
                    'tenant.documents.download', 'tenant.reviews.read', 'tenant.imports.read', 'tenant.billing.read']
                WHEN 'tenant-coordinator' THEN ARRAY[
                    'tenant.patients.read', 'tenant.templates.read', 'tenant.contract_drafts.read',
                    'tenant.contracts.read', 'tenant.contracts.manage',
                    'tenant.reviews.read', 'tenant.reviews.decide', 'tenant.reviews.history',
                    'tenant.obligations.read', 'tenant.obligations.read_all', 'tenant.obligations.assign']
                WHEN 'tenant-member' THEN ARRAY[
                    'tenant.patients.read', 'tenant.patients.manage', 'tenant.templates.read',
                    'tenant.contract_drafts.read', 'tenant.contract_drafts.manage',
                    'tenant.signature_participants.sign_self', 'tenant.signature_participants.record',
                    'tenant.contracts.read',
                    'tenant.documents.download', 'tenant.reviews.read', 'tenant.reviews.request']
                ELSE ARRAY[]::text[]
            END)
     WHERE role.tenant_id = target_tenant
       AND role.code IN ('tenant-delegated-administrator', 'tenant-coordinator', 'tenant-member')
    ON CONFLICT DO NOTHING;
END;
$$;

-- Auditoria A §4: aprovação de modelos presa à revisão instalada. Decidir estampa
-- a versão publicada do template na organização; quando o modelo muda (nova
-- versão publicada), a aprovação deixa de cobrir e o bloqueio de readiness volta
-- até nova decisão. Linhas pré-v043 (template_version_id NULL) permanecem válidas
-- por continuidade dos registros históricos já auditados.
ALTER TABLE odca.template_approvals
  ADD COLUMN template_version_id uuid REFERENCES odca.contract_template_versions(id),
  ADD COLUMN template_version_number integer CHECK(template_version_number IS NULL OR template_version_number>0),
  ADD CONSTRAINT template_approvals_revision_ck CHECK(template_version_id IS NULL OR template_version_number IS NOT NULL);

CREATE OR REPLACE FUNCTION odca.template_approval_decide(actor uuid,requested_tenant uuid,requested_key text,decision text,note text DEFAULT NULL)
RETURNS jsonb LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,odca AS $fn$
DECLARE v_note text:=nullif(btrim(coalesce(note,'')),''); v_version uuid; v_number integer;
BEGIN
 PERFORM odca.assert_platform_actor(actor);
 IF decision NOT IN('aprovado','reprovado') THEN RETURN jsonb_build_object('error','Decisao invalida.','code','approvals.validation_decision'); END IF;
 IF decision='reprovado' AND length(coalesce(v_note,''))<5 THEN RETURN jsonb_build_object('error','Reprovacao exige justificativa de ao menos 5 caracteres.','code','approvals.validation_note'); END IF;
 SELECT tv.id,tv.version_number INTO v_version,v_number
  FROM odca.contract_templates ct
  JOIN odca.contract_template_versions tv ON tv.template_id=ct.id AND tv.version_number=ct.current_version
 WHERE ct.owner_tenant_id=requested_tenant AND ct.official_key=requested_key AND ct.status<>'archived';
 IF v_version IS NULL THEN RETURN jsonb_build_object('error','Modelo oficial nao instalado nesta organizacao.','code','approvals.not_installed'); END IF;
 INSERT INTO odca.template_approvals(tenant_id,official_key,decision,note,decided_by,template_version_id,template_version_number)
 VALUES(requested_tenant,requested_key,decision,coalesce(v_note,''),actor,v_version,v_number)
 ON CONFLICT(tenant_id,official_key) DO UPDATE
  SET decision=EXCLUDED.decision,note=EXCLUDED.note,decided_by=EXCLUDED.decided_by,decided_at=now(),updated_at=now(),
      template_version_id=EXCLUDED.template_version_id,template_version_number=EXCLUDED.template_version_number;
 INSERT INTO odca.audit_events(scope_type,tenant_id,actor_user_id,action,entity_type,result,metadata)
 VALUES('tenant',requested_tenant,actor,'odca.template_approval.'||decision,'contract_template','success',
  jsonb_build_object('officialKey',requested_key,'decision',decision,'note',coalesce(v_note,''),'approvedRevision',v_number));
 RETURN jsonb_build_object('ok',true,'tenantId',requested_tenant,'officialKey',requested_key,'decision',decision,'approvedRevision',v_number);
END $fn$;
REVOKE ALL ON FUNCTION odca.template_approval_decide(uuid,uuid,text,text,text) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION odca.template_approval_decide(uuid,uuid,text,text,text) TO odca_app;

-- A tabela de retorno ganhou ApprovedRevision: CREATE OR REPLACE não altera o
-- tipo de retorno de função existente (42P13), então drop+create.
DROP FUNCTION IF EXISTS odca.template_approvals_queue(uuid, text[]);
CREATE OR REPLACE FUNCTION odca.template_approvals_queue(actor uuid,p_keys text[])
RETURNS TABLE("TenantId" uuid,"OrganizationName" text,"ActivityProfile" text,"OfficialKey" text,
 "Decision" text,"DecisionNote" text,"DecidedAt" timestamptz,"DecidedByName" text,
 "ApprovedRevision" integer,"GeneratedCount" bigint,"LastGeneratedAt" timestamptz)
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,odca AS $fn$
BEGIN
 PERFORM odca.assert_platform_actor(actor);
 RETURN QUERY
 SELECT t.id,t.display_name,t.activity_profile,ct.official_key,ta.decision,nullif(ta.note,''),ta.decided_at,du.display_name,
  ta.template_version_number,
  count(gv.id),max(gv.created_at)
 FROM odca.contract_templates ct
 JOIN odca.tenants t ON t.id=ct.owner_tenant_id AND NOT t.is_deleted
 LEFT JOIN odca.generated_contract_versions gv ON gv.source_template_id=ct.id
 LEFT JOIN odca.template_approvals ta ON ta.tenant_id=ct.owner_tenant_id AND ta.official_key=ct.official_key
 LEFT JOIN odca.users du ON du.id=ta.decided_by
 WHERE ct.official_key = ANY(p_keys) AND ct.owner_tenant_id IS NOT NULL AND ct.status<>'archived'
 GROUP BY t.id,t.display_name,t.activity_profile,ct.official_key,ta.decision,ta.note,ta.decided_at,du.display_name,ta.template_version_number
 ORDER BY (ta.decision IS NULL) DESC,max(gv.created_at) DESC NULLS LAST;
END $fn$;
REVOKE ALL ON FUNCTION odca.template_approvals_queue(uuid,text[]) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION odca.template_approvals_queue(uuid,text[]) TO odca_app;

INSERT INTO odca.schema_migrations(version,name,checksum)
VALUES(43,'Estabilização A: modalidade de assinatura, aprovação por revisão e reviews.read','b879b4e9c174596316db3ec8ea5e8aff1eabf1d291d1629b4dccda62dfe79245')
ON CONFLICT(version) DO NOTHING;
COMMIT;
-- ODCA-END 043
-- ODCA-MIGRATION 044 CHECKSUM 108c7c5e561f0a7047d06d52e80ad022c05864d93d6d48565170f5ee59610fc5
BEGIN;
-- Bloco A (D-OC1): resolucao de modelo oficial para catalogo global.
-- Ordem de resolucao por tenant+chave: (1) ultimo modelo oficial usado pelo
-- tenant em versoes geradas; (2) copia particular ativa do tenant; (3) linha
-- global publicada pelo catalogo da plataforma. A fila e a decisao de
-- aprovacao ODCA passam a funcionar com qualquer uma das fontes, mantendo o
-- registro da revisao aprovada (template_version_id/number).

CREATE OR REPLACE FUNCTION odca.resolve_official_template(p_tenant uuid,p_key text)
RETURNS TABLE(template_id uuid,version_id uuid,version_number integer)
LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path=pg_catalog,odca AS $fn$
DECLARE v_tid uuid; v_vid uuid; v_num integer;
BEGIN
  SELECT c.tid,tv.id,tv.version_number INTO v_tid,v_vid,v_num
  FROM (
    SELECT tid,rank FROM (
      SELECT v.source_template_id AS tid, 1::int AS rank
        FROM odca.generated_contract_versions v
        JOIN odca.contract_templates t ON t.id=v.source_template_id
       WHERE v.tenant_id=p_tenant AND t.official_key=p_key
       ORDER BY v.created_at DESC LIMIT 1
    ) latest
    UNION ALL
    SELECT t.id, 2 FROM odca.contract_templates t
     WHERE t.official_key=p_key AND t.owner_tenant_id=p_tenant AND t.status<>'archived'
    UNION ALL
    SELECT t.id, 3 FROM odca.contract_templates t
     WHERE t.official_key=p_key AND t.owner_tenant_id IS NULL AND t.scope='global' AND t.status<>'archived'
  ) c
  JOIN odca.contract_templates t ON t.id=c.tid AND t.status<>'archived'
  JOIN odca.contract_template_versions tv ON tv.template_id=t.id AND tv.version_number=t.current_version
  ORDER BY c.rank
  LIMIT 1;
  IF v_vid IS NOT NULL THEN RETURN QUERY SELECT v_tid,v_vid,v_num; END IF;
END $fn$;
REVOKE ALL ON FUNCTION odca.resolve_official_template(uuid,text) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION odca.resolve_official_template(uuid,text) TO odca_app;

CREATE OR REPLACE FUNCTION odca.template_approval_decide(actor uuid,requested_tenant uuid,requested_key text,decision text,note text DEFAULT NULL)
RETURNS jsonb LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,odca AS $fn$
DECLARE v_note text:=nullif(btrim(coalesce(note,'')),''); v_version uuid; v_number integer;
BEGIN
  PERFORM odca.assert_platform_actor(actor);
  IF decision NOT IN('aprovado','reprovado') THEN RETURN jsonb_build_object('error','Decisao invalida.','code','approvals.validation_decision'); END IF;
  IF decision='reprovado' AND length(coalesce(v_note,''))<5 THEN RETURN jsonb_build_object('error','Reprovacao exige justificativa de ao menos 5 caracteres.','code','approvals.validation_note'); END IF;
  SELECT r.version_id,r.version_number INTO v_version,v_number FROM odca.resolve_official_template(requested_tenant,requested_key) r;
  IF v_version IS NULL THEN RETURN jsonb_build_object('error','Modelo oficial nao disponivel nesta organizacao.','code','approvals.not_installed'); END IF;
  INSERT INTO odca.template_approvals(tenant_id,official_key,decision,note,decided_by,template_version_id,template_version_number)
  VALUES(requested_tenant,requested_key,decision,coalesce(v_note,''),actor,v_version,v_number)
  ON CONFLICT(tenant_id,official_key) DO UPDATE
   SET decision=EXCLUDED.decision,note=EXCLUDED.note,decided_by=EXCLUDED.decided_by,decided_at=now(),updated_at=now(),
       template_version_id=EXCLUDED.template_version_id,template_version_number=EXCLUDED.template_version_number;
  INSERT INTO odca.audit_events(scope_type,tenant_id,actor_user_id,action,entity_type,result,metadata)
  VALUES('tenant',requested_tenant,actor,'odca.template_approval.'||decision,'contract_template','success',
   jsonb_build_object('officialKey',requested_key,'decision',decision,'note',coalesce(v_note,''),'approvedRevision',v_number));
  RETURN jsonb_build_object('ok',true,'tenantId',requested_tenant,'officialKey',requested_key,'decision',decision,'approvedRevision',v_number);
END $fn$;
REVOKE ALL ON FUNCTION odca.template_approval_decide(uuid,uuid,text,text,text) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION odca.template_approval_decide(uuid,uuid,text,text,text) TO odca_app;

CREATE OR REPLACE FUNCTION odca.template_approvals_queue(actor uuid,p_keys text[])
RETURNS TABLE("TenantId" uuid,"OrganizationName" text,"ActivityProfile" text,"OfficialKey" text,
  "Decision" text,"DecisionNote" text,"DecidedAt" timestamptz,"DecidedByName" text,
  "ApprovedRevision" integer,"GeneratedCount" bigint,"LastGeneratedAt" timestamptz)
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,odca AS $fn$
BEGIN
  PERFORM odca.assert_platform_actor(actor);
  RETURN QUERY
  WITH usage AS (
    SELECT ct.owner_tenant_id AS tenant_id, ct.official_key
      FROM odca.contract_templates ct
     WHERE ct.official_key = ANY(p_keys) AND ct.owner_tenant_id IS NOT NULL AND ct.status<>'archived'
    UNION
    SELECT gv.tenant_id, t.official_key
      FROM odca.generated_contract_versions gv
      JOIN odca.contract_templates t ON t.id=gv.source_template_id
     WHERE t.official_key = ANY(p_keys) AND t.owner_tenant_id IS NULL AND t.status<>'archived'
  )
  SELECT t.id,t.display_name,t.activity_profile,u.official_key,ta.decision,nullif(ta.note,''),ta.decided_at,du.display_name,
   ta.template_version_number,
   count(gv.id),max(gv.created_at)
  FROM usage u
  JOIN odca.tenants t ON t.id=u.tenant_id AND NOT t.is_deleted
  LEFT JOIN odca.generated_contract_versions gv ON gv.tenant_id=u.tenant_id
    AND gv.source_template_id IN (SELECT t2.id FROM odca.contract_templates t2
       WHERE t2.official_key=u.official_key AND t2.status<>'archived'
         AND (t2.owner_tenant_id=u.tenant_id OR t2.owner_tenant_id IS NULL))
  LEFT JOIN odca.template_approvals ta ON ta.tenant_id=u.tenant_id AND ta.official_key=u.official_key
  LEFT JOIN odca.users du ON du.id=ta.decided_by
  GROUP BY t.id,t.display_name,t.activity_profile,u.official_key,ta.decision,ta.note,ta.decided_at,du.display_name,ta.template_version_number
  ORDER BY (ta.decision IS NULL) DESC,max(gv.created_at) DESC NULLS LAST;
END $fn$;
REVOKE ALL ON FUNCTION odca.template_approvals_queue(uuid,text[]) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION odca.template_approvals_queue(uuid,text[]) TO odca_app;

INSERT INTO odca.schema_migrations(version,name,checksum)
VALUES(44,'Operacao contratual A: catalogo oficial global e resolucao de aprovacao','108c7c5e561f0a7047d06d52e80ad022c05864d93d6d48565170f5ee59610fc5')
ON CONFLICT(version) DO NOTHING;
COMMIT;
-- ODCA-END 044
-- ODCA-MIGRATION 045 CHECKSUM 3ced09a710ec053f9fb10b97d803e7f3e204e439c7e07014d5e2c02b321bef0b
BEGIN;
SELECT pg_advisory_xact_lock(hashtext('odca.schema.migrations'));

-- Operacao contratual B: suporte tecnico em todo plano vigente + administracao global
-- (catalogo oficial publicavel/retiravel, busca global de usuarios, filtros de clientes).
-- Escopo: servicos documentais da ODCA (revisao/adaptacao/esclarecimento) seguem
-- restritos ao plano Enterprise com SLA; suporte tecnico e recuperacao de acesso
-- ficam abertos a qualquer plano vigente, com o mesmo relogio SLA por plano.

ALTER TABLE odca.solicitations DROP CONSTRAINT solicitations_service_check;
ALTER TABLE odca.solicitations ADD CONSTRAINT solicitations_service_ck CHECK(service IN('revisao','adaptacao','esclarecimento','suporte_tecnico'));
ALTER TABLE odca.sla_policies DROP CONSTRAINT sla_policies_service_check;
ALTER TABLE odca.sla_policies ADD CONSTRAINT sla_policies_service_ck CHECK(service IN('revisao','adaptacao','esclarecimento','suporte_tecnico'));

-- Semente do suporte tecnico por plano: mesmos tempos base do 'esclarecimento'
-- (servico documental mais leve, semente 042) com o mesmo multiplicador por plano.
INSERT INTO odca.sla_policies(plan_code,service,priority,timezone,calendar,business_start,business_end,first_response_minutes,resolution_minutes)
SELECT pl.plan_code,'suporte_tecnico',b.priority,'America/Sao_Paulo','dias_uteis','09:00','18:00',
 b.fr * CASE pl.plan_code WHEN 'enterprise' THEN 1 WHEN 'intermediate' THEN 2 ELSE 4 END,
 b.res * CASE pl.plan_code WHEN 'enterprise' THEN 1 WHEN 'intermediate' THEN 2 ELSE 4 END
FROM (VALUES ('enterprise'),('intermediate'),('basic')) pl(plan_code)
CROSS JOIN (VALUES ('baixa',1440,2880),('normal',240,720),('alta',120,480),('critica',60,180)) b(priority,fr,res)
ON CONFLICT(plan_code,service,priority) DO NOTHING;

-- Abertura de solicitacao: gate de plano apenas para os servicos documentais;
-- suporte tecnico exige apenas uma assinatura ativa. Mensagem/codigo canonicos
-- preservados para os demais casos (comportamento validado pelas suites C/E).
CREATE OR REPLACE FUNCTION odca.solicitations_open(p_tenant uuid,p_actor uuid,p_service text,p_priority text,p_subject text,p_body text,p_idempotency uuid)
RETURNS jsonb LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,odca AS $fn$
DECLARE v_subject text:=btrim(coalesce(p_subject,'')); v_body text:=btrim(coalesce(p_body,''));
 v_plan text; v_policy odca.sla_policies%rowtype; v_number bigint; v_existing uuid; v_id uuid;
BEGIN
 IF NOT EXISTS(SELECT 1 FROM odca.users WHERE id=p_actor AND is_platform_administrator AND NOT is_deleted)
  AND odca.tenant_actor_has_permission(p_actor,p_tenant,'tenant.solicitations.manage') IS DISTINCT FROM true
 THEN RETURN jsonb_build_object('error','Permissao insuficiente.','code','solicitations.permission'); END IF;
 IF length(v_subject)<3 OR length(v_subject)>200 THEN RETURN jsonb_build_object('error','Assunto deve ter entre 3 e 200 caracteres.','code','solicitations.validation_subject'); END IF;
 IF length(v_body)<10 OR length(v_body)>8000 THEN RETURN jsonb_build_object('error','Descricao deve ter entre 10 e 8000 caracteres.','code','solicitations.validation_body'); END IF;
 IF p_service IS NULL OR p_service NOT IN('revisao','adaptacao','esclarecimento','suporte_tecnico')
  OR p_priority IS NULL OR p_priority NOT IN('baixa','normal','alta','critica') OR p_idempotency IS NULL
 THEN RETURN jsonb_build_object('error','Servico, prioridade ou chave de idempotencia invalidos.','code','solicitations.validation_fields'); END IF;
 SELECT pv.code INTO v_plan FROM odca.subscriptions s JOIN odca.plan_versions pv ON pv.id=s.plan_version_id
  WHERE s.tenant_id=p_tenant AND s.status='active';
 IF v_plan IS NULL OR (p_service<>'suporte_tecnico' AND v_plan<>'enterprise')
 THEN RETURN jsonb_build_object('error','Central de solicitações disponível apenas no plano Enterprise.','code','solicitations.plan.required'); END IF;
 SELECT id INTO v_existing FROM odca.solicitations WHERE tenant_id=p_tenant AND idempotency_key=p_idempotency;
 IF v_existing IS NOT NULL THEN RETURN odca.solicitation_payload(v_existing); END IF;
 PERFORM pg_advisory_xact_lock(hashtext('odca.solicitations'),hashtext(p_tenant::text));
 SELECT coalesce(max(number),0)+1 INTO v_number FROM odca.solicitations WHERE tenant_id=p_tenant;
 SELECT * INTO STRICT v_policy FROM odca.sla_policies
  WHERE plan_code=v_plan AND service=p_service AND priority=p_priority AND enabled;
 INSERT INTO odca.solicitations(tenant_id,number,service,priority,subject,body,opened_by,idempotency_key,
  sla_policy_id,sla_timezone,sla_calendar,sla_business_start,sla_business_end,first_response_due_at,resolution_due_at)
 VALUES(p_tenant,v_number,p_service,p_priority,v_subject,v_body,p_actor,p_idempotency,
  v_policy.id,v_policy.timezone,v_policy.calendar,v_policy.business_start,v_policy.business_end,
  odca.sla_add_business_minutes(now(),v_policy.first_response_minutes,v_policy.timezone,v_policy.calendar,v_policy.business_start,v_policy.business_end),
  odca.sla_add_business_minutes(now(),v_policy.resolution_minutes,v_policy.timezone,v_policy.calendar,v_policy.business_start,v_policy.business_end))
 RETURNING id INTO v_id;
 INSERT INTO odca.solicitation_events(tenant_id,solicitation_id,actor_user_id,event_type,details)
 VALUES(p_tenant,v_id,p_actor,'abertura',jsonb_build_object('servico',p_service,'prioridade',p_priority));
 RETURN odca.solicitation_payload(v_id);
EXCEPTION WHEN unique_violation THEN
 SELECT id INTO v_existing FROM odca.solicitations WHERE tenant_id=p_tenant AND idempotency_key=p_idempotency;
 IF v_existing IS NOT NULL THEN RETURN odca.solicitation_payload(v_existing); END IF; RAISE;
END $fn$;

-- Lista de clientes da plataforma com filtros opcionais de plano e situacao
-- (mesmo corpo anterior; a versao de 2 parametros e removida).
CREATE OR REPLACE FUNCTION odca.platform_consumption_customers(actor uuid,search text DEFAULT NULL,plan_code text DEFAULT NULL,tenant_status text DEFAULT NULL)
RETURNS TABLE("TenantId" uuid,"Name" text,"MaskedDocument" text,"PlanName" text,"TenantStatus" text,"SubscriptionStatus" text,"ActiveUsers" integer,"UsedBytes" bigint,"LimitBytes" bigint,"PendingRequests" integer,"LastActivity" timestamptz)
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,odca AS $$ BEGIN PERFORM odca.assert_platform_actor(actor); RETURN QUERY
 SELECT t.id,t.display_name,CASE WHEN length(coalesce(t.business_code,''))>4 THEN repeat('*',length(t.business_code)-4)||right(t.business_code,4) ELSE '****' END,p.display_name,t.status,s.status,
 (SELECT count(*)::int FROM odca.memberships m WHERE m.tenant_id=t.id AND m.status='active'),coalesce(u.used_bytes,0),
 (coalesce(max(e.limit_value) FILTER(WHERE e.entitlement_code='storage_bytes'),0)+coalesce((SELECT sum(g.quantity_bytes) FROM odca.storage_capacity_grants g WHERE g.tenant_id=t.id AND g.revoked_at IS NULL AND(g.valid_until IS NULL OR g.valid_until>now())),0))::bigint,
 (SELECT count(*)::int FROM odca.additional_storage_requests r WHERE r.tenant_id=t.id AND r.status='pending'),greatest(t.updated_at,max(a.occurred_at))
 FROM odca.tenants t JOIN odca.subscriptions s ON s.tenant_id=t.id JOIN odca.plan_versions p ON p.id=s.plan_version_id JOIN odca.plan_entitlements e ON e.plan_version_id=p.id LEFT JOIN odca.tenant_storage_usage u ON u.tenant_id=t.id LEFT JOIN odca.audit_events a ON a.tenant_id=t.id
 WHERE NOT t.is_deleted AND(search IS NULL OR t.display_name ILIKE '%'||search||'%' OR t.business_code ILIKE '%'||search||'%')
  AND (plan_code IS NULL OR p.code=plan_code) AND (tenant_status IS NULL OR t.status=tenant_status)
 GROUP BY t.id,p.id,s.id,u.tenant_id ORDER BY t.display_name; END $$;
DROP FUNCTION odca.platform_consumption_customers(uuid,text);
REVOKE ALL ON FUNCTION odca.platform_consumption_customers(uuid,text,text,text) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION odca.platform_consumption_customers(uuid,text,text,text) TO odca_app;

-- Busca global de usuarios (identidade + vinculos por organizacao).
CREATE FUNCTION odca.platform_user_search(actor uuid,search text DEFAULT NULL,limit_count integer DEFAULT 50)
RETURNS TABLE("UserId" uuid,"Email" text,"DisplayName" text,"IsPlatformAdministrator" boolean,"IsDeleted" boolean,"TenantId" uuid,"TenantName" text,"MembershipStatus" text,"RoleCode" text)
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,odca AS $$ BEGIN PERFORM odca.assert_platform_actor(actor); RETURN QUERY
 SELECT u.id,u.email,u.display_name,u.is_platform_administrator,u.is_deleted,m.tenant_id,t.display_name,m.status,r.code
 FROM odca.users u
 LEFT JOIN odca.memberships m ON m.user_id=u.id
 LEFT JOIN odca.tenants t ON t.id=m.tenant_id
 LEFT JOIN LATERAL (SELECT mr2.role_id FROM odca.member_roles mr2 WHERE mr2.user_id=u.id AND m.tenant_id IS NOT NULL AND mr2.tenant_id=m.tenant_id LIMIT 1) mr ON true
 LEFT JOIN odca.roles r ON r.id=mr.role_id
 WHERE (search IS NULL OR u.email ILIKE '%'||search||'%' OR u.display_name ILIKE '%'||search||'%')
 ORDER BY u.email,m.tenant_id
 LIMIT coalesce(nullif(limit_count,0),50); END $$;
REVOKE ALL ON FUNCTION odca.platform_user_search(uuid,text,integer) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION odca.platform_user_search(uuid,text,integer) TO odca_app;

-- Publicar/retirar modelos do catalogo global oficial (gestao da plataforma).
CREATE FUNCTION odca.platform_catalog_set_status(actor uuid,p_official_key text,action text,reason text)
RETURNS jsonb LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,odca AS $fn$
DECLARE t odca.contract_templates%rowtype; v_reason text:=btrim(coalesce(reason,'')); v_new text;
BEGIN
 PERFORM odca.assert_platform_actor(actor);
 IF action IS NULL OR action NOT IN('publish','retire')
 THEN RETURN jsonb_build_object('error','Acao invalida.','code','catalog.action_invalid'); END IF;
 IF length(v_reason)<5 OR length(v_reason)>500
 THEN RETURN jsonb_build_object('error','Justificativa deve ter entre 5 e 500 caracteres.','code','catalog.reason'); END IF;
 SELECT * INTO t FROM odca.contract_templates WHERE owner_tenant_id IS NULL AND scope='global' AND official_key=btrim(coalesce(p_official_key,'')) FOR UPDATE;
 IF NOT FOUND THEN RETURN jsonb_build_object('error','Modelo nao existe no catalogo global da plataforma.','code','catalog.not_found'); END IF;
 v_new := CASE WHEN action='retire' THEN 'archived' ELSE 'published' END;
 IF t.status=v_new THEN RETURN jsonb_build_object('ok',true,'noOp',true,'key',t.official_key,'status',v_new,'rowVersion',t.row_version); END IF;
 UPDATE odca.contract_templates SET status=v_new,row_version=row_version+1 WHERE id=t.id;
 INSERT INTO odca.audit_events(scope_type,tenant_id,actor_user_id,action,entity_type,entity_id,result,metadata)
  VALUES('platform',NULL,actor,'template.catalog.'||v_new,'contract_template',t.id,'success',jsonb_build_object('officialKey',t.official_key,'reason',v_reason));
 RETURN jsonb_build_object('ok',true,'noOp',false,'key',t.official_key,'status',v_new,'rowVersion',t.row_version+1);
END $fn$;
REVOKE ALL ON FUNCTION odca.platform_catalog_set_status(uuid,text,text,text) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION odca.platform_catalog_set_status(uuid,text,text,text) TO odca_app;

INSERT INTO odca.schema_migrations(version,name,checksum)
VALUES(45,'Operacao contratual B: suporte tecnico em todos os planos, catalogo global publicavel/retiravel, busca global de usuarios e filtros de clientes','3ced09a710ec053f9fb10b97d803e7f3e204e439c7e07014d5e2c02b321bef0b')
ON CONFLICT(version) DO NOTHING;
COMMIT;
-- ODCA-END 045
-- ODCA-MIGRATION 046 CHECKSUM 12831bab266a05dfc1dfeeb5ca75a1ad8f316c5258725b05af7f5a690f849241
BEGIN;
SELECT pg_advisory_xact_lock(hashtext('odca.schema.migrations'));

-- Operacao contratual C: revisao administrativa como terceiro tipo de alteracao de
-- vigencia (ao lado de renovacao e aditivo). Sem cambio de semantica de aplicacao.

ALTER TABLE odca.contract_change_requests DROP CONSTRAINT IF EXISTS contract_change_requests_kind_check;
ALTER TABLE odca.contract_change_requests ADD CONSTRAINT contract_change_requests_kind_check CHECK(kind IN ('renewal','amendment','revision'));

INSERT INTO odca.schema_migrations(version,name,checksum)
VALUES(46,'Operacao contratual C: revisao administrativa como tipo de alteracao de vigencia','12831bab266a05dfc1dfeeb5ca75a1ad8f316c5258725b05af7f5a690f849241')
ON CONFLICT(version) DO NOTHING;
COMMIT;
-- ODCA-END 046
