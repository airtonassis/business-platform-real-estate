using Npgsql;
using NpgsqlTypes;
using RealEstate.Domain;
using System.Text.Json;
namespace RealEstate.Infrastructure;

public sealed partial class PostgresPropertyStore:ICustomerStore
{
    private static readonly JsonSerializerOptions CustomerJson=new(JsonSerializerDefaults.Web);
    private const string CustomerColumns="id,establishment_id,data,version";
    private static Customer ReadCustomer(NpgsqlDataReader r)=>new(r.GetGuid(0),r.GetGuid(1),JsonSerializer.Deserialize<CustomerInput>(r.GetString(2),CustomerJson)!,r.GetInt64(3));
    private static void CustomerScope(NpgsqlCommand cmd,AccessScope s){cmd.Parameters.AddWithValue("org",s.OrganizationId);cmd.Parameters.AddWithValue("unit",s.ActiveEstablishmentId!.Value);}
    private const string CustomerWhere="organization_id=@org AND establishment_id=@unit";
    public async Task<CustomerPage> SearchAsync(AccessScope scope,string query,int page,CancellationToken token)
    {
        await using var c=await dataSource.OpenConnectionAsync(token);await using var tx=await Begin(c,scope,token);
        const string filter=" AND (@q='' OR strpos(lower(name),lower(@q))>0 OR strpos(coalesce(email,''),lower(@q))>0 OR (@digits<>'' AND (strpos(coalesce(phone,''),@digits)>0 OR strpos(coalesce(document,''),@digits)>0)))";
        await using var count=new NpgsqlCommand("SELECT count(*)::int FROM real_estate.customers WHERE "+CustomerWhere+filter,c,tx);CustomerScope(count,scope);count.Parameters.AddWithValue("q",query);count.Parameters.AddWithValue("digits",CustomerRules.Digits(query));
        var total=(int)(await count.ExecuteScalarAsync(token))!;
        await using var cmd=new NpgsqlCommand("SELECT "+CustomerColumns+" FROM real_estate.customers WHERE "+CustomerWhere+filter+" ORDER BY lower(name),id LIMIT 25 OFFSET @offset",c,tx);
        CustomerScope(cmd,scope);cmd.Parameters.AddWithValue("q",query);cmd.Parameters.AddWithValue("digits",CustomerRules.Digits(query));cmd.Parameters.AddWithValue("offset",(page-1)*25);
        var items=new List<Customer>();await using(var r=await cmd.ExecuteReaderAsync(token)){while(await r.ReadAsync(token)){var item=ReadCustomer(r);items.Add(item with{Data=item.Data with{Document=null,Notes=null}});}}
        await tx.CommitAsync(token);return new(items,page,total);
    }
    async Task<CustomerFile?> ICustomerStore.FindAsync(AccessScope scope,Guid id,CancellationToken token)
    {
        await using var c=await dataSource.OpenConnectionAsync(token);await using var tx=await Begin(c,scope,token);
        await using var cmd=new NpgsqlCommand("SELECT "+CustomerColumns+" FROM real_estate.customers WHERE "+CustomerWhere+" AND id=@id",c,tx);CustomerScope(cmd,scope);cmd.Parameters.AddWithValue("id",id);
        Customer customer;await using(var r=await cmd.ExecuteReaderAsync(token)){if(!await r.ReadAsync(token))return null;customer=ReadCustomer(r);}
        var contacts=new List<CustomerContact>();var history=new List<CustomerResponsibility>();
        await using var contact=new NpgsqlCommand("SELECT id,at,kind,description,actor,responsible_subject FROM real_estate.customer_contacts WHERE "+CustomerWhere+" AND customer_id=@id ORDER BY at DESC,id",c,tx);CustomerScope(contact,scope);contact.Parameters.AddWithValue("id",id);
        await using(var r=await contact.ExecuteReaderAsync(token)){while(await r.ReadAsync(token))contacts.Add(new(r.GetGuid(0),r.GetFieldValue<DateTimeOffset>(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5)));}
        await using var changes=new NpgsqlCommand("SELECT at,actor,previous_subject,new_subject FROM real_estate.customer_responsibility_history WHERE "+CustomerWhere+" AND customer_id=@id ORDER BY at DESC,id",c,tx);CustomerScope(changes,scope);changes.Parameters.AddWithValue("id",id);
        await using(var r=await changes.ExecuteReaderAsync(token)){while(await r.ReadAsync(token))history.Add(new(r.GetFieldValue<DateTimeOffset>(0),r.GetString(1),r.GetString(2),r.GetString(3)));}
        await tx.CommitAsync(token);return new(customer,contacts,history);
    }
    public async Task<AppointmentCustomerReference?> FindAppointmentReferenceAsync(AccessScope scope,Guid id,CancellationToken token)
    {
        await using var c=await dataSource.OpenConnectionAsync(token);await using var tx=await Begin(c,scope,token);
        await using var cmd=new NpgsqlCommand("SELECT id,establishment_id,name,responsible_subject FROM real_estate.customers WHERE "+CustomerWhere+" AND id=@id",c,tx);
        CustomerScope(cmd,scope);cmd.Parameters.AddWithValue("id",id);
        AppointmentCustomerReference? customer;await using(var r=await cmd.ExecuteReaderAsync(token))
            customer=await r.ReadAsync(token)?new(r.GetGuid(0),r.GetGuid(1),r.GetString(2),r.GetString(3)):null;
        await tx.CommitAsync(token);return customer;
    }
    public async Task<Outcome<Customer>> SaveAsync(AccessScope scope,CustomerInput input,Guid? id,long version,bool acknowledgeDuplicates,CancellationToken token)
    {
        await using var c=await dataSource.OpenConnectionAsync(token);await using var tx=await Begin(c,scope,token);
        // Serialize duplicate checking and concurrent creates inside the organization.
        await using(var gate=new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(@org::text,2031))",c,tx)){gate.Parameters.AddWithValue("org",scope.OrganizationId);await gate.ExecuteNonQueryAsync(token);}
        string? previous=null;
        if(id.HasValue){await using var current=new NpgsqlCommand("SELECT responsible_subject,version FROM real_estate.customers WHERE "+CustomerWhere+" AND id=@id FOR UPDATE",c,tx);CustomerScope(current,scope);current.Parameters.AddWithValue("id",id.Value);await using var r=await current.ExecuteReaderAsync(token);if(!await r.ReadAsync(token))return Outcome<Customer>.Fail("Resource.NotFound");previous=r.GetString(0);if(r.GetInt64(1)!=version)return Outcome<Customer>.Fail("Customer.VersionConflict");}
        var target=id??Guid.NewGuid();
        if(!acknowledgeDuplicates){
            // Only visible contact matches may be suggested. Names alone never block.
            await using var duplicates=new NpgsqlCommand("SELECT 1 FROM real_estate.customers WHERE "+CustomerWhere+" AND id<>@id AND ((@phone IS NOT NULL AND phone=@phone) OR (@email IS NOT NULL AND email=@email)) LIMIT 1",c,tx);CustomerScope(duplicates,scope);duplicates.Parameters.AddWithValue("id",target);duplicates.Parameters.Add("phone",NpgsqlDbType.Text).Value=(object?)input.Phone??DBNull.Value;duplicates.Parameters.Add("email",NpgsqlDbType.Text).Value=(object?)input.Email??DBNull.Value;
            if(await duplicates.ExecuteScalarAsync(token) is not null)return Outcome<Customer>.Fail("Customer.PossibleDuplicate");
        }
        try{
            var sql=id.HasValue?"UPDATE real_estate.customers SET name=@name,data=@data,phone=@phone,email=@email,document=@document,responsible_subject=@responsible,version=version+1,updated_by=@actor,updated_at=clock_timestamp() WHERE "+CustomerWhere+" AND id=@id":
                "INSERT INTO real_estate.customers(organization_id,establishment_id,id,name,data,phone,email,document,responsible_subject,created_by,updated_by) VALUES(@org,@unit,@id,@name,@data,@phone,@email,@document,@responsible,@actor,@actor)";
            await using var cmd=new NpgsqlCommand(sql+" RETURNING "+CustomerColumns,c,tx);CustomerScope(cmd,scope);cmd.Parameters.AddWithValue("id",target);cmd.Parameters.AddWithValue("name",input.Name);cmd.Parameters.AddWithValue("data",NpgsqlDbType.Jsonb,JsonSerializer.Serialize(input,CustomerJson));
            cmd.Parameters.Add("phone",NpgsqlDbType.Text).Value=(object?)input.Phone??DBNull.Value;cmd.Parameters.Add("email",NpgsqlDbType.Text).Value=(object?)input.Email??DBNull.Value;cmd.Parameters.Add("document",NpgsqlDbType.Text).Value=(object?)input.Document??DBNull.Value;
            cmd.Parameters.AddWithValue("responsible",input.ResponsibleSubject);cmd.Parameters.AddWithValue("actor",scope.Subject);
            Customer saved;await using(var r=await cmd.ExecuteReaderAsync(token)){await r.ReadAsync(token);saved=ReadCustomer(r);}
            if(previous is not null&&previous!=input.ResponsibleSubject){
                await using var change=new NpgsqlCommand("INSERT INTO real_estate.customer_responsibility_history(organization_id,establishment_id,customer_id,id,actor,previous_subject,new_subject) VALUES(@org,@unit,@customer,@id,@actor,@previous,@next)",c,tx);CustomerScope(change,scope);change.Parameters.AddWithValue("customer",target);change.Parameters.AddWithValue("id",Guid.NewGuid());change.Parameters.AddWithValue("actor",scope.Subject);change.Parameters.AddWithValue("previous",previous);change.Parameters.AddWithValue("next",input.ResponsibleSubject);await change.ExecuteNonQueryAsync(token);
            }
            await Audit(c,tx,scope,target,id.HasValue?"CustomerUpdated":"CustomerCreated",token);await tx.CommitAsync(token);return new(saved);
        }catch(PostgresException e) when(e.SqlState=="23505"&&e.ConstraintName=="customer_document_unique"){return Outcome<Customer>.Fail("Customer.DocumentUnavailable");}
    }
    public async Task<Outcome<CustomerContact>> AddContactAsync(AccessScope scope,Guid id,ContactInput input,CancellationToken token)
    {
        await using var c=await dataSource.OpenConnectionAsync(token);await using var tx=await Begin(c,scope,token);
        await using var cmd=new NpgsqlCommand("INSERT INTO real_estate.customer_contacts(organization_id,establishment_id,customer_id,id,kind,description,actor,responsible_subject) SELECT organization_id,establishment_id,id,@contact,@kind,@description,@actor,responsible_subject FROM real_estate.customers WHERE "+CustomerWhere+" AND id=@id FOR UPDATE RETURNING id,at,kind,description,actor,responsible_subject",c,tx);
        CustomerScope(cmd,scope);cmd.Parameters.AddWithValue("id",id);cmd.Parameters.AddWithValue("contact",Guid.NewGuid());cmd.Parameters.AddWithValue("kind",input.Kind);cmd.Parameters.AddWithValue("description",input.Description);cmd.Parameters.AddWithValue("actor",scope.Subject);
        CustomerContact saved;await using(var r=await cmd.ExecuteReaderAsync(token)){if(!await r.ReadAsync(token))return Outcome<CustomerContact>.Fail("Resource.NotFound");saved=new(r.GetGuid(0),r.GetFieldValue<DateTimeOffset>(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5));}
        await Audit(c,tx,scope,id,"CustomerContactCreated",token);await tx.CommitAsync(token);return new(saved);
    }
}
