using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Npgsql;
using RealEstate.Application;
using RealEstate.Domain;

namespace RealEstate.Web.Pages;

public sealed class NegotiationsModel(NegotiationService service,NegotiationActionService actionService,CommercialAppointmentService agenda,CustomerService customers,PropertyServiceV2 properties,AnnouncementService announcements,ICustomerDirectory directory,ICoreAccess access,NegotiationActivityTransferService? activityTransfers=null):PageModel
{
    private const string TransferDependencyFallback = "Não foi possível carregar os dados da transferência agora. Tente novamente.";
    private const string TransferCoreDependencyMessage = "Não foi possível confirmar a autorização atual no Core. Tente novamente.";
    public sealed record CommercialLabel(string Customer,string Property,string Offer,string Purpose);
    public Dictionary<Guid,CommercialLabel> Labels { get; }=[];
    public CommercialLabel? DetailLabel { get; private set; }
    public CommercialLabel? OpeningLabel { get; private set; }
    private readonly Dictionary<string,string> responsibleNames=new(StringComparer.Ordinal);
    public string ResponsibleName(string? subject)=>subject is not null&&responsibleNames.TryGetValue(subject,out var name)?name:"Responsável não identificado";
    [BindProperty(SupportsGet=true)] public string? Key { get; set; }
    [BindProperty(SupportsGet=true)] public Guid? CustomerId { get; set; }
    [BindProperty(SupportsGet=true)] public Guid? PropertyId { get; set; }
    [BindProperty(SupportsGet=true)] public Guid? AnnouncementId { get; set; }
    [BindProperty(SupportsGet=true)] public string? Origin { get; set; }
    [BindProperty(SupportsGet=true)] public int PageNumber { get; set; }=1;
    [BindProperty] public Guid RequestId { get; set; }
    [BindProperty] public string? ResponsibleSubject { get; set; }
    [BindProperty] public Guid TransferRequestId { get; set; }=Guid.NewGuid();
    [BindProperty] public long TransferVersion { get; set; }
    [BindProperty] public string? TransferResponsibleSubject { get; set; }
    [BindProperty] public string? TransferReason { get; set; }
    [BindProperty] public string? TransferPreviewToken { get; set; }
    [BindProperty] public List<Guid> SelectedActivityIds { get; set; }=[];
    [BindProperty] public string? FollowupResponsibleSubject { get; set; }
    public NegotiationActivityTransferPreview? ActivityTransferPreview { get; private set; }
    public IReadOnlyList<NegotiationActivityTransferCandidate> TransferActivities { get; private set; }=[];
    public IReadOnlyList<NegotiationActivityTransferFollowup> ActivityFollowups { get; private set; }=[];
    public IReadOnlyList<NegotiationActivityFollowupEvent> ActivityFollowupEvents { get; private set; }=[];
    [BindProperty] public Guid ActionRequestId { get; set; }
    [BindProperty] public string? ActionKind { get; set; }
    [BindProperty] public string? ActionDescription { get; set; }
    [BindProperty] public string? ActionDueLocal { get; set; }
    [BindProperty] public string? ActionResponsibleSubject { get; set; }
    [BindProperty] public string? ActionAppointmentKind { get; set; }
    [BindProperty] public string? ActionAppointmentStart { get; set; }
    [BindProperty] public string? ActionAppointmentEnd { get; set; }
    [BindProperty] public string? ActionAppointmentLocation { get; set; }
    [BindProperty] public string? ActionAppointmentNotes { get; set; }
    [BindProperty] public string? CompletionResult { get; set; }

