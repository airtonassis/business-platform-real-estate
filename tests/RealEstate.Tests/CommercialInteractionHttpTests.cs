using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RealEstate.Domain;
using RealEstate.Web;
using Xunit;
namespace RealEstate.Tests;
[Collection("PostgreSQL")]
public sealed class CommercialInteractionHttpTests
{
    [Fact]public async Task Invalid_individual_feedback_keeps_the_reason_and_notes_for_correction(){
        await using var app=Program.Build([],b=>{b.WebHost.UseTestServer();FixtureProgram.Configure(b);b.Services.RemoveAll<ICustomerDirectory>();b.Services.AddScoped<ICustomerDirectory,CustomerTestDirectory>();});await app.StartAsync();var http=app.GetTestClient();http.DefaultRequestHeaders.Add("X-Fixture-Unit",Guid.NewGuid().ToString());http.DefaultRequestHeaders.Authorization=new("Bearer","test-only-fixture-token");
        var created=await http.PostAsJsonAsync("/api/v1/customers",new CustomerEndpoints.SaveCustomer(new("Feedback correction","Física","11911112222",null,"Site","Compra","test-only-actor")));Assert.Equal(HttpStatusCode.OK,created.StatusCode);var customer=(await created.Content.ReadFromJsonAsync<Customer>())!;
        var propertyResponse=await http.PostAsJsonAsync("/api/v2/properties",new PropertyInputV2(Type:"Casa",Purpose:"Venda"));Assert.Equal(HttpStatusCode.OK,propertyResponse.StatusCode);var property=(await propertyResponse.Content.ReadFromJsonAsync<PropertyV2>())!;
        var presentedResponse=await http.PostAsJsonAsync($"/api/v1/customers/{customer.Id}/presentations",new PresentationInput([property.Id],"Telefone",null));Assert.Equal(HttpStatusCode.OK,presentedResponse.StatusCode);var presented=(await presentedResponse.Content.ReadFromJsonAsync<CommercialInteraction[]>())!.Single();
        var path=$"/commercial/customers/{customer.Id}";var page=await http.GetAsync(path);var html=await page.Content.ReadAsStringAsync();var csrf=Regex.Match(html,"name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;http.DefaultRequestHeaders.Add("Cookie",string.Join("; ",page.Headers.GetValues("Set-Cookie").Select(v=>v.Split(';')[0])));
        var response=await http.PostAsync(path+"?handler=Feedback",new FormUrlEncodedContent(new Dictionary<string,string>{{"PresentationId",presented.PresentationId.ToString()},{"ReturnStatus","Sem interesse"},{"ReturnChannel","Telefone"},{"ReturnReason","Preserve this reason"},{"ReturnNotes","Preserve these notes"},{"__RequestVerificationToken",csrf}}));
        Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);var invalidHtml=await response.Content.ReadAsStringAsync();Assert.Contains("value=\"Preserve this reason\"",invalidHtml);Assert.Contains("Preserve these notes</textarea>",invalidHtml);
        var history=await http.GetFromJsonAsync<CommercialInteraction[]>($"/api/v1/customers/{customer.Id}/commercial-history");Assert.Single(history!);
    }
}
