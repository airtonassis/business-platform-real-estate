using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using RealEstate.Domain;
using RealEstate.Web;
using Xunit;

namespace RealEstate.Tests;

public sealed class CustomerPermissionGateTests
{
    private const string CustomerRead="customers/CUSTOMER_READ";
    private const string PropertyRead="properties/PROPERTY_READ";
    private static readonly string CustomerPath="/commercial/customers/"+CommercialAppointmentFixture.CustomerId;
    private static async Task<(WebApplication App,HttpClient Client)> Host()
    {
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../backend/src/RealEstate.Web"));
        var app=Program.Build(["--contentRoot",root],b=>{CommercialAppointmentFixture.Configure(b);b.Services.AddSingleton<IPropertyListingStore>(new CommercialIndicatorListingStore());b.WebHost.UseTestServer();});
        await app.StartAsync();return(app,app.GetTestClient());
    }
    private static void Rights(HttpClient client,params string[] rights)
    {
        client.DefaultRequestHeaders.Remove("X-Fixture-Rights");client.DefaultRequestHeaders.Add("X-Fixture-Rights",string.Join(',',rights));
    }
    private static (string Token,string Cookie) AntiForgery(HttpResponseMessage response,string html)
    {
        var token=WebUtility.HtmlDecode(Regex.Match(html,"name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value);
        Assert.NotEmpty(token);return(token,string.Join("; ",response.Headers.GetValues("Set-Cookie").Select(v=>v.Split(';')[0])));
    }
    private static void Cookie(HttpClient client,string value){client.DefaultRequestHeaders.Remove("Cookie");client.DefaultRequestHeaders.Add("Cookie",value);}

    [Fact]
    public async Task Customer_list_and_file_are_readable_with_customer_read_alone_and_write_actions_stay_hidden()
    {
        var(app,client)=await Host();await using var lifetime=app;Rights(client,CustomerRead);
        var list=await client.GetAsync("/commercial/customers");var listHtml=await list.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK,list.StatusCode);Assert.Contains("Cliente de teste",listHtml);Assert.DoesNotContain("href=\"/commercial/customers/new\"",listHtml);
        var file=await client.GetAsync(CustomerPath+"?Panel=data");var fileHtml=await file.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK,file.StatusCode);Assert.Contains("Cliente de teste",fileHtml);Assert.DoesNotContain("Salvar cliente",fileHtml);Assert.DoesNotContain("Registrar atendimento",fileHtml);Assert.DoesNotContain("Registrar apresenta",fileHtml);
    }

    [Fact]
    public async Task Commercial_search_needs_customer_and_property_read_but_not_presentation_write()
    {
        var(app,client)=await Host();await using var lifetime=app;Rights(client,CustomerRead,PropertyRead);
        var response=await client.GetAsync(CustomerPath+"?Panel=properties&Search=true");var html=await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.Contains("Buscar imóveis para apresentar",html);Assert.Contains("commercial-property-card",html);
        Assert.Contains("commercial-property-indicator",html);Assert.DoesNotContain("data-select-row",html);Assert.DoesNotContain("data-select-page",html);
        Assert.DoesNotContain("presentation-form",html);Assert.DoesNotContain("Registrar apresentação dos selecionados",html);
    }

