using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using RealEstate.Domain;
using RealEstate.Web;
using Xunit;
namespace RealEstate.Tests;
public sealed class CadReviewTests
{
    [Theory][InlineData("s/n",false,null,true)][InlineData("S/N",false,null,true)][InlineData("10",true,"10",false)]
    public async Task Single_number_editor_maps_to_existing_contract(string entered,bool oldFlag,string? expectedNumber,bool expectedFlag)
    {
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../backend/src/RealEstate.Web"));
        await using var app=Program.Build(["--contentRoot",root],b=>{UiFixture.Configure(b);b.WebHost.UseTestServer();});
        await app.StartAsync();using var client=app.GetTestClient();
        var page=await client.GetAsync("/properties/new");var html=await page.Content.ReadAsStringAsync();
        var token=Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        client.DefaultRequestHeaders.Add("Cookie",string.Join("; ",page.Headers.GetValues("Set-Cookie").Select(c=>c.Split(';')[0])));
        var post=await client.PostAsync("/properties/new",new FormUrlEncodedContent(new Dictionary<string,string>{{"__RequestVerificationToken",WebUtility.HtmlDecode(token)},{"Input.Address.Number",entered},{"Input.Address.WithoutNumber",oldFlag.ToString()}}));
        Assert.Equal(HttpStatusCode.Redirect,post.StatusCode);
        var id=post.Headers.Location!.OriginalString.Split('/').Last();
        var saved=(await client.GetFromJsonAsync<PropertyV2>("/api/v2/properties/"+id))!;
        Assert.Equal(expectedNumber,saved.Data.Address!.Number);Assert.Equal(expectedFlag,saved.Data.Address.WithoutNumber);
    }
}
