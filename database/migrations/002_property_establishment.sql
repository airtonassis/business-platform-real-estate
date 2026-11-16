-- Preserve existing records; null establishments are legacy, visible only to authorized managers.
BEGIN;
ALTER TABLE real_estate.properties ADD COLUMN establishment_id uuid;
CREATE INDEX property_establishment ON real_estate.properties(organization_id,establishment_id);
DROP POLICY organization_scope ON real_estate.properties;
CREATE POLICY organization_scope ON real_estate.properties
USING(organization_id=nullif(current_setting('erp.organization_id',true),'')::uuid AND (establishment_id=ANY(coalesce(nullif(current_setting('erp.establishments',true),''),'{}')::uuid[]) OR establishment_id IS NULL AND current_setting('erp.legacy_manager',true)='enabled'))
WITH CHECK(organization_id=nullif(current_setting('erp.organization_id',true),'')::uuid AND establishment_id=ANY(coalesce(nullif(current_setting('erp.establishments',true),''),'{}')::uuid[]));
INSERT INTO real_estate.schema_versions(version) VALUES(2);
COMMIT;
