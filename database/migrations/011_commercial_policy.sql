BEGIN;
CREATE TABLE real_estate.commercial_policy_revisions(
 organization_id uuid NOT NULL,id uuid NOT NULL,version bigint NOT NULL DEFAULT 1 CHECK(version>0),
 status text NOT NULL CHECK(status IN ('Draft','Approved')),name text NOT NULL,reason text NOT NULL,
 data jsonb NOT NULL CHECK(jsonb_typeof(data)='object'),property_id uuid,base_revision_id uuid,
 requested_by text NOT NULL,approved_by text,created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
 history jsonb NOT NULL DEFAULT '[]'::jsonb CHECK(jsonb_typeof(history)='array'),
 PRIMARY KEY(organization_id,id),
 FOREIGN KEY(organization_id,property_id) REFERENCES real_estate.properties(organization_id,id),
 FOREIGN KEY(organization_id,base_revision_id) REFERENCES real_estate.commercial_policy_revisions(organization_id,id),
 CHECK(status<>'Approved' OR approved_by IS NOT NULL AND approved_by<>requested_by));
ALTER TABLE real_estate.commercial_policy_revisions ENABLE ROW LEVEL SECURITY;
ALTER TABLE real_estate.commercial_policy_revisions FORCE ROW LEVEL SECURITY;
CREATE POLICY organization_scope ON real_estate.commercial_policy_revisions USING(organization_id=nullif(current_setting('erp.organization_id',true),'')::uuid) WITH CHECK(organization_id=nullif(current_setting('erp.organization_id',true),'')::uuid);
CREATE UNIQUE INDEX commercial_policy_general_successor ON real_estate.commercial_policy_revisions(organization_id,base_revision_id) WHERE status='Approved' AND property_id IS NULL AND base_revision_id IS NOT NULL;
CREATE UNIQUE INDEX commercial_policy_first_general ON real_estate.commercial_policy_revisions(organization_id) WHERE status='Approved' AND property_id IS NULL AND base_revision_id IS NULL;
CREATE TABLE real_estate.commercial_policy_applications(
 organization_id uuid NOT NULL,id uuid NOT NULL,property_id uuid NOT NULL,property_version bigint NOT NULL,
 revision_id uuid NOT NULL,revision_version bigint NOT NULL,actor text NOT NULL,occurred_at timestamptz NOT NULL DEFAULT clock_timestamp(),
 PRIMARY KEY(organization_id,id),UNIQUE(organization_id,property_id,property_version),
 FOREIGN KEY(organization_id,property_id) REFERENCES real_estate.properties(organization_id,id),
 FOREIGN KEY(organization_id,revision_id) REFERENCES real_estate.commercial_policy_revisions(organization_id,id));
ALTER TABLE real_estate.commercial_policy_applications ENABLE ROW LEVEL SECURITY;
ALTER TABLE real_estate.commercial_policy_applications FORCE ROW LEVEL SECURITY;
CREATE POLICY organization_scope ON real_estate.commercial_policy_applications USING(organization_id=nullif(current_setting('erp.organization_id',true),'')::uuid) WITH CHECK(organization_id=nullif(current_setting('erp.organization_id',true),'')::uuid);
INSERT INTO real_estate.schema_versions(version) VALUES(11);
COMMIT;
