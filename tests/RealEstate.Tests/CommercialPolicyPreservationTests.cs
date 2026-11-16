using RealEstate.Domain;
using Xunit;
namespace RealEstate.Tests;
public sealed class CommercialPolicyPreservationTests
{
    [Fact]public void Applying_policy_keeps_collective_inheritance_exceptions_and_hidden_values()
    {
        var shared=Guid.NewGuid();var current=new CaptureData{Condominium=new(){Id=shared,Inherit=true,ObservedVersion=12},Selections=[new(){GroupId="collective-leisure",OptionId="pool",SharedOverride=false,Detail="Local exception"},new(){GroupId="security",OptionId="alarm",Selected=true}]};current.Values.Rental.Authorized=1200;current.Areas.Land=1000;
        var policy=new CommercialPolicyRevision(Guid.NewGuid(),2,"Approved",new("Policy","Explicit test",new(new(){SalePercent=5},[])),"author","manager",DateTimeOffset.UtcNow,[]);
        var applied=CommercialPolicyRules.Apply(policy,current);Assert.Equal(shared,applied.Condominium.Id);Assert.True(applied.Condominium.Inherit);Assert.Equal(12,applied.Condominium.ObservedVersion);var exception=applied.Selections.Single(s=>s.GroupId=="collective-leisure");Assert.False(exception.SharedOverride);Assert.Equal("Local exception",exception.Detail);Assert.Equal(1200,applied.Values.Rental.Authorized);Assert.Equal(1000,applied.Areas.Land);Assert.True(applied.Selections.Single(s=>s.GroupId=="security").Selected);Assert.Null(current.Policy);
    }
}
