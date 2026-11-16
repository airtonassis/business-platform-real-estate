using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RealEstate.Application;
using RealEstate.Domain;
namespace RealEstate.Web.Pages;
public sealed class AgendaModel(CommercialAppointmentService agenda,ICoreAccess access,ICustomerStore customers,IAgendaReferenceStore references,IConfiguration configuration):PageModel
{
    [BindProperty(SupportsGet=true)]public string View{get;set;}="week";
    [BindProperty(SupportsGet=true)]public DateOnly? Date{get;set;}
    [BindProperty(SupportsGet=true)]public string? OwnerSearch{get;set;}
    [BindProperty(SupportsGet=true)]public string? Responsible{get;set;}
    [BindProperty(SupportsGet=true)]public string? Kind{get;set;}
    [BindProperty(SupportsGet=true)]public string? Status{get;set;}
    [BindProperty]public Guid RequestId{get;set;}=Guid.NewGuid();
    [BindProperty]public Guid? OwnerId{get;set;}
    [BindProperty]public string? ResponsibleSubject{get;set;}
    [BindProperty]public string? StartLocal{get;set;}
    [BindProperty]public string? EndLocal{get;set;}
    [BindProperty]public string? Location{get;set;}
    [BindProperty]public string? Notes{get;set;}
    [BindProperty]public Guid AppointmentId{get;set;}
    [BindProperty]public long AppointmentVersion{get;set;}
    [BindProperty]public Guid? CustomerId{get;set;}
    [BindProperty]public Guid? PropertyId{get;set;}
    [BindProperty]public string? AppointmentKind{get;set;}
    [BindProperty]public string? Result{get;set;}
    public bool CanCreateCapture{get;private set;}
    public bool CanManage{get;private set;}
    public bool CanReadProperty{get;private set;}
    public IReadOnlyList<AgendaOwnerReference> OwnerMatches{get;private set;}=[];
    public IReadOnlyList<CaptureResponsible> Candidates{get;private set;}=[];
    public DateOnly Selected{get;private set;}
    public DateOnly First{get;private set;}
    public DateOnly Last{get;private set;}
    public IReadOnlyList<CommercialAppointment> Items{get;private set;}=[];
    public IReadOnlyList<CommercialAppointment> Upcoming{get;private set;}=[];
    public IReadOnlyList<CommercialAppointment> PeriodItems{get;private set;}=[];
    public IReadOnlyList<CommercialAppointment> FutureItems{get;private set;}=[];
    public IReadOnlyDictionary<Guid,AgendaDisplay> Display{get;private set;}=new Dictionary<Guid,AgendaDisplay>();
    public IReadOnlyDictionary<Guid,IReadOnlyList<AppointmentEvent>> History{get;private set;}=new Dictionary<Guid,IReadOnlyList<AppointmentEvent>>();
    public IReadOnlyList<(string Subject,string Name)> Responsibles{get;private set;}=[];
    public int ScheduledCount=>PeriodItems.Count(x=>x.Status=="Agendado");
    public int CompletedCount=>PeriodItems.Count(x=>x.Status=="Realizado");
    public int CanceledCount=>PeriodItems.Count(x=>x.Status=="Cancelado");
    public string? Error{get;private set;}
    public static DateTimeOffset Local(DateTimeOffset utc)=>TimeZoneInfo.ConvertTime(utc,TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"));
    public string Href(string view,DateOnly date)=>"/commercial/agenda?View="+Uri.EscapeDataString(view)+"&Date="+date.ToString("yyyy-MM-dd")+(string.IsNullOrWhiteSpace(Responsible)?"":"&Responsible="+Uri.EscapeDataString(Responsible))+(string.IsNullOrWhiteSpace(Kind)?"":"&Kind="+Uri.EscapeDataString(Kind))+(string.IsNullOrWhiteSpace(Status)?"":"&Status="+Uri.EscapeDataString(Status));
    public static string KindClass(string kind)=>kind==AppointmentRules.PropertyVisit?"property":kind==AppointmentRules.Capture?"capture":"office";
    public static string StatusLabel(string status)=>status=="Agendado"?"Confirmado":status;
    private static IReadOnlyList<CommercialAppointment> ApplyFilters(IReadOnlyList<CommercialAppointment> source,string? responsible,string? kind,string? status)=>source.Where(x=>(string.IsNullOrEmpty(responsible)||x.ResponsibleSubject==responsible)&&(string.IsNullOrEmpty(kind)||x.Kind==kind)&&(string.IsNullOrEmpty(status)||x.Status==status)).ToArray();
    public async Task<IActionResult> OnGetAsync()
    {
        if(!configuration.GetValue<bool>("Agenda:Enabled"))return NotFound();
        if(View is not("day" or "week" or "month"))return BadRequest();
        CanManage=(await access.RequireAsync("customers","CUSTOMER_CONTACT_CREATE",HttpContext.RequestAborted)).IsSuccess;
        var candidates=await agenda.CandidatesAsync(HttpContext.RequestAborted);
        if(candidates.IsSuccess){Candidates=candidates.Value!.Items;CanCreateCapture=Candidates.Count>0;if(OwnerSearch is not null){var ownerRead=await access.RequireAsync("properties","PROPERTY_READ",HttpContext.RequestAborted);if(ownerRead.IsSuccess)OwnerMatches=await references.SearchAgendaOwnersAsync(ownerRead.Value!,OwnerSearch,HttpContext.RequestAborted);}}
        Selected=Date??DateOnly.FromDateTime(Local(DateTimeOffset.UtcNow).DateTime);
        First=View switch{"day"=>Selected,"week"=>Selected.AddDays(-((int)Selected.DayOfWeek+6)%7),_=>new DateOnly(Selected.Year,Selected.Month,1)};
        Last=View switch{"day"=>First.AddDays(1),"week"=>First.AddDays(7),_=>First.AddMonths(1)};
        var zone=TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
        var from=new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(First.ToDateTime(TimeOnly.MinValue),zone),TimeSpan.Zero);
        var until=new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(Last.ToDateTime(TimeOnly.MinValue),zone),TimeSpan.Zero);
        var found=await agenda.ListAsync(null,from,until,HttpContext.RequestAborted);
        if(!found.IsSuccess){Error=CustomerMessages.For(found.Code);Response.StatusCode=CustomerEndpoints.CustomerStatus(found.Code);return Page();}
        PeriodItems=found.Value!;
        var future=await agenda.ListAsync(null,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow.AddDays(30),HttpContext.RequestAborted);
        if(future.IsSuccess)FutureItems=future.Value!;
        var propertyRead=await access.RequireAsync("properties","PROPERTY_READ",HttpContext.RequestAborted);
        CanReadProperty=propertyRead.IsSuccess;
        if(!propertyRead.IsSuccess){PeriodItems=PeriodItems.Where(x=>x.OwnerId is null&&x.PropertyId is null).ToArray();FutureItems=FutureItems.Where(x=>x.OwnerId is null&&x.PropertyId is null).ToArray();}
        Responsibles=PeriodItems.Concat(FutureItems).GroupBy(x=>x.ResponsibleSubject).Select(g=>(g.Key,g.First().ResponsibleName)).OrderBy(x=>x.ResponsibleName).ToArray();
        Items=ApplyFilters(PeriodItems,Responsible,Kind,Status);
        Upcoming=ApplyFilters(FutureItems,Responsible,Kind,Status).Where(x=>x.Status=="Agendado").OrderBy(x=>x.StartsAt).Take(10).ToArray();
        var customerRead=await access.RequireAsync("customers","CUSTOMER_READ",HttpContext.RequestAborted);
        var labels=new Dictionary<Guid,AgendaDisplay>();
        var history=new Dictionary<Guid,IReadOnlyList<AppointmentEvent>>();
        foreach(var item in Items.Concat(Upcoming).DistinctBy(x=>x.Id))
        {
            string? person=null,property=null;
            if(item.CustomerId is {} customerId&&customerRead.IsSuccess)person=(await customers.FindAppointmentReferenceAsync(customerRead.Value!,customerId,HttpContext.RequestAborted))?.Name;
            if(item.OwnerId is {} ownerId&&propertyRead.IsSuccess)person=(await references.FindAgendaOwnerReferenceAsync(propertyRead.Value!,ownerId,HttpContext.RequestAborted))?.Name;
            if(item.PropertyId is {} propertyId&&propertyRead.IsSuccess){var foundProperty=await references.FindAgendaPropertyReferenceAsync(propertyRead.Value!,propertyId,HttpContext.RequestAborted);if(foundProperty is not null)property=(foundProperty.CommercialCode??foundProperty.Code)+" · "+foundProperty.Title;}
            labels[item.Id]=new(person??"Pessoa indisponível",property);
            var eventResult=await agenda.EventsForAppointmentAsync(item.Id,HttpContext.RequestAborted);
            if(eventResult.IsSuccess)history[item.Id]=eventResult.Value!;
        }
        Display=labels;
        History=history;
        return Page();
    }
    private static DateTimeOffset? ConvertLocal(string? value)
    {
        if(!DateTime.TryParseExact(value,"yyyy-MM-ddTHH:mm",System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.None,out var local))return null;
        var zone=TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");local=DateTime.SpecifyKind(local,DateTimeKind.Unspecified);
        return zone.IsInvalidTime(local)||zone.IsAmbiguousTime(local)?null:new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local,zone),TimeSpan.Zero);
    }
    public async Task<IActionResult> OnPostCaptureAsync()
    {
        if(!configuration.GetValue<bool>("Agenda:Enabled"))return NotFound();
        var start=ConvertLocal(StartLocal);var end=ConvertLocal(EndLocal);
        if(start is null||end is null||OwnerId is null||Location is null){TempData["AgendaError"]="Revise proprietário, horário e endereço.";return RedirectToPage(new{View="week"});}
        var saved=await agenda.CreateAsync(null,new(RequestId,AppointmentRules.Capture,null,start.Value,end.Value,Location,Notes,OwnerId,ResponsibleSubject),HttpContext.RequestAborted);
        TempData[saved.IsSuccess?"AgendaNotice":"AgendaError"]=saved.IsSuccess?"Visita de captação agendada.":CustomerMessages.For(saved.Code);
        return RedirectToPage(new{View="week"});
    }
    public async Task<IActionResult> OnPostAppointmentAsync(string action)
    {
        if(!configuration.GetValue<bool>("Agenda:Enabled"))return NotFound();
        var start=ConvertLocal(StartLocal);var end=ConvertLocal(EndLocal);
        AppointmentInput? input=action=="Reagendar"&&start is not null&&end is not null&&Location is not null?new(RequestId,AppointmentKind??"",PropertyId,start.Value,end.Value,Location,Notes,OwnerId):null;
        var saved=await agenda.ChangeAsync(new(AppointmentId,CustomerId,OwnerId,AppointmentVersion,action,input,Result),HttpContext.RequestAborted);
        TempData[saved.IsSuccess?"AgendaNotice":"AgendaError"]=saved.IsSuccess?"Compromisso atualizado.":CustomerMessages.For(saved.Code);
        var view=View is "day" or "week" or "month"?View:"week";
        return LocalRedirect(Href(view,Date??DateOnly.FromDateTime(Local(DateTimeOffset.UtcNow).DateTime))+"#appointment-"+AppointmentId);
    }
}
public sealed record AgendaDisplay(string Person,string? Property);
