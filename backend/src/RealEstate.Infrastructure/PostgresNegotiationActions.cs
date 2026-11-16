using Npgsql;
using NpgsqlTypes;
using RealEstate.Domain;

namespace RealEstate.Infrastructure;

public sealed partial class PostgresPropertyStore : INegotiationActionStore
{
    private const string ActionColumns = "id,negotiation_id,customer_id,property_id,appointment_id,kind,description,due_at,responsible_subject,responsible_name,status,result,created_by,created_at,version,updated_at";
    private static NegotiationAction ReadAction(NpgsqlDataReader r) => new(r.GetGuid(0), r.GetGuid(1), r.GetGuid(2), r.GetGuid(3), r.IsDBNull(4) ? null : r.GetGuid(4),
        r.GetString(5), r.GetString(6), r.IsDBNull(7) ? null : r.GetFieldValue<DateTimeOffset>(7), r.GetString(8), r.GetString(9), r.GetString(10),
        r.IsDBNull(11) ? null : r.GetString(11), r.GetString(12), r.GetFieldValue<DateTimeOffset>(13), r.GetInt64(14), r.GetFieldValue<DateTimeOffset>(15));

    public async Task<IReadOnlyList<NegotiationAction>> ListActionsAsync(AccessScope scope, Guid negotiationId, CancellationToken token)
    {
        await using var c = await dataSource.OpenConnectionAsync(token); await using var tx = await Begin(c, scope, token);
        await using var cmd = new NpgsqlCommand("SELECT " + ActionColumns + " FROM real_estate.commercial_negotiation_actions WHERE organization_id=@org AND establishment_id=@unit AND negotiation_id=@negotiation ORDER BY (status='Aberta') DESC,due_at NULLS LAST,created_at DESC,id", c, tx);
        CustomerScope(cmd, scope); cmd.Parameters.AddWithValue("negotiation", negotiationId);
        var result = new List<NegotiationAction>(); await using (var r = await cmd.ExecuteReaderAsync(token)) { while (await r.ReadAsync(token)) result.Add(ReadAction(r)); }
        await tx.CommitAsync(token); return result;
    }

    public async Task<IReadOnlyList<NegotiationActionEvent>> ActionEventsAsync(AccessScope scope, Guid negotiationId, CancellationToken token)
    {
        await using var c = await dataSource.OpenConnectionAsync(token); await using var tx = await Begin(c, scope, token);
        await using var cmd = new NpgsqlCommand("SELECT e.id,e.action_id,e.version,e.event,e.actor,e.responsible_subject,e.description,e.due_at,e.result,e.at FROM real_estate.commercial_negotiation_action_events e JOIN real_estate.commercial_negotiation_actions a ON a.organization_id=e.organization_id AND a.establishment_id=e.establishment_id AND a.id=e.action_id WHERE a.organization_id=@org AND a.establishment_id=@unit AND a.negotiation_id=@negotiation ORDER BY e.at,e.id", c, tx);
        CustomerScope(cmd, scope); cmd.Parameters.AddWithValue("negotiation", negotiationId);
        var result = new List<NegotiationActionEvent>(); await using (var r = await cmd.ExecuteReaderAsync(token))
        { while (await r.ReadAsync(token)) result.Add(new(r.GetGuid(0), r.GetGuid(1), r.GetInt64(2), r.GetString(3), r.GetString(4), r.GetString(5), r.GetString(6), r.IsDBNull(7) ? null : r.GetFieldValue<DateTimeOffset>(7), r.IsDBNull(8) ? null : r.GetString(8), r.GetFieldValue<DateTimeOffset>(9))); }
        await tx.CommitAsync(token); return result;
    }

