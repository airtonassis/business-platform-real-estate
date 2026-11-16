namespace RealEstate.Domain;
public sealed record CondominiumInput(string Name,string Kind,int? Towers=null,int? Floors=null,string? Notes=null,IReadOnlyList<CaptureSelection>? Characteristics=null);
public sealed record CondominiumProfile(Guid Id,CondominiumInput Data,long Version=1,IReadOnlyList<ProfileRevision>? History=null);
public interface ICondominiumStore
{
    Task<CondominiumProfile?> FindCondominiumAsync(AccessScope scope,Guid id,CancellationToken token);
    Task<Outcome<CondominiumProfile>> UpdateCondominiumAsync(AccessScope scope,Guid id,long version,CondominiumInput input,CancellationToken token);
    Task<IReadOnlyList<CondominiumProfile>> SearchCondominiumsAsync(AccessScope scope,string query,CancellationToken token);
    Task<Outcome<CondominiumProfile>> CreateCondominiumAsync(AccessScope scope,CondominiumInput input,CancellationToken token);
}
