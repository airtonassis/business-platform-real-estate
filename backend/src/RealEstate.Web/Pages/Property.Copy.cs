using Microsoft.AspNetCore.Mvc;
using RealEstate.Domain;
using System.Text.Json;
namespace RealEstate.Web.Pages;
public sealed partial class PropertyModel
{
 [BindProperty(SupportsGet=true)]public Guid? CopyFrom{get;set;}
 [BindProperty(SupportsGet=true)]public Guid? CopyAnnouncementId{get;set;}
 [BindProperty]public long CopyVersion{get;set;}
 [BindProperty]public string? CopyMediaRevision{get;set;}
 [BindProperty]public Guid[] CopyExcluded{get;set;}=[];
 [BindProperty]public Guid? CopyCover{get;set;}
 [BindProperty]public Guid[] CopyOrder{get;set;}=[];
 public bool IsPropertyCopy=>IsNew&&CopyFrom.HasValue;
 private async Task<bool> LoadPropertyCopy(bool prefill){
  var source=await service.FindAsync(CopyFrom!.Value,HttpContext.RequestAborted);
  if(!source.IsSuccess){Deny(source.Code);return false;}
  if(!CanCreate){Deny("Access.Forbidden");return false;}
  var gallery=await media.ListAsync(CopyFrom.Value,HttpContext.RequestAborted);
  if(!gallery.IsSuccess){Deny(gallery.Code);return false;}
  Gallery=gallery.Value;
  if(prefill){
   Input=JsonSerializer.Deserialize<PropertyInputV2>(JsonSerializer.Serialize(source.Value!.Data))!;
   Input=Input with{Code=null,EstablishmentId=ActiveEstablishmentId};
   if(CopyAnnouncementId is {} announcementId){
    if(announcements is null){Deny("Resource.NotFound");return false;}
    var listed=await announcements.ListAsync(CopyFrom.Value,HttpContext.RequestAborted);
    if(!listed.IsSuccess){Deny(listed.Code);return false;}
    var selected=listed.Value!.SingleOrDefault(a=>a.Id==announcementId);
    if(selected is null){Deny("Resource.NotFound");return false;}
    Input=Input with{Purpose=selected.Purpose};ApplyAnnouncementData(selected.Data,true);
   }
   if(Input.Capture is {} capture){capture.PriceHistory=[];capture.Policy=null;}
   Version=0;CommercialCode=null;CopyVersion=source.Value.Version;CopyMediaRevision=Gallery!.Revision;
   CopyOrder=Gallery.Items.Select(m=>m.Id).ToArray();
  }
  IncludeCategory(source.Value!.Data.Category);
  if(Input.Capture?.Condominium.Id is {} condo){var c=await condominiums.FindAsync(condo,token:HttpContext.RequestAborted);LinkedCondominium=c.Value;CondominiumName=c.Value?.Data.Name;}
  PrepareCapture();Step=ApplicableStep();return true;
 }
 private async Task<Outcome<PropertyCopySource>> CopyCommand(){
  IReadOnlyList<MediaFile>? files=null;
  if(Request.Form.Files.Count>0){
   async IAsyncEnumerable<MediaUpload> Uploads(){foreach(var file in Request.Form.Files){await using var stream=file.OpenReadStream();yield return new(file.FileName,file.ContentType,stream);}}
   var prepared=await media.PrepareUploadsAsync(Uploads(),HttpContext.RequestAborted);
   if(!prepared.IsSuccess)return Outcome<PropertyCopySource>.Fail(prepared.Code!);files=prepared.Value;
  }
  return new(new(CopyFrom!.Value,CopyVersion,CopyMediaRevision??"",CopyExcluded,CopyCover,CopyOrder,files));
 }
}
