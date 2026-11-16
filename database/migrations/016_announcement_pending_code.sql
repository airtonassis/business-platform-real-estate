-- Allocate pending child identities when a draft receives its physical type.
-- Already assigned codes and historical property rows are never rewritten.
BEGIN;
-- Null historical capture and an empty progressive form express the same commercial state.
CREATE FUNCTION real_estate.announcement_original_state(c jsonb) RETURNS jsonb LANGUAGE sql IMMUTABLE SET search_path=pg_catalog AS $$
 SELECT jsonb_strip_nulls(jsonb_build_object(
 'values','{"sale":{"differentAdvertised":false},"rental":{"differentAdvertised":false},"seasonal":{"differentAdvertised":false}}'::jsonb||coalesce(nullif(c->'values','null'::jsonb),'{}'::jsonb),
 'fees','{"allocations":[]}'::jsonb||coalesce(nullif(c->'fees','null'::jsonb),'{}'::jsonb),
 'acquisition',coalesce(nullif(c->'acquisition','null'::jsonb),'{}'::jsonb),'policy',c->'policy'))
$$;
CREATE OR REPLACE FUNCTION real_estate.lock_property_purpose() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 IF TG_OP='UPDATE' AND OLD.purpose IS NOT NULL AND NEW.purpose IS DISTINCT FROM OLD.purpose THEN
  RAISE EXCEPTION 'Duplicate announcement instead' USING ERRCODE='23514',CONSTRAINT='property_purpose_immutable'; END IF;
 IF (TG_OP='INSERT' OR OLD.purpose IS NULL) AND NEW.purpose IS NOT NULL AND NEW.purpose NOT IN ('Venda','Locação') THEN
  RAISE EXCEPTION 'Exclusive announcement purpose required' USING ERRCODE='23514',CONSTRAINT='announcement_exclusive_purpose'; END IF;
 IF TG_OP='UPDATE' AND EXISTS(SELECT 1 FROM real_estate.property_announcements a WHERE a.organization_id=OLD.organization_id AND a.property_id=OLD.id AND a.legacy_anchor AND a.status='Closed')
 AND real_estate.announcement_original_state(NEW.capture) IS DISTINCT FROM real_estate.announcement_original_state(OLD.capture) THEN
  RAISE EXCEPTION 'Closed announcement commercial state is immutable' USING ERRCODE='23514',CONSTRAINT='announcement_closed'; END IF;
 RETURN NEW;
