using RealEstate.Domain;
using Xunit;
namespace RealEstate.Tests;
public sealed class NegotiationRulesTests
{
    [Fact] public void Opening_requires_stable_request_and_existing_links()
    {
        Assert.False(NegotiationRules.Valid(new(Guid.Empty,Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid())));
        Assert.False(NegotiationRules.Valid(new(Guid.NewGuid(),Guid.Empty,Guid.NewGuid(),Guid.NewGuid())));
        Assert.False(NegotiationRules.Valid(new(Guid.NewGuid(),Guid.NewGuid(),Guid.Empty,Guid.NewGuid())));
        Assert.False(NegotiationRules.Valid(new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.Empty)));
        Assert.True(NegotiationRules.Valid(new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid())));
    }
}
