using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using RealEstate.Domain;
using RealEstate.Web;
using Xunit;

namespace RealEstate.Tests;

public sealed class CustomerPanelNavigationTests
{
    private static readonly string Path="/commercial/customers/"+CommercialAppointmentFixture.CustomerId;

    [Fact]
    public async Task Customer_file_opens_a_compact_summary_and_only_the_requested_panel()
    {
        var root=System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory,"../../../../../backend/src/RealEstate.Web"));
        await using var app=Program.Build(["--contentRoot",root],b=>{CommercialAppointmentFixture.Configure(b);b.WebHost.UseTestServer();});
        await app.StartAsync();
        var client=app.GetTestClient();

        var summary=await client.GetStringAsync(Path);
        Assert.Contains("Último atendimento",summary);
        Assert.Contains("Ações rápidas",summary);
        Assert.DoesNotContain("customer-overview-panel",summary);
        Assert.Contains("Cliente de teste",summary);
        Assert.Contains("?Panel=properties",summary);
        Assert.DoesNotContain("id=\"presented-properties\"",summary);
        Assert.DoesNotContain("id=\"customer-history\"",summary);
        Assert.DoesNotContain("id=\"customer-agenda\"",summary);

        var properties=await client.GetStringAsync(Path+"?Panel=properties");
        Assert.Contains("id=\"presented-properties\"",properties);
        Assert.Contains("customer-summary-return",properties);
        Assert.Contains("?Panel=overview",properties);
        Assert.DoesNotContain("Último atendimento",properties);
        Assert.DoesNotContain("Ações rápidas",properties);
        Assert.DoesNotContain("id=\"customer-history\"",properties);
        Assert.DoesNotContain("id=\"customer-agenda\"",properties);

        var history=await client.GetStringAsync(Path+"?Panel=history");
        Assert.Contains("id=\"customer-history\"",history);
        Assert.DoesNotContain("Último atendimento",history);
        Assert.DoesNotContain("id=\"presented-properties\"",history);
        Assert.DoesNotContain("id=\"customer-agenda\"",history);

        var agenda=await client.GetStringAsync(Path+"?Panel=agenda");
        Assert.Contains("id=\"customer-agenda\"",agenda);
        Assert.DoesNotContain("Último atendimento",agenda);
        Assert.DoesNotContain("id=\"presented-properties\"",agenda);
        Assert.DoesNotContain("id=\"customer-history\"",agenda);

        var data=await client.GetStringAsync(Path+"?Panel=data");
        Assert.Contains("id=\"customer-data\"",data);
        Assert.DoesNotContain("Último atendimento",data);
        Assert.DoesNotContain("id=\"presented-properties\"",data);
        Assert.DoesNotContain("id=\"customer-agenda\"",data);
    }

    [Fact]
    public async Task Existing_deep_links_open_their_contextual_panel()
    {
        var root=System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory,"../../../../../backend/src/RealEstate.Web"));
        await using var app=Program.Build(["--contentRoot",root],b=>{CommercialAppointmentFixture.Configure(b);b.WebHost.UseTestServer();});
        await app.StartAsync();
        var client=app.GetTestClient();

        Assert.Contains("id=\"presented-properties\"",await client.GetStringAsync(Path+"?Search=true"));
        Assert.Contains("id=\"customer-agenda\"",await client.GetStringAsync(Path+"?VisitPresentation="+CommercialAppointmentFixture.PresentationId));
    }

    [Fact]
    public async Task Commercial_property_search_reuses_wallet_filters_and_renders_accessible_selectable_cards()
    {
        var root=System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory,"../../../../../backend/src/RealEstate.Web"));
        await using var app=Program.Build(["--contentRoot",root],b=>{CommercialAppointmentFixture.Configure(b);b.WebHost.UseTestServer();});
        await app.StartAsync();
        var client=app.GetTestClient();

        var html=await client.GetStringAsync(Path+"?Panel=properties&Search=true&Filter.Search=UI-001");

        Assert.Contains("listing-filters",html);
        Assert.Contains("name=\"Filter.State\"",html);
        Assert.Contains("name=\"Filter.Sort\"",html);
        Assert.Contains("name=\"Filter.MinBedrooms\"",html);
        Assert.Contains("commercial-property-card",html);
        Assert.Contains("data-select-page",html);
        Assert.Contains("data-select-row",html);
        Assert.Contains("Selecionar o imóvel",html);
        Assert.Contains("property-photo-placeholder",html);
        Assert.Contains("commercial-property-code",html);
        Assert.Contains("Casa",html);
        Assert.Contains("commercial-property-location",html);
        Assert.Contains("commercial-property-price",html);
        Assert.Contains("commercial-channel-options",html);
        Assert.Contains("Limpar filtros",html);
        Assert.Contains("href=\"/commercial/customers/"+CommercialAppointmentFixture.CustomerId+"?Panel=properties&amp;Search=true#property-search\">Limpar filtros</a>",html);
        Assert.Contains("UI-001",html);
        Assert.DoesNotContain("UI-002",html);
        Assert.Contains("aria-live=\"polite\"",html);
        Assert.DoesNotContain("property-table",html);
    }

    [Fact]
    public async Task Commercial_cards_show_capture_rooms_and_each_active_modality_fee_precedence()
    {
        var root=System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory,"../../../../../backend/src/RealEstate.Web"));
        await using var app=Program.Build(["--contentRoot",root],b=>{CommercialAppointmentFixture.Configure(b);b.Services.AddSingleton<IPropertyListingStore>(new CommercialIndicatorListingStore());b.WebHost.UseTestServer();});
        await app.StartAsync();var client=app.GetTestClient();

        var html=await client.GetStringAsync(Path+"?Panel=properties&Search=true");
        html=System.Net.WebUtility.HtmlDecode(html);

        Assert.Contains("data-indicator-bedrooms=\"2\"",html);Assert.Contains("data-indicator-parking=\"1\"",html);
        Assert.Contains("data-indicator-condominium=\"0\"",html);Assert.Contains(Brl.Currency(0m),html);Assert.Contains("/mês",html);
        Assert.Contains("data-indicator-tax=\"85\"",html);Assert.Contains("data-indicator-tax=\"90\"",html);Assert.Contains(Brl.Currency(85m),html);Assert.Contains("/ano",html);
        Assert.Equal(2,Count(html,"data-indicator-condominium="));Assert.Equal(2,Count(html,"data-indicator-tax="));
        Assert.Contains("data-indicator-purpose=\"Venda\"",html);Assert.Contains(Brl.Currency(350m),html);
        Assert.Contains("data-indicator-purpose=\"Locação\"",html);
        Assert.Contains("/cad-icons.svg#bed",html);Assert.Contains("/cad-icons.svg#car",html);
        Assert.Contains("/cad-icons.svg#building",html);Assert.Contains("/cad-icons.svg#tax-receipt",html);
        Assert.Equal(2,Count(html,"name=\"SelectedProperties\""));
        Assert.Contains("data-select-page",html);Assert.Contains("aria-live=\"polite\"",html);
        Assert.DoesNotContain("property-table",html);
    }

    private static int Count(string value,string match)=>System.Text.RegularExpressions.Regex.Matches(value,System.Text.RegularExpressions.Regex.Escape(match)).Count;
}

