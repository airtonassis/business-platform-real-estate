using Npgsql;
using RealEstate.Application;
using RealEstate.Domain;
using RealEstate.Infrastructure;
using Xunit;
namespace RealEstate.Tests;
[Collection("PostgreSQL")]
public sealed class CommercialPolicyCompatibilityTests
{
    sealed class Access(AccessScope scope):ICoreAccess{public AccessScope Scope=scope;public bool DenyApply;public Task<Outcome<AccessScope>> RequireAsync(string r,string p,CancellationToken t)=>Task.FromResult(DenyApply&&p=="POLICY_APPLY"?Outcome<AccessScope>.Fail("Access.Forbidden"):new Outcome<AccessScope>(Scope));}
    [Fact]public async Task Legacy_capture_without_new_provenance_preserves_approval_and_allocations_but_cannot_change_approved_terms()
    {
        await using var ds=NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("ConnectionStrings__Erp")??throw new InvalidOperationException("Disposable PostgreSQL required"));var unit=Guid.NewGuid();var access=new Access(new(Guid.NewGuid(),"author","Synthetic",[new(unit,"Unit")],true,unit));var store=new PostgresPropertyStore(ds);var policy=new CommercialPolicyService(access,store,store);var service=new PropertyServiceV2(access,store);
        var draft=(await policy.SaveDraftAsync(new("Policy","Explicit test",new(new(){SalePercent=5,Allocations=[new("sale","internal","Capture role",50,null,"Agreed commission")]},[])))).Value!;access.Scope=access.Scope with{Subject="manager"};var approved=(await policy.ApproveAsync(draft.Id,1,"Independent approval")).Value!;var p=(await service.SaveAsync(new(Capture:CommercialPolicyRules.Apply(approved,null)))).Value!;access.DenyApply=true;
        var legacy=new CaptureData{Fees=new(){SalePercent=5},Rooms=new(){Bedrooms=4}};var saved=await service.SaveAsync(p.Data with{Capture=legacy,Title="Legacy client edit"},p.Id,1);Assert.True(saved.IsSuccess,saved.Code);Assert.Equal(approved.Id,saved.Value!.Data.Capture!.Policy!.RevisionId);Assert.Single(saved.Value.Data.Capture.Fees.Allocations);Assert.Equal(4,saved.Value.Data.Capture.Rooms.Bedrooms);
        var forged=new CaptureData{Fees=new(){SalePercent=9}};Assert.Equal("Policy.ExceptionRequired",(await service.SaveAsync(saved.Value.Data with{Capture=forged},p.Id,2)).Code);Assert.Equal(2,(await service.FindAsync(p.Id)).Value!.Version);
    }
}
