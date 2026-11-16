using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using RealEstate.Domain;
using RealEstate.Web;
using Xunit;
namespace RealEstate.Tests;
public sealed class DraftApiTests
{
    [Fact] public async Task Minimum_draft_partial_updates_and_legacy_conflict()
    {
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../backend/src/RealEstate.Web"));
        await using var app=Program.Build(["--contentRoot",root],b=>{UiFixture.Configure(b);b.WebHost.UseTestServer();});
        await app.StartAsync();using var client=app.GetTestClient();
        var response=await client.PostAsJsonAsync("/api/v2/properties",new {});Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var draft=(await response.Content.ReadFromJsonAsync<PropertyV2>())!;
        Assert.Null(draft.Data.OwnerId);Assert.Null(draft.Data.Price);Assert.Equal("Draft",draft.Status);
        var edit=await client.PatchAsJsonAsync("/api/v2/properties/"+draft.Id,new{version=draft.Version,data=new{title="Partial title"}});
        Assert.Equal(HttpStatusCode.OK,edit.StatusCode);
        var changed=(await edit.Content.ReadFromJsonAsync<PropertyV2>())!;Assert.Equal("Partial title",changed.Data.Title);Assert.Null(changed.Data.Price);
        var reopen=await client.GetFromJsonAsync<PropertyV2>("/api/v2/properties/"+draft.Id);Assert.Equal(changed,reopen);
        Assert.Equal(HttpStatusCode.Conflict,(await client.GetAsync("/api/v1/properties/"+draft.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed,(await client.PatchAsJsonAsync("/api/v2/properties/"+draft.Id,new{version=draft.Version,data=new{title="Stale"}})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PatchAsJsonAsync("/api/v2/properties/"+draft.Id,new{version=changed.Version,data=new{createdBy="forged"}})).StatusCode);
    }
    [Fact] public async Task Legacy_update_can_complete_partial_address_without_losing_structured_fields()
    {
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../backend/src/RealEstate.Web"));
        await using var app=Program.Build(["--contentRoot",root],b=>{UiFixture.Configure(b);b.WebHost.UseTestServer();});
        await app.StartAsync();using var client=app.GetTestClient();
        var partial=new PropertyInputV2("V1-COMPLETE","Partial",UiFixture.OwnerId,"Rural","Casa","Venda",3000,new(State:"SP",MunicipalityIbgeCode:"3550308"));
        var created=await client.PostAsJsonAsync("/api/v2/properties",partial);var saved=(await created.Content.ReadFromJsonAsync<PropertyV2>())!;
        var full=new PropertyInput("V1-COMPLETE","Completed",UiFixture.OwnerId,"Rural","Casa","Venda","Manual legacy address","01000000",0,0,3000);
        var updated=await client.PutAsJsonAsync("/api/v1/properties/"+saved.Id,new Program.UpdateProperty(full,saved.Version));Assert.Equal(HttpStatusCode.OK,updated.StatusCode);
        var reopened=(await client.GetFromJsonAsync<PropertyV2>("/api/v2/properties/"+saved.Id))!;Assert.Equal("SP",reopened.Data.Address!.State);Assert.Equal("3550308",reopened.Data.Address.MunicipalityIbgeCode);
    }
    [Fact] public async Task Editor_without_owners_still_offers_draft_and_does_not_accept_unit_assignment()
    {
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../backend/src/RealEstate.Web"));
        await using var app=Program.Build(["--contentRoot",root],b=>{UiFixture.Configure(b);b.WebHost.UseTestServer();});
        await app.StartAsync();using var client=app.GetTestClient();client.DefaultRequestHeaders.Add("X-Ui-Mode","no-owners");
        var html=await client.GetStringAsync("/properties/new");Assert.Contains("property-form",html);
        Assert.DoesNotContain("name=\"Input.EstablishmentId\"",html);Assert.DoesNotMatch("<(input|select)[^>]*\\srequired(=|>|\\s)",html);
    }
}
