namespace RealEstate.Domain;

public sealed record AppointmentInput(Guid RequestId,string Kind,Guid? PropertyId,DateTimeOffset StartsAt,DateTimeOffset EndsAt,string Location,string? Notes,Guid? OwnerId=null,string? ResponsibleSubject=null);
public sealed record AppointmentChange(Guid AppointmentId,Guid? CustomerId,Guid? OwnerId,long Version,string Action,AppointmentInput? Schedule=null,string? Result=null);
public sealed record CommercialAppointment(Guid Id,Guid? CustomerId,Guid? OwnerId,Guid? PropertyId,string Kind,string Status,string ResponsibleSubject,string ResponsibleName,DateTimeOffset StartsAt,DateTimeOffset EndsAt,string Location,string? Notes,string? Result,long Version,DateTimeOffset UpdatedAt);
public sealed record AppointmentEvent(Guid Id,Guid AppointmentId,long Version,string Action,DateTimeOffset At,string Actor,string ResponsibleSubject,DateTimeOffset StartsAt,DateTimeOffset EndsAt,string? Result);
// Future user-authorized adapters implement this contract; the ERP record remains authoritative.
public interface ICalendarConnector
{
    string ProviderKey{get;}
    Task<Outcome<string>> UpsertAsync(CommercialAppointment appointment,string? externalId,CancellationToken token);
    Task<Outcome<bool>> CancelAsync(string externalId,long sourceVersion,CancellationToken token);
}
public interface ICommercialAppointmentStore
{
    Task<IReadOnlyList<CommercialAppointment>> ListAsync(AccessScope scope,Guid? customerId,DateTimeOffset from,DateTimeOffset until,CancellationToken token);
    Task<CommercialAppointment?> FindAppointmentAsync(AccessScope scope,Guid id,CancellationToken token);
    Task<IReadOnlyList<AppointmentEvent>> EventsAsync(AccessScope scope,Guid customerId,CancellationToken token);
    Task<IReadOnlyList<AppointmentEvent>> EventsForAppointmentAsync(AccessScope scope,Guid appointmentId,CancellationToken token);
    Task<Outcome<CommercialAppointment>> CreateAsync(AccessScope scope,Guid? customerId,AppointmentInput input,string responsibleSubject,string responsibleName,CancellationToken token);
    Task<Outcome<CommercialAppointment>> ChangeAsync(AccessScope scope,AppointmentChange change,CancellationToken token);
}
public static class AppointmentRules
{
    public const string PropertyVisit="Visita ao imóvel",Office="Atendimento na imobiliária",Capture="Visita de captação";
    public static readonly string[] Kinds=[PropertyVisit,Office,Capture];
    public static Outcome<AppointmentInput> Validate(AppointmentInput input,DateTimeOffset now)
    {
        if(input is null||input.RequestId==Guid.Empty||!Kinds.Contains(input.Kind)||input.Kind==PropertyVisit&&input.PropertyId is null||input.Kind==Office&&(input.PropertyId is not null||input.OwnerId is not null)||input.Kind!=Capture&&input.OwnerId is not null||
           input.StartsAt.Offset!=TimeSpan.Zero||input.EndsAt.Offset!=TimeSpan.Zero||input.StartsAt<=now||input.EndsAt<=input.StartsAt||
           input.EndsAt-input.StartsAt>TimeSpan.FromHours(12)||CustomerRules.Clean(input.Location) is not {} location||location.Length>300||input.Notes?.Length>4000)
            return Outcome<AppointmentInput>.Fail("Appointment.InvalidInput");
        return new(input with{Location=location,Notes=CustomerRules.Clean(input.Notes)});
    }
    public static bool CanTransition(string status,string action)=>status=="Agendado"&&action is "Reagendar" or "Cancelar" or "Realizar";
}

