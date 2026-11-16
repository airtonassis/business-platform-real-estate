using RealEstate.Domain;
using RealEstate.Web;
using Xunit;
namespace RealEstate.Tests;
public sealed class IncrementATests
{
    [Fact] public void Rental_apartment_has_unit_area_without_land_dimensions_or_exchange_and_keeps_values()
    {
        var p=new PropertyInputV2(Type:"Apartamento",Purpose:"Locação",IsInCondominium:true,Capture:new(){Areas=new(){Total=300,Front=12},Values=new(){AcceptsExchange=true}});
        Assert.False(CaptureAdaptation.Applies("land-dimensions",p));Assert.False(CaptureAdaptation.Applies("exchange-allowed",p));Assert.False(CaptureAdaptation.Applies("exchange",p));
        Assert.True(CaptureAdaptation.Applies("rental",p));Assert.False(CaptureAdaptation.Applies("sale",p));Assert.True(CaptureAdaptation.Applies("condominium",p));
        Assert.Equal(300,p.Capture!.Areas.Total);Assert.Equal(12,p.Capture.Areas.Front);Assert.True(p.Capture.Values.AcceptsExchange);
        Assert.True(CaptureAdaptation.Applies("land-dimensions",p with{Type="Casa"}));Assert.True(CaptureAdaptation.Applies("exchange",p with{Purpose="Venda e locação"}));
        Assert.Equal("land-dimensions",CaptureFields.All.Single(f=>f.Path=="Areas.Total").When);
        Assert.Equal("exchange-allowed",CaptureFields.All.Single(f=>f.Path=="Values.AcceptsExchange").When);
    }
    [Fact] public void Shared_composition_changes_origin_without_materializing_values_or_destroying_exceptions()
    {
        var c=new CaptureData{Condominium=new(){Id=Guid.NewGuid(),Inherit=true},Selections=[new(){GroupId="collective-leisure",OptionId="pool",SharedOverride=false},new(){GroupId="collective-services",OptionId="elevator",SharedOverride=true,Detail="Exceção da unidade"}]};
        var a=new CondominiumInput("A","residential",Characteristics:[new(){GroupId="collective-leisure",OptionId="pool",Selected=true}]);
        var b=new CondominiumInput("B","residential",Characteristics:[new(){GroupId="collective-security",OptionId="cameras",Selected=true}]);
        Assert.Single(CollectiveInheritance.Resolve(c,a));Assert.Equal(2,CollectiveInheritance.Resolve(c,b).Count);Assert.Equal(2,c.Selections.Count);
        c.Condominium.Inherit=false;Assert.Empty(CollectiveInheritance.Resolve(c,null));Assert.Equal(2,c.Selections.Count);Assert.False(c.Selections[0].SharedOverride);Assert.True(c.Selections[1].SharedOverride);
    }
}
