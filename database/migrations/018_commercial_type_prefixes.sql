-- Approved catalogue mapping only. No property, code, counter or registry backfill.
BEGIN;
CREATE OR REPLACE FUNCTION real_estate.commercial_type_prefix(t text) RETURNS text
LANGUAGE sql IMMUTABLE SET search_path=pg_catalog AS $$
 SELECT CASE t
 WHEN 'Apartamento' THEN 'AP' WHEN 'Casa' THEN 'CAS' WHEN 'Sala Comercial' THEN 'SAL'
 WHEN 'Terreno' THEN 'TER' WHEN 'Chácara' THEN 'RUR' WHEN 'Fazenda' THEN 'RUR' WHEN 'Sítio' THEN 'RUR'
 WHEN 'Kitnet' THEN 'KIT' WHEN 'Cobertura' THEN 'COB' WHEN 'Studio' THEN 'STD' WHEN 'Loft' THEN 'LOF'
 WHEN 'Galpão' THEN 'GAL' WHEN 'Prédio Comercial' THEN 'PRC' WHEN 'Empreendimento' THEN 'EMP'
 WHEN 'Imóvel Industrial' THEN 'IND' END
$$;
INSERT INTO real_estate.schema_versions(version) VALUES(18);
COMMIT;
