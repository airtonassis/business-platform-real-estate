-- S05-A.1: persistent responsibility assignment and immutable transfer history.
BEGIN;
ALTER TABLE real_estate.commercial_negotiations
 ADD COLUMN responsible_membership_id uuid;
ALTER TABLE real_estate.commercial_negotiations
 DROP CONSTRAINT IF EXISTS commercial_negotiations_version_check;
ALTER TABLE real_estate.commercial_negotiations
 ADD CONSTRAINT commercial_negotiations_version_positive CHECK(version>0);
ALTER TABLE real_estate.commercial_negotiation_events
 ADD COLUMN request_id uuid,
 ADD COLUMN previous_responsible_subject text,
 ADD COLUMN responsible_subject text,
 ADD COLUMN previous_responsible_membership_id uuid,
 ADD COLUMN responsible_membership_id uuid,
 ADD COLUMN reason text;
ALTER TABLE real_estate.commercial_negotiation_events
 DROP CONSTRAINT IF EXISTS commercial_negotiation_events_action_check,
 DROP CONSTRAINT IF EXISTS commercial_negotiation_events_version_check;
ALTER TABLE real_estate.commercial_negotiation_events
 ADD CONSTRAINT commercial_negotiation_events_action_valid CHECK(action IN ('Opened','ResponsibilityTransferred','ResponsibilityRecovered')),
 ADD CONSTRAINT commercial_negotiation_events_version_positive CHECK(version>0),
 ADD CONSTRAINT commercial_negotiation_events_reason_length CHECK(reason IS NULL OR length(reason)<=500);
CREATE UNIQUE INDEX commercial_negotiation_events_request ON real_estate.commercial_negotiation_events(organization_id,negotiation_id,request_id) WHERE request_id IS NOT NULL;
GRANT UPDATE(responsible_subject,responsible_membership_id,version) ON real_estate.commercial_negotiations TO erp_runtime;
INSERT INTO real_estate.schema_versions(version) VALUES(27);
COMMIT;
