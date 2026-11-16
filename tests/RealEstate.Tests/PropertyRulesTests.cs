using RealEstate.Domain;
using Xunit;
namespace RealEstate.Tests;
public class PropertyRulesTests
{
    private static PropertyInput Valid() => new("IM-001", "Casa", Guid.NewGuid(), "Residencial", "Casa", "Venda", "Rua Central, 10", "01310-100", -23.55m, -46.63m, 350000m);
    [Fact] public void Draft_keeps_validated_data() { var result=PropertyRules.Validate(Valid()); Assert.True(result.IsSuccess); }
    [Fact] public void Owner_is_mandatory() => Assert.Equal("Property.OwnerRequired",PropertyRules.Validate(Valid() with {OwnerId=Guid.Empty}).Code);
    [Fact] public void Price_must_be_positive() => Assert.Equal("Property.PriceInvalid",PropertyRules.Validate(Valid() with {Price=0}).Code);
    [Theory] [InlineData("")] [InlineData("   ")] public void Title_is_required(string value) => Assert.Equal("Property.TitleRequired",PropertyRules.Validate(Valid() with {Title=value}).Code);
    [Theory] [InlineData("123")] [InlineData("abcdefgh")] [InlineData("١٢٣٤٥٦٧٨")] public void Cep_must_be_valid(string value) => Assert.Equal("Property.PostalCodeInvalid",PropertyRules.Validate(Valid() with {PostalCode=value}).Code);
    [Fact] public void Coordinates_must_be_in_range() => Assert.Equal("Property.LocationInvalid",PropertyRules.Validate(Valid() with {Latitude=91}).Code);
    [Fact] public void Unknown_property_type_is_denied() => Assert.Equal("Property.TypeInvalid",PropertyRules.Validate(Valid() with {Type="Inventado"}).Code);
}

