using Npgsql;
using RealEstate.Application;
using RealEstate.Domain;
using RealEstate.Infrastructure;
using Xunit;
namespace RealEstate.Tests;

[Collection("PostgreSQL")]
public sealed class CommercialCodeTests
{
    sealed class Authority(AccessScope scope):ICoreAccess
    {public Task<Outcome<AccessScope>> RequireAsync(string r,string p,CancellationToken t)=>Task.FromResult(new Outcome<AccessScope>(scope));}
    static NpgsqlDataSource Source()=>NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("ConnectionStrings__Erp")??throw new InvalidOperationException("Isolated database required"));
    static PropertyServiceV2 Service(NpgsqlDataSource ds,Guid org,Guid unit)=>new(new Authority(new(org,"commercial-code-fixture","Synthetic",[new(unit,"Unit")],ActiveEstablishmentId:unit)),new PostgresPropertyStore(ds));
    [Fact] public async Task Concurrent_assignments_share_company_sequence_across_branches_and_prefixes()
    {
        await using var ds=Source();var org=Guid.NewGuid();var a=Service(ds,org,Guid.NewGuid());var b=Service(ds,org,Guid.NewGuid());
        var results=await Task.WhenAll(Enumerable.Range(0,24).Select(i=>(i%2==0?a:b).SaveAsync(new(Type:i%2==0?"Apartamento":"Casa",Purpose:i%2==0?"Venda":"Locação"))));
        Assert.All(results,r=>Assert.True(r.IsSuccess,r.Code));
        Assert.Equal(Enumerable.Range(1,24).Select(x=>(long)x),results.Select(r=>long.Parse(r.Value!.CommercialCode!.Split('-')[1])).Order());
        Assert.All(results,r=>Assert.StartsWith(r.Value!.Data.Type=="Casa"?"LOCCAS-":"VENAP-",r.Value.CommercialCode));
        Assert.Equal("VENAP-1",(await Service(ds,Guid.NewGuid(),Guid.NewGuid()).SaveAsync(new(Type:"Apartamento",Purpose:"Venda"))).Value!.CommercialCode);
    }
    [Fact] public async Task Empty_and_unmapped_drafts_stay_pending_then_assignment_is_immutable_and_legacy_id_survives()
    {
        await using var ds=Source();var service=Service(ds,Guid.NewGuid(),Guid.NewGuid());
        var draft=(await service.SaveAsync(new())).Value!;Assert.Null(draft.CommercialCode);var legacy=draft.Data.Code;
        var unknown=(await service.SaveAsync(draft.Data with{Type="Cobertura",Purpose="Venda"},draft.Id,1)).Value!;Assert.Null(unknown.CommercialCode);
        var assigned=(await service.SaveAsync(unknown.Data with{Type="Apartamento"},draft.Id,2)).Value!;Assert.Equal("VENAP-1",assigned.CommercialCode);
        Assert.Equal("Announcement.PurposeImmutable",(await service.SaveAsync(assigned.Data with{Purpose="Locação"},draft.Id,3)).Code);
        var changed=(await service.SaveAsync(assigned.Data with{Type="Terreno"},draft.Id,3)).Value!;
        Assert.Equal(assigned.CommercialCode,changed.CommercialCode);Assert.Equal(legacy,changed.Data.Code);
        Assert.Equal(assigned.CommercialCode,(await service.FindAsync(draft.Id)).Value!.CommercialCode);
        Assert.Equal("VENTER-2",(await service.SaveAsync(new(Type:"Terreno",Purpose:"Venda"))).Value!.CommercialCode);
    }
    [Fact] public async Task Existing_supplied_code_is_preserved_and_numeric_suffix_reserved()
    {
        await using var ds=Source();var service=Service(ds,Guid.NewGuid(),Guid.NewGuid());
        var old=(await service.SaveAsync(new(Code:"VENAP-42",Type:"Apartamento",Purpose:"Venda"))).Value!;
        Assert.Equal("VENAP-42",old.CommercialCode);Assert.Equal("VENAP-42",old.Data.Code);
        Assert.Equal("LOCSAL-43",(await service.SaveAsync(new(Type:"Sala Comercial",Purpose:"Locação"))).Value!.CommercialCode);
        var edited=(await service.SaveAsync(old.Data with{Code="OTHER"},old.Id,1)).Value!;
        Assert.Equal("VENAP-42",edited.CommercialCode);
    }
    [Fact] public async Task Approved_rural_types_use_RUR_and_other_unmapped_combinations_remain_drafts()
    {
        await using var ds=Source();var service=Service(ds,Guid.NewGuid(),Guid.NewGuid());var n=0;
        foreach(var type in new[]{"Chácara","Fazenda","Sítio"})
        {var saved=await service.SaveAsync(new(Type:type,Purpose:"Venda"));Assert.True(saved.IsSuccess,saved.Code);Assert.Equal("VENRUR-"+(++n),saved.Value!.CommercialCode);}
        foreach(var input in new[]{new PropertyInputV2(Type:"Cobertura",Purpose:"Venda"),new PropertyInputV2(Type:"Kitnet",Purpose:"Locação")})
        {var saved=await service.SaveAsync(input);Assert.True(saved.IsSuccess,saved.Code);Assert.Null(saved.Value!.CommercialCode);Assert.Equal("Draft",saved.Value.Status);}
    }
    [Fact] public async Task Same_property_assignment_race_has_one_winner_and_stale_update_allocates_nothing()
    {
        await using var ds=Source();var service=Service(ds,Guid.NewGuid(),Guid.NewGuid());var draft=(await service.SaveAsync(new())).Value!;
        var races=await Task.WhenAll(service.SaveAsync(draft.Data with{Type="Casa",Purpose="Venda"},draft.Id,1),service.SaveAsync(draft.Data with{Type="Apartamento",Purpose="Locação"},draft.Id,1));
        Assert.Single(races,r=>r.IsSuccess);Assert.Single(races,r=>r.Code=="Property.VersionConflict");
        Assert.EndsWith("-1",races.Single(r=>r.IsSuccess).Value!.CommercialCode);
        Assert.Equal("VENTER-2",(await service.SaveAsync(new(Type:"Terreno",Purpose:"Venda"))).Value!.CommercialCode);
    }
    [Fact] public async Task Image_description_reuses_caption_preserves_file_and_rejects_overlength_edits()
    {
        await using var ds=Source();var org=Guid.NewGuid();var unit=Guid.NewGuid();var scope=new AccessScope(org,"description-fixture","Synthetic",[new(unit,"Unit")],ActiveEstablishmentId:unit);
        var store=new PostgresPropertyStore(ds);var service=Service(ds,org,unit);var p=(await service.SaveAsync(new())).Value!;
        var empty=(await store.ListMediaAsync(scope,p.Id,default))!;var photo=new MediaContentValidator().Validate("description.png","image/png",MediaTestFiles.Png()).Value!;
        var gallery=(await store.ChangeMediaAsync(scope,p.Id,empty.Revision,new("upload",Files:[photo]),default)).Value!;var media=Assert.Single(gallery.Items);
        var capture=new CaptureData{Media=[new(){MediaId=media.Id,Caption=new string('a',200),Room="Sala"}]};
        var saved=await service.SaveAsync(p.Data with{Capture=capture},p.Id,1);Assert.True(saved.IsSuccess,saved.Code);
        var reopened=(await service.FindAsync(p.Id)).Value!;Assert.Equal(200,Assert.Single(reopened.Data.Capture!.Media).Caption!.Length);
        reopened.Data.Capture.Media[0].Caption=new string('b',201);
        Assert.Equal("Capture.DescriptionTooLong",(await service.SaveAsync(reopened.Data,p.Id,reopened.Version)).Code);
        Assert.Equal(gallery.Revision,(await store.ListMediaAsync(scope,p.Id,default))!.Revision);
        Assert.Equal(media,Assert.Single((await store.ListMediaAsync(scope,p.Id,default))!.Items));
    }
    [Fact] public async Task Deleted_identity_is_not_reused_and_counter_cannot_be_tampered_with_by_runtime()
    {
        await using var ds=Source();var org=Guid.NewGuid();var service=Service(ds,org,Guid.NewGuid());
        var p=(await service.SaveAsync(new(Type:"Apartamento",Purpose:"Venda"))).Value!;
        await using var admin=new NpgsqlConnection(Environment.GetEnvironmentVariable("ErpTestAdminConnection"));await admin.OpenAsync();
        await using(var delete=new NpgsqlCommand("DELETE FROM real_estate.properties WHERE id=@id",admin)){delete.Parameters.AddWithValue("id",p.Id);Assert.Equal(1,await delete.ExecuteNonQueryAsync());}
        Assert.Equal("VENAP-2",(await service.SaveAsync(new(Type:"Apartamento",Purpose:"Venda"))).Value!.CommercialCode);
        await using var runtime=await ds.OpenConnectionAsync();
        foreach(var sql in new[]{"SELECT * FROM real_estate.commercial_code_registry","UPDATE real_estate.commercial_code_counters SET last_number=0","SELECT real_estate.assign_commercial_code()"})
        {await using var cmd=new NpgsqlCommand(sql,runtime);Assert.Equal("42501",(await Assert.ThrowsAsync<PostgresException>(()=>cmd.ExecuteNonQueryAsync())).SqlState);}
        Assert.Equal("Resource.NotFound",(await Service(ds,Guid.NewGuid(),Guid.NewGuid()).FindAsync(p.Id)).Code);
    }
    [Fact] public async Task Historical_long_caption_survives_other_edits_but_cannot_be_replaced_by_new_long_caption()
    {
        await using var ds=Source();var org=Guid.NewGuid();var unit=Guid.NewGuid();var scope=new AccessScope(org,"description-fixture","Synthetic",[new(unit,"Unit")],ActiveEstablishmentId:unit);
        var store=new PostgresPropertyStore(ds);var service=Service(ds,org,unit);var p=(await service.SaveAsync(new())).Value!;
        var empty=(await store.ListMediaAsync(scope,p.Id,default))!;var photo=new MediaContentValidator().Validate("legacy.png","image/png",MediaTestFiles.Png()).Value!;
        var gallery=(await store.ChangeMediaAsync(scope,p.Id,empty.Revision,new("upload",Files:[photo]),default)).Value!;var id=gallery.Items[0].Id;
        await using var admin=new NpgsqlConnection(Environment.GetEnvironmentVariable("ErpTestAdminConnection"));await admin.OpenAsync();
        await using(var seed=new NpgsqlCommand("SELECT set_config('erp.organization_id',@org,false);UPDATE real_estate.properties SET capture=@capture::jsonb WHERE id=@id",admin))
        {seed.Parameters.AddWithValue("org",org.ToString());seed.Parameters.AddWithValue("id",p.Id);seed.Parameters.AddWithValue("capture",System.Text.Json.JsonSerializer.Serialize(new CaptureData{Media=[new(){MediaId=id,Caption=new string('l',700)}]},new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)));await seed.ExecuteNonQueryAsync();}
        var read=(await service.FindAsync(p.Id)).Value!;
        var saved=await service.SaveAsync(read.Data with{Title="Another field"},p.Id,read.Version);Assert.True(saved.IsSuccess,saved.Code);Assert.Equal(700,saved.Value!.Data.Capture!.Media[0].Caption!.Length);
        saved.Value.Data.Capture.Media[0].Caption=new string('x',700);
        Assert.Equal("Capture.DescriptionTooLong",(await service.SaveAsync(saved.Value.Data,p.Id,saved.Value.Version)).Code);
        saved.Value.Data.Capture.Media[0].Caption="Nova descrição";
        Assert.True((await service.SaveAsync(saved.Value.Data,p.Id,saved.Value.Version)).IsSuccess);
    }
}
