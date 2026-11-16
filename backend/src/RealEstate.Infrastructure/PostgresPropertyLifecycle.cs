using Npgsql;
using RealEstate.Domain;
namespace RealEstate.Infrastructure;

public sealed partial class PostgresPropertyStore
{
    private static async Task AuditTransition(NpgsqlConnection c,NpgsqlTransaction tx,AccessScope scope,Guid id,string from,string to,string type,CancellationToken token)
    {
        await using var cmd=new NpgsqlCommand("INSERT INTO real_estate.events(organization_id,id,entity_id,event_type,actor,establishment_id,from_status,to_status) VALUES(@org,@event,@id,@type,@actor,@unit,@from,@to)",c,tx);
        cmd.Parameters.AddWithValue("org",scope.OrganizationId);cmd.Parameters.AddWithValue("event",Guid.NewGuid());cmd.Parameters.AddWithValue("id",id);
        cmd.Parameters.AddWithValue("type",type);cmd.Parameters.AddWithValue("actor",scope.Subject);cmd.Parameters.AddWithValue("unit",scope.ActiveEstablishmentId!.Value);
        cmd.Parameters.AddWithValue("from",from);cmd.Parameters.AddWithValue("to",to);await cmd.ExecuteNonQueryAsync(token);
    }
    public async Task<Outcome<PropertyV2>> SubmitForReviewAsync(AccessScope scope,Guid id,long version,CancellationToken token)
    {
        await using var c=await dataSource.OpenConnectionAsync(token);
        await using var tx=await Begin(c,scope,token);
        await using(var flag=new NpgsqlCommand("SELECT set_config('erp.property_transition','submit',true)",c,tx))
            await flag.ExecuteNonQueryAsync(token);
        await using var cmd=new NpgsqlCommand("UPDATE real_estate.properties SET status='InReview',version=version+1,updated_by=@actor,updated_at=clock_timestamp() WHERE organization_id=@org AND id=@id AND establishment_id=@unit AND status='Draft' AND version=@version RETURNING "+V2Columns,c,tx);
        cmd.Parameters.AddWithValue("actor",scope.Subject);
        cmd.Parameters.AddWithValue("org",scope.OrganizationId);
        cmd.Parameters.AddWithValue("id",id);
        cmd.Parameters.AddWithValue("unit",scope.ActiveEstablishmentId!.Value);
        cmd.Parameters.AddWithValue("version",version);
        PropertyV2? item;
        await using(var r=await cmd.ExecuteReaderAsync(token))item=await r.ReadAsync(token)?ReadV2(r):null;
        if(item is null)return Outcome<PropertyV2>.Fail("Property.VersionConflict");
        await AuditTransition(c,tx,scope,id,PropertyLifecycle.Draft,PropertyLifecycle.InReview,"PropertySubmittedForReview",token);
        await tx.CommitAsync(token);
        return new(item);
    }
    public async Task<Outcome<PropertyV2>> ApproveAsync(AccessScope scope,Guid id,long version,CancellationToken token)
    {
        await using var c=await dataSource.OpenConnectionAsync(token);
        await using var tx=await Begin(c,scope,token);
        await using(var flag=new NpgsqlCommand("SELECT set_config('erp.property_transition','approve',true)",c,tx))
            await flag.ExecuteNonQueryAsync(token);
        await using var cmd=new NpgsqlCommand("UPDATE real_estate.properties SET status='Available',version=version+1,updated_by=@actor,updated_at=clock_timestamp() WHERE organization_id=@org AND id=@id AND establishment_id=@unit AND status='InReview' AND version=@version RETURNING "+V2Columns,c,tx);
        cmd.Parameters.AddWithValue("actor",scope.Subject);cmd.Parameters.AddWithValue("org",scope.OrganizationId);
        cmd.Parameters.AddWithValue("id",id);cmd.Parameters.AddWithValue("unit",scope.ActiveEstablishmentId!.Value);cmd.Parameters.AddWithValue("version",version);
        PropertyV2? item;
        await using(var r=await cmd.ExecuteReaderAsync(token))item=await r.ReadAsync(token)?ReadV2(r):null;
        if(item is null)return Outcome<PropertyV2>.Fail("Property.VersionConflict");
        await AuditTransition(c,tx,scope,id,PropertyLifecycle.InReview,PropertyLifecycle.Available,"PropertyApproved",token);
        await tx.CommitAsync(token);
        return new(item);
    }
}
