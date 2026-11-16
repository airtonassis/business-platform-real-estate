using System.Globalization;
namespace RealEstate.Domain;

// Versioned declarative presentation predicates. Browser receives these same expressions.
// These rules never replace authorization or validation of supplied/hidden business data.
public sealed record AdaptationExpression(string? Field=null,string[]? Values=null,AdaptationExpression[]? All=null,AdaptationExpression[]? Any=null,AdaptationExpression? Not=null);
public static class CaptureAdaptation
{
    public const string Version="CAD-ADAPT-001/002:1.1";
    public static AdaptationExpression Is(string field,params string[] values)=>new(Field:field,Values:values);
    public static AdaptationExpression Every(params AdaptationExpression[] items)=>new(All:items);
    public static AdaptationExpression Either(params AdaptationExpression[] items)=>new(Any:items);
    public static AdaptationExpression Except(AdaptationExpression item)=>new(Not:item);
    public static readonly IReadOnlyDictionary<string,AdaptationExpression> Conditions=new Dictionary<string,AdaptationExpression>{
        ["always"]=Every(),["later"]=Either(),["integrated"]=Either(),
        ["rural"]=Is("Type",IdentificationRules.RuralTypes),
        ["commercial"]=Is("Type",PropertyRules.Types.Where(CaptureCatalog.Commercial).ToArray()),
        ["habitable"]=Is("Type",["",..PropertyRules.Types.Where(t=>!CaptureCatalog.Commercial(t)&&t is not("Terreno" or "Empreendimento"))]),
        ["sale"]=Is("Purpose",IdentificationRules.Purposes.Where(IdentificationRules.HasSale).ToArray()),
        ["rental"]=Is("Purpose",IdentificationRules.Purposes.Where(IdentificationRules.HasRental).ToArray()),
        ["seasonal"]=Is("Purpose","Temporada"),["condominium"]=Is("IsInCondominium","true"),
        ["land-dimensions"]=Except(Is("Type","Apartamento")),
        ["exchange-allowed"]=Except(Is("Purpose","Locação")),
        ["exchange"]=Every(Except(Is("Purpose","Locação")),Is("Capture.Values.AcceptsExchange","true")),
        ["exclusive"]=Is("Capture.Acquisition.Exclusive","true"),["alqueire"]=Is("Capture.Areas.Unit","alqueire"),
        ["sale-advertised"]=Every(Is("Purpose",IdentificationRules.Purposes.Where(IdentificationRules.HasSale).ToArray()),Is("Capture.Values.Sale.DifferentAdvertised","true")),
        ["rental-advertised"]=Every(Is("Purpose",IdentificationRules.Purposes.Where(IdentificationRules.HasRental).ToArray()),Is("Capture.Values.Rental.DifferentAdvertised","true")),
        ["seasonal-advertised"]=Every(Is("Purpose","Temporada"),Is("Capture.Values.Seasonal.DifferentAdvertised","true"))};
    public static bool Evaluate(AdaptationExpression rule,Func<string,string?> value)=>rule.All is {} all?all.All(r=>Evaluate(r,value)):
        rule.Any is {} any?any.Any(r=>Evaluate(r,value)):rule.Not is {} not?!Evaluate(not,value):rule.Field is {} field&&rule.Values?.Contains(value(field)??"")==true;
    public static bool Applies(string condition,PropertyInputV2 input)=>Conditions.TryGetValue(condition,out var rule)&&Evaluate(rule,path=>{
        object? value=input;foreach(var part in path.Split('.'))value=value?.GetType().GetProperty(part)?.GetValue(value);
        return value is bool flag?flag?"true":"false":Convert.ToString(value,CultureInfo.InvariantCulture);
    });
}
