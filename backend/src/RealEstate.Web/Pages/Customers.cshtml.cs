using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RealEstate.Application;
using RealEstate.Domain;
namespace RealEstate.Web.Pages;
public sealed class CustomersModel(CustomerService service,ICoreAccess access):PageModel
{
    [BindProperty(SupportsGet=true)] public string? Query {get;set;}
    [BindProperty(SupportsGet=true)] public int Number {get;set;}=1;
    public CustomerPage? Result {get;private set;}
    public bool CanCreate {get;private set;}
    public string? Error {get;private set;}
    public async Task OnGetAsync(){
        var read=await CustomerOperationalRights.ReadScopeAsync(access,HttpContext.RequestAborted);
        if(!read.IsSuccess){Response.StatusCode=403;Error=CustomerMessages.For(read.Code??"Access.Forbidden");return;}
        var result=await service.SearchAsync(Query,Number,HttpContext.RequestAborted);
        if(!result.IsSuccess){Response.StatusCode=CustomerEndpoints.CustomerStatus(result.Code);Error=CustomerMessages.For(result.Code);return;}
        Result=result.Value;
        var create=await access.RequireAsync("customers","CUSTOMER_CREATE",HttpContext.RequestAborted);
        CanCreate=create.IsSuccess&&create.Value!.OrganizationId==read.Value!.OrganizationId&&create.Value.Subject==read.Value.Subject&&create.Value.ActiveEstablishmentId==read.Value.ActiveEstablishmentId;
    }
}
