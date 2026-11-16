using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace RealEstate.Web.Pages;
// Compatibility URL: duplication is rendered exclusively by Property.
public sealed class DuplicateAnnouncementModel:PageModel
{
 [BindProperty(SupportsGet=true)]public Guid PropertyId{get;set;}
 [BindProperty(SupportsGet=true)]public Guid? SourceId{get;set;}
 public IActionResult OnGet()=>RedirectToPage("Property",new{key="new",copyFrom=PropertyId});
 public IActionResult OnPost()=>OnGet();
}
