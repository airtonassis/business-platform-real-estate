using System.Text.Json;
using NpgsqlTypes;
using Npgsql;
using RealEstate.Domain;
namespace RealEstate.Infrastructure;
public sealed partial class PostgresPropertyStore:IOwnerProfileStore
{
    public async Task<IReadOnlyList<OwnerProfile>> SearchOwnersAsync(AccessScope scope,string query,CancellationToken token)
    {
        await using var c=await dataSource.OpenConnectionAsync(token);await using var tx=await Begin(c,scope,token);
        await using var cmd=new NpgsqlCommand("SELECT id,name,person_kind,tax_id FROM real_estate.owners WHERE organization_id=@org AND active AND (position(lower(@query) in lower(name))>0 OR (tax_id IS NOT NULL AND position(@digits in tax_id)>0 AND @digits<>'')) ORDER BY name,id LIMIT 50",c,tx);
        cmd.Parameters.AddWithValue("org",scope.OrganizationId);cmd.Parameters.AddWithValue("query",query);cmd.Parameters.AddWithValue("digits",new string(query.Where(char.IsAsciiDigit).ToArray()));
        var result=new List<OwnerProfile>();await using(var r=await cmd.ExecuteReaderAsync(token)){while(await r.ReadAsync(token))result.Add(new(r.GetGuid(0),r.GetString(1),r.IsDBNull(2)?"":r.GetString(2),r.IsDBNull(3)?"":r.GetString(3)));}
        await tx.CommitAsync(token);return result;
    }
    public async Task<Outcome<OwnerProfile>> CreateOwnerProfileAsync(AccessScope scope,OwnerProfileInput input,CancellationToken token)
    {
        await using var c=await dataSource.OpenConnectionAsync(token);await using var tx=await Begin(c,scope,token);
        try{
            var id=Guid.NewGuid();await using var cmd=new NpgsqlCommand("INSERT INTO real_estate.owners(organization_id,id,name,person_kind,tax_id,created_by,details) VALUES(@org,@id,@name,@kind,@tax,@actor,@details)",c,tx);
            cmd.Parameters.AddWithValue("details",NpgsqlDbType.Jsonb,input.Details is null?DBNull.Value:JsonSerializer.Serialize(input.Details,CaptureJson));cmd.Parameters.AddWithValue("org",scope.OrganizationId);cmd.Parameters.AddWithValue("id",id);cmd.Parameters.AddWithValue("name",input.Name);cmd.Parameters.AddWithValue("kind",input.PersonKind);cmd.Parameters.AddWithValue("tax",input.TaxId);cmd.Parameters.AddWithValue("actor",scope.Subject);await cmd.ExecuteNonQueryAsync(token);
            await Audit(c,tx,scope,id,"OwnerProfileCreated",token);await tx.CommitAsync(token);return new(new OwnerProfile(id,input.Name,input.PersonKind,input.TaxId,Details:input.Details));
        }catch(PostgresException e)when(e.SqlState=="23505"){return Outcome<OwnerProfile>.Fail("Owner.DuplicateTaxId");}
    }
}
