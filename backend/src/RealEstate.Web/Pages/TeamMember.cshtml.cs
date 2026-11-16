using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RealEstate.Application;
using RealEstate.Domain;
namespace RealEstate.Web.Pages;
[Microsoft.AspNetCore.Authorization.Authorize]
public sealed class TeamMemberModel(TeamService service):PageModel
{
    [BindProperty(SupportsGet=true)]public Guid Id{get;set;}
    [BindProperty,Microsoft.AspNetCore.Mvc.ModelBinding.Validation.ValidateNever]public TeamForm Input{get;set;}=new();
    [BindProperty]public long Version{get;set;}
    public TeamFile? TeamFile{get;private set;}
    public string? Error{get;private set;}
    [TempData]public string? Notice{get;set;}
    public async Task OnGetAsync()=>await Load(true);
    private async Task Load(bool fill){Response.Headers.CacheControl="no-store";var found=await service.FindAsync(Id,HttpContext.RequestAborted);if(!found.IsSuccess){Response.StatusCode=TeamEndpoints.Status(found.Code);Error=TeamEndpoints.Message(found.Code);return;}TeamFile=found.Value;if(fill&&TeamFile!.Professional is {} professional){Input=TeamForm.From(professional.Data);Version=professional.Version;}}
    public async Task<IActionResult> OnPostAsync(){
        Response.Headers.CacheControl="no-store";
        if(!ModelState.IsValid){await Load(false);Response.StatusCode=400;Error="Revise os campos informados.";return Page();}
        var saved=await service.SaveAsync(Id,Input.ToData(),Version,HttpContext.RequestAborted);
        if(saved.IsSuccess){Notice="Dados profissionais salvos.";return LocalRedirect("/administration/team/"+Id);}
        await Load(false);Error=TeamEndpoints.Message(saved.Code);Response.StatusCode=TeamEndpoints.Status(saved.Code);return Page();
    }
}
public sealed class TeamForm
{
    public string JobTitle{get;set;}="";public string? Creci{get;set;}public string? CreciUf{get;set;}public string[] Areas{get;set;}=[];public string? Notes{get;set;}public string Status{get;set;}="Ativo";
    public TeamProfessional ToData()=>new(JobTitle,Creci,CreciUf,Areas,Notes,Status);
    public static TeamForm From(TeamProfessional data)=>new(){JobTitle=data.JobTitle,Creci=data.Creci,CreciUf=data.CreciUf,Areas=data.Areas,Notes=data.Notes,Status=data.Status};
}


