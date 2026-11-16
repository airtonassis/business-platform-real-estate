using Npgsql;
using RealEstate.Application;
using RealEstate.Domain;
using RealEstate.Infrastructure;
using Xunit;

namespace RealEstate.Tests;

[Collection("PostgreSQL")]
public sealed class NegotiationActionDatabaseTests
{
    private sealed class Access(AccessScope scope):ICoreAccess
    { public Task<Outcome<AccessScope>> RequireAsync(string resource,string permission,CancellationToken token)=>Task.FromResult(new Outcome<AccessScope>(scope)); }
    private sealed class Directory:ICustomerDirectory
    {
        public Task<Outcome<CaptureResponsibleDirectory>> ReadAsync(AccessScope scope,Guid unit,string? subject=null,CancellationToken token=default)
        {
            CaptureResponsible[] people=[new("test-only-actor","Atendente de teste",Guid.NewGuid(),"Corretor",Guid.NewGuid()),new("second-actor","Segundo atendente",Guid.NewGuid(),"Corretor",Guid.NewGuid())];
            return Task.FromResult(new Outcome<CaptureResponsibleDirectory>(new(scope.OrganizationId,unit,subject is null?people:people.Where(x=>x.Subject==subject).ToArray())));
        }
    }
    private sealed class Team:ITeamStore
    { public Task<TeamRecord?> FindAsync(AccessScope scope,Guid id,CancellationToken token)=>Task.FromResult<TeamRecord?>(new(id,new("Corretor",null,"SP",["Venda"],null,"Ativo"),1));public Task<Outcome<TeamRecord>> SaveAsync(AccessScope s,Guid id,TeamProfessional i,long v,CancellationToken t)=>throw new NotImplementedException(); }
    private sealed class Hierarchy:IOperationalHierarchyAccess
    { public Task<Outcome<OperationalAuthority>> AuthorityAsync(AccessScope s,Guid id,CancellationToken t)=>Task.FromResult(new Outcome<OperationalAuthority>(new(true,false,1,DateTimeOffset.UtcNow)));public Task<Outcome<OperationalRecipient>> RecipientAsync(AccessScope s,Guid id,CancellationToken t)=>Task.FromResult(new Outcome<OperationalRecipient>(new(true,DateTimeOffset.UtcNow))); }

