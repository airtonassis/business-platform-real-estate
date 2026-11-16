using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using RealEstate.Web;
using Xunit;
namespace RealEstate.Tests;

public sealed class DraftSubmissionDiagnosticsTests
{
    [Fact] public async Task Form_exposes_resolved_context_to_reject_resumption_in_another_organization_or_unit()
    {
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../backend/src/RealEstate.Web"));
        await using var app=Program.Build(["--contentRoot",root],b=>{UiFixture.Configure(b);b.WebHost.UseTestServer();});
        await app.StartAsync();using var client=app.GetTestClient();var html=await client.GetStringAsync("/properties/new");
        Assert.Contains("data-session-subject=\"test-only-actor\"",html);
        Assert.Contains("data-session-organization=\""+FixtureProgram.OrganizationA+"\"",html);
        Assert.Contains("data-session-establishment=\""+FixtureProgram.OrganizationA+"\"",html);
    }
    [Theory]
    [InlineData("/properties/new")]
    [InlineData("/properties/new?step=12")]
    [InlineData("/properties/new?step=9")]
    public async Task Missing_session_is_reported_before_stale_antiforgery_or_draft_persistence(string path)
    {
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../backend/src/RealEstate.Web"));
        await using var app=Program.Build(["--contentRoot",root],b=>{UiFixture.Configure(b);b.WebHost.UseTestServer();});
        await app.StartAsync();using var client=app.GetTestClient();client.DefaultRequestHeaders.Add("X-Fixture-Anonymous","true");
        using var response=await client.PostAsync(path,new FormUrlEncodedContent(new Dictionary<string,string>{{"__RequestVerificationToken","stale-form-token"},{"Input.Title","private-test-marker"}}));
        Assert.Equal(HttpStatusCode.Unauthorized,response.StatusCode);
        Assert.True(response.Headers.Contains("X-Request-ID"));
        Assert.DoesNotContain("private-test-marker",await response.Content.ReadAsStringAsync());
    }
    [Fact] public async Task Rejected_new_draft_has_server_correlation_without_exposing_payload()
    {
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../backend/src/RealEstate.Web"));
        await using var app=Program.Build(["--contentRoot",root],b=>{UiFixture.Configure(b);b.WebHost.UseTestServer();});
        await app.StartAsync();using var client=app.GetTestClient();
        using var response=await client.PostAsync("/properties/new",new FormUrlEncodedContent(new Dictionary<string,string>{{"Input.Title","private-test-marker"}}));
        Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);
        Assert.True(response.Headers.TryGetValues("X-Request-ID",out var references));
        Assert.False(string.IsNullOrWhiteSpace(Assert.Single(references!)));
        Assert.DoesNotContain("private-test-marker",await response.Content.ReadAsStringAsync());
    }
}
