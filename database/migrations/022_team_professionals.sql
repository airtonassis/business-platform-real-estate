-- EQP-03.A v1: product-owned professional data; no identity or permissions copy.
BEGIN;
CREATE TABLE real_estate.team_professionals(
 organization_id uuid NOT NULL,establishment_id uuid NOT NULL,directory_id uuid NOT NULL,
 data jsonb NOT NULL CHECK(jsonb_typeof(data)='object'),version bigint NOT NULL CHECK(version>0),
 updated_by text NOT NULL,updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
 PRIMARY KEY(organization_id,establishment_id,directory_id));
ALTER TABLE real_estate.team_professionals ENABLE ROW LEVEL SECURITY;
ALTER TABLE real_estate.team_professionals FORCE ROW LEVEL SECURITY;
CREATE POLICY team_scope ON real_estate.team_professionals
 USING(current_setting('erp.legacy_manager',true)='enabled' AND organization_id=nullif(current_setting('erp.organization_id',true),'')::uuid
 AND establishment_id=nullif(current_setting('erp.active_establishment',true),'')::uuid
 AND establishment_id=ANY(coalesce(nullif(current_setting('erp.establishments',true),''),'{}')::uuid[]))
 WITH CHECK(current_setting('erp.legacy_manager',true)='enabled' AND organization_id=nullif(current_setting('erp.organization_id',true),'')::uuid
 AND establishment_id=nullif(current_setting('erp.active_establishment',true),'')::uuid
 AND establishment_id=ANY(coalesce(nullif(current_setting('erp.establishments',true),''),'{}')::uuid[]));
REVOKE ALL ON real_estate.team_professionals FROM PUBLIC;
INSERT INTO real_estate.schema_versions(version) VALUES(22);
COMMIT;

