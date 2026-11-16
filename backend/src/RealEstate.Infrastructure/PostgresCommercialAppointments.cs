using Npgsql;
using NpgsqlTypes;
using RealEstate.Domain;
namespace RealEstate.Infrastructure;

public sealed partial class PostgresPropertyStore:ICommercialAppointmentStore
{
    private const string AppointmentColumns="id,customer_id,owner_id,property_id,kind,status,responsible_subject,responsible_name,starts_at,ends_at,location,notes,result,version,updated_at";
    private static CommercialAppointment ReadAppointment(NpgsqlDataReader r)=>new(r.GetGuid(0),r.IsDBNull(1)?null:r.GetGuid(1),r.IsDBNull(2)?null:r.GetGuid(2),r.IsDBNull(3)?null:r.GetGuid(3),r.GetString(4),r.GetString(5),r.GetString(6),r.GetString(7),r.GetFieldValue<DateTimeOffset>(8),r.GetFieldValue<DateTimeOffset>(9),r.GetString(10),r.IsDBNull(11)?null:r.GetString(11),r.IsDBNull(12)?null:r.GetString(12),r.GetInt64(13),r.GetFieldValue<DateTimeOffset>(14));
    private static async Task<CommercialAppointment?> ReadOne(NpgsqlConnection c,NpgsqlTransaction tx,AccessScope scope,Guid id,CancellationToken token)
    {
        await using var cmd=new NpgsqlCommand("SELECT "+AppointmentColumns+" FROM real_estate.commercial_appointments WHERE organization_id=@org AND establishment_id=@unit AND id=@id",c,tx);CustomerScope(cmd,scope);cmd.Parameters.AddWithValue("id",id);
        await using var r=await cmd.ExecuteReaderAsync(token);return await r.ReadAsync(token)?ReadAppointment(r):null;
    }
    public async Task<CommercialAppointment?> FindAppointmentAsync(AccessScope scope,Guid id,CancellationToken token)
    {
        await using var c=await dataSource.OpenConnectionAsync(token);await using var tx=await Begin(c,scope,token);var result=await ReadOne(c,tx,scope,id,token);await tx.CommitAsync(token);return result;
    }
    public async Task<IReadOnlyList<CommercialAppointment>> ListAsync(AccessScope scope,Guid? customerId,DateTimeOffset from,DateTimeOffset until,CancellationToken token)
    {
        await using var c=await dataSource.OpenConnectionAsync(token);await using var tx=await Begin(c,scope,token);
        var sql="SELECT "+AppointmentColumns+" FROM real_estate.commercial_appointments WHERE organization_id=@org AND establishment_id=@unit AND starts_at>=@from AND starts_at<@until"+(customerId.HasValue?" AND customer_id=@customer":"")+" ORDER BY starts_at,id";
        await using var cmd=new NpgsqlCommand(sql,c,tx);CustomerScope(cmd,scope);cmd.Parameters.AddWithValue("from",from);cmd.Parameters.AddWithValue("until",until);if(customerId.HasValue)cmd.Parameters.AddWithValue("customer",customerId.Value);
        var result=new List<CommercialAppointment>();await using(var r=await cmd.ExecuteReaderAsync(token)){while(await r.ReadAsync(token))result.Add(ReadAppointment(r));}await tx.CommitAsync(token);return result;
    }
    public async Task<IReadOnlyList<AppointmentEvent>> EventsAsync(AccessScope scope,Guid customerId,CancellationToken token)
    {
        await using var c=await dataSource.OpenConnectionAsync(token);await using var tx=await Begin(c,scope,token);
        await using var cmd=new NpgsqlCommand("SELECT e.id,e.appointment_id,e.version,e.action,e.at,e.actor,e.responsible_subject,e.starts_at,e.ends_at,e.result FROM real_estate.commercial_appointment_events e JOIN real_estate.commercial_appointments a ON a.organization_id=e.organization_id AND a.id=e.appointment_id WHERE a.organization_id=@org AND a.establishment_id=@unit AND a.customer_id=@customer ORDER BY e.at DESC,e.id DESC",c,tx);
        CustomerScope(cmd,scope);cmd.Parameters.AddWithValue("customer",customerId);var result=new List<AppointmentEvent>();await using(var r=await cmd.ExecuteReaderAsync(token)){while(await r.ReadAsync(token))result.Add(new(r.GetGuid(0),r.GetGuid(1),r.GetInt64(2),r.GetString(3),r.GetFieldValue<DateTimeOffset>(4),r.GetString(5),r.GetString(6),r.GetFieldValue<DateTimeOffset>(7),r.GetFieldValue<DateTimeOffset>(8),r.IsDBNull(9)?null:r.GetString(9)));}await tx.CommitAsync(token);return result;
    }
    public async Task<IReadOnlyList<AppointmentEvent>> EventsForAppointmentAsync(AccessScope scope,Guid appointmentId,CancellationToken token)
    {
        await using var c=await dataSource.OpenConnectionAsync(token);await using var tx=await Begin(c,scope,token);
        await using var cmd=new NpgsqlCommand("SELECT e.id,e.appointment_id,e.version,e.action,e.at,e.actor,e.responsible_subject,e.starts_at,e.ends_at,e.result FROM real_estate.commercial_appointment_events e JOIN real_estate.commercial_appointments a ON a.organization_id=e.organization_id AND a.id=e.appointment_id WHERE a.organization_id=@org AND a.establishment_id=@unit AND a.id=@appointment ORDER BY e.version DESC",c,tx);
        CustomerScope(cmd,scope);cmd.Parameters.AddWithValue("appointment",appointmentId);var result=new List<AppointmentEvent>();await using(var r=await cmd.ExecuteReaderAsync(token)){while(await r.ReadAsync(token))result.Add(new(r.GetGuid(0),r.GetGuid(1),r.GetInt64(2),r.GetString(3),r.GetFieldValue<DateTimeOffset>(4),r.GetString(5),r.GetString(6),r.GetFieldValue<DateTimeOffset>(7),r.GetFieldValue<DateTimeOffset>(8),r.IsDBNull(9)?null:r.GetString(9)));}await tx.CommitAsync(token);return result;
    }
    private static async Task Event(NpgsqlConnection c,NpgsqlTransaction tx,AccessScope scope,CommercialAppointment appointment,string action,CancellationToken token)
    {
        await using var cmd=new NpgsqlCommand("INSERT INTO real_estate.commercial_appointment_events(organization_id,establishment_id,id,appointment_id,version,action,actor,responsible_subject,starts_at,ends_at,result) VALUES(@org,@unit,@id,@appointment,@version,@action,@actor,@responsible,@start,@end,@result)",c,tx);
        CustomerScope(cmd,scope);cmd.Parameters.AddWithValue("id",Guid.NewGuid());cmd.Parameters.AddWithValue("appointment",appointment.Id);cmd.Parameters.AddWithValue("version",appointment.Version);cmd.Parameters.AddWithValue("action",action);cmd.Parameters.AddWithValue("actor",scope.Subject);cmd.Parameters.AddWithValue("responsible",appointment.ResponsibleSubject);cmd.Parameters.AddWithValue("start",appointment.StartsAt);cmd.Parameters.AddWithValue("end",appointment.EndsAt);cmd.Parameters.Add("result",NpgsqlDbType.Text).Value=(object?)appointment.Result??DBNull.Value;await cmd.ExecuteNonQueryAsync(token);
    }
    public async Task<Outcome<CommercialAppointment>> CreateAsync(AccessScope scope,Guid? customerId,AppointmentInput input,string responsibleSubject,string responsibleName,CancellationToken token)
    {
        await using var c=await dataSource.OpenConnectionAsync(token);await using var tx=await Begin(c,scope,token);
        await LockAgendaSubject(c,tx,scope,responsibleSubject,token);
        var id=Guid.NewGuid();var sql="INSERT INTO real_estate.commercial_appointments(organization_id,establishment_id,id,request_id,customer_id,owner_id,property_id,kind,responsible_subject,responsible_name,starts_at,ends_at,location,notes,created_by,updated_by) SELECT @org,@unit,@id,@request,@customer,@owner,@property,@kind,@responsible,@name,@start,@end,@location,@notes,@actor,@actor WHERE ((@customer IS NOT NULL AND EXISTS(SELECT 1 FROM real_estate.customers cu WHERE cu.organization_id=@org AND cu.establishment_id=@unit AND cu.id=@customer AND cu.responsible_subject=@responsible)) OR (@owner IS NOT NULL AND EXISTS(SELECT 1 FROM real_estate.owners ow WHERE ow.organization_id=@org AND ow.id=@owner AND ow.active))) AND (@property IS NULL OR EXISTS(SELECT 1 FROM real_estate.properties pr WHERE pr.organization_id=@org AND pr.establishment_id=@unit AND pr.id=@property AND (@kind='Visita de captação' OR pr.status='Available'))) ON CONFLICT(organization_id,establishment_id,request_id) DO NOTHING RETURNING "+AppointmentColumns;
        await using var cmd=new NpgsqlCommand(sql,c,tx);CustomerScope(cmd,scope);cmd.Parameters.AddWithValue("id",id);cmd.Parameters.AddWithValue("request",input.RequestId);cmd.Parameters.Add("customer",NpgsqlDbType.Uuid).Value=(object?)customerId??DBNull.Value;cmd.Parameters.Add("owner",NpgsqlDbType.Uuid).Value=(object?)input.OwnerId??DBNull.Value;cmd.Parameters.Add("property",NpgsqlDbType.Uuid).Value=(object?)input.PropertyId??DBNull.Value;cmd.Parameters.AddWithValue("kind",input.Kind);cmd.Parameters.AddWithValue("responsible",responsibleSubject);cmd.Parameters.AddWithValue("name",responsibleName);cmd.Parameters.AddWithValue("start",input.StartsAt);cmd.Parameters.AddWithValue("end",input.EndsAt);cmd.Parameters.AddWithValue("location",input.Location);cmd.Parameters.Add("notes",NpgsqlDbType.Text).Value=(object?)input.Notes??DBNull.Value;cmd.Parameters.AddWithValue("actor",scope.Subject);
        CommercialAppointment? created;await using(var r=await cmd.ExecuteReaderAsync(token)){created=await r.ReadAsync(token)?ReadAppointment(r):null;}
        if(created is null){await using var existing=new NpgsqlCommand("SELECT "+AppointmentColumns+" FROM real_estate.commercial_appointments WHERE organization_id=@org AND establishment_id=@unit AND request_id=@request",c,tx);CustomerScope(existing,scope);existing.Parameters.AddWithValue("request",input.RequestId);CommercialAppointment? prior;await using(var r=await existing.ExecuteReaderAsync(token)){prior=await r.ReadAsync(token)?ReadAppointment(r):null;}if(prior is null)return Outcome<CommercialAppointment>.Fail("Resource.NotFound");static long PgTicks(DateTimeOffset value)=>value.UtcTicks/10; if(prior.CustomerId!=customerId||prior.OwnerId!=input.OwnerId||prior.Kind!=input.Kind||prior.PropertyId!=input.PropertyId||PgTicks(prior.StartsAt)!=PgTicks(input.StartsAt)||PgTicks(prior.EndsAt)!=PgTicks(input.EndsAt)||prior.Location!=input.Location||prior.Notes!=input.Notes)return Outcome<CommercialAppointment>.Fail("Appointment.RequestConflict");await tx.CommitAsync(token);return new(prior);}
        await Event(c,tx,scope,created,"Criado",token);await tx.CommitAsync(token);return new(created);
    }
    public async Task<Outcome<CommercialAppointment>> ChangeAsync(AccessScope scope,AppointmentChange change,CancellationToken token)
    {
        await using var c=await dataSource.OpenConnectionAsync(token);await using var tx=await Begin(c,scope,token);
        await using var ownership=new NpgsqlCommand("SELECT responsible_subject FROM real_estate.commercial_appointments WHERE organization_id=@org AND establishment_id=@unit AND id=@id",c,tx);CustomerScope(ownership,scope);ownership.Parameters.AddWithValue("id",change.AppointmentId);var owner=await ownership.ExecuteScalarAsync(token) as string;if(owner is null)return Outcome<CommercialAppointment>.Fail("Resource.NotFound");await LockAgendaSubject(c,tx,scope,owner,token);
        await using var cmd=new NpgsqlCommand("SELECT "+AppointmentColumns+" FROM real_estate.commercial_appointments WHERE organization_id=@org AND establishment_id=@unit AND id=@id FOR UPDATE",c,tx);CustomerScope(cmd,scope);cmd.Parameters.AddWithValue("id",change.AppointmentId);
        CommercialAppointment? before;await using(var r=await cmd.ExecuteReaderAsync(token)){before=await r.ReadAsync(token)?ReadAppointment(r):null;}
        if(before is null||before.CustomerId!=change.CustomerId||before.OwnerId!=change.OwnerId)return Outcome<CommercialAppointment>.Fail("Resource.NotFound");if(before.ResponsibleSubject!=owner)return Outcome<CommercialAppointment>.Fail("Appointment.VersionConflict");if(before.Version!=change.Version)return Outcome<CommercialAppointment>.Fail("Appointment.VersionConflict");if(!AppointmentRules.CanTransition(before.Status,change.Action))return Outcome<CommercialAppointment>.Fail("Appointment.InvalidTransition");
        var status=change.Action switch{"Cancelar"=>"Cancelado","Realizar"=>"Realizado",_=>"Agendado"};var start=change.Schedule?.StartsAt??before.StartsAt;var end=change.Schedule?.EndsAt??before.EndsAt;var location=change.Schedule?.Location??before.Location;var notes=change.Schedule?.Notes??before.Notes;
        await using var update=new NpgsqlCommand("UPDATE real_estate.commercial_appointments SET status=@status,starts_at=@start,ends_at=@end,location=@location,notes=@notes,result=@result,version=version+1,updated_by=@actor,updated_at=clock_timestamp() WHERE organization_id=@org AND establishment_id=@unit AND id=@id RETURNING "+AppointmentColumns,c,tx);CustomerScope(update,scope);update.Parameters.AddWithValue("id",change.AppointmentId);update.Parameters.AddWithValue("status",status);update.Parameters.AddWithValue("start",start);update.Parameters.AddWithValue("end",end);update.Parameters.AddWithValue("location",location);update.Parameters.Add("notes",NpgsqlDbType.Text).Value=(object?)notes??DBNull.Value;update.Parameters.Add("result",NpgsqlDbType.Text).Value=(object?)change.Result??DBNull.Value;update.Parameters.AddWithValue("actor",scope.Subject);
        CommercialAppointment after;await using(var r=await update.ExecuteReaderAsync(token)){await r.ReadAsync(token);after=ReadAppointment(r);}
        var action=change.Action switch{"Reagendar"=>"Reagendado","Cancelar"=>"Cancelado",_=>"Realizado"};await Event(c,tx,scope,after,action,token);await tx.CommitAsync(token);return new(after);
    }
}

