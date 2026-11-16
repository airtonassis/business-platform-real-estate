using RealEstate.Domain;
namespace RealEstate.Application;

public sealed class CommercialInteractionService(ICoreAccess access,ICommercialInteractionStore store,IPropertyListingStore listings)
{
    private async Task<Outcome<AccessScope>> Scope(string resource,string permission,CancellationToken token)
    {
        var grant=await access.RequireAsync(resource,permission,token);if(!grant.IsSuccess)return grant;
        var s=grant.Value!;
        if(s.OrganizationId==Guid.Empty||string.IsNullOrWhiteSpace(s.Subject)||s.ActiveEstablishmentId is not {} unit||s.Establishments?.Any(e=>e.Id==unit)!=true)return Outcome<AccessScope>.Fail("Context.ActiveEstablishmentRequired");
        // Reads of properties and customer data require both existing rights, in the same context.
        if(resource!="properties"||permission!="PROPERTY_READ"){
            var read=await access.RequireAsync("properties","PROPERTY_READ",token);if(!read.IsSuccess)return Outcome<AccessScope>.Fail(read.Code!);
            if(read.Value!.OrganizationId!=s.OrganizationId||read.Value.ActiveEstablishmentId!=s.ActiveEstablishmentId)return Outcome<AccessScope>.Fail("Context.Invalid");
        }
        return grant;
    }
    private async Task<Outcome<AccessScope>> CustomerScope(Guid customer,string permission,CancellationToken token)
    {
        var scope=await Scope("customers",permission,token);if(!scope.IsSuccess)return scope;
        if(permission!="CUSTOMER_READ"){
            var read=await access.RequireAsync("customers","CUSTOMER_READ",token);if(!read.IsSuccess)return Outcome<AccessScope>.Fail(read.Code!);
            if(read.Value!.OrganizationId!=scope.Value!.OrganizationId||read.Value.ActiveEstablishmentId!=scope.Value.ActiveEstablishmentId)return Outcome<AccessScope>.Fail("Context.Invalid");
        }
        return await store.CustomerExistsAsync(scope.Value!,customer,token)?scope:Outcome<AccessScope>.Fail("Resource.NotFound");
    }
    public async Task<Outcome<PropertyListPage>> SearchAsync(Guid customer,PropertyListQuery query,CancellationToken token=default)
    {
        var scope=await CustomerScope(customer,"CUSTOMER_READ",token);if(!scope.IsSuccess)return Outcome<PropertyListPage>.Fail(scope.Code!);
        if(query is null||!query.IsValid||query.EstablishmentId is {} unit&&unit!=scope.Value!.ActiveEstablishmentId)return Outcome<PropertyListPage>.Fail("Listing.InvalidQuery");
        return new(await listings.ListPageAsync(scope.Value!,query with{Commercial=true,EstablishmentId=scope.Value!.ActiveEstablishmentId},token));
    }
    public async Task<Outcome<IReadOnlyList<CommercialInteraction>>> CustomerHistoryAsync(Guid customer,CancellationToken token=default)
    {
        var scope=await CustomerScope(customer,"CUSTOMER_READ",token);if(!scope.IsSuccess)return Outcome<IReadOnlyList<CommercialInteraction>>.Fail(scope.Code!);
        return new(await store.HistoryAsync(scope.Value!,customer,null,token));
    }
    public async Task<Outcome<IReadOnlyList<CommercialInteraction>>> PropertyHistoryAsync(Guid property,CancellationToken token=default)
    {
        var scope=await Scope("properties","PROPERTY_COMMERCIAL_HISTORY_READ",token);if(!scope.IsSuccess)return Outcome<IReadOnlyList<CommercialInteraction>>.Fail(scope.Code!);
        // This permission explicitly includes commercial customer identification; PROPERTY_READ alone does not.
        if(!await store.PropertyExistsAsync(scope.Value!,property,token))return Outcome<IReadOnlyList<CommercialInteraction>>.Fail("Resource.NotFound");
        return new(await store.HistoryAsync(scope.Value!,null,property,token));
    }
    public async Task<Outcome<IReadOnlyList<CommercialInteraction>>> PresentAsync(Guid customer,PresentationInput input,CancellationToken token=default)
    {
        var scope=await CustomerScope(customer,"CUSTOMER_PROPERTY_WRITE",token);if(!scope.IsSuccess)return Outcome<IReadOnlyList<CommercialInteraction>>.Fail(scope.Code!);
        var valid=CommercialInteractionRules.Validate(input);return !valid.IsSuccess?Outcome<IReadOnlyList<CommercialInteraction>>.Fail(valid.Code!):await store.PresentAsync(scope.Value!,customer,valid.Value!,token);
    }
    public async Task<Outcome<CommercialInteraction>> FeedbackAsync(Guid customer,FeedbackInput input,CancellationToken token=default)
    {
        var scope=await CustomerScope(customer,"CUSTOMER_PROPERTY_WRITE",token);if(!scope.IsSuccess)return Outcome<CommercialInteraction>.Fail(scope.Code!);
        var valid=CommercialInteractionRules.Validate(input);return !valid.IsSuccess?Outcome<CommercialInteraction>.Fail(valid.Code!):await store.FeedbackAsync(scope.Value!,customer,valid.Value!,token);
    }
    public async Task<Outcome<CommercialSummary>> SummaryAsync(Guid property,DateTimeOffset from,DateTimeOffset until,CancellationToken token=default)
    {
        var scope=await Scope("properties","PROPERTY_COMMERCIAL_SUMMARY_READ",token);if(!scope.IsSuccess)return Outcome<CommercialSummary>.Fail(scope.Code!);
        if(from>=until||until-from>TimeSpan.FromDays(366))return Outcome<CommercialSummary>.Fail("Commercial.InvalidPeriod");
        if(!await store.PropertyExistsAsync(scope.Value!,property,token))return Outcome<CommercialSummary>.Fail("Resource.NotFound");
        if(!scope.Value!.LegacyManager&&!await store.IsCaptureResponsibleAsync(scope.Value,property,token))return Outcome<CommercialSummary>.Fail("Access.Forbidden");
        return await store.SummaryAsync(scope.Value!,property,from.ToUniversalTime(),until.ToUniversalTime(),token);
    }
}
