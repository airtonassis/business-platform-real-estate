using RealEstate.Web;
using Xunit;
namespace RealEstate.Tests;
public sealed class BrlTests
{
    [Theory]
    [InlineData(null,"")][InlineData("100","100,00")][InlineData("100.5","100,50")]
    [InlineData("1234567.89","1.234.567,89")][InlineData("0","0,00")]
    public void Decimal_display_uses_two_brazilian_places_and_keeps_null_empty(string? raw,string expected)
    {Assert.Equal(expected,Brl.Format(raw is null?null:decimal.Parse(raw,System.Globalization.CultureInfo.InvariantCulture)));}
    [Fact] public void Percentages_are_not_currency_metadata()
    {Assert.All(CaptureFields.All.Where(f=>f.Path.EndsWith("Percent")),f=>Assert.Equal("percent",f.Kind));Assert.All(CaptureFields.All.Where(f=>f.Kind=="money"),f=>Assert.DoesNotContain("%",f.Label));}
    [Fact] public async Task Canonical_fee_amount_stays_decimal_even_with_portuguese_form_culture()
    {
        var metadata=new Microsoft.AspNetCore.Mvc.ModelBinding.EmptyModelMetadataProvider();
        var action=new Microsoft.AspNetCore.Mvc.ActionContext(new Microsoft.AspNetCore.Http.DefaultHttpContext(),new Microsoft.AspNetCore.Routing.RouteData(),new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor());
        var values=new Microsoft.AspNetCore.Mvc.ModelBinding.FormValueProvider(Microsoft.AspNetCore.Mvc.ModelBinding.BindingSource.Form,new Microsoft.AspNetCore.Http.FormCollection(new Dictionary<string,Microsoft.Extensions.Primitives.StringValues>{{"Fees.SaleAmount","1234.50"}}),System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));
        var context=Microsoft.AspNetCore.Mvc.ModelBinding.DefaultModelBindingContext.CreateBindingContext(action,values,metadata.GetMetadataForType(typeof(decimal?)),null,"Fees.SaleAmount");
        await new NativeDecimalBinder(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance).BindModelAsync(context);
        Assert.Equal(1234.50m,context.Result.Model);
    }
}
