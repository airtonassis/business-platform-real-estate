BEGIN;
ALTER TABLE real_estate.owners ADD COLUMN person_kind text, ADD COLUMN tax_id text;
ALTER TABLE real_estate.owners ADD CONSTRAINT owner_tax_shape CHECK((person_kind IS NULL AND tax_id IS NULL) OR (person_kind IS NOT NULL AND tax_id IS NOT NULL AND ((person_kind='PF' AND tax_id ~ '^[0-9]{11}$') OR (person_kind='PJ' AND tax_id ~ '^[0-9]{14}$'))));
CREATE UNIQUE INDEX owner_tax_unique ON real_estate.owners(organization_id,tax_id) WHERE tax_id IS NOT NULL;
INSERT INTO real_estate.schema_versions(version) VALUES(8);
COMMIT;
