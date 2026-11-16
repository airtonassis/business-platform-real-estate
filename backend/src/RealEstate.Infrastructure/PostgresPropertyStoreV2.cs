using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using RealEstate.Domain;
namespace RealEstate.Infrastructure;

public sealed partial class PostgresPropertyStore
{
    private static readonly JsonSerializerOptions CaptureJson=new(JsonSerializerDefaults.Web);
    private const string AddressColumns="address_ibge,address_uf,address_municipality,address_street,address_number,address_without_number,address_complement,address_neighborhood,address_country,coordinate_source";
    private const string V2Columns=Columns+","+AddressColumns+",summary,internal_reference,is_in_condominium,capture,commercial_code";
    private static PropertyV2 ReadV2(NpgsqlDataReader r)=>new(r.GetGuid(0),r.GetGuid(1),new(Text(r,2),Text(r,3),r.IsDBNull(4)?null:r.GetGuid(4),Text(r,5),Text(r,6),Text(r,7),Number(r,12),
        new(Text(r,8),Text(r,9),Text(r,16),Text(r,17),Text(r,18),Text(r,19),Text(r,20),r.IsDBNull(21)?null:r.GetBoolean(21),Text(r,22),Text(r,23),Text(r,24),Number(r,10),Number(r,11),Text(r,25)),
        r.IsDBNull(15)?null:r.GetGuid(15),Text(r,26),Text(r,27),r.IsDBNull(28)?null:r.GetBoolean(28),r.IsDBNull(29)?null:JsonSerializer.Deserialize<CaptureData>(r.GetString(29),CaptureJson)),r.GetInt64(13),r.GetString(14),Text(r,30));

    public async Task<AgendaPropertyReference?> FindAgendaPropertyReferenceAsync(AccessScope scope,Guid id,CancellationToken token)
    {
        if(scope.ActiveEstablishmentId is not {} establishment)return null;
        await using var c=await dataSource.OpenConnectionAsync(token);await using var tx=await Begin(c,scope,token);
        await using var cmd=new NpgsqlCommand("SELECT id,establishment_id,commercial_code,code,title,status FROM real_estate.properties WHERE organization_id=@org AND establishment_id=@unit AND id=@id",c,tx);
        cmd.Parameters.AddWithValue("org",scope.OrganizationId);cmd.Parameters.AddWithValue("unit",establishment);cmd.Parameters.AddWithValue("id",id);
        AgendaPropertyReference? result;await using(var r=await cmd.ExecuteReaderAsync(token)){result=await r.ReadAsync(token)?new(r.GetGuid(0),r.GetGuid(1),Text(r,2),r.GetString(3),r.GetString(4),r.GetString(5)):null;}
        await tx.CommitAsync(token);return result;
    }

