using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using RealEstate.Domain;
namespace RealEstate.Infrastructure;
public sealed partial class PostgresPropertyStore
{
    public async Task<IReadOnlyList<AgendaOwnerReference>> SearchAgendaOwnersAsync(AccessScope scope,string query,CancellationToken token)
    {
        if(string.IsNullOrWhiteSpace(query)||query.Trim().Length>200)return Array.Empty<AgendaOwnerReference>();
        await using var c=await dataSource.OpenConnectionAsync(token);await using var tx=await Begin(c,scope,token);
        await using var cmd=new NpgsqlCommand("SELECT id,name FROM real_estate.owners WHERE organization_id=@org AND active AND position(lower(@query) in lower(name))>0 ORDER BY name,id LIMIT 50",c,tx);
        cmd.Parameters.AddWithValue("org",scope.OrganizationId);cmd.Parameters.AddWithValue("query",query.Trim());
        var result=new List<AgendaOwnerReference>();await using(var r=await cmd.ExecuteReaderAsync(token)){while(await r.ReadAsync(token))result.Add(new(r.GetGuid(0),r.GetString(1)));}
        await tx.CommitAsync(token);return result;
    }

    public async Task<AgendaOwnerReference?> FindAgendaOwnerReferenceAsync(AccessScope scope,Guid id,CancellationToken token)
    {
        await using var c=await dataSource.OpenConnectionAsync(token);await using var tx=await Begin(c,scope,token);
        await using var cmd=new NpgsqlCommand("SELECT id,name FROM real_estate.owners WHERE organization_id=@org AND id=@id AND active",c,tx);
        cmd.Parameters.AddWithValue("org",scope.OrganizationId);cmd.Parameters.AddWithValue("id",id);
        AgendaOwnerReference? result;await using(var r=await cmd.ExecuteReaderAsync(token)){result=await r.ReadAsync(token)?new(r.GetGuid(0),r.GetString(1)):null;}
        await tx.CommitAsync(token);return result;
    }