    [Fact]
    public async Task Actions_and_agenda_commit_together_with_idempotency_versions_and_history()
    {
        var runtime=Environment.GetEnvironmentVariable("AppointmentTestRuntimeConnection");var admin=Environment.GetEnvironmentVariable("AppointmentTestAdminConnection");
        if(string.IsNullOrWhiteSpace(runtime)||string.IsNullOrWhiteSpace(admin))return;
        await using var adminSource=NpgsqlDataSource.Create(admin);await using(var check=await adminSource.OpenConnectionAsync())
        {await using var version=new NpgsqlCommand("SELECT version FROM real_estate.schema_versions WHERE version=28",check);Assert.Equal(28,Convert.ToInt32(await version.ExecuteScalarAsync()));}
        var org=Guid.NewGuid();var unit=Guid.NewGuid();var scope=new AccessScope(org,"test-only-actor","Synthetic",[new(unit,"Unidade de teste")],LegacyManager:true,ActiveEstablishmentId:unit);
        await using var dataSource=NpgsqlDataSource.Create(runtime);var store=new PostgresPropertyStore(dataSource);var access=new Access(scope);var directory=new Directory();var team=new Team();var hierarchy=new Hierarchy();
        var owner=(await new PropertyService(access,store).CreateOwnerAsync("Proprietário sintético")).Value!;
        var capture=new CaptureData{Areas=new(){Built=80,Unit="m2"},Rooms=new(){Bedrooms=2,Parking=1}};capture.Values.Sale.Authorized=350000;
        var properties=new PropertyServiceV2(access,store);var saved=await properties.SaveAsync(new(Code:"S05B1-"+Guid.NewGuid().ToString("N")[..8],Title:"Imóvel sintético",OwnerId:owner.Id,Category:"Residencial",Type:"Apartamento",Purpose:"Venda",Price:350000,Address:new("Rua sintética","12345678",Latitude:1,Longitude:1),Capture:capture));Assert.True(saved.IsSuccess,saved.Code);
        var review=await properties.SubmitForReviewAsync(saved.Value!.Id,saved.Value.Version);var approved=await properties.ApproveAsync(saved.Value.Id,review.Value!.Version);Assert.True(approved.IsSuccess,approved.Code);
        var offer=Assert.Single(await store.ListAnnouncementsAsync(scope,approved.Value!.Id,default),x=>x.LegacyAnchor);
        var customerResult=await new CustomerService(access,store,directory).SaveAsync(new("Cliente sintético","Física","11999998888",null,"Site","Compra","test-only-actor",ResponsibleName:"Atendente de teste"));Assert.True(customerResult.IsSuccess,customerResult.Code);
        var customer=customerResult.Value!;var negotiationService=new NegotiationService(access,store,store,directory,team,hierarchy);
        var negotiation=(await negotiationService.OpenAsync(new(Guid.NewGuid(),customer.Id,approved.Value.Id,offer.Id))).Value!;
        var agenda=new CommercialAppointmentService(access,store,directory,store,store);var actions=new NegotiationActionService(access,store,store,directory,team,hierarchy,agenda);

        var contactInput=new NegotiationActionInput(Guid.NewGuid(),NegotiationActionKinds.Contact,"Retornar para confirmar condições",DateTimeOffset.UtcNow.AddDays(2),null,ExpectedNegotiationVersion:negotiation.Version);
        var contact=await actions.CreateAsync(negotiation.Id,contactInput);Assert.True(contact.IsSuccess,contact.Code);
        var retry=await actions.CreateAsync(negotiation.Id,contactInput);Assert.True(retry.IsSuccess,retry.Code);Assert.Equal(contact.Value!.Id,retry.Value!.Id);
        Assert.Equal("NegotiationAction.RequestConflict",(await actions.CreateAsync(negotiation.Id,contactInput with{Description="Descrição divergente"})).Code);
        var completion=new NegotiationActionCompletion(Guid.NewGuid(),contact.Value.Version,"Cliente retornará amanhã");
        var done=await actions.CompleteAsync(negotiation.Id,contact.Value.Id,completion);Assert.True(done.IsSuccess,done.Code);Assert.Equal("Concluída",done.Value!.Action.Status);
        var doneAgain=await actions.CompleteAsync(negotiation.Id,contact.Value.Id,completion);Assert.True(doneAgain.IsSuccess,doneAgain.Code);Assert.Equal(done.Value.Action.Id,doneAgain.Value!.Action.Id);
        Assert.Equal(2,(await actions.ListAsync(negotiation.Id)).Value!.Single(x=>x.Id==contact.Value.Id).Version);
        Assert.Equal(2,(await actions.EventsAsync(negotiation.Id)).Value!.Where(x=>x.ActionId==contact.Value.Id).Count());

        var appointmentRequest=Guid.NewGuid();var start=DateTimeOffset.UtcNow.AddDays(3);var appointmentInput=new AppointmentInput(appointmentRequest,AppointmentRules.Office,null,start,start.AddHours(1),"Unidade de teste","Compromisso da negociação");
        var commitmentInput=new NegotiationActionInput(appointmentRequest,NegotiationActionKinds.Appointment,"Atender o cliente na imobiliária",null,null,appointmentInput,negotiation.Version);
        var commitment=await actions.CreateAsync(negotiation.Id,commitmentInput);Assert.True(commitment.IsSuccess,commitment.Code);Assert.NotNull(commitment.Value!.AppointmentId);
        var linked=(await store.FindAppointmentAsync(scope,commitment.Value.AppointmentId!.Value,default))!;Assert.Equal(customer.Id,linked.CustomerId);Assert.Equal("Agendado",linked.Status);
        var commitmentRetry=await actions.CreateAsync(negotiation.Id,commitmentInput);Assert.True(commitmentRetry.IsSuccess,commitmentRetry.Code);Assert.Equal(commitment.Value.Id,commitmentRetry.Value!.Id);Assert.Equal(linked.Id,commitmentRetry.Value.AppointmentId);
        await using(var runtimeConnection=await dataSource.OpenConnectionAsync())
        {
            await using var tx=await runtimeConnection.BeginTransactionAsync();await using(var context=new NpgsqlCommand("SELECT set_config('erp.organization_id',@org,true),set_config('erp.establishments',@units,true),set_config('erp.active_establishment',@unit,true),set_config('erp.legacy_manager','enabled',true)",runtimeConnection,tx))
            {context.Parameters.AddWithValue("org",org.ToString());context.Parameters.AddWithValue("units","{"+unit+"}");context.Parameters.AddWithValue("unit",unit.ToString());await context.ExecuteNonQueryAsync();}
            await using var duplicate=new NpgsqlCommand("INSERT INTO real_estate.commercial_negotiation_actions(organization_id,establishment_id,id,negotiation_id,customer_id,property_id,request_id,appointment_id,kind,description,responsible_subject,responsible_name,created_by,updated_by) VALUES(@org,@unit,@id,@negotiation,@customer,@property,@request,@appointment,'Compromisso','Vínculo repetido','test-only-actor','Atendente de teste','test-only-actor','test-only-actor')",runtimeConnection,tx);
            duplicate.Parameters.AddWithValue("org",org);duplicate.Parameters.AddWithValue("unit",unit);duplicate.Parameters.AddWithValue("id",Guid.NewGuid());duplicate.Parameters.AddWithValue("negotiation",negotiation.Id);duplicate.Parameters.AddWithValue("customer",customer.Id);duplicate.Parameters.AddWithValue("property",approved.Value.Id);duplicate.Parameters.AddWithValue("request",Guid.NewGuid());duplicate.Parameters.AddWithValue("appointment",linked.Id);
            var unique=await Assert.ThrowsAsync<PostgresException>(()=>duplicate.ExecuteNonQueryAsync());Assert.Equal("commercial_negotiation_actions_organization_id_appointment_id_key",unique.ConstraintName);await tx.RollbackAsync();
        }
        await using(var c=await adminSource.OpenConnectionAsync())
        {await using var due=new NpgsqlCommand("UPDATE real_estate.commercial_appointments SET starts_at=clock_timestamp()-interval '2 hours',ends_at=clock_timestamp()-interval '1 hour' WHERE organization_id=@org AND establishment_id=@unit AND id=@id",c);due.Parameters.AddWithValue("org",org);due.Parameters.AddWithValue("unit",unit);due.Parameters.AddWithValue("id",linked.Id);await due.ExecuteNonQueryAsync();}
        var realization=new NegotiationActionCompletion(Guid.NewGuid(),commitment.Value.Version,"Atendimento realizado");var realized=await actions.CompleteAsync(negotiation.Id,commitment.Value.Id,realization);Assert.True(realized.IsSuccess,realized.Code);
        Assert.Equal("Realizado",realized.Value!.Appointment!.Status);Assert.Equal("Realizado",(await agenda.EventsAsync(customer.Id)).Value!.Single(x=>x.AppointmentId==linked.Id&&x.Action=="Realizado").Action);
        Assert.True((await actions.CompleteAsync(negotiation.Id,commitment.Value.Id,realization)).IsSuccess);
        var linkedEvents=await actions.EventsAsync(negotiation.Id);Assert.Contains(linkedEvents.Value!,x=>x.ActionId==commitment.Value.Id&&x.Event=="Concluída");

        var appointmentsBefore=await store.ListAsync(scope,customer.Id,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow.AddDays(10),default);var actionsBefore=await actions.ListAsync(negotiation.Id);
        await using(var adminConnection=await adminSource.OpenConnectionAsync())await using(var revoke=new NpgsqlCommand("REVOKE INSERT ON real_estate.commercial_negotiation_action_events FROM erp_runtime",adminConnection))await revoke.ExecuteNonQueryAsync();
        try
        {
            var rollbackId=Guid.NewGuid();var rollbackStart=DateTimeOffset.UtcNow.AddDays(5);var rollbackAppointment=new AppointmentInput(rollbackId,AppointmentRules.Office,null,rollbackStart,rollbackStart.AddHours(1),"Unidade de teste",null);
            var rollbackInput=new NegotiationActionInput(rollbackId,NegotiationActionKinds.Appointment,"Compromisso para testar rollback",null,null,rollbackAppointment,negotiation.Version);
            await Assert.ThrowsAsync<PostgresException>(()=>actions.CreateAsync(negotiation.Id,rollbackInput));
            Assert.Equal(appointmentsBefore.Count,(await store.ListAsync(scope,customer.Id,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow.AddDays(10),default)).Count);
            Assert.Equal(actionsBefore.Value!.Count,(await actions.ListAsync(negotiation.Id)).Value!.Count);
        }
        finally{await using var adminConnection=await adminSource.OpenConnectionAsync();await using var grant=new NpgsqlCommand("GRANT SELECT,INSERT ON real_estate.commercial_negotiation_action_events TO erp_runtime",adminConnection);await grant.ExecuteNonQueryAsync();}

        var concurrentRequest=Guid.NewGuid();var concurrentInput=new NegotiationActionInput(concurrentRequest,NegotiationActionKinds.Pending,"Enviar resumo da proposta",DateTimeOffset.UtcNow.AddDays(1),null,ExpectedNegotiationVersion:negotiation.Version);
        var concurrent=await Task.WhenAll(actions.CreateAsync(negotiation.Id,concurrentInput),actions.CreateAsync(negotiation.Id,concurrentInput));Assert.All(concurrent,x=>Assert.True(x.IsSuccess,x.Code));var concurrentAction=concurrent[0].Value!;Assert.Equal(concurrentAction.Id,concurrent[1].Value!.Id);
        var contested=new NegotiationActionCompletion(Guid.NewGuid(),concurrentAction.Version,"Resumo enviado");var race=await Task.WhenAll(actions.CompleteAsync(negotiation.Id,concurrentAction.Id,contested),actions.CompleteAsync(negotiation.Id,concurrentAction.Id,contested with{RequestId=Guid.NewGuid()}));Assert.Single(race,x=>x.IsSuccess);Assert.Single(race,x=>x.Code=="NegotiationAction.VersionConflict");
        Assert.Contains((await actions.EventsAsync(negotiation.Id)).Value!,x=>x.ActionId==concurrentAction.Id&&x.Actor==scope.Subject&&x.Version==2);
    }
}
