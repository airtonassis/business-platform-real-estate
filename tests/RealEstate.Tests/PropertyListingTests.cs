using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using RealEstate.Domain;
using RealEstate.Web;
using Xunit;
namespace RealEstate.Tests;
public sealed class PropertyListingTests
{
    [Fact]
    public async Task Portfolio_shows_persisted_capture_groups_without_internal_notes()
    {
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../backend/src/RealEstate.Web"));
        await using var app=Program.Build(["--contentRoot",root],b=>{UiFixture.Configure(b);b.WebHost.UseTestServer();});
        await app.StartAsync();using var client=app.GetTestClient();
        var scope=new AccessScope(FixtureProgram.OrganizationA,"test-only-actor","Synthetic",[new(FixtureProgram.OrganizationA,"Unidade de teste")],ActiveEstablishmentId:FixtureProgram.OrganizationA);
        var store=app.Services.GetRequiredService<UiAddressStore>();
        var prior=await store.FindV2Async(scope,UiFixture.PropertyId,default);
        Assert.NotNull(prior);
        var capture=new CaptureData{Areas=new(){Total=84.5m,Unit="m2"},Rooms=new(){Bedrooms=2,Parking=1},
            Values=new(){Sale=new(){Authorized=560000m}},Acquisition=new(){Origin="Indicação",InternalNotes="PRIVATE CAPTURE NOTE"},InspectionNotes="PRIVATE INSPECTION NOTE"};
        var saved=await store.SaveV2Async(scope,prior.Data with{Summary="Descrição sintética",InternalReference="PRIVATE REFERENCE",Capture=capture},prior.Id,prior.Version,default);
        Assert.True(saved.IsSuccess);
        client.DefaultRequestHeaders.Add("X-Fixture-Rights","properties/PROPERTY_READ");
        var page=System.Net.WebUtility.HtmlDecode(await client.GetStringAsync("/portfolio/"+UiFixture.PropertyId));
        Assert.Contains("Área total",page);
        Assert.Contains("84,5",page);
        Assert.Contains("Dormitórios",page);
        Assert.Contains("560.000",page);
        Assert.Contains("Descrição sintética",page);
        Assert.Contains("Indicação",page);
        Assert.DoesNotContain("PRIVATE CAPTURE NOTE",page);
        Assert.DoesNotContain("PRIVATE INSPECTION NOTE",page);
        Assert.DoesNotContain("PRIVATE REFERENCE",page);
    }

    [Fact]
    public async Task Portfolio_detail_is_readable_without_write_right_and_keeps_capture_closed()
    {
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../backend/src/RealEstate.Web"));
        await using var app=Program.Build(["--contentRoot",root],b=>{UiFixture.Configure(b);b.WebHost.UseTestServer();});
        await app.StartAsync();using var client=app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Fixture-Rights","properties/PROPERTY_READ");
        var listing=await client.GetStringAsync("/");
        Assert.Contains("/portfolio/"+UiFixture.PropertyId,listing);
        var response=await client.GetAsync("/portfolio/"+UiFixture.PropertyId);
        var page=await response.Content.ReadAsStringAsync();
        Assert.Equal(200,(int)response.StatusCode);
        Assert.Contains("Synthetic property",page);
        Assert.Contains("Unidade de teste",page);
        Assert.DoesNotContain("Continuar captação",page);
        client.DefaultRequestHeaders.Remove("X-Fixture-Rights");
        client.DefaultRequestHeaders.Add("X-Fixture-Rights","properties/PROPERTY_UPDATE");
        Assert.Equal(403,(int)(await client.GetAsync("/portfolio/"+UiFixture.PropertyId)).StatusCode);
    }

    [Theory]
    [InlineData("?Filter.Search=NO-SUCH-PROPERTY",200,false)]
    [InlineData("?Filter.Code=UI-001",200,true)]
    [InlineData("?Filter.Sort=price;DROP TABLE",400,false)]
    public async Task Web_filters_are_processed_on_server_and_sort_is_allowlisted(string query,int status,bool visible)
    {
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../backend/src/RealEstate.Web"));
        await using var app=Program.Build(["--contentRoot",root],b=>{UiFixture.Configure(b);b.WebHost.UseTestServer();});
        await app.StartAsync();using var client=app.GetTestClient();var response=await client.GetAsync("/"+query);
        var page=await response.Content.ReadAsStringAsync();Assert.Equal(status,(int)response.StatusCode);
        Assert.Equal(visible,page.Contains("data-property ",StringComparison.Ordinal));
    }
}