    public Negotiation? Detail { get; private set; }
    public IReadOnlyList<NegotiationEvent> Events { get; private set; }=[];
    public NegotiationPage? Result { get; private set; }
    public CustomerFile? Customer { get; private set; }
    public PropertyAnnouncement? Offer { get; private set; }
    public string? Error { get; private set; }
    public IReadOnlyList<NegotiationResponsibleOption> ResponsibleOptions { get; private set; }=[];
    public IReadOnlyList<NegotiationResponsibleOption> TransferOptions { get; private set; }=[];
    public bool ManagerialOpening { get; private set; }
    public bool CanTransfer { get; private set; }
    public bool TransferAccessDenied { get; private set; }
    public string? TransferDependencyMessage { get; private set; }
    public bool CanCreateActions { get; private set; }
    public bool ActionAccessDenied { get; private set; }
    public string? ActionLoadError { get; private set; }
    public string? AppointmentUnitName { get; private set; }
    public IReadOnlyList<NegotiationAction> Actions { get; private set; }=[];
    public IReadOnlyList<NegotiationActionEvent> ActionEvents { get; private set; }=[];
    public IReadOnlyList<NegotiationResponsibleOption> ActionResponsibleOptions { get; private set; }=[];
    public Dictionary<Guid,CommercialAppointment> ActionAppointments { get; }=[];
    public bool Opening=>Key is null&&CustomerId.HasValue&&PropertyId.HasValue&&AnnouncementId.HasValue;
    public string AgendaHref(NegotiationAction action)=>ActionAppointments.TryGetValue(action.Id,out var appointment)?"/commercial/agenda?View=day&Date="+AgendaModel.Local(appointment.StartsAt).ToString("yyyy-MM-dd")+"#appointment-"+appointment.Id:"/commercial/agenda";
    public string ReturnUrl=>Origin=="property"&&PropertyId.HasValue?"/commercial/properties/"+PropertyId:"/commercial/customers/"+CustomerId+"?Panel=properties";
    private static string TransferError(string? code)=>code=="Dependency.Unavailable"?TransferDependencyFallback:Message(code);

