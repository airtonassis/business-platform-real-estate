-- Minimal reference projections consumed by the Commercial Agenda.
-- Existing RLS/FORCE RLS policies remain authoritative for organization and establishment scope.
BEGIN;

GRANT SELECT (organization_id, id, name, active)
  ON TABLE real_estate.owners TO erp_runtime;

GRANT SELECT (organization_id, establishment_id, id, status, code, commercial_code, title)
  ON TABLE real_estate.properties TO erp_runtime;

INSERT INTO real_estate.schema_versions(version) VALUES (32);

COMMIT;
