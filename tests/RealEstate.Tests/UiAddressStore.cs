using Microsoft.AspNetCore.Http;
using RealEstate.Domain;
namespace RealEstate.Tests;

// TEST ONLY. One store shared by v1/v2, never used for database certification.
internal sealed class UiAddressStore(IHttpContextAccessor accessor):IPropertyStore,IPropertyStoreV2,IPropertyListingStore
{
    private readonly List<Owner> owners=[new(UiFixture.OwnerId,"Synthetic owner")];
    private readonly List<PropertyV2> records=Enumerable.Range(1,3).Select(i=>new PropertyV2(i==1?UiFixture.PropertyId:Guid.NewGuid(),FixtureProgram.OrganizationA,
        new($"UI-{i:000}","Synthetic property with a long title for responsive verification "+i,UiFixture.OwnerId,i==3?"Rural":"Residencial","Casa","Venda",3000,
            new(LegacyText:"Synthetic rural road, S/N",PostalCode:"01310100",Latitude:-23,Longitude:-46),FixtureProgram.OrganizationA),1,"Draft")).ToList();
    private static Property Legacy(PropertyV2 p)=>new(p.Id,p.OrganizationId,new(p.Data.Code!,p.Data.Title!,p.Data.OwnerId??Guid.Empty,p.Data.Category!,p.Data.Type!,p.Data.Purpose!,p.Data.Address!.LegacyText!,p.Data.Address.PostalCode!,p.Data.Address.Latitude,p.Data.Address.Longitude,p.Data.Price??0,p.Data.EstablishmentId),p.Version,p.Status);
    public Task<WorkspaceV2> ListV2Async(AccessScope scope,CancellationToken token)
    {
        var mode=accessor.HttpContext!.Request.Headers["X-Ui-Mode"].ToString();
        lock(records)return Task.FromResult(new WorkspaceV2(mode=="empty"?[]:records.ToArray(),mode=="no-owners"?[]:owners.ToArray(),scope.Establishments));
    }
    public async Task<Workspace> ListAsync(AccessScope scope,CancellationToken token)
    {
        var ws=await ListV2Async(scope,token);
        return new(ws.Properties.Where(p=>StructuredAddressRules.LegacyCompatible(p.Data.Address?.LegacyText,p.Data.Address?.PostalCode,p.Data.Address?.Latitude,p.Data.Address?.Longitude)).Select(Legacy).Take(100).ToArray(),ws.Owners,ws.Establishments);
    }
    public async Task<PropertyListPage> ListPageAsync(AccessScope scope,PropertyListQuery query,CancellationToken token)
    {
        var ws=await ListV2Async(scope,token);IEnumerable<PropertyV2> rows=ws.Properties;
        static string Text(PropertyV2 p,string key)=>key switch{
            "code"=>p.Data.Code??"","title"=>p.Data.Title??"","municipality"=>p.Data.Address?.Municipality??"","state"=>p.Data.Address?.State??"",
            "neighborhood"=>p.Data.Address?.Neighborhood??"","category"=>p.Data.Category??"","type"=>p.Data.Type??"","purpose"=>p.Data.Purpose??"","status"=>p.Status,_=>p.Id.ToString()};
        foreach(var (key,value) in query.TextFilters())rows=rows.Where(p=>Text(p,key).Contains(value,StringComparison.OrdinalIgnoreCase));
        if(!string.IsNullOrWhiteSpace(query.Search))rows=rows.Where(p=>new[]{"code","title","municipality","state","neighborhood","category","type","purpose","status"}.Any(k=>Text(p,k).Contains(query.Search.Trim(),StringComparison.OrdinalIgnoreCase)));
        if(query.MinPrice is {} min)rows=rows.Where(p=>p.Data.Price>=min);
        if(query.MaxPrice is {} max)rows=rows.Where(p=>p.Data.Price<=max);
        if(query.EstablishmentId is {} unit)rows=rows.Where(p=>p.Data.EstablishmentId==unit);
        var count=rows.LongCount();
        if(query.Sort=="price")rows=query.Descending?rows.OrderByDescending(p=>p.Data.Price):rows.OrderBy(p=>p.Data.Price);
        else rows=query.Descending?rows.OrderByDescending(p=>Text(p,query.Sort),StringComparer.OrdinalIgnoreCase):rows.OrderBy(p=>Text(p,query.Sort),StringComparer.OrdinalIgnoreCase);
        return new(rows.Skip((query.Page-1)*PropertyListQuery.PageSize).Take(PropertyListQuery.PageSize).ToArray(),count,scope.Establishments??[]);
    }
    public Task<PropertyV2?> FindV2Async(AccessScope scope,Guid id,CancellationToken token)
    {lock(records)return Task.FromResult(records.FirstOrDefault(p=>p.Id==id));}
    public async Task<Property?> FindAsync(AccessScope scope,Guid id,CancellationToken token)
    {var p=await FindV2Async(scope,id,token);return p is null?null:Legacy(p);}
    public Task<Outcome<Owner>> CreateOwnerAsync(AccessScope scope,string name,CancellationToken token)
    {lock(records){var o=new Owner(Guid.NewGuid(),name);owners.Add(o);return Task.FromResult(new Outcome<Owner>(o));}}
    public Task<Outcome<PropertyV2>> SaveV2Async(AccessScope scope,PropertyInputV2 p,Guid? id,long version,CancellationToken token)
    {
        lock(records){
            var prior=records.FirstOrDefault(x=>x.Id==id);
            if(id.HasValue && (prior is null || prior.Version!=version))return Task.FromResult(Outcome<PropertyV2>.Fail("Property.VersionConflict"));
            var a=p.Address??new();if(a.LegacyText is null && prior?.Data.Address?.LegacyText is {} text)a=a with{LegacyText=text};
            var saved=new PropertyV2(id??Guid.NewGuid(),scope.OrganizationId,p with{Address=a},version+1,"Draft");
            if(prior is not null)records.Remove(prior);records.Add(saved);return Task.FromResult(new Outcome<PropertyV2>(saved));
        }
    }
    public async Task<Outcome<Property>> SaveAsync(AccessScope scope,PropertyInput p,Guid? id,long version,CancellationToken token)
    {
        var prior=id.HasValue?await FindV2Async(scope,id.Value,token):null;
        var a=(prior?.Data.Address??new()) with{LegacyText=p.Address,PostalCode=p.PostalCode,Latitude=p.Latitude,Longitude=p.Longitude};
        var result=await SaveV2Async(scope,new(p.Code,p.Title,p.OwnerId,p.Category,p.Type,p.Purpose,p.Price,a,p.EstablishmentId),id,version,token);
        return result.IsSuccess?new(Legacy(result.Value!)):Outcome<Property>.Fail(result.Code!);
    }
}
