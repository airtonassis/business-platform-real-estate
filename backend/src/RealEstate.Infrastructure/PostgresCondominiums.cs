using Npgsql;
using NpgsqlTypes;
using RealEstate.Domain;
namespace RealEstate.Infrastructure;
public sealed partial class PostgresPropertyStore:ICondominiumStore
{
    public async Task<IReadOnlyList<CondominiumProfile>> SearchCondominiumsAsync(AccessScope scope,string query,CancellationToken token)
    {
        await using var c=await dataSource.OpenConnectionAsync(token);await using var tx=await Begin(c,scope,token);
        await using var cmd=new NpgsqlCommand("SELECT id,name,kind,towers,floors,notes,characteristics,version FROM real_estate.condominiums WHERE organization_id=@org AND (position(lower(@query) in lower(name))>0 OR id::text=@query) ORDER BY name,id LIMIT 50",c,tx);cmd.Parameters.AddWithValue("org",scope.OrganizationId);cmd.Parameters.AddWithValue("query",query);
        var result=new List<CondominiumProfile>();await using(var r=await cmd.ExecuteReaderAsync(token)){while(await r.ReadAsync(token))result.Add(new(r.GetGuid(0),new(r.GetString(1),r.GetString(2),r.IsDBNull(3)?null:r.GetInt32(3),r.IsDBNull(4)?null:r.GetInt32(4),r.IsDBNull(5)?null:r.GetString(5),System.Text.Json.JsonSerializer.Deserialize<List<CaptureSelection>>(r.GetString(6),CaptureJson)),r.GetInt64(7)));}await tx.CommitAsync(token);return result;
    }
    public async Task<Outcome<CondominiumProfile>> CreateCondominiumAsync(AccessScope scope,CondominiumInput input,CancellationToken token)
    {
        await using var c=await dataSource.OpenConnectionAsync(token);await using var tx=await Begin(c,scope,token);var id=Guid.NewGuid();
        await using var cmd=new NpgsqlCommand("INSERT INTO real_estate.condominiums(organization_id,id,name,kind,towers,floors,notes,created_by) VALUES(@org,@id,@name,@kind,@towers,@floors,@notes,@actor)",c,tx);
        cmd.Parameters.AddWithValue("org",scope.OrganizationId);cmd.Parameters.AddWithValue("id",id);cmd.Parameters.AddWithValue("name",input.Name);cmd.Parameters.AddWithValue("kind",input.Kind);cmd.Parameters.AddWithValue("towers",NpgsqlDbType.Integer,(object?)input.Towers??DBNull.Value);cmd.Parameters.AddWithValue("floors",NpgsqlDbType.Integer,(object?)input.Floors??DBNull.Value);cmd.Parameters.AddWithValue("notes",NpgsqlDbType.Text,(object?)input.Notes??DBNull.Value);cmd.Parameters.AddWithValue("actor",scope.Subject);await cmd.ExecuteNonQueryAsync(token);
        await Audit(c,tx,scope,id,"CondominiumCreated",token);await tx.CommitAsync(token);return new(new CondominiumProfile(id,input));
    }
}
