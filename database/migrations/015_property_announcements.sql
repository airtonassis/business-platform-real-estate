-- CAD-ANUNCIO-001: additive separation. Original rows and UUIDs remain untouched.
BEGIN;
CREATE FUNCTION real_estate.commercial_type_prefix(t text) RETURNS text
LANGUAGE sql IMMUTABLE SET search_path=pg_catalog AS $$
 SELECT CASE t WHEN 'Apartamento' THEN 'AP' WHEN 'Casa' THEN 'CAS' WHEN 'Sala Comercial' THEN 'SAL'
 WHEN 'Terreno' THEN 'TER' WHEN 'Chácara' THEN 'RUR' WHEN 'Fazenda' THEN 'RUR' WHEN 'Sítio' THEN 'RUR' END
$$;
-- Evolve the existing reservation ledger, never create a second sequence/registry.
ALTER TABLE real_estate.commercial_code_registry ADD COLUMN announcement_id uuid;
ALTER TABLE real_estate.commercial_code_registry DROP CONSTRAINT commercial_code_registry_pkey;
CREATE UNIQUE INDEX commercial_original_reservation ON real_estate.commercial_code_registry(organization_id,property_id) WHERE announcement_id IS NULL;
CREATE UNIQUE INDEX commercial_announcement_reservation ON real_estate.commercial_code_registry(organization_id,announcement_id) WHERE announcement_id IS NOT NULL;
DO $$ DECLARE definition text; BEGIN
 SELECT pg_get_functiondef('real_estate.assign_commercial_code()'::regprocedure) INTO definition;
 definition:=replace(definition,'WHERE organization_id=NEW.organization_id AND property_id=NEW.id;',
   'WHERE organization_id=NEW.organization_id AND property_id=NEW.id AND announcement_id IS NULL;');
 definition:=replace(definition,$old$CASE NEW.property_type WHEN 'Apartamento' THEN 'AP' WHEN 'Casa' THEN 'CAS' WHEN 'Sala Comercial' THEN 'SAL' WHEN 'Terreno' THEN 'TER' WHEN 'Chácara' THEN 'RUR' WHEN 'Fazenda' THEN 'RUR' WHEN 'Sítio' THEN 'RUR' END$old$,
   'real_estate.commercial_type_prefix(NEW.property_type)');
 EXECUTE definition;
END $$;

CREATE TABLE real_estate.property_announcements(
 organization_id uuid NOT NULL,id uuid NOT NULL,property_id uuid NOT NULL,establishment_id uuid NOT NULL,
 purpose text NOT NULL CHECK(purpose IN ('Venda','Locação')),commercial_code text,
 status text NOT NULL DEFAULT 'Draft' CHECK(status IN ('Draft','Closed')),
 version bigint NOT NULL DEFAULT 1 CHECK(version>0),legacy_anchor boolean NOT NULL DEFAULT false,
 source_announcement_id uuid,request_id uuid NOT NULL,
 commercial_data jsonb NOT NULL DEFAULT '{}' CHECK(jsonb_typeof(commercial_data)='object'),
 created_by text NOT NULL,created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
 updated_by text NOT NULL,updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
 PRIMARY KEY(organization_id,id),UNIQUE(organization_id,request_id),
 FOREIGN KEY(organization_id,property_id) REFERENCES real_estate.properties(organization_id,id),
 FOREIGN KEY(organization_id,source_announcement_id) REFERENCES real_estate.property_announcements(organization_id,id));
CREATE UNIQUE INDEX announcement_active_modality ON real_estate.property_announcements(organization_id,property_id,purpose) WHERE status='Draft';
CREATE UNIQUE INDEX announcement_legacy_anchor ON real_estate.property_announcements(organization_id,property_id) WHERE legacy_anchor;
CREATE UNIQUE INDEX announcement_commercial_code ON real_estate.property_announcements(organization_id,commercial_code);
CREATE TABLE real_estate.announcement_history(
 organization_id uuid NOT NULL,announcement_id uuid NOT NULL,version bigint NOT NULL,
 event_type text NOT NULL,actor text NOT NULL,occurred_at timestamptz NOT NULL DEFAULT clock_timestamp(),snapshot jsonb NOT NULL,
 PRIMARY KEY(organization_id,announcement_id,version),
 FOREIGN KEY(organization_id,announcement_id) REFERENCES real_estate.property_announcements(organization_id,id));

