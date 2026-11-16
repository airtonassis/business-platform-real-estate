-- Sprint 04: internal disclosure decisions only. No public endpoint or site delivery.
BEGIN;
CREATE TABLE real_estate.showcase_entries (
 organization_id uuid NOT NULL, property_id uuid NOT NULL, establishment_id uuid NOT NULL,
 version bigint NOT NULL DEFAULT 1 CHECK (version > 0),
 state text NOT NULL CHECK (state IN ('Prepared','Authorized','Withdrawn')),
 selection jsonb NOT NULL, updated_by text NOT NULL,
 updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
 PRIMARY KEY (organization_id, property_id),
 FOREIGN KEY (organization_id, property_id) REFERENCES real_estate.properties(organization_id, id)
);
CREATE TABLE real_estate.showcase_history (
 organization_id uuid NOT NULL, id uuid NOT NULL, property_id uuid NOT NULL,
 establishment_id uuid NOT NULL, version bigint NOT NULL,
 action text NOT NULL CHECK (action IN ('prepare','authorize','withdraw')),
 state text NOT NULL, selection jsonb NOT NULL, actor text NOT NULL,
 occurred_at timestamptz NOT NULL DEFAULT clock_timestamp(),
 PRIMARY KEY (organization_id, id),
 UNIQUE (organization_id, property_id, version),
 FOREIGN KEY (organization_id, property_id) REFERENCES real_estate.properties(organization_id, id)
);
CREATE FUNCTION real_estate.reject_showcase_history_change() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN RAISE EXCEPTION 'Showcase history is append-only' USING ERRCODE='42501'; END $$;
CREATE TRIGGER showcase_history_immutable BEFORE UPDATE OR DELETE ON real_estate.showcase_history
 FOR EACH ROW EXECUTE FUNCTION real_estate.reject_showcase_history_change();
ALTER TABLE real_estate.showcase_entries ENABLE ROW LEVEL SECURITY;
ALTER TABLE real_estate.showcase_entries FORCE ROW LEVEL SECURITY;
ALTER TABLE real_estate.showcase_history ENABLE ROW LEVEL SECURITY;
ALTER TABLE real_estate.showcase_history FORCE ROW LEVEL SECURITY;
CREATE POLICY showcase_scope ON real_estate.showcase_entries
 USING (organization_id = nullif(current_setting('erp.organization_id',true),'')::uuid
    AND establishment_id = ANY (nullif(current_setting('erp.establishments',true),'')::uuid[]))
 WITH CHECK (organization_id = nullif(current_setting('erp.organization_id',true),'')::uuid
    AND establishment_id = ANY (nullif(current_setting('erp.establishments',true),'')::uuid[]));
CREATE POLICY showcase_scope ON real_estate.showcase_history
 USING (organization_id = nullif(current_setting('erp.organization_id',true),'')::uuid
    AND establishment_id = ANY (nullif(current_setting('erp.establishments',true),'')::uuid[]))
 WITH CHECK (organization_id = nullif(current_setting('erp.organization_id',true),'')::uuid
    AND establishment_id = ANY (nullif(current_setting('erp.establishments',true),'')::uuid[]));
INSERT INTO real_estate.schema_versions(version) VALUES(25);
COMMIT;