    public async Task<Outcome<NegotiationAction?>> ExistingCreateRequestAsync(AccessScope scope, Guid negotiationId, NegotiationActionInput input, CancellationToken token)
    {
        await using var c = await dataSource.OpenConnectionAsync(token); await using var tx = await Begin(c, scope, token);
        await using var cmd = new NpgsqlCommand("SELECT " + ActionColumns + " FROM real_estate.commercial_negotiation_actions WHERE organization_id=@org AND establishment_id=@unit AND request_id=@request", c, tx);
        CustomerScope(cmd, scope); cmd.Parameters.AddWithValue("request", input.RequestId);
        NegotiationAction? existing; await using (var r = await cmd.ExecuteReaderAsync(token)) existing = await r.ReadAsync(token) ? ReadAction(r) : null;
        if (existing is null) { await tx.CommitAsync(token); return new((NegotiationAction?)null); }
        static long PgTicks(DateTimeOffset value) => value.UtcTicks / 10;
        var same = existing.NegotiationId == negotiationId && existing.Kind == input.Kind && existing.Description == CustomerRules.Clean(input.Description) &&
            (existing.DueAt is null && input.DueAt is null || existing.DueAt is {} storedDue && input.DueAt is {} inputDue && PgTicks(storedDue) == PgTicks(inputDue)) &&
            (string.IsNullOrWhiteSpace(input.ResponsibleSubject) || existing.ResponsibleSubject == input.ResponsibleSubject.Trim()) &&
            (input.Kind == NegotiationActionKinds.Appointment) == existing.AppointmentId.HasValue;
        if (same && input.Appointment is { } rawAppointment && existing.AppointmentId is { } appointmentId)
        {
            var validated = AppointmentRules.Validate(rawAppointment, DateTimeOffset.MinValue);
            same = validated.IsSuccess && rawAppointment.RequestId == input.RequestId;
            if (same)
            {
                var app = validated.Value!;
                await using var linked = new NpgsqlCommand("SELECT 1 FROM real_estate.commercial_appointments WHERE organization_id=@org AND establishment_id=@unit AND id=@id AND request_id=@request AND customer_id=@customer AND property_id IS NOT DISTINCT FROM @property AND kind=@kind AND starts_at=@start AND ends_at=@end AND location=@location AND notes IS NOT DISTINCT FROM @notes AND (@responsible IS NULL OR responsible_subject=@responsible)", c, tx);
                CustomerScope(linked, scope); linked.Parameters.AddWithValue("id", appointmentId); linked.Parameters.AddWithValue("request", input.RequestId); linked.Parameters.AddWithValue("customer", existing.CustomerId);
                linked.Parameters.Add("property", NpgsqlDbType.Uuid).Value = (object?)app.PropertyId ?? DBNull.Value; linked.Parameters.AddWithValue("kind", app.Kind); linked.Parameters.AddWithValue("start", app.StartsAt); linked.Parameters.AddWithValue("end", app.EndsAt); linked.Parameters.AddWithValue("location", app.Location);
                linked.Parameters.Add("notes", NpgsqlDbType.Text).Value = (object?)app.Notes ?? DBNull.Value; linked.Parameters.Add("responsible", NpgsqlDbType.Text).Value = (object?)rawAppointment.ResponsibleSubject ?? DBNull.Value;
                same = await linked.ExecuteScalarAsync(token) is not null;
            }
        }
        if (!same) return Outcome<NegotiationAction?>.Fail("NegotiationAction.RequestConflict");
        await tx.CommitAsync(token); return new(existing);
    }

