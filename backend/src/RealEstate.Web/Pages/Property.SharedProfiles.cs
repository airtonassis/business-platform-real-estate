using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using RealEstate.Application;
using RealEstate.Domain;
namespace RealEstate.Web.Pages;
public sealed partial class PropertyModel
{
    static IActionResult SharedResult<T>(Outcome<T> result)=>new JsonResult(result.IsSuccess?(object?)result.Value:new{code=result.Code}){StatusCode=result.IsSuccess?200:Program.Status(result.Code)};
    public async Task<IActionResult> OnPostReadOwnerProfileAsync([FromServices] OwnerProfileService service,Guid profileId)=>SharedResult(await service.FindForEditAsync(profileId,HttpContext.RequestAborted));
    public async Task<IActionResult> OnPostUpdateOwnerProfileAsync([FromServices] OwnerProfileService service,Guid profileId,long profileVersion,string ownerName,string? personKind,string? taxId,string? ownerDetails)
    {
        try{return SharedResult(await service.UpdateAsync(profileId,profileVersion,new(ownerName,personKind??"",taxId??"",ReadOwnerDetails(ownerDetails)),HttpContext.RequestAborted));}
        catch(JsonException){return BadRequest();}
    }
    public async Task<IActionResult> OnPostReadCondominiumProfileAsync([FromServices] CondominiumService service,Guid profileId)=>SharedResult(await service.FindAsync(profileId,true,HttpContext.RequestAborted));
    public async Task<IActionResult> OnPostUpdateCondominiumProfileAsync([FromServices] CondominiumService service,Guid profileId,long profileVersion,string? profileData)
    {
        if(!ModelState.IsValid)return BadRequest();
        try{var input=JsonSerializer.Deserialize<CondominiumInput>(profileData??"",new JsonSerializerOptions(JsonSerializerDefaults.Web));return input is null?BadRequest():SharedResult(await service.UpdateAsync(profileId,profileVersion,input,HttpContext.RequestAborted));}
        catch(JsonException){return BadRequest();}
    }
}