    public async Task<WorkspaceV2> ListV2Async(AccessScope scope,CancellationToken token)
    {
        await using var c=await dataSource.OpenConnectionAsync(token);await using var tx=await Begin(c,scope,token);
        var properties=new List<PropertyV2>();var owners=new List<Owner>();
        await using(var cmd=new NpgsqlCommand($"SELECT {V2Columns} FROM real_estate.properties WHERE organization_id=@org ORDER BY updated_at DESC,id LIMIT 100",c,tx))
        {cmd.Parameters.AddWithValue("org",scope.OrganizationId);await using var r=await cmd.ExecuteReaderAsync(token);while(await r.ReadAsync(token))properties.Add(ReadV2(r));}
        await using(var cmd=new NpgsqlCommand("SELECT id,name FROM real_estate.owners WHERE organization_id=@org AND active ORDER BY name LIMIT 500",c,tx))
        {cmd.Parameters.AddWithValue("org",scope.OrganizationId);await using var r=await cmd.ExecuteReaderAsync(token);while(await r.ReadAsync(token))owners.Add(new(r.GetGuid(0),r.GetString(1)));}
        await tx.CommitAsync(token);return new(properties,owners,scope.Establishments);
    }
    public async Task<PropertyV2?> FindV2Async(AccessScope scope,Guid id,CancellationToken token)
    {
        await using var c=await dataSource.OpenConnectionAsync(token);await using var tx=await Begin(c,scope,token);
        await using var cmd=new NpgsqlCommand($"SELECT {V2Columns} FROM real_estate.properties WHERE organization_id=@org AND id=@id",c,tx);
        cmd.Parameters.AddWithValue("org",scope.OrganizationId);cmd.Parameters.AddWithValue("id",id);
        PropertyV2? item;await using(var r=await cmd.ExecuteReaderAsync(token)){item=await r.ReadAsync(token)?ReadV2(r):null;}
        await tx.CommitAsync(token);return item;
    }
    public Task<Outcome<PropertyV2>> SaveV2Async(AccessScope scope,PropertyInputV2 p,Guid? id,long version,CancellationToken token)=>SaveV2Core(scope,p,id,version,token);
    public Task<Outcome<PropertyV2>> CopyV2Async(AccessScope scope,PropertyInputV2 p,PropertyCopySource source,CancellationToken token)=>SaveV2Core(scope,p with{Code=null},null,0,token,source);
    private async Task<Outcome<PropertyV2>> SaveV2Core(AccessScope scope,PropertyInputV2 p,Guid? id,long version,CancellationToken token,PropertyCopySource? copy=null)
    {
        await using var c=await dataSource.OpenConnectionAsync(token);await using var tx=await Begin(c,scope,token);
        try
        {
            Dictionary<Guid,Guid> copiedMedia=[];
            CaptureData? sourceCapture=null;
            if(copy is not null){
                if(p.Capture is not null)p=p with{Capture=JsonSerializer.Deserialize<CaptureData>(JsonSerializer.Serialize(p.Capture,CaptureJson),CaptureJson)};
                await using(var source=new NpgsqlCommand("SELECT version,capture FROM real_estate.properties WHERE organization_id=@org AND id=@source FOR UPDATE",c,tx)){
                    source.Parameters.AddWithValue("org",scope.OrganizationId);source.Parameters.AddWithValue("source",copy.Id);
                    await using var current=await source.ExecuteReaderAsync(token);
                    if(!await current.ReadAsync(token))return Outcome<PropertyV2>.Fail("Resource.NotFound");
                    if(current.GetInt64(0)!=copy.Version)return Outcome<PropertyV2>.Fail("Property.VersionConflict");
                    sourceCapture=current.IsDBNull(1)?null:JsonSerializer.Deserialize<CaptureData>(current.GetString(1),CaptureJson);
                }
                var gallery=await MediaList(c,tx,scope,copy.Id,token);
                if(gallery.Revision!=copy.MediaRevision)return Outcome<PropertyV2>.Fail("Media.VersionConflict");
                copiedMedia=gallery.Items.Where(m=>copy.Excluded?.Contains(m.Id)!=true).ToDictionary(m=>m.Id,_=>Guid.NewGuid());
                if(copy.Cover.HasValue&&!gallery.Items.Any(m=>m.Id==copy.Cover&&m.Kind=="image"&&copiedMedia.ContainsKey(m.Id)))return Outcome<PropertyV2>.Fail("Media.InvalidContent");
                if(!gallery.Items.Any(m=>m.IsCover&&copiedMedia.ContainsKey(m.Id)))copy=copy with{Cover=copy.Cover??gallery.Items.FirstOrDefault(m=>m.Kind=="image"&&copiedMedia.ContainsKey(m.Id))?.Id};
                if(p.Capture is {} detached){
                    detached.Media.RemoveAll(m=>!copiedMedia.ContainsKey(m.MediaId));
                    foreach(var selection in detached.Selections.Where(s=>s.PhotoId.HasValue&&!copiedMedia.ContainsKey(s.PhotoId.Value)))selection.PhotoId=null;
                }
                if(gallery.Items.Where(m=>copiedMedia.ContainsKey(m.Id)).Sum(m=>(long)m.Size)+(copy.Files?.Sum(f=>(long)f.Content.Length)??0)>MediaRules.PropertyLimit)return Outcome<PropertyV2>.Fail("Media.TooLarge");
            }
            if(p.OwnerId.HasValue)
            {
            await using(var owner=new NpgsqlCommand("SELECT 1 FROM real_estate.owners WHERE organization_id=@org AND id=@id AND active",c,tx))
            {owner.Parameters.AddWithValue("org",scope.OrganizationId);owner.Parameters.AddWithValue("id",p.OwnerId.Value);if(await owner.ExecuteScalarAsync(token) is null)return Outcome<PropertyV2>.Fail("Property.OwnerUnavailable");}
            }
            var target=id??Guid.NewGuid();
            if(!id.HasValue && p.Code is null)p=p with{Code="IM-"+target.ToString("N")};
            CaptureData? previousCapture=null;
            if(id.HasValue)
            {
                await using var exists=new NpgsqlCommand("SELECT version,capture FROM real_estate.properties WHERE organization_id=@org AND id=@id FOR UPDATE",c,tx);
                exists.Parameters.AddWithValue("org",scope.OrganizationId);exists.Parameters.AddWithValue("id",target);
                await using var reader=await exists.ExecuteReaderAsync(token);
                if(!await reader.ReadAsync(token))return Outcome<PropertyV2>.Fail("Resource.NotFound");
                if(reader.GetInt64(0)!=version)return Outcome<PropertyV2>.Fail("Property.VersionConflict");
                previousCapture=reader.IsDBNull(1)?null:JsonSerializer.Deserialize<CaptureData>(reader.GetString(1),CaptureJson);
            }
            var policyCheck=await ValidatePolicyBinding(c,tx,scope,target,p.Capture,previousCapture,token);
            if(!policyCheck.IsSuccess)return Outcome<PropertyV2>.Fail(policyCheck.Code!);
            if(p.Capture is {} capture)
            {
                capture.PriceHistory=previousCapture?.PriceHistory??[];
                if(JsonSerializer.Serialize(previousCapture?.Values,CaptureJson)!=JsonSerializer.Serialize(capture.Values,CaptureJson))
                    capture.PriceHistory=[..capture.PriceHistory,new(id.HasValue?version+1:1,scope.Subject,DateTimeOffset.UtcNow,capture.Values)];
                foreach(var photo in capture.Selections.Where(s=>s.PhotoId.HasValue).Select(s=>s.PhotoId!.Value).Distinct())
                {
                    await using var check=new NpgsqlCommand("SELECT 1 FROM real_estate.property_media WHERE organization_id=@org AND property_id=@property AND id=@photo AND kind='image'",c,tx);
                    check.Parameters.AddWithValue("org",scope.OrganizationId);check.Parameters.AddWithValue("property",copy?.Id??target);check.Parameters.AddWithValue("photo",photo);
                    if(await check.ExecuteScalarAsync(token) is null)return Outcome<PropertyV2>.Fail("Capture.PhotoUnavailable");
                }
            }
            if(p.Capture?.Condominium.Id is {} condominiumId)
            {
                await using var check=new NpgsqlCommand("SELECT version FROM real_estate.condominiums WHERE organization_id=@org AND id=@condominium FOR SHARE",c,tx);
                check.Parameters.AddWithValue("org",scope.OrganizationId);check.Parameters.AddWithValue("condominium",condominiumId);
                var sharedVersion=await check.ExecuteScalarAsync(token);
                if(sharedVersion is null)return Outcome<PropertyV2>.Fail("Condominium.Unavailable");
                if(p.Capture.Condominium.Inherit==true){
                    if(p.Capture.Condominium.ObservedVersion is null)return Outcome<PropertyV2>.Fail("Profile.VersionRequired");
                    if(p.Capture.Condominium.ObservedVersion!=(long)sharedVersion)return Outcome<PropertyV2>.Fail("Profile.VersionConflict");
                    p.Capture.Selections.RemoveAll(s=>s.GroupId.StartsWith("collective-",StringComparison.Ordinal)&&!s.Selected&&s.SharedOverride is null&&s.Detail is null);
                }
            }
            if(p.Capture is {} annotated)
            {
                // Existing long captions remain compatible; only a new/changed description is limited.
                if(annotated.Media.Any(m=>m.Caption?.Length>200 && m.Caption!=(copy is null?previousCapture:sourceCapture)?.Media.FirstOrDefault(old=>old.MediaId==m.MediaId)?.Caption))
                    return Outcome<PropertyV2>.Fail("Capture.DescriptionTooLong");
                foreach(var item in annotated.Media)
                {
                    await using var check=new NpgsqlCommand("SELECT 1 FROM real_estate.property_media WHERE organization_id=@org AND property_id=@property AND id=@media",c,tx);
                    check.Parameters.AddWithValue("org",scope.OrganizationId);check.Parameters.AddWithValue("property",copy?.Id??target);check.Parameters.AddWithValue("media",item.MediaId);
                    if(await check.ExecuteScalarAsync(token) is null)return Outcome<PropertyV2>.Fail("Capture.PhotoUnavailable");
                }
            }
            if(copy is not null&&p.Capture is {} remapped){
                foreach(var annotation in remapped.Media)annotation.MediaId=copiedMedia[annotation.MediaId];
                foreach(var selection in remapped.Selections.Where(s=>s.PhotoId.HasValue))selection.PhotoId=copiedMedia[selection.PhotoId!.Value];
            }
            // Missing legacy text on update preserves the old free text; no automatic decomposition.
            var sql=id.HasValue?
                "UPDATE real_estate.properties SET capture=coalesce(@capture,capture),summary=@summary,internal_reference=@reference,is_in_condominium=@condominium,owner_id=@owner,code=@code,title=@title,category=@category,property_type=@type,purpose=@purpose,price=@price,address=coalesce(@legacy,address),postal_code=@postal,latitude=@lat,longitude=@lon,address_ibge=@ibge,address_uf=@uf,address_municipality=@municipality,address_street=@street,address_number=@number,address_without_number=@sn,address_complement=@complement,address_neighborhood=@neighborhood,address_country=@country,coordinate_source=@source,version=version+1,updated_by=@actor,updated_at=clock_timestamp() WHERE organization_id=@org AND id=@id":
                "INSERT INTO real_estate.properties(organization_id,id,establishment_id,owner_id,code,title,category,property_type,purpose,price,address,postal_code,latitude,longitude,"+AddressColumns+",summary,internal_reference,is_in_condominium,capture,created_by,updated_by) VALUES(@org,@id,@unit,@owner,@code,@title,@category,@type,@purpose,@price,@legacy,@postal,@lat,@lon,@ibge,@uf,@municipality,@street,@number,@sn,@complement,@neighborhood,@country,@source,@summary,@reference,@condominium,@capture,@actor,@actor)";
            await using var cmd=new NpgsqlCommand(sql+" RETURNING "+V2Columns,c,tx);
            cmd.Parameters.AddWithValue("org",scope.OrganizationId);cmd.Parameters.AddWithValue("id",target);cmd.Parameters.AddWithValue("unit",p.EstablishmentId!.Value);
            cmd.Parameters.AddWithValue("actor",scope.Subject);
            Add("owner",NpgsqlDbType.Uuid,p.OwnerId);Add("code",NpgsqlDbType.Text,p.Code);Add("title",NpgsqlDbType.Text,p.Title);
            Add("category",NpgsqlDbType.Text,p.Category);Add("type",NpgsqlDbType.Text,p.Type);Add("purpose",NpgsqlDbType.Text,p.Purpose);Add("price",NpgsqlDbType.Numeric,p.Price);
            Add("summary",NpgsqlDbType.Text,p.Summary);Add("reference",NpgsqlDbType.Text,p.InternalReference);Add("condominium",NpgsqlDbType.Boolean,p.IsInCondominium);
            Add("capture",NpgsqlDbType.Jsonb,p.Capture is null?null:JsonSerializer.Serialize(p.Capture,CaptureJson));
            var a=p.Address??new();
            void Add(string key,NpgsqlDbType type,object? value)=>cmd.Parameters.AddWithValue(key,type,value??DBNull.Value);
            Add("legacy",NpgsqlDbType.Text,a.LegacyText);Add("postal",NpgsqlDbType.Text,a.PostalCode);Add("lat",NpgsqlDbType.Numeric,a.Latitude);Add("lon",NpgsqlDbType.Numeric,a.Longitude);
            Add("ibge",NpgsqlDbType.Text,a.MunicipalityIbgeCode);Add("uf",NpgsqlDbType.Text,a.State);Add("municipality",NpgsqlDbType.Text,a.Municipality);
            Add("street",NpgsqlDbType.Text,a.Street);Add("number",NpgsqlDbType.Text,a.Number);Add("sn",NpgsqlDbType.Boolean,a.WithoutNumber);
            Add("complement",NpgsqlDbType.Text,a.Complement);Add("neighborhood",NpgsqlDbType.Text,a.Neighborhood);Add("country",NpgsqlDbType.Text,a.Country);Add("source",NpgsqlDbType.Text,a.CoordinateSource);
            PropertyV2 saved;await using(var r=await cmd.ExecuteReaderAsync(token)){await r.ReadAsync(token);saved=ReadV2(r);}
            if(copy is not null){
                var position=0;
                var order=(copy.Order??copiedMedia.Keys.ToArray()).Where(copiedMedia.ContainsKey).Distinct().ToArray();
                if(order.Length!=copiedMedia.Count)return Outcome<PropertyV2>.Fail("Media.InvalidOrder");
                foreach(var sourceId in order){
                    await using var file=new NpgsqlCommand("INSERT INTO real_estate.property_media(organization_id,property_id,id,kind,mime,name,content,byte_length,sha256,position,is_cover,created_by) SELECT organization_id,@target,@new,kind,mime,name,content,byte_length,sha256,@position,CASE WHEN @cover IS NULL THEN is_cover ELSE id=@cover END,@actor FROM real_estate.property_media WHERE organization_id=@org AND property_id=@source AND id=@old",c,tx);
                    file.Parameters.AddWithValue("target",target);file.Parameters.AddWithValue("new",copiedMedia[sourceId]);file.Parameters.AddWithValue("position",position++);file.Parameters.AddWithValue("cover",NpgsqlDbType.Uuid,(object?)copy.Cover??DBNull.Value);file.Parameters.AddWithValue("actor",scope.Subject);file.Parameters.AddWithValue("org",scope.OrganizationId);file.Parameters.AddWithValue("source",copy.Id);file.Parameters.AddWithValue("old",sourceId);await file.ExecuteNonQueryAsync(token);
                }
                if(copy.Files is {Count:>0}){
                    var cover=(await MediaList(c,tx,scope,target,token)).Items.Any(m=>m.IsCover);
                    foreach(var file in copy.Files){
                        var isCover=!cover&&file.Kind=="image";if(isCover)cover=true;
                        await using var insert=new NpgsqlCommand("INSERT INTO real_estate.property_media(organization_id,property_id,id,kind,mime,name,content,byte_length,sha256,position,is_cover,created_by) VALUES(@org,@property,@id,@kind,@mime,@name,@content,@size,@sha,@position,@cover,@actor)",c,tx);
                        insert.Parameters.AddWithValue("org",scope.OrganizationId);insert.Parameters.AddWithValue("property",target);insert.Parameters.AddWithValue("id",Guid.NewGuid());insert.Parameters.AddWithValue("kind",file.Kind);insert.Parameters.AddWithValue("mime",file.Mime);insert.Parameters.AddWithValue("name",file.Name);insert.Parameters.AddWithValue("content",file.Content);insert.Parameters.AddWithValue("size",file.Content.Length);insert.Parameters.AddWithValue("sha",file.Sha256);insert.Parameters.AddWithValue("position",position++);insert.Parameters.AddWithValue("cover",isCover);insert.Parameters.AddWithValue("actor",scope.Subject);await insert.ExecuteNonQueryAsync(token);
                    }
                }
                await Audit(c,tx,scope,target,"PropertyCopied",token);
                if(copiedMedia.Count>0||copy.Files is {Count:>0})await Audit(c,tx,scope,target,"PropertyMedia.copy",token);
            }
            await Audit(c,tx,scope,target,id.HasValue?"PropertyUpdated":"PropertyCreated",token);
            if(p.Capture?.Policy is {} policy&&policy!=previousCapture?.Policy){
                await using var applied=new NpgsqlCommand("INSERT INTO real_estate.commercial_policy_applications(organization_id,id,property_id,property_version,revision_id,revision_version,actor) VALUES(@org,@id,@property,@version,@revision,@revisionVersion,@actor)",c,tx);
                applied.Parameters.AddWithValue("org",scope.OrganizationId);applied.Parameters.AddWithValue("id",Guid.NewGuid());applied.Parameters.AddWithValue("property",target);applied.Parameters.AddWithValue("version",saved.Version);applied.Parameters.AddWithValue("revision",policy.RevisionId);applied.Parameters.AddWithValue("revisionVersion",policy.Version);applied.Parameters.AddWithValue("actor",scope.Subject);await applied.ExecuteNonQueryAsync(token);
                await Audit(c,tx,scope,target,"CommercialPolicyApplied",token);
            }
            await tx.CommitAsync(token);return new(saved);
        }
        catch(PostgresException e) when(e.ConstraintName=="announcement_closed"){return Outcome<PropertyV2>.Fail("Announcement.Closed");}
        catch(PostgresException e) when(e.ConstraintName=="property_purpose_immutable"){return Outcome<PropertyV2>.Fail("Announcement.PurposeImmutable");}
        catch(PostgresException e) when(e.ConstraintName=="announcement_exclusive_purpose"){return Outcome<PropertyV2>.Fail("Property.PurposeInvalid");}
        catch(PostgresException e) when(e.SqlState=="23505"){return Outcome<PropertyV2>.Fail("Property.DuplicateCode");}
    }
}