internal sealed class CommercialIndicatorListingStore:IPropertyListingStore
{
    private readonly PropertyV2 property;
    private readonly PropertyV2 propertyWithoutFees;
    public CommercialIndicatorListingStore()
    {
        var capture=new CaptureData();capture.Rooms.Bedrooms=2;capture.Rooms.Parking=1;
        capture.Values.Sale.Authorized=500000m;capture.Values.Rental.Authorized=2500m;
        capture.Values.CondominiumFee=350m;capture.Values.CondominiumPeriod="monthly";
        capture.Values.PropertyTax=85m;capture.Values.PropertyTaxPeriod="annual";
        property=new(UiFixture.PropertyId,FixtureProgram.OrganizationA,
            new("TEST-CARD-01","Apartamento de teste",null,"Residencial","Apartamento","Venda e locação",null,Capture:capture),1,"Draft");
        var secondCapture=new CaptureData();secondCapture.Rooms.Bedrooms=1;
        propertyWithoutFees=new(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbc"),FixtureProgram.OrganizationA,
            new("TEST-CARD-02","Casa de teste",null,"Residencial","Casa","Venda",null,Capture:secondCapture),1,"Draft");
    }
    public Task<PropertyListPage> ListPageAsync(AccessScope scope,PropertyListQuery query,CancellationToken token)
    {
        var announcements=new ListingAnnouncement[]{
            new(Guid.NewGuid(),"Venda","TEST-CARD-01-VEN",new OperationValue{Authorized=510000m},false,PropertyTax:90m,PropertyTaxPeriod:"annual"),
            new(Guid.NewGuid(),"Locação","TEST-CARD-01-LOC",new OperationValue{Authorized=2600m},false,CondominiumFee:0m,CondominiumPeriod:"monthly")};
        return Task.FromResult(new PropertyListPage([property,propertyWithoutFees],2,scope.Establishments??[],new Dictionary<Guid,IReadOnlyList<ListingAnnouncement>>{{property.Id,announcements},{propertyWithoutFees.Id,[]}}));
    }
}