-- Compatibility anchors reference existing commercial state. No copying of approvals/history.
-- Combined and other historical purposes remain exclusively in their original rows.
ALTER TABLE real_estate.properties NO FORCE ROW LEVEL SECURITY;
INSERT INTO real_estate.property_announcements(organization_id,id,property_id,establishment_id,purpose,commercial_code,legacy_anchor,request_id,created_by,updated_by)
 SELECT organization_id,gen_random_uuid(),id,establishment_id,purpose,commercial_code,true,gen_random_uuid(),'migration:015-compatibility-anchor','migration:015-compatibility-anchor'
 FROM real_estate.properties WHERE purpose IN ('Venda','Locação') AND establishment_id IS NOT NULL;
INSERT INTO real_estate.announcement_history
 SELECT organization_id,id,version,'AnnouncementCompatibilityLinked','migration:015-compatibility-anchor',clock_timestamp(),to_jsonb(a)
 FROM real_estate.property_announcements a;

ALTER TABLE real_estate.properties FORCE ROW LEVEL SECURITY;
ALTER TABLE real_estate.property_announcements ENABLE ROW LEVEL SECURITY;
ALTER TABLE real_estate.property_announcements FORCE ROW LEVEL SECURITY;
CREATE POLICY announcement_scope ON real_estate.property_announcements
 USING(organization_id=nullif(current_setting('erp.organization_id',true),'')::uuid
 AND establishment_id=ANY(coalesce(nullif(current_setting('erp.establishments',true),''),'{}')::uuid[])
 AND EXISTS(SELECT 1 FROM real_estate.properties p WHERE p.organization_id=property_announcements.organization_id AND p.id=property_id))
 WITH CHECK(organization_id=nullif(current_setting('erp.organization_id',true),'')::uuid
 AND establishment_id=ANY(coalesce(nullif(current_setting('erp.establishments',true),''),'{}')::uuid[])
 AND EXISTS(SELECT 1 FROM real_estate.properties p WHERE p.organization_id=property_announcements.organization_id AND p.id=property_id));
ALTER TABLE real_estate.announcement_history ENABLE ROW LEVEL SECURITY;
ALTER TABLE real_estate.announcement_history FORCE ROW LEVEL SECURITY;
CREATE POLICY announcement_history_scope ON real_estate.announcement_history USING(
 organization_id=nullif(current_setting('erp.organization_id',true),'')::uuid AND EXISTS(
 SELECT 1 FROM real_estate.property_announcements a WHERE a.organization_id=announcement_history.organization_id AND a.id=announcement_id));

CREATE FUNCTION real_estate.protect_announcement() RETURNS trigger LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,real_estate AS $$
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
  NEW.commercial_code:=coalesce(OLD.commercial_code,CASE WHEN NEW.legacy_anchor THEN p.commercial_code END);NEW.version:=OLD.version+1;NEW.updated_at:=clock_timestamp();
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
  IF NEW.legacy_anchor THEN NEW.commercial_code:=p.commercial_code;
  ELSE
   NEW.commercial_code:=NULL;
   prefix:=real_estate.commercial_type_prefix(p.property_type);
   IF prefix IS NOT NULL THEN
    INSERT INTO real_estate.commercial_code_counters VALUES(NEW.organization_id,1)
     ON CONFLICT(organization_id) DO UPDATE SET last_number=real_estate.commercial_code_counters.last_number+1 RETURNING last_number INTO assigned;
    NEW.commercial_code:=(CASE NEW.purpose WHEN 'Venda' THEN 'VEN' ELSE 'LOC' END)||prefix||'-'||assigned;
    INSERT INTO real_estate.commercial_code_registry(organization_id,property_id,code,assigned_number,actor,announcement_id)
     VALUES(NEW.organization_id,NEW.property_id,NEW.commercial_code,assigned,NEW.created_by,NEW.id);
   END IF;
  END IF;
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER protect_announcement BEFORE INSERT OR UPDATE ON real_estate.property_announcements FOR EACH ROW EXECUTE FUNCTION real_estate.protect_announcement();
CREATE FUNCTION real_estate.audit_announcement() RETURNS trigger LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,real_estate AS $$
DECLARE kind text;record_snapshot jsonb;p real_estate.properties;
BEGIN
 kind:=CASE WHEN TG_OP='INSERT' THEN CASE WHEN NEW.source_announcement_id IS NOT NULL THEN 'AnnouncementDuplicated' ELSE 'AnnouncementCreated' END
 WHEN NEW.status='Closed' THEN 'AnnouncementClosed' ELSE 'AnnouncementUpdated' END;
 record_snapshot:=to_jsonb(NEW);
 IF NEW.legacy_anchor THEN
  SELECT * INTO p FROM real_estate.properties WHERE organization_id=NEW.organization_id AND id=NEW.property_id;
  record_snapshot:=record_snapshot||jsonb_build_object('physicalVersion',p.version,'originalCommercialState',jsonb_build_object('values',p.capture->'values','fees',p.capture->'fees','acquisition',p.capture->'acquisition','policy',p.capture->'policy'));
 END IF;
 INSERT INTO real_estate.announcement_history VALUES(NEW.organization_id,NEW.id,NEW.version,kind,NEW.updated_by,clock_timestamp(),record_snapshot);
 INSERT INTO real_estate.events(organization_id,id,entity_id,event_type,actor) VALUES(NEW.organization_id,gen_random_uuid(),NEW.id,kind,NEW.updated_by);
 RETURN NEW;
