-- Additive commercial identity. Legacy properties.code and UUID are never rewritten.
-- Execute as schema DDL owner. All changes, including the bounded backfill, are atomic.
BEGIN;
ALTER TABLE real_estate.properties ADD COLUMN commercial_code text CHECK(length(commercial_code) BETWEEN 1 AND 50);
CREATE UNIQUE INDEX property_commercial_code ON real_estate.properties(organization_id,commercial_code);
CREATE TABLE real_estate.commercial_code_counters(
 organization_id uuid PRIMARY KEY,last_number bigint NOT NULL CHECK(last_number>=0));
CREATE TABLE real_estate.commercial_code_registry(
 organization_id uuid NOT NULL,property_id uuid NOT NULL,code text NOT NULL,
 assigned_number bigint,assigned_at timestamptz NOT NULL DEFAULT clock_timestamp(),actor text NOT NULL,
 PRIMARY KEY(organization_id,property_id),UNIQUE(organization_id,code),
 CHECK(assigned_number IS NULL OR assigned_number>0));
-- No FK/cascade: an assigned identity remains reserved even if its property is later removed.
-- Legacy IM-UUID is a historical identifier, not a commercial code. All other codes are preserved.
-- ACCESS EXCLUSIVE is held until COMMIT; no other session sees the temporary owner bypass.
ALTER TABLE real_estate.properties NO FORCE ROW LEVEL SECURITY;
UPDATE real_estate.properties SET commercial_code=code WHERE code IS NOT NULL AND code !~ '^IM-[0-9a-fA-F]{32}$';
INSERT INTO real_estate.commercial_code_registry(organization_id,property_id,code,assigned_number,actor)
 SELECT organization_id,id,commercial_code,
 CASE WHEN substring(commercial_code FROM '-([1-9][0-9]{0,18})$')::numeric<=9223372036854775807
 THEN substring(commercial_code FROM '-([1-9][0-9]{0,18})$')::bigint END,'migration:legacy-preservation'
 FROM real_estate.properties WHERE commercial_code IS NOT NULL;
ALTER TABLE real_estate.properties FORCE ROW LEVEL SECURITY;
INSERT INTO real_estate.commercial_code_counters SELECT organization_id,coalesce(max(assigned_number),0) FROM real_estate.commercial_code_registry GROUP BY organization_id;
ALTER TABLE real_estate.commercial_code_counters ENABLE ROW LEVEL SECURITY;
ALTER TABLE real_estate.commercial_code_counters FORCE ROW LEVEL SECURITY;
ALTER TABLE real_estate.commercial_code_registry ENABLE ROW LEVEL SECURITY;
ALTER TABLE real_estate.commercial_code_registry FORCE ROW LEVEL SECURITY;
CREATE POLICY organization_scope ON real_estate.commercial_code_counters USING(organization_id=nullif(current_setting('erp.organization_id',true),'')::uuid) WITH CHECK(organization_id=nullif(current_setting('erp.organization_id',true),'')::uuid);
CREATE POLICY organization_scope ON real_estate.commercial_code_registry USING(organization_id=nullif(current_setting('erp.organization_id',true),'')::uuid) WITH CHECK(organization_id=nullif(current_setting('erp.organization_id',true),'')::uuid);

CREATE FUNCTION real_estate.assign_commercial_code() RETURNS trigger
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,real_estate AS $$
DECLARE purpose_prefix text;type_prefix text;assigned bigint;existing_code text;existing_number bigint;
BEGIN
 IF NEW.organization_id IS DISTINCT FROM nullif(current_setting('erp.organization_id',true),'')::uuid THEN
  RAISE EXCEPTION 'Commercial identity context mismatch' USING ERRCODE='42501';
 END IF;
 IF TG_OP='UPDATE' AND (NEW.organization_id IS DISTINCT FROM OLD.organization_id OR NEW.id IS DISTINCT FROM OLD.id) THEN
  RAISE EXCEPTION 'Technical identity is immutable' USING ERRCODE='42501';
 END IF;
 IF TG_OP='UPDATE' AND OLD.commercial_code IS NOT NULL THEN
  NEW.commercial_code:=OLD.commercial_code;RETURN NEW;
 END IF;
 SELECT code INTO existing_code FROM real_estate.commercial_code_registry WHERE organization_id=NEW.organization_id AND property_id=NEW.id;
 IF existing_code IS NOT NULL THEN NEW.commercial_code:=existing_code;RETURN NEW;END IF;
 NEW.commercial_code:=NULL; -- Caller cannot choose the new commercial identity.
 purpose_prefix:=CASE NEW.purpose WHEN 'Venda' THEN 'VEN' WHEN 'Locação' THEN 'LOC' WHEN 'Venda e locação' THEN 'AMB' END;
 type_prefix:=CASE NEW.property_type WHEN 'Apartamento' THEN 'AP' WHEN 'Casa' THEN 'CAS' WHEN 'Sala Comercial' THEN 'SAL' WHEN 'Terreno' THEN 'TER' END;
 -- Preserve codes supplied through the existing v1/v2 contract; never replace them.
 IF NEW.code IS NOT NULL AND NEW.code !~ '^IM-[0-9a-fA-F]{32}$' THEN
  NEW.commercial_code:=NEW.code;
  IF substring(NEW.code FROM '-([1-9][0-9]{0,18})$')::numeric<=9223372036854775807 THEN
   existing_number:=substring(NEW.code FROM '-([1-9][0-9]{0,18})$')::bigint;
  END IF;
 ELSIF purpose_prefix IS NOT NULL AND type_prefix IS NOT NULL THEN
  INSERT INTO real_estate.commercial_code_counters VALUES(NEW.organization_id,1)
   ON CONFLICT(organization_id) DO UPDATE SET last_number=real_estate.commercial_code_counters.last_number+1
   RETURNING last_number INTO assigned;
  NEW.commercial_code:=purpose_prefix||type_prefix||'-'||assigned;
 ELSE RETURN NEW;
 END IF;
 IF existing_number IS NOT NULL THEN
  INSERT INTO real_estate.commercial_code_counters VALUES(NEW.organization_id,existing_number)
   ON CONFLICT(organization_id) DO UPDATE SET last_number=greatest(real_estate.commercial_code_counters.last_number,excluded.last_number);
 END IF;
 INSERT INTO real_estate.commercial_code_registry(organization_id,property_id,code,assigned_number,actor)
 VALUES(NEW.organization_id,NEW.id,NEW.commercial_code,coalesce(assigned,existing_number),NEW.updated_by);
 INSERT INTO real_estate.events(organization_id,id,entity_id,event_type,actor)
 VALUES(NEW.organization_id,gen_random_uuid(),NEW.id,'CommercialCodeAssigned',NEW.updated_by);
 RETURN NEW;
END $$;
REVOKE ALL ON FUNCTION real_estate.assign_commercial_code() FROM PUBLIC;
CREATE TRIGGER property_commercial_identity BEFORE INSERT OR UPDATE ON real_estate.properties FOR EACH ROW EXECUTE FUNCTION real_estate.assign_commercial_code();
-- Runtime gets no direct counter/registry writes. The trigger's NOLOGIN owner is subject to FORCE RLS.
INSERT INTO real_estate.schema_versions(version) VALUES(13);
COMMIT;
