using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using RealEstate.Web;
using Xunit;
using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using RealEstate.Domain;
namespace RealEstate.Tests;

public sealed class AddressAssistanceTests
{
    [Theory]
    [InlineData("normal",false)]
    [InlineData("empty",true)]
    public async Task Category_selection_explains_when_no_authorized_values_exist(string mode,bool empty)
    {
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../backend/src/RealEstate.Web"));
        await using var app=Program.Build(["--contentRoot",root],b=>{UiFixture.Configure(b);b.WebHost.UseTestServer();});
        await app.StartAsync();using var client=app.GetTestClient();client.DefaultRequestHeaders.Add("X-Ui-Mode",mode);
        var page=await client.GetStringAsync("/properties/new");
        Assert.Equal(empty,page.Contains("Nenhuma categoria disponível",StringComparison.Ordinal));
        Assert.Contains("Nova categoria",page);
    }
    [Theory]
    [InlineData(true,null,"BR")]
    [InlineData(false,null,null)]
    [InlineData(false,"PT","PT")]
    public async Task Country_is_server_owned_for_new_records_and_preserved_for_legacy(bool isNew,string? storedCountry,string? expected)
    {
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../backend/src/RealEstate.Web"));
        await using var app=Program.Build(["--contentRoot",root],b=>{UiFixture.Configure(b);b.WebHost.UseTestServer();});
        await app.StartAsync();using var client=app.GetTestClient();
        long version=0;var path="/properties/new";
        if(!isNew){
            var old=(await client.GetFromJsonAsync<PropertyV2>("/api/v2/properties/"+UiFixture.PropertyId))!;
            var input=old.Data with{Address=old.Data.Address! with{Country=storedCountry},Category="Categoria legada única"};
            var update=await client.PutAsJsonAsync("/api/v2/properties/"+old.Id,new Program.UpdatePropertyV2(input,old.Version));
            var saved=(await update.Content.ReadFromJsonAsync<PropertyV2>())!;version=saved.Version;path="/properties/"+old.Id;
        }
        var response=await client.GetAsync(path);var page=await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("name=\"Input.Address.Country\"",page);
        if(!isNew)Assert.Contains("Categoria legada",page);
        var token=Regex.Match(page,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        client.DefaultRequestHeaders.Add("Cookie",string.Join("; ",response.Headers.GetValues("Set-Cookie").Select(c=>c.Split(';')[0])));
        var data=new Dictionary<string,string>{["__RequestVerificationToken"]=WebUtility.HtmlDecode(token),["Version"]=version.ToString(),
            ["Input.Code"]="COUNTRY-TEST",["Input.Title"]="Country test",["Input.OwnerId"]=UiFixture.OwnerId.ToString(),
            ["Input.EstablishmentId"]=FixtureProgram.OrganizationA.ToString(),["Input.Category"]=isNew?"Residencial":"Categoria legada única",
            ["Input.Type"]="Casa",["Input.Purpose"]="Venda",["Input.Price"]="3000",["Input.Address.Country"]="US"};
        var posted=await client.PostAsync(path,new FormUrlEncodedContent(data));Assert.Equal(HttpStatusCode.Found,posted.StatusCode);
        var workspace=(await client.GetFromJsonAsync<WorkspaceV2>("/api/v2/properties"))!;
        var savedId=Guid.Parse(posted.Headers.Location!.OriginalString.Split('/').Last());
        var savedProperty=workspace.Properties.Single(p=>p.Id==savedId);
        Assert.Equal(expected,savedProperty.Data.Address!.Country);
        Assert.NotEqual("COUNTRY-TEST",savedProperty.Data.Code); // Browser cannot assign/replace the identification code.
        if(isNew){
            data["Input.Code"]="FORGED-CATEGORY";data["Input.Category"]="Não autorizada";
            Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsync(path,new FormUrlEncodedContent(data))).StatusCode);
            workspace=(await client.GetFromJsonAsync<WorkspaceV2>("/api/v2/properties"))!;
            Assert.DoesNotContain(workspace.Properties,p=>p.Data.Category=="Não autorizada");
        }
    }
    [Theory]
    [InlineData("normal",true)]
    [InlineData("read-only",false)]
    [InlineData("update-only",false)]
    public async Task Existing_property_offers_new_only_with_create_permission(string mode,bool expected)
    {
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../backend/src/RealEstate.Web"));
        await using var app=Program.Build(["--contentRoot",root],b=>{UiFixture.Configure(b);b.WebHost.UseTestServer();});
        await app.StartAsync();using var client=app.GetTestClient();client.DefaultRequestHeaders.Add("X-Ui-Mode",mode);
        var html=await client.GetStringAsync("/properties/"+UiFixture.PropertyId);
        Assert.Equal(expected,html.Contains("data-new-property"));
        if(mode=="update-only")Assert.Contains("data-pending-label",html);
    }
    [Fact]
    public async Task Editor_prioritizes_structured_fields_and_offers_accessible_confirmable_suggestions()
    {
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../backend/src/RealEstate.Web"));
        await using var app=Program.Build(["--contentRoot",root],b=>{UiFixture.Configure(b);b.WebHost.UseTestServer();});
        await app.StartAsync();using var client=app.GetTestClient();var response=await client.GetAsync("/properties/new");
        var html=await response.Content.ReadAsStringAsync();
        Assert.True(html.IndexOf("name=\"Input.Address.Street\"",StringComparison.Ordinal)<html.IndexOf("name=\"Input.Address.LegacyText\"",StringComparison.Ordinal));
        Assert.Contains("id=\"cep-status\"",html);Assert.Contains("aria-live=\"polite\"",html);
        Assert.DoesNotContain("id=\"cep-apply\"",html);Assert.Contains("id=\"address-map\"",html);
        Assert.Contains("<select",html);Assert.Matches("<select[^>]*name=\"Input.Category\"",html);
        Assert.Contains("Nova categoria",html);
        Assert.DoesNotContain("name=\"Input.Address.Country\"",html);
        Assert.Contains("rel=\"noopener noreferrer\"",html);
        Assert.Contains("connect-src 'self' https://viacep.com.br",response.Headers.GetValues("Content-Security-Policy").Single());
    }
}
