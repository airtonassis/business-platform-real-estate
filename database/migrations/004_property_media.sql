-- Private local media. Existing property/API schemas are unchanged.
BEGIN;
CREATE TABLE real_estate.property_media(
 organization_id uuid NOT NULL,property_id uuid NOT NULL,id uuid NOT NULL,
 kind text NOT NULL CHECK(kind IN ('image','video')),mime text NOT NULL CHECK(mime IN ('image/jpeg','image/png','video/mp4')),
 name text NOT NULL CHECK(length(name) BETWEEN 1 AND 200),content bytea NOT NULL,
 byte_length integer NOT NULL CHECK(byte_length>0 AND byte_length=octet_length(content)),
 sha256 text NOT NULL CHECK(sha256 ~ '^[A-F0-9]{64}$'),position integer NOT NULL CHECK(position>=0),
 is_cover boolean NOT NULL DEFAULT false CHECK(NOT is_cover OR kind='image'),
 version bigint NOT NULL DEFAULT 1 CHECK(version>0),created_by text NOT NULL,created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
 PRIMARY KEY(organization_id,id),FOREIGN KEY(organization_id,property_id) REFERENCES real_estate.properties(organization_id,id) ON DELETE CASCADE,
 UNIQUE(organization_id,property_id,position) DEFERRABLE INITIALLY DEFERRED,
 CHECK((kind='image' AND mime IN ('image/jpeg','image/png') AND byte_length<=10485760) OR (kind='video' AND mime='video/mp4' AND byte_length<=52428800)));
CREATE UNIQUE INDEX property_media_one_cover ON real_estate.property_media(organization_id,property_id) WHERE is_cover;
ALTER TABLE real_estate.property_media ENABLE ROW LEVEL SECURITY;
ALTER TABLE real_estate.property_media FORCE ROW LEVEL SECURITY;
CREATE POLICY property_visibility ON real_estate.property_media
 USING(organization_id=nullif(current_setting('erp.organization_id',true),'')::uuid AND EXISTS(SELECT 1 FROM real_estate.properties p WHERE p.organization_id=property_media.organization_id AND p.id=property_media.property_id))
 WITH CHECK(organization_id=nullif(current_setting('erp.organization_id',true),'')::uuid AND EXISTS(SELECT 1 FROM real_estate.properties p WHERE p.organization_id=property_media.organization_id AND p.id=property_media.property_id));
INSERT INTO real_estate.schema_versions(version) VALUES(4);
COMMIT;
