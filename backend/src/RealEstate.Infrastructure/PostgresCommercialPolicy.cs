using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using RealEstate.Domain;
namespace RealEstate.Infrastructure;

public sealed partial class PostgresPropertyStore:ICommercialPolicyStore
{
    const string PolicyColumns="id,version,status,name,reason,data,property_id,base_revision_id,requested_by,approved_by,created_at,history";
    static CommercialPolicyRevision ReadPolicy(NpgsqlDataReader r)=>new(r.GetGuid(0),r.GetInt64(1),r.GetString(2),new(r.GetString(3),r.GetString(4),JsonSerializer.Deserialize<CommercialPolicyData>(r.GetString(5),CaptureJson)!,r.IsDBNull(6)?null:r.GetGuid(6),r.IsDBNull(7)?null:r.GetGuid(7)),r.GetString(8),r.IsDBNull(9)?null:r.GetString(9),new DateTimeOffset(r.GetDateTime(10)),JsonSerializer.Deserialize<List<ProfileRevision>>(r.GetString(11),CaptureJson)!);
    static async Task<CommercialPolicyRevision?> ReadPolicy(NpgsqlConnection c,NpgsqlTransaction tx,AccessScope scope,Guid id,bool locked,CancellationToken token)
    {
        await using var cmd=new NpgsqlCommand($"SELECT {PolicyColumns} FROM real_estate.commercial_policy_revisions WHERE organization_id=@org AND id=@id"+(locked?" FOR UPDATE":""),c,tx);cmd.Parameters.AddWithValue("org",scope.OrganizationId);cmd.Parameters.AddWithValue("id",id);await using var r=await cmd.ExecuteReaderAsync(token);return await r.ReadAsync(token)?ReadPolicy(r):null;
    }
    async Task<IReadOnlyList<CommercialPolicyRevision>> ICommercialPolicyStore.ListAsync(AccessScope scope,CancellationToken token)
    {
        await using var c=await dataSource.OpenConnectionAsync(token);await using var tx=await Begin(c,scope,token);
        await using var cmd=new NpgsqlCommand($"SELECT {PolicyColumns} FROM real_estate.commercial_policy_revisions p WHERE organization_id=@org AND (property_id IS NULL OR EXISTS(SELECT FROM real_estate.properties r WHERE r.organization_id=p.organization_id AND r.id=p.property_id)) ORDER BY created_at DESC,id LIMIT 100",c,tx);cmd.Parameters.AddWithValue("org",scope.OrganizationId);var items=new List<CommercialPolicyRevision>();await using(var r=await cmd.ExecuteReaderAsync(token)){while(await r.ReadAsync(token))items.Add(ReadPolicy(r));}await tx.CommitAsync(token);return items;
    }
    async Task<CommercialPolicyRevision?> ICommercialPolicyStore.FindAsync(AccessScope scope,Guid id,CancellationToken token)
    {await using var c=await dataSource.OpenConnectionAsync(token);await using var tx=await Begin(c,scope,token);var item=await ReadPolicy(c,tx,scope,id,false,token);if(item?.Input.PropertyId is {} property){await using var check=new NpgsqlCommand("SELECT 1 FROM real_estate.properties WHERE organization_id=@org AND id=@id",c,tx);check.Parameters.AddWithValue("org",scope.OrganizationId);check.Parameters.AddWithValue("id",property);if(await check.ExecuteScalarAsync(token) is null)return null;}await tx.CommitAsync(token);return item;}
    public async Task<Outcome<CommercialPolicyRevision>> SaveDraftAsync(AccessScope scope,CommercialPolicyInput input,Guid? id,long version,CancellationToken token)
    {
        await using var c=await dataSource.OpenConnectionAsync(token);await using var tx=await Begin(c,scope,token);var target=id??Guid.NewGuid();
        var previous=id.HasValue?await ReadPolicy(c,tx,scope,target,true,token):null;
        if(id.HasValue&&previous is null)return Outcome<CommercialPolicyRevision>.Fail("Resource.NotFound");
        if(previous is not null&&(version<1||previous.Version!=version))return Outcome<CommercialPolicyRevision>.Fail(version<1?"Profile.VersionRequired":"Profile.VersionConflict");
        if(previous?.Status=="Approved")return Outcome<CommercialPolicyRevision>.Fail("Policy.Immutable");
        if(previous is not null&&(previous.Input.PropertyId!=input.PropertyId||previous.Input.BaseRevisionId!=input.BaseRevisionId))return Outcome<CommercialPolicyRevision>.Fail("Policy.ScopeImmutable");
        if(input.BaseRevisionId is {} baseId){var basis=await ReadPolicy(c,tx,scope,baseId,false,token);if(basis?.Status!="Approved"||basis.Input.PropertyId is {} bound&&bound!=input.PropertyId)return Outcome<CommercialPolicyRevision>.Fail("Policy.BaseUnavailable");}
        if(input.PropertyId is {} property){await using var check=new NpgsqlCommand("SELECT 1 FROM real_estate.properties WHERE organization_id=@org AND id=@id",c,tx);check.Parameters.AddWithValue("org",scope.OrganizationId);check.Parameters.AddWithValue("id",property);if(await check.ExecuteScalarAsync(token) is null)return Outcome<CommercialPolicyRevision>.Fail("Resource.NotFound");}
        var next=(previous?.Version??0)+1;var history=(previous?.History??[]).ToList();history.Add(new(next,scope.Subject,DateTimeOffset.UtcNow,JsonSerializer.Serialize(new{Action="DraftSaved",Input=input},CaptureJson)));
        var statement=previous is null?"INSERT INTO real_estate.commercial_policy_revisions(organization_id,id,version,status,name,reason,data,property_id,base_revision_id,requested_by,history) VALUES(@org,@id,@version,'Draft',@name,@reason,@data,@property,@base,@actor,@history)":"UPDATE real_estate.commercial_policy_revisions SET version=@version,name=@name,reason=@reason,data=@data,requested_by=@actor,history=@history WHERE organization_id=@org AND id=@id";
        await using(var cmd=new NpgsqlCommand(statement,c,tx)){cmd.Parameters.AddWithValue("org",scope.OrganizationId);cmd.Parameters.AddWithValue("id",target);cmd.Parameters.AddWithValue("version",next);cmd.Parameters.AddWithValue("name",input.Name);cmd.Parameters.AddWithValue("reason",input.Reason);cmd.Parameters.AddWithValue("data",NpgsqlDbType.Jsonb,JsonSerializer.Serialize(input.Data,CaptureJson));cmd.Parameters.AddWithValue("property",NpgsqlDbType.Uuid,(object?)input.PropertyId??DBNull.Value);cmd.Parameters.AddWithValue("base",NpgsqlDbType.Uuid,(object?)input.BaseRevisionId??DBNull.Value);cmd.Parameters.AddWithValue("actor",scope.Subject);cmd.Parameters.AddWithValue("history",NpgsqlDbType.Jsonb,JsonSerializer.Serialize(history,CaptureJson));await cmd.ExecuteNonQueryAsync(token);}
        await Audit(c,tx,scope,target,"CommercialPolicyDraftSaved",token);var saved=await ReadPolicy(c,tx,scope,target,false,token);await tx.CommitAsync(token);return new(saved!);
    }
    public async Task<Outcome<CommercialPolicyRevision>> ApproveAsync(AccessScope scope,Guid id,long version,string reason,CancellationToken token)
    {
        await using var c=await dataSource.OpenConnectionAsync(token);await using var tx=await Begin(c,scope,token);
        await using(var gate=new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(@org,0))",c,tx)){gate.Parameters.AddWithValue("org","imob-policy:"+scope.OrganizationId);await gate.ExecuteNonQueryAsync(token);}
        var previous=await ReadPolicy(c,tx,scope,id,true,token);if(previous is null)return Outcome<CommercialPolicyRevision>.Fail("Resource.NotFound");
        if(version<1||previous.Version!=version)return Outcome<CommercialPolicyRevision>.Fail(version<1?"Profile.VersionRequired":"Profile.VersionConflict");
        if(previous.Status!="Draft")return Outcome<CommercialPolicyRevision>.Fail("Policy.Immutable");
        if(previous.RequestedBy==scope.Subject||previous.History.Any(h=>h.Actor==scope.Subject))return Outcome<CommercialPolicyRevision>.Fail("Policy.SelfApproval");
        if(previous.Input.PropertyId is null){
            await using var current=new NpgsqlCommand("SELECT p.id FROM real_estate.commercial_policy_revisions p WHERE p.organization_id=@org AND p.status='Approved' AND p.property_id IS NULL AND NOT EXISTS(SELECT FROM real_estate.commercial_policy_revisions n WHERE n.organization_id=p.organization_id AND n.base_revision_id=p.id AND n.status='Approved' AND n.property_id IS NULL)",c,tx);current.Parameters.AddWithValue("org",scope.OrganizationId);var active=await current.ExecuteScalarAsync(token);
            if((active is Guid activeId?(Guid?)activeId:null)!=previous.Input.BaseRevisionId)return Outcome<CommercialPolicyRevision>.Fail("Profile.VersionConflict");
        }
        if(previous.Input.PropertyId is {} property){await using var check=new NpgsqlCommand("SELECT 1 FROM real_estate.properties WHERE organization_id=@org AND id=@id",c,tx);check.Parameters.AddWithValue("org",scope.OrganizationId);check.Parameters.AddWithValue("id",property);if(await check.ExecuteScalarAsync(token) is null)return Outcome<CommercialPolicyRevision>.Fail("Resource.NotFound");}
        var history=previous.History.Append(new ProfileRevision(version+1,scope.Subject,DateTimeOffset.UtcNow,JsonSerializer.Serialize(new{Action="Approved",Reason=reason,Input=previous.Input},CaptureJson)));
        await using(var cmd=new NpgsqlCommand("UPDATE real_estate.commercial_policy_revisions SET version=version+1,status='Approved',approved_by=@actor,history=@history WHERE organization_id=@org AND id=@id",c,tx)){cmd.Parameters.AddWithValue("org",scope.OrganizationId);cmd.Parameters.AddWithValue("id",id);cmd.Parameters.AddWithValue("actor",scope.Subject);cmd.Parameters.AddWithValue("history",NpgsqlDbType.Jsonb,JsonSerializer.Serialize(history,CaptureJson));await cmd.ExecuteNonQueryAsync(token);}
        await Audit(c,tx,scope,id,"CommercialPolicyApproved",token);var result=await ReadPolicy(c,tx,scope,id,false,token);await tx.CommitAsync(token);return new(result!);
    }
    static async Task<Outcome<bool>> ValidatePolicyBinding(NpgsqlConnection c,NpgsqlTransaction tx,AccessScope scope,Guid property,CaptureData? current,CaptureData? previous,CancellationToken token)
    {
        if(current is null)return new(true); // Legacy omitted capture preserves the aggregate.
        if(previous?.Policy is not null&&current.Policy is null){
            // Existing v2 clients do not know this additive server-owned provenance.
            // Omission/null cannot detach an approval or erase its allocation template.
            current.Policy=previous.Policy;
            if(current.Fees.Allocations.Count==0)current.Fees.Allocations=previous.Fees.Allocations;
        }
        if(current.Policy is not {} binding)return new(true);
        var policy=await ReadPolicy(c,tx,scope,binding.RevisionId,false,token);
        if(policy?.Status!="Approved"||policy.Version!=binding.Version||policy.Input.PropertyId is {} bound&&bound!=property)return Outcome<bool>.Fail("Policy.Unavailable");
        if(!CommercialPolicyRules.Matches(policy.Input.Data,current))return Outcome<bool>.Fail("Policy.ExceptionRequired");
        return new(true);
    }
}
