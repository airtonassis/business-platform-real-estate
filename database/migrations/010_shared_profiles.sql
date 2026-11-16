BEGIN;
ALTER TABLE real_estate.owners ADD COLUMN version bigint NOT NULL DEFAULT 1 CHECK(version>0), ADD COLUMN history jsonb NOT NULL DEFAULT '[]'::jsonb CHECK(jsonb_typeof(history)='array');
ALTER TABLE real_estate.condominiums ADD COLUMN version bigint NOT NULL DEFAULT 1 CHECK(version>0), ADD COLUMN history jsonb NOT NULL DEFAULT '[]'::jsonb CHECK(jsonb_typeof(history)='array'), ADD COLUMN characteristics jsonb NOT NULL DEFAULT '[]'::jsonb CHECK(jsonb_typeof(characteristics)='array');
INSERT INTO real_estate.schema_versions(version) VALUES(10);
COMMIT;
