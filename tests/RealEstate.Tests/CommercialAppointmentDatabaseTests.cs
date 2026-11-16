using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using RealEstate.Application;
using RealEstate.Domain;
using RealEstate.Infrastructure;
using Xunit;

namespace RealEstate.Tests;

// Run only with a disposable PostgreSQL supplied by s05_postgres_gate.py.
public sealed class CommercialAppointmentDatabaseTests
{
    private sealed class Access(AccessScope scope):ICoreAccess
    {
        public Task<Outcome<AccessScope>> RequireAsync(string resource,string permission,CancellationToken token)
            =>Task.FromResult(new Outcome<AccessScope>(scope));
    }

    private static async Task InsertCustomerAsync(NpgsqlConnection connection,Guid organization,Guid unit,
        Guid id,string name,string responsible)
    {
        var input=new CustomerInput(name,"Física","11999998888",null,"Site","Compra",responsible,
            ResponsibleName:"Atendente sintético");
        await using var command=new NpgsqlCommand(
            "INSERT INTO real_estate.customers(organization_id,establishment_id,id,name,data,phone,responsible_subject,created_by,updated_by) VALUES(@org,@unit,@id,@name,@data,@phone,@responsible,'test','test')",
            connection);
        command.Parameters.AddWithValue("org",organization);command.Parameters.AddWithValue("unit",unit);
        command.Parameters.AddWithValue("id",id);command.Parameters.AddWithValue("name",input.Name);
        command.Parameters.Add("data",NpgsqlDbType.Jsonb).Value=JsonSerializer.Serialize(input);
        command.Parameters.AddWithValue("phone",input.Phone!);command.Parameters.AddWithValue("responsible",responsible);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task AssertDeniedAsync(NpgsqlConnection connection,string sql)
    {
        await using var command=new NpgsqlCommand(sql,connection);
        var error=await Assert.ThrowsAsync<PostgresException>(()=>command.ExecuteNonQueryAsync());
        Assert.Equal("42501",error.SqlState);
    }

    private static async Task InsertPropertyAsync(NpgsqlConnection connection,Guid organization,Guid unit,Guid id,Guid owner,string status="Available")
    {
        var code="IM-"+id.ToString("N");
        await using var tx=await connection.BeginTransactionAsync();
        await using(var context=new NpgsqlCommand("SELECT set_config('erp.organization_id',@org,true),set_config('erp.establishments',@units,true),set_config('erp.active_establishment',@unit,true)",connection,tx))
        {context.Parameters.AddWithValue("org",organization.ToString());context.Parameters.AddWithValue("units","{"+unit+"}");context.Parameters.AddWithValue("unit",unit.ToString());await context.ExecuteNonQueryAsync();}
        await using(var insert=new NpgsqlCommand("INSERT INTO real_estate.properties(organization_id,id,owner_id,establishment_id,code,title,category,property_type,purpose,address,postal_code,latitude,longitude,price,status,created_by,updated_by) VALUES(@org,@id,@owner,@unit,@code,'Imóvel sintético','Residencial','Empreendimento','Venda','Rua de teste','01001000',-23.55,-46.63,100000,'Draft','test','test')",connection,tx))
        {
            insert.Parameters.AddWithValue("org",organization);insert.Parameters.AddWithValue("id",id);insert.Parameters.AddWithValue("owner",owner);insert.Parameters.AddWithValue("unit",unit);insert.Parameters.AddWithValue("code",code);
            await insert.ExecuteNonQueryAsync();
        }
        if(status!="Draft")
        {
            await using(var flag=new NpgsqlCommand("SELECT set_config('erp.property_transition','submit',true)",connection,tx))await flag.ExecuteNonQueryAsync();
            await using var submit=new NpgsqlCommand("UPDATE real_estate.properties SET status='InReview',version=version+1,updated_by='test',updated_at=clock_timestamp() WHERE organization_id=@org AND establishment_id=@unit AND id=@id",connection,tx);
            submit.Parameters.AddWithValue("org",organization);submit.Parameters.AddWithValue("unit",unit);submit.Parameters.AddWithValue("id",id);await submit.ExecuteNonQueryAsync();
        }
        if(status=="Available")
        {
            await using(var flag=new NpgsqlCommand("SELECT set_config('erp.property_transition','approve',true)",connection,tx))await flag.ExecuteNonQueryAsync();
            await using var approve=new NpgsqlCommand("UPDATE real_estate.properties SET status='Available',version=version+1,updated_by='test',updated_at=clock_timestamp() WHERE organization_id=@org AND establishment_id=@unit AND id=@id",connection,tx);
            approve.Parameters.AddWithValue("org",organization);approve.Parameters.AddWithValue("unit",unit);approve.Parameters.AddWithValue("id",id);await approve.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();
    }

    [Fact]
    public async Task Agenda_uses_column_scoped_customer_reference_and_preserves_customer_and_appointment_isolation()
    {
        var runtime=Environment.GetEnvironmentVariable("AppointmentTestRuntimeConnection");
        var admin=Environment.GetEnvironmentVariable("AppointmentTestAdminConnection");
        Assert.False(string.IsNullOrWhiteSpace(runtime),"The gate must supply the real erp_runtime login.");
        Assert.False(string.IsNullOrWhiteSpace(admin),"The gate must supply the disposable database admin login.");

        var organization=Guid.NewGuid();var unit=Guid.NewGuid();var otherUnit=Guid.NewGuid();
        var otherOrganization=Guid.NewGuid();var customerId=Guid.NewGuid();var otherUnitCustomerId=Guid.NewGuid();
        var otherOrganizationCustomerId=Guid.NewGuid();var ownerId=Guid.NewGuid();var inactiveOwnerId=Guid.NewGuid();var otherOrganizationOwnerId=Guid.NewGuid();
        var propertyId=Guid.NewGuid();var otherUnitPropertyId=Guid.NewGuid();var draftPropertyId=Guid.NewGuid();
        await using(var adminSource=NpgsqlDataSource.Create(admin!))
        {
            await using var connection=await adminSource.OpenConnectionAsync();
            await InsertCustomerAsync(connection,organization,unit,customerId,"Cliente autorizado","test-only-actor");
            await InsertCustomerAsync(connection,organization,otherUnit,otherUnitCustomerId,"Cliente outra filial","other-unit-actor");
            await InsertCustomerAsync(connection,otherOrganization,unit,otherOrganizationCustomerId,"Cliente outra organização","other-org-actor");
            await using var owner=new NpgsqlCommand(
                "INSERT INTO real_estate.owners(organization_id,id,name,created_by) VALUES(@org,@id,'Proprietário sintético','test')",connection);
            owner.Parameters.AddWithValue("org",organization);owner.Parameters.AddWithValue("id",ownerId);
            await owner.ExecuteNonQueryAsync();
            await using(var inactive=new NpgsqlCommand("INSERT INTO real_estate.owners(organization_id,id,name,active,created_by) VALUES(@org,@id,'Proprietário inativo',false,'test')",connection))
            {inactive.Parameters.AddWithValue("org",organization);inactive.Parameters.AddWithValue("id",inactiveOwnerId);await inactive.ExecuteNonQueryAsync();}
            await using(var foreign=new NpgsqlCommand("INSERT INTO real_estate.owners(organization_id,id,name,created_by) VALUES(@org,@id,'Proprietário de outra organização','test')",connection))
            {foreign.Parameters.AddWithValue("org",otherOrganization);foreign.Parameters.AddWithValue("id",otherOrganizationOwnerId);await foreign.ExecuteNonQueryAsync();}
            await InsertPropertyAsync(connection,organization,unit,propertyId,ownerId);
            await InsertPropertyAsync(connection,organization,otherUnit,otherUnitPropertyId,ownerId);
            await InsertPropertyAsync(connection,organization,unit,draftPropertyId,ownerId,"Draft");
        }

        await using var runtimeSource=NpgsqlDataSource.Create(runtime!);
        var store=new PostgresPropertyStore(runtimeSource);
        var scope=new AccessScope(organization,"test-only-actor","Teste",[new(unit,"Unidade teste")],ActiveEstablishmentId:unit);

        // T4/T5: only the projection columns are readable; no JSONB, contact/history, or write grants.
        await using(var connection=await runtimeSource.OpenConnectionAsync())
        {
            await using var currentUser=new NpgsqlCommand("SELECT current_user",connection);
            Assert.Equal("erp_runtime",await currentUser.ExecuteScalarAsync());
            await using(var genericAuditPrivilege=new NpgsqlCommand("SELECT NOT has_table_privilege('erp_runtime','real_estate.events','INSERT')",connection))
                Assert.True((bool)(await genericAuditPrivilege.ExecuteScalarAsync())!,"Agenda must not require generic audit-table INSERT.");
            const string ownerProjection="SELECT has_column_privilege('erp_runtime','real_estate.owners','organization_id','SELECT') AND has_column_privilege('erp_runtime','real_estate.owners','id','SELECT') AND has_column_privilege('erp_runtime','real_estate.owners','name','SELECT') AND has_column_privilege('erp_runtime','real_estate.owners','active','SELECT') AND NOT has_table_privilege('erp_runtime','real_estate.owners','SELECT') AND NOT has_column_privilege('erp_runtime','real_estate.owners','tax_id','SELECT') AND NOT has_column_privilege('erp_runtime','real_estate.owners','history','SELECT') AND NOT has_column_privilege('erp_runtime','real_estate.owners','details','SELECT') AND NOT has_table_privilege('erp_runtime','real_estate.owners','INSERT') AND NOT has_table_privilege('erp_runtime','real_estate.owners','UPDATE') AND NOT has_table_privilege('erp_runtime','real_estate.owners','DELETE')";
            await using(var command=new NpgsqlCommand(ownerProjection,connection))Assert.True((bool)(await command.ExecuteScalarAsync())!);
            const string propertyProjection="SELECT has_column_privilege('erp_runtime','real_estate.properties','organization_id','SELECT') AND has_column_privilege('erp_runtime','real_estate.properties','establishment_id','SELECT') AND has_column_privilege('erp_runtime','real_estate.properties','id','SELECT') AND has_column_privilege('erp_runtime','real_estate.properties','status','SELECT') AND has_column_privilege('erp_runtime','real_estate.properties','code','SELECT') AND has_column_privilege('erp_runtime','real_estate.properties','commercial_code','SELECT') AND has_column_privilege('erp_runtime','real_estate.properties','title','SELECT') AND NOT has_table_privilege('erp_runtime','real_estate.properties','SELECT') AND NOT has_column_privilege('erp_runtime','real_estate.properties','capture','SELECT') AND NOT has_column_privilege('erp_runtime','real_estate.properties','internal_reference','SELECT') AND NOT has_column_privilege('erp_runtime','real_estate.properties','summary','SELECT') AND NOT has_table_privilege('erp_runtime','real_estate.properties','INSERT') AND NOT has_table_privilege('erp_runtime','real_estate.properties','UPDATE') AND NOT has_table_privilege('erp_runtime','real_estate.properties','DELETE')";
            await using(var command=new NpgsqlCommand(propertyProjection,connection))Assert.True((bool)(await command.ExecuteScalarAsync())!);
            await AssertDeniedAsync(connection,"SELECT tax_id FROM real_estate.owners");
            await AssertDeniedAsync(connection,"SELECT capture FROM real_estate.properties");
            await AssertDeniedAsync(connection,"INSERT INTO real_estate.events(organization_id,id,entity_id,event_type,actor) VALUES(gen_random_uuid(),gen_random_uuid(),gen_random_uuid(),'Agenda.Forbidden','test')");
            const string allowed="SELECT has_column_privilege('erp_runtime','real_estate.customers','organization_id','SELECT') AND has_column_privilege('erp_runtime','real_estate.customers','id','SELECT') AND has_column_privilege('erp_runtime','real_estate.customers','establishment_id','SELECT') AND has_column_privilege('erp_runtime','real_estate.customers','name','SELECT') AND has_column_privilege('erp_runtime','real_estate.customers','responsible_subject','SELECT')";
            await using(var command=new NpgsqlCommand(allowed,connection))Assert.True((bool)(await command.ExecuteScalarAsync())!);
            const string forbidden="SELECT NOT has_column_privilege('erp_runtime','real_estate.customers','data','SELECT') AND NOT has_column_privilege('erp_runtime','real_estate.customers','phone','SELECT') AND NOT has_column_privilege('erp_runtime','real_estate.customers','email','SELECT') AND NOT has_column_privilege('erp_runtime','real_estate.customers','document','SELECT') AND NOT has_table_privilege('erp_runtime','real_estate.customer_contacts','SELECT') AND NOT has_table_privilege('erp_runtime','real_estate.customer_responsibility_history','SELECT') AND NOT has_table_privilege('erp_runtime','real_estate.customers','INSERT') AND NOT has_table_privilege('erp_runtime','real_estate.customers','UPDATE') AND NOT has_table_privilege('erp_runtime','real_estate.customers','DELETE') AND NOT has_table_privilege('erp_runtime','real_estate.customers','TRUNCATE') AND NOT has_any_column_privilege('erp_runtime','real_estate.customers','INSERT') AND NOT has_any_column_privilege('erp_runtime','real_estate.customers','UPDATE')";
            await using(var command=new NpgsqlCommand(forbidden,connection))Assert.True((bool)(await command.ExecuteScalarAsync())!);
            await AssertDeniedAsync(connection,"SELECT data FROM real_estate.customers");
            await AssertDeniedAsync(connection,"SELECT id FROM real_estate.customer_contacts");
            await AssertDeniedAsync(connection,"SELECT id FROM real_estate.customer_responsibility_history");
        }

        // T1/T2/T3/T6: scoped lookup returns only the active establishment's projection and current responsible.
        var ownerReference=await store.FindAgendaOwnerReferenceAsync(scope,ownerId,default);
        Assert.Equal("Proprietário sintético",ownerReference!.Name);
        Assert.Null(await store.FindAgendaOwnerReferenceAsync(scope,inactiveOwnerId,default));
        Assert.Null(await store.FindAgendaOwnerReferenceAsync(scope,otherOrganizationOwnerId,default));
        Assert.Null(await store.FindAgendaOwnerReferenceAsync(scope,Guid.NewGuid(),default));
        var propertyReference=await store.FindAgendaPropertyReferenceAsync(scope,propertyId,default);
        Assert.NotNull(propertyReference);Assert.Equal(unit,propertyReference!.EstablishmentId);Assert.Equal("Available",propertyReference.Status);
        Assert.Null(await store.FindAgendaPropertyReferenceAsync(scope,otherUnitPropertyId,default));
        Assert.Null(await store.FindAgendaPropertyReferenceAsync(scope,Guid.NewGuid(),default));
        var draftReference=await store.FindAgendaPropertyReferenceAsync(scope,draftPropertyId,default);
        Assert.NotNull(draftReference);Assert.Equal("Draft",draftReference!.Status);

        var reference=await store.FindAppointmentReferenceAsync(scope,customerId,default);
        Assert.NotNull(reference);Assert.Equal("Cliente autorizado",reference!.Name);
        Assert.Equal("test-only-actor",reference.ResponsibleSubject);Assert.Equal(unit,reference.EstablishmentId);
        Assert.Null(await store.FindAppointmentReferenceAsync(scope,otherUnitCustomerId,default));
        Assert.Null(await store.FindAppointmentReferenceAsync(scope,otherOrganizationCustomerId,default));

        var service=new CommercialAppointmentService(new Access(scope),store,new CustomerTestDirectory(),store,store);
        var starts=DateTimeOffset.UtcNow.AddDays(2);
        var office=new AppointmentInput(Guid.NewGuid(),AppointmentRules.Office,null,starts,starts.AddHours(1),"Unidade teste",null);
        var created=await service.CreateAsync(customerId,office);
        Assert.True(created.IsSuccess,created.Code);Assert.Equal("test-only-actor",created.Value!.ResponsibleSubject);
        var createdEvent=Assert.Single((await store.EventsForAppointmentAsync(scope,created.Value.Id,default))!);
        Assert.Equal(created.Value.Id,createdEvent.AppointmentId);Assert.Equal(1,createdEvent.Version);Assert.Equal("Criado",createdEvent.Action);
        Assert.Equal(scope.Subject,createdEvent.Actor);Assert.Equal(created.Value.ResponsibleSubject,createdEvent.ResponsibleSubject);
        Assert.Equal(created.Value.StartsAt,createdEvent.StartsAt);Assert.Equal(created.Value.EndsAt,createdEvent.EndsAt);
        var retried=await service.CreateAsync(customerId,office);
        Assert.True(retried.IsSuccess,retried.Code);Assert.Equal(created.Value.Id,retried.Value!.Id);
        Assert.Equal("Resource.NotFound",(await service.CreateAsync(otherUnitCustomerId,office with{RequestId=Guid.NewGuid()})).Code);
        Assert.Equal("Resource.NotFound",(await service.CreateAsync(otherOrganizationCustomerId,office with{RequestId=Guid.NewGuid()})).Code);

        var validVisit=new AppointmentInput(Guid.NewGuid(),AppointmentRules.PropertyVisit,propertyId,starts.AddHours(2),starts.AddHours(3),"Endereço do cliente",null);
        var visit=await service.CreateAsync(customerId,validVisit);
        Assert.True(visit.IsSuccess,visit.Code);
        var ineligibleVisit=await service.CreateAsync(customerId,validVisit with{RequestId=Guid.NewGuid(),PropertyId=draftPropertyId});
        Assert.Equal("Appointment.PropertyUnavailable",ineligibleVisit.Code);
        var foreignUnitVisit=await service.CreateAsync(customerId,validVisit with{RequestId=Guid.NewGuid(),PropertyId=otherUnitPropertyId});
        Assert.Equal("Appointment.PropertyUnavailable",foreignUnitVisit.Code);

        // T7/T8: capture without customer, rescheduling, cancellation, event history, and unit isolation remain intact.
        var captureInput=new AppointmentInput(Guid.NewGuid(),AppointmentRules.Capture,null,starts.AddDays(1),starts.AddDays(1).AddHours(1),"Endereço isolado",null,ownerId,"test-only-actor");
        Assert.Equal("Resource.NotFound",(await service.CreateAsync(null,captureInput with{RequestId=Guid.NewGuid(),OwnerId=inactiveOwnerId})).Code);
        Assert.Equal("Resource.NotFound",(await service.CreateAsync(null,captureInput with{RequestId=Guid.NewGuid(),OwnerId=otherOrganizationOwnerId})).Code);
        var capture=await service.CreateAsync(null,captureInput);
        Assert.True(capture.IsSuccess,capture.Code);Assert.Null(capture.Value!.CustomerId);
        var rescheduled=await service.ChangeAsync(new(created.Value.Id,customerId,null,1,"Reagendar",office with{RequestId=Guid.NewGuid(),StartsAt=starts.AddDays(4),EndsAt=starts.AddDays(4).AddHours(1)}));
        Assert.True(rescheduled.IsSuccess,rescheduled.Code);Assert.Equal(2,rescheduled.Value!.Version);
        var appointmentEvents=await store.EventsForAppointmentAsync(scope,created.Value.Id,default);
        Assert.Equal(2,appointmentEvents.Count);Assert.Contains(appointmentEvents,e=>e.Version==2&&e.Action=="Reagendado"&&e.Actor==scope.Subject&&e.ResponsibleSubject==created.Value.ResponsibleSubject);
        var canceled=await service.ChangeAsync(new(capture.Value.Id,null,ownerId,1,"Cancelar"));
        Assert.True(canceled.IsSuccess,canceled.Code);Assert.Equal("Cancelado",(await store.FindAppointmentAsync(scope,capture.Value.Id,default))!.Status);
        Assert.Equal(3,(await service.ListAsync(null,starts.AddDays(-1),starts.AddDays(6))).Value!.Count);
        Assert.Contains((await service.EventsAsync(customerId)).Value!,e=>e.Action=="Reagendado");
        var switchedScope=scope with{ActiveEstablishmentId=otherUnit,Establishments=[new(otherUnit,"Outra")]};
        Assert.Null(await store.FindAppointmentAsync(switchedScope,created.Value.Id,default));

        // T3: failure writing the Agenda-specific event rolls back the appointment in the same transaction.
        await using(var adminSource=NpgsqlDataSource.Create(admin!))
        {
            await using var connection=await adminSource.OpenConnectionAsync();
            var suffix=Guid.NewGuid().ToString("N");var function=$"fail_appointment_event_{suffix}";var trigger=$"fail_appointment_event_trigger_{suffix}";
            await using(var setup=new NpgsqlCommand($"CREATE FUNCTION real_estate.{function}() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'controlled appointment event failure' USING ERRCODE='P0001'; END $$; CREATE TRIGGER {trigger} BEFORE INSERT ON real_estate.commercial_appointment_events FOR EACH ROW EXECUTE FUNCTION real_estate.{function}()",connection))
                await setup.ExecuteNonQueryAsync();
            var failedInput=new AppointmentInput(Guid.NewGuid(),AppointmentRules.Capture,null,starts.AddDays(8),starts.AddDays(8).AddHours(1),"Local de teste",null,ownerId,"test-only-actor");
            try
            {
                var failure=await Assert.ThrowsAsync<PostgresException>(()=>service.CreateAsync(null,failedInput));
                Assert.Equal("P0001",failure.SqlState);Assert.Equal("controlled appointment event failure",failure.MessageText);
                await using var count=new NpgsqlCommand("SELECT count(*) FROM real_estate.commercial_appointments WHERE organization_id=@org AND establishment_id=@unit AND request_id=@request",connection);
                count.Parameters.AddWithValue("org",organization);count.Parameters.AddWithValue("unit",unit);count.Parameters.AddWithValue("request",failedInput.RequestId);
                Assert.Equal(0L,(long)(await count.ExecuteScalarAsync())!);
            }
            finally
            {
                await using var cleanup=new NpgsqlCommand($"DROP TRIGGER IF EXISTS {trigger} ON real_estate.commercial_appointment_events; DROP FUNCTION IF EXISTS real_estate.{function}()",connection);
                await cleanup.ExecuteNonQueryAsync();
            }
            await using(var immutable=new NpgsqlCommand("UPDATE real_estate.commercial_appointment_events SET action='Corrompido' WHERE appointment_id=@id",connection))
            {
                immutable.Parameters.AddWithValue("id",created.Value.Id);var error=await Assert.ThrowsAsync<PostgresException>(()=>immutable.ExecuteNonQueryAsync());Assert.Equal("42501",error.SqlState);
            }
            await using(var immutable=new NpgsqlCommand("DELETE FROM real_estate.commercial_appointment_events WHERE appointment_id=@id",connection))
            {
                immutable.Parameters.AddWithValue("id",created.Value.Id);var error=await Assert.ThrowsAsync<PostgresException>(()=>immutable.ExecuteNonQueryAsync());Assert.Equal("42501",error.SqlState);
            }
        }
    }
}
