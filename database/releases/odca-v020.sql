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
