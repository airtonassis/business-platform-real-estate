BEGIN;
-- General policies are organizational. Property exceptions and application history follow the property's existing RLS scope.
ALTER POLICY organization_scope ON real_estate.commercial_policy_revisions
 USING(organization_id=nullif(current_setting('erp.organization_id',true),'')::uuid AND
 (property_id IS NULL OR EXISTS(SELECT FROM real_estate.properties p WHERE p.organization_id=commercial_policy_revisions.organization_id AND p.id=commercial_policy_revisions.property_id)))
 WITH CHECK(organization_id=nullif(current_setting('erp.organization_id',true),'')::uuid AND
 (property_id IS NULL OR EXISTS(SELECT FROM real_estate.properties p WHERE p.organization_id=commercial_policy_revisions.organization_id AND p.id=commercial_policy_revisions.property_id)));
ALTER POLICY organization_scope ON real_estate.commercial_policy_applications
 USING(organization_id=nullif(current_setting('erp.organization_id',true),'')::uuid AND
 EXISTS(SELECT FROM real_estate.properties p WHERE p.organization_id=commercial_policy_applications.organization_id AND p.id=commercial_policy_applications.property_id))
 WITH CHECK(organization_id=nullif(current_setting('erp.organization_id',true),'')::uuid AND
 EXISTS(SELECT FROM real_estate.properties p WHERE p.organization_id=commercial_policy_applications.organization_id AND p.id=commercial_policy_applications.property_id));
INSERT INTO real_estate.schema_versions(version) VALUES(12);
COMMIT;