    public async Task<Outcome<NegotiationAction>> CreateActionAsync(AccessScope scope, Negotiation negotiation, NegotiationActionInput input,
        NegotiationAssignee responsible, NegotiationActionAppointment? appointment, CancellationToken token)
    {
        await using var c = await dataSource.OpenConnectionAsync(token); await using var tx = await Begin(c, scope, token);
        try
        {
            Guid customerId, propertyId; long negotiationVersion;
            await using (var current = new NpgsqlCommand("SELECT customer_id,property_id,responsible_subject,version FROM real_estate.commercial_negotiations WHERE organization_id=@org AND establishment_id=@unit AND id=@id FOR UPDATE", c, tx))
            {
                CustomerScope(current, scope); current.Parameters.AddWithValue("id", negotiation.Id);
                await using var r = await current.ExecuteReaderAsync(token);
                if (!await r.ReadAsync(token)) return Outcome<NegotiationAction>.Fail("Resource.NotFound");
                customerId = r.GetGuid(0); propertyId = r.GetGuid(1); negotiationVersion = r.GetInt64(3);
                if (string.IsNullOrWhiteSpace(r.GetString(2))) return Outcome<NegotiationAction>.Fail("NegotiationAction.ResponsibleUnavailable");
            }

            await using (var prior = new NpgsqlCommand("SELECT " + ActionColumns + " FROM real_estate.commercial_negotiation_actions WHERE organization_id=@org AND establishment_id=@unit AND request_id=@request", c, tx))
            {
                CustomerScope(prior, scope); prior.Parameters.AddWithValue("request", input.RequestId);
                NegotiationAction? existing; await using (var r = await prior.ExecuteReaderAsync(token)) existing = await r.ReadAsync(token) ? ReadAction(r) : null;
                if (existing is not null)
                {
                    var same = existing.NegotiationId == negotiation.Id && existing.Kind == input.Kind && existing.Description == input.Description &&
                        existing.CustomerId == customerId && existing.PropertyId == propertyId && existing.DueAt == input.DueAt && existing.ResponsibleSubject == responsible.Subject &&
                        (appointment is null ? existing.AppointmentId is null : existing.AppointmentId is not null);
                    if (!same) return Outcome<NegotiationAction>.Fail("NegotiationAction.RequestConflict");
                    if (appointment is not null)
                    {
                        await using var ap = new NpgsqlCommand("SELECT 1 FROM real_estate.commercial_appointments WHERE organization_id=@org AND establishment_id=@unit AND id=@id AND request_id=@request AND customer_id=@customer AND property_id IS NOT DISTINCT FROM @property AND kind=@kind AND starts_at=@start AND ends_at=@end AND location=@location AND notes IS NOT DISTINCT FROM @notes AND responsible_subject=@responsible", c, tx);
                        CustomerScope(ap, scope); ap.Parameters.AddWithValue("id", existing.AppointmentId!.Value); ap.Parameters.AddWithValue("request", input.RequestId); ap.Parameters.AddWithValue("customer", negotiation.CustomerId);
                        ap.Parameters.Add("property", NpgsqlDbType.Uuid).Value = (object?)appointment.Input.PropertyId ?? DBNull.Value; ap.Parameters.AddWithValue("kind", appointment.Input.Kind);
                        ap.Parameters.AddWithValue("start", appointment.Input.StartsAt); ap.Parameters.AddWithValue("end", appointment.Input.EndsAt); ap.Parameters.AddWithValue("location", appointment.Input.Location);
                        ap.Parameters.Add("notes", NpgsqlDbType.Text).Value = (object?)appointment.Input.Notes ?? DBNull.Value; ap.Parameters.AddWithValue("responsible", responsible.Subject);
                        if (await ap.ExecuteScalarAsync(token) is null) return Outcome<NegotiationAction>.Fail("NegotiationAction.RequestConflict");
                    }
                    await tx.CommitAsync(token); return new(existing);
                }
            }
            if (customerId != negotiation.CustomerId || propertyId != negotiation.PropertyId || negotiationVersion != input.ExpectedNegotiationVersion)
                return Outcome<NegotiationAction>.Fail("Negotiation.VersionConflict");

            Guid? appointmentId = null;
            if (appointment is not null)
            {
                var app = appointment.Input; var id = Guid.NewGuid();
                await using var insertAppointment = new NpgsqlCommand("INSERT INTO real_estate.commercial_appointments(organization_id,establishment_id,id,request_id,customer_id,property_id,kind,responsible_subject,responsible_name,starts_at,ends_at,location,notes,created_by,updated_by) SELECT @org,@unit,@id,@request,@customer,@property,@kind,@responsible,@name,@start,@end,@location,@notes,@actor,@actor WHERE EXISTS(SELECT 1 FROM real_estate.customers cu WHERE cu.organization_id=@org AND cu.establishment_id=@unit AND cu.id=@customer) AND (@property IS NULL OR EXISTS(SELECT 1 FROM real_estate.properties pr WHERE pr.organization_id=@org AND pr.establishment_id=@unit AND pr.id=@property AND pr.status='Available')) RETURNING " + AppointmentColumns, c, tx);
                CustomerScope(insertAppointment, scope); insertAppointment.Parameters.AddWithValue("id", id); insertAppointment.Parameters.AddWithValue("request", input.RequestId); insertAppointment.Parameters.AddWithValue("customer", negotiation.CustomerId);
                insertAppointment.Parameters.Add("property", NpgsqlDbType.Uuid).Value = (object?)app.PropertyId ?? DBNull.Value; insertAppointment.Parameters.AddWithValue("kind", app.Kind);
                insertAppointment.Parameters.AddWithValue("responsible", responsible.Subject); insertAppointment.Parameters.AddWithValue("name", responsible.DisplayName); insertAppointment.Parameters.AddWithValue("start", app.StartsAt);
                insertAppointment.Parameters.AddWithValue("end", app.EndsAt); insertAppointment.Parameters.AddWithValue("location", app.Location); insertAppointment.Parameters.Add("notes", NpgsqlDbType.Text).Value = (object?)app.Notes ?? DBNull.Value; insertAppointment.Parameters.AddWithValue("actor", scope.Subject);
                CommercialAppointment? createdAppointment; await using (var r = await insertAppointment.ExecuteReaderAsync(token)) createdAppointment = await r.ReadAsync(token) ? ReadAppointment(r) : null;
                if (createdAppointment is null) return Outcome<NegotiationAction>.Fail("Appointment.PropertyUnavailable");
                appointmentId = createdAppointment.Id; await Event(c, tx, scope, createdAppointment, "Criado", token); await Audit(c, tx, scope, createdAppointment.Id, "CommercialAppointmentCreated", token);
            }

            var actionId = Guid.NewGuid();
            await using (var insert = new NpgsqlCommand("INSERT INTO real_estate.commercial_negotiation_actions(organization_id,establishment_id,id,negotiation_id,customer_id,property_id,request_id,appointment_id,kind,description,due_at,responsible_subject,responsible_name,created_by,updated_by) VALUES(@org,@unit,@id,@negotiation,@customer,@property,@request,@appointment,@kind,@description,@due,@responsible,@name,@actor,@actor) RETURNING " + ActionColumns, c, tx))
            {
                CustomerScope(insert, scope); insert.Parameters.AddWithValue("id", actionId); insert.Parameters.AddWithValue("negotiation", negotiation.Id); insert.Parameters.AddWithValue("customer", customerId); insert.Parameters.AddWithValue("property", propertyId); insert.Parameters.AddWithValue("request", input.RequestId);
                insert.Parameters.Add("appointment", NpgsqlDbType.Uuid).Value = (object?)appointmentId ?? DBNull.Value; insert.Parameters.AddWithValue("kind", input.Kind); insert.Parameters.AddWithValue("description", input.Description);
                insert.Parameters.Add("due", NpgsqlDbType.TimestampTz).Value = (object?)input.DueAt ?? DBNull.Value; insert.Parameters.AddWithValue("responsible", responsible.Subject); insert.Parameters.AddWithValue("name", responsible.DisplayName); insert.Parameters.AddWithValue("actor", scope.Subject);
                NegotiationAction created; await using (var r = await insert.ExecuteReaderAsync(token)) { await r.ReadAsync(token); created = ReadAction(r); }
                await ActionEvent(c, tx, scope, created, "Criada", input.RequestId, token); await Audit(c, tx, scope, created.Id, "NegotiationActionCreated", token);
                await tx.CommitAsync(token); return new(created);
            }
        }
        catch (PostgresException e) when (e.SqlState == "23505") { return Outcome<NegotiationAction>.Fail(e.ConstraintName == "commercial_negotiation_actions_organization_id_appointment_id_key" ? "NegotiationAction.AppointmentAlreadyLinked" : "NegotiationAction.RequestConflict"); }
    }

