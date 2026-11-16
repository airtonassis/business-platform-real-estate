using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RealEstate.Domain;
using RealEstate.Web;
using Xunit;
namespace RealEstate.Tests;
// TEST ONLY HTML/antiforgery fixture; real OIDC/new-property journey is recorded separately.
public sealed class CaptureMediaEntryTests
{
    sealed class Directory:ICaptureDirectory
    {
        public Task<Outcome<CaptureResponsibleDirectory>> ReadAsync(AccessScope s,Guid unit,string? subject=null,CancellationToken token=default)
        =>Task.FromResult(new Outcome<CaptureResponsibleDirectory>(new(s.OrganizationId,unit,[new("eligible-entry-broker","Eligible broker",Guid.NewGuid(),"Broker")])));
    }
    static async Task<string> NewPage(bool empty=false)
    {
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../backend/src/RealEstate.Web"));
        await using var app=Program.Build(["--contentRoot",root],b=>{UiFixture.Configure(b);b.Services.RemoveAll<ICaptureDirectory>();b.Services.AddSingleton<ICaptureDirectory>(new Directory());b.WebHost.UseTestServer();});
        await app.StartAsync();using var client=app.GetTestClient();if(empty)client.DefaultRequestHeaders.Add("X-Ui-Mode","empty");return await client.GetStringAsync("/properties/new");
    }
    [Fact] public async Task Initial_new_page_contains_authorized_broker_for_client_side_navigation()
    {var html=await NewPage();Assert.Contains("value=\"eligible-entry-broker\"",html);}
    [Fact] public async Task New_property_exposes_real_file_picker_and_draft_save_before_upload()
    {var html=await NewPage();Assert.Contains("id=\"media-files\"",html);Assert.Contains("data-new-property=\"true\"",html);Assert.Contains("data-media-upload",html);}
    [Fact] public async Task Empty_category_catalog_is_optional_and_has_no_dead_end_creation_control()
    {var html=await NewPage(true);Assert.DoesNotContain("<select id=\"Input_Category\"",html);Assert.DoesNotContain("id=\"category-maintenance\"",html);Assert.Contains("O rascunho pode ser salvo sem categoria",html);}
}
