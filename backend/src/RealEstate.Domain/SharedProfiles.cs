namespace RealEstate.Domain;
public sealed record ProfileRevision(long Version,string Actor,DateTimeOffset At,string Data);
public static class CollectiveInheritance
{
    public static readonly AdaptationExpression SelectionRule=CaptureAdaptation.Either(
        CaptureAdaptation.Every(CaptureAdaptation.Except(CaptureAdaptation.Is("inherit","true")),CaptureAdaptation.Is("local","true")),
        CaptureAdaptation.Every(CaptureAdaptation.Is("inherit","true"),CaptureAdaptation.Either(CaptureAdaptation.Is("override","true"),
            CaptureAdaptation.Every(CaptureAdaptation.Except(CaptureAdaptation.Is("override","false")),CaptureAdaptation.Either(CaptureAdaptation.Is("local","true"),CaptureAdaptation.Is("shared","true"))))));
    public static IReadOnlyList<CaptureSelection> Resolve(CaptureData capture,CondominiumInput? shared)
    {
        var local=capture.Selections.Where(s=>s.GroupId.StartsWith("collective-",StringComparison.Ordinal)).ToArray();
        if(capture.Condominium.Inherit!=true)return local.Where(s=>s.Selected).ToArray();
        var inherited=shared?.Characteristics??[];
        return inherited.Concat(local).DistinctBy(s=>(s.GroupId,s.OptionId)).Select(item=>{
            var individual=local.FirstOrDefault(s=>s.GroupId==item.GroupId&&s.OptionId==item.OptionId);
            var common=inherited.FirstOrDefault(s=>s.GroupId==item.GroupId&&s.OptionId==item.OptionId);
            var selected=CaptureAdaptation.Evaluate(SelectionRule,key=>key switch{"inherit"=>"true","override"=>individual?.SharedOverride?.ToString().ToLowerInvariant(),"local"=>individual?.Selected==true?"true":"false","shared"=>common?.Selected==true?"true":"false",_=>null});
            return new CaptureSelection{GroupId=item.GroupId,OptionId=item.OptionId,Label=common?.Label??individual?.Label,Selected=selected,Detail=individual?.Detail??common?.Detail};
        }).Where(s=>s.Selected).ToArray();
    }
}
