using RealEstate.Domain;
namespace RealEstate.Application;

public sealed class CommercialPolicyService(ICoreAccess access,ICommercialPolicyStore policies,IPropertyStoreV2 properties)
{
    public async Task<Outcome<CommercialPolicyRevision>> ForApplicationAsync(Guid id,CancellationToken token=default)
    {
        var grant=await access.RequireAsync("commercial-policies","POLICY_APPLY",token);if(!grant.IsSuccess)return Outcome<CommercialPolicyRevision>.Fail(grant.Code!);
        var item=await policies.FindAsync(grant.Value!,id,token);return item?.Status=="Approved"?new(item):Outcome<CommercialPolicyRevision>.Fail("Policy.Unavailable");
    }
    public async Task<Outcome<IReadOnlyList<CommercialPolicyRevision>>> ListAsync(CancellationToken token=default)
    {var grant=await access.RequireAsync("commercial-policies","POLICY_READ",token);return grant.IsSuccess?new(await policies.ListAsync(grant.Value!,token)):Outcome<IReadOnlyList<CommercialPolicyRevision>>.Fail(grant.Code!);}
    public async Task<Outcome<CommercialPolicyRevision>> SaveDraftAsync(CommercialPolicyInput input,Guid? id=null,long version=0,CancellationToken token=default)
    {
        var grant=await access.RequireAsync("commercial-policies","POLICY_EDIT",token);if(!grant.IsSuccess)return Outcome<CommercialPolicyRevision>.Fail(grant.Code!);
        var valid=CommercialPolicyRules.Validate(input);if(!valid.IsSuccess)return Outcome<CommercialPolicyRevision>.Fail(valid.Code!);
        if(input.PropertyId is {} property&&await properties.FindV2Async(grant.Value!,property,token) is null)return Outcome<CommercialPolicyRevision>.Fail("Resource.NotFound");
        return await policies.SaveDraftAsync(grant.Value!,valid.Value!,id,version,token);
    }
    public async Task<Outcome<CommercialPolicyRevision>> ApproveAsync(Guid id,long version,string reason,CancellationToken token=default)
    {
        var grant=await access.RequireAsync("commercial-policies","POLICY_APPROVE",token);if(!grant.IsSuccess)return Outcome<CommercialPolicyRevision>.Fail(grant.Code!);
        if(!grant.Value!.LegacyManager)return Outcome<CommercialPolicyRevision>.Fail("Access.Forbidden");
        if(string.IsNullOrWhiteSpace(reason)||reason.Length>1000)return Outcome<CommercialPolicyRevision>.Fail("Policy.ReasonRequired");
        var current=await policies.FindAsync(grant.Value!,id,token);if(current is null)return Outcome<CommercialPolicyRevision>.Fail("Resource.NotFound");
        if(current.Input.PropertyId is {} property&&await properties.FindV2Async(grant.Value!,property,token) is null)return Outcome<CommercialPolicyRevision>.Fail("Resource.NotFound");
        return await policies.ApproveAsync(grant.Value!,id,version,reason.Trim(),token);
    }
}
