using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RealEstate.Application;
using RealEstate.Domain;
namespace RealEstate.Web.Pages;
public sealed class CommercialPropertyModel(PropertyServiceV2 properties,CommercialInteractionService commercial,AnnouncementService announcements,NegotiationService negotiations,ICustomerDirectory directory,ICoreAccess access):PageModel
{
    private readonly Dictionary<string,string> peopleNames=new(StringComparer.Ordinal);
    public string PersonName(string? subject)=>subject is not null&&peopleNames.TryGetValue(subject,out var name)?name:"Pessoa não identificada";
    public IReadOnlyList<PropertyAnnouncement> NegotiableOffers{get;private set;}=[];
    public HashSet<Guid> EligibleCustomers{get;}=[];
    [BindProperty(SupportsGet=true)]public Guid Id{get;set;}
    [BindProperty(SupportsGet=true)]public DateOnly? From{get;set;}
    [BindProperty(SupportsGet=true)]public DateOnly? Through{get;set;}
    public PropertyV2? Property{get;private set;}
    public IReadOnlyList<CommercialInteraction>? History{get;private set;}
    public CommercialSummary? Summary{get;private set;}
    public string? Error{get;private set;}
    public string? HistoryError{get;private set;}
    public string? SummaryError{get;private set;}
    public async Task OnGetAsync(){
        var grant=await access.RequireAsync("properties","PROPERTY_READ",HttpContext.RequestAborted);
        if(!grant.IsSuccess){Error=CustomerMessages.For(grant.Code);Response.StatusCode=CustomerEndpoints.CustomerStatus(grant.Code);return;}
        var found=await properties.FindAsync(Id,HttpContext.RequestAborted);
        if(!found.IsSuccess||found.Value!.Data.EstablishmentId!=grant.Value!.ActiveEstablishmentId){Error=CustomerMessages.For(found.Code??"Resource.NotFound");Response.StatusCode=CustomerEndpoints.CustomerStatus(found.Code??"Resource.NotFound");return;}
        Property=found.Value;
        var history=await commercial.PropertyHistoryAsync(Id,HttpContext.RequestAborted);if(history.IsSuccess)History=history.Value;else HistoryError=CustomerMessages.For(history.Code);
        if(History is not null&&grant.Value.ActiveEstablishmentId is {} unit){
            var people=await directory.ReadAsync(grant.Value,unit,token:HttpContext.RequestAborted);
            if(people.IsSuccess&&people.Value is {Items:not null} result&&result.OrganizationId==grant.Value.OrganizationId&&result.EstablishmentId==unit)
                foreach(var person in result.Items.Where(p=>p is not null&&!string.IsNullOrWhiteSpace(p.Subject)&&!string.IsNullOrWhiteSpace(p.DisplayName)))peopleNames[person.Subject]=person.DisplayName;
        }
        if(Property.Status==PropertyLifecycle.Available&&History is not null){
            var offers=await announcements.ListAsync(Id,HttpContext.RequestAborted);
            if(offers.IsSuccess)NegotiableOffers=offers.Value!.Where(o=>o.Status=="Draft"&&AnnouncementRules.Exclusive(o.Purpose)&&o.EstablishmentId==grant.Value.ActiveEstablishmentId).ToArray();
            if(NegotiableOffers.Count>0)foreach(var customerId in History.Select(h=>h.CustomerId).Distinct()){
                var candidates=await negotiations.OpenCandidatesAsync(customerId,HttpContext.RequestAborted);
                if(candidates.IsSuccess&&candidates.Value!.Count>0)EligibleCustomers.Add(customerId);
            }
        }
        var today=DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-3)).DateTime);From??=today.AddDays(-30);Through??=today;
        if(!ModelState.IsValid||Through<From||Through==DateOnly.MaxValue){SummaryError="Revise as datas do período.";Response.StatusCode=400;return;}
        var from=new DateTimeOffset(From.Value.ToDateTime(TimeOnly.MinValue),TimeSpan.FromHours(-3));var until=new DateTimeOffset(Through.Value.AddDays(1).ToDateTime(TimeOnly.MinValue),TimeSpan.FromHours(-3));
        var summary=await commercial.SummaryAsync(Id,from,until,HttpContext.RequestAborted);if(summary.IsSuccess)Summary=summary.Value;else SummaryError=summary.Code=="Access.Forbidden"?"O resumo exige permissão comercial e vínculo como captador responsável ou gestor autorizado.":CustomerMessages.For(summary.Code);
    }
}
