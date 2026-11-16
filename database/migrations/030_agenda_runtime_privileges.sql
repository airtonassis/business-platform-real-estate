-- S05 persistence gate: minimum runtime privileges for the existing Commercial Agenda.
BEGIN;

GRANT USAGE ON SCHEMA real_estate TO erp_runtime;
GRANT SELECT, INSERT ON TABLE real_estate.commercial_appointments TO erp_runtime;
GRANT SELECT ON TABLE real_estate.commercial_appointment_events TO erp_runtime;
GRANT UPDATE (status, starts_at, ends_at, location, notes, result)
  ON TABLE real_estate.commercial_appointments TO erp_runtime;

INSERT INTO real_estate.schema_versions(version) VALUES(30);
COMMIT;
