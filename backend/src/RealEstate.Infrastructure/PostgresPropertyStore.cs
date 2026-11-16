using Npgsql;
using RealEstate.Domain;
namespace RealEstate.Infrastructure;

public sealed partial class PostgresPropertyStore(NpgsqlDataSource dataSource):IPropertyStore,IPropertyStoreV2
{
    private static async Task<NpgsqlTransaction> Begin(NpgsqlConnection connection,AccessScope scope,CancellationToken token)
    {
        var tx=await connection.BeginTransactionAsync(token);
        await using var cmd=new NpgsqlCommand("SELECT set_config('erp.organization_id',@org,true),set_config('erp.establishments',@units,true),set_config('erp.legacy_manager',@legacy,true),set_config('erp.active_establishment',@active,true)",connection,tx);
        cmd.Parameters.AddWithValue("active",scope.ActiveEstablishmentId?.ToString()??"");
        cmd.Parameters.AddWithValue("org",scope.OrganizationId.ToString());cmd.Parameters.AddWithValue("units","{"+string.Join(',',scope.Establishments?.Select(e=>e.Id)??[])+"}");cmd.Parameters.AddWithValue("legacy",scope.LegacyManager?"enabled":"disabled");await cmd.ExecuteNonQueryAsync(token);return tx;
    }
    private const string Columns="id,organization_id,code,title,owner_id,category,property_type,purpose,address,postal_code,latitude,longitude,price,version,status,establishment_id";
    private const string LegacyCompatibleSql="owner_id IS NOT NULL AND code IS NOT NULL AND title IS NOT NULL AND category IS NOT NULL AND property_type IS NOT NULL AND purpose IN ('Venda','Locação','Temporada','Permuta','Lançamento') AND price IS NOT NULL AND address IS NOT NULL AND length(btrim(address))>0 AND postal_code IS NOT NULL AND latitude IS NOT NULL AND longitude IS NOT NULL";
    private static string? Text(NpgsqlDataReader r,int index)=>r.IsDBNull(index)?null:r.GetString(index);
    private static decimal? Number(NpgsqlDataReader r,int index)=>r.IsDBNull(index)?null:r.GetDecimal(index);
    private static Property Read(NpgsqlDataReader r)=>new(r.GetGuid(0),r.GetGuid(1),new(Text(r,2)!,Text(r,3)!,r.IsDBNull(4)?Guid.Empty:r.GetGuid(4),Text(r,5)!,Text(r,6)!,Text(r,7)!,Text(r,8)!,Text(r,9)!,Number(r,10),Number(r,11),Number(r,12)??0,r.IsDBNull(15)?null:r.GetGuid(15)),r.GetInt64(13),r.GetString(14));
    public async Task<Workspace> ListAsync(AccessScope scope,CancellationToken token)
    {
        await using var c=await dataSource.OpenConnectionAsync(token);await using var tx=await Begin(c,scope,token);
        var properties=new List<Property>();var owners=new List<Owner>();
        await using(var cmd=new NpgsqlCommand($"SELECT {Columns} FROM real_estate.properties WHERE organization_id=@org AND {LegacyCompatibleSql} ORDER BY updated_at DESC,id LIMIT 100",c,tx))
        {cmd.Parameters.AddWithValue("org",scope.OrganizationId);await using var r=await cmd.ExecuteReaderAsync(token);while(await r.ReadAsync(token))properties.Add(Read(r));}
        await using(var cmd=new NpgsqlCommand("SELECT id,name FROM real_estate.owners WHERE organization_id=@org AND active ORDER BY name LIMIT 500",c,tx))
        {cmd.Parameters.AddWithValue("org",scope.OrganizationId);await using var r=await cmd.ExecuteReaderAsync(token);while(await r.ReadAsync(token))owners.Add(new(r.GetGuid(0),r.GetString(1)));}
        await tx.CommitAsync(token);return new(properties,owners,scope.Establishments);
    }
    public async Task<Property?> FindAsync(AccessScope scope,Guid id,CancellationToken token)
    {
        await using var c=await dataSource.OpenConnectionAsync(token);await using var tx=await Begin(c,scope,token);
        await using var cmd=new NpgsqlCommand($"SELECT {Columns} FROM real_estate.properties WHERE organization_id=@org AND id=@id",c,tx);
        cmd.Parameters.AddWithValue("org",scope.OrganizationId);cmd.Parameters.AddWithValue("id",id);
        Property? result;await using(var r=await cmd.ExecuteReaderAsync(token)){result=await r.ReadAsync(token)?Read(r):null;}
        await tx.CommitAsync(token);return result;
    }
    private static async Task Audit(NpgsqlConnection c,NpgsqlTransaction tx,AccessScope s,Guid id,string type,CancellationToken token)
    {
        await using var cmd=new NpgsqlCommand("INSERT INTO real_estate.events(organization_id,id,entity_id,event_type,actor) VALUES(@org,@event,@id,@type,@actor)",c,tx);
        cmd.Parameters.AddWithValue("org",s.OrganizationId);cmd.Parameters.AddWithValue("event",Guid.NewGuid());cmd.Parameters.AddWithValue("id",id);cmd.Parameters.AddWithValue("type",type);cmd.Parameters.AddWithValue("actor",s.Subject);await cmd.ExecuteNonQueryAsync(token);
    }
    public async Task<Outcome<Property>> SaveAsync(AccessScope scope,PropertyInput p,Guid? id,long version,CancellationToken token)
    {
        await using var c=await dataSource.OpenConnectionAsync(token);await using var tx=await Begin(c,scope,token);
        try
        {
            await using(var owner=new NpgsqlCommand("SELECT 1 FROM real_estate.owners WHERE organization_id=@org AND id=@id AND active",c,tx))
            {owner.Parameters.AddWithValue("org",scope.OrganizationId);owner.Parameters.AddWithValue("id",p.OwnerId);if(await owner.ExecuteScalarAsync(token) is null)return Outcome<Property>.Fail("Property.OwnerUnavailable");}
            var target=id??Guid.NewGuid();
            if(id.HasValue)
            {
                await using var exists=new NpgsqlCommand("SELECT version FROM real_estate.properties WHERE organization_id=@org AND id=@id FOR UPDATE",c,tx);
                exists.Parameters.AddWithValue("org",scope.OrganizationId);exists.Parameters.AddWithValue("id",target);
                var current=await exists.ExecuteScalarAsync(token);
                if(current is null)return Outcome<Property>.Fail("Resource.NotFound");
                if((long)current!=version)return Outcome<Property>.Fail("Property.VersionConflict");
            }
            var sql=id.HasValue?
                "UPDATE real_estate.properties SET owner_id=@owner,code=@code,title=@title,category=@category,property_type=@type,purpose=@purpose,address=@address,postal_code=@postal,latitude=@lat,longitude=@lon,price=@price,version=version+1,updated_by=@actor,updated_at=clock_timestamp() WHERE organization_id=@org AND id=@id":
                "INSERT INTO real_estate.properties(organization_id,id,establishment_id,owner_id,code,title,category,property_type,purpose,address,postal_code,latitude,longitude,price,created_by,updated_by) VALUES(@org,@id,@unit,@owner,@code,@title,@category,@type,@purpose,@address,@postal,@lat,@lon,@price,@actor,@actor)";
            await using var cmd=new NpgsqlCommand(sql+" RETURNING "+Columns,c,tx);
            cmd.Parameters.AddWithValue("unit",p.EstablishmentId!.Value);cmd.Parameters.AddWithValue("org",scope.OrganizationId);cmd.Parameters.AddWithValue("id",target);cmd.Parameters.AddWithValue("owner",p.OwnerId);
            cmd.Parameters.AddWithValue("code",p.Code);cmd.Parameters.AddWithValue("title",p.Title);cmd.Parameters.AddWithValue("category",p.Category);cmd.Parameters.AddWithValue("type",p.Type);cmd.Parameters.AddWithValue("purpose",p.Purpose);cmd.Parameters.AddWithValue("address",p.Address);cmd.Parameters.AddWithValue("postal",p.PostalCode);cmd.Parameters.AddWithValue("lat",p.Latitude!.Value);cmd.Parameters.AddWithValue("lon",p.Longitude!.Value);cmd.Parameters.AddWithValue("price",p.Price);cmd.Parameters.AddWithValue("actor",scope.Subject);
            Property property;await using(var r=await cmd.ExecuteReaderAsync(token)){await r.ReadAsync(token);property=Read(r);}
            await Audit(c,tx,scope,target,id.HasValue?"PropertyUpdated":"PropertyCreated",token);
            await tx.CommitAsync(token);return new(property);
        }
        catch(PostgresException e) when(e.ConstraintName=="property_purpose_immutable"){return Outcome<Property>.Fail("Announcement.PurposeImmutable");}
        catch(PostgresException e) when(e.ConstraintName=="announcement_exclusive_purpose"){return Outcome<Property>.Fail("Property.PurposeInvalid");}
        catch(PostgresException e) when(e.SqlState=="23505"){return Outcome<Property>.Fail("Property.DuplicateCode");}
    }
    public async Task<Outcome<Owner>> CreateOwnerAsync(AccessScope scope,string name,CancellationToken token)
    {
        await using var c=await dataSource.OpenConnectionAsync(token);await using var tx=await Begin(c,scope,token);
        var id=Guid.NewGuid();await using var cmd=new NpgsqlCommand("INSERT INTO real_estate.owners(organization_id,id,name,created_by) VALUES(@org,@id,@name,@actor)",c,tx);
        cmd.Parameters.AddWithValue("org",scope.OrganizationId);cmd.Parameters.AddWithValue("id",id);cmd.Parameters.AddWithValue("name",name);cmd.Parameters.AddWithValue("actor",scope.Subject);await cmd.ExecuteNonQueryAsync(token);
        await Audit(c,tx,scope,id,"OwnerCreated",token);await tx.CommitAsync(token);return new(new Owner(id,name));
    }
}

