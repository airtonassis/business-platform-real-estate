using Npgsql;
using RealEstate.Domain;
namespace RealEstate.Infrastructure;
public sealed partial class PostgresPropertyStore:INegotiationStore
{
    private const string NegotiationColumns="id,customer_id,property_id,announcement_id,owner_id,establishment_id,responsible_subject,status,version,created_at,responsible_membership_id";
    private static Negotiation ReadNegotiation(NpgsqlDataReader r)=>new(r.GetGuid(0),r.GetGuid(1),r.GetGuid(2),r.GetGuid(3),r.GetGuid(4),r.GetGuid(5),r.GetString(6),r.GetString(7),r.GetInt64(8),r.GetFieldValue<DateTimeOffset>(9),r.IsDBNull(10)?Guid.Empty:r.GetGuid(10));
    private static void NegotiationScope(NpgsqlCommand cmd,AccessScope scope)
    {cmd.Parameters.AddWithValue("org",scope.OrganizationId);cmd.Parameters.AddWithValue("unit",scope.ActiveEstablishmentId!.Value);}
    public async Task<Outcome<Negotiation>> OpenAsync(AccessScope scope,OpenNegotiation input,string customerResponsibleSubject,NegotiationAssignee assignee,CancellationToken token)
    {
        await using var c=await dataSource.OpenConnectionAsync(token);await using var tx=await Begin(c,scope,token);
        // The insertion predicate checks all links in the same transaction. A rejected link writes neither negotiation nor event.
        var sql="INSERT INTO real_estate.commercial_negotiations(organization_id,establishment_id,id,request_id,customer_id,property_id,announcement_id,owner_id,responsible_subject,responsible_membership_id,created_by) "+
            "SELECT @org,@unit,@id,@request,cu.id,p.id,a.id,p.owner_id,@responsible,@membership,@actor "+
            "FROM real_estate.customers cu JOIN real_estate.properties p ON p.organization_id=cu.organization_id AND p.establishment_id=cu.establishment_id "+
            "JOIN real_estate.property_announcements a ON a.organization_id=p.organization_id AND a.property_id=p.id AND a.establishment_id=p.establishment_id "+
            "JOIN real_estate.owners ow ON ow.organization_id=p.organization_id AND ow.id=p.owner_id AND ow.active "+
            "WHERE cu.organization_id=@org AND cu.establishment_id=@unit AND cu.id=@customer AND cu.responsible_subject=@owner AND p.id=@property AND a.id=@announcement "+
            "AND p.status='Available' AND a.status='Draft' AND a.purpose IN ('Venda','Locação') "+
            "AND length(btrim(cu.responsible_subject))>0 AND NOT EXISTS(SELECT 1 FROM real_estate.commercial_negotiations n WHERE n.organization_id=@org AND n.establishment_id=@unit AND n.request_id=@request) FOR SHARE OF cu,p,a,ow "+
            "ON CONFLICT(organization_id,establishment_id,request_id) DO NOTHING RETURNING "+NegotiationColumns;
        await using var cmd=new NpgsqlCommand(sql,c,tx);NegotiationScope(cmd,scope);
        cmd.Parameters.AddWithValue("id",Guid.NewGuid());cmd.Parameters.AddWithValue("request",input.RequestId);cmd.Parameters.AddWithValue("customer",input.CustomerId);
        cmd.Parameters.AddWithValue("property",input.PropertyId);cmd.Parameters.AddWithValue("announcement",input.AnnouncementId);cmd.Parameters.AddWithValue("actor",scope.Subject);
        cmd.Parameters.AddWithValue("owner",customerResponsibleSubject);cmd.Parameters.AddWithValue("responsible",assignee.Subject);cmd.Parameters.AddWithValue("membership",assignee.MembershipId);
        Negotiation? created;await using(var r=await cmd.ExecuteReaderAsync(token)){created=await r.ReadAsync(token)?ReadNegotiation(r):null;}
        if(created is null)
        {
            await using var prior=new NpgsqlCommand("SELECT "+NegotiationColumns+" FROM real_estate.commercial_negotiations WHERE organization_id=@org AND establishment_id=@unit AND request_id=@request AND created_by=@actor",c,tx);
            NegotiationScope(prior,scope);prior.Parameters.AddWithValue("request",input.RequestId);prior.Parameters.AddWithValue("actor",scope.Subject);
            Negotiation? found;await using(var r=await prior.ExecuteReaderAsync(token)){found=await r.ReadAsync(token)?ReadNegotiation(r):null;}
            if(found is null)return Outcome<Negotiation>.Fail("Negotiation.OfferUnavailable");
            if(found.CustomerId!=input.CustomerId||found.PropertyId!=input.PropertyId||found.AnnouncementId!=input.AnnouncementId||found.ResponsibleSubject!=assignee.Subject||found.ResponsibleMembershipId!=assignee.MembershipId)return Outcome<Negotiation>.Fail("Negotiation.RequestConflict");
            await tx.CommitAsync(token);return new(found);
        }
        await using(var ev=new NpgsqlCommand("INSERT INTO real_estate.commercial_negotiation_events(organization_id,establishment_id,id,negotiation_id,version,action,actor,request_id,responsible_subject,responsible_membership_id) VALUES(@org,@unit,@id,@negotiation,1,'Opened',@actor,@request,@responsible,@membership)",c,tx))
        {NegotiationScope(ev,scope);ev.Parameters.AddWithValue("id",Guid.NewGuid());ev.Parameters.AddWithValue("negotiation",created.Id);ev.Parameters.AddWithValue("actor",scope.Subject);ev.Parameters.AddWithValue("request",input.RequestId);ev.Parameters.AddWithValue("responsible",assignee.Subject);ev.Parameters.AddWithValue("membership",assignee.MembershipId);await ev.ExecuteNonQueryAsync(token);}
        await Audit(c,tx,scope,created.Id,"CommercialNegotiationOpened",token);await tx.CommitAsync(token);return new(created);
    }