END $$;
CREATE OR REPLACE FUNCTION real_estate.protect_announcement() RETURNS trigger LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,real_estate AS $$
DECLARE p real_estate.properties;source real_estate.property_announcements;prefix text;assigned bigint;
BEGIN
 IF NEW.organization_id IS DISTINCT FROM nullif(current_setting('erp.organization_id',true),'')::uuid THEN
  RAISE EXCEPTION 'Announcement context mismatch' USING ERRCODE='42501'; END IF;
 SELECT * INTO p FROM real_estate.properties WHERE organization_id=NEW.organization_id AND id=NEW.property_id FOR UPDATE;
 IF NOT FOUND OR p.establishment_id IS DISTINCT FROM NEW.establishment_id OR NOT(NEW.establishment_id=ANY(coalesce(nullif(current_setting('erp.establishments',true),''),'{}')::uuid[])) THEN
  RAISE EXCEPTION 'Announcement scope mismatch' USING ERRCODE='42501'; END IF;
 IF TG_OP='UPDATE' THEN
  IF (NEW.id,NEW.organization_id,NEW.property_id,NEW.establishment_id,NEW.purpose,NEW.legacy_anchor,NEW.source_announcement_id,NEW.request_id,NEW.created_by,NEW.created_at)
   IS DISTINCT FROM (OLD.id,OLD.organization_id,OLD.property_id,OLD.establishment_id,OLD.purpose,OLD.legacy_anchor,OLD.source_announcement_id,OLD.request_id,OLD.created_by,OLD.created_at) THEN
   RAISE EXCEPTION 'Announcement identity immutable' USING ERRCODE='23514',CONSTRAINT='announcement_identity_immutable'; END IF;
  IF OLD.status='Closed' THEN RAISE EXCEPTION 'Announcement closed' USING ERRCODE='23514',CONSTRAINT='announcement_closed'; END IF;
  IF OLD.legacy_anchor AND NEW.commercial_data IS DISTINCT FROM OLD.commercial_data THEN
   RAISE EXCEPTION 'Edit original compatibility projection' USING ERRCODE='23514',CONSTRAINT='announcement_edit_original'; END IF;
  NEW.commercial_code:=coalesce(OLD.commercial_code,CASE WHEN NEW.legacy_anchor THEN p.commercial_code END);
  NEW.version:=OLD.version+1;NEW.updated_at:=clock_timestamp();
 ELSE
  IF NEW.establishment_id IS DISTINCT FROM nullif(current_setting('erp.active_establishment',true),'')::uuid THEN
   RAISE EXCEPTION 'Active establishment required' USING ERRCODE='42501'; END IF;
  NEW.version:=1;NEW.status:='Draft';NEW.created_at:=clock_timestamp();NEW.updated_at:=NEW.created_at;
  IF NEW.source_announcement_id IS NOT NULL THEN
   SELECT * INTO source FROM real_estate.property_announcements WHERE organization_id=NEW.organization_id AND id=NEW.source_announcement_id;
   IF NOT FOUND OR source.property_id<>NEW.property_id OR source.purpose=NEW.purpose THEN
    RAISE EXCEPTION 'Invalid announcement origin' USING ERRCODE='23514',CONSTRAINT='announcement_origin'; END IF;
  END IF;
  IF NEW.legacy_anchor AND pg_trigger_depth()<2 THEN RAISE EXCEPTION 'Anchor is server managed' USING ERRCODE='42501'; END IF;
  NEW.commercial_code:=CASE WHEN NEW.legacy_anchor THEN p.commercial_code END;
 END IF;
 IF NOT NEW.legacy_anchor AND NEW.commercial_code IS NULL THEN
  SELECT code INTO NEW.commercial_code FROM real_estate.commercial_code_registry WHERE organization_id=NEW.organization_id AND announcement_id=NEW.id;
  prefix:=real_estate.commercial_type_prefix(p.property_type);
  IF NEW.commercial_code IS NULL AND prefix IS NOT NULL THEN
   INSERT INTO real_estate.commercial_code_counters VALUES(NEW.organization_id,1)
    ON CONFLICT(organization_id) DO UPDATE SET last_number=real_estate.commercial_code_counters.last_number+1 RETURNING last_number INTO assigned;
   NEW.commercial_code:=(CASE NEW.purpose WHEN 'Venda' THEN 'VEN' ELSE 'LOC' END)||prefix||'-'||assigned;
   INSERT INTO real_estate.commercial_code_registry(organization_id,property_id,code,assigned_number,actor,announcement_id)
    VALUES(NEW.organization_id,NEW.property_id,NEW.commercial_code,assigned,NEW.updated_by,NEW.id);
  END IF;
 END IF;
 RETURN NEW;
END $$;
CREATE OR REPLACE FUNCTION real_estate.link_property_announcement() RETURNS trigger LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,real_estate AS $$
BEGIN
 IF NEW.purpose IN ('Venda','Locação') AND NEW.establishment_id IS NOT NULL THEN
  IF NOT EXISTS(SELECT 1 FROM real_estate.property_announcements WHERE organization_id=NEW.organization_id AND property_id=NEW.id AND legacy_anchor) THEN
   INSERT INTO real_estate.property_announcements(organization_id,id,property_id,establishment_id,purpose,legacy_anchor,request_id,created_by,updated_by)
    VALUES(NEW.organization_id,gen_random_uuid(),NEW.id,NEW.establishment_id,NEW.purpose,true,gen_random_uuid(),NEW.updated_by,NEW.updated_by);
  ELSIF TG_OP='UPDATE' AND (real_estate.announcement_original_state(NEW.capture),NEW.commercial_code) IS DISTINCT FROM (real_estate.announcement_original_state(OLD.capture),OLD.commercial_code) THEN
   UPDATE real_estate.property_announcements SET updated_by=NEW.updated_by WHERE organization_id=NEW.organization_id AND property_id=NEW.id AND legacy_anchor AND status='Draft';
  END IF;
 END IF;
 IF real_estate.commercial_type_prefix(NEW.property_type) IS NOT NULL THEN
  UPDATE real_estate.property_announcements SET updated_by=NEW.updated_by
   WHERE organization_id=NEW.organization_id AND property_id=NEW.id AND NOT legacy_anchor AND status='Draft' AND commercial_code IS NULL;
 END IF;
 RETURN NEW;
END $$;
INSERT INTO real_estate.schema_versions(version) VALUES(16);
COMMIT;
