using Npgsql;
using RealEstate.Application;
using RealEstate.Domain;
using RealEstate.Infrastructure;
using Xunit;
namespace RealEstate.Tests;
[Collection("PostgreSQL")]
public class SharedProfilesDatabaseTests
{
    sealed class Grant:ICoreAccess
    {
        public AccessScope Scope;public bool AllowShared=true;
        public Grant(){var unit=Guid.NewGuid();Scope=new(Guid.NewGuid(),"shared-author","Synthetic",[new(unit,"One")],ActiveEstablishmentId:unit);}
        public Task<Outcome<AccessScope>> RequireAsync(string r,string p,CancellationToken t)=>Task.FromResult(!AllowShared&&p is "OWNER_UPDATE" or "CONDOMINIUM_UPDATE"?Outcome<AccessScope>.Fail("Access.Forbidden"):new Outcome<AccessScope>(Scope));
    }
    static NpgsqlDataSource Source()=>NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("ConnectionStrings__Erp")??throw new InvalidOperationException("Disposable database required"));
    [Fact] public async Task Omitted_shared_features_are_preserved_and_legacy_owner_can_keep_missing_tax_data()
    {
        await using var ds=Source();var g=new Grant();var store=new PostgresPropertyStore(ds);var condos=new CondominiumService(g,store);
        var c=(await condos.CreateAsync(new("Preserve","mixed"))).Value!;
        Assert.True((await condos.UpdateAsync(c.Id,1,c.Data with{Characteristics=[new(){GroupId="collective-leisure",OptionId="pool",Selected=true}]})).IsSuccess);
        var renamed=await condos.UpdateAsync(c.Id,2,new("Renamed","mixed"));Assert.True(renamed.IsSuccess,renamed.Code);Assert.Single(renamed.Value!.Data.Characteristics!);
        Assert.Equal("Capture.ChoiceInvalid",(await condos.UpdateAsync(c.Id,3,new("Invalid","mixed",Characteristics:[new(){GroupId=null!,OptionId="pool"}]))).Code);
        var legacy=(await new PropertyService(g,store).CreateOwnerAsync("Legacy")).Value!;var owners=new OwnerProfileService(g,store);
        Assert.True((await owners.UpdateAsync(legacy.Id,1,new("Legacy renamed","",""))).IsSuccess);
        Assert.Equal("Owner.Invalid",(await owners.UpdateAsync(legacy.Id,2,null!)).Code);
    }
    [Fact] public async Task Owner_edit_keeps_uuid_links_history_concurrency_and_tax_uniqueness()
    {
        await using var ds=Source();var g=new Grant();var store=new PostgresPropertyStore(ds);var owners=new OwnerProfileService(g,store);var properties=new PropertyServiceV2(g,store);
        var owner=(await owners.CreateAsync(new("Synthetic","PF","52998224725"))).Value!;var p=(await properties.SaveAsync(new(OwnerId:owner.Id))).Value!;
        g.AllowShared=false;Assert.Equal("Access.Forbidden",(await owners.UpdateAsync(owner.Id,1,new("Forbidden","PF",owner.TaxId))).Code);g.AllowShared=true;
        var race=await Task.WhenAll(owners.UpdateAsync(owner.Id,1,new("One","PF",owner.TaxId)),owners.UpdateAsync(owner.Id,1,new("Two","PF",owner.TaxId)));Assert.Single(race,x=>x.IsSuccess);Assert.Single(race,x=>x.Code=="Profile.VersionConflict");
        var read=(await owners.FindForEditAsync(owner.Id)).Value!;Assert.Equal(owner.Id,read.Id);Assert.Equal(2,read.Version);Assert.Equal(2,read.History!.Count);Assert.Equal("shared-author",read.History.Last().Actor);
        Assert.Equal(owner.Id,(await properties.FindAsync(p.Id)).Value!.Data.OwnerId);
        var other=(await owners.CreateAsync(new("Other","PJ","11222333000181"))).Value!;Assert.Equal("Owner.DuplicateTaxId",(await owners.UpdateAsync(other.Id,1,new("Duplicate","PF",owner.TaxId))).Code);Assert.Equal(1,(await owners.FindForEditAsync(other.Id)).Value!.Version);
        g.Scope=g.Scope with{OrganizationId=Guid.NewGuid()};Assert.Equal("Resource.NotFound",(await owners.UpdateAsync(owner.Id,2,new("Cross","PF",owner.TaxId))).Code);
    }
    [Fact] public async Task Shared_features_are_referenced_not_copied_and_exclusions_survive_new_revisions()
    {
        await using var ds=Source();var g=new Grant();var store=new PostgresPropertyStore(ds);var condos=new CondominiumService(g,store);var properties=new PropertyServiceV2(g,store);
        var profile=(await condos.CreateAsync(new("Synthetic condo","mixed"))).Value!;
        var input=profile.Data with{Characteristics=[new(){GroupId="collective-leisure",OptionId="pool",Selected=true}]};
        g.AllowShared=false;Assert.Equal("Access.Forbidden",(await condos.UpdateAsync(profile.Id,1,input)).Code);g.AllowShared=true;
        var update=await condos.UpdateAsync(profile.Id,1,input);Assert.True(update.IsSuccess,update.Code);Assert.Equal(2,update.Value!.Version);
        var a=new CaptureData();a.Condominium=new(){Id=profile.Id,Inherit=true,ObservedVersion=2};
        var first=(await properties.SaveAsync(new(Capture:a))).Value!;Assert.Empty(first.Data.Capture!.Selections);Assert.Single(CollectiveInheritance.Resolve(first.Data.Capture,update.Value.Data));
        var b=new CaptureData{Selections=[new(){GroupId="collective-leisure",OptionId="pool",SharedOverride=false}]};b.Condominium=new(){Id=profile.Id,Inherit=true,ObservedVersion=2};
        var second=(await properties.SaveAsync(new(Capture:b))).Value!;Assert.Empty(CollectiveInheritance.Resolve(second.Data.Capture!,update.Value.Data));
        var changed=await condos.UpdateAsync(profile.Id,2,input with{Name="Renamed",Characteristics=[..input.Characteristics!,new(){GroupId="collective-services",OptionId="elevator",Selected=true}]});Assert.True(changed.IsSuccess,changed.Code);
        Assert.Equal(2,CollectiveInheritance.Resolve((await properties.FindAsync(first.Id)).Value!.Data.Capture!,changed.Value!.Data).Count);
        Assert.Single(CollectiveInheritance.Resolve((await properties.FindAsync(second.Id)).Value!.Data.Capture!,changed.Value.Data));
        Assert.Equal("Profile.VersionConflict",(await properties.SaveAsync(first.Data,first.Id,1)).Code);
        Assert.Equal("Profile.VersionConflict",(await condos.UpdateAsync(profile.Id,2,input)).Code);
        var history=(await condos.FindAsync(profile.Id,true)).Value!.History!;Assert.Equal(3,history.Count);
        g.Scope=g.Scope with{OrganizationId=Guid.NewGuid()};Assert.Equal("Resource.NotFound",(await condos.FindAsync(profile.Id)).Code);
    }
}
