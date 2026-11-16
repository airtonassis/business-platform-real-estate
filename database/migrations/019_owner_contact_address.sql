BEGIN;
-- Additive owner data; UUID, fiscal uniqueness, RLS and existing records remain intact.
ALTER TABLE real_estate.owners ADD COLUMN details jsonb NULL CHECK(details IS NULL OR jsonb_typeof(details)='object');
INSERT INTO real_estate.schema_versions(version) VALUES(19);
COMMIT;
