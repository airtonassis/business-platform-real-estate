using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RealEstate.Application;
using RealEstate.Domain;
namespace RealEstate.Web.Pages;
public sealed class CustomerModel(CustomerService service,ICoreAccess access,CommercialInteractionService commercial,CommercialAppointmentService agenda,PropertyMediaService media,IConfiguration configuration,NegotiationService negotiations):PageModel
{
    [BindProperty(SupportsGet=true)]public string Key{get;set;}="new";
    [BindProperty(SupportsGet=true)]public string? Seed{get;set;}
    // Business validation lives in CustomerService; contact POSTs do not submit this form.
    [BindProperty,Microsoft.AspNetCore.Mvc.ModelBinding.Validation.ValidateNever]public CustomerForm Input{get;set;}=new();
    [BindProperty]public long Version{get;set;}
    [BindProperty]public bool AcknowledgeDuplicates{get;set;}
    [BindProperty]public string? ContactKind{get;set;}="Telefone";
    [BindProperty]public string? Description{get;set;}
    [BindProperty(SupportsGet=true)]public bool Search{get;set;}
    [BindProperty(SupportsGet=true)]public string? Feature{get;set;}
    [BindProperty(SupportsGet=true),FromQuery(Name="Filter"),Microsoft.AspNetCore.Mvc.ModelBinding.Validation.ValidateNever]public PropertyListQuery Filter{get;set;}=new();
    [BindProperty]public Guid[]? SelectedProperties{get;set;}
    [BindProperty]public string? PresentationChannel{get;set;}="WhatsApp";
    [BindProperty]public string? PresentationNotes{get;set;}
    [BindProperty]public Guid PresentationId{get;set;}
    [BindProperty]public string? ReturnStatus{get;set;}
    [BindProperty]public string? ReturnCategory{get;set;}
    [BindProperty]public string? ReturnReason{get;set;}
    [BindProperty]public string? ReturnNotes{get;set;}
    [BindProperty]public string? ReturnChannel{get;set;}="Outro";
    [BindProperty]public Guid AppointmentRequestId{get;set;}=Guid.NewGuid();
    [BindProperty]public Guid AppointmentId{get;set;}
    [BindProperty]public long AppointmentVersion{get;set;}
    [BindProperty]public string? AppointmentKind{get;set;}
    [BindProperty]public Guid? AppointmentPropertyId{get;set;}
    [BindProperty]public string? AppointmentStart{get;set;}
    [BindProperty]public string? AppointmentEnd{get;set;}
    [BindProperty]public string? AppointmentLocation{get;set;}
    [BindProperty]public string? AppointmentNotes{get;set;}
    [BindProperty]public string? AppointmentResult{get;set;}
    [BindProperty(SupportsGet=true)]public string? AppointmentPropertySearch{get;set;}
    [BindProperty(SupportsGet=true)]public string? Panel{get;set;}
    [BindProperty(SupportsGet=true)]public Guid? VisitPresentation{get;set;}
    [BindProperty]public Guid ContextualPresentationId{get;set;}
    public CommercialInteraction? SelectedVisit{get;private set;}
    public bool CanSchedule{get;private set;}
    public bool CanVisitProperty{get;private set;}
    public IReadOnlyList<CommercialAppointment> Appointments{get;private set;}=[];
    public IReadOnlyList<AppointmentEvent> AppointmentEvents{get;private set;}=[];
    public PropertyListPage? AppointmentProperties{get;private set;}
    public string AppointmentUnitName{get;private set;}="";
    public IReadOnlyList<CommercialInteraction> Interactions{get;private set;}=[];
    public PropertyListPage? Properties{get;private set;}
    public IReadOnlyDictionary<Guid,Guid> PropertyCovers{get;private set;}=new Dictionary<Guid,Guid>();
    public IEnumerable<WalletCommercialOperation> WalletValues(PropertyV2 property)
    {
        var announcements=Properties?.Announcements?.GetValueOrDefault(property.Id)??[];
        var operations=CommercialValues.Active(property.Data).ToArray();
        foreach(var operation in operations){var announcement=announcements.FirstOrDefault(a=>a.Purpose==operation.Label);yield return new(operation.Label,announcement is {LegacyAnchor:false}?announcement.Values??new():operation.Value,announcement);}
        foreach(var announcement in announcements.Where(a=>!operations.Any(o=>o.Label==a.Purpose)))yield return new(announcement.Purpose,announcement.Values??new(),announcement);
    }
    public bool CanPresent{get;private set;}
    public bool CanReadProperty{get;private set;}
    public bool CanReadCommercial{get;private set;}
    public bool CanStartNegotiation{get;private set;}
    public bool CanViewNegotiations{get;private set;}
    public string? CommercialError{get;private set;}
    public IEnumerable<(DateTimeOffset At,CustomerContact? Contact,CustomerResponsibility? Change)> Timeline=>
        (CustomerFile?.Contacts??[]).Select(c=>(c.At,(CustomerContact?)c,(CustomerResponsibility?)null))
        .Concat((CustomerFile?.ResponsibilityHistory??[]).Select(c=>(c.At,(CustomerContact?)null,(CustomerResponsibility?)c))).OrderByDescending(e=>e.At);
    public string PageHref(int page){var query=Request.Query.ToDictionary(x=>x.Key,x=>(string?)x.Value.ToString(),StringComparer.OrdinalIgnoreCase);query["Search"]="true";query["Filter.Page"]=page.ToString();return Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString(Request.Path,query)+"#property-search";}
    public CustomerFile? CustomerFile{get;private set;}
    public IReadOnlyList<CaptureResponsible> Candidates{get;private set;}=[];
    public bool CanSave{get;private set;}
    public bool CanContact{get;private set;}
    public bool PossibleDuplicate{get;private set;}
    public string? Error{get;private set;}
    public string? DirectoryError{get;private set;}
    [TempData]public string? Notice{get;set;}
    public bool IsNew=>Key=="new";
    public string ActivePanel=>IsNew?"data":Panel switch{"overview"=>"overview","properties"=>"properties","history"=>"history","agenda"=>"agenda","data"=>"data",_=>VisitPresentation.HasValue||AppointmentPropertySearch is not null?"agenda":Search?"properties":"overview"};
    public string PanelHref(string panel)=>"/commercial/customers/"+Uri.EscapeDataString(Key)+"?Panel="+Uri.EscapeDataString(panel);
    public string ResponsibleDisplayName=>CustomerFile?.Customer.Data.ResponsibleName??"Responsável cadastrado";
    private Guid? Id=>Guid.TryParse(Key,out var id)?id:null;
    public async Task OnGetAsync()=>await Load(true);
    private async Task Load(bool fill){
        if(!IsNew&&Id is null){Response.StatusCode=404;Error=CustomerMessages.For("Resource.NotFound");return;}
        AccessScope? customerScope=null;
        if(!IsNew){
            var read=await CustomerOperationalRights.ReadScopeAsync(access,HttpContext.RequestAborted);
            if(!read.IsSuccess){Response.StatusCode=403;Error=CustomerMessages.For(read.Code??"Access.Forbidden");return;}
            customerScope=read.Value;
        }
        if(Id is {} id){var found=await service.FindAsync(id,HttpContext.RequestAborted);if(!found.IsSuccess){Error=CustomerMessages.For(found.Code);Response.StatusCode=CustomerEndpoints.CustomerStatus(found.Code);return;}CustomerFile=found.Value;if(fill){Input=CustomerForm.From(CustomerFile!.Customer.Data);Version=CustomerFile.Customer.Version;}}
        var write=await access.RequireAsync("customers",IsNew?"CUSTOMER_CREATE":"CUSTOMER_UPDATE",HttpContext.RequestAborted);
        CanSave=write.IsSuccess&&(customerScope is null||SameContext(write.Value,customerScope));
        var contact=!IsNew?await access.RequireAsync("customers","CUSTOMER_CONTACT_CREATE",HttpContext.RequestAborted):null;
        CanContact=contact?.IsSuccess==true&&SameContext(contact.Value,customerScope);
        if(CanSave){var candidates=await service.CandidatesAsync(!IsNew,HttpContext.RequestAborted);if(candidates.IsSuccess){Candidates=candidates.Value!.Items;if(IsNew&&fill){if(Candidates.Any(c=>c.Subject==User.FindFirst("sub")?.Value))Input.ResponsibleSubject=User.FindFirst("sub")!.Value;var seed=CustomerSearchSeed.Classify(Seed);if(seed.Document is not null)Input.Document=seed.Document;else if(seed.Name is not null)Input.Name=seed.Name;}}else{DirectoryError=CustomerMessages.For(candidates.Code);CanSave=false;if(IsNew)Response.StatusCode=CustomerEndpoints.CustomerStatus(candidates.Code);}}
        if(IsNew&&!CanSave){if(Response.StatusCode==200)Response.StatusCode=CustomerEndpoints.CustomerStatus(write.Code??"Access.Forbidden");Error??=DirectoryError??CustomerMessages.For(write.Code??"Access.Forbidden");}
        if(Id is {} customer&&CustomerFile is not null){
            var propertyRead=await access.RequireAsync("properties","PROPERTY_READ",HttpContext.RequestAborted);
            CanReadProperty=propertyRead.IsSuccess&&SameContext(propertyRead.Value,customerScope);
            if(CanReadProperty){var eligible=await negotiations.OpenCandidatesAsync(customer,HttpContext.RequestAborted);CanStartNegotiation=eligible.IsSuccess&&eligible.Value!.Count>0;}
            if(CanReadProperty){var negotiation=await access.RequireAsync("negotiations","NEGOTIATION_VIEW",HttpContext.RequestAborted);CanViewNegotiations=negotiation.IsSuccess&&SameContext(negotiation.Value,customerScope);}
            CanStartNegotiation=CanStartNegotiation&&CanViewNegotiations;
            if(ActivePanel=="properties"&&!CanReadProperty){Response.StatusCode=403;CommercialError=CustomerMessages.For("Access.Forbidden");}
            if(configuration.GetValue<bool>("Agenda:Enabled")){
                var unitScope=await access.RequireAsync("customers","CUSTOMER_READ",HttpContext.RequestAborted);
                if(unitScope.IsSuccess)AppointmentUnitName=unitScope.Value!.Establishments?.FirstOrDefault(e=>e.Id==unitScope.Value.ActiveEstablishmentId)?.Name??"";
                var agendaRead=await agenda.ListAsync(customer,DateTimeOffset.UtcNow.AddYears(-10),DateTimeOffset.UtcNow.AddYears(10),HttpContext.RequestAborted);
                if(agendaRead.IsSuccess){Appointments=agendaRead.Value!;var events=await agenda.EventsAsync(customer,HttpContext.RequestAborted);if(events.IsSuccess)AppointmentEvents=events.Value!;CanSchedule=CanContact;}
                if(CanSchedule){var visitRead=await access.RequireAsync("properties","PROPERTY_READ",HttpContext.RequestAborted);CanVisitProperty=visitRead.IsSuccess&&SameContext(visitRead.Value,customerScope);if(CanVisitProperty){var available=await commercial.SearchAsync(customer,new PropertyListQuery{Search=AppointmentPropertySearch},HttpContext.RequestAborted);if(available.IsSuccess)AppointmentProperties=available.Value;}else if(fill)AppointmentKind=AppointmentRules.Office;}
            }
            if(CanReadProperty){var propertyWrite=await access.RequireAsync("customers","CUSTOMER_PROPERTY_WRITE",HttpContext.RequestAborted);CanPresent=propertyWrite.IsSuccess&&SameContext(propertyWrite.Value,customerScope);}
            if(CanReadProperty){var history=await commercial.CustomerHistoryAsync(customer,HttpContext.RequestAborted);CanReadCommercial=history.IsSuccess;
            if(history.IsSuccess){Interactions=history.Value!;
                if(CanSchedule&&CanVisitProperty&&VisitPresentation is {} visit)SelectedVisit=Interactions.FirstOrDefault(i=>i.PresentationId==visit);
                if(Search){if(!string.IsNullOrWhiteSpace(Feature)){var selected=Feature.Split(':');Filter=Filter with{FeatureGroup=selected[0],FeatureOption=selected.Length==2?selected[1]:""};}var properties=await commercial.SearchAsync(customer,Filter,HttpContext.RequestAborted);if(properties.IsSuccess&&ModelState.Where(x=>x.Key.StartsWith("Filter",StringComparison.Ordinal)).All(x=>x.Value?.Errors.Count==0)){Properties=properties.Value;PropertyCovers=await media.CoversAsync(Properties!.Properties.Select(p=>p.Id).ToArray(),HttpContext.RequestAborted);}else CommercialError=properties.IsSuccess?"Revise os filtros informados.":CustomerMessages.For(properties.Code);}
            }else CommercialError=CustomerMessages.For(history.Code);
            }
        }
    }
    private static bool SameContext(AccessScope? left,AccessScope? right)
        =>left is not null&&right is not null&&left.OrganizationId==right.OrganizationId&&left.Subject==right.Subject&&left.ActiveEstablishmentId==right.ActiveEstablishmentId&&left.ActiveEstablishmentId is {} unit&&left.Establishments?.Any(e=>e.Id==unit)==true&&right.Establishments?.Any(e=>e.Id==unit)==true;
    public async Task<IActionResult> OnPostAsync(){
        if(!IsNew&&Id is null)return NotFound();
        if(!ModelState.IsValid){await Load(false);Error="Revise os campos informados.";Response.StatusCode=400;return Page();}
        var saved=await service.SaveAsync(Input.ToInput(),Id,Version,AcknowledgeDuplicates,HttpContext.RequestAborted);
        if(saved.IsSuccess){Notice="Cliente salvo.";return LocalRedirect(PanelHref(Id is null?"overview":"data"));}
        await Load(false);Error=CustomerMessages.For(saved.Code);PossibleDuplicate=saved.Code=="Customer.PossibleDuplicate";Response.StatusCode=CustomerEndpoints.CustomerStatus(saved.Code);return Page();
    }
    public async Task<IActionResult> OnPostContactAsync(){
        if(Id is not {} id)return NotFound();
        if(!ModelState.IsValid){await Load(true);Error="Revise os campos informados.";Response.StatusCode=400;return Page();}
        var saved=await service.AddContactAsync(id,new(ContactKind??"",Description??""),HttpContext.RequestAborted);
        if(saved.IsSuccess){Notice="Atendimento registrado.";return LocalRedirect(PanelHref("history"));}
        await Load(true);Error=CustomerMessages.For(saved.Code);Response.StatusCode=CustomerEndpoints.CustomerStatus(saved.Code);return Page();
    }
    public async Task<IActionResult> OnPostPresentAsync(){
        if(Id is not {} id)return NotFound();
        if(!ModelState.IsValid){await Load(true);Error="Revise a seleção e os campos informados.";Response.StatusCode=400;return Page();}
        var result=await commercial.PresentAsync(id,new(SelectedProperties??[],PresentationChannel??"",PresentationNotes),HttpContext.RequestAborted);
        if(result.IsSuccess){Notice="Imóveis apresentados registrados.";return LocalRedirect(PanelHref("properties")+"#presented-properties");}
        Search=true;await Load(true);Error=CustomerMessages.For(result.Code);Response.StatusCode=CustomerEndpoints.CustomerStatus(result.Code);return Page();
    }
    public async Task<IActionResult> OnPostFeedbackAsync(){
        if(Id is not {} id)return NotFound();
        if(!ModelState.IsValid){await Load(true);Error="Revise os campos do retorno.";Response.StatusCode=400;return Page();}
        var result=await commercial.FeedbackAsync(id,new(PresentationId,ReturnStatus??"",ReturnCategory,ReturnReason,ReturnNotes,ReturnChannel??""),HttpContext.RequestAborted);
        if(result.IsSuccess){Notice="Retorno do imóvel registrado.";return LocalRedirect(PanelHref("properties")+"#presented-properties");}
        await Load(true);Error=CustomerMessages.For(result.Code);Response.StatusCode=CustomerEndpoints.CustomerStatus(result.Code);return Page();
    }
    private static DateTimeOffset? LocalTime(string? value)
    {
        if(!DateTime.TryParseExact(value,"yyyy-MM-ddTHH:mm",System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.None,out var local))return null;
        var zone=TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");local=DateTime.SpecifyKind(local,DateTimeKind.Unspecified);
        return zone.IsInvalidTime(local)||zone.IsAmbiguousTime(local)?null:new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local,zone),TimeSpan.Zero);
    }
    private AppointmentInput? ScheduleInput()
    {
        var start=LocalTime(AppointmentStart);var end=LocalTime(AppointmentEnd);
        return start is null||end is null||AppointmentLocation is null?null:new(AppointmentRequestId,AppointmentKind??"",AppointmentPropertyId,start.Value,end.Value,AppointmentLocation,AppointmentNotes);
    }
    public async Task<IActionResult> OnPostScheduleAsync()
    {
        if(!configuration.GetValue<bool>("Agenda:Enabled"))return NotFound();
        if(Id is not {} customer)return NotFound();var input=ScheduleInput();
        var saved=input is null?Outcome<CommercialAppointment>.Fail("Appointment.InvalidInput"):await agenda.CreateAsync(customer,input,HttpContext.RequestAborted);
        if(saved.IsSuccess){Notice="Compromisso agendado.";return LocalRedirect(PanelHref("agenda")+"#customer-agenda");}
        await Load(false);Error=CustomerMessages.For(saved.Code);Response.StatusCode=400;return Page();
    }
    public async Task<IActionResult> OnPostContextualVisitAsync()
    {
        if(!configuration.GetValue<bool>("Agenda:Enabled"))return NotFound();
        if(Id is not {} customer||ContextualPresentationId==Guid.Empty)return NotFound();
        var history=await commercial.CustomerHistoryAsync(customer,HttpContext.RequestAborted);
        var linked=history.IsSuccess?history.Value!.FirstOrDefault(i=>i.PresentationId==ContextualPresentationId):null;
        if(linked is null)return NotFound();
        var input=ScheduleInput();
        if(input is null)return BadRequest();
        // The server owns the kind and property; posted identifiers cannot select another property.
        input=input with{Kind=AppointmentRules.PropertyVisit,PropertyId=linked.PropertyId};
        var saved=await agenda.CreateAsync(customer,input,HttpContext.RequestAborted);
        if(saved.IsSuccess){Notice="Visita agendada. Consulte o compromisso nesta ficha ou na Agenda comercial.";return LocalRedirect(PanelHref("agenda")+"#customer-agenda");}
        VisitPresentation=ContextualPresentationId;
        await Load(false);Error=CustomerMessages.For(saved.Code);Response.StatusCode=400;return Page();
    }
    public async Task<IActionResult> OnPostAppointmentAsync(string action)
    {
        if(!configuration.GetValue<bool>("Agenda:Enabled"))return NotFound();
        if(Id is not {} customer)return NotFound();var input=action=="Reagendar"?ScheduleInput():null;
        var saved=await agenda.ChangeAsync(new(AppointmentId,customer,null,AppointmentVersion,action,input,AppointmentResult),HttpContext.RequestAborted);
        if(saved.IsSuccess&&saved.Value!.CustomerId==customer){Notice="Compromisso atualizado.";return LocalRedirect(PanelHref("agenda")+"#customer-agenda");}
        await Load(false);Error=CustomerMessages.For(saved.Code??"Resource.NotFound");Response.StatusCode=400;return Page();
    }
}
public sealed record WalletCommercialOperation(string Purpose,OperationValue Value,ListingAnnouncement? Announcement);
public static class CustomerSearchSeed
{
    public static (string? Name,string? Document) Classify(string? value)
    {
        var query=CustomerRules.Clean(value);
        if(query is null || query.Length>200 || query.Any(char.IsControl))return (null,null);
        var digits=CustomerRules.Digits(query);
        if(digits.Length==11 && query.All(c=>char.IsAsciiDigit(c)||c is '.' or '-') &&
           (query.Contains('.') || query.Contains('-') || OwnerProfileRules.Validate(new("Cliente","PF",digits)).IsSuccess))return (null,digits);
        if(query.Any(char.IsLetter) && !query.Any(char.IsAsciiDigit))return (query,null);
        return (null,null);
    }
}
public sealed class CustomerForm
{
    public string Name{get;set;}="";public string PersonType{get;set;}="Física";public string? Phone{get;set;}public string? Email{get;set;}
    public string Origin{get;set;}="Outros";public string Interest{get;set;}="Locação";public string ResponsibleSubject{get;set;}="";public string? Notes{get;set;}public string? Document{get;set;}
    public CustomerInput ToInput()=>new(Name,PersonType,Phone,Email,Origin,Interest,ResponsibleSubject,Notes,Document);
    public static CustomerForm From(CustomerInput p)=>new(){Name=p.Name,PersonType=p.PersonType,Phone=p.Phone,Email=p.Email,Origin=p.Origin,Interest=p.Interest,ResponsibleSubject=p.ResponsibleSubject,Notes=p.Notes,Document=p.Document};
}
