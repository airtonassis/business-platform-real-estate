using RealEstate.Domain;
namespace RealEstate.Application;
public sealed class CondominiumService(ICoreAccess access,ICondominiumStore store)
{
    public async Task<Outcome<IReadOnlyList<CondominiumProfile>>> SearchAsync(string? query,CancellationToken token=default)
    {
        var grant=await access.RequireAsync("properties","PROPERTY_READ",token);if(!grant.IsSuccess)return Outcome<IReadOnlyList<CondominiumProfile>>.Fail(grant.Code!);
        if(string.IsNullOrWhiteSpace(query)||query.Length>200)return new(Array.Empty<CondominiumProfile>());
        return new(await store.SearchCondominiumsAsync(grant.Value!,query.Trim(),token));
    }
    public async Task<Outcome<CondominiumProfile>> CreateAsync(CondominiumInput input,CancellationToken token=default)
    {
        var grant=await access.RequireAsync("properties","PROPERTY_CREATE",token);if(!grant.IsSuccess)return Outcome<CondominiumProfile>.Fail(grant.Code!);
        if(input?.Characteristics?.Count>0)return Outcome<CondominiumProfile>.Fail("Access.Forbidden");
        if(input is null||string.IsNullOrWhiteSpace(input.Name)||input.Name.Length>200||input.Kind is not("residential" or "commercial" or "mixed" or "other")||input.Towers<0||input.Floors<0||input.Towers>100000||input.Floors>100000||input.Notes?.Length>2000)return Outcome<CondominiumProfile>.Fail("Condominium.Invalid");
        return await store.CreateCondominiumAsync(grant.Value!,input with{Name=input.Name.Trim()},token);
    }
    public async Task<Outcome<CondominiumProfile>> FindAsync(Guid id,bool edit=false,CancellationToken token=default)
    {
        var grant=await access.RequireAsync(edit?"condominiums":"properties",edit?"CONDOMINIUM_UPDATE":"PROPERTY_READ",token);if(!grant.IsSuccess)return Outcome<CondominiumProfile>.Fail(grant.Code!);
        var item=await store.FindCondominiumAsync(grant.Value!,id,token);return item is null?Outcome<CondominiumProfile>.Fail("Resource.NotFound"):new(item);
    }
    public async Task<Outcome<CondominiumProfile>> UpdateAsync(Guid id,long version,CondominiumInput input,CancellationToken token=default)
    {
        var grant=await access.RequireAsync("condominiums","CONDOMINIUM_UPDATE",token);if(!grant.IsSuccess)return Outcome<CondominiumProfile>.Fail(grant.Code!);
        if(version<1)return Outcome<CondominiumProfile>.Fail("Profile.VersionRequired");
        if(input is null||string.IsNullOrWhiteSpace(input.Name)||input.Name.Length>200||input.Kind is not("residential" or "commercial" or "mixed" or "other")||input.Towers<0||input.Floors<0||input.Towers>100000||input.Floors>100000||input.Notes?.Length>2000)return Outcome<CondominiumProfile>.Fail("Condominium.Invalid");
        var current=await store.FindCondominiumAsync(grant.Value!,id,token);if(current is null)return Outcome<CondominiumProfile>.Fail("Resource.NotFound");
        var features=(input.Characteristics??current.Data.Characteristics)?.ToList()??[];
        if(features.Any(s=>s is null||s.GroupId is null||!s.GroupId.StartsWith("collective-",StringComparison.Ordinal)||s.PhotoId.HasValue||s.Quantity.HasValue||s.Inclusion is not null||s.Condition is not null||s.SharedOverride.HasValue))return Outcome<CondominiumProfile>.Fail("Capture.ChoiceInvalid");
        var valid=CaptureRules.Validate(new(){Selections=features});if(!valid.IsSuccess)return Outcome<CondominiumProfile>.Fail(valid.Code!);
        return await store.UpdateCondominiumAsync(grant.Value!,id,version,input with{Name=input.Name.Trim(),Characteristics=valid.Value!.Selections},token);
    }
}
