using RealEstate.Domain;
namespace RealEstate.Application;
public sealed class OwnerProfileService(ICoreAccess access,IOwnerProfileStore store)
{
    public async Task<Outcome<IReadOnlyList<OwnerProfile>>> SearchAsync(string? query,CancellationToken token=default)
    {
        var grant=await access.RequireAsync("properties","PROPERTY_READ",token);
        if(!grant.IsSuccess)return Outcome<IReadOnlyList<OwnerProfile>>.Fail(grant.Code!);
        if(string.IsNullOrWhiteSpace(query)||query.Trim().Length>200)return new(Array.Empty<OwnerProfile>());
        return new(await store.SearchOwnersAsync(grant.Value!,query.Trim(),token));
    }
    public async Task<Outcome<OwnerProfile>> CreateAsync(OwnerProfileInput input,CancellationToken token=default)
    {
        var grant=await access.RequireAsync("owners","OWNER_CREATE",token);
        if(!grant.IsSuccess)return Outcome<OwnerProfile>.Fail(grant.Code!);
        var valid=OwnerProfileRules.Validate(input);if(!valid.IsSuccess)return Outcome<OwnerProfile>.Fail(valid.Code!);
        return await store.CreateOwnerProfileAsync(grant.Value!,valid.Value!,token);
    }
    public async Task<Outcome<OwnerProfile>> FindForEditAsync(Guid id,CancellationToken token=default)
    {
        var grant=await access.RequireAsync("owners","OWNER_UPDATE",token);if(!grant.IsSuccess)return Outcome<OwnerProfile>.Fail(grant.Code!);
        var item=await store.FindOwnerProfileAsync(grant.Value!,id,token);return item is null?Outcome<OwnerProfile>.Fail("Resource.NotFound"):new(item);
    }
    public async Task<Outcome<OwnerProfile>> UpdateAsync(Guid id,long version,OwnerProfileInput input,CancellationToken token=default)
    {
        var grant=await access.RequireAsync("owners","OWNER_UPDATE",token);if(!grant.IsSuccess)return Outcome<OwnerProfile>.Fail(grant.Code!);
        if(version<1)return Outcome<OwnerProfile>.Fail("Profile.VersionRequired");
        if(input is null)return Outcome<OwnerProfile>.Fail("Owner.Invalid");
        var current=await store.FindOwnerProfileAsync(grant.Value!,id,token);if(current is null)return Outcome<OwnerProfile>.Fail("Resource.NotFound");
        input=input with{Details=input.Details??current.Details};
        var valid=current.PersonKind==""&&current.TaxId==""&&input.PersonKind==""&&input.TaxId==""&&!string.IsNullOrWhiteSpace(input.Name)&&input.Name.Length<=200
            ?OwnerProfileRules.ValidateDetails(input with{Name=input.Name.Trim()}):OwnerProfileRules.Validate(input);
        if(!valid.IsSuccess)return Outcome<OwnerProfile>.Fail(valid.Code!);
        return await store.UpdateOwnerProfileAsync(grant.Value!,id,version,valid.Value!,token);
    }
}
