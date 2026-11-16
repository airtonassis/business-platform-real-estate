-- Additive address evolution. No legacy data parsing/backfill or policy changes.
BEGIN;
ALTER TABLE real_estate.properties
 ALTER COLUMN address DROP NOT NULL,
 ALTER COLUMN postal_code DROP NOT NULL,
 ALTER COLUMN latitude DROP NOT NULL,
 ALTER COLUMN longitude DROP NOT NULL,
 ADD COLUMN address_ibge text,
 ADD COLUMN address_uf text,
 ADD COLUMN address_municipality text,
 ADD COLUMN address_street text,
 ADD COLUMN address_number text,
 ADD COLUMN address_without_number boolean,
 ADD COLUMN address_complement text,
 ADD COLUMN address_neighborhood text,
 ADD COLUMN address_country text,
 ADD COLUMN coordinate_source text,
 ADD CONSTRAINT address_ibge_format CHECK(address_ibge IS NULL OR address_ibge ~ '^[0-9]{7}$'),
 ADD CONSTRAINT address_uf_valid CHECK(address_uf IS NULL OR address_uf IN ('AC','AL','AP','AM','BA','CE','DF','ES','GO','MA','MT','MS','MG','PA','PB','PR','PE','PI','RJ','RN','RS','RO','RR','SC','SP','SE','TO')),
 ADD CONSTRAINT address_country_format CHECK(address_country IS NULL OR address_country ~ '^[A-Z]{2}$'),
 ADD CONSTRAINT address_sn_consistency CHECK(address_without_number IS DISTINCT FROM true OR address_number IS NULL),
 ADD CONSTRAINT address_coordinate_pair CHECK((latitude IS NULL)=(longitude IS NULL)),
 ADD CONSTRAINT address_text_limits CHECK(
  (address_municipality IS NULL OR length(btrim(address_municipality)) BETWEEN 1 AND 200) AND
  (address_street IS NULL OR length(btrim(address_street)) BETWEEN 1 AND 300) AND
  (address_number IS NULL OR length(btrim(address_number)) BETWEEN 1 AND 50) AND
  (address_complement IS NULL OR length(btrim(address_complement)) BETWEEN 1 AND 200) AND
  (address_neighborhood IS NULL OR length(btrim(address_neighborhood)) BETWEEN 1 AND 200) AND
  (coordinate_source IS NULL OR length(btrim(coordinate_source)) BETWEEN 1 AND 100)),
 ADD CONSTRAINT address_partial_only_draft CHECK(status='Draft' OR
  (address IS NOT NULL AND postal_code IS NOT NULL AND latitude IS NOT NULL AND longitude IS NOT NULL));
INSERT INTO real_estate.schema_versions(version) VALUES(3);
COMMIT;
