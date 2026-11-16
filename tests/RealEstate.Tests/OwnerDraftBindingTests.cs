using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using RealEstate.Web;
using Xunit;
namespace RealEstate.Tests;
// TEST ONLY Razor binding regression, not an OIDC or PostgreSQL proof.
public sealed class OwnerDraftBindingTests
{
 [Fact] public async Task Normal_new_draft_does_not_require_copy_gallery_revision()
 {
  var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../backend/src/RealEstate.Web"));
  await using var app=Program.Build(["--contentRoot",root],b=>{UiFixture.Configure(b);b.WebHost.UseTestServer();});await app.StartAsync();using var c=app.GetTestClient();
  var response=await c.GetAsync("/properties/new");var html=await response.Content.ReadAsStringAsync();
  var token=WebUtility.HtmlDecode(Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
  c.DefaultRequestHeaders.Add("Cookie",string.Join("; ",response.Headers.GetValues("Set-Cookie").Select(x=>x.Split(';')[0])));
  var saved=await c.PostAsync("/properties/new",new FormUrlEncodedContent(new Dictionary<string,string>{{"__RequestVerificationToken",token},{"Version","0"},{"Step","1"},{"Input.Title","Synthetic normal draft"},{"CopyMediaRevision",""}}));
  Assert.Equal(HttpStatusCode.Redirect,saved.StatusCode);Assert.StartsWith("/properties/",saved.Headers.Location!.OriginalString);Assert.DoesNotContain("new",saved.Headers.Location.OriginalString);
 }
}
