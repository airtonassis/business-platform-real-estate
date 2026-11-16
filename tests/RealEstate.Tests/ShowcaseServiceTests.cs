using RealEstate.Application;
using RealEstate.Domain;
using Xunit;

namespace RealEstate.Tests;

public sealed class ShowcaseServiceTests
{
    private static readonly Guid Org = Guid.NewGuid(), Unit = Guid.NewGuid(), Other = Guid.NewGuid();
    private sealed class Grant(string? denied = null, Guid? active = null) : ICoreAccess
    {
        public Task<Outcome<AccessScope>> RequireAsync(string resource, string permission, CancellationToken token) =>
            Task.FromResult(permission == denied ? Outcome<AccessScope>.Fail("Access.Forbidden") :
                new Outcome<AccessScope>(new(Org, "operator", "Synthetic", [new(Unit, "Branch")], ActiveEstablishmentId: active ?? Unit)));
    }
    private sealed class Properties(PropertyV2 item) : IPropertyStoreV2
    {
        public Task<PropertyV2?> FindV2Async(AccessScope scope, Guid id, CancellationToken token) =>
            Task.FromResult<PropertyV2?>(scope.OrganizationId == item.OrganizationId && id == item.Id ? item : null);
        public Task<WorkspaceV2> ListV2Async(AccessScope scope, CancellationToken token) => throw new NotSupportedException();
        public Task<Outcome<PropertyV2>> SaveV2Async(AccessScope scope, PropertyInputV2 input, Guid? id, long version, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class Media : IPropertyMediaStore
    {
        public Task<MediaCollection?> ListMediaAsync(AccessScope scope, Guid id, CancellationToken token) => Task.FromResult<MediaCollection?>(new([], "x"));
        public Task<MediaContent?> ReadMediaAsync(AccessScope scope, Guid propertyId, Guid mediaId, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Guid, Guid>> CoversAsync(AccessScope scope, Guid[] properties, CancellationToken token) => throw new NotSupportedException();
        public Task<Outcome<MediaCollection>> ChangeMediaAsync(AccessScope scope, Guid propertyId, string revision, MediaChange change, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class Store : IShowcaseStore
    {
        public int Writes;
        public ShowcaseEntry? Entry;
        public Task<ShowcaseEntry?> FindAsync(AccessScope scope, Guid propertyId, CancellationToken token) => Task.FromResult(Entry);
        public Task<Outcome<ShowcaseEntry>> ChangeAsync(AccessScope scope, Guid propertyId, long version, ShowcaseSelection? selection, string action, CancellationToken token)
        {
            Writes++;
            Entry = new(propertyId, scope.OrganizationId, scope.ActiveEstablishmentId!.Value, version + 1,
                action == "authorize" ? "Authorized" : action == "withdraw" ? "Withdrawn" : "Prepared",
                selection ?? new(["title"]), scope.Subject, DateTimeOffset.UtcNow);
            return Task.FromResult<Outcome<ShowcaseEntry>>(new(Entry));
        }
    }
    private static PropertyV2 Property(string status = PropertyLifecycle.Available) => new(
        Guid.NewGuid(), Org, new(Title: "Synthetic", EstablishmentId: Unit), 2, status);

    [Fact]
    public async Task EffectiveRightsAndActiveBranchGateEachAction()
    {
        var property = Property(); var store = new Store(); var selection = new ShowcaseSelection(["title"]);
        Assert.Equal("Access.Forbidden", (await new ShowcaseService(new Grant("PROPERTY_APPROVE"), new Properties(property), new Media(), store)
            .ChangeAsync(property.Id, new(1, selection), "authorize")).Code);
        Assert.Equal("Access.Forbidden", (await new ShowcaseService(new Grant(active: Other), new Properties(property), new Media(), store)
            .ChangeAsync(property.Id, new(0, selection), "prepare")).Code);
        Assert.Equal("Access.Forbidden", (await new ShowcaseService(new Grant(active: Other), new Properties(property), new Media(), store)
            .FindAsync(property.Id)).Code);
        Assert.Equal(0, store.Writes);
        Assert.True((await new ShowcaseService(new Grant(), new Properties(property), new Media(), store)
            .ChangeAsync(property.Id, new(0, selection), "prepare")).IsSuccess);
        Assert.Equal(1, store.Writes);
    }

    [Fact]
    public async Task DraftAndForeignMediaNeverReachPersistence()
    {
        var draft = Property(PropertyLifecycle.Draft); var store = new Store();
        Assert.Equal("Showcase.NotEligible", (await new ShowcaseService(new Grant(), new Properties(draft), new Media(), store)
            .ChangeAsync(draft.Id, new(0, new(["title"])), "prepare")).Code);
        var ready = Property();
        Assert.Equal("Showcase.InvalidSelection", (await new ShowcaseService(new Grant(), new Properties(ready), new Media(), store)
            .ChangeAsync(ready.Id, new(0, new(["title"], [Guid.NewGuid()])), "prepare")).Code);
        Assert.Equal(0, store.Writes);
    }
}
