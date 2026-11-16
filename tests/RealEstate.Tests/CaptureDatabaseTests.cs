using System.Text.Json;
using Npgsql;
using RealEstate.Application;
using RealEstate.Domain;
using RealEstate.Infrastructure;
using Xunit;
namespace RealEstate.Tests;
[Collection("PostgreSQL")]
public sealed class CaptureDatabaseTests
{
    sealed class Grant(AccessScope scope):ICoreAccess
    {public AccessScope Scope=scope;public bool Denied;public Task<Outcome<AccessScope>> RequireAsync(string r,string p,CancellationToken t)=>Task.FromResult(Denied?Outcome<AccessScope>.Fail("Access.Forbidden"):new Outcome<AccessScope>(Scope));}
    static NpgsqlDataSource Source()=>NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("ConnectionStrings__Erp")??throw new InvalidOperationException("Disposable database required"));
    static Grant Access(){var unit=Guid.NewGuid();return new(new(Guid.NewGuid(),"capture-synthetic","Synthetic",[new(unit,"Unit")],ActiveEstablishmentId:unit));}
    [Fact] public async Task Typed_capture_roundtrip_history_legacy_omission_and_adaptation_preserve_data()
    {
        await using var ds=Source();var grant=Access();var service=new PropertyServiceV2(grant,new PostgresPropertyStore(ds));
        var c=new CaptureData();c.Areas.Total=150;c.Areas.Unit="m2";c.Rooms.Bedrooms=3;c.Rooms.Suites=1;c.Values.Sale.Authorized=500000;c.Values.Rental.Authorized=2500;
        c.Selections=[new(){GroupId="security",OptionId="custom:"+Guid.NewGuid(),Label="Sensor especial",Selected=true,Detail="Teste"},new(){GroupId="guarantees",OptionId="cash",Selected=true,Detail="Condições de teste"}];
        var saved=await service.SaveAsync(new(Type:"Casa",Purpose:"Venda e locação",Capture:c));Assert.True(saved.IsSuccess,saved.Code);var id=saved.Value!.Id;
        var read=(await service.FindAsync(id)).Value!;Assert.Equal(150,read.Data.Capture!.Areas.Total);Assert.Equal("Sensor especial",read.Data.Capture.Selections[0].Label);Assert.Single(read.Data.Capture.PriceHistory);
        var change=await service.PatchAsync(id,1,JsonSerializer.SerializeToElement(new{type="Fazenda",purpose="Venda",isInCondominium=false}));Assert.True(change.IsSuccess,change.Code);
        Assert.Equal(2500,change.Value!.Data.Capture!.Values.Rental.Authorized);Assert.True(change.Value.Data.Capture.Selections[1].Selected);
        var oldClient=await service.SaveAsync(new(Title:"Old client"),id,2);Assert.True(oldClient.IsSuccess,oldClient.Code);Assert.Equal(150,oldClient.Value!.Data.Capture!.Areas.Total);
        var modified=oldClient.Value.Data.Capture;modified.Values.Sale.Authorized=510000;modified.PriceHistory=[new(999,"forged",DateTimeOffset.UtcNow,new())];
        var update=await service.SaveAsync(oldClient.Value.Data,id,3);Assert.True(update.IsSuccess,update.Code);
        Assert.Equal(2,update.Value!.Data.Capture!.PriceHistory.Count);Assert.All(update.Value.Data.Capture.PriceHistory,h=>Assert.Equal("capture-synthetic",h.Actor));
        var races=await Task.WhenAll(service.SaveAsync(update.Value.Data,id,4),service.SaveAsync(update.Value.Data,id,4));Assert.Single(races,r=>r.IsSuccess);Assert.Single(races,r=>r.Code=="Property.VersionConflict");
        var partial=await service.PatchAsync(id,5,JsonSerializer.SerializeToElement(new{capture=new{areas=new{total=160}}}));Assert.True(partial.IsSuccess,partial.Code);
        Assert.Equal(160,partial.Value!.Data.Capture!.Areas.Total);Assert.Equal("m2",partial.Value.Data.Capture.Areas.Unit);Assert.Equal(2500,partial.Value.Data.Capture.Values.Rental.Authorized);Assert.Equal(2,partial.Value.Data.Capture.Selections.Count);
        grant.Scope=grant.Scope with{OrganizationId=Guid.NewGuid()};Assert.Equal("Resource.NotFound",(await service.FindAsync(id)).Code);
    }
    [Fact] public async Task Invalid_capture_foreign_photo_and_unauthorized_context_do_not_write()
    {
        await using var ds=Source();var grant=Access();var service=new PropertyServiceV2(grant,new PostgresPropertyStore(ds));var p=(await service.SaveAsync(new())).Value!;
        var c=new CaptureData{Selections=[new(){GroupId="equipment",OptionId="tools",Selected=true,PhotoId=Guid.NewGuid()}]};
        Assert.Equal("Capture.PhotoUnavailable",(await service.SaveAsync(p.Data with{Capture=c},p.Id,1)).Code);
        c.Selections=[];c.Rooms.Bedrooms=1;c.Rooms.Suites=2;Assert.Equal("Capture.SuitesInvalid",(await service.SaveAsync(p.Data with{Capture=c},p.Id,1)).Code);
        c.Rooms.Suites=1;c.Acquisition.ResponsibleSubject="another-user";Assert.Equal("Access.Forbidden",(await service.SaveAsync(p.Data with{Capture=c},p.Id,1)).Code);
        Assert.Equal(1,(await service.FindAsync(p.Id)).Value!.Version);grant.Denied=true;Assert.Equal("Access.Forbidden",(await service.SaveAsync(p.Data,p.Id,1)).Code);
    }
    [Fact] public async Task Owner_fiscal_uniqueness_is_concurrent_and_organization_scoped()
    {
        await using var ds=Source();var grant=Access();var store=new PostgresPropertyStore(ds);var service=new OwnerProfileService(grant,store);
        var input=new OwnerProfileInput("Synthetic PF","PF","529.982.247-25");
        var races=await Task.WhenAll(service.CreateAsync(input),service.CreateAsync(input));Assert.Single(races,r=>r.IsSuccess);Assert.Single(races,r=>r.Code=="Owner.DuplicateTaxId");
        var owner=races.First(r=>r.IsSuccess).Value!;Assert.Equal(owner.Id,Assert.Single((await service.SearchAsync("52998224725")).Value!).Id);
        grant.Scope=Access().Scope;Assert.Empty((await service.SearchAsync(input.Name)).Value!);Assert.True((await service.CreateAsync(input)).IsSuccess);
        grant.Denied=true;Assert.Equal("Access.Forbidden",(await service.CreateAsync(input)).Code);
    }
    [Fact] public async Task Media_annotations_reopen_on_authorized_property_and_reject_other_property_media()
    {
        await using var ds=Source();var grant=Access();var store=new PostgresPropertyStore(ds);var service=new PropertyServiceV2(grant,store);var p=(await service.SaveAsync(new())).Value!;
        var empty=(await store.ListMediaAsync(grant.Scope,p.Id,default))!;var photo=new MediaContentValidator().Validate("synthetic.png","image/png",MediaTestFiles.Png()).Value!;
        var uploaded=(await store.ChangeMediaAsync(grant.Scope,p.Id,empty.Revision,new("upload",Files:[photo]),default)).Value!;var id=uploaded.Items[0].Id;
        var c=new CaptureData{Media=[new(){MediaId=id,Caption="Legenda",Room="Sala",Differential="Vista",Visibility="public",InternalNotes="Interno"}]};
        Assert.True((await service.SaveAsync(p.Data with{Capture=c},p.Id,1)).IsSuccess);var reopened=(await service.FindAsync(p.Id)).Value!;Assert.Equal("Vista",Assert.Single(reopened.Data.Capture!.Media).Differential);
        var other=(await service.SaveAsync(new())).Value!;Assert.Equal("Capture.PhotoUnavailable",(await service.SaveAsync(other.Data with{Capture=c},other.Id,1)).Code);
        var removed=await store.ChangeMediaAsync(grant.Scope,p.Id,uploaded.Revision,new("remove",id),default);Assert.True(removed.IsSuccess,removed.Code);
        var afterRemoval=(await service.FindAsync(p.Id)).Value!;Assert.Empty(afterRemoval.Data.Capture!.Media);Assert.Equal(3,afterRemoval.Version);
    }
}
