using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using RealEstate.Domain;

namespace RealEstate.Infrastructure;

public sealed partial class PostgresPropertyStore : INegotiationActivityTransferStore
{
    public async Task<Outcome<NegotiationActivityTransferReceipt?>> ExistingReceiptAsync(AccessScope scope,Guid requestId,string fingerprint,CancellationToken token)
    { await using var c=await dataSource.OpenConnectionAsync(token);await using var tx=await Begin(c,scope,token);await using var cmd=new NpgsqlCommand("SELECT content_fingerprint,receipt::text,actor FROM real_estate.commercial_negotiation_transfer_requests WHERE organization_id=@org AND establishment_id=@unit AND request_id=@request",c,tx);CustomerScope(cmd,scope);cmd.Parameters.AddWithValue("request",requestId);await using var r=await cmd.ExecuteReaderAsync(token);if(!await r.ReadAsync(token)){await tx.CommitAsync(token);return new((NegotiationActivityTransferReceipt?)null);}var saved=r.GetString(0);var receipt=JsonSerializer.Deserialize<NegotiationActivityTransferReceipt>(r.GetString(1));var actor=r.GetString(2);await r.DisposeAsync();if(saved!=fingerprint||actor!=scope.Subject||receipt is null)return Outcome<NegotiationActivityTransferReceipt?>.Fail("NegotiationTransfer.RequestConflict");await tx.CommitAsync(token);return new(receipt); }
    public async Task<IReadOnlyList<NegotiationActivityTransferCandidate>> EligibleActivitiesAsync(AccessScope scope, Guid negotiationId, CancellationToken token)
    {
        await using var c = await dataSource.OpenConnectionAsync(token); await using var tx = await Begin(c, scope, token);
        var sql = "SELECT " + string.Join(',', "a." + ActionColumns.Replace(",", ",a.")) + ",p." + AppointmentColumns.Replace(",", ",p.") +
            " FROM real_estate.commercial_negotiation_actions a LEFT JOIN real_estate.commercial_appointments p ON p.organization_id=a.organization_id AND p.establishment_id=a.establishment_id AND p.id=a.appointment_id" +
            " WHERE a.organization_id=@org AND a.establishment_id=@unit AND a.negotiation_id=@neg AND a.status='Aberta' AND (a.kind<>'Compromisso' OR (p.id IS NOT NULL AND p.status='Agendado' AND p.responsible_subject=a.responsible_subject)) ORDER BY a.id";
        await using var cmd = new NpgsqlCommand(sql, c, tx); CustomerScope(cmd, scope); cmd.Parameters.AddWithValue("neg", negotiationId);
        var items = new List<NegotiationActivityTransferCandidate>();
        await using (var r = await cmd.ExecuteReaderAsync(token)) while (await r.ReadAsync(token))
        {
            var action = ReadAction(r); CommercialAppointment? appointment = null;
            if (!r.IsDBNull(16)) appointment = ReadAppointmentOffset(r, 16);
            items.Add(new(action, appointment));
        }
        await tx.CommitAsync(token); return items;
    }

    public async Task<IReadOnlyList<CommercialAppointment>> RecipientScheduleAsync(AccessScope scope, string responsibleSubject, DateTimeOffset from, DateTimeOffset until, CancellationToken token)
    {
        await using var c = await dataSource.OpenConnectionAsync(token); await using var tx = await Begin(c, scope, token);
        await using var cmd = new NpgsqlCommand("SELECT " + AppointmentColumns + " FROM real_estate.commercial_appointments WHERE organization_id=@org AND establishment_id=@unit AND responsible_subject=@subject AND status='Agendado' AND starts_at<@until AND ends_at>@from ORDER BY starts_at,ends_at,id", c, tx);
        CustomerScope(cmd, scope); cmd.Parameters.AddWithValue("subject", responsibleSubject); cmd.Parameters.AddWithValue("from", from); cmd.Parameters.AddWithValue("until", until);
        var items = new List<CommercialAppointment>(); await using (var r = await cmd.ExecuteReaderAsync(token)) while (await r.ReadAsync(token)) items.Add(ReadAppointment(r));
        await tx.CommitAsync(token); return items;
    }

