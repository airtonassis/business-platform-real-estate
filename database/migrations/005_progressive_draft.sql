-- CAD-1A: nullable business data; no legacy backfill, policy or privilege changes.
BEGIN;
ALTER TABLE real_estate.properties
 ALTER COLUMN owner_id DROP NOT NULL,
 ALTER COLUMN code DROP NOT NULL,
 ALTER COLUMN title DROP NOT NULL,
 ALTER COLUMN category DROP NOT NULL,
 ALTER COLUMN property_type DROP NOT NULL,
 ALTER COLUMN purpose DROP NOT NULL,
 ALTER COLUMN price DROP NOT NULL,
 ADD CONSTRAINT property_partial_business_only_draft CHECK(status='Draft' OR
   (owner_id IS NOT NULL AND code IS NOT NULL AND title IS NOT NULL AND category IS NOT NULL AND property_type IS NOT NULL AND purpose IS NOT NULL AND price IS NOT NULL));
CREATE FUNCTION real_estate.protect_property_origin() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 IF NEW.organization_id IS DISTINCT FROM OLD.organization_id OR NEW.id IS DISTINCT FROM OLD.id OR
    NEW.establishment_id IS DISTINCT FROM OLD.establishment_id OR NEW.created_by IS DISTINCT FROM OLD.created_by OR NEW.created_at IS DISTINCT FROM OLD.created_at THEN
   RAISE EXCEPTION 'Property origin is immutable' USING ERRCODE='23514';
 END IF;
 RETURN NEW;
END;
$$;
CREATE TRIGGER protect_property_origin BEFORE UPDATE ON real_estate.properties
 FOR EACH ROW EXECUTE FUNCTION real_estate.protect_property_origin();
INSERT INTO real_estate.schema_versions(version) VALUES(5);
COMMIT;
