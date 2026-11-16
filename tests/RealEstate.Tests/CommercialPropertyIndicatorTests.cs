using RealEstate.Domain;
using RealEstate.Web;
using Xunit;

namespace RealEstate.Tests;

public sealed class CommercialPropertyIndicatorTests
{
    private static PropertyV2 Property(CaptureData capture)=>new(Guid.NewGuid(),Guid.NewGuid(),
        new PropertyInputV2("CARD-1","Apartamento",null,"Residencial","Apartamento","Venda e locação",null,Capture:capture),1,"Draft");

    [Fact]
    public void Capture_indicators_are_used_when_no_active_announcement_value_exists()
    {
        var capture=new CaptureData();
        capture.Rooms.Bedrooms=2;capture.Rooms.Parking=1;
        capture.Values.CondominiumFee=350m;capture.Values.CondominiumPeriod="monthly";
        capture.Values.PropertyTax=85m;capture.Values.PropertyTaxPeriod="annual";

        var result=CommercialPropertyIndicators.Resolve(Property(capture),null);

        Assert.Equal(2,result.Bedrooms);Assert.Equal(1,result.Parking);
        Assert.Equal(350m,result.CondominiumFee);Assert.Equal("monthly",result.CondominiumPeriod);
        Assert.Equal(85m,result.PropertyTax);Assert.Equal("annual",result.PropertyTaxPeriod);
    }

    [Fact]
    public void Active_announcement_values_take_precedence_and_keep_explicit_zero_and_unknown_period()
    {
        var capture=new CaptureData();capture.Values.CondominiumFee=350m;capture.Values.CondominiumPeriod="monthly";
        capture.Values.PropertyTax=85m;capture.Values.PropertyTaxPeriod="annual";
        var property=Property(capture);
        var announcement=new ListingAnnouncement(Guid.NewGuid(),"Locação","CARD-1-LOC",CondominiumFee:0m,CondominiumPeriod:"monthly",PropertyTax:12.5m);

        var result=CommercialPropertyIndicators.Resolve(property,announcement);

        Assert.Equal(0m,result.CondominiumFee);Assert.Equal("monthly",result.CondominiumPeriod);
        Assert.Equal(12.5m,result.PropertyTax);Assert.Null(result.PropertyTaxPeriod);
        Assert.Equal("Venda e locação",property.Data.Purpose);
    }

    [Fact]
    public void Missing_announcement_amount_falls_back_to_capture_and_unknown_values_stay_absent()
    {
        var capture=new CaptureData();capture.Values.CondominiumFee=0m;capture.Values.CondominiumPeriod="single";
        var property=Property(capture);
        var announcement=new ListingAnnouncement(Guid.NewGuid(),"Locação","CARD-1-LOC",CondominiumFee:null,CondominiumPeriod:"annual");

        var result=CommercialPropertyIndicators.Resolve(property,announcement);

        Assert.Equal(0m,result.CondominiumFee);Assert.Equal("single",result.CondominiumPeriod);
        Assert.Null(result.PropertyTax);Assert.Null(result.PropertyTaxPeriod);
        Assert.Null(CommercialPropertyIndicators.Resolve(Property(new()),null).Bedrooms);
    }

    [Theory]
    [InlineData("daily","/dia")][InlineData("weekly","/semana")][InlineData("monthly","/mês")]
    [InlineData("annual","/ano")][InlineData("single","pagamento único")]
    [InlineData(null,"período não informado")][InlineData("legacy","período não informado")]
    public void Fee_period_labels_are_explicit_and_never_inferred(string? period,string expected)
        =>Assert.Equal(expected,CommercialCardFormatting.PeriodLabel(period));
}
