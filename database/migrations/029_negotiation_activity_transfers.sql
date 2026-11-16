-- S05-B2: assisted transfers and explicit follow-up, prepared only.
BEGIN;

ALTER TABLE real_estate.commercial_appointment_events
  ADD COLUMN previous_responsible_subject text;
ALTER TABLE real_estate.commercial_appointment_events
  DROP CONSTRAINT IF EXISTS commercial_appointment_events_action_check;
ALTER TABLE real_estate.commercial_appointment_events
  ADD CONSTRAINT commercial_appointment_events_action_check
  CHECK(action IN ('Criado','Reagendado','Cancelado','Realizado','ResponsibilityTransferred'));

ALTER TABLE real_estate.commercial_negotiation_action_events
  ADD COLUMN previous_responsible_subject text;
ALTER TABLE real_estate.commercial_negotiation_action_events
  DROP CONSTRAINT IF EXISTS commercial_negotiation_action_events_event_check;
ALTER TABLE real_estate.commercial_negotiation_action_events
  ADD CONSTRAINT commercial_negotiation_action_events_event_check
  CHECK(event IN ('Criada','Concluída','ResponsibilityTransferred'));
ALTER TABLE real_estate.commercial_negotiation_actions
  ADD CONSTRAINT commercial_negotiation_actions_transfer_link UNIQUE(organization_id,establishment_id,id,negotiation_id,appointment_id);
ALTER TABLE real_estate.commercial_negotiation_actions
  ADD CONSTRAINT commercial_negotiation_actions_transfer_negotiation UNIQUE(organization_id,establishment_id,id,negotiation_id);

CREATE TABLE real_estate.commercial_negotiation_transfer_requests(
 organization_id uuid NOT NULL, establishment_id uuid NOT NULL, request_id uuid NOT NULL,
 negotiation_id uuid NOT NULL, content_fingerprint text NOT NULL CHECK(length(content_fingerprint)=64),
 receipt jsonb NOT NULL, actor text NOT NULL, created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
 PRIMARY KEY(organization_id,establishment_id,request_id),
 FOREIGN KEY(organization_id,establishment_id,negotiation_id)
   REFERENCES real_estate.commercial_negotiations(organization_id,establishment_id,id));

CREATE TABLE real_estate.commercial_negotiation_transfer_followups(
 organization_id uuid NOT NULL, establishment_id uuid NOT NULL, id uuid NOT NULL,
 negotiation_id uuid NOT NULL, action_id uuid NOT NULL, appointment_id uuid,
 reason text NOT NULL CHECK(reason IN ('ExistingAppointment','SelectedAppointment')),
 status text NOT NULL DEFAULT 'Pendente de tratamento' CHECK(status IN ('Pendente de tratamento','Resolvido')),
 responsible_subject text NOT NULL, original_responsible_subject text NOT NULL,
 created_by text NOT NULL, created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
 result text, version bigint NOT NULL DEFAULT 1 CHECK(version>0),
 updated_by text NOT NULL, updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
 PRIMARY KEY(organization_id,id),
 UNIQUE(organization_id,establishment_id,id),
 FOREIGN KEY(organization_id,establishment_id,negotiation_id)
   REFERENCES real_estate.commercial_negotiations(organization_id,establishment_id,id),
 FOREIGN KEY(organization_id,establishment_id,action_id,negotiation_id,appointment_id)
   REFERENCES real_estate.commercial_negotiation_actions(organization_id,establishment_id,id,negotiation_id,appointment_id),
 FOREIGN KEY(organization_id,establishment_id,action_id)
   REFERENCES real_estate.commercial_negotiation_actions(organization_id,establishment_id,id),
 FOREIGN KEY(organization_id,establishment_id,action_id,negotiation_id)
   REFERENCES real_estate.commercial_negotiation_actions(organization_id,establishment_id,id,negotiation_id),
 FOREIGN KEY(organization_id,establishment_id,appointment_id)
   REFERENCES real_estate.commercial_appointments(organization_id,establishment_id,id),
 CHECK((status='Resolvido')=(result IS NOT NULL) AND (result IS NULL OR length(btrim(result)) BETWEEN 1 AND 1000)));
