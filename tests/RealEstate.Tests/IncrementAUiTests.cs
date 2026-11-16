using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RealEstate.Domain;
using RealEstate.Web;
using Xunit;
namespace RealEstate.Tests;
// TEST ONLY Razor/HTTP fixture. Does not stand in for the actual OIDC directory proof.
public sealed class IncrementAUiTests
{
    sealed class Directory(bool eligible):ICaptureDirectory
    {
        public Task<Outcome<CaptureResponsibleDirectory>> ReadAsync(AccessScope s,Guid unit,string? subject=null,CancellationToken token=default)
        =>Task.FromResult(new Outcome<CaptureResponsibleDirectory>(new(s.OrganizationId,unit,eligible?[new(s.Subject,"Self eligible",Guid.NewGuid(),"Descriptive")]:[])));
    }
    [Theory][InlineData(true)][InlineData(false)]
    public async Task Self_suggestion_requires_directory_eligibility_and_does_not_assign_automatically(bool eligible)
    {
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../backend/src/RealEstate.Web"));
        await using var app=Program.Build(["--contentRoot",root],b=>{UiFixture.Configure(b);b.Services.RemoveAll<ICaptureDirectory>();b.Services.AddSingleton<ICaptureDirectory>(new Directory(eligible));b.WebHost.UseTestServer();});
        await app.StartAsync();using var client=app.GetTestClient();var html=await client.GetStringAsync("/properties/new?step=7");
        Assert.Equal(eligible,html.Contains("data-use-responsible=\"test-only-actor\""));
        Assert.DoesNotContain("selected=\"selected\" value=\"test-only-actor\"",html);
    }
}
