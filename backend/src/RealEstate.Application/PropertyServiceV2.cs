using System.Text.Json;
using System.Text.Json.Nodes;
using RealEstate.Domain;
namespace RealEstate.Application;

public sealed class PropertyServiceV2(ICoreAccess access,IPropertyStoreV2 store,ICaptureDirectory? directory=null)
{
    public async Task<Outcome<WorkspaceV2>> ListAsync(CancellationToken token=default)
    {
        var grant=await access.RequireAsync("properties","PROPERTY_READ",token);
        return grant.IsSuccess?new(await store.ListV2Async(grant.Value!,token)):Outcome<WorkspaceV2>.Fail(grant.Code!);
    }
    public async Task<Outcome<PropertyV2>> FindAsync(Guid id,CancellationToken token=default)
    {
        var grant=await access.RequireAsync("properties","PROPERTY_READ",token);
        if(!grant.IsSuccess)return Outcome<PropertyV2>.Fail(grant.Code!);
        var item=await store.FindV2Async(grant.Value!,id,token);
        return item is null?Outcome<PropertyV2>.Fail("Resource.NotFound"):new(item);
    }
    public async Task<Outcome<PropertyV2>> SubmitForReviewAsync(Guid id,long version,CancellationToken token=default)
    {
        var grant=await access.RequireAsync("properties","PROPERTY_UPDATE",token);
        if(!grant.IsSuccess)return Outcome<PropertyV2>.Fail(grant.Code!);
        var scope=grant.Value!;
        if(string.IsNullOrWhiteSpace(scope.Subject))return Outcome<PropertyV2>.Fail("Authentication.Required");
        if(scope.OrganizationId==Guid.Empty)return Outcome<PropertyV2>.Fail("Context.Invalid");
        if(version<1)return Outcome<PropertyV2>.Fail("Property.VersionRequired");
        var item=await store.FindV2Async(scope,id,token);
        if(item is null)return Outcome<PropertyV2>.Fail("Resource.NotFound");
        if(item.Version!=version)return Outcome<PropertyV2>.Fail("Property.VersionConflict");
        if(item.Status!=PropertyLifecycle.Draft)return Outcome<PropertyV2>.Fail("Property.StatusConflict");
        if(scope.ActiveEstablishmentId!=item.Data.EstablishmentId||scope.Establishments?.Any(e=>e.Id==item.Data.EstablishmentId)!=true)
            return Outcome<PropertyV2>.Fail("Access.Forbidden");
        var data=item.Data;
        if(data.OwnerId is null||string.IsNullOrWhiteSpace(data.Code)||string.IsNullOrWhiteSpace(data.Title)||
           string.IsNullOrWhiteSpace(data.Category)||string.IsNullOrWhiteSpace(data.Type)||string.IsNullOrWhiteSpace(data.Purpose)||
           data.Price is null||!StructuredAddressRules.LegacyCompatible(data.Address?.LegacyText,data.Address?.PostalCode,data.Address?.Latitude,data.Address?.Longitude))
            return Outcome<PropertyV2>.Fail("Property.ReviewIncomplete");
        return await store.SubmitForReviewAsync(scope,id,version,token);
    }
    public async Task<Outcome<PropertyV2>> ApproveAsync(Guid id,long version,CancellationToken token=default)
    {
        var grant=await access.RequireAsync("properties","PROPERTY_APPROVE",token);
        if(!grant.IsSuccess)return Outcome<PropertyV2>.Fail(grant.Code!);
        var scope=grant.Value!;
        if(string.IsNullOrWhiteSpace(scope.Subject))return Outcome<PropertyV2>.Fail("Authentication.Required");
        if(scope.OrganizationId==Guid.Empty)return Outcome<PropertyV2>.Fail("Context.Invalid");
        if(version<1)return Outcome<PropertyV2>.Fail("Property.VersionRequired");
        var item=await store.FindV2Async(scope,id,token);
        if(item is null)return Outcome<PropertyV2>.Fail("Resource.NotFound");
        if(item.Version!=version)return Outcome<PropertyV2>.Fail("Property.VersionConflict");
        if(item.Status!=PropertyLifecycle.InReview)return Outcome<PropertyV2>.Fail("Property.StatusConflict");
        if(scope.ActiveEstablishmentId!=item.Data.EstablishmentId||scope.Establishments?.Any(e=>e.Id==item.Data.EstablishmentId)!=true)
            return Outcome<PropertyV2>.Fail("Access.Forbidden");
        return await store.ApproveAsync(scope,id,version,token);
    }
    public async Task<Outcome<PropertyV2>> SaveAsync(PropertyInputV2 input,Guid? id=null,long version=0,CancellationToken token=default,PropertyCopySource? copy=null)
    {
        var grant=await access.RequireAsync("properties",id.HasValue?"PROPERTY_UPDATE":"PROPERTY_CREATE",token);
        if(!grant.IsSuccess)return Outcome<PropertyV2>.Fail(grant.Code!);
        if(input is null)return Outcome<PropertyV2>.Fail("Property.InvalidInput");
        var scope=grant.Value!;
        if(string.IsNullOrWhiteSpace(scope.Subject))return Outcome<PropertyV2>.Fail("Authentication.Required");
        if(scope.OrganizationId==Guid.Empty)return Outcome<PropertyV2>.Fail("Context.Invalid");
        if(copy is not null){
            if(id.HasValue)return Outcome<PropertyV2>.Fail("Property.InvalidInput");
            var read=await access.RequireAsync("properties","PROPERTY_READ",token);
            if(!read.IsSuccess)return Outcome<PropertyV2>.Fail(read.Code!);
            if(read.Value!.OrganizationId!=scope.OrganizationId||read.Value.Subject!=scope.Subject)return Outcome<PropertyV2>.Fail("Access.Forbidden");
            if(await store.FindV2Async(read.Value,copy.Id,token) is null)return Outcome<PropertyV2>.Fail("Resource.NotFound");
            input=input with{Code=null};
        }
        Guid? unit;string? previousResponsible=null;PolicyBinding? previousPolicy=null;
        if(id is {} existingId)
        {
            var existing=await store.FindV2Async(scope,existingId,token);
            if(existing is null)return Outcome<PropertyV2>.Fail("Resource.NotFound");
            if(existing.Status!=PropertyLifecycle.Draft)return Outcome<PropertyV2>.Fail("Property.StatusConflict");
            if(version<1)return Outcome<PropertyV2>.Fail("Property.VersionRequired");
            if(existing.Version!=version)return Outcome<PropertyV2>.Fail("Property.VersionConflict");
            if(existing.Data.Purpose is not null&&input.Purpose?.Trim()!=existing.Data.Purpose)return Outcome<PropertyV2>.Fail("Announcement.PurposeImmutable");
            unit=existing.Data.EstablishmentId;
            previousResponsible=existing.Data.Capture?.Acquisition.ResponsibleSubject;
            previousPolicy=existing.Data.Capture?.Policy;
        }
        else
        {
            unit=scope.ActiveEstablishmentId;
            if(unit is null)return Outcome<PropertyV2>.Fail("Context.ActiveEstablishmentRequired");
        }
        if(unit is null || scope.Establishments?.Any(e=>e.Id==unit)!=true || input.EstablishmentId is {} supplied && supplied!=unit)
            return Outcome<PropertyV2>.Fail("Access.Forbidden");
        var valid=Validate(input with{EstablishmentId=unit});
        if(!valid.IsSuccess)return Outcome<PropertyV2>.Fail(valid.Code!);
        if(input.Capture?.Policy is {} policy&&policy!=previousPolicy){
            var approval=await access.RequireAsync("commercial-policies","POLICY_APPLY",token);
            if(!approval.IsSuccess)return Outcome<PropertyV2>.Fail(approval.Code!);
            if(approval.Value!.OrganizationId!=scope.OrganizationId)return Outcome<PropertyV2>.Fail("Access.Forbidden");
        }
        var effectiveResponsible=input.Capture is null?previousResponsible:input.Capture.Acquisition.ResponsibleSubject;
        if(effectiveResponsible is {} responsible){
            if(directory is null)return Outcome<PropertyV2>.Fail("Access.Forbidden");
            var candidates=await directory.ReadAsync(scope,unit.Value,responsible,token);
            if(!candidates.IsSuccess)return Outcome<PropertyV2>.Fail(candidates.Code!);
            if(candidates.Value!.OrganizationId!=scope.OrganizationId||candidates.Value.EstablishmentId!=unit)return Outcome<PropertyV2>.Fail("Access.Forbidden");
            // Revalidate every save; an unchanged historical link remains preservable, never a new designation.
            if(responsible!=previousResponsible&&!candidates.Value.Items.Any(p=>p.Subject==responsible))return Outcome<PropertyV2>.Fail("Access.Forbidden");
        }
        if(id.HasValue && version<1)return Outcome<PropertyV2>.Fail("Property.VersionRequired");
        return copy is null?await store.SaveV2Async(grant.Value!,valid.Value!,id,version,token):await store.CopyV2Async(grant.Value!,valid.Value!,copy,token);
    }
    // PATCH preserves omitted values. Explicit null clears mutable values; legacy text retains its preservation rule.
    public async Task<Outcome<PropertyV2>> PatchAsync(Guid id,long version,JsonElement changes,CancellationToken token=default)
    {
        var grant=await access.RequireAsync("properties","PROPERTY_UPDATE",token);
        if(!grant.IsSuccess)return Outcome<PropertyV2>.Fail(grant.Code!);
        if(version<1)return Outcome<PropertyV2>.Fail("Property.VersionRequired");
        var current=await store.FindV2Async(grant.Value!,id,token);
        if(current is null)return Outcome<PropertyV2>.Fail("Resource.NotFound");
        if(current.Status!=PropertyLifecycle.Draft)return Outcome<PropertyV2>.Fail("Property.StatusConflict");
        if(current.Version!=version)return Outcome<PropertyV2>.Fail("Property.VersionConflict");
        var options=new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var node=JsonSerializer.SerializeToNode(current.Data,options)!.AsObject();
        static bool Merge(JsonObject target,JsonElement patch,IReadOnlyCollection<string> allowed)
        {
            if(patch.ValueKind!=JsonValueKind.Object)return false;
            var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var field in patch.EnumerateObject())
            {
                var key=allowed.FirstOrDefault(k=>string.Equals(k,field.Name,StringComparison.OrdinalIgnoreCase));
                if(key is null || !seen.Add(key))return false;
                if((key=="address"||key=="capture"||target[key] is JsonObject) && field.Value.ValueKind==JsonValueKind.Object)
                {
                    var address=target[key] as JsonObject??(key=="capture"
                        ?JsonSerializer.SerializeToNode(new CaptureData(),new JsonSerializerOptions(JsonSerializerDefaults.Web))!
                        :JsonSerializer.SerializeToNode(new StructuredAddress(),new JsonSerializerOptions(JsonSerializerDefaults.Web))!).AsObject();
                    if(!Merge(address,field.Value,address.Select(x=>x.Key).ToArray()))return false;
                    target[key]=address;
                }
                else target[key]=JsonNode.Parse(field.Value.GetRawText());
            }
            return true;
        }
        if(!Merge(node,changes,["code","title","ownerId","category","type","purpose","price","address","summary","internalReference","isInCondominium","capture"]))return Outcome<PropertyV2>.Fail("Property.InvalidInput");
        try {return await SaveAsync(node.Deserialize<PropertyInputV2>(options)!,id,version,token);}
        catch(JsonException){return Outcome<PropertyV2>.Fail("Property.InvalidInput");}
    }
    public static Outcome<PropertyInputV2> Validate(PropertyInputV2 p)
    {
        static string? Clean(string? value)=>string.IsNullOrWhiteSpace(value)?null:value.Trim();
        p=p with{Code=Clean(p.Code),Title=Clean(p.Title),Category=Clean(p.Category),Type=Clean(p.Type),Purpose=Clean(p.Purpose),Summary=Clean(p.Summary),InternalReference=Clean(p.InternalReference)};
        if(p.Title?.Length>200)return Outcome<PropertyInputV2>.Fail("Property.InvalidInput");
        if(p.OwnerId==Guid.Empty)return Outcome<PropertyInputV2>.Fail("Property.OwnerRequired");
        if(p.Price is {} price && (price<=0 || price>999999999999.99m || decimal.Round(price,2)!=price))return Outcome<PropertyInputV2>.Fail("Property.PriceInvalid");
        if(p.Type is not null && !PropertyRules.Types.Contains(p.Type))return Outcome<PropertyInputV2>.Fail("Property.TypeInvalid");
        if(p.Purpose is not null && !IdentificationRules.Purposes.Contains(p.Purpose))return Outcome<PropertyInputV2>.Fail("Property.PurposeInvalid");
        if(p.Code?.Length>50 || p.Category?.Length>100 || p.Summary?.Length>2000 || p.InternalReference?.Length>100)return Outcome<PropertyInputV2>.Fail("Property.InvalidInput");
        if(p.Capture is not null){var capture=CaptureRules.Validate(p.Capture);if(!capture.IsSuccess)return Outcome<PropertyInputV2>.Fail(capture.Code!);p=p with{Capture=capture.Value};}
        var address=StructuredAddressRules.Validate(p.Address);
        if(!address.IsSuccess)return Outcome<PropertyInputV2>.Fail(address.Code!);
        return new(p with{Address=address.Value});
    }
}
