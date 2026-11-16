using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RealEstate.Domain;
using RealEstate.Web;
using Xunit;
namespace RealEstate.Tests;

// TEST ONLY: exercises real Razor/HTTP/antiforgery against a disposable in-memory store.
// Does not certify PostgreSQL, Core license, OIDC or cross-tenant persistence.
public static class UiFixture
{
    public static readonly Guid OwnerId=Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    public static readonly Guid PropertyId=Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    public static void Configure(WebApplicationBuilder builder)
    {
        FixtureProgram.Configure(builder);
        builder.Configuration["UiOnlyFixture"]="true";
        builder.Services.RemoveAll<ICoreAccess>();
        builder.Services.RemoveAll<IPropertyStore>();
        builder.Services.RemoveAll<IPropertyStoreV2>();
        builder.Services.RemoveAll<IPropertyListingStore>();
        builder.Services.RemoveAll<IPropertyMediaStore>();builder.Services.AddSingleton<IPropertyMediaStore,EmptyMediaStore>();
        builder.Services.AddScoped<ICoreAccess,Access>();
        builder.Services.AddSingleton<UiAddressStore>();
        builder.Services.AddSingleton<IPropertyStore>(s=>s.GetRequiredService<UiAddressStore>());
        builder.Services.AddSingleton<IPropertyStoreV2>(s=>s.GetRequiredService<UiAddressStore>());
        builder.Services.AddSingleton<IPropertyListingStore>(s=>s.GetRequiredService<UiAddressStore>());
    }
    public static void Serve(string url)
    {
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../backend/src/RealEstate.Web"));
        Program.Build(["--contentRoot",root,"--urls",url],Configure).Run();
    }
    private sealed class Access(IHttpContextAccessor accessor):ICoreAccess
    {
        public Task<Outcome<AccessScope>> RequireAsync(string resource,string permission,CancellationToken token)
        {
            var http=accessor.HttpContext!;var mode=http.Request.Headers["X-Ui-Mode"].ToString();
            var rights=http.Request.Headers["X-Fixture-Rights"].ToString();
            if(!string.IsNullOrWhiteSpace(rights)&&!rights.Split(',',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries).Contains($"{resource}/{permission}",StringComparer.Ordinal))
                return Task.FromResult(Outcome<AccessScope>.Fail("Access.Forbidden"));
            if(mode=="outage")return Task.FromResult(Outcome<AccessScope>.Fail("Dependency.Unavailable"));
            if(mode=="denied" || mode=="read-only" && permission!="PROPERTY_READ" || mode=="update-only" && permission=="PROPERTY_CREATE" || mode=="no-owner-write" && permission=="OWNER_CREATE")
                return Task.FromResult(Outcome<AccessScope>.Fail("Access.Forbidden"));
            var organization=http.Request.Headers["X-Fixture-Organization"]=="B"?FixtureProgram.OrganizationB:FixtureProgram.OrganizationA;
            var unit=Guid.TryParse(http.Request.Headers["X-Fixture-Unit"],out var requestedUnit)?requestedUnit:organization;
            var scope=new AccessScope(organization,"test-only-actor","Empresa sintética de homologação visual",[new(unit,"Unidade de teste")],ActiveEstablishmentId:unit);
            http.Items[typeof(AccessScope)]=scope;
            return Task.FromResult(new Outcome<AccessScope>(scope));
        }
    }

}

