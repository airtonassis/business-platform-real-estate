using RealEstate.Application;
using RealEstate.Domain;
using Xunit;
namespace RealEstate.Tests;
public sealed class MediaLimitTests
{
    private sealed class Access(bool allowed):ICoreAccess{public Task<Outcome<AccessScope>> RequireAsync(string r,string p,CancellationToken ct)=>Task.FromResult(allowed?new Outcome<AccessScope>(new(Guid.NewGuid(),"test","Synthetic",[])):Outcome<AccessScope>.Fail("Access.Forbidden"));}
    private sealed class Properties:IPropertyStoreV2{
        public Task<PropertyV2?> FindV2Async(AccessScope s,Guid id,CancellationToken ct)=>Task.FromResult<PropertyV2?>(new(id,s.OrganizationId,new("T","Synthetic",Guid.NewGuid(),"Rural","Fazenda","Venda",1),1,"Draft"));
        public Task<WorkspaceV2> ListV2Async(AccessScope s,CancellationToken ct)=>throw new NotSupportedException();
        public Task<Outcome<PropertyV2>> SaveV2Async(AccessScope s,PropertyInputV2 p,Guid? id,long v,CancellationToken ct)=>throw new NotSupportedException();
    }
    // Deliberately replace only validation/storage boundaries to exercise byte-stream limits.
    // Real decoding and PostgreSQL quotas have independent integration tests.
    private sealed class Validator:IMediaContentValidator{public Outcome<MediaFile> Validate(string n,string m,byte[] b)=>new(new(n,m,m.StartsWith("image/",StringComparison.Ordinal)?"image":"video",b,new string('A',64)));}
    private sealed class Store:IPropertyMediaStore{
        public Task<Outcome<MediaCollection>> ChangeMediaAsync(AccessScope s,Guid id,string revision,MediaChange c,CancellationToken ct)=>Task.FromResult(new Outcome<MediaCollection>(MediaRules.Collection((c.Files??[]).Select((f,i)=>new PropertyMedia(Guid.NewGuid(),f.Kind,f.Mime,f.Name,f.Content.Length,i,false,1)).ToArray())));
        public Task<MediaCollection?> ListMediaAsync(AccessScope s,Guid id,CancellationToken ct)=>throw new NotSupportedException();
        public Task<MediaContent?> ReadMediaAsync(AccessScope s,Guid p,Guid id,CancellationToken ct)=>throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Guid,Guid>> CoversAsync(AccessScope s,Guid[] p,CancellationToken ct)=>throw new NotSupportedException();
    }
    private sealed class GeneratedStream(long length,bool fail=false,bool cancel=false):Stream{
        private long remaining=length;public override bool CanRead=>true;public override bool CanSeek=>false;public override bool CanWrite=>false;public override long Length=>throw new NotSupportedException();public override long Position{get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
        public override ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken ct=default){ct.ThrowIfCancellationRequested();if(fail)throw new InvalidOperationException("Unauthorized stream was consumed");if(cancel)throw new OperationCanceledException();var count=(int)Math.Min(remaining,buffer.Length);buffer.Span[..count].Clear();remaining-=count;return ValueTask.FromResult(count);}
        public override int Read(byte[] b,int o,int c)=>throw new NotSupportedException();public override void Flush()=>throw new NotSupportedException();public override long Seek(long o,SeekOrigin s)=>throw new NotSupportedException();public override void SetLength(long v)=>throw new NotSupportedException();public override void Write(byte[] b,int o,int c)=>throw new NotSupportedException();
    }
    private static async IAsyncEnumerable<MediaUpload> Uploads(int count,long length,string mime="image/png",bool fail=false,bool cancel=false){for(var i=0;i<count;i++){await Task.CompletedTask;yield return new("fixture"+i+(mime=="image/png"?".png":".mp4"),mime,new GeneratedStream(length,fail,cancel));}}
    private static PropertyMediaService Service(bool allowed=true)=>new(new Access(allowed),new Properties(),new Store(),new Validator());
    [Theory]
    [InlineData(1,10485761,"image/png")]
    [InlineData(1,52428801,"video/mp4")]
    [InlineData(11,10485760,"image/png")]
    [InlineData(21,1,"image/png")]
    public async Task Actual_bytes_batch_and_file_count_excess_are_rejected(int count,long size,string mime){var result=await Service().UploadAsync(Guid.NewGuid(),"revision",Uploads(count,size,mime));Assert.Equal("Media.TooLarge",result.Code);}
    [Fact] public async Task Exact_image_limit_and_empty_stream_boundary(){Assert.True((await Service().UploadAsync(Guid.NewGuid(),"revision",Uploads(1,MediaRules.ImageLimit))).IsSuccess);Assert.Equal("Media.InvalidContent",(await new PropertyMediaService(new Access(true),new Properties(),new Store(),new RealEstate.Infrastructure.MediaContentValidator()).UploadAsync(Guid.NewGuid(),"revision",Uploads(1,0))).Code);}
    [Fact] public async Task Authorization_precedes_read_and_cancellation_is_not_reported_as_success(){Assert.Equal("Access.Forbidden",(await Service(false).UploadAsync(Guid.NewGuid(),"revision",Uploads(1,100,fail:true))).Code);await Assert.ThrowsAsync<OperationCanceledException>(()=>Service().UploadAsync(Guid.NewGuid(),"revision",Uploads(1,100,cancel:true)));}
}
