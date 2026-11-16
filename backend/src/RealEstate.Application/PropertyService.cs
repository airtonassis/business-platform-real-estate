using RealEstate.Domain;
namespace RealEstate.Application;

// Every use case resolves fresh Core authority, including non-HTTP callers.
public sealed class PropertyService(ICoreAccess access,IPropertyStore store)
{
    public async Task<Outcome<Workspace>> ListAsync(CancellationToken token=default)
    {
        var grant=await access.RequireAsync("properties","PROPERTY_READ",token);
        return grant.IsSuccess?new(await store.ListAsync(grant.Value!,token)):Outcome<Workspace>.Fail(grant.Code!);
    }
    public async Task<Outcome<Property>> FindAsync(Guid id,CancellationToken token=default)
    {
        var grant=await access.RequireAsync("properties","PROPERTY_READ",token);
        if(!grant.IsSuccess)return Outcome<Property>.Fail(grant.Code!);
        var item=await store.FindAsync(grant.Value!,id,token);
        if(item is null)return Outcome<Property>.Fail("Resource.NotFound");
        return PropertyRules.Validate(item.Data).IsSuccess && StructuredAddressRules.LegacyCompatible(item.Data.Address,item.Data.PostalCode,item.Data.Latitude,item.Data.Longitude)
            ?new(item):Outcome<Property>.Fail("Property.AddressVersionRequired");
    }
    public async Task<Outcome<Property>> SaveAsync(PropertyInput input,Guid? id=null,long version=0,CancellationToken token=default)
    {
        var grant=await access.RequireAsync("properties",id.HasValue?"PROPERTY_UPDATE":"PROPERTY_CREATE",token);
        if(!grant.IsSuccess)return Outcome<Property>.Fail(grant.Code!);
        if(input is null)return Outcome<Property>.Fail("Property.InvalidInput");
        var scope=grant.Value!;
        if(string.IsNullOrWhiteSpace(scope.Subject))return Outcome<Property>.Fail("Authentication.Required");
        if(scope.OrganizationId==Guid.Empty)return Outcome<Property>.Fail("Context.Invalid");
        Guid? selected;
        if(id is {} existingId)
        {
            var existing=await store.FindAsync(scope,existingId,token);
            if(existing is null)return Outcome<Property>.Fail("Resource.NotFound");
            if(existing.Status!=PropertyLifecycle.Draft)return Outcome<Property>.Fail("Property.StatusConflict");
            if(version<1)return Outcome<Property>.Fail("Property.VersionRequired");
            if(existing.Version!=version)return Outcome<Property>.Fail("Property.VersionConflict");
            if(existing.Data.Purpose is not null&&input.Purpose?.Trim()!=existing.Data.Purpose)return Outcome<Property>.Fail("Announcement.PurposeImmutable");
            selected=existing.Data.EstablishmentId;
        }
        else
        {
            if(!AnnouncementRules.Exclusive(input.Purpose?.Trim()))return Outcome<Property>.Fail("Property.PurposeInvalid");
            selected=scope.ActiveEstablishmentId;
            if(selected is null)return Outcome<Property>.Fail("Context.ActiveEstablishmentRequired");
        }
        if(selected is null || scope.Establishments?.Any(e=>e.Id==selected)!=true || input.EstablishmentId is {} supplied && supplied!=selected)return Outcome<Property>.Fail("Access.Forbidden");
        input=input with{EstablishmentId=selected};
        var valid=PropertyRules.Validate(input);
        if(!valid.IsSuccess)return Outcome<Property>.Fail(valid.Code!);
        if(id.HasValue && version<1)return Outcome<Property>.Fail("Property.VersionRequired");
        return await store.SaveAsync(grant.Value!,valid.Value!,id,version,token);
    }
    public async Task<Outcome<Owner>> CreateOwnerAsync(string name,CancellationToken token=default)
    {
        var grant=await access.RequireAsync("owners","OWNER_CREATE",token);
        if(!grant.IsSuccess)return Outcome<Owner>.Fail(grant.Code!);
        if(string.IsNullOrWhiteSpace(name)||name.Trim().Length>200)return Outcome<Owner>.Fail("Owner.NameRequired");
        return await store.CreateOwnerAsync(grant.Value!,name.Trim(),token);
    }
}

