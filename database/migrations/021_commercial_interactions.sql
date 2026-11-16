-- IMOB-COM-02.A v1: append-only origin for client and property commercial histories.
BEGIN;
CREATE TABLE real_estate.commercial_presentations(
 organization_id uuid NOT NULL,establishment_id uuid NOT NULL,id uuid NOT NULL,
 customer_id uuid NOT NULL,property_id uuid NOT NULL,
 PRIMARY KEY(organization_id,id),UNIQUE(organization_id,id,establishment_id),
 FOREIGN KEY(organization_id,customer_id,establishment_id) REFERENCES real_estate.customers(organization_id,id,establishment_id),
 FOREIGN KEY(organization_id,property_id) REFERENCES real_estate.properties(organization_id,id));
CREATE TABLE real_estate.commercial_interactions(
 organization_id uuid NOT NULL,establishment_id uuid NOT NULL,id uuid NOT NULL,presentation_id uuid NOT NULL,contact_id uuid NOT NULL,
 at timestamptz NOT NULL DEFAULT clock_timestamp(),actor text NOT NULL,responsible_subject text NOT NULL,
 channel text NOT NULL CHECK(channel IN ('Presencial','Telefone','WhatsApp','E-mail','Visita','Outro')),
 status text NOT NULL CHECK(status IN ('Apresentado — aguardando retorno','Demonstrou interesse','Deseja agendar visita','Visitado','Sem interesse','Em negociação')),
 category text CHECK(category IN ('Preço e condições','Localização','Área e dimensões','Distribuição e ambientes','Conservação','Estrutura e comodidades','Condomínio e infraestrutura','Garagem e estacionamento','Documentação ou condições comerciais','Outros')),
 reason text CHECK(reason IS NULL OR length(btrim(reason)) BETWEEN 1 AND 1000),notes text CHECK(notes IS NULL OR length(notes)<=4000),
 PRIMARY KEY(organization_id,id),
 FOREIGN KEY(organization_id,presentation_id,establishment_id) REFERENCES real_estate.commercial_presentations(organization_id,id,establishment_id),
 FOREIGN KEY(organization_id,contact_id) REFERENCES real_estate.customer_contacts(organization_id,id),
 CHECK(status<>'Sem interesse' OR category IS NOT NULL AND reason IS NOT NULL));
CREATE INDEX commercial_customer_history ON real_estate.commercial_presentations(organization_id,establishment_id,customer_id,id);
CREATE INDEX commercial_property_history ON real_estate.commercial_presentations(organization_id,establishment_id,property_id,id);
CREATE INDEX commercial_interaction_history ON real_estate.commercial_interactions(organization_id,presentation_id,at,id);
ALTER TABLE real_estate.commercial_presentations ENABLE ROW LEVEL SECURITY;
ALTER TABLE real_estate.commercial_presentations FORCE ROW LEVEL SECURITY;
ALTER TABLE real_estate.commercial_interactions ENABLE ROW LEVEL SECURITY;
ALTER TABLE real_estate.commercial_interactions FORCE ROW LEVEL SECURITY;
CREATE POLICY commercial_scope ON real_estate.commercial_presentations
 USING(organization_id=nullif(current_setting('erp.organization_id',true),'')::uuid
  AND establishment_id=nullif(current_setting('erp.active_establishment',true),'')::uuid
  AND establishment_id=ANY(coalesce(nullif(current_setting('erp.establishments',true),''),'{}')::uuid[])
  AND EXISTS(SELECT 1 FROM real_estate.properties p WHERE p.organization_id=commercial_presentations.organization_id AND p.id=commercial_presentations.property_id AND p.establishment_id=commercial_presentations.establishment_id))
 WITH CHECK(organization_id=nullif(current_setting('erp.organization_id',true),'')::uuid
  AND establishment_id=nullif(current_setting('erp.active_establishment',true),'')::uuid
  AND establishment_id=ANY(coalesce(nullif(current_setting('erp.establishments',true),''),'{}')::uuid[])
  AND EXISTS(SELECT 1 FROM real_estate.properties p WHERE p.organization_id=commercial_presentations.organization_id AND p.id=commercial_presentations.property_id AND p.establishment_id=commercial_presentations.establishment_id));
CREATE POLICY commercial_scope ON real_estate.commercial_interactions
 USING(organization_id=nullif(current_setting('erp.organization_id',true),'')::uuid
  AND establishment_id=nullif(current_setting('erp.active_establishment',true),'')::uuid
  AND establishment_id=ANY(coalesce(nullif(current_setting('erp.establishments',true),''),'{}')::uuid[])
  AND EXISTS(SELECT 1 FROM real_estate.commercial_presentations p WHERE p.organization_id=commercial_interactions.organization_id AND p.id=commercial_interactions.presentation_id))
 WITH CHECK(organization_id=nullif(current_setting('erp.organization_id',true),'')::uuid
  AND establishment_id=nullif(current_setting('erp.active_establishment',true),'')::uuid
  AND establishment_id=ANY(coalesce(nullif(current_setting('erp.establishments',true),''),'{}')::uuid[])
  AND EXISTS(SELECT 1 FROM real_estate.commercial_presentations p JOIN real_estate.customer_contacts c ON c.organization_id=p.organization_id AND c.customer_id=p.customer_id AND c.establishment_id=p.establishment_id WHERE p.organization_id=commercial_interactions.organization_id AND p.id=commercial_interactions.presentation_id AND c.id=commercial_interactions.contact_id));
CREATE TRIGGER commercial_presentations_immutable BEFORE UPDATE OR DELETE ON real_estate.commercial_presentations FOR EACH ROW EXECUTE FUNCTION real_estate.reject_customer_history_change();
CREATE TRIGGER commercial_interactions_immutable BEFORE UPDATE OR DELETE ON real_estate.commercial_interactions FOR EACH ROW EXECUTE FUNCTION real_estate.reject_customer_history_change();
REVOKE ALL ON real_estate.commercial_presentations,real_estate.commercial_interactions FROM PUBLIC;
INSERT INTO real_estate.schema_versions(version) VALUES(21);
COMMIT;
