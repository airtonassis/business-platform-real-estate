using RealEstate.Domain;
using Xunit;
namespace RealEstate.Tests;
public sealed class StructuredAddressTests
{
    [Fact] public void Empty_address_is_a_partial_draft_not_an_invented_location()
    {
        var result=StructuredAddressRules.Validate(new());
        Assert.True(result.IsSuccess);Assert.Null(result.Value!.PostalCode);Assert.Null(result.Value.Latitude);
    }
    [Fact] public void Normalize_manual_generic_postal_and_uf_without_claiming_catalog_lookup()
    {
        var result=StructuredAddressRules.Validate(new(PostalCode:"01000-000",State:"sp",MunicipalityIbgeCode:"3550308",Municipality:"São Paulo",WithoutNumber:true));
        Assert.True(result.IsSuccess);Assert.Equal("01000000",result.Value!.PostalCode);Assert.Equal("SP",result.Value.State);
    }
    [Theory][InlineData("123")][InlineData("abcdefgh")]
    public void Invalid_ibge_format_is_rejected(string code)=>Assert.False(StructuredAddressRules.Validate(new(MunicipalityIbgeCode:code)).IsSuccess);
    [Fact] public void Number_and_sn_cannot_contradict()=>Assert.False(StructuredAddressRules.Validate(new(Number:"10",WithoutNumber:true)).IsSuccess);
    [Fact] public void Invalid_uf_or_partial_point_is_rejected()
    {
        Assert.False(StructuredAddressRules.Validate(new(State:"XX")).IsSuccess);
        Assert.False(StructuredAddressRules.Validate(new(Latitude:0)).IsSuccess);
    }
}
