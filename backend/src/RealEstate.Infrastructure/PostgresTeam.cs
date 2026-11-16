using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using RealEstate.Domain;
namespace RealEstate.Infrastructure;

public sealed partial class PostgresPropertyStore:ITeamStore
{
    private static readonly JsonSerializerOptions TeamJson=new(JsonSerializerDefaults.Web);
    async Task<TeamRecord?> ITeamStore.FindAsync(AccessScope scope,Guid id,CancellationToken token)
    {
        await using var c=await dataSource.OpenConnectionAsync(token);await using var tx=await Begin(c,scope,token);
        await using var cmd=new NpgsqlCommand("SELECT data,version FROM real_estate.team_professionals WHERE organization_id=@org AND establishment_id=@unit AND directory_id=@id",c,tx);CustomerScope(cmd,scope);cmd.Parameters.AddWithValue("id",id);
        TeamRecord? result=null;await using(var r=await cmd.ExecuteReaderAsync(token)){if(await r.ReadAsync(token))result=new(id,JsonSerializer.Deserialize<TeamProfessional>(r.GetString(0),TeamJson)!,r.GetInt64(1));}
        await tx.CommitAsync(token);return result;
    }
    public async Task<Outcome<TeamRecord>> SaveAsync(AccessScope scope,Guid id,TeamProfessional input,long version,CancellationToken token)
    {
        await using var c=await dataSource.OpenConnectionAsync(token);await using var tx=await Begin(c,scope,token);
        var sql=version==0?
          "INSERT INTO real_estate.team_professionals(organization_id,establishment_id,directory_id,data,version,updated_by) VALUES(@org,@unit,@id,@data,1,@actor) ON CONFLICT DO NOTHING RETURNING version":
          "UPDATE real_estate.team_professionals SET data=@data,version=version+1,updated_by=@actor,updated_at=clock_timestamp() WHERE organization_id=@org AND establishment_id=@unit AND directory_id=@id AND version=@version RETURNING version";
        await using var cmd=new NpgsqlCommand(sql,c,tx);CustomerScope(cmd,scope);cmd.Parameters.AddWithValue("id",id);cmd.Parameters.AddWithValue("data",NpgsqlDbType.Jsonb,JsonSerializer.Serialize(input,TeamJson));cmd.Parameters.AddWithValue("actor",scope.Subject);if(version>0)cmd.Parameters.AddWithValue("version",version);
        var saved=await cmd.ExecuteScalarAsync(token);if(saved is null)return Outcome<TeamRecord>.Fail("Team.VersionConflict");
        await Audit(c,tx,scope,id,version==0?"TeamProfessionalLinked":"TeamProfessionalUpdated",token);await tx.CommitAsync(token);
        return new(new TeamRecord(id,input,(long)saved));
    }
}

