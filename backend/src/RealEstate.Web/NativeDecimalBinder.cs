using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Binders;
namespace RealEstate.Web;
// HTML input[type=number] posts an invariant decimal, independent of its visual locale.
// Monetary components and native fee controls also post invariant decimals. JSON transport is unchanged.
public sealed class NativeDecimalBinder(ILoggerFactory logger):IModelBinder
{
    public Task BindModelAsync(ModelBindingContext context)
    {
        if(!context.ModelName.StartsWith("Input.",StringComparison.Ordinal)&&!context.ModelName.StartsWith("Fees.",StringComparison.Ordinal)&&context.ModelName is not("Filter.MinPrice" or "Filter.MaxPrice"))return new SimpleTypeModelBinder(context.ModelType,logger).BindModelAsync(context);
        var value=context.ValueProvider.GetValue(context.ModelName);
        if(value==ValueProviderResult.None)return Task.CompletedTask;
        context.ModelState.SetModelValue(context.ModelName,value);
        if(string.IsNullOrWhiteSpace(value.FirstValue)&&Nullable.GetUnderlyingType(context.ModelType) is not null)context.Result=ModelBindingResult.Success(null);
        else if(decimal.TryParse(value.FirstValue,NumberStyles.AllowLeadingSign|NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var number))context.Result=ModelBindingResult.Success(number);
        else context.ModelState.TryAddModelError(context.ModelName,"Informe um número válido.");
        return Task.CompletedTask;
    }
}
public sealed class NativeDecimalBinderProvider:IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context)=>context.Metadata.ModelType==typeof(decimal)||context.Metadata.ModelType==typeof(decimal?)?new BinderTypeModelBinder(typeof(NativeDecimalBinder)):null;
}