    public async Task<Outcome<Negotiation>> TransferAsync(AccessScope scope,Guid negotiationId,NegotiationTransfer input,NegotiationAssignee assignee,bool recovery,CancellationToken token)
    {
        await using var c=await dataSource.OpenConnectionAsync(token);await using var tx=await Begin(c,scope,token);
        var existingSql="SELECT version,responsible_subject,responsible_membership_id FROM real_estate.commercial_negotiation_events WHERE organization_id=@org AND establishment_id=@unit AND negotiation_id=@id AND request_id=@request";
        await using(var prior=new NpgsqlCommand(existingSql,c,tx))
        {
            NegotiationScope(prior,scope);prior.Parameters.AddWithValue("id",negotiationId);prior.Parameters.AddWithValue("request",input.RequestId);
            await using var r=await prior.ExecuteReaderAsync(token);
            if(await r.ReadAsync(token))
            {
                var version=r.GetInt64(0);var subject=r.GetString(1);var member=r.GetGuid(2);await r.DisposeAsync();
                if(subject!=assignee.Subject||member!=assignee.MembershipId)return Outcome<Negotiation>.Fail("Negotiation.RequestConflict");
                await using var current=new NpgsqlCommand("SELECT "+NegotiationColumns+" FROM real_estate.commercial_negotiations WHERE organization_id=@org AND establishment_id=@unit AND id=@id",c,tx);
                NegotiationScope(current,scope);current.Parameters.AddWithValue("id",negotiationId);
                Negotiation? item;await using(var data=await current.ExecuteReaderAsync(token)){item=await data.ReadAsync(token)?ReadNegotiation(data):null;}
                if(item is null)return Outcome<Negotiation>.Fail("Resource.NotFound");
                await tx.CommitAsync(token);return new(item with{ResponsibleSubject=subject,ResponsibleMembershipId=member,Version=version});
            }
        }
        Negotiation? before;
        await using(var read=new NpgsqlCommand("SELECT "+NegotiationColumns+" FROM real_estate.commercial_negotiations WHERE organization_id=@org AND establishment_id=@unit AND id=@id FOR UPDATE",c,tx))
        {NegotiationScope(read,scope);read.Parameters.AddWithValue("id",negotiationId);await using var r=await read.ExecuteReaderAsync(token);before=await r.ReadAsync(token)?ReadNegotiation(r):null;}
        if(before is null)return Outcome<Negotiation>.Fail("Resource.NotFound");
        if(before.Version!=input.ExpectedVersion)return Outcome<Negotiation>.Fail("Negotiation.VersionConflict");
        if(before.ResponsibleMembershipId==Guid.Empty)return Outcome<Negotiation>.Fail("Negotiation.ResponsibleHistoryUnavailable");
        if(before.ResponsibleMembershipId==assignee.MembershipId)return Outcome<Negotiation>.Fail("Negotiation.SameResponsible");
        var nextVersion=checked(before.Version+1);var now=DateTimeOffset.UtcNow;
        await using(var update=new NpgsqlCommand("UPDATE real_estate.commercial_negotiations SET responsible_subject=@subject,responsible_membership_id=@membership,version=@next WHERE organization_id=@org AND establishment_id=@unit AND id=@id AND version=@expected",c,tx))
        {NegotiationScope(update,scope);update.Parameters.AddWithValue("id",negotiationId);update.Parameters.AddWithValue("subject",assignee.Subject);update.Parameters.AddWithValue("membership",assignee.MembershipId);update.Parameters.AddWithValue("next",nextVersion);update.Parameters.AddWithValue("expected",input.ExpectedVersion);if(await update.ExecuteNonQueryAsync(token)!=1)return Outcome<Negotiation>.Fail("Negotiation.VersionConflict");}
        var action=recovery?"ResponsibilityRecovered":"ResponsibilityTransferred";
        await using(var ev=new NpgsqlCommand("INSERT INTO real_estate.commercial_negotiation_events(organization_id,establishment_id,id,negotiation_id,version,action,actor,request_id,previous_responsible_subject,responsible_subject,previous_responsible_membership_id,responsible_membership_id,reason) VALUES(@org,@unit,@event,@id,@version,@action,@actor,@request,@previous,@subject,@previousMembership,@membership,@reason)",c,tx))
        {NegotiationScope(ev,scope);ev.Parameters.AddWithValue("event",Guid.NewGuid());ev.Parameters.AddWithValue("id",negotiationId);ev.Parameters.AddWithValue("version",nextVersion);ev.Parameters.AddWithValue("action",action);ev.Parameters.AddWithValue("actor",scope.Subject);ev.Parameters.AddWithValue("request",input.RequestId);ev.Parameters.AddWithValue("previous",before.ResponsibleSubject);ev.Parameters.AddWithValue("subject",assignee.Subject);ev.Parameters.AddWithValue("previousMembership",before.ResponsibleMembershipId);ev.Parameters.AddWithValue("membership",assignee.MembershipId);ev.Parameters.AddWithValue("reason",(object?)input.Reason?.Trim()??DBNull.Value);await ev.ExecuteNonQueryAsync(token);}
        await Audit(c,tx,scope,negotiationId,action,token);await tx.CommitAsync(token);
        return new(before with{ResponsibleSubject=assignee.Subject,ResponsibleMembershipId=assignee.MembershipId,Version=nextVersion,CreatedAt=before.CreatedAt});
    }
    public async Task<Negotiation?> FindNegotiationAsync(AccessScope scope,Guid id,CancellationToken token)
    {
        await using var c=await dataSource.OpenConnectionAsync(token);await using var tx=await Begin(c,scope,token);
        await using var cmd=new NpgsqlCommand("SELECT "+NegotiationColumns+" FROM real_estate.commercial_negotiations WHERE organization_id=@org AND establishment_id=@unit AND id=@id",c,tx);
        NegotiationScope(cmd,scope);cmd.Parameters.AddWithValue("id",id);
        Negotiation? item;await using(var r=await cmd.ExecuteReaderAsync(token)){item=await r.ReadAsync(token)?ReadNegotiation(r):null;}
        await tx.CommitAsync(token);return item;
    }
    public async Task<NegotiationPage> ListAsync(AccessScope scope,Guid? customerId,int page,CancellationToken token)
    {
        await using var c=await dataSource.OpenConnectionAsync(token);await using var tx=await Begin(c,scope,token);
        var where="organization_id=@org AND establishment_id=@unit"+(customerId.HasValue?" AND customer_id=@customer":"");
        await using var cmd=new NpgsqlCommand("SELECT "+NegotiationColumns+",count(*) OVER() FROM real_estate.commercial_negotiations WHERE "+where+" ORDER BY created_at DESC,id DESC LIMIT 25 OFFSET @offset",c,tx);
        NegotiationScope(cmd,scope);if(customerId.HasValue)cmd.Parameters.AddWithValue("customer",customerId.Value);cmd.Parameters.AddWithValue("offset",(page-1)*25);
        var items=new List<Negotiation>();long total=0;await using(var r=await cmd.ExecuteReaderAsync(token)){while(await r.ReadAsync(token)){items.Add(ReadNegotiation(r));total=r.GetInt64(11);}}
        if(items.Count==0){await using var count=new NpgsqlCommand("SELECT count(*) FROM real_estate.commercial_negotiations WHERE "+where,c,tx);NegotiationScope(count,scope);if(customerId.HasValue)count.Parameters.AddWithValue("customer",customerId.Value);total=(long)(await count.ExecuteScalarAsync(token))!;}
        await tx.CommitAsync(token);return new(items,total,page);
    }
    public async Task<IReadOnlyList<NegotiationEvent>> NegotiationEventsAsync(AccessScope scope,Guid id,CancellationToken token)
    {
        await using var c=await dataSource.OpenConnectionAsync(token);await using var tx=await Begin(c,scope,token);
        await using var cmd=new NpgsqlCommand("SELECT id,negotiation_id,version,action,actor,at,previous_responsible_subject,responsible_subject,reason,previous_responsible_membership_id,responsible_membership_id FROM real_estate.commercial_negotiation_events WHERE organization_id=@org AND establishment_id=@unit AND negotiation_id=@id ORDER BY version",c,tx);
        NegotiationScope(cmd,scope);cmd.Parameters.AddWithValue("id",id);
        var events=new List<NegotiationEvent>();await using(var r=await cmd.ExecuteReaderAsync(token)){while(await r.ReadAsync(token))events.Add(new(r.GetGuid(0),r.GetGuid(1),r.GetInt64(2),r.GetString(3),r.GetString(4),r.GetFieldValue<DateTimeOffset>(5),r.IsDBNull(6)?null:r.GetString(6),r.IsDBNull(7)?null:r.GetString(7),r.IsDBNull(8)?null:r.GetString(8),r.IsDBNull(9)?null:r.GetGuid(9),r.IsDBNull(10)?null:r.GetGuid(10)));}
        await tx.CommitAsync(token);return events;
    }
}