    internal static async Task LockAgendaSubject(NpgsqlConnection c,NpgsqlTransaction tx,AccessScope scope,string subject,CancellationToken token)
    { await using var cmd=new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(@key,0))",c,tx);cmd.Parameters.AddWithValue("key",$"agenda:{scope.OrganizationId:N}:{scope.ActiveEstablishmentId:N}:{subject}");await cmd.ExecuteNonQueryAsync(token); }

    public async Task<Outcome<NegotiationActivityTransferReceipt>> ConfirmTransferAsync(AccessScope scope,
        NegotiationActivityTransferSnapshot snapshot, NegotiationAssignee recipient,
        IReadOnlyList<NegotiationActivityTransferDecision> decisions, CancellationToken token)
    {
        await using var c = await dataSource.OpenConnectionAsync(token); await using var tx = await Begin(c, scope, token);
        await using (var advisory = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(@key,0))", c, tx))
        { advisory.Parameters.AddWithValue("key", $"{scope.OrganizationId:N}:{snapshot.EstablishmentId:N}:{snapshot.RequestId:N}"); await advisory.ExecuteNonQueryAsync(token); }
        await using (var replay = new NpgsqlCommand("SELECT content_fingerprint,receipt::text,actor FROM real_estate.commercial_negotiation_transfer_requests WHERE organization_id=@org AND establishment_id=@unit AND request_id=@request", c, tx))
        {
            CustomerScope(replay, scope); replay.Parameters.AddWithValue("request", snapshot.RequestId);
            await using var r = await replay.ExecuteReaderAsync(token);
            if (await r.ReadAsync(token))
            {
                var fingerprint = r.GetString(0); var priorReceipt = JsonSerializer.Deserialize<NegotiationActivityTransferReceipt>(r.GetString(1));var priorActor=r.GetString(2);
                if (fingerprint != snapshot.Fingerprint || priorActor!=scope.Subject || priorReceipt is null) return Outcome<NegotiationActivityTransferReceipt>.Fail("NegotiationTransfer.RequestConflict");
                await tx.CommitAsync(token); return new(priorReceipt);
            }
        }
        Negotiation? negotiation;
        await using (var read = new NpgsqlCommand("SELECT " + NegotiationColumns + " FROM real_estate.commercial_negotiations WHERE organization_id=@org AND establishment_id=@unit AND id=@id FOR UPDATE", c, tx))
        { NegotiationScope(read, scope); read.Parameters.AddWithValue("id", snapshot.NegotiationId); await using var r = await read.ExecuteReaderAsync(token); negotiation = await r.ReadAsync(token) ? ReadNegotiation(r) : null; }
        if (negotiation is null) return Outcome<NegotiationActivityTransferReceipt>.Fail("Resource.NotFound");
        if (negotiation.Version != snapshot.NegotiationVersion || negotiation.ResponsibleSubject == recipient.Subject) return Outcome<NegotiationActivityTransferReceipt>.Fail("NegotiationTransfer.PreviewExpired");

        var selected = snapshot.Selection.Where(x => x.Selected).OrderBy(x => x.ActionId).ToArray();
        var decisionMap = decisions.ToDictionary(x => x.ActionId);
        if (selected.Any(x => !decisionMap.ContainsKey(x.ActionId)) || decisionMap.Count != selected.Length)
            return Outcome<NegotiationActivityTransferReceipt>.Fail("NegotiationTransfer.PreviewExpired");
        await LockAgendaSubject(c,tx,scope,recipient.Subject,token);
        var lockedCandidates = new List<NegotiationActivityTransferCandidate>();
        var actionSql = "SELECT " + string.Join(',', "a." + ActionColumns.Replace(",", ",a.")) + ",p." + AppointmentColumns.Replace(",", ",p.") +
            " FROM real_estate.commercial_negotiation_actions a LEFT JOIN real_estate.commercial_appointments p ON p.organization_id=a.organization_id AND p.establishment_id=a.establishment_id AND p.id=a.appointment_id" +
            " WHERE a.organization_id=@org AND a.establishment_id=@unit AND a.negotiation_id=@neg AND a.status='Aberta' ORDER BY a.id FOR UPDATE OF a";
        await using (var readActions = new NpgsqlCommand(actionSql, c, tx))
        {
            CustomerScope(readActions, scope); readActions.Parameters.AddWithValue("neg", snapshot.NegotiationId);
            await using var r = await readActions.ExecuteReaderAsync(token);
            while (await r.ReadAsync(token))
            {
                var action = ReadAction(r); var appointment = r.IsDBNull(ActionColumns.Split(',').Length) ? null : ReadAppointmentOffset(r, ActionColumns.Split(',').Length);
                lockedCandidates.Add(new(action, appointment));
            }
        }
        if (!NegotiationActivityTransferSnapshotRules.MatchesActivities(snapshot.Selection, lockedCandidates))
            return Outcome<NegotiationActivityTransferReceipt>.Fail("NegotiationTransfer.PreviewExpired");

        var lockedActions = lockedCandidates.ToDictionary(x => x.Action.Id, x => x.Action);
        var selectedAppointments = new Dictionary<Guid, CommercialAppointment>();
        var appointmentIds = selected.Where(x => x.AppointmentVersion.HasValue)
            .Select(x => lockedActions[x.ActionId].AppointmentId!.Value).Order().ToArray();
        if (appointmentIds.Length > 0)
        {
            await using var lockAppointments = new NpgsqlCommand("SELECT " + AppointmentColumns + " FROM real_estate.commercial_appointments WHERE organization_id=@org AND establishment_id=@unit AND id=ANY(@ids) ORDER BY id FOR UPDATE", c, tx);
            CustomerScope(lockAppointments, scope); lockAppointments.Parameters.AddWithValue("ids", appointmentIds);
            await using var r = await lockAppointments.ExecuteReaderAsync(token);
            while (await r.ReadAsync(token))
            {
                var appointment = ReadAppointment(r); selectedAppointments.Add(appointment.Id, appointment);
            }
        }
        foreach (var item in selected.Where(x => x.AppointmentVersion.HasValue))
        {
            var action = lockedActions[item.ActionId];
            if (!selectedAppointments.TryGetValue(action.AppointmentId!.Value, out var appointment) ||
                appointment.Version != item.AppointmentVersion || appointment.Status != "Agendado" ||
                appointment.ResponsibleSubject != action.ResponsibleSubject)
                return Outcome<NegotiationActivityTransferReceipt>.Fail("NegotiationTransfer.PreviewExpired");
        }
        var appointmentTimes=new List<(Guid Id,DateTimeOffset Start,DateTimeOffset End)>();
        foreach(var item in selected.Where(x=>x.AppointmentVersion.HasValue))
        { var appointment=selectedAppointments[lockedActions[item.ActionId].AppointmentId!.Value];appointmentTimes.Add((appointment.Id,appointment.StartsAt,appointment.EndsAt)); }
        if(appointmentTimes.Count>0)
        {
            var from=appointmentTimes.Min(x=>x.Start);var until=appointmentTimes.Max(x=>x.End);
            await using var q=new NpgsqlCommand("SELECT "+AppointmentColumns+" FROM real_estate.commercial_appointments WHERE organization_id=@org AND establishment_id=@unit AND responsible_subject=@subject AND status='Agendado' AND starts_at<@until AND ends_at>@from ORDER BY starts_at,ends_at,id FOR UPDATE",c,tx);CustomerScope(q,scope);q.Parameters.AddWithValue("subject",recipient.Subject);q.Parameters.AddWithValue("from",from);q.Parameters.AddWithValue("until",until);var observed=new List<NegotiationActivityTransferObservedAppointment>();await using(var rr=await q.ExecuteReaderAsync(token))while(await rr.ReadAsync(token)){var a=ReadAppointment(rr);if(appointmentTimes.All(x=>x.Id!=a.Id))observed.Add(new(a.Id,a.Version,a.StartsAt,a.EndsAt,a.Status,a.ResponsibleSubject));}
            var expected=snapshot.Schedule.OrderBy(x=>x.StartsAt.UtcTicks).ThenBy(x=>x.EndsAt.UtcTicks).ThenBy(x=>x.Id).ToArray();var actual=observed.OrderBy(x=>x.StartsAt.UtcTicks).ThenBy(x=>x.EndsAt.UtcTicks).ThenBy(x=>x.Id).ToArray();
            if(!expected.SequenceEqual(actual))return Outcome<NegotiationActivityTransferReceipt>.Fail("NegotiationTransfer.PreviewExpired");
        }
        var transferred = 0; var preserved = 0; var followupCount = 0;
        foreach (var item in selected)
        {
            var action = lockedActions[item.ActionId];
            var decision = decisionMap[item.ActionId];
            if (decision.WillTransfer)
            {
                var next = checked(action.Version + 1);
                await using (var update = new NpgsqlCommand("UPDATE real_estate.commercial_negotiation_actions SET responsible_subject=@subject,responsible_name=@name,version=@next,updated_by=@actor,updated_at=clock_timestamp() WHERE organization_id=@org AND establishment_id=@unit AND id=@id AND version=@expected", c, tx))
                { CustomerScope(update, scope); update.Parameters.AddWithValue("id", action.Id); update.Parameters.AddWithValue("subject", recipient.Subject); update.Parameters.AddWithValue("name", recipient.DisplayName); update.Parameters.AddWithValue("next", next); update.Parameters.AddWithValue("expected", action.Version); update.Parameters.AddWithValue("actor", scope.Subject); if (await update.ExecuteNonQueryAsync(token) != 1) return Outcome<NegotiationActivityTransferReceipt>.Fail("NegotiationTransfer.PreviewExpired"); }
                await using (var ev = new NpgsqlCommand("INSERT INTO real_estate.commercial_negotiation_action_events(organization_id,establishment_id,id,action_id,version,request_id,event,actor,responsible_subject,previous_responsible_subject,description,due_at,result) VALUES(@org,@unit,@id,@action,@version,@request,'ResponsibilityTransferred',@actor,@subject,@previous,@description,@due,NULL)", c, tx))
                { CustomerScope(ev, scope); ev.Parameters.AddWithValue("id", Guid.NewGuid()); ev.Parameters.AddWithValue("action", action.Id); ev.Parameters.AddWithValue("version", next); ev.Parameters.AddWithValue("request", snapshot.RequestId); ev.Parameters.AddWithValue("actor", scope.Subject); ev.Parameters.AddWithValue("subject", recipient.Subject); ev.Parameters.AddWithValue("previous", action.ResponsibleSubject); ev.Parameters.AddWithValue("description", action.Description); ev.Parameters.Add("due", NpgsqlDbType.TimestampTz).Value = (object?)action.DueAt ?? DBNull.Value; await ev.ExecuteNonQueryAsync(token); }
                if (action.AppointmentId is { } appointmentId)
                {
                    if (!selectedAppointments.TryGetValue(appointmentId, out var appointment)) return Outcome<NegotiationActivityTransferReceipt>.Fail("NegotiationTransfer.PreviewExpired");
                    var nextAppointment = checked(appointment.Version + 1);
                    await using (var update = new NpgsqlCommand("UPDATE real_estate.commercial_appointments SET responsible_subject=@subject,responsible_name=@name,version=@next,updated_by=@actor,updated_at=clock_timestamp() WHERE organization_id=@org AND establishment_id=@unit AND id=@id AND version=@expected", c, tx))
                    { CustomerScope(update, scope); update.Parameters.AddWithValue("id", appointment.Id); update.Parameters.AddWithValue("subject", recipient.Subject); update.Parameters.AddWithValue("name", recipient.DisplayName); update.Parameters.AddWithValue("next", nextAppointment); update.Parameters.AddWithValue("expected", appointment.Version); update.Parameters.AddWithValue("actor", scope.Subject); if (await update.ExecuteNonQueryAsync(token) != 1) return Outcome<NegotiationActivityTransferReceipt>.Fail("NegotiationTransfer.PreviewExpired"); }
                    await using var ev = new NpgsqlCommand("INSERT INTO real_estate.commercial_appointment_events(organization_id,establishment_id,id,appointment_id,version,action,actor,responsible_subject,previous_responsible_subject,starts_at,ends_at,result) VALUES(@org,@unit,@id,@appointment,@version,'ResponsibilityTransferred',@actor,@subject,@previous,@start,@end,@result)", c, tx);
                    CustomerScope(ev, scope); ev.Parameters.AddWithValue("id", Guid.NewGuid()); ev.Parameters.AddWithValue("appointment", appointment.Id); ev.Parameters.AddWithValue("version", nextAppointment); ev.Parameters.AddWithValue("actor", scope.Subject); ev.Parameters.AddWithValue("subject", recipient.Subject); ev.Parameters.AddWithValue("previous", appointment.ResponsibleSubject); ev.Parameters.AddWithValue("start", appointment.StartsAt); ev.Parameters.AddWithValue("end", appointment.EndsAt); ev.Parameters.Add("result", NpgsqlDbType.Text).Value = (object?)appointment.Result ?? DBNull.Value; await ev.ExecuteNonQueryAsync(token);
                    await Audit(c, tx, scope, appointment.Id, "CommercialAppointmentResponsibilityTransferred", token);
                }
                await Audit(c, tx, scope, action.Id, "NegotiationActionResponsibilityTransferred", token); transferred++;
            }
            else
            {
                preserved++;
                var followupId=Guid.NewGuid();
                await using var add = new NpgsqlCommand("INSERT INTO real_estate.commercial_negotiation_transfer_followups(organization_id,establishment_id,id,negotiation_id,action_id,appointment_id,reason,responsible_subject,original_responsible_subject,created_by,updated_by) VALUES(@org,@unit,@id,@neg,@action,@appointment,@reason,@responsible,@original,@actor,@actor) ON CONFLICT (organization_id,establishment_id,action_id) WHERE status='Pendente de tratamento' DO NOTHING RETURNING id", c, tx);
                CustomerScope(add, scope); add.Parameters.AddWithValue("id", followupId); add.Parameters.AddWithValue("neg", snapshot.NegotiationId); add.Parameters.AddWithValue("action", action.Id); add.Parameters.Add("appointment", NpgsqlDbType.Uuid).Value = (object?)action.AppointmentId ?? DBNull.Value; add.Parameters.AddWithValue("reason", decision.ConflictReason ?? "ExistingAppointment"); add.Parameters.AddWithValue("responsible", snapshot.FollowupResponsibleSubject); add.Parameters.AddWithValue("original", action.ResponsibleSubject); add.Parameters.AddWithValue("actor", scope.Subject);
                var inserted = await add.ExecuteScalarAsync(token);
                var created = NegotiationActivityFollowupRules.CreatedOrPending(inserted is Guid createdId ? createdId : null);
                if (!created.IsSuccess) return Outcome<NegotiationActivityTransferReceipt>.Fail(created.Code!);
                await using var ev=new NpgsqlCommand("INSERT INTO real_estate.commercial_negotiation_transfer_followup_events(organization_id,establishment_id,id,followup_id,version,request_id,event,actor,responsible_subject) VALUES(@org,@unit,@id,@followup,1,@request,'Criado',@actor,@responsible)",c,tx);CustomerScope(ev,scope);ev.Parameters.AddWithValue("id",Guid.NewGuid());ev.Parameters.AddWithValue("followup",created.Value);ev.Parameters.AddWithValue("request",snapshot.RequestId);ev.Parameters.AddWithValue("actor",scope.Subject);ev.Parameters.AddWithValue("responsible",snapshot.FollowupResponsibleSubject);await ev.ExecuteNonQueryAsync(token);await Audit(c,tx,scope,created.Value,"NegotiationTransferFollowupCreated",token);followupCount++;
            }
        }
        var oldSubject = negotiation.ResponsibleSubject; var nextVersionNegotiation = checked(negotiation.Version + 1);
        await using (var update = new NpgsqlCommand("UPDATE real_estate.commercial_negotiations SET responsible_subject=@subject,responsible_membership_id=@membership,version=@next WHERE organization_id=@org AND establishment_id=@unit AND id=@id AND version=@expected", c, tx))
        { NegotiationScope(update, scope); update.Parameters.AddWithValue("id", negotiation.Id); update.Parameters.AddWithValue("subject", recipient.Subject); update.Parameters.AddWithValue("membership", recipient.MembershipId); update.Parameters.AddWithValue("next", nextVersionNegotiation); update.Parameters.AddWithValue("expected", negotiation.Version); if (await update.ExecuteNonQueryAsync(token) != 1) return Outcome<NegotiationActivityTransferReceipt>.Fail("NegotiationTransfer.PreviewExpired"); }
        await using (var ev = new NpgsqlCommand("INSERT INTO real_estate.commercial_negotiation_events(organization_id,establishment_id,id,negotiation_id,version,action,actor,request_id,previous_responsible_subject,responsible_subject,previous_responsible_membership_id,responsible_membership_id,reason) VALUES(@org,@unit,@id,@neg,@version,'ResponsibilityTransferred',@actor,@request,@previous,@subject,@previousMembership,@membership,@reason)", c, tx))
        { NegotiationScope(ev, scope); ev.Parameters.AddWithValue("id", Guid.NewGuid()); ev.Parameters.AddWithValue("neg", negotiation.Id); ev.Parameters.AddWithValue("version", nextVersionNegotiation); ev.Parameters.AddWithValue("actor", scope.Subject); ev.Parameters.AddWithValue("request", snapshot.RequestId); ev.Parameters.AddWithValue("previous", oldSubject); ev.Parameters.AddWithValue("subject", recipient.Subject); ev.Parameters.AddWithValue("previousMembership", negotiation.ResponsibleMembershipId); ev.Parameters.AddWithValue("membership", recipient.MembershipId); ev.Parameters.Add("reason", NpgsqlDbType.Text).Value = (object?)snapshot.Reason ?? DBNull.Value; await ev.ExecuteNonQueryAsync(token); }
        await Audit(c, tx, scope, negotiation.Id, "CommercialNegotiationResponsibilityTransferred", token);
        var receipt = new NegotiationActivityTransferReceipt(snapshot.RequestId, transferred, preserved, followupCount, decisions);
        await using (var save = new NpgsqlCommand("INSERT INTO real_estate.commercial_negotiation_transfer_requests(organization_id,establishment_id,request_id,negotiation_id,content_fingerprint,receipt,actor) VALUES(@org,@unit,@request,@neg,@fingerprint,@receipt::jsonb,@actor)", c, tx))
        { CustomerScope(save, scope); save.Parameters.AddWithValue("request", snapshot.RequestId); save.Parameters.AddWithValue("neg", negotiation.Id); save.Parameters.AddWithValue("fingerprint", snapshot.Fingerprint); save.Parameters.AddWithValue("receipt", JsonSerializer.Serialize(receipt)); save.Parameters.AddWithValue("actor", scope.Subject); await save.ExecuteNonQueryAsync(token); }
        await tx.CommitAsync(token); return new(receipt);
    }

