using RealEstate.Domain;
using Xunit;
namespace RealEstate.Tests;
public class SharedInheritanceTests
{
    [Fact] public void Legacy_does_not_opt_in_and_explicit_exclusion_survives_shared_changes()
    {
        var data=new CaptureData();var pool=new CaptureSelection{GroupId="collective-leisure",OptionId="pool",Selected=true};
        var common=new CondominiumInput("Example","residential",Characteristics:[pool]);
        Assert.Empty(CollectiveInheritance.Resolve(data,common));data.Condominium.Inherit=true;
        Assert.Single(CollectiveInheritance.Resolve(data,common));Assert.Empty(data.Selections);
        data.Selections.Add(new(){GroupId=pool.GroupId,OptionId=pool.OptionId,SharedOverride=false});
        Assert.Empty(CollectiveInheritance.Resolve(data,common));Assert.False(data.Selections[0].SharedOverride);
    }
}
