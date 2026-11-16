using RealEstate.Domain;
namespace RealEstate.Application;
public sealed class PropertyListingService(ICoreAccess access,IPropertyListingStore store)
{
    public async Task<Outcome<PropertyListPage>> ListAsync(PropertyListQuery query,CancellationToken token=default)
    {
        var grant=await access.RequireAsync("properties","PROPERTY_READ",token);
        if(!grant.IsSuccess)return Outcome<PropertyListPage>.Fail(grant.Code!);
        if(!query.IsValid)return Outcome<PropertyListPage>.Fail("Listing.InvalidQuery");
        if(query.EstablishmentId is {} unit&&grant.Value!.Establishments?.Any(e=>e.Id==unit)!=true)return Outcome<PropertyListPage>.Fail("Access.Forbidden");
        return new(await store.ListPageAsync(grant.Value!,query,token));
    }
}
