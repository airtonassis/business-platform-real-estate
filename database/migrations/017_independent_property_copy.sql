-- Independent property creation: preserve linked announcements and all assigned identities.
BEGIN;
DO $$ DECLARE definition text; BEGIN
 SELECT pg_get_functiondef('real_estate.lock_property_purpose()'::regprocedure) INTO definition;
 definition:=replace(definition,'''Venda'',''Locação'') THEN','''Venda'',''Locação'',''Temporada'',''Permuta'',''Lançamento'',''Venda e locação'') THEN');
 EXECUTE definition;
 SELECT pg_get_functiondef('real_estate.assign_commercial_code()'::regprocedure) INTO definition;
 definition:=replace(definition,'WHEN ''Venda e locação'' THEN ''AMB'' END','WHEN ''Venda e locação'' THEN ''AMB'' WHEN ''Temporada'' THEN ''TEM'' END');
 EXECUTE definition;
END $$;
INSERT INTO real_estate.schema_versions(version) VALUES(17);
COMMIT;
