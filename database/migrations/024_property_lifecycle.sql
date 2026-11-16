-- Physical property status is independent of announcement/publication status.
BEGIN;
ALTER TABLE real_estate.properties DROP CONSTRAINT properties_status_check;
ALTER TABLE real_estate.properties ADD CONSTRAINT property_lifecycle_status_check
 CHECK(status IN ('Draft','InReview','Available','InNegotiation','Negotiated','Inactive'));
ALTER TABLE real_estate.events ADD COLUMN establishment_id uuid, ADD COLUMN from_status text, ADD COLUMN to_status text;
CREATE FUNCTION real_estate.reject_property_lifecycle_event_change() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN RAISE EXCEPTION 'Property lifecycle event is append-only' USING ERRCODE='42501'; END $$;
CREATE TRIGGER property_lifecycle_events_immutable BEFORE UPDATE OR DELETE ON real_estate.events
 FOR EACH ROW WHEN (OLD.event_type IN ('PropertySubmittedForReview','PropertyApproved'))
 EXECUTE FUNCTION real_estate.reject_property_lifecycle_event_change();
CREATE FUNCTION real_estate.guard_property_lifecycle() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 IF TG_OP='INSERT' THEN
   IF NEW.status<>'Draft' THEN RAISE EXCEPTION 'Property must start as Draft' USING ERRCODE='23514'; END IF;
   RETURN NEW;
 END IF;
 IF OLD.status='Draft' AND NEW.status='Draft' THEN RETURN NEW; END IF;
 IF OLD.status='Draft' AND NEW.status='InReview'
    AND current_setting('erp.property_transition',true)='submit'
    AND NEW.version=OLD.version+1
    AND (to_jsonb(NEW)-'status'-'version'-'updated_by'-'updated_at')=
        (to_jsonb(OLD)-'status'-'version'-'updated_by'-'updated_at')
    AND NEW.updated_by IS NOT NULL THEN RETURN NEW; END IF;
 IF OLD.status='InReview' AND NEW.status='Available'
    AND current_setting('erp.property_transition',true)='approve'
    AND NEW.version=OLD.version+1
    AND (to_jsonb(NEW)-'status'-'version'-'updated_by'-'updated_at')=
        (to_jsonb(OLD)-'status'-'version'-'updated_by'-'updated_at')
    AND NEW.updated_by IS NOT NULL THEN RETURN NEW; END IF;
 RAISE EXCEPTION 'Property lifecycle transition is not available' USING ERRCODE='23514';
END $$;
CREATE TRIGGER guard_property_lifecycle BEFORE INSERT OR UPDATE ON real_estate.properties
 FOR EACH ROW EXECUTE FUNCTION real_estate.guard_property_lifecycle();
INSERT INTO real_estate.schema_versions(version) VALUES(24);
COMMIT;

