-- Additive typed capture aggregate. Existing property RLS/version/audit remain authoritative.
BEGIN;
ALTER TABLE real_estate.properties ADD COLUMN capture jsonb;
ALTER TABLE real_estate.properties ADD CONSTRAINT capture_object CHECK(capture IS NULL OR jsonb_typeof(capture)='object');
INSERT INTO real_estate.schema_versions(version) VALUES(7);
COMMIT;
