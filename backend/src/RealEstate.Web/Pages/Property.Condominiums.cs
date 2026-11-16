using Microsoft.AspNetCore.Mvc;
using RealEstate.Application;
using RealEstate.Domain;
namespace RealEstate.Web.Pages;
public sealed partial class PropertyModel
{
    public async Task<IActionResult> OnPostSearchCondominiumsAsync([FromServices] CondominiumService service,string? query)
    {
        var result=await service.SearchAsync(query,HttpContext.RequestAborted);return result.IsSuccess?new JsonResult(result.Value):new JsonResult(new{code=result.Code}){StatusCode=Program.Status(result.Code)};
    }
    public async Task<IActionResult> OnPostCreateCondominiumAsync([FromServices] CondominiumService service,string condominiumName,string kind,int? towers,int? floors,string? notes)
    {
        var result=await service.CreateAsync(new(condominiumName,kind,towers,floors,notes),HttpContext.RequestAborted);return result.IsSuccess?new JsonResult(result.Value):new JsonResult(new{code=result.Code}){StatusCode=Program.Status(result.Code)};
    }
}
