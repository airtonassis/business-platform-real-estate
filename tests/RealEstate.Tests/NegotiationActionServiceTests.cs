using RealEstate.Application;
using RealEstate.Domain;
using Xunit;

namespace RealEstate.Tests;

public sealed class NegotiationActionServiceTests
{
    private static readonly Guid Org=Guid.NewGuid(),Unit=Guid.NewGuid(),Customer=Guid.NewGuid(),Property=Guid.NewGuid(),NegotiationId=Guid.NewGuid();
    private static readonly Guid CurrentMembership=Guid.NewGuid(),CurrentProfile=Guid.NewGuid(),OtherMembership=Guid.NewGuid(),OtherProfile=Guid.NewGuid();
    private static Negotiation Negotiation()=>new(NegotiationId,Customer,Property,Guid.NewGuid(),Guid.NewGuid(),Unit,"current","Preparing",4,DateTimeOffset.UtcNow,CurrentMembership);

    private sealed class Access:ICoreAccess
    {
        public string? Denied;
        public string? CrossUnitPermission;
        public Task<Outcome<AccessScope>> RequireAsync(string resource,string permission,CancellationToken token)
        {
            if(permission==Denied)return Task.FromResult(Outcome<AccessScope>.Fail("Access.Forbidden"));
            var unit=permission==CrossUnitPermission?Guid.NewGuid():Unit;
            return Task.FromResult(new Outcome<AccessScope>(new(Org,"actor","Unit",[new(Unit,"Unit")],ActiveEstablishmentId:unit)));
        }
    }
    private sealed class Negotiations:INegotiationStore
    {
        public Task<Negotiation?> FindNegotiationAsync(AccessScope scope,Guid id,CancellationToken token)=>Task.FromResult<Negotiation?>(id==NegotiationId&&scope.ActiveEstablishmentId==Unit?Negotiation():null);
        public Task<Outcome<Negotiation>> OpenAsync(AccessScope s,OpenNegotiation i,string o,NegotiationAssignee a,CancellationToken t)=>throw new NotImplementedException();
        public Task<Outcome<Negotiation>> TransferAsync(AccessScope s,Guid id,NegotiationTransfer i,NegotiationAssignee a,bool r,CancellationToken t)=>throw new NotImplementedException();
        public Task<NegotiationPage> ListAsync(AccessScope s,Guid? c,int p,CancellationToken t)=>throw new NotImplementedException();
        public Task<IReadOnlyList<NegotiationEvent>> NegotiationEventsAsync(AccessScope s,Guid id,CancellationToken t)=>Task.FromResult<IReadOnlyList<NegotiationEvent>>([]);
    }
    private sealed class Directory:ICustomerDirectory
    {
        public Task<Outcome<CaptureResponsibleDirectory>> ReadAsync(AccessScope scope,Guid unit,string? subject=null,CancellationToken token=default)
        {
            CaptureResponsible[] all=[new("current","Current owner",CurrentMembership,"Broker",CurrentProfile),new("other","Other owner",OtherMembership,"Broker",OtherProfile)];
            return Task.FromResult(new Outcome<CaptureResponsibleDirectory>(new(scope.OrganizationId,unit,subject is null?all:all.Where(x=>x.Subject==subject).ToArray())));
        }
    }
    private sealed class Team:ITeamStore
    {
        public Task<TeamRecord?> FindAsync(AccessScope s,Guid id,CancellationToken t)=>Task.FromResult<TeamRecord?>(new(id,new("Broker",null,"SP",["Venda"],null,"Ativo"),1));
        public Task<Outcome<TeamRecord>> SaveAsync(AccessScope s,Guid id,TeamProfessional i,long v,CancellationToken t)=>throw new NotImplementedException();
    }
    private sealed class Hierarchy(bool eligible=true):IOperationalHierarchyAccess
    {
        public Task<Outcome<OperationalAuthority>> AuthorityAsync(AccessScope s,Guid id,CancellationToken t)=>Task.FromResult(new Outcome<OperationalAuthority>(new(true,false,1,DateTimeOffset.UtcNow)));
        public Task<Outcome<OperationalRecipient>> RecipientAsync(AccessScope s,Guid id,CancellationToken t)=>Task.FromResult(new Outcome<OperationalRecipient>(new(eligible,DateTimeOffset.UtcNow)));
    }
    private sealed class Actions:INegotiationActionStore
    {
        public int Creates;
        public NegotiationAction? Created;
        public IReadOnlyList<NegotiationAction> Prior=[];
        public Task<IReadOnlyList<NegotiationAction>> ListActionsAsync(AccessScope s,Guid id,CancellationToken t)=>Task.FromResult(Prior);
        public Task<IReadOnlyList<NegotiationActionEvent>> ActionEventsAsync(AccessScope s,Guid id,CancellationToken t)=>Task.FromResult<IReadOnlyList<NegotiationActionEvent>>([]);
        public Task<Outcome<NegotiationAction?>> ExistingCreateRequestAsync(AccessScope s,Guid id,NegotiationActionInput i,CancellationToken t)=>Task.FromResult(new Outcome<NegotiationAction?>((NegotiationAction?)null));
        public Task<Outcome<NegotiationAction>> CreateActionAsync(AccessScope s,Negotiation n,NegotiationActionInput i,NegotiationAssignee a,NegotiationActionAppointment? p,CancellationToken t)
        {
            Creates++;Created=new(Guid.NewGuid(),n.Id,n.CustomerId,n.PropertyId,null,i.Kind,i.Description,i.DueAt,a.Subject,a.DisplayName,"Aberta",null,s.Subject,DateTimeOffset.UtcNow,1,DateTimeOffset.UtcNow);return Task.FromResult(new Outcome<NegotiationAction>(Created));
        }
        public Task<Outcome<NegotiationActionCompletionResult>> CompleteActionAsync(AccessScope s,Guid id,NegotiationActionCompletion i,CancellationToken t)=>Task.FromResult(Outcome<NegotiationActionCompletionResult>.Fail("not used"));
    }

