using RealEstate.Domain;

namespace RealEstate.Application;

public sealed class NegotiationService(ICoreAccess access,INegotiationStore store,ICustomerStore? customers=null,ICustomerDirectory? directory=null,ITeamStore? team=null,IOperationalHierarchyAccess? hierarchy=null)
{
    private async Task<Outcome<AccessScope>> Scope(string permission,CancellationToken token)
    {
        var grant=await access.RequireAsync("negotiations",permission,token);
        if(!grant.IsSuccess)return grant;
        var scope=grant.Value!;
        if(scope.OrganizationId==Guid.Empty||string.IsNullOrWhiteSpace(scope.Subject)||scope.ActiveEstablishmentId is not {} unit||scope.Establishments?.Any(e=>e.Id==unit)!=true)return Outcome<AccessScope>.Fail("Context.ActiveEstablishmentRequired");
        foreach(var(resource,right) in new[]{("customers","CUSTOMER_READ"),("properties","PROPERTY_READ")})
        {var read=await access.RequireAsync(resource,right,token);if(!SameContext(scope,read))return Outcome<AccessScope>.Fail("Access.Forbidden");}
        return grant;
    }
    private static bool SameContext(AccessScope expected,Outcome<AccessScope> actual)=>actual.IsSuccess&&actual.Value!.OrganizationId==expected.OrganizationId&&actual.Value.ActiveEstablishmentId==expected.ActiveEstablishmentId&&actual.Value.Subject==expected.Subject&&actual.Value.Establishments?.Any(e=>e.Id==expected.ActiveEstablishmentId)==true;
    public async Task<Outcome<IReadOnlyList<NegotiationResponsibleOption>>> OpenCandidatesAsync(Guid customerId,CancellationToken token=default)
    {
        var scope=await Scope("NEGOTIATION_CREATE",token);if(!scope.IsSuccess)return Outcome<IReadOnlyList<NegotiationResponsibleOption>>.Fail(scope.Code!);
        var view=await access.RequireAsync("negotiations","NEGOTIATION_VIEW",token);if(!SameContext(scope.Value!,view))return Outcome<IReadOnlyList<NegotiationResponsibleOption>>.Fail("Access.Forbidden");
        if(customers is null||directory is null||team is null||hierarchy is null)return Outcome<IReadOnlyList<NegotiationResponsibleOption>>.Fail("Dependency.Unavailable");
        var customer=await customers.FindAsync(scope.Value!,customerId,token);if(customer is null||customer.Customer.EstablishmentId!=scope.Value!.ActiveEstablishmentId)return Outcome<IReadOnlyList<NegotiationResponsibleOption>>.Fail("Resource.NotFound");
        var owner=await Candidate(scope.Value!,customer.Customer.Data.ResponsibleSubject,token);if(!owner.IsSuccess)return Outcome<IReadOnlyList<NegotiationResponsibleOption>>.Fail(owner.Code!);
        var isOwner=customer.Customer.Data.ResponsibleSubject==scope.Value.Subject;
        if(!isOwner){var authority=await hierarchy.AuthorityAsync(scope.Value!,owner.Value!.MembershipId,token);if(!authority.IsSuccess)return Outcome<IReadOnlyList<NegotiationResponsibleOption>>.Fail(authority.Code!);if(!authority.Value!.IsImmediateSupervisor)return Outcome<IReadOnlyList<NegotiationResponsibleOption>>.Fail("Access.Forbidden");}
        var candidates=await directory.ReadAsync(scope.Value!,scope.Value.ActiveEstablishmentId.Value,token:token);if(!ValidDirectory(candidates,scope.Value!))return Outcome<IReadOnlyList<NegotiationResponsibleOption>>.Fail(candidates.IsSuccess?"Dependency.InvalidResponse":candidates.Code!);
        var options=new List<NegotiationResponsibleOption>();foreach(var p in candidates.Value!.Items){if(p.MembershipId==Guid.Empty||p.ProfileId==Guid.Empty||isOwner&&p.Subject!=scope.Value.Subject)continue;if(await LocallyEnabled(scope.Value!,p,token))options.Add(new(p.Subject,p.DisplayName,p.MembershipId));}
        return new(options.OrderBy(p=>p.DisplayName,StringComparer.OrdinalIgnoreCase).ToArray());
    }

