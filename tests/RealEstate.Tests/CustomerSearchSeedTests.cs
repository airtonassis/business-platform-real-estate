using RealEstate.Web.Pages;
using Xunit;
namespace RealEstate.Tests;
public sealed class CustomerSearchSeedTests
{
    [Theory]
    [InlineData("  Maria Silva  ","Maria Silva",null)]
    [InlineData("529.982.247-25",null,"52998224725")]
    [InlineData("52998224725",null,"52998224725")]
    [InlineData("11987654321",null,null)]
    [InlineData("123",null,null)]
    [InlineData("123 Maria",null,null)]
    public void Only_name_or_cpf_is_reused(string query,string? name,string? document)
    {var result=CustomerSearchSeed.Classify(query);Assert.Equal(name,result.Name);Assert.Equal(document,result.Document);}
}
