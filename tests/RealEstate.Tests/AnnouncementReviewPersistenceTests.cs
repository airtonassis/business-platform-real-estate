using Npgsql;
using RealEstate.Domain;
using RealEstate.Application;
using RealEstate.Infrastructure;
using Xunit;
namespace RealEstate.Tests;
[Collection("PostgreSQL")]
public sealed class AnnouncementReviewPersistenceTests
{
 sealed class Authority(AccessScope s):ICoreAccess{public Task<Outcome<AccessScope>> RequireAsync(string r,string p,CancellationToken t)=>Task.FromResult(new Outcome<AccessScope>(s));}
 [Fact]public async Task Reviewed_data_is_created_atomically_and_retry_cannot_overwrite_with_other_data(){
  await using var ds=NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("ConnectionStrings__Erp")!);var unit=Guid.NewGuid();var s=new AccessScope(Guid.NewGuid(),"review-fixture","Synthetic",[new(unit,"Unit")],ActiveEstablishmentId:unit);var store=new PostgresPropertyStore(ds);var props=new PropertyServiceV2(new Authority(s),store);var service=new AnnouncementService(new Authority(s),store,store);
  var p=(await props.SaveAsync(new(Type:"Casa",Purpose:"Venda",Capture:new()))).Value!;var original=Assert.Single((await service.ListAsync(p.Id)).Value!);
  var command=new CreateAnnouncement("Locação",Guid.NewGuid(),original.Id,original.Version,new(new(){Authorized=2500.50m,Conditions="Reviewed",Period="monthly"},123.45m,"annual",321.09m,"monthly"));
  var saved=await service.CreateAsync(p.Id,command);Assert.True(saved.IsSuccess,saved.Code);Assert.Equal(1,saved.Value!.Version);Assert.NotEqual(original.Id,saved.Value.Id);Assert.StartsWith("LOCCAS-",saved.Value.CommercialCode);Assert.Equal(2500.50m,saved.Value.Data.Values!.Authorized);
  Assert.Equal(saved.Value.Id,(await service.CreateAsync(p.Id,command)).Value!.Id);
  Assert.Equal("Announcement.Conflict",(await service.CreateAsync(p.Id,command with{Data=new(new(){Authorized=999m})})).Code);
  Assert.Equal("Announcement.ActiveExists",(await service.CreateAsync(p.Id,command with{RequestId=Guid.NewGuid()})).Code);
  Assert.Equal("Capture.MoneyInvalid",(await service.CreateAsync(p.Id,command with{RequestId=Guid.NewGuid(),Data=new(new(){Authorized=-1m})})).Code);
  var all=(await service.ListAsync(p.Id)).Value!;Assert.Equal(2,all.Count);Assert.Equal(123.45m,all.Single(a=>!a.LegacyAnchor).Data.PropertyTax);Assert.Equal(p.Version,(await props.FindAsync(p.Id)).Value!.Version);
  var page=await store.ListPageAsync(s,new(),default);Assert.Equal(2,page.Announcements![p.Id].Count);Assert.Equal(0,(await store.ListPageAsync(s with{OrganizationId=Guid.NewGuid()},new(),default)).Total);
  Assert.Equal(p.Id,Assert.Single((await store.ListPageAsync(s,new(){Search=saved.Value.CommercialCode},default)).Properties).Id);
  Assert.Equal(p.Id,Assert.Single((await store.ListPageAsync(s,new(){Code=saved.Value.CommercialCode},default)).Properties).Id);
 }
}
