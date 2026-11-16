using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;
using RealEstate.Domain;
namespace RealEstate.Web;
public sealed class MediaStreamingAttribute:Attribute,IResourceFilter
{
    public void OnResourceExecuting(ResourceExecutingContext context){if(context.HttpContext.Request.Query["handler"]!="Upload")return;context.ValueProviderFactories.RemoveType<FormValueProviderFactory>();context.ValueProviderFactories.RemoveType<FormFileValueProviderFactory>();context.ValueProviderFactories.RemoveType<JQueryFormValueProviderFactory>();}
    public void OnResourceExecuted(ResourceExecutedContext context){}
    public static async IAsyncEnumerable<MediaUpload> Files(HttpRequest request,[System.Runtime.CompilerServices.EnumeratorCancellation]CancellationToken ct){
        if(!MediaTypeHeaderValue.TryParse(request.ContentType,out var content)||content.MediaType!="multipart/form-data")throw new InvalidDataException();
        var boundary=HeaderUtilities.RemoveQuotes(content.Boundary).Value;if(string.IsNullOrWhiteSpace(boundary)||boundary.Length>200)throw new InvalidDataException();
        var reader=new MultipartReader(boundary,request.Body){HeadersCountLimit=16,HeadersLengthLimit=16384,BodyLengthLimit=MediaRules.VideoLimit+1};MultipartSection? section;
        while((section=await reader.ReadNextSectionAsync(ct)) is not null){
            if(!ContentDispositionHeaderValue.TryParse(section.ContentDisposition,out var disposition)||disposition.DispositionType!="form-data"||!disposition.FileName.HasValue&&!disposition.FileNameStar.HasValue)throw new InvalidDataException();
            var name=HeaderUtilities.RemoveQuotes(disposition.FileNameStar.HasValue?disposition.FileNameStar:disposition.FileName).Value??"";
            yield return new(name,section.ContentType??"",section.Body);
        }
    }
}
