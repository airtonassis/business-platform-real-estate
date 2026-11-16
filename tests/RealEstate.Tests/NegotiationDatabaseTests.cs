using Npgsql;
using RealEstate.Application;
using RealEstate.Domain;
using RealEstate.Infrastructure;
using Xunit;
namespace RealEstate.Tests;
[Collection("PostgreSQL")]
public sealed class NegotiationDatabaseTests
{
    private sealed class ActiveProfessional:ITeamStore
    {public Task<TeamRecord?> FindAsync(AccessScope s,Guid id,CancellationToken t)=>Task.FromResult<TeamRecord?>(new(id,new("Corretor","CRECI-1","SP",["Venda"],null),1));public Task<Outcome<TeamRecord>> SaveAsync(AccessScope s,Guid id,TeamProfessional i,long v,CancellationToken t)=>throw new NotImplementedException();}
    private sealed class Hierarchy(bool immediate=false,bool recovery=false):IOperationalHierarchyAccess
    {public Task<Outcome<OperationalAuthority>> AuthorityAsync(AccessScope s,Guid id,CancellationToken t)=>Task.FromResult(new Outcome<OperationalAuthority>(new(immediate,recovery,1,DateTimeOffset.UtcNow)));public Task<Outcome<OperationalRecipient>> RecipientAsync(AccessScope s,Guid id,CancellationToken t)=>Task.FromResult(new Outcome<OperationalRecipient>(new(true,DateTimeOffset.UtcNow)));}
    private sealed class Access(AccessScope scope):ICoreAccess
    {public Task<Outcome<AccessScope>> RequireAsync(string r,string p,CancellationToken t)=>Task.FromResult(new Outcome<AccessScope>(scope));}
    [Fact]public async Task Restricted_login_opens_once_scopes_reads_and_rolls_back_failed_event()
    {
        var runtime=Environment.GetEnvironmentVariable("ConnectionStrings__Erp");var adminConnection=Environment.GetEnvironmentVariable("ErpTestAdminConnection");
        if(string.IsNullOrWhiteSpace(runtime)||string.IsNullOrWhiteSpace(adminConnection))return;
        var unit=Guid.NewGuid();var scope=new AccessScope(Guid.NewGuid(),"test-only-actor","Synthetic",[new(unit,"Unit")],LegacyManager:true,ActiveEstablishmentId:unit);
        await using var ds=NpgsqlDataSource.Create(runtime);var store=new PostgresPropertyStore(ds);var auth=new Access(scope);
        var owner=(await new PropertyService(auth,store).CreateOwnerAsync("Owner S05A")).Value!;
        var capture=new CaptureData{Areas=new(){Built=80,Unit="m2"},Rooms=new(){Bedrooms=2,Parking=1}};capture.Values.Sale.Authorized=350000;
        var properties=new PropertyServiceV2(auth,store);
        var saved=await properties.SaveAsync(new(Code:"S05-"+Guid.NewGuid().ToString("N")[..9],Type:"Apartamento",Purpose:"Venda",Title:"Imóvel S05A",OwnerId:owner.Id,Category:"Residencial",Price:350000,Address:new("Rua sintética","12345678",Latitude:1,Longitude:1),Capture:capture));Assert.True(saved.IsSuccess,saved.Code);
        var sent=await properties.SubmitForReviewAsync(saved.Value!.Id,saved.Value.Version);Assert.True(sent.IsSuccess,sent.Code);
        var approved=await properties.ApproveAsync(saved.Value.Id,sent.Value!.Version);Assert.True(approved.IsSuccess,approved.Code);
        var offer=Assert.Single(await store.ListAnnouncementsAsync(scope,approved.Value!.Id,default),a=>a.LegacyAnchor);
        var customer=await new CustomerService(auth,store,new CustomerTestDirectory()).SaveAsync(new("Cliente S05A","Física","11999998888",null,"Site","Compra",scope.Subject));Assert.True(customer.IsSuccess,customer.Code);
        var service=new NegotiationService(auth,store,store,new CustomerTestDirectory(),new ActiveProfessional(),new Hierarchy());var request=new OpenNegotiation(Guid.NewGuid(),customer.Value!.Id,approved.Value.Id,offer.Id);
        var created=await service.OpenAsync(request);Assert.True(created.IsSuccess,created.Code);Assert.Equal("Preparing",created.Value!.Status);Assert.Equal(owner.Id,created.Value.OwnerId);
        var retried=await service.OpenAsync(request);Assert.Equal(created.Value.Id,retried.Value!.Id);
        Assert.Equal("Negotiation.RequestConflict",(await service.OpenAsync(request with{PropertyId=Guid.NewGuid()})).Code);
        Assert.Single((await service.EventsAsync(created.Value.Id)).Value!);Assert.Equal(1,(await service.ListAsync(customer.Value.Id)).Value!.Total);
        Assert.Equal("Negotiation.OfferUnavailable",(await service.OpenAsync(request with{RequestId=Guid.NewGuid(),AnnouncementId=Guid.NewGuid()})).Code);
        var managerScope=scope with{Subject="manager-actor"};var manager=new NegotiationService(new Access(managerScope),store,store,new CustomerTestDirectory(),new ActiveProfessional(),new Hierarchy(immediate:true));
        var transfer=new NegotiationTransfer(Guid.NewGuid(),"second-actor",created.Value.Version,"ReorganizaÃ§Ã£o sintÃ©tica");
        var moved=await manager.TransferAsync(created.Value.Id,transfer);Assert.True(moved.IsSuccess,moved.Code);Assert.Equal(2,moved.Value!.Version);Assert.Equal("second-actor",moved.Value.ResponsibleSubject);
        var transferEvent=(await manager.EventsAsync(created.Value.Id)).Value!.Last();Assert.Equal("ResponsibilityTransferred",transferEvent.Action);Assert.Equal("test-only-actor",transferEvent.PreviousResponsibleSubject);Assert.Equal("second-actor",transferEvent.ResponsibleSubject);Assert.Equal(managerScope.Subject,transferEvent.Actor);Assert.Equal(transfer.Reason,transferEvent.Reason);
        var repeatedTransfer=await manager.TransferAsync(created.Value.Id,transfer);Assert.True(repeatedTransfer.IsSuccess,repeatedTransfer.Code);Assert.Equal(2,repeatedTransfer.Value!.Version);
        NegotiationTransfer[] competing=[new(Guid.NewGuid(),"test-only-actor",2,"ConcorrÃªncia A"),new(Guid.NewGuid(),"test-only-actor",2,"ConcorrÃªncia B")];
        var competingResults=await Task.WhenAll(competing.Select(x=>manager.TransferAsync(created.Value.Id,x)));Assert.Single(competingResults,x=>x.IsSuccess);Assert.Single(competingResults,x=>x.Code=="Negotiation.VersionConflict");
        var afterRace=(await manager.FindAsync(created.Value.Id)).Value!;Assert.Equal(3L,afterRace.Version);Assert.Equal("test-only-actor",afterRace.ResponsibleSubject);Assert.Equal(3,(await manager.EventsAsync(created.Value.Id)).Value!.Count);
        var parallel=request with{RequestId=Guid.NewGuid()};var simultaneous=await Task.WhenAll(service.OpenAsync(parallel),service.OpenAsync(parallel));Assert.All(simultaneous,r=>Assert.True(r.IsSuccess,r.Code));Assert.Equal(simultaneous[0].Value!.Id,simultaneous[1].Value!.Id);
        var otherUnit=Guid.NewGuid();var foreign=new NegotiationService(new Access(scope with{ActiveEstablishmentId=otherUnit,Establishments=[new(otherUnit,"Other")]}),store,store,new CustomerTestDirectory(),new ActiveProfessional(),new Hierarchy());
        Assert.Equal("Resource.NotFound",(await foreign.FindAsync(created.Value.Id)).Code);Assert.Equal(0,(await foreign.ListAsync(null)).Value!.Total);
        var otherOrg=new NegotiationService(new Access(scope with{OrganizationId=Guid.NewGuid()}),store,store,new CustomerTestDirectory(),new ActiveProfessional(),new Hierarchy());Assert.Equal("Resource.NotFound",(await otherOrg.FindAsync(created.Value.Id)).Code);Assert.Equal(0,(await otherOrg.ListAsync(null)).Value!.Total);
        await using var admin=new NpgsqlConnection(adminConnection);await admin.OpenAsync();
        try{
            await using(var revoke=new NpgsqlCommand("REVOKE INSERT ON real_estate.commercial_negotiation_events FROM erp_runtime",admin))await revoke.ExecuteNonQueryAsync();
            await Assert.ThrowsAsync<PostgresException>(()=>service.OpenAsync(request with{RequestId=Guid.NewGuid()}));
            var beforeRollback=(await manager.FindAsync(created.Value.Id)).Value!;
            await Assert.ThrowsAsync<PostgresException>(()=>manager.TransferAsync(created.Value.Id,new(Guid.NewGuid(),"second-actor",beforeRollback.Version,"Falha sintÃ©tica")));
            var afterRollback=(await manager.FindAsync(created.Value.Id)).Value!;Assert.Equal(beforeRollback.Version,afterRollback.Version);Assert.Equal(beforeRollback.ResponsibleSubject,afterRollback.ResponsibleSubject);
        }finally{await using var restore=new NpgsqlCommand("GRANT INSERT ON real_estate.commercial_negotiation_events TO erp_runtime",admin);await restore.ExecuteNonQueryAsync();}
        Assert.Equal(2,(await service.ListAsync(null)).Value!.Total);
    }
}
