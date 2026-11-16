using System.Text.Json;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using System.Text.RegularExpressions;
using Npgsql;
using RealEstate.Domain;
using RealEstate.Application;
using RealEstate.Infrastructure;
using Xunit;
namespace RealEstate.Tests;
[Collection("PostgreSQL")]
public sealed class AnnouncementTests
{
 [Fact] public async Task Pending_child_codes_are_assigned_when_physical_type_is_filled_without_changing_existing_codes()
 {
  await using var ds=Source();var s=Scope();var props=Properties(ds,s);var p=(await props.SaveAsync(new(Purpose:"Venda"))).Value!;var svc=Service(ds,s);var original=Assert.Single((await svc.ListAsync(p.Id)).Value!);
  var duplicate=(await svc.CreateAsync(p.Id,new("Locação",Guid.NewGuid(),original.Id,original.Version))).Value!;Assert.Null(duplicate.CommercialCode);
  var saved=await props.SaveAsync(p.Data with{Type="Apartamento"},p.Id,p.Version);Assert.True(saved.IsSuccess,saved.Code);
  var all=(await svc.ListAsync(p.Id)).Value!;Assert.Equal("VENAP-1",all.Single(a=>a.Id==original.Id).CommercialCode);Assert.Equal("LOCAP-2",all.Single(a=>a.Id==duplicate.Id).CommercialCode);
  var updated=await props.SaveAsync(saved.Value!.Data with{Type="Casa"},p.Id,saved.Value.Version);Assert.True(updated.IsSuccess,updated.Code);
  Assert.Equal("LOCAP-2",(await svc.ListAsync(p.Id)).Value!.Single(a=>a.Id==duplicate.Id).CommercialCode);
 }
 sealed class FormStore:IAnnouncementStore{
  public Guid PropertyId;public bool Saved;public AnnouncementInput? SavedData;
  public readonly Guid Id=Guid.NewGuid();
  PropertyAnnouncement Item=>new(Id,Guid.NewGuid(),PropertyId,Guid.NewGuid(),"Locação","LOCAP-99","Draft",1,false,null,new(),"fixture",DateTimeOffset.UtcNow);
  public Task<IReadOnlyList<PropertyAnnouncement>> ListAnnouncementsAsync(AccessScope s,Guid p,CancellationToken ct)=>Task.FromResult<IReadOnlyList<PropertyAnnouncement>>([Item]);
  public Task<Outcome<PropertyAnnouncement>> CreateAnnouncementAsync(AccessScope s,Guid p,CreateAnnouncement c,CancellationToken ct)=>throw new NotSupportedException();
  public Task<Outcome<PropertyAnnouncement>> UpdateAnnouncementAsync(AccessScope s,Guid id,long v,AnnouncementInput? d,bool close,CancellationToken ct){Saved=true;SavedData=d;return Task.FromResult(new Outcome<PropertyAnnouncement>(Item with{Data=d??new()}));}
 }
 [Fact] public async Task Rental_form_does_not_require_purpose_for_an_immutable_announcement()
 {
  var store=new FormStore();var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../backend/src/RealEstate.Web"));
  await using var app=RealEstate.Web.Program.Build(["--contentRoot",root],b=>{UiFixture.Configure(b);b.Services.AddSingleton<IAnnouncementStore>(store);b.WebHost.UseTestServer();});await app.StartAsync();using var client=app.GetTestClient();
  var created=(await (await client.PostAsJsonAsync("/api/v2/properties",new PropertyInputV2(Type:"Casa",Purpose:"Venda"))).Content.ReadFromJsonAsync<PropertyV2>())!;store.PropertyId=created.Id;
  var path="/properties/"+created.Id+"/announcements";var page=await client.GetAsync(path);Assert.Equal(HttpStatusCode.OK,page.StatusCode);var html=await page.Content.ReadAsStringAsync();
  var csrf=Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;Assert.NotEmpty(csrf);
  client.DefaultRequestHeaders.Add("Cookie",string.Join("; ",page.Headers.GetValues("Set-Cookie").Select(x=>x.Split(';')[0])));
  var save=await client.PostAsync(path+"?handler=Save",new FormUrlEncodedContent(new Dictionary<string,string>{{"__RequestVerificationToken",csrf},{"AnnouncementId",store.Id.ToString()},{"Version","1"},{"Input.Values.Authorized","100.50"},{"Input.Values.Conditions","Rental"},{"Input.PropertyTax","123.45"},{"Input.CondominiumFee","321.09"},{"Input.PropertyTaxPeriod","annual"}}));
  Assert.Equal(HttpStatusCode.Redirect,save.StatusCode);Assert.True(store.Saved);Assert.Equal(123.45m,store.SavedData!.PropertyTax);Assert.Equal(321.09m,store.SavedData.CondominiumFee);Assert.Equal("annual",store.SavedData.PropertyTaxPeriod);
 }
 [Fact] public async Task Http_v2_purpose_patch_is_rejected_and_new_contract_does_not_accept_purpose_updates()
 {
  var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../backend/src/RealEstate.Web"));
  await using var app=RealEstate.Web.Program.Build(["--contentRoot",root],b=>{UiFixture.Configure(b);b.WebHost.UseTestServer();});await app.StartAsync();using var client=app.GetTestClient();
  var created=await client.PostAsJsonAsync("/api/v2/properties",new PropertyInputV2(Type:"Casa",Purpose:"Venda"));Assert.Equal(HttpStatusCode.OK,created.StatusCode);
  var p=(await created.Content.ReadFromJsonAsync<PropertyV2>())!;
  var changed=await client.PatchAsJsonAsync("/api/v2/properties/"+p.Id,new{version=p.Version,data=new{purpose="Locação"}});Assert.Equal(HttpStatusCode.Conflict,changed.StatusCode);
  Assert.Contains("Announcement.PurposeImmutable",await changed.Content.ReadAsStringAsync());
  var invalid=await client.PutAsJsonAsync("/api/v1/announcements/"+Guid.NewGuid(),new{version=1,purpose="Locação",data=new{}});Assert.Equal(HttpStatusCode.BadRequest,invalid.StatusCode);
 }
 sealed class Authority(AccessScope scope,string? deny=null):ICoreAccess{
  public Task<Outcome<AccessScope>> RequireAsync(string r,string p,CancellationToken t)=>Task.FromResult(p==deny?Outcome<AccessScope>.Fail("Access.Forbidden"):new(scope));
 }
 static NpgsqlDataSource Source()=>NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("ConnectionStrings__Erp")??throw new InvalidOperationException("Isolated database required"));
 static AccessScope Scope(){var unit=Guid.NewGuid();return new(Guid.NewGuid(),"announcement-fixture","Synthetic",[new(unit,"Branch")],ActiveEstablishmentId:unit);}
 static PropertyServiceV2 Properties(NpgsqlDataSource ds,AccessScope s)=>new(new Authority(s),new PostgresPropertyStore(ds));
 static AnnouncementService Service(NpgsqlDataSource ds,AccessScope s,string? deny=null)=>new(new Authority(s,deny),new PostgresPropertyStore(ds),new PostgresPropertyStore(ds));
 [Fact] public async Task Duplicate_has_own_identity_blank_values_same_physical_and_no_copied_policy()
 {
  await using var ds=Source();var s=Scope();var c=new CaptureData();c.Values.Sale.Authorized=1234.56m;c.Values.Sale.Conditions="Sale only";c.Fees.SalePercent=3;
  var p=(await Properties(ds,s).SaveAsync(new(Type:"Casa",Purpose:"Venda",Price:777.35m,Capture:c))).Value!;
  var service=Service(ds,s);var original=Assert.Single((await service.ListAsync(p.Id)).Value!);
  var command=new CreateAnnouncement("Locação",Guid.NewGuid());
  var created=await service.CreateAsync(p.Id,command);Assert.True(created.IsSuccess,created.Code);var rental=created.Value!;
  Assert.NotEqual(original.Id,rental.Id);Assert.NotEqual(original.CommercialCode,rental.CommercialCode);Assert.Equal(p.Id,rental.PropertyId);Assert.Equal(original.Id,rental.SourceAnnouncementId);Assert.Null(rental.Data.Values);
  Assert.StartsWith("LOCCAS-",rental.CommercialCode);Assert.Equal(rental.Id,(await service.CreateAsync(p.Id,command)).Value!.Id);
  var values=new AnnouncementInput(new(){Authorized=100.50m,Advertised=110.75m,Conditions="Rental only",Period="monthly"});
  var saved=await service.UpdateAsync(rental.Id,rental.Version,values);Assert.True(saved.IsSuccess,saved.Code);
  var reopened=(await service.ListAsync(p.Id)).Value!.Single(a=>a.Id==rental.Id);Assert.Equal(100.50m,reopened.Data.Values!.Authorized);Assert.Equal("Rental only",reopened.Data.Values.Conditions);Assert.Equal(rental.CommercialCode,reopened.CommercialCode);
  var physical=(await Properties(ds,s).FindAsync(p.Id)).Value!;Assert.Equal(p.Version,physical.Version);Assert.Equal(p.Id,physical.Id);Assert.Equal(777.35m,physical.Data.Price);Assert.Equal(1234.56m,physical.Data.Capture!.Values.Sale.Authorized);Assert.Null(physical.Data.Capture.Policy);
 }
 [Fact] public async Task Racing_duplicates_have_one_winner_and_database_unique_slot_is_released_only_on_close()
 {
  await using var ds=Source();var s=Scope();var p=(await Properties(ds,s).SaveAsync(new(Type:"Apartamento",Purpose:"Venda"))).Value!;var svc=Service(ds,s);var original=Assert.Single((await svc.ListAsync(p.Id)).Value!);
  var results=await Task.WhenAll(Enumerable.Range(0,8).Select(_=>svc.CreateAsync(p.Id,new("Locação",Guid.NewGuid(),original.Id,original.Version))));
  var rental=Assert.Single(results,r=>r.IsSuccess).Value!;Assert.All(results.Where(r=>!r.IsSuccess),r=>Assert.Equal("Announcement.ActiveExists",r.Code));
  var closed=await svc.UpdateAsync(rental.Id,rental.Version,null,true);Assert.True(closed.IsSuccess,closed.Code);
  Assert.Equal("Announcement.Closed",(await svc.UpdateAsync(rental.Id,closed.Value!.Version,new())).Code);
  var next=await svc.CreateAsync(p.Id,new("Locação",Guid.NewGuid(),original.Id,original.Version));Assert.True(next.IsSuccess,next.Code);Assert.NotEqual(rental.CommercialCode,next.Value!.CommercialCode);
  Assert.Equal(3,(await svc.ListAsync(p.Id)).Value!.Count);Assert.Contains((await svc.ListAsync(p.Id)).Value!,a=>a.Id==rental.Id&&a.Status=="Closed");
 }
 [Theory][InlineData("Locação")][InlineData(null)]
 public async Task Purpose_cannot_be_changed_or_cleared_through_save_patch_or_database(string? purpose)
 {
  await using var ds=Source();var s=Scope();var props=Properties(ds,s);var p=(await props.SaveAsync(new(Type:"Casa",Purpose:"Venda"))).Value!;
  Assert.Equal("Announcement.PurposeImmutable",(await props.SaveAsync(p.Data with{Purpose=purpose},p.Id,p.Version)).Code);
  Assert.Equal("Announcement.PurposeImmutable",(await props.PatchAsync(p.Id,p.Version,JsonSerializer.SerializeToElement(new{purpose}))).Code);
  await using var c=await ds.OpenConnectionAsync();await using var tx=await c.BeginTransactionAsync();
  await using(var context=new NpgsqlCommand("SELECT set_config('erp.organization_id',@org,true),set_config('erp.establishments',@units,true)",c,tx)){context.Parameters.AddWithValue("org",s.OrganizationId.ToString());context.Parameters.AddWithValue("units","{"+s.ActiveEstablishmentId+"}");await context.ExecuteNonQueryAsync();}
  await using var cmd=new NpgsqlCommand("UPDATE real_estate.properties SET purpose=@purpose WHERE id=@id",c,tx);cmd.Parameters.AddWithValue("id",p.Id);cmd.Parameters.AddWithValue("purpose",NpgsqlTypes.NpgsqlDbType.Text,(object?)purpose??DBNull.Value);
  Assert.Equal("property_purpose_immutable",(await Assert.ThrowsAsync<PostgresException>(()=>cmd.ExecuteNonQueryAsync())).ConstraintName);
 }
 [Fact] public async Task Authorization_and_branch_scope_are_revalidated()
 {
  await using var ds=Source();var s=Scope();var p=(await Properties(ds,s).SaveAsync(new(Type:"Casa",Purpose:"Venda"))).Value!;var svc=Service(ds,s);
  Assert.Equal("Access.Forbidden",(await Service(ds,s,"PROPERTY_CREATE").CreateAsync(p.Id,new("Locação",Guid.NewGuid()))).Code);
  Assert.Equal("Resource.NotFound",(await Service(ds,Scope()).ListAsync(p.Id)).Code);
  var other=Guid.NewGuid();var switched=s with{ActiveEstablishmentId=other,Establishments=[..s.Establishments!,new(other,"Other")]};
  Assert.Equal("Access.Forbidden",(await Service(ds,switched).CreateAsync(p.Id,new("Locação",Guid.NewGuid()))).Code);
  var hidden=s with{ActiveEstablishmentId=other,Establishments=[new(other,"Other")]};Assert.Equal("Resource.NotFound",(await Service(ds,hidden).ListAsync(p.Id)).Code);
 }
 [Fact] public async Task Empty_draft_is_allowed_and_unmapped_kitnet_remains_explicitly_pending()
 {
  await using var ds=Source();var s=Scope();var props=Properties(ds,s);var draft=(await props.SaveAsync(new())).Value!;Assert.Empty((await Service(ds,s).ListAsync(draft.Id)).Value!);
  var kitnet=await props.SaveAsync(draft.Data with{Type="Kitnet",Purpose="Venda"},draft.Id,draft.Version);Assert.True(kitnet.IsSuccess,kitnet.Code);Assert.Null(kitnet.Value!.CommercialCode);
  Assert.Null(Assert.Single((await Service(ds,s).ListAsync(draft.Id)).Value!).CommercialCode);
  Assert.Equal("Property.PurposeInvalid",(await props.SaveAsync(new(Type:"Casa",Purpose:"Venda e locação"))).Code);
 }
 [Fact] public async Task Closed_anchor_keeps_history_and_prevents_commercial_overwrite_but_allows_physical_edit()
 {
  await using var ds=Source();var s=Scope();var props=Properties(ds,s);var p=(await props.SaveAsync(new(Type:"Casa",Purpose:"Venda"))).Value!;var svc=Service(ds,s);var a=Assert.Single((await svc.ListAsync(p.Id)).Value!);
  Assert.True((await svc.UpdateAsync(a.Id,a.Version,null,true)).IsSuccess);
  var changed=p.Data with{Summary="Physical description",Capture=new()};Assert.True((await props.SaveAsync(changed,p.Id,p.Version)).IsSuccess);
  var current=(await props.FindAsync(p.Id)).Value!;current.Data.Capture!.Values.Sale.Authorized=123m;
  var result=await props.SaveAsync(current.Data,p.Id,current.Version);Assert.Equal("Announcement.Closed",result.Code);
 }
 [Fact] public async Task Existing_combined_listing_can_have_additional_exclusive_announcements_without_conversion()
 {
  await using var ds=Source();var unit=Guid.Parse("33333333-3333-3333-3333-333333333333");
  var s=new AccessScope(Guid.Parse("11111111-1111-1111-1111-111111111111"),"legacy-fixture","Synthetic",[new(unit,"Legacy")],ActiveEstablishmentId:unit);
  var id=Guid.Parse("22222222-2222-2222-2222-222222222222");var svc=Service(ds,s);
  Assert.True((await svc.CreateAsync(id,new("Venda",Guid.NewGuid()))).IsSuccess);
  Assert.True((await svc.CreateAsync(id,new("Locação",Guid.NewGuid()))).IsSuccess);
  var original=(await Properties(ds,s).FindAsync(id)).Value!;Assert.Equal("Venda e locação",original.Data.Purpose);Assert.Equal(777.35m,original.Data.Price);
 }
 [Fact] public async Task Ledger_is_reserved_history_is_append_only_and_announcements_do_not_leak_through_direct_sql()
 {
  await using var ds=Source();var s=Scope();var p=(await Properties(ds,s).SaveAsync(new(Type:"Casa",Purpose:"Venda"))).Value!;var svc=Service(ds,s);var a=Assert.Single((await svc.ListAsync(p.Id)).Value!);
  var r=(await svc.CreateAsync(p.Id,new("Locação",Guid.NewGuid(),a.Id,a.Version))).Value!;
  await using var c=await ds.OpenConnectionAsync();
  foreach(var sql in new[]{"DELETE FROM real_estate.announcement_history","UPDATE real_estate.announcement_history SET actor='forged'","UPDATE real_estate.commercial_code_counters SET last_number=0"}){
   await using var cmd=new NpgsqlCommand(sql,c);Assert.Equal("42501",(await Assert.ThrowsAsync<PostgresException>(()=>cmd.ExecuteNonQueryAsync())).SqlState);
  }
  await using var tx=await c.BeginTransactionAsync();
  await using(var context=new NpgsqlCommand("SELECT set_config('erp.organization_id',@org,true),set_config('erp.establishments','{}',true)",c,tx)){context.Parameters.AddWithValue("org",s.OrganizationId.ToString());await context.ExecuteNonQueryAsync();}
  await using(var query=new NpgsqlCommand("SELECT count(*) FROM real_estate.property_announcements WHERE id=@id",c,tx)){query.Parameters.AddWithValue("id",r.Id);Assert.Equal(0L,await query.ExecuteScalarAsync());}
 }
}