    private static async Task ActionEvent(NpgsqlConnection c, NpgsqlTransaction tx, AccessScope scope, NegotiationAction action, string name, Guid requestId, CancellationToken token)
    {
        await using var cmd = new NpgsqlCommand("INSERT INTO real_estate.commercial_negotiation_action_events(organization_id,establishment_id,id,action_id,version,request_id,event,actor,responsible_subject,description,due_at,result) VALUES(@org,@unit,@id,@action,@version,@request,@event,@actor,@responsible,@description,@due,@result)", c, tx);
        CustomerScope(cmd, scope); cmd.Parameters.AddWithValue("id", Guid.NewGuid()); cmd.Parameters.AddWithValue("action", action.Id); cmd.Parameters.AddWithValue("version", action.Version); cmd.Parameters.AddWithValue("request", requestId); cmd.Parameters.AddWithValue("event", name); cmd.Parameters.AddWithValue("actor", scope.Subject); cmd.Parameters.AddWithValue("responsible", action.ResponsibleSubject); cmd.Parameters.AddWithValue("description", action.Description);
        cmd.Parameters.Add("due", NpgsqlDbType.TimestampTz).Value = (object?)action.DueAt ?? DBNull.Value; cmd.Parameters.Add("result", NpgsqlDbType.Text).Value = (object?)action.Result ?? DBNull.Value; await cmd.ExecuteNonQueryAsync(token);
    }