    public async Task OnGetAsync()
    {
        if(Key is not null)
        {
            if(!Guid.TryParse(Key,out var id)){Response.StatusCode=404;return;}
            var found=await service.FindAsync(id,HttpContext.RequestAborted);
            if(!found.IsSuccess){Response.StatusCode=Program.Status(found.Code);Error=Message(found.Code);return;}
            Detail=found.Value;
            var customerContext=await access.RequireAsync("customers","CUSTOMER_READ",HttpContext.RequestAborted);
            if(customerContext.IsSuccess&&customerContext.Value!.ActiveEstablishmentId==Detail!.EstablishmentId)
                AppointmentUnitName=customerContext.Value.Establishments?.SingleOrDefault(e=>e.Id==Detail.EstablishmentId)?.Name;
            DetailLabel=await LabelAsync(Detail!);
            await LoadResponsibleNamesAsync();
            var events=await service.EventsAsync(id,HttpContext.RequestAborted);if(events.IsSuccess)Events=events.Value!;
            var actions=await actionService.ListAsync(id,HttpContext.RequestAborted);
            if(actions.IsSuccess)
            {
                Actions=actions.Value!;var actionEvents=await actionService.EventsAsync(id,HttpContext.RequestAborted);if(actionEvents.IsSuccess)ActionEvents=actionEvents.Value!;
                foreach(var action in Actions.Where(a=>a.AppointmentId.HasValue)){var appointment=await agenda.FindLinkedAsync(action.AppointmentId!.Value,Detail!.CustomerId,HttpContext.RequestAborted);if(appointment.IsSuccess)ActionAppointments[action.Id]=appointment.Value!;}
                var options=await actionService.ResponsibleOptionsAsync(id,HttpContext.RequestAborted);
                if(options.IsSuccess){var currentSubject=Detail!.ResponsibleSubject;ActionResponsibleOptions=options.Value!;CanCreateActions=ActionResponsibleOptions.Count>0;ActionRequestId=Guid.NewGuid();ActionResponsibleSubject=ActionResponsibleOptions.Any(x=>x.Subject==currentSubject)?currentSubject:ActionResponsibleOptions[0].Subject;}
                else if(options.Code=="Access.Forbidden")ActionAccessDenied=true;else ActionLoadError=Message(options.Code);
            }
            else if(actions.Code=="Access.Forbidden")ActionAccessDenied=true;else ActionLoadError=Message(actions.Code);
            var candidates=await service.TransferCandidatesAsync(id,HttpContext.RequestAborted);
            if(candidates.IsSuccess){TransferOptions=candidates.Value!;CanTransfer=TransferOptions.Count>0;TransferVersion=Detail!.Version;}else if(candidates.Code=="Access.Forbidden")TransferAccessDenied=true;else if(candidates.Code=="Dependency.Unavailable")TransferDependencyMessage=TransferDependencyFallback;
            if(string.IsNullOrWhiteSpace(FollowupResponsibleSubject)){var manager=await access.RequireAsync("negotiations","NEGOTIATION_MANAGE",HttpContext.RequestAborted);if(manager.IsSuccess)FollowupResponsibleSubject=manager.Value!.Subject;}
            if(activityTransfers is not null){try{var eligibleActivities=await activityTransfers.EligibleAsync(id,HttpContext.RequestAborted);if(eligibleActivities.IsSuccess)TransferActivities=eligibleActivities.Value!;else if(eligibleActivities.Code=="Access.Forbidden")TransferAccessDenied=true;else TransferDependencyMessage=eligibleActivities.Code switch{"Dependency.Unavailable"=>TransferCoreDependencyMessage,"Resource.NotFound" or "Negotiation.ResponsibleHistoryUnavailable"=>Message(eligibleActivities.Code),_=>TransferDependencyFallback};var followups=await activityTransfers.FollowupsAsync(id,HttpContext.RequestAborted);if(followups.IsSuccess)ActivityFollowups=followups.Value!;var followupEvents=await activityTransfers.FollowupEventsAsync(id,HttpContext.RequestAborted);if(followupEvents.IsSuccess)ActivityFollowupEvents=followupEvents.Value!;}catch(NpgsqlException){TransferDependencyMessage=TransferDependencyFallback;}}
            return;
        }
        if(Opening)
        {
            var create=await access.RequireAsync("negotiations","NEGOTIATION_CREATE",HttpContext.RequestAborted);
            var candidates=await service.OpenCandidatesAsync(CustomerId!.Value,HttpContext.RequestAborted);
            if(!candidates.IsSuccess){Response.StatusCode=Program.Status(candidates.Code);Error=Message(candidates.Code);return;}
            var customer=await customers.FindAsync(CustomerId.Value,HttpContext.RequestAborted);
            var offers=await announcements.ListAsync(PropertyId!.Value,HttpContext.RequestAborted);
            if(!customer.IsSuccess||!offers.IsSuccess){Response.StatusCode=404;Error="Cliente ou imóvel indisponível nesta filial.";return;}
            Customer=customer.Value;ResponsibleOptions=candidates.Value!;
            ManagerialOpening=!create.IsSuccess||Customer!.Customer.Data.ResponsibleSubject!=create.Value!.Subject;
            Offer=offers.Value!.SingleOrDefault(a=>a.Id==AnnouncementId&&a.Status=="Draft"&&create.IsSuccess&&a.OrganizationId==create.Value!.OrganizationId&&a.EstablishmentId==create.Value!.ActiveEstablishmentId);
            if(Offer is null||Customer!.Customer.EstablishmentId!=create.Value!.ActiveEstablishmentId||ResponsibleOptions.Count==0)
            {Response.StatusCode=404;Error="Oferta ou responsável indisponível nesta filial.";return;}
            ResponsibleSubject=ManagerialOpening?ResponsibleOptions[0].Subject:create.Value!.Subject;
            foreach(var option in ResponsibleOptions)responsibleNames[option.Subject]=option.DisplayName;
            var property=await properties.FindAsync(PropertyId.Value,HttpContext.RequestAborted);
            if(!property.IsSuccess||property.Value!.Data.EstablishmentId!=create.Value.ActiveEstablishmentId||property.Value.Status!=PropertyLifecycle.Available)
            {Response.StatusCode=404;Error="Imóvel indisponível nesta filial.";Offer=null;return;}
            OpeningLabel=new(Customer.Customer.Data.Name,property.Value.CommercialCode??property.Value.Data.Title??"Imóvel sem título",Offer.CommercialCode??"Código pendente",Offer.Purpose);
            RequestId=Guid.NewGuid();return;
        }
        var result=await service.ListAsync(CustomerId,PageNumber,HttpContext.RequestAborted);
        if(result.IsSuccess)
        {
            Result=result.Value;
            await LoadResponsibleNamesAsync();
            foreach(var item in result.Value!.Items){var label=await LabelAsync(item);if(label is not null)Labels[item.Id]=label;}
        }
        else{Response.StatusCode=Program.Status(result.Code);Error=Message(result.Code);}
    }

