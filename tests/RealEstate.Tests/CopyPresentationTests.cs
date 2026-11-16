using System.Net;
using System.Net.Http.Json;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using RealEstate.Domain;
using RealEstate.Web;
using Xunit;
namespace RealEstate.Tests;
// TEST ONLY rendering fixtures; real OIDC and persistence are evidenced separately.
public sealed class CopyPresentationTests
{
 sealed class Announcements:IAnnouncementStore{
  public Guid Id=Guid.NewGuid(),PropertyId;
  public Task<IReadOnlyList<PropertyAnnouncement>> ListAnnouncementsAsync(AccessScope s,Guid p,CancellationToken t)=>Task.FromResult<IReadOnlyList<PropertyAnnouncement>>([new(Id,s.OrganizationId,PropertyId,s.ActiveEstablishmentId!.Value,"Locação","LOCCAS-2","Draft",1,false,null,new(new(){Authorized=2500.50m,Advertised=0,DifferentAdvertised=true,Period="monthly",Conditions="Reviewed rental",Negotiable=true},0,"annual",345.67m,"monthly"),s.Subject,DateTimeOffset.UtcNow)]);
  public Task<Outcome<PropertyAnnouncement>> CreateAnnouncementAsync(AccessScope s,Guid p,CreateAnnouncement c,CancellationToken t)=>throw new NotSupportedException();
  public Task<Outcome<PropertyAnnouncement>> UpdateAnnouncementAsync(AccessScope s,Guid id,long v,AnnouncementInput? d,bool close,CancellationToken t)=>throw new NotSupportedException();
 }
 sealed class Media:IPropertyMediaStore{
  public Guid Id=Guid.NewGuid();
  public Task<MediaCollection?> ListMediaAsync(AccessScope s,Guid p,CancellationToken t)=>Task.FromResult<MediaCollection?>(MediaRules.Collection([new(Id,"image","image/png","photo.png",10,0,true,1)]));
  public Task<MediaContent?> ReadMediaAsync(AccessScope s,Guid p,Guid id,CancellationToken t)=>Task.FromResult<MediaContent?>(null);
  public Task<IReadOnlyDictionary<Guid,Guid>> CoversAsync(AccessScope s,Guid[] p,CancellationToken t)=>Task.FromResult<IReadOnlyDictionary<Guid,Guid>>(new Dictionary<Guid,Guid>());
  public Task<Outcome<MediaCollection>> ChangeMediaAsync(AccessScope s,Guid p,string r,MediaChange c,CancellationToken t)=>throw new NotSupportedException();
 }
 static string Field(string html,string name)=>WebUtility.HtmlDecode(Regex.Match(html,"name=\""+Regex.Escape(name)+"\"[^>]*value=\"([^\"]*)\"").Groups[1].Value);
 [Fact]public async Task Selected_commercial_values_and_hidden_media_metadata_survive_copy_rendering(){
  var source=new Announcements();var media=new Media();var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../backend/src/RealEstate.Web"));
  await using var app=Program.Build(["--contentRoot",root],b=>{UiFixture.Configure(b);b.Services.AddSingleton<IAnnouncementStore>(source);b.Services.AddSingleton<IPropertyMediaStore>(media);b.WebHost.UseTestServer();});await app.StartAsync();using var c=app.GetTestClient();
  var capture=new CaptureData{Values=new(){Sale=new(){Authorized=123456.78m},Rental=new(){Authorized=10.50m},Seasonal=new(){Authorized=null},PropertyTax=0,CondominiumFee=150.50m},Media=[new(){MediaId=media.Id,Caption="Short caption",Room="Sala",Differential="Janela",InternalNotes="Interna",Visibility="internal"}]};
  var created=(await(await c.PostAsJsonAsync("/api/v2/properties",new PropertyInputV2(Type:"Casa",Purpose:"Venda",Price:777.35m,Capture:capture))).Content.ReadFromJsonAsync<PropertyV2>())!;source.PropertyId=created.Id;
  var html=await c.GetStringAsync($"/properties/new?copyFrom={created.Id}&copyAnnouncementId={source.Id}");
  Assert.Equal(2500.50m,decimal.Parse(Field(html,"Input.Capture.Values.Rental.Authorized"),CultureInfo.InvariantCulture));Assert.Equal("0",Field(html,"Input.Capture.Values.Rental.Advertised"));Assert.Contains("value=\"2.500,50\"",html);Assert.Equal("0",Field(html,"Input.Capture.Values.PropertyTax"));Assert.Equal("",Field(html,"Input.Capture.Values.Seasonal.Authorized"));Assert.Equal("123456.78",Field(html,"Input.Capture.Values.Sale.Authorized"));Assert.Equal("777.35",Field(html,"Input.Price"));Assert.Contains("Reviewed rental",html);
  foreach(var pair in new[]{("Room","Sala"),("Differential","Janela"),("InternalNotes","Interna"),("Visibility","internal")}){Assert.Equal(pair.Item2,Field(html,"Input.Capture.Media[0]."+pair.Item1));Assert.Contains("type=\"hidden\"",Regex.Match(html,"<input[^>]*name=\"Input.Capture.Media\\[0\\]."+pair.Item1+"\"[^>]*>").Value);}
  Assert.DoesNotContain("Metadados de mídias já enviadas",html);Assert.DoesNotContain("Visibilidade pretendida",html);Assert.DoesNotContain("Diferencial da foto",html);Assert.Single(Regex.Matches(html,"data-image-description").Cast<Match>());
  Assert.Equal(JsonSerializer.Serialize(created),JsonSerializer.Serialize(await c.GetFromJsonAsync<PropertyV2>("/api/v2/properties/"+created.Id)));
  var original=await c.GetStringAsync($"/properties/{created.Id}?announcementId={source.Id}");Assert.Contains("copyAnnouncementId="+source.Id,original);
  Assert.Equal(HttpStatusCode.NotFound,(await c.GetAsync($"/properties/new?copyFrom={created.Id}&copyAnnouncementId={Guid.NewGuid()}")).StatusCode);
 }
}
