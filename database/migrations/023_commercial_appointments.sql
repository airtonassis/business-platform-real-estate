-- IMOB-03 Etapa 2. Prepared only; requires operational authorization for persistent use.
BEGIN;
CREATE TABLE real_estate.commercial_appointments(
 organization_id uuid NOT NULL,establishment_id uuid NOT NULL,id uuid NOT NULL,request_id uuid NOT NULL,
 customer_id uuid,owner_id uuid,property_id uuid,kind text NOT NULL CHECK(kind IN ('Visita ao imóvel','Atendimento na imobiliária','Visita de captação')),
 status text NOT NULL DEFAULT 'Agendado' CHECK(status IN ('Agendado','Cancelado','Realizado')),
 responsible_subject text NOT NULL,responsible_name text NOT NULL,
 starts_at timestamptz NOT NULL,ends_at timestamptz NOT NULL CHECK(ends_at>starts_at),
 location text NOT NULL CHECK(length(btrim(location)) BETWEEN 1 AND 300),notes text,result text,
 version bigint NOT NULL DEFAULT 1 CHECK(version>0),created_by text NOT NULL,created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
 updated_by text NOT NULL,updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
 PRIMARY KEY(organization_id,id),UNIQUE(organization_id,establishment_id,request_id),
 CHECK((customer_id IS NOT NULL) <> (owner_id IS NOT NULL)),CHECK(kind='Visita de captação' OR customer_id IS NOT NULL),
 FOREIGN KEY(organization_id,customer_id,establishment_id) REFERENCES real_estate.customers(organization_id,id,establishment_id),
 FOREIGN KEY(organization_id,owner_id) REFERENCES real_estate.owners(organization_id,id),
 FOREIGN KEY(organization_id,property_id) REFERENCES real_estate.properties(organization_id,id));
CREATE INDEX commercial_appointments_unit_time ON real_estate.commercial_appointments(organization_id,establishment_id,starts_at,id);
CREATE INDEX commercial_appointments_customer_time ON real_estate.commercial_appointments(organization_id,establishment_id,customer_id,starts_at DESC);
CREATE TABLE real_estate.commercial_appointment_events(
 organization_id uuid NOT NULL,establishment_id uuid NOT NULL,id uuid NOT NULL,appointment_id uuid NOT NULL,version bigint NOT NULL,
 action text NOT NULL CHECK(action IN ('Criado','Reagendado','Cancelado','Realizado')),
 at timestamptz NOT NULL DEFAULT clock_timestamp(),actor text NOT NULL,responsible_subject text NOT NULL,
 starts_at timestamptz NOT NULL,ends_at timestamptz NOT NULL,result text,
 PRIMARY KEY(organization_id,id),UNIQUE(organization_id,appointment_id,version),
 FOREIGN KEY(organization_id,appointment_id) REFERENCES real_estate.commercial_appointments(organization_id,id));
DO $body$ DECLARE t text; BEGIN
 FOREACH t IN ARRAY ARRAY['commercial_appointments','commercial_appointment_events'] LOOP
  EXECUTE format('ALTER TABLE real_estate.%I ENABLE ROW LEVEL SECURITY',t);
  EXECUTE format('ALTER TABLE real_estate.%I FORCE ROW LEVEL SECURITY',t);
  EXECUTE format($policy$CREATE POLICY commercial_appointment_scope ON real_estate.%I
   USING(organization_id=nullif(current_setting('erp.organization_id',true),'')::uuid
     AND establishment_id=nullif(current_setting('erp.active_establishment',true),'')::uuid
     AND establishment_id=ANY(coalesce(nullif(current_setting('erp.establishments',true),''),'{}')::uuid[]))
   WITH CHECK(organization_id=nullif(current_setting('erp.organization_id',true),'')::uuid
     AND establishment_id=nullif(current_setting('erp.active_establishment',true),'')::uuid
     AND establishment_id=ANY(coalesce(nullif(current_setting('erp.establishments',true),''),'{}')::uuid[]))$policy$,t);
 END LOOP;
END $body$;
CREATE TRIGGER commercial_appointment_events_immutable BEFORE UPDATE OR DELETE ON real_estate.commercial_appointment_events FOR EACH ROW EXECUTE FUNCTION real_estate.reject_customer_history_change();
REVOKE ALL ON real_estate.commercial_appointments,real_estate.commercial_appointment_events FROM PUBLIC;
INSERT INTO real_estate.schema_versions(version) VALUES(23);
COMMIT;
