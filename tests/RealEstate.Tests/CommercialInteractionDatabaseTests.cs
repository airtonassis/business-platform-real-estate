using System.Text.Json;
using Npgsql;
using RealEstate.Application;
using RealEstate.Domain;
using RealEstate.Infrastructure;
using Xunit;
namespace RealEstate.Tests;
[Collection("PostgreSQL")]
public sealed class CommercialInteractionDatabaseTests
{
    private sealed class Authority(AccessScope scope):ICoreAccess
    {
        public string? Denied;
        public Task<Outcome<AccessScope>> RequireAsync(string r,string p,CancellationToken t)=>Task.FromResult(p==Denied?Outcome<AccessScope>.Fail("Access.Forbidden"):new Outcome<AccessScope>(scope));
    }
    private static AccessScope Scope(){var unit=Guid.NewGuid();return new(Guid.NewGuid(),"test-only-actor","Synthetic",[new(unit,"Unit")],LegacyManager:true,ActiveEstablishmentId:unit);}
    private static async Task<PropertyV2> Property(PropertyServiceV2 service,PropertyService legacy,string title,string type="Apartamento",string purpose="Locação",decimal amount=3000,string unit="m2",decimal area=80){
        var capture=new CaptureData{Areas=new(){Built=area,Unit=unit},Rooms=new(){Bedrooms=2,Parking=1}};
        if(purpose=="Venda")capture.Values.Sale.Authorized=amount;else capture.Values.Rental.Authorized=amount;
        var owner=(await legacy.CreateOwnerAsync("Synthetic owner")).Value!;
        var result=await service.SaveAsync(new(Code:"FIX-"+Guid.NewGuid().ToString("N")[..10],Type:type,Purpose:purpose,Title:title,OwnerId:owner.Id,Category:IdentificationRules.IsRural(type)?"Rural":"Residencial",Price:999999,Address:new("Synthetic address","12345678",Latitude:1,Longitude:1),Capture:capture));Assert.True(result.IsSuccess,result.Code);return result.Value!;
    }
    private static async Task<PropertyV2> Approve(PropertyServiceV2 service,PropertyV2 draft)
    {
        var sent=await service.SubmitForReviewAsync(draft.Id,draft.Version);Assert.True(sent.IsSuccess,sent.Code);
        var approved=await service.ApproveAsync(draft.Id,sent.Value!.Version);Assert.True(approved.IsSuccess,approved.Code);return approved.Value!;
    }
    private static async Task<PropertyV2> CompleteAndApprove(PropertyServiceV2 service,PropertyService legacy,PropertyV2 draft)
    {
        var owner=(await legacy.CreateOwnerAsync("Synthetic owner")).Value!;
        var complete=await service.SaveAsync(draft.Data with{Code="FIX-"+Guid.NewGuid().ToString("N")[..10],Title="Synthetic",OwnerId=owner.Id,Category="Residential",Type="Apartamento",Purpose="Venda",Price=100,Address=new("Synthetic address","12345678",Latitude:1,Longitude:1)},draft.Id,draft.Version);
        Assert.True(complete.IsSuccess,complete.Code);return await Approve(service,complete.Value!);
    }
    private static async Task<Guid> Customer(CustomerService service,string name="Synthetic customer"){
        var result=await service.SaveAsync(new(name,"Física","11999998888",null,"Site","Locação","test-only-actor"));Assert.True(result.IsSuccess,result.Code);return result.Value!.Id;
    }
    private static async Task<string> PropertyFingerprint(Guid org){
        await using var c=new NpgsqlConnection(Environment.GetEnvironmentVariable("ErpTestAdminConnection"));await c.OpenAsync();
        await using var cmd=new NpgsqlCommand("SELECT coalesce(string_agg(md5(row_to_json(p)::text),',' ORDER BY p.id),'') FROM real_estate.properties p WHERE organization_id=@org",c);cmd.Parameters.AddWithValue("org",org);return (string)(await cmd.ExecuteScalarAsync())!;
    }
    [Fact]public async Task Three_properties_have_individual_feedback_from_one_origin_and_original_records_do_not_change(){
        await using var ds=NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("ConnectionStrings__Erp")!);var scope=Scope();var auth=new Authority(scope);var store=new PostgresPropertyStore(ds);var properties=new PropertyServiceV2(auth,store);var customers=new CustomerService(auth,store,new CustomerTestDirectory());var crm=new CommercialInteractionService(auth,store,store);
        var customer=await Customer(customers);var items=new[]{await Approve(properties,await Property(properties,new PropertyService(auth,store),"Price")),await Approve(properties,await Property(properties,new PropertyService(auth,store),"Pool")),await Approve(properties,await Property(properties,new PropertyService(auth,store),"Parking"))};var before=items.ToDictionary(p=>p.Id,p=>JsonSerializer.Serialize(p));var rawBefore=await PropertyFingerprint(scope.OrganizationId);
        var presented=await crm.PresentAsync(customer,new(items.Select(p=>p.Id).ToArray(),"WhatsApp","Initial presentation"));Assert.True(presented.IsSuccess,presented.Code);Assert.Equal(3,presented.Value!.Count);Assert.Single(presented.Value.Select(i=>i.ContactId).Distinct());Assert.All(presented.Value,i=>Assert.Equal(CommercialInteractionRules.Presented,i.Status));
        var categories=new[]{"Preço e condições","Condomínio e infraestrutura","Garagem e estacionamento"};var reasons=new[]{"Valor acima do orçamento","Condomínio sem piscina","Imóvel sem garagem"};
        for(var index=0;index<3;index++){var root=presented.Value.Single(i=>i.PropertyId==items[index].Id);Assert.True((await crm.FeedbackAsync(customer,new(root.PresentationId,"Sem interesse",categories[index],reasons[index],null,"Telefone"))).IsSuccess);}
        var customerHistory=(await crm.CustomerHistoryAsync(customer)).Value!;Assert.Equal(6,customerHistory.Count);
        for(var index=0;index<3;index++){
            var history=(await crm.PropertyHistoryAsync(items[index].Id)).Value!;Assert.Equal(2,history.Count);Assert.Contains(history,i=>i.Reason==reasons[index]);Assert.Equal(customerHistory.Where(i=>i.PropertyId==items[index].Id).Select(i=>i.Id),history.Select(i=>i.Id));Assert.Equal(before[items[index].Id],JsonSerializer.Serialize((await properties.FindAsync(items[index].Id)).Value));
        }
        var file=(await customers.FindAsync(customer)).Value!;Assert.Equal(4,file.Contacts.Count);Assert.All(customerHistory,i=>Assert.Contains(file.Contacts,c=>c.Id==i.ContactId));
        var rootA=presented.Value[0];Assert.True((await crm.FeedbackAsync(customer,new(rootA.PresentationId,"Visitado",null,null,null,"Visita"))).IsSuccess);Assert.True((await crm.FeedbackAsync(customer,new(rootA.PresentationId,"Demonstrou interesse",null,null,null))).IsSuccess);
        var summary=(await crm.SummaryAsync(rootA.PropertyId,DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow.AddDays(1))).Value!;Assert.Equal(1,summary.UniqueCustomers);Assert.Equal(1,summary.Presentations);Assert.Equal(1,summary.VisitRecords);Assert.Equal(1,summary.InterestedCustomers);
        var json=JsonSerializer.Serialize(summary);Assert.DoesNotContain("Synthetic customer",json);Assert.DoesNotContain("11999998888",json);Assert.DoesNotContain("test-only-actor",json);Assert.DoesNotContain("Valor acima",json);
        Assert.True((await crm.PresentAsync(customer,new([rootA.PropertyId],"E-mail",null))).IsSuccess);summary=(await crm.SummaryAsync(rootA.PropertyId,DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow.AddDays(1))).Value!;Assert.Equal(1,summary.UniqueCustomers);Assert.Equal(2,summary.Presentations);Assert.Equal(0,summary.InterestedCustomers);
        Assert.Equal(rawBefore,await PropertyFingerprint(scope.OrganizationId));
    }
    [Fact]public async Task Scope_permissions_batch_atomicity_and_immutable_history_are_enforced(){
        await using var ds=NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("ConnectionStrings__Erp")!);var scope=Scope();var auth=new Authority(scope);var store=new PostgresPropertyStore(ds);var properties=new PropertyServiceV2(auth,store);var customers=new CustomerService(auth,store,new CustomerTestDirectory());var crm=new CommercialInteractionService(auth,store,store);var customer=await Customer(customers);var property=await Approve(properties,await Property(properties,new PropertyService(auth,store),"Scoped"));
        Assert.Equal("Commercial.PropertyUnavailable",(await crm.PresentAsync(customer,new([property.Id,Guid.NewGuid()],"Telefone",null))).Code);Assert.Empty((await customers.FindAsync(customer)).Value!.Contacts);
        auth.Denied="CUSTOMER_PROPERTY_WRITE";Assert.Equal("Access.Forbidden",(await crm.PresentAsync(customer,new([property.Id],"Telefone",null))).Code);auth.Denied=null;
        var root=(await crm.PresentAsync(customer,new([property.Id],"Telefone",null))).Value!.Single();
        auth.Denied="PROPERTY_COMMERCIAL_HISTORY_READ";Assert.Equal("Access.Forbidden",(await crm.PropertyHistoryAsync(property.Id)).Code);Assert.True((await crm.SummaryAsync(property.Id,DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow)).IsSuccess);auth.Denied=null;
        var unrelated=new CommercialInteractionService(new Authority(scope with{Subject="unrelated-user",LegacyManager=false}),store,store);Assert.Equal("Access.Forbidden",(await unrelated.SummaryAsync(property.Id,DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow)).Code);
        var foreignScope=scope with{OrganizationId=Guid.NewGuid()};var foreign=new CommercialInteractionService(new Authority(foreignScope),store,store);Assert.Equal("Resource.NotFound",(await foreign.CustomerHistoryAsync(customer)).Code);Assert.Equal("Resource.NotFound",(await foreign.FeedbackAsync(customer,new(root.PresentationId,"Visitado",null,null,null))).Code);
        var otherUnit=Guid.NewGuid();foreign=new CommercialInteractionService(new Authority(scope with{ActiveEstablishmentId=otherUnit,Establishments=[new(otherUnit,"Other")]}),store,store);Assert.Equal("Resource.NotFound",(await foreign.PropertyHistoryAsync(property.Id)).Code);
        await using var admin=new NpgsqlConnection(Environment.GetEnvironmentVariable("ErpTestAdminConnection"));await admin.OpenAsync();
        try{await using(var revoke=new NpgsqlCommand("REVOKE INSERT ON real_estate.events FROM erp_runtime",admin))await revoke.ExecuteNonQueryAsync();await Assert.ThrowsAsync<PostgresException>(()=>crm.FeedbackAsync(customer,new(root.PresentationId,"Visitado",null,null,null)));await Assert.ThrowsAsync<PostgresException>(()=>crm.PresentAsync(customer,new([property.Id],"Telefone",null)));}
        finally{await using var restore=new NpgsqlCommand("GRANT INSERT ON real_estate.events TO erp_runtime",admin);await restore.ExecuteNonQueryAsync();}
        Assert.Single((await crm.CustomerHistoryAsync(customer)).Value!);Assert.Single((await customers.FindAsync(customer)).Value!.Contacts);
        await using var connection=await ds.OpenConnectionAsync();await using var tx=await connection.BeginTransactionAsync();
        await using(var context=new NpgsqlCommand("SELECT set_config('erp.organization_id',@org,true),set_config('erp.active_establishment',@unit,true),set_config('erp.establishments',@units,true)",connection,tx)){context.Parameters.AddWithValue("org",scope.OrganizationId.ToString());context.Parameters.AddWithValue("unit",scope.ActiveEstablishmentId.ToString()!);context.Parameters.AddWithValue("units","{"+scope.ActiveEstablishmentId+"}");await context.ExecuteNonQueryAsync();}
        await using(var mutation=new NpgsqlCommand("UPDATE real_estate.commercial_interactions SET status='Visitado' WHERE id=@id",connection,tx)){mutation.Parameters.AddWithValue("id",root.Id);var error=await Assert.ThrowsAsync<PostgresException>(()=>mutation.ExecuteNonQueryAsync());Assert.Equal("42501",error.SqlState);}await tx.RollbackAsync();
        await using(var bare=new NpgsqlCommand("SELECT (SELECT count(*) FROM real_estate.commercial_presentations)+(SELECT count(*) FROM real_estate.commercial_interactions)",connection)){Assert.Equal(0L,await bare.ExecuteScalarAsync());}
    }
    [Fact]public async Task Commercial_search_reuses_listing_with_operation_values_and_typed_rural_filters(){
        await using var ds=NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("ConnectionStrings__Erp")!);var scope=Scope();var auth=new Authority(scope);var store=new PostgresPropertyStore(ds);var properties=new PropertyServiceV2(auth,store);var crm=new CommercialInteractionService(auth,store,store);var customer=await Customer(new(auth,store,new CustomerTestDirectory()));
        var flat=await Property(properties,new PropertyService(auth,store),"Urban","Apartamento","Locação",2500);var farm=await Property(properties,new PropertyService(auth,store),"Rural","Fazenda","Venda",500000,"ha",20);var commercial=await Property(properties,new PropertyService(auth,store),"Commercial","Sala Comercial","Locação",4000);
        flat=await Approve(properties,flat);farm=await Approve(properties,farm);commercial=await Approve(properties,commercial);
        Assert.Equal(flat.Id,Assert.Single((await crm.SearchAsync(customer,new(){Purpose="Locação",MaxPrice=3000})).Value!.Properties).Id);
        Assert.Equal(farm.Id,Assert.Single((await crm.SearchAsync(customer,new(){Type="Fazenda",MinArea=10,MaxArea=30,AreaKind="built",AreaUnit="ha"})).Value!.Properties).Id);
        Assert.Empty((await crm.SearchAsync(customer,new(){Type="Fazenda",MinArea=10,MaxArea=30,AreaKind="built",AreaUnit="m2"})).Value!.Properties);
        Assert.Equal("Listing.InvalidQuery",(await crm.SearchAsync(customer,new(){MinArea=10})).Code);
        Assert.Equal(commercial.Id,Assert.Single((await crm.SearchAsync(customer,new(){Type="Sala Comercial",MinBedrooms=2,MinParking=1})).Value!.Properties).Id);
        Assert.Equal(3,(await crm.SearchAsync(customer,new())).Value!.Total);
        var updated=JsonSerializer.Deserialize<PropertyInputV2>(JsonSerializer.Serialize(flat.Data))!;updated.Capture!.Values.Rental.DifferentAdvertised=true;updated.Capture.Values.Rental.Advertised=4500;
        Assert.Equal("Property.StatusConflict",(await properties.SaveAsync(updated,flat.Id,flat.Version)).Code);Assert.Single((await crm.SearchAsync(customer,new(){Purpose="Locação",MaxPrice=3000})).Value!.Properties);
    }
    [Fact]public async Task Characteristic_search_matches_effective_condominium_inheritance_and_explicit_exceptions(){
        await using var ds=NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("ConnectionStrings__Erp")!);var scope=Scope();var auth=new Authority(scope);var store=new PostgresPropertyStore(ds);var properties=new PropertyServiceV2(auth,store);var crm=new CommercialInteractionService(auth,store,store);var customer=await Customer(new(auth,store,new CustomerTestDirectory()));
        var condos=new CondominiumService(auth,store);var condo=(await condos.CreateAsync(new("Pool condo","mixed"))).Value!;
        var shared=(await condos.UpdateAsync(condo.Id,condo.Version,condo.Data with{Characteristics=[new(){GroupId="collective-leisure",OptionId="pool",Selected=true}]})).Value!;
        var inherited=(await properties.SaveAsync(new(Capture:new(){Condominium=new(){Id=condo.Id,Inherit=true,ObservedVersion=shared.Version}}))).Value!;
        var excluded=(await properties.SaveAsync(new(Capture:new(){Condominium=new(){Id=condo.Id,Inherit=true,ObservedVersion=shared.Version},Selections=[new(){GroupId="collective-leisure",OptionId="pool",SharedOverride=false}]}))).Value!;
        var explicitYes=(await properties.SaveAsync(new(Capture:new(){Condominium=new(){Id=condo.Id,Inherit=true,ObservedVersion=shared.Version},Selections=[new(){GroupId="collective-services",OptionId="elevator",SharedOverride=true}]}))).Value!;
        inherited=await CompleteAndApprove(properties,new PropertyService(auth,store),inherited);excluded=await CompleteAndApprove(properties,new PropertyService(auth,store),excluded);explicitYes=await CompleteAndApprove(properties,new PropertyService(auth,store),explicitYes);
        Assert.Equal(new[]{inherited.Id,explicitYes.Id}.Order(),(await crm.SearchAsync(customer,new(){FeatureGroup="collective-leisure",FeatureOption="pool"})).Value!.Properties.Select(p=>p.Id).Order());
        Assert.Equal(explicitYes.Id,Assert.Single((await crm.SearchAsync(customer,new(){FeatureGroup="collective-services",FeatureOption="elevator"})).Value!.Properties).Id);
    }
}
