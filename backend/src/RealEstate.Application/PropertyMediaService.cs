using RealEstate.Domain;
namespace RealEstate.Application;
public sealed class PropertyMediaService(ICoreAccess access,IPropertyStoreV2 properties,IPropertyMediaStore store,IMediaContentValidator validator)
{
    public async Task<Outcome<MediaCollection>> ListAsync(Guid id,CancellationToken ct=default){var grant=await access.RequireAsync("properties","PROPERTY_READ",ct);if(!grant.IsSuccess)return Outcome<MediaCollection>.Fail(grant.Code!);var collection=await store.ListMediaAsync(grant.Value!,id,ct);return collection is null?Outcome<MediaCollection>.Fail("Resource.NotFound"):new(collection);}
    public async Task<Outcome<MediaContent>> ReadAsync(Guid id,Guid mediaId,CancellationToken ct=default){var grant=await access.RequireAsync("properties","PROPERTY_READ",ct);if(!grant.IsSuccess)return Outcome<MediaContent>.Fail(grant.Code!);var file=await store.ReadMediaAsync(grant.Value!,id,mediaId,ct);return file is null?Outcome<MediaContent>.Fail("Resource.NotFound"):new(file);}
    public async Task<IReadOnlyDictionary<Guid,Guid>> CoversAsync(Guid[] ids,CancellationToken ct=default){var grant=await access.RequireAsync("properties","PROPERTY_READ",ct);return grant.IsSuccess?await store.CoversAsync(grant.Value!,ids,ct):new Dictionary<Guid,Guid>();}
    public async Task<Outcome<MediaCollection>> ChangeAsync(Guid id,string revision,MediaChange change,CancellationToken ct=default){var grant=await access.RequireAsync("properties","PROPERTY_UPDATE",ct);if(!grant.IsSuccess)return Outcome<MediaCollection>.Fail(grant.Code!);return await store.ChangeMediaAsync(grant.Value!,id,revision,change,ct);}
    public async Task<Outcome<MediaCollection>> UploadAsync(Guid id,string revision,IAsyncEnumerable<MediaUpload> uploads,CancellationToken ct=default)
    {
        var grant=await access.RequireAsync("properties","PROPERTY_UPDATE",ct);
        if(!grant.IsSuccess)return Outcome<MediaCollection>.Fail(grant.Code!);
        if(await properties.FindV2Async(grant.Value!,id,ct) is null)return Outcome<MediaCollection>.Fail("Resource.NotFound");
        var prepared=await PrepareUploadsAsync(uploads,ct);
        return prepared.IsSuccess?await store.ChangeMediaAsync(grant.Value!,id,revision,new("upload",Files:prepared.Value),ct):Outcome<MediaCollection>.Fail(prepared.Code!);
    }
    public async Task<Outcome<IReadOnlyList<MediaFile>>> PrepareUploadsAsync(IAsyncEnumerable<MediaUpload> uploads,CancellationToken ct=default)
    {
        var files=new List<MediaFile>();long total=0;
        try{
            await foreach(var file in uploads.WithCancellation(ct)){
                if(files.Count==MediaRules.FileCountLimit)return Outcome<IReadOnlyList<MediaFile>>.Fail("Media.TooLarge");
                var ext=Path.GetExtension(file.Name).ToLowerInvariant();
                var expected=ext switch{".jpg" or ".jpeg"=>"image/jpeg",".png"=>"image/png",".mp4"=>"video/mp4",_=>null};
                if(!MediaRules.NameValid(file.Name)||expected is null||!string.Equals(file.Mime,expected,StringComparison.OrdinalIgnoreCase))return Outcome<IReadOnlyList<MediaFile>>.Fail("Media.InvalidContent");
                var limit=ext==".mp4"?MediaRules.VideoLimit:MediaRules.ImageLimit;
                using var buffer=new MemoryStream();var chunk=new byte[65536];int count;
                while((count=await file.Content.ReadAsync(chunk,ct))>0){total+=count;if(buffer.Length+count>limit||total>MediaRules.BatchLimit)return Outcome<IReadOnlyList<MediaFile>>.Fail("Media.TooLarge");await buffer.WriteAsync(chunk.AsMemory(0,count),ct);}
                var valid=validator.Validate(file.Name,expected,buffer.ToArray());if(!valid.IsSuccess)return Outcome<IReadOnlyList<MediaFile>>.Fail(valid.Code!);files.Add(valid.Value!);
            }
        }catch(InvalidDataException){return Outcome<IReadOnlyList<MediaFile>>.Fail("Media.InvalidContent");}
        if(files.Count==0)return Outcome<IReadOnlyList<MediaFile>>.Fail("Media.InvalidContent");
        return new(files);
    }
}
