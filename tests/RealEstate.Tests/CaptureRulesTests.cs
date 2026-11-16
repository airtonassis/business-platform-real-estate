using RealEstate.Domain;
using Xunit;
namespace RealEstate.Tests;
public class CaptureRulesTests
{
    [Fact] public void Empty_capture_is_valid_and_unknown_counts_remain_null()
    {var c=new CaptureData();Assert.True(CaptureRules.Validate(c).IsSuccess);Assert.Null(c.Rooms.Bedrooms);}
    [Fact] public void Suites_are_included_in_bedrooms()
    {var c=new CaptureData();c.Rooms.Bedrooms=2;c.Rooms.Suites=3;Assert.False(CaptureRules.Validate(c).IsSuccess);c.Rooms.Bedrooms=null;Assert.True(CaptureRules.Validate(c).IsSuccess);}
    [Fact] public void No_guarantee_is_exclusive_even_for_forged_requests()
    {var c=new CaptureData {Selections=[new(){GroupId="guarantees",OptionId="none",Selected=true},new(){GroupId="guarantees",OptionId="guarantor",Selected=true}]};Assert.False(CaptureRules.Validate(c).IsSuccess);}
    [Fact] public void Other_choices_require_stable_ids_and_normalized_unique_labels()
    {var c=new CaptureData{Selections=[new(){GroupId="security",OptionId="custom:"+Guid.NewGuid(),Label="Alarme próprio",Selected=true},new(){GroupId="security",OptionId="custom:"+Guid.NewGuid(),Label=" ALARME PROPRIO ",Selected=true}]};Assert.False(CaptureRules.Validate(c).IsSuccess);}
    [Fact] public void Money_requires_decimal_precision_and_explicit_area_unit()
    {var c=new CaptureData();c.Values.Sale.Authorized=12.345m;Assert.False(CaptureRules.Validate(c).IsSuccess);c.Values.Sale.Authorized=null;c.Areas.Total=12;c.Areas.Unit=null;Assert.False(CaptureRules.Validate(c).IsSuccess);}
    [Fact] public void Hidden_selections_are_valid_and_not_erased_by_rules()
    {var c=new CaptureData{Selections=[new(){GroupId="rural",OptionId="barn",Selected=true,Detail="Existente"}]};var r=CaptureRules.Validate(c);Assert.True(r.IsSuccess);Assert.Equal("Existente",r.Value!.Selections[0].Detail);}
}
