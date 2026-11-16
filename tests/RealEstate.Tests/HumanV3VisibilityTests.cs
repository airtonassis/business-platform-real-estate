using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using RealEstate.Web;
using Xunit;
namespace RealEstate.Tests;

// Directed Razor/authorization fixture. Separate from real OIDC browser evidence.
public sealed class HumanV3VisibilityTests
{
    [Theory]
    [InlineData("read-only")]
    [InlineData("denied")]
    public async Task Unavailable_actions_are_hidden_and_policy_url_is_denied(string mode)
    {
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../backend/src/RealEstate.Web"));
        await using var app=Program.Build(["--contentRoot",root],b=>{UiFixture.Configure(b);b.WebHost.UseTestServer();});await app.StartAsync();
        using var client=app.GetTestClient();client.DefaultRequestHeaders.Add("X-Ui-Mode",mode);
        var response=await client.GetAsync("/properties/"+UiFixture.PropertyId);var html=await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("data-owner-create",html);Assert.DoesNotContain("href=\"/commercial-policy",html);Assert.DoesNotContain("formaction=\"?handler=ApplyPolicy",html);
        Assert.Equal(System.Net.HttpStatusCode.Forbidden,(await client.GetAsync("/commercial-policy")).StatusCode);
        if(mode=="denied"){Assert.Equal(System.Net.HttpStatusCode.Forbidden,response.StatusCode);Assert.DoesNotContain(">Imóveis</a>",html);}
    }
}
