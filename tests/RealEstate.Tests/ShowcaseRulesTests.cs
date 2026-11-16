using RealEstate.Domain;
using Xunit;

namespace RealEstate.Tests;

public sealed class ShowcaseRulesTests
{
    [Theory]
    [InlineData(PropertyLifecycle.Draft, false)]
    [InlineData(PropertyLifecycle.InReview, false)]
    [InlineData(PropertyLifecycle.Inactive, false)]
    [InlineData(PropertyLifecycle.Available, true)]
    [InlineData(PropertyLifecycle.InNegotiation, true)]
    [InlineData(PropertyLifecycle.Negotiated, true)]
    public void OnlyCommerciallyReleasedPropertiesCanBeSelected(string status, bool expected)
        => Assert.Equal(expected, ShowcaseRules.CanPrepare(status));

    [Fact]
    public void AuthorizationNeedsAnExplicitSafeSelection()
    {
        Assert.False(ShowcaseRules.CanAuthorize(PropertyLifecycle.Available, new([]), false));
        Assert.False(ShowcaseRules.CanAuthorize(PropertyLifecycle.Available, new(["title", "internalReference"]), true));
        Assert.False(ShowcaseRules.CanAuthorize(PropertyLifecycle.Available, new(["description"]), true));
        Assert.True(ShowcaseRules.CanAuthorize(PropertyLifecycle.Available, new(["description"], Description: "Texto revisado"), true));
        Assert.False(ShowcaseRules.CanAuthorize(PropertyLifecycle.Draft, new(["title", "price"]), true));
        Assert.True(ShowcaseRules.CanAuthorize(PropertyLifecycle.Negotiated, new(["title", "price"]), true));
    }
}
