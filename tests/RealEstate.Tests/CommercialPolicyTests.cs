using Npgsql;
using RealEstate.Application;
using RealEstate.Domain;
using RealEstate.Infrastructure;
using Xunit;
namespace RealEstate.Tests;

public sealed class CommercialPolicyRulesTests
{
    static CommercialPolicyInput Input(PreliminaryFees? fees=null,params CaptureSelection[] guarantees)=>new("Policy","Synthetic configuration",new(fees??new(),guarantees));
    [Fact]public void No_financial_defaults_are_invented(){var r=CommercialPolicyRules.Validate(Input());Assert.True(r.IsSuccess);Assert.Null(r.Value!.Data.Fees.SalePercent);Assert.Empty(r.Value.Data.Guarantees);Assert.Empty(r.Value.Data.Fees.Allocations);}
    [Fact]public void Allocation_configuration_is_typed_and_does_not_apportion_money(){var fees=new PreliminaryFees{Allocations=[new("sale","internal","Capture role",40,null,"Commission defined in negotiation"),new("sale","organization","Agency",60,null,"Same base")]};Assert.True(CommercialPolicyRules.Validate(Input(fees)).IsSuccess);fees.Allocations.Add(new("sale","external","External",1,null,null));Assert.False(CommercialPolicyRules.Validate(Input(fees)).IsSuccess);}
    [Fact]public void Percentage_is_not_a_payment_or_a_fixed_amount(){Assert.False(CommercialPolicyRules.Validate(Input(new(){SalePercent=101})).IsSuccess);Assert.False(CommercialPolicyRules.AllocationsValid([new("sale","internal","Role",50,100,null)]));}
    [Fact]public void No_guarantee_is_exclusive(){Assert.False(CommercialPolicyRules.Validate(Input(null,new CaptureSelection(){GroupId="guarantees",OptionId="none",Selected=true},new CaptureSelection(){GroupId="guarantees",OptionId="cash",Selected=true})).IsSuccess);}
    [Fact]public void Application_preserves_unrelated_and_hidden_data_without_mutating_source(){var current=new CaptureData();current.Rooms.Bedrooms=4;current.Values.Rental.Authorized=1250;var data=Input(new(){SalePercent=7},new CaptureSelection(){GroupId="guarantees",OptionId="insurance",Selected=true});var r=new CommercialPolicyRevision(Guid.NewGuid(),2,"Approved",data,"author","manager",DateTimeOffset.UtcNow,[]);var applied=CommercialPolicyRules.Apply(r,current);Assert.Equal(4,applied.Rooms.Bedrooms);Assert.Equal(1250,applied.Values.Rental.Authorized);Assert.Null(current.Fees.SalePercent);Assert.Equal(7,applied.Fees.SalePercent);Assert.Equal(r.Id,applied.Policy!.RevisionId);}
}
[Collection("PostgreSQL")]
public sealed class CommercialPolicyDatabaseTests
{
    sealed class Access(AccessScope scope):ICoreAccess
    {public AccessScope Scope=scope;public string? Denied;public Task<Outcome<AccessScope>> RequireAsync(string resource,string permission,CancellationToken token)=>Task.FromResult(permission==Denied?Outcome<AccessScope>.Fail("Access.Forbidden"):new Outcome<AccessScope>(Scope));}
    static Access Grant(){var unit=Guid.NewGuid();return new(new(Guid.NewGuid(),"author","Synthetic",[new(unit,"Unit")],true,unit));}
    static NpgsqlDataSource Source()=>NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("ConnectionStrings__Erp")??throw new InvalidOperationException("Disposable PostgreSQL required"));
    static CommercialPolicyInput Input(decimal percent,Guid? property=null,Guid? basis=null)=>new("Synthetic policy","Explicit test values",new(new(){SalePercent=percent},[new CaptureSelection(){GroupId="guarantees",OptionId="cash",Selected=true,Detail="Synthetic acceptance"}]),property,basis);
    [Fact]public async Task Draft_approval_versions_history_and_tenant_boundaries_are_real()
    {
        await using var ds=Source();var access=Grant();var store=new PostgresPropertyStore(ds);var svc=new CommercialPolicyService(access,store,store);
        var draft=(await svc.SaveDraftAsync(Input(6))).Value!;Assert.NotNull(draft);Assert.Equal("Policy.SelfApproval",(await svc.ApproveAsync(draft.Id,1,"Cannot self approve")).Code);
        access.Scope=access.Scope with{Subject="manager"};access.Denied="POLICY_APPROVE";Assert.Equal("Access.Forbidden",(await svc.ApproveAsync(draft.Id,1,"Explicit manager approval")).Code);access.Denied=null;
        var approved=(await svc.ApproveAsync(draft.Id,1,"Explicit manager approval")).Value!;Assert.Equal("Approved",approved.Status);Assert.Equal(2,approved.Version);Assert.Equal(2,approved.History.Count);
        Assert.Equal("Policy.Immutable",(await svc.SaveDraftAsync(Input(8),approved.Id,2)).Code);
        access.Scope=access.Scope with{OrganizationId=Guid.NewGuid()};Assert.Empty((await svc.ListAsync()).Value!);Assert.Equal("Resource.NotFound",(await svc.ApproveAsync(draft.Id,2,"Other organization")).Code);
    }
    [Fact]public async Task Applied_policy_and_approved_exception_roundtrip_without_bypass_or_automatic_replacement()
    {
        await using var ds=Source();var access=Grant();var store=new PostgresPropertyStore(ds);var svc=new CommercialPolicyService(access,store,store);var properties=new PropertyServiceV2(access,store);
        var draft=(await svc.SaveDraftAsync(Input(6))).Value!;access.Scope=access.Scope with{Subject="manager"};var policy=(await svc.ApproveAsync(draft.Id,1,"Explicit approval")).Value!;
        var p=(await properties.SaveAsync(new(Capture:new(){Rooms=new(){Bedrooms=3}}))).Value!;
        var applied=(await properties.SaveAsync(p.Data with{Capture=CommercialPolicyRules.Apply(policy,p.Data.Capture)},p.Id,1)).Value!;Assert.NotNull(applied);Assert.Equal(6,(await properties.FindAsync(p.Id)).Value!.Data.Capture!.Fees.SalePercent);
        var changed=CommercialPolicyRules.Apply(policy,applied.Data.Capture);changed.Fees.SalePercent=9;Assert.Equal("Policy.ExceptionRequired",(await properties.SaveAsync(applied.Data with{Capture=changed},p.Id,2)).Code);
        changed.Policy=null;Assert.Equal("Policy.ExceptionRequired",(await properties.SaveAsync(applied.Data with{Capture=changed},p.Id,2)).Code);
        access.Scope=access.Scope with{Subject="author"};var exception=(await svc.SaveDraftAsync(Input(9,p.Id,policy.Id))).Value!;access.Scope=access.Scope with{Subject="manager"};var approved=(await svc.ApproveAsync(exception.Id,1,"Exception justified by property")).Value!;
        var other=(await properties.SaveAsync(new())).Value!;Assert.Equal("Policy.Unavailable",(await properties.SaveAsync(other.Data with{Capture=CommercialPolicyRules.Apply(approved,null)},other.Id,1)).Code);
        var result=await properties.SaveAsync(applied.Data with{Capture=CommercialPolicyRules.Apply(approved,applied.Data.Capture)},p.Id,2);Assert.True(result.IsSuccess,result.Code);Assert.Equal(9,result.Value!.Data.Capture!.Fees.SalePercent);Assert.Equal(3,result.Value.Data.Capture.Rooms.Bedrooms);
        access.Denied="POLICY_APPLY";Assert.Equal("Access.Forbidden",(await properties.SaveAsync(result.Value.Data with{Capture=CommercialPolicyRules.Apply(policy,result.Value.Data.Capture)},p.Id,3)).Code);access.Denied=null;
        var preserved=await properties.SaveAsync(result.Value.Data with{Title="Normal edit"},p.Id,3);Assert.True(preserved.IsSuccess,preserved.Code);Assert.Equal(approved.Id,preserved.Value!.Data.Capture!.Policy!.RevisionId);
        access.Scope=access.Scope with{Subject="author"};var next=(await svc.SaveDraftAsync(Input(8,basis:policy.Id))).Value!;access.Scope=access.Scope with{Subject="manager"};Assert.True((await svc.ApproveAsync(next.Id,1,"New general policy")).IsSuccess);Assert.Equal(9,(await properties.FindAsync(p.Id)).Value!.Data.Capture!.Fees.SalePercent);
    }
    [Fact]public async Task Concurrent_approvals_and_stale_edits_cannot_overwrite_an_approved_revision()
    {
        await using var ds=Source();var access=Grant();var store=new PostgresPropertyStore(ds);var svc=new CommercialPolicyService(access,store,store);var draft=(await svc.SaveDraftAsync(Input(5))).Value!;
        var edited=(await svc.SaveDraftAsync(Input(7),draft.Id,1)).Value!;Assert.Equal("Profile.VersionConflict",(await svc.SaveDraftAsync(Input(8),draft.Id,1)).Code);
        access.Scope=access.Scope with{Subject="manager"};var races=await Task.WhenAll(svc.ApproveAsync(draft.Id,edited.Version,"Concurrent approval"),svc.ApproveAsync(draft.Id,edited.Version,"Concurrent approval"));Assert.Single(races,r=>r.IsSuccess);Assert.Single(races,r=>r.Code=="Profile.VersionConflict");
        Assert.Equal(3,((await svc.ListAsync()).Value!.Single()).History.Count);
    }
}
