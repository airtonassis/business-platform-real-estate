-- IMOB-COM-01 v1. Additive product-owned data. Apply only in authorized ERP DB.
BEGIN;
CREATE TABLE real_estate.customers(
 organization_id uuid NOT NULL,id uuid NOT NULL,establishment_id uuid NOT NULL,
 name text NOT NULL CHECK(length(btrim(name)) BETWEEN 1 AND 200),
 data jsonb NOT NULL CHECK(jsonb_typeof(data)='object'),
 phone text,email text,document text,
 responsible_subject text NOT NULL CHECK(length(btrim(responsible_subject)) BETWEEN 1 AND 255),
 version bigint NOT NULL DEFAULT 1 CHECK(version>0),
 created_by text NOT NULL,created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
 updated_by text NOT NULL,updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
 PRIMARY KEY(organization_id,id),UNIQUE(organization_id,id,establishment_id),
 CHECK(phone IS NOT NULL OR email IS NOT NULL));
-- A fiscal identity is unique within the organization; contact matches only warn.
CREATE UNIQUE INDEX customer_document_unique ON real_estate.customers(organization_id,document) WHERE document IS NOT NULL;
CREATE INDEX customer_scope ON real_estate.customers(organization_id,establishment_id,name,id);
CREATE TABLE real_estate.customer_contacts(
 organization_id uuid NOT NULL,establishment_id uuid NOT NULL,customer_id uuid NOT NULL,id uuid NOT NULL,
 at timestamptz NOT NULL DEFAULT clock_timestamp(),kind text NOT NULL CHECK(kind IN ('Telefone','WhatsApp','E-mail','Presencial','Outros')),
 description text NOT NULL CHECK(length(btrim(description)) BETWEEN 1 AND 4000),actor text NOT NULL,responsible_subject text NOT NULL,
 PRIMARY KEY(organization_id,id),FOREIGN KEY(organization_id,customer_id,establishment_id) REFERENCES real_estate.customers(organization_id,id,establishment_id));
CREATE TABLE real_estate.customer_responsibility_history(
 organization_id uuid NOT NULL,establishment_id uuid NOT NULL,customer_id uuid NOT NULL,id uuid NOT NULL,
 at timestamptz NOT NULL DEFAULT clock_timestamp(),actor text NOT NULL,previous_subject text NOT NULL,new_subject text NOT NULL,
 PRIMARY KEY(organization_id,id),FOREIGN KEY(organization_id,customer_id,establishment_id) REFERENCES real_estate.customers(organization_id,id,establishment_id));
DO $body$
DECLARE t text;
BEGIN
 FOREACH t IN ARRAY ARRAY['customers','customer_contacts','customer_responsibility_history'] LOOP
  EXECUTE format('ALTER TABLE real_estate.%I ENABLE ROW LEVEL SECURITY',t);
  EXECUTE format('ALTER TABLE real_estate.%I FORCE ROW LEVEL SECURITY',t);
  EXECUTE format($policy$CREATE POLICY customer_scope ON real_estate.%I
   USING(organization_id=nullif(current_setting('erp.organization_id',true),'')::uuid
     AND establishment_id=nullif(current_setting('erp.active_establishment',true),'')::uuid
     AND establishment_id=ANY(coalesce(nullif(current_setting('erp.establishments',true),''),'{}')::uuid[]))
   WITH CHECK(organization_id=nullif(current_setting('erp.organization_id',true),'')::uuid
     AND establishment_id=nullif(current_setting('erp.active_establishment',true),'')::uuid
     AND establishment_id=ANY(coalesce(nullif(current_setting('erp.establishments',true),''),'{}')::uuid[]))$policy$,t);
 END LOOP;
END $body$;
-- Immutable history even if a deployment accidentally grants UPDATE/DELETE.
CREATE FUNCTION real_estate.reject_customer_history_change() RETURNS trigger LANGUAGE plpgsql AS $fn$
BEGIN RAISE EXCEPTION 'Customer history is append-only' USING ERRCODE='42501'; END $fn$;
CREATE TRIGGER customer_contacts_immutable BEFORE UPDATE OR DELETE ON real_estate.customer_contacts FOR EACH ROW EXECUTE FUNCTION real_estate.reject_customer_history_change();
CREATE TRIGGER customer_responsibility_immutable BEFORE UPDATE OR DELETE ON real_estate.customer_responsibility_history FOR EACH ROW EXECUTE FUNCTION real_estate.reject_customer_history_change();
REVOKE ALL ON real_estate.customers,real_estate.customer_contacts,real_estate.customer_responsibility_history FROM PUBLIC;
INSERT INTO real_estate.schema_versions(version) VALUES(20);
COMMIT;
