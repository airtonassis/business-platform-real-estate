using RealEstate.Domain;
using Xunit;
namespace RealEstate.Tests;
public class OwnerProfileRulesTests
{
    [Theory][InlineData("PF","529.982.247-25",true)][InlineData("PF","11111111111",false)][InlineData("PF","52998224726",false)][InlineData("PJ","11.222.333/0001-81",true)][InlineData("PJ","11222333000182",false)]
    public void Fiscal_identification_checks_kind_format_and_check_digits(string kind,string id,bool expected)=>Assert.Equal(expected,OwnerProfileRules.Validate(new("Synthetic",kind,id)).IsSuccess);
}
