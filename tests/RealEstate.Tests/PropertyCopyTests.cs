using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using RealEstate.Domain;
using System.Text.Json;
using Xunit;
namespace RealEstate.Tests;
public sealed class PropertyCopyTests
{
 [Fact]public async Task New_property_keeps_existing_seasonal_purpose_available(){
  await using var app=RealEstate.Web.Program.Build([],b=>{UiFixture.Configure(b);b.WebHost.UseTestServer();});await app.StartAsync();using var c=app.GetTestClient();
  var r=await c.PostAsJsonAsync("/api/v2/properties",new PropertyInputV2(Type:"Apartamento",Purpose:"Temporada"));
  Assert.True(r.IsSuccessStatusCode,await r.Content.ReadAsStringAsync());
 }
 [Fact]public async Task Copy_opens_original_form_with_editable_data_without_creating_a_record(){
  var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../backend/src/RealEstate.Web"));
  await using var app=RealEstate.Web.Program.Build(["--contentRoot",root],b=>{UiFixture.Configure(b);b.WebHost.UseTestServer();});await app.StartAsync();using var c=app.GetTestClient();
  var created=await c.PostAsJsonAsync("/api/v2/properties",new PropertyInputV2(Type:"Casa",Purpose:"Venda",Title:"Copy source",Capture:new(){Condominium=new(){Unit="101"}}));var source=(await created.Content.ReadFromJsonAsync<PropertyV2>())!;
  var count=(await c.GetFromJsonAsync<WorkspaceV2>("/api/v2/properties"))!.Properties.Count;
  var response=await c.GetAsync("/properties/new?copyFrom="+source.Id);var html=await response.Content.ReadAsStringAsync();Assert.True(response.IsSuccessStatusCode,html);Assert.Contains("id=\"property-form\"",html);Assert.Contains("Copy source",html);Assert.Contains("Temporada",html);Assert.Contains("value=\"101\"",html);Assert.DoesNotContain("/announcement-form.js",html);Assert.DoesNotContain("data-shared-notice",html);
  Assert.Equal(JsonSerializer.Serialize(source),JsonSerializer.Serialize(await c.GetFromJsonAsync<PropertyV2>("/api/v2/properties/"+source.Id)));
  Assert.Equal(count,(await c.GetFromJsonAsync<WorkspaceV2>("/api/v2/properties"))!.Properties.Count);
 }
}
