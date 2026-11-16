using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RealEstate.Application;
using RealEstate.Domain;
namespace RealEstate.Web.Pages;
public sealed class AnnouncementsModel(AnnouncementService announcements,PropertyServiceV2 properties,ICoreAccess access):PageModel
{
 [BindProperty(SupportsGet=true)]public Guid PropertyId{get;set;}
 [BindProperty]public Guid AnnouncementId{get;set;}
 [BindProperty]public long Version{get;set;}
 [BindProperty]public Guid RequestId{get;set;}=Guid.NewGuid();
 [BindProperty]public Guid? SourceId{get;set;}
 [BindProperty]public long? SourceVersion{get;set;}
 [BindProperty]public string? Purpose{get;set;}
 [BindProperty]public AnnouncementInput Input{get;set;}=new();
 public PropertyV2? Physical{get;private set;}
 public IReadOnlyList<PropertyAnnouncement> Items{get;private set;}=[];
 public string? Error{get;private set;}
 public bool CanCreate{get;private set;}
 public bool CanEdit{get;private set;}
 public IActionResult OnGet()=>RedirectToPage("Property",new{key=PropertyId});
 private async Task<bool> Load(){
  var p=await properties.FindAsync(PropertyId);if(!p.IsSuccess){Error=Messages.For(p.Code);Response.StatusCode=Program.Status(p.Code);return false;}Physical=p.Value;
  var all=await announcements.ListAsync(PropertyId);if(!all.IsSuccess){Error=Messages.For(all.Code);Response.StatusCode=Program.Status(all.Code);return false;}Items=all.Value!;
  var create=await access.RequireAsync("properties","PROPERTY_CREATE",HttpContext.RequestAborted);
  var edit=await access.RequireAsync("properties","PROPERTY_UPDATE",HttpContext.RequestAborted);
  CanCreate=create.IsSuccess&&Physical!.Data.EstablishmentId==create.Value!.ActiveEstablishmentId;
  CanEdit=edit.IsSuccess&&Physical!.Data.EstablishmentId==edit.Value!.ActiveEstablishmentId;return true;
 }
 private async Task<IActionResult> Result(Outcome<PropertyAnnouncement> result){
  if(!result.IsSuccess){Error=AnnouncementMessages.For(result.Code);Response.StatusCode=Program.Status(result.Code);await Load();if(Input.Values is not null)Items=Items.Select(a=>a.Id==AnnouncementId?a with{Data=Input}:a).ToArray();return Page();}
  TempData["Notice"]="Anúncio salvo.";return RedirectToPage(new{propertyId=PropertyId});
 }
 // Older open pages also enter review; opening duplication never creates a record.
 public IActionResult OnPostCreate()=>RedirectToPage("DuplicateAnnouncement",new{propertyId=PropertyId,sourceId=SourceId});
 public async Task<IActionResult> OnPostSaveAsync(){if(!await Load())return Page();if(!Items.Any(a=>a.Id==AnnouncementId))return NotFound();if(!ModelState.IsValid){Error="Revise os valores informados.";Response.StatusCode=400;return Page();}return await Result(await announcements.UpdateAsync(AnnouncementId,Version,Input,ct:HttpContext.RequestAborted));}
 public async Task<IActionResult> OnPostCloseAsync(){if(!await Load())return Page();if(!Items.Any(a=>a.Id==AnnouncementId))return NotFound();return await Result(await announcements.UpdateAsync(AnnouncementId,Version,null,true,HttpContext.RequestAborted));}
}
public static class AnnouncementMessages
{
 public static string For(string? code)=>code switch{
  "Announcement.ActiveExists"=>"Já existe um anúncio ativo desta finalidade para este imóvel.",
  "Announcement.PurposeImmutable"=>"A finalidade é permanente. Duplique o anúncio para outra finalidade.",
  "Announcement.Closed"=>"Este anúncio está encerrado e permanece disponível no histórico.",
  "Announcement.EditOriginal"=>"Edite os valores do anúncio original no cadastro do imóvel.",
  "Announcement.InitialPurposeRequired"=>"Salve a primeira finalidade no cadastro do imóvel.",
  "Announcement.Conflict"=>"A operação já foi utilizada com outros dados. Atualize a consulta antes de tentar novamente.",
  _=>Messages.For(code)};
}
