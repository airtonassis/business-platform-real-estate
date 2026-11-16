using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using RealEstate.Domain;
using RealEstate.Web;
using Xunit;
namespace RealEstate.Tests;

// TEST ONLY Razor/CSRF fixture. Real hom-owner journey and PG are separate evidence.
public sealed class AnnouncementNavigationTests
{
 sealed class Store:IAnnouncementStore{
  public Guid PropertyId;public Guid SourceId=Guid.NewGuid();public int Writes;public bool BothActive;public CreateAnnouncement? Command;
  public PropertyAnnouncement Source=>new(SourceId,FixtureProgram.OrganizationA,PropertyId,FixtureProgram.OrganizationA,"Venda","VENCAS-91","Draft",3,true,null,new(new(){Authorized=123456.78m,Conditions="Sale terms"},123.45m,"annual",321.09m,"monthly",true,true,"Sale exchange"),"fixture",DateTimeOffset.UtcNow);
  public Task<IReadOnlyList<PropertyAnnouncement>> ListAnnouncementsAsync(AccessScope s,Guid p,CancellationToken t)=>Task.FromResult<IReadOnlyList<PropertyAnnouncement>>(BothActive?[Source,Source with{Id=Guid.NewGuid(),Purpose="Locação",LegacyAnchor=false}]:[Source]);
  public Task<Outcome<PropertyAnnouncement>> CreateAnnouncementAsync(AccessScope s,Guid p,CreateAnnouncement c,CancellationToken t){Writes++;Command=c;return Task.FromResult(new Outcome<PropertyAnnouncement>(Source with{Id=Guid.NewGuid(),Purpose=c.Purpose,CommercialCode="LOCCAS-92",LegacyAnchor=false,Data=c.Data??new(),SourceAnnouncementId=SourceId}));}
  public Task<Outcome<PropertyAnnouncement>> UpdateAnnouncementAsync(AccessScope s,Guid id,long v,AnnouncementInput? d,bool close,CancellationToken t)=>throw new NotSupportedException();
 }
 static async Task<(Microsoft.AspNetCore.Builder.WebApplication App,HttpClient Client,Store Store)> Host(){
  var store=new Store();var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../backend/src/RealEstate.Web"));
  var app=Program.Build(["--contentRoot",root],b=>{UiFixture.Configure(b);b.Services.AddSingleton<IAnnouncementStore>(store);b.WebHost.UseTestServer();});await app.StartAsync();var client=app.GetTestClient();
  var p=(await(await client.PostAsJsonAsync("/api/v2/properties",new PropertyInputV2(Type:"Casa",Purpose:"Venda"))).Content.ReadFromJsonAsync<PropertyV2>())!;store.PropertyId=p.Id;return(app,client,store);
 }
 static string Field(string html,string name)=>System.Net.WebUtility.HtmlDecode(Regex.Match(html,"name=\""+Regex.Escape(name)+"\"[^>]*value=\"([^\"]*)\"").Groups[1].Value);
 static async Task<(string Path,string Html)> Open(HttpClient c,Store s){var path="/properties/new?sourcePropertyId="+s.PropertyId+"&announcementId="+s.SourceId;var page=await c.GetAsync(path);Assert.Equal(HttpStatusCode.OK,page.StatusCode);c.DefaultRequestHeaders.Remove("Cookie");c.DefaultRequestHeaders.Add("Cookie",string.Join("; ",page.Headers.GetValues("Set-Cookie").Select(x=>x.Split(';')[0])));return(path,await page.Content.ReadAsStringAsync());}
 [Fact]public async Task Opening_review_prefills_compatible_data_and_cancel_does_not_create_anything(){
  var(app,c,s)=await Host();await using var lifetime=app;using var client=c;var(path,html)=await Open(c,s);
  Assert.Contains("id=\"property-form\"",html);Assert.Contains("data-shared-notice",html);Assert.Contains("Input.Type",html);Assert.Contains("cad-4",html);Assert.DoesNotContain("duplicate-announcement-form",html);Assert.Equal("123.45",Field(html,"Input.Capture.Values.PropertyTax"));Assert.Equal("321.09",Field(html,"Input.Capture.Values.CondominiumFee"));Assert.Equal("",Field(html,"Input.Capture.Values.Rental.Authorized"));Assert.Contains("Input.Purpose",html);Assert.Equal("3",Field(html,"SourceVersion"));Assert.Equal(0,s.Writes);
  await c.GetAsync("/properties/"+s.PropertyId+"/announcements");Assert.Equal(0,s.Writes);
 }
 [Fact]public async Task Save_creates_once_through_existing_command_with_reviewed_fields_and_origin(){
  var(app,c,s)=await Host();await using var lifetime=app;using var client=c;var(path,html)=await Open(c,s);
  var form=new Dictionary<string,string>{{"SourcePropertyId",s.PropertyId.ToString()},{"__RequestVerificationToken",Field(html,"__RequestVerificationToken")},{"AnnouncementId",s.SourceId.ToString()},{"SourceVersion","3"},{"RequestId",Field(html,"RequestId")},{"Input.Purpose","Locação"},{"Input.Capture.Values.Rental.Authorized","2500.50"},{"Input.Capture.Values.Rental.Conditions","Rental reviewed"},{"Input.Capture.Values.Rental.Period","monthly"},{"Input.Capture.Values.PropertyTax","123.45"},{"Input.Capture.Values.CondominiumFee","321.09"}};
  var result=await c.PostAsync(path,new FormUrlEncodedContent(form));Assert.Equal(HttpStatusCode.Redirect,result.StatusCode);Assert.Equal(1,s.Writes);Assert.Equal(s.SourceId,s.Command!.SourceAnnouncementId);Assert.Equal(2500.50m,s.Command.Data!.Values!.Authorized);Assert.Equal(123.45m,s.Command.Data.PropertyTax);Assert.Null(s.Command.Data.Financing);Assert.Equal("/",result.Headers.Location!.ToString());
 }
 [Fact]public async Task Review_requires_create_permission_and_blocks_occupied_destination_without_writing(){
  var(app,c,s)=await Host();await using var lifetime=app;using var client=c;
  c.DefaultRequestHeaders.Add("X-Ui-Mode","read-only");Assert.Equal(HttpStatusCode.Forbidden,(await c.GetAsync("/properties/new?sourcePropertyId="+s.PropertyId+"&announcementId="+s.SourceId)).StatusCode);c.DefaultRequestHeaders.Remove("X-Ui-Mode");s.BothActive=true;
  Assert.Equal(HttpStatusCode.OK,(await c.GetAsync("/properties/new?sourcePropertyId="+s.PropertyId+"&announcementId="+s.SourceId)).StatusCode);Assert.Equal(0,s.Writes);
 }
 [Fact]public async Task Old_immediate_create_action_now_only_redirects_to_review(){
  var(app,c,s)=await Host();await using var lifetime=app;using var client=c;var(_,html)=await Open(c,s);
  var response=await c.PostAsync("/properties/"+s.PropertyId+"/announcements?handler=Create",new FormUrlEncodedContent(new Dictionary<string,string>{{"__RequestVerificationToken",Field(html,"__RequestVerificationToken")},{"AnnouncementId",s.SourceId.ToString()},{"SourceVersion","3"},{"Input.Purpose","Locação"}}));
  Assert.Equal(HttpStatusCode.Redirect,response.StatusCode);Assert.Contains("/duplicate",response.Headers.Location!.ToString());Assert.Equal(0,s.Writes);
 }
 [Fact]public void Review_never_classifies_original_prices_or_copies_sale_exclusive_fields(){
  var review=AnnouncementRules.ReviewFrom(new(new(){Authorized=100000m,Conditions="Sale"},12.34m,"annual",56.78m,"monthly",true,true,"Exchange"));
  Assert.Null(review.Values);Assert.Null(review.Financing);Assert.Null(review.AcceptsExchange);Assert.Null(review.ExchangeNotes);Assert.Equal(12.34m,review.PropertyTax);Assert.Equal(56.78m,review.CondominiumFee);
 }
 [Fact]public async Task Occupied_purpose_is_rejected_in_original_form_without_any_write(){
  var(app,c,s)=await Host();await using var lifetime=app;using var client=c;var(path,html)=await Open(c,s);s.BothActive=true;
  var response=await c.PostAsync(path,new FormUrlEncodedContent(new Dictionary<string,string>{{"__RequestVerificationToken",Field(html,"__RequestVerificationToken")},{"SourcePropertyId",s.PropertyId.ToString()},{"AnnouncementId",s.SourceId.ToString()},{"SourceVersion","3"},{"RequestId",Field(html,"RequestId")},{"Input.Purpose","Locação"}}));
  Assert.True(response.StatusCode==HttpStatusCode.Conflict,$"Status={response.StatusCode}; location={response.Headers.Location}; writes={s.Writes}; purpose={s.Command?.Purpose}");Assert.Contains("an&#xFA;ncio ativo",await response.Content.ReadAsStringAsync());Assert.Equal(0,s.Writes);
 }
}