    public async Task<OwnerProfile?> FindOwnerProfileAsync(AccessScope scope,Guid id,CancellationToken token)
    {
        await using var c=await dataSource.OpenConnectionAsync(token);await using var tx=await Begin(c,scope,token);
        var result=await ReadOwner(c,tx,scope,id,false,token);await tx.CommitAsync(token);return result;
    }
    static async Task<OwnerProfile?> ReadOwner(NpgsqlConnection c,NpgsqlTransaction tx,AccessScope scope,Guid id,bool locked,CancellationToken token)
    {
        await using var cmd=new NpgsqlCommand("SELECT id,name,person_kind,tax_id,version,history,details FROM real_estate.owners WHERE organization_id=@org AND id=@id AND active"+(locked?" FOR UPDATE":""),c,tx);cmd.Parameters.AddWithValue("org",scope.OrganizationId);cmd.Parameters.AddWithValue("id",id);
        await using var r=await cmd.ExecuteReaderAsync(token);return await r.ReadAsync(token)?new(r.GetGuid(0),r.GetString(1),r.IsDBNull(2)?"":r.GetString(2),r.IsDBNull(3)?"":r.GetString(3),r.GetInt64(4),JsonSerializer.Deserialize<List<ProfileRevision>>(r.GetString(5),CaptureJson),r.IsDBNull(6)?null:JsonSerializer.Deserialize<OwnerDetails>(r.GetString(6),CaptureJson)):null;
    }
    public async Task<Outcome<OwnerProfile>> UpdateOwnerProfileAsync(AccessScope scope,Guid id,long version,OwnerProfileInput input,CancellationToken token)
    {
        await using var c=await dataSource.OpenConnectionAsync(token);await using var tx=await Begin(c,scope,token);
        try{
            var previous=await ReadOwner(c,tx,scope,id,true,token);if(previous is null)return Outcome<OwnerProfile>.Fail("Resource.NotFound");if(previous.Version!=version)return Outcome<OwnerProfile>.Fail("Profile.VersionConflict");
            var history=await ProfileHistory(c,tx,scope,id,"owners",previous.Version,previous.History,new OwnerProfileInput(previous.Name,previous.PersonKind,previous.TaxId,previous.Details),input,token);
            await using var cmd=new NpgsqlCommand("UPDATE real_estate.owners SET name=@name,person_kind=@kind,tax_id=@tax,version=version+1,history=@history,details=@details WHERE organization_id=@org AND id=@id",c,tx);cmd.Parameters.AddWithValue("details",NpgsqlDbType.Jsonb,input.Details is null?DBNull.Value:JsonSerializer.Serialize(input.Details,CaptureJson));cmd.Parameters.AddWithValue("name",input.Name);cmd.Parameters.AddWithValue("kind",NpgsqlDbType.Text,string.IsNullOrEmpty(input.PersonKind)?DBNull.Value:input.PersonKind);cmd.Parameters.AddWithValue("tax",NpgsqlDbType.Text,string.IsNullOrEmpty(input.TaxId)?DBNull.Value:input.TaxId);cmd.Parameters.AddWithValue("history",NpgsqlDbType.Jsonb,JsonSerializer.Serialize(history,CaptureJson));cmd.Parameters.AddWithValue("org",scope.OrganizationId);cmd.Parameters.AddWithValue("id",id);await cmd.ExecuteNonQueryAsync(token);
            await Audit(c,tx,scope,id,"OwnerProfileUpdated",token);await tx.CommitAsync(token);return new(new OwnerProfile(id,input.Name,input.PersonKind,input.TaxId,version+1,history,input.Details));
        }catch(PostgresException e)when(e.SqlState=="23505"){return Outcome<OwnerProfile>.Fail("Owner.DuplicateTaxId");}
    }
    public async Task<CondominiumProfile?> FindCondominiumAsync(AccessScope scope,Guid id,CancellationToken token)
    {
        await using var c=await dataSource.OpenConnectionAsync(token);await using var tx=await Begin(c,scope,token);var result=await ReadCondominium(c,tx,scope,id,false,token);await tx.CommitAsync(token);return result;
    }
    static async Task<CondominiumProfile?> ReadCondominium(NpgsqlConnection c,NpgsqlTransaction tx,AccessScope scope,Guid id,bool locked,CancellationToken token)
    {
        await using var cmd=new NpgsqlCommand("SELECT id,name,kind,towers,floors,notes,characteristics,version,history FROM real_estate.condominiums WHERE organization_id=@org AND id=@id"+(locked?" FOR UPDATE":""),c,tx);cmd.Parameters.AddWithValue("org",scope.OrganizationId);cmd.Parameters.AddWithValue("id",id);await using var r=await cmd.ExecuteReaderAsync(token);
        return await r.ReadAsync(token)?new(r.GetGuid(0),new(r.GetString(1),r.GetString(2),r.IsDBNull(3)?null:r.GetInt32(3),r.IsDBNull(4)?null:r.GetInt32(4),r.IsDBNull(5)?null:r.GetString(5),JsonSerializer.Deserialize<List<CaptureSelection>>(r.GetString(6),CaptureJson)),r.GetInt64(7),JsonSerializer.Deserialize<List<ProfileRevision>>(r.GetString(8),CaptureJson)):null;
    }
    public async Task<Outcome<CondominiumProfile>> UpdateCondominiumAsync(AccessScope scope,Guid id,long version,CondominiumInput input,CancellationToken token)
    {
        await using var c=await dataSource.OpenConnectionAsync(token);await using var tx=await Begin(c,scope,token);
        var previous=await ReadCondominium(c,tx,scope,id,true,token);if(previous is null)return Outcome<CondominiumProfile>.Fail("Resource.NotFound");if(previous.Version!=version)return Outcome<CondominiumProfile>.Fail("Profile.VersionConflict");
        var history=await ProfileHistory(c,tx,scope,id,"condominiums",previous.Version,previous.History,previous.Data,input,token);
        await using var cmd=new NpgsqlCommand("UPDATE real_estate.condominiums SET name=@name,kind=@kind,towers=@towers,floors=@floors,notes=@notes,characteristics=@features,version=version+1,history=@history WHERE organization_id=@org AND id=@id",c,tx);
        cmd.Parameters.AddWithValue("name",input.Name);cmd.Parameters.AddWithValue("kind",input.Kind);cmd.Parameters.AddWithValue("towers",NpgsqlDbType.Integer,(object?)input.Towers??DBNull.Value);cmd.Parameters.AddWithValue("floors",NpgsqlDbType.Integer,(object?)input.Floors??DBNull.Value);cmd.Parameters.AddWithValue("notes",NpgsqlDbType.Text,(object?)input.Notes??DBNull.Value);cmd.Parameters.AddWithValue("features",NpgsqlDbType.Jsonb,JsonSerializer.Serialize(input.Characteristics??[],CaptureJson));cmd.Parameters.AddWithValue("history",NpgsqlDbType.Jsonb,JsonSerializer.Serialize(history,CaptureJson));cmd.Parameters.AddWithValue("org",scope.OrganizationId);cmd.Parameters.AddWithValue("id",id);await cmd.ExecuteNonQueryAsync(token);
        await Audit(c,tx,scope,id,"CondominiumUpdated",token);await tx.CommitAsync(token);return new(new CondominiumProfile(id,input,version+1,history));
    }
    static async Task<List<ProfileRevision>> ProfileHistory(NpgsqlConnection c,NpgsqlTransaction tx,AccessScope scope,Guid id,string table,long version,IReadOnlyList<ProfileRevision>? history,object oldData,object newData,CancellationToken token)
    {
        var revisions=history?.ToList()??[];
        if(revisions.Count==0){
            // Internal fixed table names only; never supplied by a request.
            if(table is not("owners" or "condominiums"))throw new ArgumentException(nameof(table));
            await using var cmd=new NpgsqlCommand($"SELECT created_by,created_at FROM real_estate.{table} WHERE organization_id=@org AND id=@id",c,tx);cmd.Parameters.AddWithValue("org",scope.OrganizationId);cmd.Parameters.AddWithValue("id",id);await using var r=await cmd.ExecuteReaderAsync(token);await r.ReadAsync(token);revisions.Add(new(version,r.GetString(0),new DateTimeOffset(r.GetDateTime(1)),JsonSerializer.Serialize(oldData,CaptureJson)));
        }
        revisions.Add(new(version+1,scope.Subject,DateTimeOffset.UtcNow,JsonSerializer.Serialize(newData,CaptureJson)));return revisions;
    }
}
