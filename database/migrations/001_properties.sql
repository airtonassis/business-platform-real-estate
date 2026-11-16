-- Additive initial ERP schema. Run only on its independent database as DDL owner.
BEGIN;
CREATE SCHEMA IF NOT EXISTS real_estate;
CREATE TABLE IF NOT EXISTS real_estate.schema_versions(version integer PRIMARY KEY,applied_at timestamptz NOT NULL DEFAULT clock_timestamp());
CREATE TABLE IF NOT EXISTS real_estate.owners(
 organization_id uuid NOT NULL,id uuid NOT NULL,name text NOT NULL CHECK(length(btrim(name)) BETWEEN 1 AND 200),
 active boolean NOT NULL DEFAULT true,created_by text NOT NULL,created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
 PRIMARY KEY(organization_id,id));
CREATE TABLE IF NOT EXISTS real_estate.properties(
 organization_id uuid NOT NULL,id uuid NOT NULL,owner_id uuid NOT NULL,
 code text NOT NULL CHECK(length(btrim(code)) BETWEEN 1 AND 50),title text NOT NULL CHECK(length(btrim(title)) BETWEEN 1 AND 200),
 category text NOT NULL CHECK(length(btrim(category)) BETWEEN 1 AND 100),property_type text NOT NULL,
 purpose text NOT NULL,address text NOT NULL CHECK(length(btrim(address)) BETWEEN 1 AND 500),
 postal_code text NOT NULL CHECK(postal_code ~ '^[0-9]{8}$'),latitude numeric NOT NULL CHECK(latitude BETWEEN -90 AND 90),
 longitude numeric NOT NULL CHECK(longitude BETWEEN -180 AND 180),price numeric(14,2) NOT NULL CHECK(price>0),
 status text NOT NULL DEFAULT 'Draft' CHECK(status='Draft'),version bigint NOT NULL DEFAULT 1 CHECK(version>0),
 created_by text NOT NULL,created_at timestamptz NOT NULL DEFAULT clock_timestamp(),updated_by text NOT NULL,updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
 PRIMARY KEY(organization_id,id),UNIQUE(organization_id,code),FOREIGN KEY(organization_id,owner_id) REFERENCES real_estate.owners(organization_id,id),
 CHECK(property_type IN ('Casa','Apartamento','Cobertura','Kitnet','Studio','Loft','Sala Comercial','Galpão','Terreno','Chácara','Fazenda','Sítio','Prédio Comercial','Empreendimento','Imóvel Industrial')),
 CHECK(purpose IN ('Venda','Locação','Temporada','Permuta','Lançamento')));
CREATE TABLE IF NOT EXISTS real_estate.events(
 organization_id uuid NOT NULL,id uuid NOT NULL,entity_id uuid NOT NULL,event_type text NOT NULL,
 actor text NOT NULL,occurred_at timestamptz NOT NULL DEFAULT clock_timestamp(),PRIMARY KEY(organization_id,id));
ALTER TABLE real_estate.owners ENABLE ROW LEVEL SECURITY;
ALTER TABLE real_estate.owners FORCE ROW LEVEL SECURITY;
ALTER TABLE real_estate.properties ENABLE ROW LEVEL SECURITY;
ALTER TABLE real_estate.properties FORCE ROW LEVEL SECURITY;
ALTER TABLE real_estate.events ENABLE ROW LEVEL SECURITY;
ALTER TABLE real_estate.events FORCE ROW LEVEL SECURITY;
-- Policies created only once; migration version protects repeat execution in runner.
CREATE POLICY organization_scope ON real_estate.owners USING(organization_id=nullif(current_setting('erp.organization_id',true),'')::uuid) WITH CHECK(organization_id=nullif(current_setting('erp.organization_id',true),'')::uuid);
CREATE POLICY organization_scope ON real_estate.properties USING(organization_id=nullif(current_setting('erp.organization_id',true),'')::uuid) WITH CHECK(organization_id=nullif(current_setting('erp.organization_id',true),'')::uuid);
CREATE POLICY organization_scope ON real_estate.events USING(organization_id=nullif(current_setting('erp.organization_id',true),'')::uuid) WITH CHECK(organization_id=nullif(current_setting('erp.organization_id',true),'')::uuid);
INSERT INTO real_estate.schema_versions(version) VALUES(1);
COMMIT;
