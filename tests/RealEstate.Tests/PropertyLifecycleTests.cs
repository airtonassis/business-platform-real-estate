using RealEstate.Application;
using RealEstate.Domain;
using Xunit;
namespace RealEstate.Tests;

public sealed class PropertyLifecycleTests
{
    private static readonly Guid Org=Guid.NewGuid(),Unit=Guid.NewGuid(),OtherUnit=Guid.NewGuid();
    private sealed class Access(Guid active,bool allowed=true,string? deniedPermission=null):ICoreAccess
    {
        public Task<Outcome<AccessScope>> RequireAsync(string resource,string permission,CancellationToken token)=>
            Task.FromResult(allowed&&permission!=deniedPermission?new Outcome<AccessScope>(new(Org,"reviewer","Synthetic",[new(Unit,"Branch"),new(OtherUnit,"Other")],ActiveEstablishmentId:active)):
                Outcome<AccessScope>.Fail("Access.Forbidden"));
    }
    private sealed class Store(PropertyV2 item):IPropertyStoreV2
    {
        public int Submissions,Approvals;public PropertyV2 Current=item;
        public Task<WorkspaceV2> ListV2Async(AccessScope scope,CancellationToken token)=>throw new NotSupportedException();
        public Task<PropertyV2?> FindV2Async(AccessScope scope,Guid id,CancellationToken token)=>Task.FromResult<PropertyV2?>(id==Current.Id?Current:null);
        public Task<Outcome<PropertyV2>> SaveV2Async(AccessScope scope,PropertyInputV2 input,Guid? id,long version,CancellationToken token)=>throw new NotSupportedException();
        public Task<Outcome<PropertyV2>> SubmitForReviewAsync(AccessScope scope,Guid id,long version,CancellationToken token)
        {Submissions++;Current=Current with{Status=PropertyLifecycle.InReview,Version=version+1};return Task.FromResult<Outcome<PropertyV2>>(new(Current));}
        public Task<Outcome<PropertyV2>> ApproveAsync(AccessScope scope,Guid id,long version,CancellationToken token)
        {Approvals++;Current=Current with{Status=PropertyLifecycle.Available,Version=version+1};return Task.FromResult<Outcome<PropertyV2>>(new(Current));}
    }
    private static PropertyV2 Item(string status=PropertyLifecycle.Draft,bool complete=true)=>new(Guid.NewGuid(),Org,
        complete?new("CODE","Synthetic",Guid.NewGuid(),"Residential","Apartamento","Venda",100m,new("Address","12345678",Latitude:1,Longitude:1),Unit):
            new(EstablishmentId:Unit),1,status);

    [Fact] public async Task Complete_draft_can_be_submitted_once_by_active_branch()
    {
        var item=Item();var store=new Store(item);var result=await new PropertyServiceV2(new Access(Unit),store).SubmitForReviewAsync(item.Id,1);
        Assert.True(result.IsSuccess);Assert.Equal(PropertyLifecycle.InReview,result.Value!.Status);Assert.Equal(1,store.Submissions);
    }
    [Theory][InlineData(false,PropertyLifecycle.Draft,"Property.ReviewIncomplete")]
    [InlineData(true,PropertyLifecycle.InReview,"Property.StatusConflict")]
    [InlineData(true,PropertyLifecycle.Available,"Property.StatusConflict")]
    public async Task Invalid_submission_does_not_reach_store(bool complete,string status,string expected)
    {
        var item=Item(status,complete);var store=new Store(item);Assert.Equal(expected,(await new PropertyServiceV2(new Access(Unit),store).SubmitForReviewAsync(item.Id,1)).Code);Assert.Equal(0,store.Submissions);
    }
    [Fact] public async Task Foreign_active_branch_and_missing_right_are_denied()
    {
        var item=Item();var store=new Store(item);
        Assert.Equal("Access.Forbidden",(await new PropertyServiceV2(new Access(OtherUnit),store).SubmitForReviewAsync(item.Id,1)).Code);
        Assert.Equal("Access.Forbidden",(await new PropertyServiceV2(new Access(Unit,false),store).SubmitForReviewAsync(item.Id,1)).Code);
        Assert.Equal(0,store.Submissions);
    }
    [Fact] public void Six_statuses_are_exact_filter_values()
    {
        Assert.Equal(6,PropertyLifecycle.Labels.Count);
        foreach(var status in PropertyLifecycle.Labels.Keys)Assert.True(new PropertyListQuery{Status=status}.IsValid);
        Assert.False(new PropertyListQuery{Status="Approved"}.IsValid);
    }
    [Fact] public async Task Approval_requires_effective_profile_right_and_active_branch()
    {
        var item=Item(PropertyLifecycle.InReview);var store=new Store(item);
        Assert.Equal("Access.Forbidden",(await new PropertyServiceV2(new Access(Unit,deniedPermission:"PROPERTY_APPROVE"),store).ApproveAsync(item.Id,1)).Code);
        Assert.Equal("Access.Forbidden",(await new PropertyServiceV2(new Access(OtherUnit),store).ApproveAsync(item.Id,1)).Code);
        Assert.Equal(0,store.Approvals);
        var result=await new PropertyServiceV2(new Access(Unit),store).ApproveAsync(item.Id,1);
        Assert.True(result.IsSuccess);Assert.Equal(PropertyLifecycle.Available,result.Value!.Status);Assert.Equal(1,store.Approvals);
        Assert.Equal("Property.StatusConflict",(await new PropertyServiceV2(new Access(Unit),store).ApproveAsync(item.Id,2)).Code);
    }
}
