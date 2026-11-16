using Microsoft.AspNetCore.Mvc.RazorPages;
namespace RealEstate.Web.Pages;
public sealed class ErrorModel:PageModel {public void OnGet()=>Response.StatusCode=503;}
