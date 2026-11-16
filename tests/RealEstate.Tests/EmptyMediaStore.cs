using RealEstate.Domain;
namespace RealEstate.Tests;
// Existing UI-only regressions have no media persistence. Real media proofs use PostgreSQL.
internal sealed class EmptyMediaStore:IPropertyMediaStore
{
    public Task<MediaCollection?> ListMediaAsync(AccessScope scope,Guid propertyId,CancellationToken token)=>Task.FromResult<MediaCollection?>(MediaRules.Collection([]));
    public Task<MediaContent?> ReadMediaAsync(AccessScope scope,Guid propertyId,Guid mediaId,CancellationToken token)=>Task.FromResult<MediaContent?>(null);
    public Task<IReadOnlyDictionary<Guid,Guid>> CoversAsync(AccessScope scope,Guid[] properties,CancellationToken token)=>Task.FromResult<IReadOnlyDictionary<Guid,Guid>>(new Dictionary<Guid,Guid>());
    public Task<Outcome<MediaCollection>> ChangeMediaAsync(AccessScope scope,Guid propertyId,string revision,MediaChange change,CancellationToken token)=>Task.FromResult(Outcome<MediaCollection>.Fail("Dependency.Unavailable"));
}
