-- S05-B1: actions belong to one negotiation; a linked commitment is a new Agenda appointment.
BEGIN;
ALTER TABLE real_estate.commercial_negotiations
 ADD CONSTRAINT commercial_negotiations_org_unit_id UNIQUE(organization_id,establishment_id,id);
ALTER TABLE real_estate.commercial_negotiations
 ADD CONSTRAINT commercial_negotiations_action_context UNIQUE(organization_id,establishment_id,id,customer_id,property_id);
ALTER TABLE real_estate.commercial_appointments
 ADD CONSTRAINT commercial_appointments_org_unit_id UNIQUE(organization_id,establishment_id,id);
ALTER TABLE real_estate.commercial_appointments
 ADD CONSTRAINT commercial_appointments_customer_context UNIQUE(organization_id,establishment_id,id,customer_id);

CREATE TABLE real_estate.commercial_negotiation_actions(
 organization_id uuid NOT NULL,establishment_id uuid NOT NULL,id uuid NOT NULL,negotiation_id uuid NOT NULL,customer_id uuid NOT NULL,property_id uuid NOT NULL,request_id uuid NOT NULL,
 appointment_id uuid,kind text NOT NULL CHECK(kind IN ('Contato','Compromisso','Pendência','Outros')),
 description text NOT NULL CHECK(length(btrim(description)) BETWEEN 1 AND 4000),due_at timestamptz,
 responsible_subject text NOT NULL CHECK(length(btrim(responsible_subject))>0),responsible_name text NOT NULL CHECK(length(btrim(responsible_name))>0),
 status text NOT NULL DEFAULT 'Aberta' CHECK(status IN ('Aberta','Concluída')),result text,
 created_by text NOT NULL,created_at timestamptz NOT NULL DEFAULT clock_timestamp(),updated_by text NOT NULL,updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
 version bigint NOT NULL DEFAULT 1 CHECK(version>0),PRIMARY KEY(organization_id,id),
 CONSTRAINT commercial_negotiation_actions_org_unit_id UNIQUE(organization_id,establishment_id,id),
 UNIQUE(organization_id,establishment_id,request_id),UNIQUE(organization_id,appointment_id),
 CHECK((kind IN ('Contato','Pendência'))=(due_at IS NOT NULL)),
 CHECK(kind<>'Outros' OR length(btrim(description))>=12),
 CHECK((kind='Compromisso')=(appointment_id IS NOT NULL)),
 CHECK((status='Concluída')=(result IS NOT NULL) AND (result IS NULL OR length(btrim(result)) BETWEEN 1 AND 1000)),
 FOREIGN KEY(organization_id,establishment_id,negotiation_id,customer_id,property_id)
   REFERENCES real_estate.commercial_negotiations(organization_id,establishment_id,id,customer_id,property_id),
 FOREIGN KEY(organization_id,establishment_id,appointment_id,customer_id)
   REFERENCES real_estate.commercial_appointments(organization_id,establishment_id,id,customer_id));
CREATE INDEX commercial_negotiation_actions_open ON real_estate.commercial_negotiation_actions(organization_id,establishment_id,negotiation_id,status,due_at,id);

CREATE TABLE real_estate.commercial_negotiation_action_events(
 organization_id uuid NOT NULL,establishment_id uuid NOT NULL,id uuid NOT NULL,action_id uuid NOT NULL,version bigint NOT NULL CHECK(version>0),request_id uuid NOT NULL,
 event text NOT NULL CHECK(event IN ('Criada','Concluída')),actor text NOT NULL,responsible_subject text NOT NULL,
 description text NOT NULL,due_at timestamptz,result text CHECK(result IS NULL OR length(btrim(result)) BETWEEN 1 AND 1000),at timestamptz NOT NULL DEFAULT clock_timestamp(),
 CHECK((event='Concluída')=(result IS NOT NULL)),
 PRIMARY KEY(organization_id,id),UNIQUE(organization_id,action_id,version),UNIQUE(organization_id,action_id,request_id),
 FOREIGN KEY(organization_id,establishment_id,action_id)
   REFERENCES real_estate.commercial_negotiation_actions(organization_id,establishment_id,id));

DO $body$ DECLARE t text; BEGIN
 FOREACH t IN ARRAY ARRAY['commercial_negotiation_actions','commercial_negotiation_action_events'] LOOP
  EXECUTE format('ALTER TABLE real_estate.%I ENABLE ROW LEVEL SECURITY',t);
  EXECUTE format('ALTER TABLE real_estate.%I FORCE ROW LEVEL SECURITY',t);
  EXECUTE format($policy$CREATE POLICY negotiation_action_scope ON real_estate.%I
   USING(organization_id=nullif(current_setting('erp.organization_id',true),'')::uuid
     AND establishment_id=nullif(current_setting('erp.active_establishment',true),'')::uuid
     AND establishment_id=ANY(coalesce(nullif(current_setting('erp.establishments',true),''),'{}')::uuid[]))
   WITH CHECK(organization_id=nullif(current_setting('erp.organization_id',true),'')::uuid
     AND establishment_id=nullif(current_setting('erp.active_establishment',true),'')::uuid
     AND establishment_id=ANY(coalesce(nullif(current_setting('erp.establishments',true),''),'{}')::uuid[]))$policy$,t);
 END LOOP;
END $body$;
CREATE TRIGGER commercial_negotiation_action_events_immutable BEFORE UPDATE OR DELETE ON real_estate.commercial_negotiation_action_events
 FOR EACH ROW EXECUTE FUNCTION real_estate.reject_customer_history_change();
REVOKE ALL ON real_estate.commercial_negotiation_actions,real_estate.commercial_negotiation_action_events FROM PUBLIC;
GRANT SELECT,INSERT ON real_estate.commercial_negotiation_actions TO erp_runtime;
GRANT UPDATE(status,result,version,updated_by,updated_at) ON real_estate.commercial_negotiation_actions TO erp_runtime;
GRANT SELECT,INSERT ON real_estate.commercial_negotiation_action_events TO erp_runtime;
INSERT INTO real_estate.schema_versions(version) VALUES(28);
COMMIT;
