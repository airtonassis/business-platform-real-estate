using System.Globalization;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Razor.TagHelpers;
namespace RealEstate.Web;

public static class Brl
{
    private static readonly CultureInfo Culture=CultureInfo.GetCultureInfo("pt-BR");
    public static string Format(decimal? value)=>value?.ToString("N2",Culture)??"";
    public static string Currency(decimal? value)=>value?.ToString("C2",Culture)??"";
}

// The named hidden control retains the existing invariant-decimal form contract.
// Without JavaScript the display stays disabled, so edits cannot silently lose their value.
[HtmlTargetElement("brl-input")]
public sealed class BrlInputTagHelper:TagHelper
{
    public string Name {get;set;}="";
    public string? Id {get;set;}
    public decimal? Value {get;set;}
    public string Min {get;set;}="0";
    public override void Process(TagHelperContext context,TagHelperOutput output)
    {
        output.TagName="input";output.TagMode=TagMode.SelfClosing;
        output.Attributes.Clear();
        output.Attributes.SetAttribute("id",Id??TagBuilder.CreateSanitizedId(Name,"_"));
        output.Attributes.SetAttribute("type","text");output.Attributes.SetAttribute("inputmode","decimal");
        output.Attributes.SetAttribute("data-brl-input","");output.Attributes.SetAttribute("data-brl-min",Min);
        output.Attributes.SetAttribute("value",Brl.Format(Value));output.Attributes.SetAttribute("disabled","disabled");
        var hidden=new TagBuilder("input");hidden.TagRenderMode=TagRenderMode.SelfClosing;
        hidden.Attributes["type"]="hidden";hidden.Attributes["name"]=Name;hidden.Attributes["data-brl-value"]="";
        hidden.Attributes["value"]=Value?.ToString(CultureInfo.InvariantCulture)??"";
        output.PostElement.AppendHtml(hidden);
    }
}
