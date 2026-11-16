using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RealEstate.Application;
using RealEstate.Domain;
namespace RealEstate.Web.Pages;
[Microsoft.AspNetCore.Authorization.Authorize]
public sealed class TeamModel(TeamService service):PageModel
{
    [BindProperty(SupportsGet=true)]public string? Query{get;set;}
    public TeamDirectory? Directory{get;private set;}
    public string? Error{get;private set;}
    public async Task OnGetAsync(){Response.Headers.CacheControl="no-store";var result=await service.SearchAsync(Query,HttpContext.RequestAborted);if(result.IsSuccess)Directory=result.Value;else{Error=TeamEndpoints.Message(result.Code);Response.StatusCode=TeamEndpoints.Status(result.Code);}}
}

