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
