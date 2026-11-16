using Microsoft.AspNetCore.Mvc;
using RealEstate.Domain;
namespace RealEstate.Web.Pages;
public sealed partial class PropertyModel
{
    [TempData]public string? MediaNotice {get;set;}
    public MediaCollection? Gallery {get;private set;}
    public async Task<IActionResult> OnGetContentAsync(Guid mediaId){
        if(!Guid.TryParse(Key,out var id))return NotFound();var result=await media.ReadAsync(id,mediaId,HttpContext.RequestAborted);
        if(!result.IsSuccess)return MediaResponse(result);
        Response.Headers.CacheControl="no-store";Response.Headers.ContentDisposition="inline";
        var response=File(result.Value!.Content,result.Value.Mime);response.EnableRangeProcessing=true;return response;
    }
    public async Task<IActionResult> OnPostUploadAsync(){
        if(!Guid.TryParse(Key,out var id))return NotFound();
        try{
            var result=await media.UploadAsync(id,Request.Headers["X-Media-Revision"].ToString(),RealEstate.Web.MediaStreamingAttribute.Files(Request,HttpContext.RequestAborted),HttpContext.RequestAborted);
            if(result.IsSuccess)MediaNotice="Mídias salvas. Os arquivos confirmados aparecem na galeria deste imóvel.";
            return MediaResponse(result);
        }
        catch(BadHttpRequestException e) when(e.StatusCode==413){return MediaResponse(Outcome<MediaCollection>.Fail("Media.TooLarge"));}
        catch(IOException){return MediaResponse(Outcome<MediaCollection>.Fail("Media.InvalidContent"));}
    }
    public Task<IActionResult> OnPostCoverAsync(Guid mediaId,string? mediaRevision)=>ChangeMedia(new("cover",mediaId),mediaRevision);
    public Task<IActionResult> OnPostRemoveAsync(Guid mediaId,string? mediaRevision)=>ChangeMedia(new("remove",mediaId),mediaRevision);
    public Task<IActionResult> OnPostReorderAsync(Guid[] order,string? mediaRevision)=>ChangeMedia(new("reorder",Order:order),mediaRevision);
    private async Task<IActionResult> ChangeMedia(MediaChange change,string? revision){if(!Guid.TryParse(Key,out var id))return NotFound();return MediaResponse(await media.ChangeAsync(id,revision??"",change,HttpContext.RequestAborted));}
    private static IActionResult MediaResponse<T>(Outcome<T> result)=>new JsonResult(result.IsSuccess?new{ok=true,code=(string?)null}:new{ok=false,code=result.Code}){StatusCode=result.IsSuccess?200:result.Code switch{"Media.VersionConflict"=>409,"Media.TooLarge"=>413,_=>Program.Status(result.Code)}};
}