    public async Task<IReadOnlyList<NegotiationActivityTransferFollowup>> FollowupsAsync(AccessScope scope, Guid negotiationId, CancellationToken token)
    {
        await using var c = await dataSource.OpenConnectionAsync(token); await using var tx = await Begin(c, scope, token);
        await using var cmd = new NpgsqlCommand("SELECT f.id,f.negotiation_id,f.action_id,f.appointment_id,f.reason,f.status,f.responsible_subject,'Responsável não identificado',f.original_responsible_subject,f.result,f.created_by,f.created_at,f.version,f.updated_at FROM real_estate.commercial_negotiation_transfer_followups f WHERE f.organization_id=@org AND f.establishment_id=@unit AND f.negotiation_id=@neg ORDER BY f.created_at DESC,f.id", c, tx);
        CustomerScope(cmd, scope); cmd.Parameters.AddWithValue("neg", negotiationId); var rows = new List<NegotiationActivityTransferFollowup>();
        await using (var r = await cmd.ExecuteReaderAsync(token)) while (await r.ReadAsync(token)) rows.Add(new(r.GetGuid(0), r.GetGuid(1), r.GetGuid(2), r.IsDBNull(3) ? null : r.GetGuid(3), r.GetString(4), r.GetString(5), r.GetString(6), r.GetString(7), r.GetString(8), r.IsDBNull(9) ? null : r.GetString(9), r.GetString(10), r.GetFieldValue<DateTimeOffset>(11), r.GetInt64(12), r.GetFieldValue<DateTimeOffset>(13)));
        await tx.CommitAsync(token); return rows;
    }
    public async Task<IReadOnlyList<NegotiationActivityFollowupEvent>> FollowupEventsAsync(AccessScope scope, Guid negotiationId, CancellationToken token)
    {
        await using var c=await dataSource.OpenConnectionAsync(token); await using var tx=await Begin(c,scope,token);
        await using var cmd=new NpgsqlCommand("SELECT e.id,e.followup_id,e.version,e.event,e.actor,e.responsible_subject,e.result,e.request_id,e.at FROM real_estate.commercial_negotiation_transfer_followup_events e JOIN real_estate.commercial_negotiation_transfer_followups f ON f.organization_id=e.organization_id AND f.establishment_id=e.establishment_id AND f.id=e.followup_id WHERE f.organization_id=@org AND f.establishment_id=@unit AND f.negotiation_id=@neg ORDER BY e.at,e.id",c,tx);
        CustomerScope(cmd,scope);cmd.Parameters.AddWithValue("neg",negotiationId);var rows=new List<NegotiationActivityFollowupEvent>();
        await using(var r=await cmd.ExecuteReaderAsync(token))while(await r.ReadAsync(token))rows.Add(new(r.GetGuid(0),r.GetGuid(1),r.GetInt64(2),r.GetString(3),r.GetString(4),r.GetString(5),r.IsDBNull(6)?null:r.GetString(6),r.GetGuid(7),r.GetFieldValue<DateTimeOffset>(8)));
        await tx.CommitAsync(token);return rows;
    }
    public async Task<Outcome<NegotiationActivityTransferFollowup>> ResolveFollowupAsync(AccessScope scope, Guid negotiationId, Guid followupId, NegotiationActivityFollowupResolution resolution, CancellationToken token)
    {
        await using var c=await dataSource.OpenConnectionAsync(token);await using var tx=await Begin(c,scope,token);
        await using (var requestLock = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(@key,0))", c, tx))
        { requestLock.Parameters.AddWithValue("key", $"followup-resolution:{scope.OrganizationId:N}:{scope.ActiveEstablishmentId:N}:{followupId:N}:{resolution.RequestId:N}"); await requestLock.ExecuteNonQueryAsync(token); }
        await using(var prior=new NpgsqlCommand("SELECT actor,version,result FROM real_estate.commercial_negotiation_transfer_followup_events WHERE organization_id=@org AND establishment_id=@unit AND followup_id=@id AND request_id=@request AND event='Resolvido'",c,tx))
        {
            CustomerScope(prior,scope);prior.Parameters.AddWithValue("id",followupId);prior.Parameters.AddWithValue("request",resolution.RequestId);
            await using var r=await prior.ExecuteReaderAsync(token);
            if(await r.ReadAsync(token))
            {
                var replayRecord=new NegotiationActivityFollowupResolutionReplay(resolution.RequestId,r.GetString(0),r.GetInt64(1),r.GetString(2));
                if(!NegotiationActivityFollowupResolutionRules.MatchesReplay(replayRecord,resolution.RequestId,scope.Subject,resolution.ExpectedVersion,resolution.Result))
                    return Outcome<NegotiationActivityTransferFollowup>.Fail("NegotiationTransfer.RequestConflict");
                await r.DisposeAsync();var replay=await ReadFollowup(c,tx,scope,negotiationId,followupId,token);
                if(replay is null)return Outcome<NegotiationActivityTransferFollowup>.Fail("Resource.NotFound");
                await tx.CommitAsync(token);return new(replay);
            }
        }
        var before=await ReadFollowup(c,tx,scope,negotiationId,followupId,token,true);if(before is null)return Outcome<NegotiationActivityTransferFollowup>.Fail("Resource.NotFound");
        if(before.Status!="Pendente de tratamento")return Outcome<NegotiationActivityTransferFollowup>.Fail("NegotiationTransfer.FollowupAlreadyResolved");
        if(before.Version!=resolution.ExpectedVersion)return Outcome<NegotiationActivityTransferFollowup>.Fail("NegotiationTransfer.VersionConflict");
        var next=checked(before.Version+1);
        await using(var update=new NpgsqlCommand("UPDATE real_estate.commercial_negotiation_transfer_followups SET status='Resolvido',result=@result,version=@next,updated_by=@actor,updated_at=clock_timestamp() WHERE organization_id=@org AND establishment_id=@unit AND id=@id AND version=@expected",c,tx))
        {CustomerScope(update,scope);update.Parameters.AddWithValue("id",followupId);update.Parameters.AddWithValue("result",resolution.Result);update.Parameters.AddWithValue("next",next);update.Parameters.AddWithValue("expected",resolution.ExpectedVersion);update.Parameters.AddWithValue("actor",scope.Subject);if(await update.ExecuteNonQueryAsync(token)!=1)return Outcome<NegotiationActivityTransferFollowup>.Fail("NegotiationTransfer.VersionConflict");}
        await using(var ev=new NpgsqlCommand("INSERT INTO real_estate.commercial_negotiation_transfer_followup_events(organization_id,establishment_id,id,followup_id,version,request_id,event,actor,responsible_subject,result) VALUES(@org,@unit,@id,@followup,@version,@request,'Resolvido',@actor,@responsible,@result)",c,tx))
        {CustomerScope(ev,scope);ev.Parameters.AddWithValue("id",Guid.NewGuid());ev.Parameters.AddWithValue("followup",followupId);ev.Parameters.AddWithValue("version",next);ev.Parameters.AddWithValue("request",resolution.RequestId);ev.Parameters.AddWithValue("actor",scope.Subject);ev.Parameters.AddWithValue("responsible",before.ResponsibleSubject);ev.Parameters.AddWithValue("result",resolution.Result);await ev.ExecuteNonQueryAsync(token);}
        await Audit(c,tx,scope,followupId,"NegotiationTransferFollowupResolved",token);var after=await ReadFollowup(c,tx,scope,negotiationId,followupId,token);await tx.CommitAsync(token);return after is null?Outcome<NegotiationActivityTransferFollowup>.Fail("Resource.NotFound"):new(after);
    }
    private static async Task<NegotiationActivityTransferFollowup?> ReadFollowup(NpgsqlConnection c,NpgsqlTransaction tx,AccessScope scope,Guid negotiationId,Guid followupId,CancellationToken token,bool forUpdate=false)
    {
        await using var cmd=new NpgsqlCommand("SELECT id,negotiation_id,action_id,appointment_id,reason,status,responsible_subject,'Responsável não identificado',original_responsible_subject,result,created_by,created_at,version,updated_at FROM real_estate.commercial_negotiation_transfer_followups WHERE organization_id=@org AND establishment_id=@unit AND negotiation_id=@neg AND id=@id"+(forUpdate?" FOR UPDATE":""),c,tx);CustomerScope(cmd,scope);cmd.Parameters.AddWithValue("neg",negotiationId);cmd.Parameters.AddWithValue("id",followupId);
        await using var r=await cmd.ExecuteReaderAsync(token);return await r.ReadAsync(token)?new(r.GetGuid(0),r.GetGuid(1),r.GetGuid(2),r.IsDBNull(3)?null:r.GetGuid(3),r.GetString(4),r.GetString(5),r.GetString(6),r.GetString(7),r.GetString(8),r.IsDBNull(9)?null:r.GetString(9),r.GetString(10),r.GetFieldValue<DateTimeOffset>(11),r.GetInt64(12),r.GetFieldValue<DateTimeOffset>(13)):null;
    }

    private static CommercialAppointment ReadAppointmentOffset(NpgsqlDataReader r, int i) => new(r.GetGuid(i), r.IsDBNull(i+1)?null:r.GetGuid(i+1), r.IsDBNull(i+2)?null:r.GetGuid(i+2), r.IsDBNull(i+3)?null:r.GetGuid(i+3), r.GetString(i+4), r.GetString(i+5), r.GetString(i+6), r.GetString(i+7), r.GetFieldValue<DateTimeOffset>(i+8), r.GetFieldValue<DateTimeOffset>(i+9), r.GetString(i+10), r.IsDBNull(i+11)?null:r.GetString(i+11), r.IsDBNull(i+12)?null:r.GetString(i+12), r.GetInt64(i+13), r.GetFieldValue<DateTimeOffset>(i+14));
}
