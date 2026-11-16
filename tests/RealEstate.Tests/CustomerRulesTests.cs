using RealEstate.Domain;
using Xunit;
namespace RealEstate.Tests;
public sealed class CustomerRulesTests
{
    private static CustomerInput Valid()=>new("Maria","Física","(11) 99999-8888",null,"Indicação","Locação","subject-1");
    [Theory]
    [InlineData("Compra")]
    [InlineData("Locação")]
    [InlineData("Temporada")]
    [InlineData("Captação de imóveis")]
    public void Accepts_each_primary_interest(string interest)=>Assert.Equal(interest,CustomerRules.Validate(Valid() with {Interest=interest}).Value!.Interest);
    [Fact] public void Quick_registration_requires_contact_but_not_document(){
        Assert.True(CustomerRules.Validate(Valid()).IsSuccess);
        Assert.Equal("Customer.ContactRequired",CustomerRules.Validate(Valid() with {Phone=null}).Code);
        Assert.True(CustomerRules.Validate(Valid() with {Phone=null,Email="maria@example.test"}).IsSuccess);
    }
    [Fact] public void Normalizes_contacts_without_changing_name_identity(){
        var result=CustomerRules.Validate(Valid() with {Name=" Maria ",Email=" MARIA@example.test "});
        Assert.Equal("Maria",result.Value!.Name);Assert.Equal("11999998888",result.Value.Phone);Assert.Equal("maria@example.test",result.Value.Email);
    }
    [Theory][InlineData("Name")][InlineData("PersonType")][InlineData("Responsible")][InlineData("Email")][InlineData("Document")][InlineData("Interest")]
    public void Rejects_invalid_data(string field){
        var input=Valid();input=field switch {"Name"=>input with{Name=" "},"PersonType"=>input with{PersonType="X"},"Responsible"=>input with{ResponsibleSubject=" "},"Email"=>input with{Email="invalid"},"Document"=>input with{Document="123"},_=>input with{Interest="X"}};
        Assert.False(CustomerRules.Validate(input).IsSuccess);
    }
}