END $$;
CREATE TRIGGER audit_announcement AFTER INSERT OR UPDATE ON real_estate.property_announcements FOR EACH ROW EXECUTE FUNCTION real_estate.audit_announcement();

CREATE FUNCTION real_estate.lock_property_purpose() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 IF TG_OP='UPDATE' AND OLD.purpose IS NOT NULL AND NEW.purpose IS DISTINCT FROM OLD.purpose THEN
  RAISE EXCEPTION 'Duplicate announcement instead' USING ERRCODE='23514',CONSTRAINT='property_purpose_immutable'; END IF;
 IF (TG_OP='INSERT' OR OLD.purpose IS NULL) AND NEW.purpose IS NOT NULL AND NEW.purpose NOT IN ('Venda','Locação') THEN
  RAISE EXCEPTION 'Exclusive announcement purpose required' USING ERRCODE='23514',CONSTRAINT='announcement_exclusive_purpose'; END IF;
 IF TG_OP='UPDATE' AND EXISTS(SELECT 1 FROM real_estate.property_announcements a WHERE a.organization_id=OLD.organization_id AND a.property_id=OLD.id AND a.legacy_anchor AND a.status='Closed')
 AND (NEW.capture->'values',NEW.capture->'fees',NEW.capture->'acquisition',NEW.capture->'policy') IS DISTINCT FROM (OLD.capture->'values',OLD.capture->'fees',OLD.capture->'acquisition',OLD.capture->'policy') THEN
  RAISE EXCEPTION 'Closed announcement commercial state is immutable' USING ERRCODE='23514',CONSTRAINT='announcement_closed'; END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER lock_property_purpose BEFORE INSERT OR UPDATE ON real_estate.properties FOR EACH ROW EXECUTE FUNCTION real_estate.lock_property_purpose();
CREATE FUNCTION real_estate.link_property_announcement() RETURNS trigger LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,real_estate AS $$
BEGIN
 IF NEW.purpose IN ('Venda','Locação') AND NEW.establishment_id IS NOT NULL THEN
  IF NOT EXISTS(SELECT 1 FROM real_estate.property_announcements WHERE organization_id=NEW.organization_id AND property_id=NEW.id AND legacy_anchor) THEN
   INSERT INTO real_estate.property_announcements(organization_id,id,property_id,establishment_id,purpose,legacy_anchor,request_id,created_by,updated_by)
    VALUES(NEW.organization_id,gen_random_uuid(),NEW.id,NEW.establishment_id,NEW.purpose,true,gen_random_uuid(),NEW.updated_by,NEW.updated_by);
  ELSIF TG_OP='UPDATE' AND (NEW.capture->'values',NEW.capture->'fees',NEW.capture->'acquisition',NEW.capture->'policy',NEW.commercial_code) IS DISTINCT FROM (OLD.capture->'values',OLD.capture->'fees',OLD.capture->'acquisition',OLD.capture->'policy',OLD.commercial_code) THEN
   UPDATE real_estate.property_announcements SET updated_by=NEW.updated_by WHERE organization_id=NEW.organization_id AND property_id=NEW.id AND legacy_anchor AND status='Draft';
  END IF;
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER link_property_announcement AFTER INSERT OR UPDATE ON real_estate.properties FOR EACH ROW EXECUTE FUNCTION real_estate.link_property_announcement();
REVOKE ALL ON FUNCTION real_estate.protect_announcement(),real_estate.audit_announcement(),real_estate.link_property_announcement() FROM PUBLIC;
INSERT INTO real_estate.schema_versions(version) VALUES(15);
COMMIT;