    public async Task<Outcome<NegotiationActionCompletionResult>> CompleteActionAsync(AccessScope scope, Guid actionId,
        NegotiationActionCompletion input, CancellationToken token)
    {
        await using var c = await dataSource.OpenConnectionAsync(token); await using var tx = await Begin(c, scope, token);
        await using var cmd = new NpgsqlCommand("SELECT " + ActionColumns + " FROM real_estate.commercial_negotiation_actions WHERE organization_id=@org AND establishment_id=@unit AND id=@id FOR UPDATE", c, tx);
        CustomerScope(cmd, scope); cmd.Parameters.AddWithValue("id", actionId);
        NegotiationAction? before; await using (var r = await cmd.ExecuteReaderAsync(token)) before = await r.ReadAsync(token) ? ReadAction(r) : null;
        if (before is null) return Outcome<NegotiationActionCompletionResult>.Fail("Resource.NotFound");

        await using (var prior = new NpgsqlCommand("SELECT 1 FROM real_estate.commercial_negotiation_action_events WHERE organization_id=@org AND establishment_id=@unit AND action_id=@id AND request_id=@request AND event='Concluída' AND result=@result", c, tx))
        {
            CustomerScope(prior, scope); prior.Parameters.AddWithValue("id", actionId); prior.Parameters.AddWithValue("request", input.RequestId); prior.Parameters.AddWithValue("result", input.Result);
            if (await prior.ExecuteScalarAsync(token) is not null)
            {
                CommercialAppointment? priorAppointment = before.AppointmentId is { } priorId ? await ReadOne(c, tx, scope, priorId, token) : null;
                await tx.CommitAsync(token); return new(new(before, priorAppointment));
            }
        }
        if (before.Version != input.ExpectedVersion) return Outcome<NegotiationActionCompletionResult>.Fail("NegotiationAction.VersionConflict");
        if (before.Status != "Aberta") return Outcome<NegotiationActionCompletionResult>.Fail("NegotiationAction.AlreadyCompleted");

        CommercialAppointment? appointmentAfter = null;
        if (before.AppointmentId is { } appointmentId)
        {
            await using var ap = new NpgsqlCommand("SELECT " + AppointmentColumns + " FROM real_estate.commercial_appointments WHERE organization_id=@org AND establishment_id=@unit AND id=@id FOR UPDATE", c, tx);
            CustomerScope(ap, scope); ap.Parameters.AddWithValue("id", appointmentId);
            CommercialAppointment? appointmentBefore; await using (var r = await ap.ExecuteReaderAsync(token)) appointmentBefore = await r.ReadAsync(token) ? ReadAppointment(r) : null;
            if (appointmentBefore is null || appointmentBefore.CustomerId != before.CustomerId) return Outcome<NegotiationActionCompletionResult>.Fail("NegotiationAction.IntegrityError");
            if (appointmentBefore.Version != input.ExpectedAppointmentVersion) return Outcome<NegotiationActionCompletionResult>.Fail("Appointment.VersionConflict");
            if (!AppointmentRules.CanTransition(appointmentBefore.Status, "Realizar") || appointmentBefore.StartsAt > DateTimeOffset.UtcNow) return Outcome<NegotiationActionCompletionResult>.Fail("Appointment.InvalidTransition");
            await using var updateAppointment = new NpgsqlCommand("UPDATE real_estate.commercial_appointments SET status='Realizado',result=@result,version=version+1,updated_by=@actor,updated_at=clock_timestamp() WHERE organization_id=@org AND establishment_id=@unit AND id=@id RETURNING " + AppointmentColumns, c, tx);
            CustomerScope(updateAppointment, scope); updateAppointment.Parameters.AddWithValue("id", appointmentId); updateAppointment.Parameters.AddWithValue("result", input.Result); updateAppointment.Parameters.AddWithValue("actor", scope.Subject);
            await using (var r = await updateAppointment.ExecuteReaderAsync(token)) { await r.ReadAsync(token); appointmentAfter = ReadAppointment(r); }
            await Event(c, tx, scope, appointmentAfter!, "Realizado", token); await Audit(c, tx, scope, appointmentAfter!.Id, "CommercialAppointmentRealizado", token);
        }

        await using var update = new NpgsqlCommand("UPDATE real_estate.commercial_negotiation_actions SET status='Concluída',result=@result,version=version+1,updated_by=@actor,updated_at=clock_timestamp() WHERE organization_id=@org AND establishment_id=@unit AND id=@id AND version=@version RETURNING " + ActionColumns, c, tx);
        CustomerScope(update, scope); update.Parameters.AddWithValue("id", actionId); update.Parameters.AddWithValue("version", input.ExpectedVersion); update.Parameters.AddWithValue("result", input.Result); update.Parameters.AddWithValue("actor", scope.Subject);
        NegotiationAction after; await using (var r = await update.ExecuteReaderAsync(token)) { if (!await r.ReadAsync(token)) return Outcome<NegotiationActionCompletionResult>.Fail("NegotiationAction.VersionConflict"); after = ReadAction(r); }
        await ActionEvent(c, tx, scope, after, "Concluída", input.RequestId, token); await Audit(c, tx, scope, after.Id, "NegotiationActionCompleted", token); await tx.CommitAsync(token);
        return new(new(after, appointmentAfter));
    }
}