    private static NegotiationActionService Service(Access access,Actions actions,bool eligible=true)=>new(access,new Negotiations(),actions,new Directory(),new Team(),new Hierarchy(eligible),null!);
    private static NegotiationActionInput Valid(string? responsible="current")=>new(Guid.NewGuid(),"Contato","Ligar para confirmar as condições",DateTimeOffset.UtcNow.AddDays(1),responsible,null,4);

    [Fact]
    public async Task Missing_contact_create_right_stops_before_write()
    {
        var access=new Access{Denied="CUSTOMER_CONTACT_CREATE"};var store=new Actions();
        var result=await Service(access,store).CreateAsync(NegotiationId,Valid());
        Assert.Equal("Access.Forbidden",result.Code);Assert.Equal(0,store.Creates);
    }

    [Fact]
    public async Task Mismatched_active_branch_stops_before_write()
    {
        var access=new Access{CrossUnitPermission="PROPERTY_READ"};var store=new Actions();
        var result=await Service(access,store).CreateAsync(NegotiationId,Valid());
        Assert.Equal("Access.Forbidden",result.Code);Assert.Equal(0,store.Creates);
    }

    [Fact]
    public async Task Different_responsible_requires_manage_right_before_write()
    {
        var access=new Access{Denied="NEGOTIATION_MANAGE"};var store=new Actions();
        var result=await Service(access,store).CreateAsync(NegotiationId,Valid("other"));
        Assert.Equal("Access.Forbidden",result.Code);Assert.Equal(0,store.Creates);
    }

    [Fact]
    public async Task Eligible_alternate_responsible_is_recorded_on_action()
    {
        var store=new Actions();var result=await Service(new Access(),store).CreateAsync(NegotiationId,Valid("other"));
        Assert.True(result.IsSuccess,result.Code);Assert.Equal("other",store.Created!.ResponsibleSubject);Assert.Equal("Other owner",store.Created.ResponsibleName);
    }

    [Fact]
    public async Task Alternate_without_current_core_eligibility_is_rejected()
    {
        var store=new Actions();var result=await Service(new Access(),store,eligible:false).CreateAsync(NegotiationId,Valid("other"));
        Assert.Equal("Negotiation.ResponsibleIneligible",result.Code);Assert.Equal(0,store.Creates);
    }

    [Fact]
    public async Task Historical_actions_remain_visible_after_responsible_changes()
    {
        var action=new NegotiationAction(Guid.NewGuid(),NegotiationId,Customer,Property,null,"Contato","Ligar",DateTimeOffset.UtcNow,"previous","Previous owner","Aberta",null,"actor",DateTimeOffset.UtcNow,1,DateTimeOffset.UtcNow);
        var store=new Actions{Prior=[action]};var result=await Service(new Access(),store).ListAsync(NegotiationId);
        Assert.True(result.IsSuccess);Assert.Contains(result.Value!,x=>x.ResponsibleSubject=="previous");
    }

    [Fact]
    public async Task Invalid_contact_without_due_date_stops_before_write()
    {
        var store=new Actions();var result=await Service(new Access(),store).CreateAsync(NegotiationId,Valid() with{DueAt=null});
        Assert.Equal("NegotiationAction.DueRequired",result.Code);Assert.Equal(0,store.Creates);
    }
}
