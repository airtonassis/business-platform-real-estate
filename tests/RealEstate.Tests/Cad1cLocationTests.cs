using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using RealEstate.Web;
using Xunit;
namespace RealEstate.Tests;
public sealed class Cad1cLocationTests
{
    [Fact] public async Task Saving_location_keeps_the_same_step_for_progressive_editing()
    {
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../backend/src/RealEstate.Web"));
        await using var app=Program.Build(["--contentRoot",root],b=>{UiFixture.Configure(b);b.WebHost.UseTestServer();});
        await app.StartAsync();using var client=app.GetTestClient();
        var response=await client.GetAsync("/properties/new?step=2");var html=await response.Content.ReadAsStringAsync();
        var token=Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        client.DefaultRequestHeaders.Add("Cookie",string.Join("; ",response.Headers.GetValues("Set-Cookie").Select(c=>c.Split(';')[0])));
        var post=await client.PostAsync("/properties/new",new FormUrlEncodedContent(new Dictionary<string,string>{{"__RequestVerificationToken",WebUtility.HtmlDecode(token)},{"Step","2"}}));
        Assert.Equal(HttpStatusCode.Redirect,post.StatusCode);
        Assert.Contains("step=2",post.Headers.Location!.OriginalString,StringComparison.OrdinalIgnoreCase);
    }
}
