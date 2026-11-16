using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using RealEstate.Application;
using RealEstate.Domain;
namespace RealEstate.Web.Pages;
public sealed partial class PropertyModel
{
    public async Task<IActionResult> OnPostSearchOwnersAsync([FromServices] OwnerProfileService owners,string? query)
    {
        var result=await owners.SearchAsync(query,HttpContext.RequestAborted);
        return result.IsSuccess?new JsonResult(result.Value!.Select(o=>new{o.Id,o.Name,o.PersonKind,Document=o.TaxId.Length>4?"•••"+o.TaxId[^4..]:""})):new JsonResult(new{code=result.Code}){StatusCode=Program.Status(result.Code)};
    }
    public async Task<IActionResult> OnPostCreateOwnerAsync([FromServices] OwnerProfileService owners,string ownerName,string personKind,string taxId,string? ownerDetails)
    {
        OwnerDetails? details;
        try{details=ReadOwnerDetails(ownerDetails);}catch(JsonException){return BadRequest();}
        var result=await owners.CreateAsync(new(ownerName,personKind,taxId,details),HttpContext.RequestAborted);
        return result.IsSuccess?new JsonResult(new{result.Value!.Id,result.Value.Name}):new JsonResult(new{code=result.Code}){StatusCode=Program.Status(result.Code)};
    }
    static OwnerDetails? ReadOwnerDetails(string? json)=>string.IsNullOrWhiteSpace(json)?null:JsonSerializer.Deserialize<OwnerDetails>(json,new JsonSerializerOptions(JsonSerializerDefaults.Web));
}
