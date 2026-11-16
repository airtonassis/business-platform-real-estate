-- CAD-1B: additive optional identification, no backfill, grants or RLS changes.
BEGIN;
ALTER TABLE real_estate.properties
 ADD COLUMN summary text CHECK(length(summary)<=2000),
 ADD COLUMN internal_reference text CHECK(length(internal_reference)<=100),
 ADD COLUMN is_in_condominium boolean;
ALTER TABLE real_estate.properties DROP CONSTRAINT properties_purpose_check;
ALTER TABLE real_estate.properties ADD CONSTRAINT properties_purpose_check
 CHECK(purpose IN ('Venda','Locação','Temporada','Permuta','Lançamento','Venda e locação'));
INSERT INTO real_estate.schema_versions(version) VALUES(6);
COMMIT;
