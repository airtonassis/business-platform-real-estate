-- S05-A: additive negotiation identity. Prepared for an isolated, authorized application only.
BEGIN;
CREATE TABLE real_estate.commercial_negotiations(
 organization_id uuid NOT NULL,establishment_id uuid NOT NULL,id uuid NOT NULL,request_id uuid NOT NULL,
 customer_id uuid NOT NULL,property_id uuid NOT NULL,announcement_id uuid NOT NULL,owner_id uuid NOT NULL,
 responsible_subject text NOT NULL CHECK(length(btrim(responsible_subject))>0),
 status text NOT NULL DEFAULT 'Preparing' CHECK(status='Preparing'),version bigint NOT NULL DEFAULT 1 CHECK(version=1),
 created_by text NOT NULL,created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
 PRIMARY KEY(organization_id,id),UNIQUE(organization_id,establishment_id,request_id),
 FOREIGN KEY(organization_id,customer_id,establishment_id) REFERENCES real_estate.customers(organization_id,id,establishment_id),
 FOREIGN KEY(organization_id,property_id) REFERENCES real_estate.properties(organization_id,id),
 FOREIGN KEY(organization_id,announcement_id) REFERENCES real_estate.property_announcements(organization_id,id),
 FOREIGN KEY(organization_id,owner_id) REFERENCES real_estate.owners(organization_id,id));
CREATE INDEX commercial_negotiations_customer ON real_estate.commercial_negotiations(organization_id,establishment_id,customer_id,created_at DESC,id);
CREATE TABLE real_estate.commercial_negotiation_events(
 organization_id uuid NOT NULL,establishment_id uuid NOT NULL,id uuid NOT NULL,negotiation_id uuid NOT NULL,
 version bigint NOT NULL CHECK(version=1),action text NOT NULL CHECK(action='Opened'),actor text NOT NULL,
 at timestamptz NOT NULL DEFAULT clock_timestamp(),PRIMARY KEY(organization_id,id),
 UNIQUE(organization_id,negotiation_id,version),
 FOREIGN KEY(organization_id,negotiation_id) REFERENCES real_estate.commercial_negotiations(organization_id,id));
DO $body$ DECLARE t text; BEGIN
 FOREACH t IN ARRAY ARRAY['commercial_negotiations','commercial_negotiation_events'] LOOP
  EXECUTE format('ALTER TABLE real_estate.%I ENABLE ROW LEVEL SECURITY',t);
  EXECUTE format('ALTER TABLE real_estate.%I FORCE ROW LEVEL SECURITY',t);
  EXECUTE format($policy$CREATE POLICY negotiation_scope ON real_estate.%I
   USING(organization_id=nullif(current_setting('erp.organization_id',true),'')::uuid
     AND establishment_id=nullif(current_setting('erp.active_establishment',true),'')::uuid
     AND establishment_id=ANY(coalesce(nullif(current_setting('erp.establishments',true),''),'{}')::uuid[]))
   WITH CHECK(organization_id=nullif(current_setting('erp.organization_id',true),'')::uuid
     AND establishment_id=nullif(current_setting('erp.active_establishment',true),'')::uuid
     AND establishment_id=ANY(coalesce(nullif(current_setting('erp.establishments',true),''),'{}')::uuid[]))$policy$,t);
 END LOOP;
END $body$;
CREATE TRIGGER commercial_negotiation_events_immutable BEFORE UPDATE OR DELETE ON real_estate.commercial_negotiation_events
 FOR EACH ROW EXECUTE FUNCTION real_estate.reject_customer_history_change();
REVOKE ALL ON real_estate.commercial_negotiations,real_estate.commercial_negotiation_events FROM PUBLIC;
-- Runtime role needs only the two operations used by S05-A; application rights remain Core-owned.
GRANT SELECT,INSERT ON real_estate.commercial_negotiations TO erp_runtime;
GRANT SELECT,INSERT ON real_estate.commercial_negotiation_events TO erp_runtime;
INSERT INTO real_estate.schema_versions(version) VALUES(26);
COMMIT;
