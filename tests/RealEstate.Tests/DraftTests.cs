using RealEstate.Application;
using RealEstate.Domain;
using Xunit;
namespace RealEstate.Tests;
public sealed class DraftTests
{
    [Fact] public void Empty_business_data_is_valid_and_remains_absent()
    {
        var result=PropertyServiceV2.Validate(System.Text.Json.JsonSerializer.Deserialize<PropertyInputV2>("{}")!);
        Assert.True(result.IsSuccess,result.Code);
        Assert.Null(result.Value!.Code);Assert.Null(result.Value.Title);
        Assert.Null(result.Value.OwnerId);Assert.Null(result.Value.Price);
    }
    [Theory][InlineData(-1)][InlineData(1.001)]
    public void Supplied_invalid_prices_are_not_draft_omissions(decimal price)
        =>Assert.False(PropertyServiceV2.Validate(new(Price:price)).IsSuccess);
}
