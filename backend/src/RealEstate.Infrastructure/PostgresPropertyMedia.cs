using Npgsql;
using RealEstate.Domain;
namespace RealEstate.Infrastructure;
public sealed partial class PostgresPropertyStore:IPropertyMediaStore
{
    private static async Task<bool> MediaParent(NpgsqlConnection c,NpgsqlTransaction tx,AccessScope scope,Guid id,bool locked,CancellationToken ct){await using var cmd=new NpgsqlCommand("SELECT 1 FROM real_estate.properties WHERE organization_id=@org AND id=@id"+(locked?" FOR UPDATE":""),c,tx);cmd.Parameters.AddWithValue("org",scope.OrganizationId);cmd.Parameters.AddWithValue("id",id);return await cmd.ExecuteScalarAsync(ct) is not null;}
    private static async Task<MediaCollection> MediaList(NpgsqlConnection c,NpgsqlTransaction tx,AccessScope scope,Guid id,CancellationToken ct){
        await using var cmd=new NpgsqlCommand("SELECT id,kind,mime,name,byte_length,position,is_cover,version FROM real_estate.property_media WHERE organization_id=@org AND property_id=@id ORDER BY position,id",c,tx);cmd.Parameters.AddWithValue("org",scope.OrganizationId);cmd.Parameters.AddWithValue("id",id);
        var items=new List<PropertyMedia>();await using(var r=await cmd.ExecuteReaderAsync(ct))while(await r.ReadAsync(ct))items.Add(new(r.GetGuid(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetInt32(4),r.GetInt32(5),r.GetBoolean(6),r.GetInt64(7)));
        await using var epoch=new NpgsqlCommand("SELECT count(*) FROM real_estate.events WHERE organization_id=@org AND entity_id=@id AND event_type LIKE 'PropertyMedia.%'",c,tx);epoch.Parameters.AddWithValue("org",scope.OrganizationId);epoch.Parameters.AddWithValue("id",id);return MediaRules.Collection(items,(long)(await epoch.ExecuteScalarAsync(ct))!);
    }
    public async Task<MediaCollection?> ListMediaAsync(AccessScope scope,Guid propertyId,CancellationToken token){await using var c=await dataSource.OpenConnectionAsync(token);await using var tx=await Begin(c,scope,token);if(!await MediaParent(c,tx,scope,propertyId,false,token))return null;var result=await MediaList(c,tx,scope,propertyId,token);await tx.CommitAsync(token);return result;}
    public async Task<MediaContent?> ReadMediaAsync(AccessScope scope,Guid propertyId,Guid mediaId,CancellationToken token){
        await using var c=await dataSource.OpenConnectionAsync(token);await using var tx=await Begin(c,scope,token);
        await using var cmd=new NpgsqlCommand("SELECT mime,content FROM real_estate.property_media WHERE organization_id=@org AND property_id=@property AND id=@id",c,tx);cmd.Parameters.AddWithValue("org",scope.OrganizationId);cmd.Parameters.AddWithValue("property",propertyId);cmd.Parameters.AddWithValue("id",mediaId);
        MediaContent? result;await using(var r=await cmd.ExecuteReaderAsync(token))result=await r.ReadAsync(token)?new(r.GetString(0),r.GetFieldValue<byte[]>(1)):null;await tx.CommitAsync(token);return result;
    }
    public async Task<IReadOnlyDictionary<Guid,Guid>> CoversAsync(AccessScope scope,Guid[] properties,CancellationToken token){
        if(properties.Length==0)return new Dictionary<Guid,Guid>();await using var c=await dataSource.OpenConnectionAsync(token);await using var tx=await Begin(c,scope,token);
        await using var cmd=new NpgsqlCommand("SELECT property_id,id FROM real_estate.property_media WHERE organization_id=@org AND property_id=ANY(@ids) AND is_cover",c,tx);cmd.Parameters.AddWithValue("org",scope.OrganizationId);cmd.Parameters.AddWithValue("ids",properties);
        var result=new Dictionary<Guid,Guid>();await using(var r=await cmd.ExecuteReaderAsync(token))while(await r.ReadAsync(token))result.Add(r.GetGuid(0),r.GetGuid(1));await tx.CommitAsync(token);return result;
    }
    public async Task<Outcome<MediaCollection>> ChangeMediaAsync(AccessScope scope,Guid propertyId,string revision,MediaChange change,CancellationToken token){
        await using var c=await dataSource.OpenConnectionAsync(token);await using var tx=await Begin(c,scope,token);
        if(!await MediaParent(c,tx,scope,propertyId,true,token))return Outcome<MediaCollection>.Fail("Resource.NotFound");
        var current=await MediaList(c,tx,scope,propertyId,token);
        if(string.IsNullOrWhiteSpace(revision)||current.Revision!=revision)return Outcome<MediaCollection>.Fail("Media.VersionConflict");
        async Task Execute(string sql,Guid? media=null,int? position=null){await using var cmd=new NpgsqlCommand(sql,c,tx);cmd.Parameters.AddWithValue("org",scope.OrganizationId);cmd.Parameters.AddWithValue("property",propertyId);if(media.HasValue)cmd.Parameters.AddWithValue("id",media.Value);if(position.HasValue)cmd.Parameters.AddWithValue("position",position.Value);await cmd.ExecuteNonQueryAsync(token);}
        const string where=" WHERE organization_id=@org AND property_id=@property";
        if(change.Action=="upload"){
            var files=change.Files;
            if(files is null||files.Count is <1 or >MediaRules.FileCountLimit||files.Any(f=>f.Content.Length==0))return Outcome<MediaCollection>.Fail("Media.InvalidContent");
            if(files.Sum(x=>(long)x.Content.Length)>MediaRules.BatchLimit||current.Items.Sum(x=>(long)x.Size)+files.Sum(x=>(long)x.Content.Length)>MediaRules.PropertyLimit)return Outcome<MediaCollection>.Fail("Media.TooLarge");
            var next=current.Items.Count==0?0:current.Items.Max(x=>x.Position)+1;var cover=current.Items.Any(x=>x.IsCover);
            foreach(var file in files){
                var isCover=!cover&&file.Kind=="image";if(isCover)cover=true;
                await using var cmd=new NpgsqlCommand("INSERT INTO real_estate.property_media(organization_id,property_id,id,kind,mime,name,content,byte_length,sha256,position,is_cover,created_by) VALUES(@org,@property,@id,@kind,@mime,@name,@content,@size,@sha,@position,@cover,@actor)",c,tx);
                cmd.Parameters.AddWithValue("org",scope.OrganizationId);cmd.Parameters.AddWithValue("property",propertyId);cmd.Parameters.AddWithValue("id",Guid.NewGuid());cmd.Parameters.AddWithValue("kind",file.Kind);cmd.Parameters.AddWithValue("mime",file.Mime);cmd.Parameters.AddWithValue("name",file.Name);cmd.Parameters.AddWithValue("content",file.Content);cmd.Parameters.AddWithValue("size",file.Content.Length);cmd.Parameters.AddWithValue("sha",file.Sha256);cmd.Parameters.AddWithValue("position",next++);cmd.Parameters.AddWithValue("cover",isCover);cmd.Parameters.AddWithValue("actor",scope.Subject);await cmd.ExecuteNonQueryAsync(token);
            }
        }else if(change.Action=="cover"){
            if(!current.Items.Any(x=>x.Id==change.MediaId&&x.Kind=="image"))return Outcome<MediaCollection>.Fail("Media.InvalidContent");
            await Execute("UPDATE real_estate.property_media SET is_cover=false,version=version+1"+where+" AND is_cover");
            await Execute("UPDATE real_estate.property_media SET is_cover=true,version=version+1"+where+" AND id=@id",change.MediaId);
        }else if(change.Action=="remove"){
            if(!current.Items.Any(x=>x.Id==change.MediaId))return Outcome<MediaCollection>.Fail("Resource.NotFound");
            await Execute("DELETE FROM real_estate.property_media"+where+" AND id=@id",change.MediaId);
            // Explicit removal also detaches capture annotations/photo links atomically.
            await using(var read=new NpgsqlCommand("SELECT capture FROM real_estate.properties WHERE organization_id=@org AND id=@property",c,tx)){
                read.Parameters.AddWithValue("org",scope.OrganizationId);read.Parameters.AddWithValue("property",propertyId);
                var json=await read.ExecuteScalarAsync(token);
                if(json is string text && System.Text.Json.JsonSerializer.Deserialize<CaptureData>(text,CaptureJson) is {} capture){
                    var changed=capture.Media.RemoveAll(m=>m.MediaId==change.MediaId)>0;
                    foreach(var selection in capture.Selections.Where(s=>s.PhotoId==change.MediaId)){selection.PhotoId=null;changed=true;}
                    if(changed){await using var update=new NpgsqlCommand("UPDATE real_estate.properties SET capture=@capture,version=version+1,updated_by=@actor,updated_at=clock_timestamp() WHERE organization_id=@org AND id=@property",c,tx);
                        update.Parameters.AddWithValue("capture",NpgsqlTypes.NpgsqlDbType.Jsonb,System.Text.Json.JsonSerializer.Serialize(capture,CaptureJson));update.Parameters.AddWithValue("actor",scope.Subject);update.Parameters.AddWithValue("org",scope.OrganizationId);update.Parameters.AddWithValue("property",propertyId);await update.ExecuteNonQueryAsync(token);}
                }
            }
            var left=await MediaList(c,tx,scope,propertyId,token);
            for(var i=0;i<left.Items.Count;i++)await Execute("UPDATE real_estate.property_media SET position=@position,version=version+1"+where+" AND id=@id",left.Items[i].Id,i);
            if(!left.Items.Any(x=>x.IsCover)&&left.Items.FirstOrDefault(x=>x.Kind=="image") is {} replacement)await Execute("UPDATE real_estate.property_media SET is_cover=true,version=version+1"+where+" AND id=@id",replacement.Id);
        }else if(change.Action=="reorder"){
            var order=change.Order;
            if(order is null||order.Count!=current.Items.Count||order.Distinct().Count()!=order.Count||!order.ToHashSet().SetEquals(current.Items.Select(x=>x.Id)))return Outcome<MediaCollection>.Fail("Media.InvalidOrder");
            for(var i=0;i<order.Count;i++)await Execute("UPDATE real_estate.property_media SET position=@position,version=version+1"+where+" AND id=@id",order[i],i);
        }else return Outcome<MediaCollection>.Fail("Media.InvalidContent");
        await Audit(c,tx,scope,propertyId,"PropertyMedia."+change.Action,token);
        var result=await MediaList(c,tx,scope,propertyId,token);await tx.CommitAsync(token);return new(result);
    }
}