CREATE UNIQUE INDEX commercial_negotiation_followup_pending_action
 ON real_estate.commercial_negotiation_transfer_followups(organization_id,establishment_id,action_id)
 WHERE status='Pendente de tratamento';
CREATE INDEX commercial_negotiation_followup_queue
 ON real_estate.commercial_negotiation_transfer_followups(organization_id,establishment_id,negotiation_id,status,created_at,id);

CREATE TABLE real_estate.commercial_negotiation_transfer_followup_events(
 organization_id uuid NOT NULL, establishment_id uuid NOT NULL, id uuid NOT NULL,
 followup_id uuid NOT NULL, version bigint NOT NULL CHECK(version>0), request_id uuid NOT NULL,
 event text NOT NULL CHECK(event IN ('Criado','Resolvido')), actor text NOT NULL,
 responsible_subject text NOT NULL, result text, at timestamptz NOT NULL DEFAULT clock_timestamp(),
 PRIMARY KEY(organization_id,id), UNIQUE(organization_id,establishment_id,followup_id,version),
 UNIQUE(organization_id,establishment_id,followup_id,request_id),
 FOREIGN KEY(organization_id,establishment_id,followup_id)
   REFERENCES real_estate.commercial_negotiation_transfer_followups(organization_id,establishment_id,id),
 CHECK((event='Resolvido')=(result IS NOT NULL)));

DO $body$ DECLARE t text; BEGIN
 FOREACH t IN ARRAY ARRAY['commercial_negotiation_transfer_requests','commercial_negotiation_transfer_followups','commercial_negotiation_transfer_followup_events'] LOOP
  EXECUTE format('ALTER TABLE real_estate.%I ENABLE ROW LEVEL SECURITY',t);
  EXECUTE format('ALTER TABLE real_estate.%I FORCE ROW LEVEL SECURITY',t);
  EXECUTE format($policy$CREATE POLICY negotiation_transfer_scope ON real_estate.%I
   USING(organization_id=nullif(current_setting('erp.organization_id',true),'')::uuid
     AND establishment_id=nullif(current_setting('erp.active_establishment',true),'')::uuid
     AND establishment_id=ANY(coalesce(nullif(current_setting('erp.establishments',true),''),'{}')::uuid[]))
   WITH CHECK(organization_id=nullif(current_setting('erp.organization_id',true),'')::uuid
     AND establishment_id=nullif(current_setting('erp.active_establishment',true),'')::uuid
     AND establishment_id=ANY(coalesce(nullif(current_setting('erp.establishments',true),''),'{}')::uuid[]))$policy$,t);
 END LOOP;
END $body$;

CREATE TRIGGER commercial_negotiation_transfer_followup_events_immutable
 BEFORE UPDATE OR DELETE ON real_estate.commercial_negotiation_transfer_followup_events
 FOR EACH ROW EXECUTE FUNCTION real_estate.reject_customer_history_change();

REVOKE ALL ON real_estate.commercial_negotiation_transfer_requests,
 real_estate.commercial_negotiation_transfer_followups,
 real_estate.commercial_negotiation_transfer_followup_events FROM PUBLIC;
-- Narrow column-level grants; no broad table UPDATE is introduced.
GRANT SELECT,INSERT ON real_estate.commercial_negotiation_transfer_requests TO erp_runtime;
GRANT SELECT,INSERT ON real_estate.commercial_negotiation_transfer_followups TO erp_runtime;
GRANT UPDATE(status,result,version,updated_by,updated_at) ON real_estate.commercial_negotiation_transfer_followups TO erp_runtime;
GRANT SELECT,INSERT ON real_estate.commercial_negotiation_transfer_followup_events TO erp_runtime;
GRANT UPDATE(responsible_subject,responsible_name,version,updated_by,updated_at) ON real_estate.commercial_appointments TO erp_runtime;
GRANT INSERT ON real_estate.commercial_appointment_events TO erp_runtime;
GRANT UPDATE(responsible_subject,responsible_name,version,updated_by,updated_at) ON real_estate.commercial_negotiation_actions TO erp_runtime;
GRANT INSERT ON real_estate.commercial_negotiation_action_events TO erp_runtime;
INSERT INTO real_estate.schema_versions(version) VALUES(29);
COMMIT;
