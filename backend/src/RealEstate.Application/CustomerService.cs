using RealEstate.Domain;
namespace RealEstate.Application;

public sealed class CustomerService(ICoreAccess access,ICustomerStore store,ICustomerDirectory directory)
{
    private async Task<Outcome<AccessScope>> Scope(string permission,CancellationToken token)
    {
        var result=await access.RequireAsync("customers",permission,token);
        if(!result.IsSuccess)return result;
        var s=result.Value!;
        if(s.OrganizationId==Guid.Empty||string.IsNullOrWhiteSpace(s.Subject))return Outcome<AccessScope>.Fail("Context.Invalid");
        return s.ActiveEstablishmentId is {} unit&&s.Establishments?.Any(e=>e.Id==unit)==true?result:Outcome<AccessScope>.Fail("Context.ActiveEstablishmentRequired");
    }
    public async Task<Outcome<CustomerPage>> SearchAsync(string? query=null,int page=1,CancellationToken token=default)
    {
        var scope=await Scope("CUSTOMER_READ",token);if(!scope.IsSuccess)return Outcome<CustomerPage>.Fail(scope.Code!);
        if(query?.Length>200||page<1||page>100000)return Outcome<CustomerPage>.Fail("Customer.QueryInvalid");
        return new(await store.SearchAsync(scope.Value!,query?.Trim()??"",page,token));
    }
    public async Task<Outcome<CustomerFile>> FindAsync(Guid id,CancellationToken token=default)
    {
        var scope=await Scope("CUSTOMER_READ",token);if(!scope.IsSuccess)return Outcome<CustomerFile>.Fail(scope.Code!);
        var file=await store.FindAsync(scope.Value!,id,token);return file is null?Outcome<CustomerFile>.Fail("Resource.NotFound"):new(file);
    }
    public async Task<Outcome<CaptureResponsibleDirectory>> CandidatesAsync(bool editing=false,CancellationToken token=default)
    {
        var scope=await Scope(editing?"CUSTOMER_UPDATE":"CUSTOMER_CREATE",token);if(!scope.IsSuccess)return Outcome<CaptureResponsibleDirectory>.Fail(scope.Code!);
        return await directory.ReadAsync(scope.Value!,scope.Value!.ActiveEstablishmentId!.Value,token:token);
    }
    public async Task<Outcome<Customer>> SaveAsync(CustomerInput input,Guid? id=null,long version=0,bool acknowledgeDuplicates=false,CancellationToken token=default)
    {
        var scope=await Scope(id.HasValue?"CUSTOMER_UPDATE":"CUSTOMER_CREATE",token);if(!scope.IsSuccess)return Outcome<Customer>.Fail(scope.Code!);
        var valid=CustomerRules.Validate(input);if(!valid.IsSuccess)return Outcome<Customer>.Fail(valid.Code!);
        var s=scope.Value!;
        if(id.HasValue){var file=await store.FindAsync(s,id.Value,token);if(file is null)return Outcome<Customer>.Fail("Resource.NotFound");if(version<1||file.Customer.Version!=version)return Outcome<Customer>.Fail("Customer.VersionConflict");}
        var candidates=await directory.ReadAsync(s,s.ActiveEstablishmentId!.Value,valid.Value!.ResponsibleSubject,token);
        if(!candidates.IsSuccess)return Outcome<Customer>.Fail(candidates.Code!);
        if(candidates.Value!.OrganizationId!=s.OrganizationId||candidates.Value.EstablishmentId!=s.ActiveEstablishmentId||!candidates.Value.Items.Any(p=>p.Subject==valid.Value.ResponsibleSubject))return Outcome<Customer>.Fail("Customer.ResponsibleUnavailable");
        var selected=candidates.Value.Items.First(p=>p.Subject==valid.Value.ResponsibleSubject);
        return await store.SaveAsync(s,valid.Value with{ResponsibleName=selected.DisplayName},id,version,acknowledgeDuplicates,token);
    }
    public async Task<Outcome<CustomerContact>> AddContactAsync(Guid id,ContactInput input,CancellationToken token=default)
    {
        var scope=await Scope("CUSTOMER_CONTACT_CREATE",token);if(!scope.IsSuccess)return Outcome<CustomerContact>.Fail(scope.Code!);
        if(input is null||!CustomerRules.ContactKinds.Contains(input.Kind)||CustomerRules.Clean(input.Description) is not {} description||description.Length>4000)return Outcome<CustomerContact>.Fail("Customer.ContactInvalid");
        return await store.AddContactAsync(scope.Value!,id,input with{Description=description},token);
    }
}
