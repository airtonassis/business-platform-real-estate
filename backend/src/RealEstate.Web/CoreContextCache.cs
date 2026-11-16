using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;
using RealEstate.Domain;
namespace RealEstate.Web;
public sealed record CoreContextSnapshot(int SchemaVersion,string State,string Issuer,string Subject,Guid OrganizationId,
    string? OrganizationName,string? Role,Guid? ActiveEstablishmentId,long ContextVersion,
    EstablishmentAccess[]? Establishments,CoreAccess.Grant[]? Grants,DateTimeOffset IssuedAtUtc,DateTimeOffset ExpiresAtUtc,Guid MembershipId=default);
/// <summary>Server-only authorization leases; never extend a lease on failure or key it by client tenant IDs.</summary>
public sealed class CoreContextCache:IDisposable
{
    private readonly MemoryCache cache=new(new MemoryCacheOptions{SizeLimit=1024});
    private readonly ConcurrentDictionary<string,Lazy<Task<Outcome<CoreContextSnapshot>>>> pending=new();
    public void Invalidate(string key)=>cache.Remove(key);
    public async Task<Outcome<CoreContextSnapshot>> GetAsync(string key,Func<Task<Outcome<CoreContextSnapshot>>> load,CancellationToken token)
    {
        if(cache.TryGetValue<CoreContextSnapshot>(key,out var value)&&value!.ExpiresAtUtc>DateTimeOffset.UtcNow)return new(value);
        if(pending.Count>=128)return Outcome<CoreContextSnapshot>.Fail("Dependency.Unavailable");
        var lazy=pending.GetOrAdd(key,_=>new(async()=>{
            var result=await load();
            if(result.IsSuccess && result.Value!.ExpiresAtUtc>DateTimeOffset.UtcNow)
                cache.Set(key,result.Value,new MemoryCacheEntryOptions{AbsoluteExpiration=result.Value.ExpiresAtUtc,Size=1});
            return result;
        },LazyThreadSafetyMode.ExecutionAndPublication));
        try{return await lazy.Value.WaitAsync(token);}
        finally{pending.TryRemove(new KeyValuePair<string,Lazy<Task<Outcome<CoreContextSnapshot>>>>(key,lazy));}
    }
    public void Dispose()=>cache.Dispose();
}
