using RealEstate.Application;
using RealEstate.Domain;
using Xunit;

namespace RealEstate.Tests;

public sealed class NegotiationHierarchyServiceTests
{
    private static readonly Guid Org=Guid.NewGuid(),Unit=Guid.NewGuid(),OwnerMembership=Guid.NewGuid(),TargetMembership=Guid.NewGuid(),Profile=Guid.NewGuid();
    private sealed class Access:ICoreAccess
    {
        public HashSet<string> Denied=[];
        public Task<Outcome<AccessScope>> RequireAsync(string resource,string permission,CancellationToken token)
            =>Task.FromResult(Denied.Contains(resource+"/"+permission)?Outcome<AccessScope>.Fail("Access.Forbidden"):
                new Outcome<AccessScope>(new(Org,"manager","Org",[new(Unit,"Unit")],ActiveEstablishmentId:Unit)));
    }
    private sealed class Customers:ICustomerStore
    {
        public Task<CustomerFile?> FindAsync(AccessScope scope,Guid id,CancellationToken token)
            =>Task.FromResult<CustomerFile?>(new CustomerFile(new Customer(id,Unit,new CustomerInput("Cliente","Física","11999999999",null,"Site","Compra","responsible"),1),[],[]));
        public Task<AppointmentCustomerReference?> FindAppointmentReferenceAsync(AccessScope scope,Guid id,CancellationToken token)
            =>Task.FromResult<AppointmentCustomerReference?>(new(id,Unit,"Cliente","responsible"));
        public Task<CustomerPage> SearchAsync(AccessScope s,string q,int p,CancellationToken t)=>throw new NotImplementedException();
        public Task<Outcome<Customer>> SaveAsync(AccessScope s,CustomerInput i,Guid? id,long v,bool a,CancellationToken t)=>throw new NotImplementedException();
        public Task<Outcome<CustomerContact>> AddContactAsync(AccessScope s,Guid id,ContactInput i,CancellationToken t)=>throw new NotImplementedException();
    }
    private sealed class Directory:ICustomerDirectory
    {
        public Task<Outcome<CaptureResponsibleDirectory>> ReadAsync(AccessScope scope,Guid unit,string? subject=null,CancellationToken token=default)
        {
            var owner=new CaptureResponsible("responsible","Corretora A",Profile,"Corretora",OwnerMembership);
            var target=new CaptureResponsible("target","Corretora B",Profile,"Corretora",TargetMembership);
            var items=new[]{owner,target}.Where(p=>subject is null||p.Subject==subject).ToArray();
            return Task.FromResult(new Outcome<CaptureResponsibleDirectory>(new(scope.OrganizationId,unit,items)));
        }
    }
    private sealed class Teams:ITeamStore
    {
        public Task<TeamRecord?> FindAsync(AccessScope scope,Guid id,CancellationToken token)=>Task.FromResult<TeamRecord?>(new(id,new("Corretora","CRECI-01","SP",["Venda"],null),1));
        public Task<Outcome<TeamRecord>> SaveAsync(AccessScope s,Guid id,TeamProfessional i,long v,CancellationToken t)=>throw new NotImplementedException();
    }
    private sealed class Hierarchy:IOperationalHierarchyAccess
    {
        public bool Authority=true;public bool Recover;public bool TargetEligible=true;public int AuthorityCalls;public int RecipientCalls;
        public Task<Outcome<OperationalAuthority>> AuthorityAsync(AccessScope s,Guid id,CancellationToken t){AuthorityCalls++;return Task.FromResult(new Outcome<OperationalAuthority>(new(Authority,Recover,3,DateTimeOffset.UtcNow)));}
        public Task<Outcome<OperationalRecipient>> RecipientAsync(AccessScope s,Guid id,CancellationToken t){RecipientCalls++;return Task.FromResult(new Outcome<OperationalRecipient>(new(id==OwnerMembership?false:TargetEligible,DateTimeOffset.UtcNow)));}
    }
    private sealed class Store:INegotiationStore
    {
        public int Opens;public int Transfers;public string? Assigned;public bool Recovery;
        public Task<Outcome<Negotiation>> OpenAsync(AccessScope s,OpenNegotiation i,string owner,NegotiationAssignee assignee,CancellationToken t){Opens++;Assigned=assignee.Subject;return Task.FromResult(Outcome<Negotiation>.Fail("Negotiation.OfferUnavailable"));}
        public Task<Outcome<Negotiation>> TransferAsync(AccessScope s,Guid id,NegotiationTransfer i,NegotiationAssignee assignee,bool recovery,CancellationToken t){Transfers++;Assigned=assignee.Subject;Recovery=recovery;return Task.FromResult(Outcome<Negotiation>.Fail("Negotiation.VersionConflict"));}
        public Task<Negotiation?> FindNegotiationAsync(AccessScope s,Guid id,CancellationToken t)=>Task.FromResult<Negotiation?>(new(id,Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Unit,"responsible","Preparing",4,DateTimeOffset.UtcNow,OwnerMembership));
        public Task<NegotiationPage> ListAsync(AccessScope s,Guid? c,int p,CancellationToken t)=>Task.FromResult(new NegotiationPage([],0,p));
        public Task<IReadOnlyList<NegotiationEvent>> NegotiationEventsAsync(AccessScope s,Guid id,CancellationToken t)=>Task.FromResult<IReadOnlyList<NegotiationEvent>>([]);
    }
    private static NegotiationService Service(Access access,Store store,Hierarchy hierarchy)=>new(access,store,new Customers(),new Directory(),new Teams(),hierarchy);
    [Fact] public async Task Immediate_superior_can_open_for_customer_owner_and_assign_eligible_candidate()
    {
        var store=new Store();var hierarchy=new Hierarchy();var result=await Service(new(),store,hierarchy).OpenAsync(new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"target"));
        Assert.Equal("Negotiation.OfferUnavailable",result.Code);Assert.Equal("target",store.Assigned);Assert.Equal(1,hierarchy.AuthorityCalls);Assert.Equal(1,hierarchy.RecipientCalls);
    }
    [Fact] public async Task Manager_without_immediate_authority_cannot_open_even_with_admin_context()
    {
        var store=new Store();var hierarchy=new Hierarchy{Authority=false};var result=await Service(new(),store,hierarchy).OpenAsync(new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"target"));
        Assert.Equal("Access.Forbidden",result.Code);Assert.Equal(0,store.Opens);
    }
    [Fact] public async Task Transfer_requires_manage_right_and_revalidates_recipient()
    {
        var store=new Store();var hierarchy=new Hierarchy();var service=Service(new(),store,hierarchy);
        var result=await service.TransferAsync(Guid.NewGuid(),new(Guid.NewGuid(),"target",4,"Cobertura de fÃ©rias"));
        Assert.Equal("Negotiation.VersionConflict",result.Code);Assert.Equal(1,store.Transfers);Assert.Equal("target",store.Assigned);Assert.False(store.Recovery);
    }
    [Fact] public async Task Recovery_requires_explicit_manage_right_and_core_recovery_decision()
    {
        var store=new Store();var access=new Access{Denied=["negotiations/NEGOTIATION_MANAGE"]};var hierarchy=new Hierarchy{Recover=true};
        var result=await Service(access,store,hierarchy).TransferAsync(Guid.NewGuid(),new(Guid.NewGuid(),"target",4));
        Assert.Equal("Access.Forbidden",result.Code);Assert.Equal(0,store.Transfers);
    }
}
