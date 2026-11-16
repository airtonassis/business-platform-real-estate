using System.Security.Cryptography;
using System.Text;
namespace RealEstate.Domain;
public sealed record PropertyMedia(Guid Id,string Kind,string Mime,string Name,int Size,int Position,bool IsCover,long Version);
public sealed record MediaCollection(IReadOnlyList<PropertyMedia> Items,string Revision);
public sealed record MediaUpload(string Name,string Mime,Stream Content);
public sealed record MediaFile(string Name,string Mime,string Kind,byte[] Content,string Sha256);
public sealed record MediaContent(string Mime,byte[] Content);
public sealed record MediaChange(string Action,Guid? MediaId=null,IReadOnlyList<Guid>? Order=null,IReadOnlyList<MediaFile>? Files=null);
public static class MediaRules
{
    public const int MiB=1024*1024,ImageLimit=10*MiB,VideoLimit=50*MiB,BatchLimit=100*MiB,PropertyLimit=250*MiB,FileCountLimit=20;
    public static string Revision(IEnumerable<PropertyMedia> items)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|',items.OrderBy(x=>x.Id).Select(x=>$"{x.Id}:{x.Version}:{x.Position}:{x.IsCover}")))));
    public static MediaCollection Collection(IReadOnlyList<PropertyMedia> items,long generation=0)=>new(items,Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(generation+":"+Revision(items)))));
    public static bool NameValid(string name)=>!string.IsNullOrWhiteSpace(name)&&name.Length<=200&&!name.Any(char.IsControl)&&name.IndexOfAny(['/', '\\', ':'])<0&&!name.Contains("..",StringComparison.Ordinal);
}
public interface IMediaContentValidator { Outcome<MediaFile> Validate(string name,string mime,byte[] bytes); }
public interface IPropertyMediaStore
{
    Task<MediaCollection?> ListMediaAsync(AccessScope scope,Guid propertyId,CancellationToken token);
    Task<MediaContent?> ReadMediaAsync(AccessScope scope,Guid propertyId,Guid mediaId,CancellationToken token);
    Task<IReadOnlyDictionary<Guid,Guid>> CoversAsync(AccessScope scope,Guid[] properties,CancellationToken token);
    Task<Outcome<MediaCollection>> ChangeMediaAsync(AccessScope scope,Guid propertyId,string revision,MediaChange change,CancellationToken token);
}
