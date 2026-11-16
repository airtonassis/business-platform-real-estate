using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using RealEstate.Web;
using Xunit;
namespace RealEstate.Tests;
public class CaptureDecimalTests
{
    [Theory][InlineData("150.5",150.5)][InlineData("510000.25",510000.25)][InlineData("-23.55052",-23.55052)]
    public async Task Native_number_controls_are_invariant_even_when_form_culture_is_portuguese(string raw,decimal expected)
    {
        var metadata=new EmptyModelMetadataProvider();
        var context=DefaultModelBindingContext.CreateBindingContext(new ActionContext(new DefaultHttpContext(),new RouteData(),new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor()),
            new FormValueProvider(BindingSource.Form,new FormCollection(new Dictionary<string,Microsoft.Extensions.Primitives.StringValues>{{"Input.Capture.Areas.Total",raw}}),CultureInfo.GetCultureInfo("pt-BR")),metadata.GetMetadataForType(typeof(decimal?)),null,"Input.Capture.Areas.Total");
        await new NativeDecimalBinder(NullLoggerFactory.Instance).BindModelAsync(context);
        Assert.Equal(expected,context.Result.Model);
    }
}