    [Fact]
    public async Task Customer_and_property_reads_are_independently_required_for_their_routes()
    {
        var(app,client)=await Host();await using var lifetime=app;
        Rights(client,PropertyRead);Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync("/commercial/customers")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync(CustomerPath)).StatusCode);
        Rights(client,CustomerRead);var noProperty=await client.GetAsync(CustomerPath+"?Panel=properties&Search=true");var html=await noProperty.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.Forbidden,noProperty.StatusCode);Assert.DoesNotContain("commercial-property-card",html);Assert.DoesNotContain("commercial-property-indicator",html);
        Rights(client,CustomerRead,PropertyRead);client.DefaultRequestHeaders.Remove("X-Fixture-Organization");client.DefaultRequestHeaders.Add("X-Fixture-Organization","B");
        var otherOrganization=await client.GetAsync("/commercial/customers");Assert.DoesNotContain("Cliente de teste",await otherOrganization.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync(CustomerPath)).StatusCode);
        client.DefaultRequestHeaders.Remove("X-Fixture-Organization");client.DefaultRequestHeaders.Add("X-Fixture-Unit",Guid.NewGuid().ToString());
        var otherUnit=await client.GetAsync("/commercial/customers");Assert.DoesNotContain("Cliente de teste",await otherUnit.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync(CustomerPath)).StatusCode);
    }

    [Fact]
    public async Task Direct_customer_contact_and_presentation_posts_are_denied_without_their_write_rights()
    {
        var(app,client)=await Host();await using var lifetime=app;
        Rights(client,CustomerRead,PropertyRead,"customers/CUSTOMER_CREATE","customers/CUSTOMER_UPDATE","customers/CUSTOMER_CONTACT_CREATE","customers/CUSTOMER_PROPERTY_WRITE");
        var createPage=await client.GetAsync("/commercial/customers/new");var createHtml=await createPage.Content.ReadAsStringAsync();var(createToken,createCookie)=AntiForgery(createPage,createHtml);
        var pathPage=await client.GetAsync(CustomerPath+"?Panel=history");var pathHtml=await pathPage.Content.ReadAsStringAsync();var(pathToken,pathCookie)=AntiForgery(pathPage,pathHtml);
        var searchPage=await client.GetAsync(CustomerPath+"?Panel=properties&Search=true");var searchHtml=await searchPage.Content.ReadAsStringAsync();var(presentToken,presentCookie)=AntiForgery(searchPage,searchHtml);
        var store=app.Services.GetRequiredService<FixtureCustomers>();var interactions=app.Services.GetRequiredService<FixtureInteractions>();

        Rights(client,CustomerRead);Cookie(client,createCookie);var create=await client.PostAsync("/commercial/customers/new",new FormUrlEncodedContent(new Dictionary<string,string>{{"Input.Name","Teste bloqueado"},{"Input.PersonType","Física"},{"Input.Phone","11987654321"},{"Input.Origin","Site"},{"Input.Interest","Compra"},{"Input.ResponsibleSubject","test-only-actor"},{"__RequestVerificationToken",createToken}}));
        Assert.Equal(HttpStatusCode.Forbidden,create.StatusCode);Assert.Equal(0,store.SaveCalls);

        Cookie(client,pathCookie);var update=await client.PostAsync(CustomerPath,new FormUrlEncodedContent(new Dictionary<string,string>{{"Input.Name","Cliente alterado"},{"Input.PersonType","Física"},{"Input.Phone","11999998888"},{"Input.Origin","Site"},{"Input.Interest","Compra"},{"Input.ResponsibleSubject","test-only-actor"},{"Version","1"},{"__RequestVerificationToken",pathToken}}));
        Assert.Equal(HttpStatusCode.Forbidden,update.StatusCode);Assert.Equal(0,store.SaveCalls);
        var contact=await client.PostAsync(CustomerPath+"?handler=Contact",new FormUrlEncodedContent(new Dictionary<string,string>{{"ContactKind","Telefone"},{"Description","Teste bloqueado"},{"__RequestVerificationToken",pathToken}}));
        Assert.Equal(HttpStatusCode.Forbidden,contact.StatusCode);Assert.Equal(0,store.ContactCalls);

        Rights(client,CustomerRead,PropertyRead);Cookie(client,presentCookie);var present=await client.PostAsync(CustomerPath+"?handler=Present",new FormUrlEncodedContent(new Dictionary<string,string>{{"SelectedProperties",UiFixture.PropertyId.ToString()},{"PresentationChannel","Telefone"},{"__RequestVerificationToken",presentToken}}));
        Assert.Equal(HttpStatusCode.Forbidden,present.StatusCode);Assert.Equal(0,interactions.PresentCalls);
        var feedback=await client.PostAsync(CustomerPath+"?handler=Feedback",new FormUrlEncodedContent(new Dictionary<string,string>{{"PresentationId",CommercialAppointmentFixture.PresentationId.ToString()},{"ReturnStatus","Visitado"},{"ReturnChannel","Telefone"},{"__RequestVerificationToken",presentToken}}));
        Assert.Equal(HttpStatusCode.Forbidden,feedback.StatusCode);Assert.Equal(0,interactions.FeedbackCalls);
    }
}
