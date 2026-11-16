using RealEstate.Domain;
namespace RealEstate.Application;

public sealed class CommercialAppointmentService(ICoreAccess access,ICustomerStore customers,ICustomerDirectory directory,IAgendaReferenceStore references,ICommercialAppointmentStore appointments)
{
    public async Task<Outcome<AppointmentInput>> ValidateNegotiationAppointmentAsync(Negotiation negotiation,AppointmentInput input,string responsibleSubject,CancellationToken token=default)
    {
        var scope=await Scope("CUSTOMER_CONTACT_CREATE",token);if(!scope.IsSuccess)return Outcome<AppointmentInput>.Fail(scope.Code!);
        if(negotiation.EstablishmentId!=scope.Value!.ActiveEstablishmentId||negotiation.CustomerId==Guid.Empty||negotiation.PropertyId==Guid.Empty||string.IsNullOrWhiteSpace(responsibleSubject))return Outcome<AppointmentInput>.Fail("Resource.NotFound");
        var valid=AppointmentRules.Validate(input,DateTimeOffset.UtcNow);if(!valid.IsSuccess)return Outcome<AppointmentInput>.Fail(valid.Code!);
        var value=valid.Value!;
        if(value.OwnerId is not null||value.PropertyId is {} property&&property!=negotiation.PropertyId||value.Kind==AppointmentRules.PropertyVisit&&value.PropertyId!=negotiation.PropertyId)return Outcome<AppointmentInput>.Fail("Appointment.PropertyUnavailable");
        var customer=await customers.FindAppointmentReferenceAsync(scope.Value,negotiation.CustomerId,token);if(customer is null||customer.EstablishmentId!=scope.Value.ActiveEstablishmentId)return Outcome<AppointmentInput>.Fail("Resource.NotFound");
        if(value.PropertyId is {} propertyId){var propertyRead=await access.RequireAsync("properties","PROPERTY_READ",token);if(!propertyRead.IsSuccess||propertyRead.Value!.OrganizationId!=scope.Value.OrganizationId||propertyRead.Value.ActiveEstablishmentId!=scope.Value.ActiveEstablishmentId||await references.FindAgendaPropertyReferenceAsync(scope.Value,propertyId,token) is null)return Outcome<AppointmentInput>.Fail("Appointment.PropertyUnavailable");}
        var people=await directory.ReadAsync(scope.Value,scope.Value.ActiveEstablishmentId!.Value,responsibleSubject,token);if(!people.IsSuccess||people.Value is not {Items:not null} directoryResult||directoryResult.OrganizationId!=scope.Value.OrganizationId||directoryResult.EstablishmentId!=scope.Value.ActiveEstablishmentId||!directoryResult.Items.Any(x=>x.Subject==responsibleSubject))return Outcome<AppointmentInput>.Fail("Appointment.ResponsibleUnavailable");
        if(value.Kind==AppointmentRules.Office&&value.Location!=scope.Value.Establishments!.Single(e=>e.Id==scope.Value.ActiveEstablishmentId).Name)return Outcome<AppointmentInput>.Fail("Appointment.UnitInvalid");
        return new(value with{ResponsibleSubject=responsibleSubject});
    }

    public async Task<Outcome<CommercialAppointment>> ValidateLinkedRealizationAsync(Guid appointmentId,Guid customerId,string result,CancellationToken token=default)
    {
        var scope=await Scope("CUSTOMER_CONTACT_CREATE",token);if(!scope.IsSuccess)return Outcome<CommercialAppointment>.Fail(scope.Code!);
        var current=await appointments.FindAppointmentAsync(scope.Value!,appointmentId,token);if(current is null||current.CustomerId!=customerId||current.OwnerId is not null)return Outcome<CommercialAppointment>.Fail("Resource.NotFound");
        if(current.PropertyId is not null){var propertyRead=await access.RequireAsync("properties","PROPERTY_READ",token);if(!propertyRead.IsSuccess||propertyRead.Value!.OrganizationId!=scope.Value!.OrganizationId||propertyRead.Value.ActiveEstablishmentId!=scope.Value.ActiveEstablishmentId)return Outcome<CommercialAppointment>.Fail("Access.Forbidden");}
        if(CustomerRules.Clean(result) is not {} clean||clean.Length>4000)return Outcome<CommercialAppointment>.Fail("Appointment.ResultRequired");
        if(current.Status=="Realizado"&&current.Result==clean)return new(current);
        if(!AppointmentRules.CanTransition(current.Status,"Realizar")||current.StartsAt>DateTimeOffset.UtcNow)return Outcome<CommercialAppointment>.Fail("Appointment.InvalidTransition");
        return new(current);
    }

