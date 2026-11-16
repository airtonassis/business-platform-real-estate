using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using RealEstate.Domain;
using RealEstate.Web;
using Xunit;
namespace RealEstate.Tests;
public sealed class AddressApiTests
{
    [Fact] public async Task V2_partial_roundtrip_v1_conflict_and_authorization_precedence()
    {
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../backend/src/RealEstate.Web"));
        await using var app=Program.Build(["--contentRoot",root],b=>{UiFixture.Configure(b);b.WebHost.UseTestServer();});
        await app.StartAsync();using var client=app.GetTestClient();
        var input=new PropertyInputV2("API-PARTIAL","Partial rural draft",UiFixture.OwnerId,"Rural","Fazenda","Venda",3000,
            new(WithoutNumber:true,State:"sp",MunicipalityIbgeCode:"3550308"));
        var created=await client.PostAsJsonAsync("/api/v2/properties",input);Assert.Equal(HttpStatusCode.OK,created.StatusCode);
        var saved=(await created.Content.ReadFromJsonAsync<PropertyV2>())!;Assert.Null(saved.Data.Address!.Latitude);Assert.True(saved.Data.Address.WithoutNumber);Assert.Equal("Draft",saved.Status);
        var reopened=await client.GetFromJsonAsync<PropertyV2>("/api/v2/properties/"+saved.Id);Assert.Equal(saved,reopened);
        var incompatible=await client.GetAsync("/api/v1/properties/"+saved.Id);Assert.Equal(HttpStatusCode.Conflict,incompatible.StatusCode);Assert.Contains("Property.AddressVersionRequired",await incompatible.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Add("X-Ui-Mode","denied");Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync("/api/v1/properties/"+saved.Id)).StatusCode);
        client.DefaultRequestHeaders.Remove("X-Ui-Mode");
        var list=(await client.GetFromJsonAsync<Workspace>("/api/v1/properties"))!;Assert.DoesNotContain(list.Properties,p=>p.Id==saved.Id);
        var updated=await client.PutAsJsonAsync("/api/v2/properties/"+saved.Id,new Program.UpdatePropertyV2(saved.Data with{Title="Edited partial"},saved.Version));Assert.Equal(HttpStatusCode.OK,updated.StatusCode);
        var conflict=await client.PutAsJsonAsync("/api/v2/properties/"+saved.Id,new Program.UpdatePropertyV2(saved.Data,saved.Version));Assert.Equal(HttpStatusCode.PreconditionFailed,conflict.StatusCode);
        var invalid=await client.PostAsJsonAsync("/api/v2/properties",input with{Address=new(State:"XX")});Assert.Equal(HttpStatusCode.BadRequest,invalid.StatusCode);
    }
    [Fact] public async Task Editor_allows_partial_address_without_required_lookup_or_coordinates()
    {
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../backend/src/RealEstate.Web"));
        await using var app=Program.Build(["--contentRoot",root],b=>{UiFixture.Configure(b);b.WebHost.UseTestServer();});await app.StartAsync();using var client=app.GetTestClient();
        var html=await client.GetStringAsync("/properties/new");
        Assert.Contains("Input.Address.MunicipalityIbgeCode",html);Assert.Contains("Input.Address.WithoutNumber",html);
        Assert.DoesNotMatch("<input[^>]*name=\"Input.Address.Latitude\"[^>]*required",html);
        Assert.DoesNotMatch("<input[^>]*required[^>]*name=\"Input.Address.PostalCode\"",html);
        Assert.DoesNotContain("maps.googleapis",html);Assert.Contains("address-help",html);
    }
}