    private async Task LoadResponsibleNamesAsync()
    {
        var ct=HttpContext.RequestAborted;
        var grant=await access.RequireAsync("negotiations","NEGOTIATION_VIEW",ct);
        if(!grant.IsSuccess||grant.Value!.ActiveEstablishmentId is not {} unit)return;
        var result=await directory.ReadAsync(grant.Value,unit,token:ct);
        if(!result.IsSuccess||result.Value is not {Items:not null} people||people.OrganizationId!=grant.Value.OrganizationId||people.EstablishmentId!=unit)return;
        foreach(var person in people.Items.Where(p=>p is not null&&!string.IsNullOrWhiteSpace(p.Subject)&&!string.IsNullOrWhiteSpace(p.DisplayName)))
            responsibleNames[person.Subject]=person.DisplayName;
    }

    private async Task<CommercialLabel?> LabelAsync(Negotiation item)
    {
        var ct=HttpContext.RequestAborted;
        var customer=await customers.FindAsync(item.CustomerId,ct);
        var property=await properties.FindAsync(item.PropertyId,ct);
        var offers=await announcements.ListAsync(item.PropertyId,ct);
        if(!customer.IsSuccess||!property.IsSuccess||!offers.IsSuccess||
           customer.Value!.Customer.EstablishmentId!=item.EstablishmentId||
           property.Value!.Data.EstablishmentId!=item.EstablishmentId)return null;
        var offer=offers.Value!.SingleOrDefault(x=>x.Id==item.AnnouncementId&&x.EstablishmentId==item.EstablishmentId);
        return offer is null?null:new(customer.Value.Customer.Data.Name,
            property.Value.CommercialCode??property.Value.Data.Title??"Imóvel sem título",offer.CommercialCode??"Código pendente",offer.Purpose);
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if(CustomerId is null||PropertyId is null||AnnouncementId is null)return BadRequest();
        var result=await service.OpenAsync(new(RequestId,CustomerId.Value,PropertyId.Value,AnnouncementId.Value,ResponsibleSubject),HttpContext.RequestAborted);
        if(result.IsSuccess)return Redirect("/commercial/negotiations/"+result.Value!.Id);
        Response.StatusCode=Program.Status(result.Code);Error=Message(result.Code);
        var requestId=RequestId;await OnGetAsync();RequestId=requestId;ResponsibleSubject??=Request.Form[nameof(ResponsibleSubject)].ToString();return Page();
    }

    public async Task<IActionResult> OnPostTransferAsync()
    {
        if(Key is null||!Guid.TryParse(Key,out var id))return NotFound();
        var result=await service.TransferAsync(id,new(TransferRequestId,TransferResponsibleSubject??"",TransferVersion,TransferReason),HttpContext.RequestAborted);
        if(result.IsSuccess)return Redirect("/commercial/negotiations/"+id);
        Response.StatusCode=Program.Status(result.Code);Error=Message(result.Code);
        var requestId=TransferRequestId;await OnGetAsync();TransferRequestId=requestId;return Page();
    }

