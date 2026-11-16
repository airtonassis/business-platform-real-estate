using System.Text.Json;
using RealEstate.Application;
using RealEstate.Domain;
using Xunit;
namespace RealEstate.Tests;

public sealed class IdentificationTests
{
    [Fact] public void Combined_purpose_and_optional_identification_round_trip()
    {
        var data=JsonSerializer.Deserialize<PropertyInputV2>("""{"Purpose":"Venda e locação","Summary":" Vista livre ","InternalReference":" Pasta 12 "}""")!;
        var result=PropertyServiceV2.Validate(data);
        Assert.True(result.IsSuccess,result.Code);
        var json=JsonSerializer.SerializeToElement(result.Value);
        Assert.Equal("Vista livre",json.GetProperty("Summary").GetString());
        Assert.Equal("Pasta 12",json.GetProperty("InternalReference").GetString());
    }
    [Theory][InlineData("Summary",2001)][InlineData("InternalReference",101)]
    public void Supplied_identification_text_has_limits(string field,int length)
    {
        var input=JsonSerializer.Deserialize<PropertyInputV2>(JsonSerializer.Serialize(new Dictionary<string,string>{{field,new('x',length)}}))!;
        Assert.False(PropertyServiceV2.Validate(input).IsSuccess);
    }
}