    public async Task<Outcome<IReadOnlyList<NegotiationResponsibleOption>>> TransferCandidatesAsync(Guid negotiationId,CancellationToken token=default)
    {
        var scope=await Scope("NEGOTIATION_MANAGE",token);if(!scope.IsSuccess)return Outcome<IReadOnlyList<NegotiationResponsibleOption>>.Fail(scope.Code!);
        var view=await access.RequireAsync("negotiations","NEGOTIATION_VIEW",token);if(!SameContext(scope.Value!,view))return Outcome<IReadOnlyList<NegotiationResponsibleOption>>.Fail("Access.Forbidden");
        if(directory is null||team is null||hierarchy is null)return Outcome<IReadOnlyList<NegotiationResponsibleOption>>.Fail("Dependency.Unavailable");
        var current=await store.FindNegotiationAsync(scope.Value!,negotiationId,token);if(current is null)return Outcome<IReadOnlyList<NegotiationResponsibleOption>>.Fail("Resource.NotFound");
        if(current.ResponsibleMembershipId==Guid.Empty)return Outcome<IReadOnlyList<NegotiationResponsibleOption>>.Fail("Negotiation.ResponsibleHistoryUnavailable");
        var authority=await hierarchy.AuthorityAsync(scope.Value!,current.ResponsibleMembershipId,token);if(!authority.IsSuccess)return Outcome<IReadOnlyList<NegotiationResponsibleOption>>.Fail(authority.Code!);
        if(!authority.Value!.IsImmediateSupervisor)
        {
            if(!authority.Value.CanRecoverOperationalResponsibility||current.ResponsibleMembershipId==Guid.Empty)return Outcome<IReadOnlyList<NegotiationResponsibleOption>>.Fail("Access.Forbidden");
            var previous=await hierarchy.RecipientAsync(scope.Value!,current.ResponsibleMembershipId,token);if(!previous.IsSuccess)return Outcome<IReadOnlyList<NegotiationResponsibleOption>>.Fail(previous.Code!);if(previous.Value!.IsEligible)return Outcome<IReadOnlyList<NegotiationResponsibleOption>>.Fail("Access.Forbidden");
        }
        var candidates=await directory.ReadAsync(scope.Value!,scope.Value!.ActiveEstablishmentId!.Value,token:token);if(!ValidDirectory(candidates,scope.Value!))return Outcome<IReadOnlyList<NegotiationResponsibleOption>>.Fail(candidates.IsSuccess?"Dependency.InvalidResponse":candidates.Code!);
        var options=new List<NegotiationResponsibleOption>();foreach(var p in candidates.Value!.Items){if(p.MembershipId==Guid.Empty||p.ProfileId==Guid.Empty||p.MembershipId==current.ResponsibleMembershipId)continue;if(await LocallyEnabled(scope.Value!,p,token))options.Add(new(p.Subject,p.DisplayName,p.MembershipId));}
        return new(options.OrderBy(p=>p.DisplayName,StringComparer.OrdinalIgnoreCase).ToArray());
    }
    public async Task<Outcome<Negotiation>> OpenAsync(OpenNegotiation input,CancellationToken token=default)
    {
        var scope=await Scope("NEGOTIATION_CREATE",token);if(!scope.IsSuccess)return Outcome<Negotiation>.Fail(scope.Code!);
        var view=await access.RequireAsync("negotiations","NEGOTIATION_VIEW",token);if(!SameContext(scope.Value!,view))return Outcome<Negotiation>.Fail("Access.Forbidden");
        if(input is null||!NegotiationRules.Valid(input)||input.ResponsibleSubject?.Length>255)return Outcome<Negotiation>.Fail("Negotiation.InvalidInput");
        if(customers is null||directory is null||team is null||hierarchy is null)return Outcome<Negotiation>.Fail("Dependency.Unavailable");
        var customer=await customers.FindAsync(scope.Value!,input.CustomerId,token);if(customer is null||customer.Customer.EstablishmentId!=scope.Value!.ActiveEstablishmentId)return Outcome<Negotiation>.Fail("Resource.NotFound");
        var ownerSubject=customer.Customer.Data.ResponsibleSubject;var owner=await Candidate(scope.Value!,ownerSubject,token);if(!owner.IsSuccess)return Outcome<Negotiation>.Fail(owner.Code!);
        var isOwner=ownerSubject==scope.Value.Subject;
        if(!isOwner){var authority=await hierarchy.AuthorityAsync(scope.Value!,owner.Value!.MembershipId,token);if(!authority.IsSuccess)return Outcome<Negotiation>.Fail(authority.Code!);if(!authority.Value!.IsImmediateSupervisor)return Outcome<Negotiation>.Fail("Access.Forbidden");}
        var assignedSubject=isOwner?scope.Value.Subject:(string.IsNullOrWhiteSpace(input.ResponsibleSubject)?ownerSubject:input.ResponsibleSubject.Trim());
        if(isOwner&&assignedSubject!=ownerSubject)return Outcome<Negotiation>.Fail("Access.Forbidden");
        var assigned=assignedSubject==ownerSubject?owner:await Candidate(scope.Value!,assignedSubject,token);if(!assigned.IsSuccess)return Outcome<Negotiation>.Fail(assigned.Code!);
        if(!await LocallyEnabled(scope.Value!,assigned.Value!,token))return Outcome<Negotiation>.Fail("Negotiation.ResponsibleIneligible");
        var recipient=await hierarchy.RecipientAsync(scope.Value!,assigned.Value!.MembershipId,token);if(!recipient.IsSuccess)return Outcome<Negotiation>.Fail(recipient.Code!);if(!recipient.Value!.IsEligible)return Outcome<Negotiation>.Fail("Negotiation.ResponsibleIneligible");
        return await store.OpenAsync(scope.Value!,input,ownerSubject,Assignee(assigned.Value),token);
    }
    public async Task<Outcome<Negotiation>> TransferAsync(Guid negotiationId,NegotiationTransfer input,CancellationToken token=default)
    {
        var scope=await Scope("NEGOTIATION_MANAGE",token);if(!scope.IsSuccess)return Outcome<Negotiation>.Fail(scope.Code!);
        var view=await access.RequireAsync("negotiations","NEGOTIATION_VIEW",token);if(!SameContext(scope.Value!,view))return Outcome<Negotiation>.Fail("Access.Forbidden");
        if(negotiationId==Guid.Empty||input is null||input.RequestId==Guid.Empty||string.IsNullOrWhiteSpace(input.ResponsibleSubject)||input.ResponsibleSubject.Length>255||input.ExpectedVersion<1||input.Reason?.Length>500)return Outcome<Negotiation>.Fail("Negotiation.InvalidInput");
        if(directory is null||team is null||hierarchy is null)return Outcome<Negotiation>.Fail("Dependency.Unavailable");
        var current=await store.FindNegotiationAsync(scope.Value!,negotiationId,token);if(current is null)return Outcome<Negotiation>.Fail("Resource.NotFound");
        if(current.ResponsibleMembershipId==Guid.Empty)return Outcome<Negotiation>.Fail("Negotiation.ResponsibleHistoryUnavailable");
        var authority=await hierarchy.AuthorityAsync(scope.Value!,current.ResponsibleMembershipId,token);if(!authority.IsSuccess)return Outcome<Negotiation>.Fail(authority.Code!);
        var recovery=false;if(!authority.Value!.IsImmediateSupervisor){if(!authority.Value.CanRecoverOperationalResponsibility)return Outcome<Negotiation>.Fail("Access.Forbidden");var previous=await hierarchy.RecipientAsync(scope.Value!,current.ResponsibleMembershipId,token);if(!previous.IsSuccess)return Outcome<Negotiation>.Fail(previous.Code!);if(previous.Value!.IsEligible)return Outcome<Negotiation>.Fail("Access.Forbidden");recovery=true;}
        var assignee=await Candidate(scope.Value!,input.ResponsibleSubject.Trim(),token);if(!assignee.IsSuccess)return Outcome<Negotiation>.Fail(assignee.Code!);
        if(!await LocallyEnabled(scope.Value!,assignee.Value!,token))return Outcome<Negotiation>.Fail("Negotiation.ResponsibleIneligible");
        var recipient=await hierarchy.RecipientAsync(scope.Value!,assignee.Value!.MembershipId,token);if(!recipient.IsSuccess)return Outcome<Negotiation>.Fail(recipient.Code!);if(!recipient.Value!.IsEligible)return Outcome<Negotiation>.Fail("Negotiation.ResponsibleIneligible");
        return await store.TransferAsync(scope.Value!,negotiationId,input with{ResponsibleSubject=assignee.Value!.Subject},Assignee(assignee.Value!),recovery,token);
    }
    public async Task<Outcome<NegotiationPage>> ListAsync(Guid? customerId,int page=1,CancellationToken token=default){var s=await Scope("NEGOTIATION_VIEW",token);if(!s.IsSuccess)return Outcome<NegotiationPage>.Fail(s.Code!);if(page<1||page>100000)return Outcome<NegotiationPage>.Fail("Negotiation.InvalidQuery");return new(await store.ListAsync(s.Value!,customerId,page,token));}
    public async Task<Outcome<Negotiation>> FindAsync(Guid id,CancellationToken token=default){var s=await Scope("NEGOTIATION_VIEW",token);if(!s.IsSuccess)return Outcome<Negotiation>.Fail(s.Code!);var item=await store.FindNegotiationAsync(s.Value!,id,token);return item is null?Outcome<Negotiation>.Fail("Resource.NotFound"):new(item);}
    public async Task<Outcome<IReadOnlyList<NegotiationEvent>>> EventsAsync(Guid id,CancellationToken token=default){var s=await Scope("NEGOTIATION_VIEW",token);if(!s.IsSuccess)return Outcome<IReadOnlyList<NegotiationEvent>>.Fail(s.Code!);if(await store.FindNegotiationAsync(s.Value!,id,token)is null)return Outcome<IReadOnlyList<NegotiationEvent>>.Fail("Resource.NotFound");return new(await store.NegotiationEventsAsync(s.Value!,id,token));}
    private async Task<Outcome<CaptureResponsible>> Candidate(AccessScope scope,string subject,CancellationToken token){if(directory is null)return Outcome<CaptureResponsible>.Fail("Dependency.Unavailable");var result=await directory.ReadAsync(scope,scope.ActiveEstablishmentId!.Value,subject,token);if(!ValidDirectory(result,scope))return Outcome<CaptureResponsible>.Fail(result.IsSuccess?"Dependency.InvalidResponse":result.Code!);var p=result.Value!.Items.SingleOrDefault(x=>x.Subject==subject);return p is null||p.MembershipId==Guid.Empty||p.ProfileId==Guid.Empty?Outcome<CaptureResponsible>.Fail("Negotiation.ResponsibleIneligible"):new(p);}
    private async Task<bool> LocallyEnabled(AccessScope scope,CaptureResponsible person,CancellationToken token){if(team is null)return false;var professional=await team.FindAsync(scope,person.ProfileId,token);return professional is not null&&professional.Data.Status=="Ativo";}
    private static NegotiationAssignee Assignee(CaptureResponsible person)=>new(person.Subject,person.MembershipId,person.ProfileId,person.DisplayName);
    private static bool ValidDirectory(Outcome<CaptureResponsibleDirectory> result,AccessScope scope)=>result.IsSuccess&&result.Value is {Items:not null} d&&d.OrganizationId==scope.OrganizationId&&d.EstablishmentId==scope.ActiveEstablishmentId&&d.Items.All(p=>p is not null&&!string.IsNullOrWhiteSpace(p.Subject)&&!string.IsNullOrWhiteSpace(p.DisplayName))&&d.Items.Select(p=>p.Subject).Distinct(StringComparer.Ordinal).Count()==d.Items.Count;
}