    public async Task<IActionResult> OnPostPreviewActivityTransferAsync()
    {
        if(Key is null||!Guid.TryParse(Key,out var id)||activityTransfers is null)return NotFound();
        var result=await activityTransfers.PreviewAsync(id,new(TransferRequestId,TransferResponsibleSubject??"",FollowupResponsibleSubject??"",SelectedActivityIds,TransferVersion,TransferReason),HttpContext.RequestAborted);
        if(result.IsSuccess){await OnGetAsync();ActivityTransferPreview=result.Value;return Page();}
        Response.StatusCode=Program.Status(result.Code);Error=TransferError(result.Code);await OnGetAsync();return Page();
    }
    public async Task<IActionResult> OnPostConfirmActivityTransferAsync()
    {
        if(Key is null||!Guid.TryParse(Key,out var id)||activityTransfers is null||string.IsNullOrWhiteSpace(TransferPreviewToken))return BadRequest();
        var result=await activityTransfers.ConfirmAsync(id,TransferPreviewToken,HttpContext.RequestAborted);
        if(result.IsSuccess)return Redirect("/commercial/negotiations/"+id+"#negotiation-transfer");
        Response.StatusCode=Program.Status(result.Code);Error=TransferError(result.Code);await OnGetAsync();return Page();
    }
    public async Task<IActionResult> OnPostResolveTransferFollowupAsync(Guid followupId,long version,Guid requestId)
    {
        if(Key is null||!Guid.TryParse(Key,out var id)||activityTransfers is null)return NotFound();
        var result=await activityTransfers.ResolveAsync(id,followupId,new(requestId,version,Request.Form["FollowupResult"].ToString()),HttpContext.RequestAborted);
        if(result.IsSuccess)return Redirect("/commercial/negotiations/"+id+"#negotiation-transfer-followups");
        Response.StatusCode=Program.Status(result.Code);Error=TransferError(result.Code);await OnGetAsync();return Page();
    }

    public async Task<IActionResult> OnPostCreateActionAsync()
    {
        if(Key is null||!Guid.TryParse(Key,out var id))return NotFound();
        var current=await service.FindAsync(id,HttpContext.RequestAborted);if(!current.IsSuccess)return NotFound();
        var due=LocalTime(ActionDueLocal);var start=LocalTime(ActionAppointmentStart);var end=LocalTime(ActionAppointmentEnd);
        AppointmentInput? appointment=null;
        if(ActionKind==NegotiationActionKinds.Appointment&&start is not null&&end is not null&&ActionAppointmentKind is not null&&ActionAppointmentLocation is not null)
            appointment=new(ActionRequestId,ActionAppointmentKind,ActionAppointmentKind==AppointmentRules.PropertyVisit?current.Value!.PropertyId:null,start.Value,end.Value,ActionAppointmentLocation,ActionAppointmentNotes,ResponsibleSubject:ActionResponsibleSubject);
        var input=new NegotiationActionInput(ActionRequestId,ActionKind??"",ActionDescription??"",due,ActionResponsibleSubject,appointment,current.Value!.Version);
        var result=await actionService.CreateAsync(id,input,HttpContext.RequestAborted);
        if(result.IsSuccess)return Redirect("/commercial/negotiations/"+id+"#negotiation-actions");
        Response.StatusCode=Program.Status(result.Code);Error=Message(result.Code);
        var fields=(ActionRequestId,ActionKind,ActionDescription,ActionDueLocal,ActionResponsibleSubject,ActionAppointmentKind,ActionAppointmentStart,ActionAppointmentEnd,ActionAppointmentLocation,ActionAppointmentNotes);
        await OnGetAsync();(ActionRequestId,ActionKind,ActionDescription,ActionDueLocal,ActionResponsibleSubject,ActionAppointmentKind,ActionAppointmentStart,ActionAppointmentEnd,ActionAppointmentLocation,ActionAppointmentNotes)=fields;return Page();
    }

    public async Task<IActionResult> OnPostCompleteActionAsync(Guid actionId,long version,Guid requestId)
    {
        if(Key is null||!Guid.TryParse(Key,out var id))return NotFound();
        var result=await actionService.CompleteAsync(id,actionId,new(requestId,version,CompletionResult??""),HttpContext.RequestAborted);
        if(result.IsSuccess)return Redirect("/commercial/negotiations/"+id+"#negotiation-actions");
        Response.StatusCode=Program.Status(result.Code);Error=Message(result.Code);var completion=CompletionResult;await OnGetAsync();CompletionResult=completion;return Page();
    }

