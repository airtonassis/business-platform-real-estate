using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using RealEstate.Domain;
using RealEstate.Web;
using Xunit;
namespace RealEstate.Tests;

public sealed class CommercialValuesTests
{
    [Theory]
    [InlineData("Venda","sale")][InlineData("Locação","rental")]
    [InlineData("Venda e locação","sale,rental")][InlineData(null,"")]
    public void Commercial_projection_only_exposes_active_operations_and_never_uses_legacy_price(string? purpose,string keys)
    {
        var c=new CaptureData();c.Values.Sale.Authorized=100m;c.Values.Rental.Authorized=200.5m;
        Assert.Equal(keys,string.Join(',',CommercialValues.Active(new(Purpose:purpose,Price:999m,Capture:c)).Select(x=>x.Key)));
        Assert.All(CommercialValues.Active(new(Purpose:purpose,Price:999m,Capture:new())),x=>Assert.Null(x.Value.Authorized));
        Assert.Equal(100m,c.Values.Sale.Authorized);Assert.Equal(200.5m,c.Values.Rental.Authorized);
    }
    [Fact] public void Per_operation_conditions_are_optional_limited_and_do_not_classify_old_general_terms()
    {
        var c=JsonSerializer.Deserialize<CaptureData>("{\"values\":{\"conditions\":\"Historic\",\"negotiable\":true}}",new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.True(CaptureRules.Validate(c).IsSuccess);Assert.Null(c.Values.Sale.Conditions);Assert.Null(c.Values.Rental.Conditions);
        c.Values.Rental.Conditions=new string('x',2001);Assert.Equal("Capture.TextTooLong",CaptureRules.Validate(c).Code);
        c.Values.Rental.Conditions=new string('x',2000);Assert.True(CaptureRules.Validate(c).IsSuccess);Assert.Equal("Historic",c.Values.Conditions);
    }
    [Fact] public void Applying_existing_policy_preserves_both_commercial_conditions_and_legacy_general_terms()
    {
        var c=new CaptureData();c.Values.Sale.Conditions="Sale only";c.Values.Rental.Conditions="Rental only";c.Values.Conditions="Historic";
        var revision=new CommercialPolicyRevision(Guid.NewGuid(),2,"Approved",new("Policy","Reason",new(new(){SalePercent=3},[])),"author","manager",DateTimeOffset.UtcNow,[]);
        var applied=CommercialPolicyRules.Apply(revision,c);
        Assert.Equal("Sale only",applied.Values.Sale.Conditions);Assert.Equal("Rental only",applied.Values.Rental.Conditions);Assert.Equal("Historic",applied.Values.Conditions);
        Assert.Equal(3,applied.Fees.SalePercent);Assert.Equal(revision.Id,applied.Policy!.RevisionId);Assert.Null(c.Policy);
    }
    [Theory][InlineData("Filter.MinPrice")][InlineData("Filter.MaxPrice")]
    public async Task Legacy_price_filters_keep_canonical_decimal_transport_under_portuguese_culture(string name)
    {
        var metadata=new Microsoft.AspNetCore.Mvc.ModelBinding.EmptyModelMetadataProvider();
        var action=new Microsoft.AspNetCore.Mvc.ActionContext(new Microsoft.AspNetCore.Http.DefaultHttpContext(),new Microsoft.AspNetCore.Routing.RouteData(),new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor());
        var values=new Microsoft.AspNetCore.Mvc.ModelBinding.FormValueProvider(Microsoft.AspNetCore.Mvc.ModelBinding.BindingSource.Form,new Microsoft.AspNetCore.Http.FormCollection(new Dictionary<string,Microsoft.Extensions.Primitives.StringValues>{{name,"1234.50"}}),System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));
        var context=Microsoft.AspNetCore.Mvc.ModelBinding.DefaultModelBindingContext.CreateBindingContext(action,values,metadata.GetMetadataForType(typeof(decimal?)),null,name);
        await new NativeDecimalBinder(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance).BindModelAsync(context);Assert.Equal(1234.50m,context.Result.Model);
    }
    // TEST ONLY HTTP contract. PostgreSQL/OIDC proof is a separate real-user journey.
    [Fact] public async Task V2_optional_conditions_roundtrip_keeps_immutable_purpose_independent_values_and_legacy()
    {
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../backend/src/RealEstate.Web"));
        await using var app=Program.Build(["--contentRoot",root],b=>{UiFixture.Configure(b);b.WebHost.UseTestServer();});await app.StartAsync();using var client=app.GetTestClient();
        var c=new CaptureData();c.Values.Sale.Authorized=123456.78m;c.Values.Sale.Conditions="Sale";c.Values.Sale.Negotiable=true;c.Values.Rental.Authorized=100.5m;c.Values.Rental.Conditions="Rental";c.Values.Rental.Period="monthly";
        var response=await client.PostAsJsonAsync("/api/v2/properties",new PropertyInputV2(Type:"Casa",Purpose:"Venda",Price:777m,Capture:c));Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var p=(await response.Content.ReadFromJsonAsync<PropertyV2>())!;
        var patch=await client.PatchAsJsonAsync("/api/v2/properties/"+p.Id,new{version=p.Version,data=new{capture=new{values=new{rental=new{conditions="Edited independently"}}}}});Assert.Equal(HttpStatusCode.OK,patch.StatusCode);
        var reopened=(await client.GetFromJsonAsync<PropertyV2>("/api/v2/properties/"+p.Id))!;
        Assert.Equal(777m,reopened.Data.Price);Assert.Equal(123456.78m,reopened.Data.Capture!.Values.Sale.Authorized);Assert.Equal("Sale",reopened.Data.Capture.Values.Sale.Conditions);
        Assert.Equal("Edited independently",reopened.Data.Capture.Values.Rental.Conditions);Assert.Equal("Venda",reopened.Data.Purpose);Assert.Equal("sale",Assert.Single(CommercialValues.Active(reopened.Data)).Key);
        var listing=await client.GetStringAsync("/");Assert.Contains("Valores do an",listing);Assert.Contains("data-active-operation=\"sale\"",listing);Assert.DoesNotContain("data-active-operation=\"rental\"",listing);
    }
}
