-- Minimum customer projection consumed by the Commercial Agenda.
-- Existing customer RLS/FORCE RLS policies remain authoritative for organization and establishment scope.
BEGIN;

GRANT SELECT (organization_id, id, establishment_id, name, responsible_subject)
  ON TABLE real_estate.customers TO erp_runtime;

INSERT INTO real_estate.schema_versions(version) VALUES (31);

COMMIT;