    private static DateTimeOffset? LocalTime(string? value)
    {
        if(!DateTime.TryParseExact(value,"yyyy-MM-ddTHH:mm",System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.None,out var local))return null;
        local=DateTime.SpecifyKind(local,DateTimeKind.Unspecified);var zone=TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
        return zone.IsInvalidTime(local)||zone.IsAmbiguousTime(local)?null:new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local,zone),TimeSpan.Zero);
    }

    private static string Message(string? code)=>code switch
    {
        "Negotiation.OfferUnavailable"=>"A oferta não está elegível nesta filial. Confira o imóvel e tente novamente.",
        "Negotiation.RequestConflict"=>"Este pedido já foi usado para outra negociação.",
        "Negotiation.VersionConflict"=>"A negociação foi alterada por outra pessoa. Atualize a jornada e tente novamente.",
        "Negotiation.ResponsibleIneligible"=>"O responsável não está mais habilitado para esta filial.",
        "Negotiation.ResponsibleHistoryUnavailable"=>"O registro não contém o vínculo Core necessário para autorizar a transferência.",
        "Negotiation.SameResponsible"=>"Selecione um responsável diferente do atual.",
        "NegotiationAction.DueRequired"=>"Informe o prazo da ação.",
        "NegotiationAction.DescriptionRequired"=>"Informe uma descrição para a ação.",
        "NegotiationAction.OtherDescriptionRequired"=>"Descreva a outra ação com contexto suficiente para o acompanhamento.",
        "NegotiationAction.InvalidType"=>"Selecione um dos tipos de ação disponíveis.",
        "NegotiationAction.InvalidInput" or "NegotiationAction.AppointmentFieldsRequired"=>"Revise os dados do compromisso na Agenda.",
        "NegotiationAction.ResultRequired"=>"Informe um resultado breve para concluir a ação.",
        "NegotiationAction.VersionRequired"=>"Atualize a jornada antes de criar a ação.",
        "NegotiationAction.VersionConflict"=>"A ação mudou. Atualize a jornada e tente novamente.",
        "NegotiationAction.AlreadyCompleted"=>"Esta ação já foi concluída.",
        "NegotiationAction.RequestConflict"=>"Este pedido já foi utilizado com outros dados. Atualize a jornada e tente novamente.",
        "NegotiationAction.AppointmentAlreadyLinked"=>"Este compromisso da Agenda já está vinculado a outra ação.",
        "NegotiationAction.IntegrityError"=>"Não foi possível confirmar o vínculo da ação com a Agenda. Nenhuma conclusão foi gravada.",
        "Appointment.VersionConflict"=>"O compromisso da Agenda foi alterado. Atualize a jornada antes de concluir.",
        "Appointment.InvalidTransition"=>"Este compromisso ainda não pode ser realizado.",
        "Appointment.InvalidInput" or "Appointment.PropertyUnavailable" or "Appointment.UnitInvalid"=>"Revise o tipo, os horários e o local do compromisso.",
        "Appointment.ResponsibleUnavailable" or "Negotiation.ResponsibleIneligible" or "NegotiationAction.ResponsibleUnavailable"=>"O responsável não está elegível nesta filial.",
        "NegotiationTransfer.PreviewExpired"=>"A prévia expirou ou os dados/permissões mudaram. Gere uma nova prévia.",
        "NegotiationTransfer.ResultRequired"=>"Informe um resultado breve para o follow-up.",
        "NegotiationTransfer.VersionConflict"=>"O follow-up foi alterado. Atualize a jornada antes de resolver.",
        "NegotiationTransfer.FollowupAlreadyResolved"=>"Este follow-up já foi resolvido.",
        "NegotiationTransfer.FollowupAlreadyPending"=>"Esta atividade já possui um follow-up pendente. Atualize a jornada para acompanhá-lo.",
        "NegotiationTransfer.InvalidInput"=>"A solicitação está incompleta ou inválida. Gere uma nova prévia.",
        "NegotiationTransfer.RequestConflict"=>"Este identificador de solicitação já foi usado com outros dados.",
        "Access.Forbidden"=>"Você não possui autoridade ou direito para esta operação.",
        "Dependency.Unavailable"=>"Não foi possível confirmar a autorização atual no Core. Tente novamente.",
        "Resource.NotFound"=>"Negociação não encontrada nesta filial.",
        _=>"Não foi possível concluir a operação."
    };
}
