BEGIN;
CREATE TABLE real_estate.condominiums(
 organization_id uuid NOT NULL,id uuid NOT NULL,name text NOT NULL CHECK(length(btrim(name)) BETWEEN 1 AND 200),
 kind text NOT NULL CHECK(kind IN ('residential','commercial','mixed','other')),
 towers integer CHECK(towers>=0 AND towers<=100000),floors integer CHECK(floors>=0 AND floors<=100000),notes text CHECK(length(notes)<=2000),
 created_by text NOT NULL,created_at timestamptz NOT NULL DEFAULT clock_timestamp(),PRIMARY KEY(organization_id,id));
ALTER TABLE real_estate.condominiums ENABLE ROW LEVEL SECURITY;
ALTER TABLE real_estate.condominiums FORCE ROW LEVEL SECURITY;
CREATE POLICY organization_scope ON real_estate.condominiums USING(organization_id=nullif(current_setting('erp.organization_id',true),'')::uuid) WITH CHECK(organization_id=nullif(current_setting('erp.organization_id',true),'')::uuid);
INSERT INTO real_estate.schema_versions(version) VALUES(9);
COMMIT;
