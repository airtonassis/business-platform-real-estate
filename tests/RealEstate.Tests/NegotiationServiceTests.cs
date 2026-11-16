using RealEstate.Application;
using RealEstate.Domain;
using Xunit;
namespace RealEstate.Tests;
public sealed class NegotiationServiceTests
{
    private static readonly Guid Organization=Guid.NewGuid(),Unit=Guid.NewGuid();
    private sealed class Access:ICoreAccess
    {
        public string? Denied;
        public bool CrossUnit;
        public Task<Outcome<AccessScope>> RequireAsync(string resource,string permission,CancellationToken token)
        {
            if(permission==Denied)return Task.FromResult(Outcome<AccessScope>.Fail("Access.Forbidden"));
            var current=CrossUnit&&resource=="properties"?Guid.NewGuid():Unit;
            return Task.FromResult(new Outcome<AccessScope>(new(Organization,"operator","Organization",[new(Unit,"Unit")],ActiveEstablishmentId:current)));
        }
    }
    private sealed class Store:INegotiationStore
    {
        public int Calls;
        public Task<Outcome<Negotiation>> OpenAsync(AccessScope scope,OpenNegotiation input,string owner,NegotiationAssignee assignee,CancellationToken token){Calls++;return Task.FromResult(Outcome<Negotiation>.Fail("Negotiation.OfferUnavailable"));}
        public Task<Outcome<Negotiation>> TransferAsync(AccessScope scope,Guid id,NegotiationTransfer input,NegotiationAssignee assignee,bool recovery,CancellationToken token){Calls++;return Task.FromResult(Outcome<Negotiation>.Fail("Negotiation.VersionConflict"));}
        public Task<Negotiation?> FindNegotiationAsync(AccessScope scope,Guid id,CancellationToken token){Calls++;return Task.FromResult<Negotiation?>(null);}
        public Task<NegotiationPage> ListAsync(AccessScope scope,Guid? customerId,int page,CancellationToken token){Calls++;return Task.FromResult(new NegotiationPage([],0,page));}
        public Task<IReadOnlyList<NegotiationEvent>> NegotiationEventsAsync(AccessScope scope,Guid id,CancellationToken token)=>Task.FromResult<IReadOnlyList<NegotiationEvent>>([]);
    }
    [Fact]public async Task Missing_create_right_does_not_touch_store()
    {
        var access=new Access{Denied="NEGOTIATION_CREATE"};var store=new Store();var result=await new NegotiationService(access,store).OpenAsync(new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid()));
        Assert.Equal("Access.Forbidden",result.Code);Assert.Equal(0,store.Calls);
    }
    [Fact]public async Task Cross_unit_read_right_does_not_touch_store()
    {
        var access=new Access{CrossUnit=true};var store=new Store();var result=await new NegotiationService(access,store).OpenAsync(new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid()));
        Assert.Equal("Access.Forbidden",result.Code);Assert.Equal(0,store.Calls);
    }
    [Fact]public async Task List_requires_view_right_before_counting()
    {
        var access=new Access{Denied="NEGOTIATION_VIEW"};var store=new Store();var result=await new NegotiationService(access,store).ListAsync(null);
        Assert.Equal("Access.Forbidden",result.Code);Assert.Equal(0,store.Calls);
    }
    [Fact]public async Task Opening_requires_view_right_for_created_dossier()
    {
        var access=new Access{Denied="NEGOTIATION_VIEW"};var store=new Store();var result=await new NegotiationService(access,store).OpenAsync(new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid()));
        Assert.Equal("Access.Forbidden",result.Code);Assert.Equal(0,store.Calls);
    }
}
