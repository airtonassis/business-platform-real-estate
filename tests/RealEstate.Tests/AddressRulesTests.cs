using RealEstate.Domain;
using Xunit;
namespace RealEstate.Tests;

public sealed class AddressRulesTests
{
    [Theory]
    [InlineData("01310-100","01310100")]
    [InlineData("01310100","01310100")]
    [InlineData("01000-000","01000000")]
    [InlineData("00000000","00000000")]
    public void Normalize_keeps_eight_digits_and_does_not_claim_postal_existence(string input,string expected)
        =>Assert.Equal(expected,AddressRules.NormalizePostalCode(input).Value);

    [Theory]
    [InlineData("123")][InlineData("01310--100")][InlineData("abcdefgh")]
    [InlineData("١٢٣٤٥٦٧٨")][InlineData(" 01310100 ")]
    public void Invalid_postal_input_is_not_silently_repaired(string input)
        =>Assert.Equal("Property.PostalCodeInvalid",AddressRules.NormalizePostalCode(input).Code);

    [Fact] public void Optional_draft_fields_are_distinct_from_legacy_required_fields()
    {
        Assert.True(AddressRules.NormalizePostalCode(null,required:false).IsSuccess);
        Assert.Null(AddressRules.NormalizePostalCode(null,required:false).Value);
        Assert.Equal("Property.PostalCodeInvalid",AddressRules.NormalizePostalCode(null).Code);
        Assert.True(AddressRules.CoordinatesValid(null,null,required:false));
        Assert.False(AddressRules.CoordinatesValid(null,null,required:true));
        Assert.True(AddressRules.CoordinatesValid(0,0,required:true));
    }

    [Theory]
    [InlineData(91,0)][InlineData(0,181)][InlineData(-91,0)][InlineData(0,-181)]
    public void Coordinates_outside_limits_are_invalid(int latitude,int longitude)
        =>Assert.False(AddressRules.CoordinatesValid(latitude,longitude,required:false));

    [Fact] public void Partial_coordinate_pair_is_not_a_complete_point()
    {
        Assert.False(AddressRules.CoordinatesValid(0,null,required:false));
        Assert.False(AddressRules.CoordinatesValid(null,0,required:false));
    }
}