    public async Task<Outcome<CommercialAppointment>> FindLinkedAsync(Guid appointmentId,Guid customerId,CancellationToken token=default)
    {
        var scope=await Scope("CUSTOMER_READ",token);if(!scope.IsSuccess)return Outcome<CommercialAppointment>.Fail(scope.Code!);
        var item=await appointments.FindAppointmentAsync(scope.Value!,appointmentId,token);if(item is null||item.CustomerId!=customerId||item.OwnerId is not null)return Outcome<CommercialAppointment>.Fail("Resource.NotFound");
        if(item.PropertyId is not null){var propertyRead=await access.RequireAsync("properties","PROPERTY_READ",token);if(!propertyRead.IsSuccess||propertyRead.Value!.OrganizationId!=scope.Value!.OrganizationId||propertyRead.Value.ActiveEstablishmentId!=scope.Value.ActiveEstablishmentId)return Outcome<CommercialAppointment>.Fail("Access.Forbidden");}
        return new(item);
    }

    private async Task<Outcome<AccessScope>> Scope(string permission,CancellationToken token)
    {
        var result=await access.RequireAsync("customers",permission,token);
        if(!result.IsSuccess)return result;
        var s=result.Value!;
        if(s.OrganizationId==Guid.Empty||s.ActiveEstablishmentId is not {} unit||s.Establishments?.Any(e=>e.Id==unit)!=true)return Outcome<AccessScope>.Fail("Context.ActiveEstablishmentRequired");
        if(permission!="CUSTOMER_READ"){
            var read=await access.RequireAsync("customers","CUSTOMER_READ",token);
            if(!read.IsSuccess)return Outcome<AccessScope>.Fail(read.Code!);
            if(read.Value!.OrganizationId!=s.OrganizationId||read.Value.ActiveEstablishmentId!=s.ActiveEstablishmentId)return Outcome<AccessScope>.Fail("Context.Invalid");
        }
        return result;
    }
    public async Task<Outcome<IReadOnlyList<CommercialAppointment>>> ListAsync(Guid? customerId,DateTimeOffset from,DateTimeOffset until,CancellationToken token=default)
    {
        var scope=await Scope("CUSTOMER_READ",token);if(!scope.IsSuccess)return Outcome<IReadOnlyList<CommercialAppointment>>.Fail(scope.Code!);
        if(until<=from||until-from>TimeSpan.FromDays(36525))return Outcome<IReadOnlyList<CommercialAppointment>>.Fail("Appointment.InvalidRange");
        if(customerId is {} customer&&await customers.FindAppointmentReferenceAsync(scope.Value!,customer,token) is null)return Outcome<IReadOnlyList<CommercialAppointment>>.Fail("Resource.NotFound");
        return new(await appointments.ListAsync(scope.Value!,customerId,from,until,token));
    }
    public async Task<Outcome<IReadOnlyList<AppointmentEvent>>> EventsAsync(Guid customerId,CancellationToken token=default)
    {
        var scope=await Scope("CUSTOMER_READ",token);if(!scope.IsSuccess)return Outcome<IReadOnlyList<AppointmentEvent>>.Fail(scope.Code!);
        if(await customers.FindAppointmentReferenceAsync(scope.Value!,customerId,token) is null)return Outcome<IReadOnlyList<AppointmentEvent>>.Fail("Resource.NotFound");
        return new(await appointments.EventsAsync(scope.Value!,customerId,token));
    }
    public async Task<Outcome<IReadOnlyList<AppointmentEvent>>> EventsForAppointmentAsync(Guid appointmentId,CancellationToken token=default)
    {
        var scope=await Scope("CUSTOMER_READ",token);if(!scope.IsSuccess)return Outcome<IReadOnlyList<AppointmentEvent>>.Fail(scope.Code!);
        var item=await appointments.FindAppointmentAsync(scope.Value!,appointmentId,token);if(item is null)return Outcome<IReadOnlyList<AppointmentEvent>>.Fail("Resource.NotFound");
        if(item.OwnerId is not null||item.PropertyId is not null){var propertyRead=await access.RequireAsync("properties","PROPERTY_READ",token);if(!propertyRead.IsSuccess||propertyRead.Value!.OrganizationId!=scope.Value!.OrganizationId||propertyRead.Value.ActiveEstablishmentId!=scope.Value.ActiveEstablishmentId)return Outcome<IReadOnlyList<AppointmentEvent>>.Fail("Access.Forbidden");}
        return new(await appointments.EventsForAppointmentAsync(scope.Value!,appointmentId,token));
    }
    public async Task<Outcome<CaptureResponsibleDirectory>> CandidatesAsync(CancellationToken token=default)
    {
        var scope=await Scope("CUSTOMER_CONTACT_CREATE",token);if(!scope.IsSuccess)return Outcome<CaptureResponsibleDirectory>.Fail(scope.Code!);
        var read=await access.RequireAsync("properties","PROPERTY_READ",token);if(!read.IsSuccess||read.Value!.OrganizationId!=scope.Value!.OrganizationId||read.Value.ActiveEstablishmentId!=scope.Value.ActiveEstablishmentId)return Outcome<CaptureResponsibleDirectory>.Fail("Access.Forbidden");
        return await directory.ReadAsync(scope.Value,scope.Value.ActiveEstablishmentId!.Value,token:token);
    }
    public async Task<Outcome<CommercialAppointment>> CreateAsync(Guid? customerId,AppointmentInput input,CancellationToken token=default)
    {
        var scope=await Scope("CUSTOMER_CONTACT_CREATE",token);if(!scope.IsSuccess)return Outcome<CommercialAppointment>.Fail(scope.Code!);
        var valid=AppointmentRules.Validate(input,DateTimeOffset.UtcNow);if(!valid.IsSuccess||customerId is null&&input.OwnerId is null||customerId is not null&&input.OwnerId is not null||customerId is null&&input.Kind!=AppointmentRules.Capture)return Outcome<CommercialAppointment>.Fail("Appointment.InvalidInput");
        var customer=customerId is {} id?await customers.FindAppointmentReferenceAsync(scope.Value!,id,token):null;
        if(customerId is not null&&customer is null)return Outcome<CommercialAppointment>.Fail("Resource.NotFound");
        if(input.OwnerId is {} ownerId){var readOwner=await access.RequireAsync("properties","PROPERTY_READ",token);if(!readOwner.IsSuccess||readOwner.Value!.OrganizationId!=scope.Value!.OrganizationId||readOwner.Value.ActiveEstablishmentId!=scope.Value.ActiveEstablishmentId)return Outcome<CommercialAppointment>.Fail("Access.Forbidden");if(await references.FindAgendaOwnerReferenceAsync(scope.Value,ownerId,token) is null)return Outcome<CommercialAppointment>.Fail("Resource.NotFound");}
        if(input.PropertyId is {} property)
        {
            var read=await access.RequireAsync("properties","PROPERTY_READ",token);if(!read.IsSuccess)return Outcome<CommercialAppointment>.Fail(read.Code!);
            if(read.Value!.OrganizationId!=scope.Value!.OrganizationId||read.Value.ActiveEstablishmentId!=scope.Value.ActiveEstablishmentId)return Outcome<CommercialAppointment>.Fail("Appointment.PropertyUnavailable");
            var propertyReference=await references.FindAgendaPropertyReferenceAsync(scope.Value!,property,token);
            if(propertyReference is null||input.Kind==AppointmentRules.PropertyVisit&&propertyReference.Status!="Available")return Outcome<CommercialAppointment>.Fail("Appointment.PropertyUnavailable");
        }
        var responsible=customer?.ResponsibleSubject??input.ResponsibleSubject??scope.Value!.Subject;
        var candidates=await directory.ReadAsync(scope.Value!,scope.Value!.ActiveEstablishmentId!.Value,responsible,token);
        if(!candidates.IsSuccess||candidates.Value!.OrganizationId!=scope.Value.OrganizationId||candidates.Value.EstablishmentId!=scope.Value.ActiveEstablishmentId)return Outcome<CommercialAppointment>.Fail("Appointment.ResponsibleUnavailable");
        var candidate=candidates.Value.Items.SingleOrDefault(x=>x.Subject==responsible);if(candidate is null)return Outcome<CommercialAppointment>.Fail("Appointment.ResponsibleUnavailable");
        if(input.Kind==AppointmentRules.Office&&input.Location!=scope.Value.Establishments!.Single(e=>e.Id==scope.Value.ActiveEstablishmentId).Name)return Outcome<CommercialAppointment>.Fail("Appointment.UnitInvalid");
        return await appointments.CreateAsync(scope.Value,customerId,valid.Value!,responsible,candidate.DisplayName,token);
    }
    public async Task<Outcome<CommercialAppointment>> ChangeAsync(AppointmentChange change,CancellationToken token=default)
    {
        var scope=await Scope("CUSTOMER_CONTACT_CREATE",token);if(!scope.IsSuccess)return Outcome<CommercialAppointment>.Fail(scope.Code!);
        if(change.AppointmentId==Guid.Empty||change.CustomerId is null&&change.OwnerId is null||change.CustomerId is not null&&change.OwnerId is not null||change.Version<1)return Outcome<CommercialAppointment>.Fail("Appointment.InvalidInput");
        var current=await appointments.FindAppointmentAsync(scope.Value!,change.AppointmentId,token);if(current is null)return Outcome<CommercialAppointment>.Fail("Resource.NotFound");
        if(current.CustomerId!=change.CustomerId||current.OwnerId!=change.OwnerId)return Outcome<CommercialAppointment>.Fail("Resource.NotFound");
        if(current.PropertyId is not null||current.OwnerId is not null){var propertyRead=await access.RequireAsync("properties","PROPERTY_READ",token);if(!propertyRead.IsSuccess||propertyRead.Value!.OrganizationId!=scope.Value!.OrganizationId||propertyRead.Value.ActiveEstablishmentId!=scope.Value.ActiveEstablishmentId)return Outcome<CommercialAppointment>.Fail("Access.Forbidden");}
        if(!AppointmentRules.CanTransition(current.Status,change.Action))return Outcome<CommercialAppointment>.Fail("Appointment.InvalidTransition");
        if(change.Action=="Reagendar")
        {
            if(change.Schedule is null)return Outcome<CommercialAppointment>.Fail("Appointment.InvalidInput");
            var valid=AppointmentRules.Validate(change.Schedule,DateTimeOffset.UtcNow);if(!valid.IsSuccess||valid.Value!.Kind!=current.Kind||valid.Value.PropertyId!=current.PropertyId||valid.Value.OwnerId!=current.OwnerId)return Outcome<CommercialAppointment>.Fail("Appointment.InvalidInput");
            change=change with{Schedule=valid.Value};
        }
        if(change.Action=="Realizar"&&CustomerRules.Clean(change.Result) is not {} result||change.Action=="Realizar"&&change.Result!.Length>4000)return Outcome<CommercialAppointment>.Fail("Appointment.ResultRequired");
        if(change.Action=="Realizar"&&current.StartsAt>DateTimeOffset.UtcNow)return Outcome<CommercialAppointment>.Fail("Appointment.InvalidTransition");
        return await appointments.ChangeAsync(scope.Value!,change,token);
    }
}