public sealed class UiPresentationTests
{
    private static async Task<(WebApplication App,HttpClient Client)> Host()
    {
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../backend/src/RealEstate.Web"));
        var app=Program.Build(["--contentRoot",root],builder=>{UiFixture.Configure(builder);builder.WebHost.UseTestServer();});
        await app.StartAsync();
        return (app,app.GetTestClient());
    }
    [Theory]
    [InlineData("normal",200,"property-table")]
    [InlineData("denied",403,"data-error-focus")]
    [InlineData("outage",503,"data-error-focus")]
    [InlineData("empty",200,"empty-state")]
    public async Task List_context_and_failure_states(string mode,int status,string text)
    {
        var (app,client)=await Host();await using var lifetime=app;
        client.DefaultRequestHeaders.Add("X-Ui-Mode",mode);
        var response=await client.GetAsync("/");var html=await response.Content.ReadAsStringAsync();
        Assert.Equal(status,(int)response.StatusCode);Assert.Contains(text,html);
        Assert.Contains("workspace-content",html);
        if(status!=200)Assert.DoesNotContain("Empresa sint",html);
    }
    [Fact] public async Task Read_only_disables_fields_and_owner_permission_is_independent()
    {
        var (app,client)=await Host();await using var lifetime=app;
        client.DefaultRequestHeaders.Add("X-Ui-Mode","read-only");
        var html=await client.GetStringAsync("/properties/"+UiFixture.PropertyId);
        Assert.Matches("<fieldset[^>]*disabled",html);Assert.DoesNotContain("data-pending-label",html);
        Assert.Contains("name=\"Version\"",html);Assert.Contains("name=\"Input.Code\"",html);
        client.DefaultRequestHeaders.Remove("X-Ui-Mode");client.DefaultRequestHeaders.Add("X-Ui-Mode","no-owner-write");
        Assert.DoesNotContain("owner-panel",await client.GetStringAsync("/"));
    }
    [Fact] public async Task Antiforgery_is_required_and_logout_remains_post()
    {
        var (app,client)=await Host();await using var lifetime=app;
        var result=await client.PostAsync("/?handler=Owner",new FormUrlEncodedContent(new Dictionary<string,string>{{"OwnerName","Synthetic"}}));
        Assert.Equal(HttpStatusCode.BadRequest,result.StatusCode);
        result=await client.PostAsync("/account/logout",new FormUrlEncodedContent(new Dictionary<string,string>()));
        // Existing endpoint throws on invalid CSRF; production exception handler redirects to /Error.
        Assert.Equal(HttpStatusCode.Found,result.StatusCode);
        Assert.Equal("/Error",result.Headers.Location?.OriginalString);
        var html=await client.GetStringAsync("/");
        Assert.Contains("action=\"/account/logout\"",html);Assert.Contains("__RequestVerificationToken",html);
    }
    [Fact] public async Task Read_only_cannot_bypass_disabled_fields_with_a_forged_post()
    {
        var (app,client)=await Host();await using var lifetime=app;
        client.DefaultRequestHeaders.Add("X-Ui-Mode","read-only");
        var response=await client.GetAsync("/properties/"+UiFixture.PropertyId);
        var html=await response.Content.ReadAsStringAsync();
        var token=Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(token);
        client.DefaultRequestHeaders.Add("Cookie",string.Join("; ",response.Headers.GetValues("Set-Cookie").Select(c=>c.Split(';')[0])));
        var values=new Dictionary<string,string>{
            ["__RequestVerificationToken"]=WebUtility.HtmlDecode(token),["Version"]="1",
            ["Input.Code"]="UI-001",["Input.Title"]="Forbidden edit",["Input.OwnerId"]=UiFixture.OwnerId.ToString(),
            ["Input.EstablishmentId"]=FixtureProgram.OrganizationA.ToString(),["Input.Category"]="Residencial",
            ["Input.Type"]="Casa",["Input.Purpose"]="Venda",["Input.Address"]="Synthetic street",
            ["Input.PostalCode"]="01310100",["Input.Price"]="3000",["Input.Latitude"]="-23",["Input.Longitude"]="-46"
        };
        response=await client.PostAsync("/properties/"+UiFixture.PropertyId,new FormUrlEncodedContent(values));
        Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
        Assert.DoesNotContain("Forbidden edit",await client.GetStringAsync("/"));
    }
    [Fact] public async Task Address_manual_entry_keeps_legacy_field_names_without_external_scripts()
    {
        var (app,client)=await Host();await using var lifetime=app;
        var html=await client.GetStringAsync("/properties/new");
        Assert.Contains("name=\"Input.Address.LegacyText\"",html);
        Assert.Contains("name=\"Input.Address.PostalCode\"",html);
        Assert.Contains("autocomplete=\"street-address\"",html);
        Assert.Contains("autocomplete=\"postal-code\"",html);
        Assert.Contains("aria-describedby=\"postal-help\"",html);
        Assert.Contains("address-help",html);
        Assert.DoesNotContain("maps.googleapis.com",html);
        Assert.DoesNotContain("viacep.com.br",html);
    }
}
