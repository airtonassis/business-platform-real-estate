using Npgsql;
using NpgsqlTypes;
using RealEstate.Domain;
namespace RealEstate.Infrastructure;

public sealed partial class PostgresPropertyStore:ICommercialInteractionStore,IAgendaReferenceStore
{
    public async Task<bool> CustomerExistsAsync(AccessScope scope,Guid id,CancellationToken token)=>await CommercialExists(scope,id,"customers",token);
    public async Task<bool> PropertyExistsAsync(AccessScope scope,Guid id,CancellationToken token)=>await CommercialExists(scope,id,"properties",token);
    public async Task<bool> IsCaptureResponsibleAsync(AccessScope scope,Guid propertyId,CancellationToken token)
    {
        await using var c=await dataSource.OpenConnectionAsync(token);await using var tx=await Begin(c,scope,token);
        await using var cmd=new NpgsqlCommand("SELECT 1 FROM real_estate.properties WHERE "+CustomerWhere+" AND id=@id AND capture->'acquisition'->>'responsibleSubject'=@subject",c,tx);CustomerScope(cmd,scope);cmd.Parameters.AddWithValue("id",propertyId);cmd.Parameters.AddWithValue("subject",scope.Subject);var allowed=await cmd.ExecuteScalarAsync(token) is not null;await tx.CommitAsync(token);return allowed;
    }
    private async Task<bool> CommercialExists(AccessScope scope,Guid id,string table,CancellationToken token)
    {
        await using var c=await dataSource.OpenConnectionAsync(token);await using var tx=await Begin(c,scope,token);
        // Table is a code-owned constant from the two methods above, never a request field.
        await using var cmd=new NpgsqlCommand("SELECT 1 FROM real_estate."+table+" WHERE "+CustomerWhere+" AND id=@id",c,tx);CustomerScope(cmd,scope);cmd.Parameters.AddWithValue("id",id);
        var exists=await cmd.ExecuteScalarAsync(token) is not null;await tx.CommitAsync(token);return exists;
    }
    private static async Task<IReadOnlyList<CommercialInteraction>> ReadCommercial(NpgsqlConnection c,NpgsqlTransaction tx,AccessScope scope,Guid? customer,Guid? property,Guid[]? ids,CancellationToken token)
    {
        var sql="SELECT i.id,p.id,p.customer_id,p.property_id,i.contact_id,i.at,i.channel,i.status,i.category,i.reason,i.notes,i.actor,i.responsible_subject,coalesce(pr.commercial_code,pr.code,'Código pendente'),pr.title,cu.name FROM real_estate.commercial_interactions i JOIN real_estate.commercial_presentations p ON p.organization_id=i.organization_id AND p.id=i.presentation_id JOIN real_estate.properties pr ON pr.organization_id=p.organization_id AND pr.id=p.property_id JOIN real_estate.customers cu ON cu.organization_id=p.organization_id AND cu.id=p.customer_id WHERE p.organization_id=@org AND p.establishment_id=@unit";
        if(customer.HasValue)sql+=" AND p.customer_id=@customer";if(property.HasValue)sql+=" AND p.property_id=@property";if(ids is not null)sql+=" AND i.id=ANY(@ids)";
        await using var cmd=new NpgsqlCommand(sql+" ORDER BY i.at DESC,i.id DESC",c,tx);CustomerScope(cmd,scope);
        if(customer.HasValue)cmd.Parameters.AddWithValue("customer",customer.Value);if(property.HasValue)cmd.Parameters.AddWithValue("property",property.Value);if(ids is not null)cmd.Parameters.AddWithValue("ids",ids);
        var result=new List<CommercialInteraction>();await using var r=await cmd.ExecuteReaderAsync(token);
        while(await r.ReadAsync(token))result.Add(new(r.GetGuid(0),r.GetGuid(1),r.GetGuid(2),r.GetGuid(3),r.GetGuid(4),r.GetFieldValue<DateTimeOffset>(5),r.GetString(6),r.GetString(7),Text(r,8),Text(r,9),Text(r,10),r.GetString(11),r.GetString(12),r.GetString(13),Text(r,14),r.GetString(15)));
        return result;
    }
    public async Task<IReadOnlyList<CommercialInteraction>> HistoryAsync(AccessScope scope,Guid? customerId,Guid? propertyId,CancellationToken token)
    {
        await using var c=await dataSource.OpenConnectionAsync(token);await using var tx=await Begin(c,scope,token);
        var result=await ReadCommercial(c,tx,scope,customerId,propertyId,null,token);await tx.CommitAsync(token);return result;
    }
    private static async Task<Guid> CommercialContact(NpgsqlConnection c,NpgsqlTransaction tx,AccessScope scope,Guid customer,string responsible,string channel,string description,CancellationToken token)
    {
        var id=Guid.NewGuid();await using var cmd=new NpgsqlCommand("INSERT INTO real_estate.customer_contacts(organization_id,establishment_id,customer_id,id,kind,description,actor,responsible_subject) VALUES(@org,@unit,@customer,@id,@kind,@description,@actor,@responsible)",c,tx);CustomerScope(cmd,scope);
        cmd.Parameters.AddWithValue("customer",customer);cmd.Parameters.AddWithValue("id",id);cmd.Parameters.AddWithValue("kind",CustomerRules.ContactKinds.Contains(channel)?channel:"Outros");cmd.Parameters.AddWithValue("description",description);cmd.Parameters.AddWithValue("actor",scope.Subject);cmd.Parameters.AddWithValue("responsible",responsible);await cmd.ExecuteNonQueryAsync(token);return id;
    }
    private static async Task InsertCommercial(NpgsqlConnection c,NpgsqlTransaction tx,AccessScope scope,Guid id,Guid presentation,Guid contact,string responsible,string channel,string status,string? category,string? reason,string? notes,CancellationToken token)
    {
        await using var cmd=new NpgsqlCommand("INSERT INTO real_estate.commercial_interactions(organization_id,establishment_id,id,presentation_id,contact_id,actor,responsible_subject,channel,status,category,reason,notes) VALUES(@org,@unit,@id,@presentation,@contact,@actor,@responsible,@channel,@status,@category,@reason,@notes)",c,tx);CustomerScope(cmd,scope);
        cmd.Parameters.AddWithValue("id",id);cmd.Parameters.AddWithValue("presentation",presentation);cmd.Parameters.AddWithValue("contact",contact);cmd.Parameters.AddWithValue("actor",scope.Subject);cmd.Parameters.AddWithValue("responsible",responsible);cmd.Parameters.AddWithValue("channel",channel);cmd.Parameters.AddWithValue("status",status);
        cmd.Parameters.Add("category",NpgsqlDbType.Text).Value=(object?)category??DBNull.Value;cmd.Parameters.Add("reason",NpgsqlDbType.Text).Value=(object?)reason??DBNull.Value;cmd.Parameters.Add("notes",NpgsqlDbType.Text).Value=(object?)notes??DBNull.Value;
        await cmd.ExecuteNonQueryAsync(token);
        await Audit(c,tx,scope,id,id==presentation?"CommercialPropertyPresented":"CommercialFeedbackRecorded",token);
    }
    public async Task<Outcome<IReadOnlyList<CommercialInteraction>>> PresentAsync(AccessScope scope,Guid customerId,PresentationInput input,CancellationToken token)
    {
        await using var c=await dataSource.OpenConnectionAsync(token);await using var tx=await Begin(c,scope,token);
        string responsible;
        await using(var customer=new NpgsqlCommand("SELECT responsible_subject FROM real_estate.customers WHERE "+CustomerWhere+" AND id=@id FOR SHARE",c,tx)){CustomerScope(customer,scope);customer.Parameters.AddWithValue("id",customerId);var value=await customer.ExecuteScalarAsync(token);if(value is not string s)return Outcome<IReadOnlyList<CommercialInteraction>>.Fail("Resource.NotFound");responsible=s;}
        await using(var properties=new NpgsqlCommand("SELECT id FROM real_estate.properties WHERE "+CustomerWhere+" AND id=ANY(@ids) AND status='Available' ORDER BY id FOR SHARE",c,tx)){
            CustomerScope(properties,scope);properties.Parameters.AddWithValue("ids",input.PropertyIds);var count=0;await using var r=await properties.ExecuteReaderAsync(token);while(await r.ReadAsync(token))count++;if(count!=input.PropertyIds.Length)return Outcome<IReadOnlyList<CommercialInteraction>>.Fail("Commercial.PropertyUnavailable");
        }
        var contact=await CommercialContact(c,tx,scope,customerId,responsible,input.Channel,$"Apresentação de {input.PropertyIds.Length} imóvel(is).",token);var ids=new List<Guid>();
        foreach(var property in input.PropertyIds){
            var id=Guid.NewGuid();ids.Add(id);await using var cmd=new NpgsqlCommand("INSERT INTO real_estate.commercial_presentations(organization_id,establishment_id,id,customer_id,property_id) VALUES(@org,@unit,@id,@customer,@property)",c,tx);CustomerScope(cmd,scope);cmd.Parameters.AddWithValue("id",id);cmd.Parameters.AddWithValue("customer",customerId);cmd.Parameters.AddWithValue("property",property);await cmd.ExecuteNonQueryAsync(token);
            await InsertCommercial(c,tx,scope,id,id,contact,responsible,input.Channel,CommercialInteractionRules.Presented,null,null,input.Notes,token);
        }
        var result=await ReadCommercial(c,tx,scope,customerId,null,ids.ToArray(),token);await tx.CommitAsync(token);return new(result);
    }
    public async Task<Outcome<CommercialInteraction>> FeedbackAsync(AccessScope scope,Guid customerId,FeedbackInput input,CancellationToken token)
    {
        await using var c=await dataSource.OpenConnectionAsync(token);await using var tx=await Begin(c,scope,token);
        string responsible;
        await using(var root=new NpgsqlCommand("SELECT cu.responsible_subject FROM real_estate.commercial_presentations p JOIN real_estate.customers cu ON cu.organization_id=p.organization_id AND cu.id=p.customer_id JOIN real_estate.properties pr ON pr.organization_id=p.organization_id AND pr.id=p.property_id WHERE p.organization_id=@org AND p.establishment_id=@unit AND p.id=@id AND p.customer_id=@customer FOR SHARE OF cu,pr",c,tx)){
            CustomerScope(root,scope);root.Parameters.AddWithValue("id",input.PresentationId);root.Parameters.AddWithValue("customer",customerId);var value=await root.ExecuteScalarAsync(token);if(value is not string s)return Outcome<CommercialInteraction>.Fail("Resource.NotFound");responsible=s;
        }
        var contact=await CommercialContact(c,tx,scope,customerId,responsible,input.Channel,"Retorno sobre imóvel: "+input.Status+".",token);var id=Guid.NewGuid();
        await InsertCommercial(c,tx,scope,id,input.PresentationId,contact,responsible,input.Channel,input.Status,input.Category,input.Reason,input.Notes,token);
        var result=await ReadCommercial(c,tx,scope,customerId,null,[id],token);await tx.CommitAsync(token);return new(result.Single());
    }
    public async Task<Outcome<CommercialSummary>> SummaryAsync(AccessScope scope,Guid propertyId,DateTimeOffset from,DateTimeOffset until,CancellationToken token)
    {
        await using var c=await dataSource.OpenConnectionAsync(token);await using var tx=await Begin(c,scope,token);
        // Keep the captor/manager relationship valid for the complete read, including a concurrent transfer.
        await using(var authority=new NpgsqlCommand("SELECT 1 FROM real_estate.properties WHERE "+CustomerWhere+" AND id=@id AND (@manager OR capture->'acquisition'->>'responsibleSubject'=@actor) FOR SHARE",c,tx)){
            CustomerScope(authority,scope);authority.Parameters.AddWithValue("id",propertyId);authority.Parameters.AddWithValue("manager",scope.LegacyManager);authority.Parameters.AddWithValue("actor",scope.Subject);
            if(await authority.ExecuteScalarAsync(token) is null)return Outcome<CommercialSummary>.Fail("Access.Forbidden");
        }
        // No names, contacts, documents, free reasons or notes are fetched for an owner summary.
        await using var cmd=new NpgsqlCommand("SELECT p.customer_id,i.id=p.id,i.status,i.category,i.at,i.id FROM real_estate.commercial_interactions i JOIN real_estate.commercial_presentations p ON p.organization_id=i.organization_id AND p.id=i.presentation_id WHERE p.organization_id=@org AND p.establishment_id=@unit AND p.property_id=@property AND i.at>=@from AND i.at<@until ORDER BY i.at DESC,i.id DESC",c,tx);CustomerScope(cmd,scope);cmd.Parameters.AddWithValue("property",propertyId);cmd.Parameters.AddWithValue("from",from);cmd.Parameters.AddWithValue("until",until);
        var people=new HashSet<Guid>();var latest=new HashSet<Guid>();var presented=new HashSet<Guid>();var reasons=new Dictionary<string,int>();var days=new Dictionary<string,int>();var presentations=0;var visits=0;var interested=0;
        await using(var r=await cmd.ExecuteReaderAsync(token)){while(await r.ReadAsync(token)){
            var customer=r.GetGuid(0);people.Add(customer);var status=r.GetString(2);
            if(r.GetBoolean(1)){presentations++;presented.Add(customer);}if(status=="Visitado")visits++;
            if(latest.Add(customer)&&status is "Demonstrou interesse" or "Em negociação")interested++;
            if(status=="Sem interesse"&&!r.IsDBNull(3)){var reason=r.GetString(3);reasons[reason]=reasons.GetValueOrDefault(reason)+1;}
            var day=r.GetFieldValue<DateTimeOffset>(4).UtcDateTime.ToString("yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture);days[day]=days.GetValueOrDefault(day)+1;
        }}
        await tx.CommitAsync(token);return new(new(from,until,presented.Count,presentations,visits,interested,reasons,days));
    }
}
