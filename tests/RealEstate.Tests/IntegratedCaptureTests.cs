using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using RealEstate.Domain;
using RealEstate.Web;
using Xunit;
namespace RealEstate.Tests;
public sealed class IntegratedCaptureTests
{
    [Fact] public void Canonical_catalog_is_preserved_but_documentation_and_exchange_have_no_capture_step()
    {
        Assert.Equal(IdentificationRules.Categories,CadastroPresentation.Categories.Select(c=>c.Name));
        foreach(var purpose in new[]{"Venda","Locação","Venda e locação","Permuta"})
        foreach(var id in new[]{8,14})
            Assert.False(CadastroPresentation.IsApplicable(CadastroPresentation.Categories[id-1],new(Purpose:purpose,IsInCondominium:true)));
    }
    [Theory]
    [InlineData(8,7)]
    [InlineData(14,12)]
    public async Task Direct_links_resolve_to_the_nearest_capture_step(int requested,int expected)
    {
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../backend/src/RealEstate.Web"));
        await using var app=Program.Build(["--contentRoot",root],b=>{UiFixture.Configure(b);b.WebHost.UseTestServer();});
        await app.StartAsync();using var client=app.GetTestClient();
        var html=await client.GetStringAsync($"/properties/new?step={requested}");
        Assert.Contains($"id=\"Step\" name=\"Step\" value=\"{expected}\"",html);
        var first=html.IndexOf("id=\"cad-1\"",StringComparison.Ordinal);
        var second=html.IndexOf("id=\"cad-2\"",StringComparison.Ordinal);
        var owner=html.IndexOf("id=\"Input_OwnerId\"",StringComparison.Ordinal);
        Assert.InRange(owner,first,second);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(html,"id=\"Input_OwnerId\""));
    }
}
