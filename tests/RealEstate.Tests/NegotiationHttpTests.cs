using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RealEstate.Domain;
using RealEstate.Web;
using Xunit;
namespace RealEstate.Tests;
public sealed class NegotiationHttpTests
{
    private sealed class Customers:ICustomerStore
    {
        public Task<CustomerFile?> FindAsync(AccessScope s,Guid id,CancellationToken t)=>Task.FromResult<CustomerFile?>(new(new Customer(id,s.ActiveEstablishmentId!.Value,new CustomerInput("Cliente","Física","11999999999",null,"Site","Compra",s.Subject),1),[],[]));
        public Task<AppointmentCustomerReference?> FindAppointmentReferenceAsync(AccessScope s,Guid id,CancellationToken t)=>Task.FromResult<AppointmentCustomerReference?>(new(id,s.ActiveEstablishmentId!.Value,"Cliente",s.Subject));
        public Task<CustomerPage> SearchAsync(AccessScope s,string q,int p,CancellationToken t)=>throw new NotImplementedException();
        public Task<Outcome<Customer>> SaveAsync(AccessScope s,CustomerInput i,Guid? id,long v,bool a,CancellationToken t)=>throw new NotImplementedException();
        public Task<Outcome<CustomerContact>> AddContactAsync(AccessScope s,Guid id,ContactInput i,CancellationToken t)=>throw new NotImplementedException();
    }
    private sealed class Directory:ICustomerDirectory
    {public Task<Outcome<CaptureResponsibleDirectory>> ReadAsync(AccessScope s,Guid u,string? subject=null,CancellationToken t=default)=>Task.FromResult(new Outcome<CaptureResponsibleDirectory>(new(s.OrganizationId,u,[new(s.Subject,"Responsável",Guid.NewGuid(),"Corretor",Guid.NewGuid())])));}
    private sealed class Teams:ITeamStore
    {public Task<TeamRecord?> FindAsync(AccessScope s,Guid id,CancellationToken t)=>Task.FromResult<TeamRecord?>(new(id,new("Corretor","CRECI-1","SP",["Venda"],null),1));public Task<Outcome<TeamRecord>> SaveAsync(AccessScope s,Guid id,TeamProfessional i,long v,CancellationToken t)=>throw new NotImplementedException();}
    private sealed class Hierarchy:IOperationalHierarchyAccess
    {public Task<Outcome<OperationalAuthority>> AuthorityAsync(AccessScope s,Guid id,CancellationToken t)=>Task.FromResult(new Outcome<OperationalAuthority>(new(true,false,1,DateTimeOffset.UtcNow)));public Task<Outcome<OperationalRecipient>> RecipientAsync(AccessScope s,Guid id,CancellationToken t)=>Task.FromResult(new Outcome<OperationalRecipient>(new(true,DateTimeOffset.UtcNow)));}
    private sealed class Store:INegotiationStore
    {
        public int Calls;
        public Task<Outcome<Negotiation>> OpenAsync(AccessScope scope,OpenNegotiation input,string owner,NegotiationAssignee assignee,CancellationToken token)
        {Calls++;return Task.FromResult(new Outcome<Negotiation>(new(Guid.NewGuid(),input.CustomerId,input.PropertyId,input.AnnouncementId,Guid.NewGuid(),scope.ActiveEstablishmentId!.Value,assignee.Subject,"Preparing",1,DateTimeOffset.UtcNow,assignee.MembershipId)));}
        public Task<Outcome<Negotiation>> TransferAsync(AccessScope scope,Guid id,NegotiationTransfer input,NegotiationAssignee assignee,bool recovery,CancellationToken token){Calls++;return Task.FromResult(Outcome<Negotiation>.Fail("Negotiation.VersionConflict"));}
        public Task<Negotiation?> FindNegotiationAsync(AccessScope scope,Guid id,CancellationToken token)=>Task.FromResult<Negotiation?>(null);
        public Task<NegotiationPage> ListAsync(AccessScope scope,Guid? customerId,int page,CancellationToken token)=>Task.FromResult(new NegotiationPage([],0,page));
        public Task<IReadOnlyList<NegotiationEvent>> NegotiationEventsAsync(AccessScope scope,Guid id,CancellationToken token)=>Task.FromResult<IReadOnlyList<NegotiationEvent>>([]);
    }
    [Fact]public async Task Razor_list_is_scoped_and_denial_does_not_read_store()
    {
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../backend/src/RealEstate.Web"));
        var store=new Store();await using var app=Program.Build(["--contentRoot",root],b=>{b.WebHost.UseTestServer();FixtureProgram.Configure(b);b.Services.RemoveAll<INegotiationStore>();b.Services.AddSingleton<INegotiationStore>(store);});await app.StartAsync();
        var client=app.GetTestClient();var ok=await client.GetAsync("/commercial/negotiations");Assert.Equal(HttpStatusCode.OK,ok.StatusCode);
        var html=await ok.Content.ReadAsStringAsync();Assert.Contains("Nenhuma negociação encontrada",html);Assert.Contains("name=\"viewport\"",html);
        client.DefaultRequestHeaders.Add("X-Fixture-Deny","true");var denied=await client.GetAsync("/commercial/negotiations");Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        Assert.Equal(0,store.Calls);
    }
    [Fact]public async Task Api_open_requires_authenticated_effective_rights()
    {
        var store=new Store();await using var app=Program.Build([],b=>{b.WebHost.UseTestServer();FixtureProgram.Configure(b);b.Services.RemoveAll<INegotiationStore>();b.Services.AddSingleton<INegotiationStore>(store);b.Services.RemoveAll<ICustomerStore>();b.Services.AddSingleton<ICustomerStore>(new Customers());b.Services.RemoveAll<ICustomerDirectory>();b.Services.AddSingleton<ICustomerDirectory>(new Directory());b.Services.RemoveAll<ITeamStore>();b.Services.AddSingleton<ITeamStore>(new Teams());b.Services.RemoveAll<IOperationalHierarchyAccess>();b.Services.AddSingleton<IOperationalHierarchyAccess>(new Hierarchy());});await app.StartAsync();
        var client=app.GetTestClient();client.DefaultRequestHeaders.Authorization=new("Bearer","fixture");
        var input=new OpenNegotiation(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid());
        client.DefaultRequestHeaders.Add("X-Fixture-Deny","true");Assert.Equal(HttpStatusCode.Forbidden,(await client.PostAsJsonAsync("/api/v1/negotiations",input)).StatusCode);Assert.Equal(0,store.Calls);
        client.DefaultRequestHeaders.Remove("X-Fixture-Deny");Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync("/api/v1/negotiations",input)).StatusCode);Assert.Equal(1,store.Calls);
    }

    [Fact]public async Task Api_transfer_confirmation_rejects_missing_and_malformed_tokens_without_database_access()
    {
        await using var app=Program.Build([],b=>{b.WebHost.UseTestServer();FixtureProgram.Configure(b);});await app.StartAsync();
        var client=app.GetTestClient();client.DefaultRequestHeaders.Authorization=new("Bearer","fixture");
        var url=$"/api/v1/negotiations/{Guid.NewGuid()}/transfer-activities/confirm";

        var missing=await client.PostAsJsonAsync(url,new { });
        Assert.Equal(HttpStatusCode.BadRequest,missing.StatusCode);
        var malformed=await client.PostAsJsonAsync(url,new { Token="not-a-protected-preview" });
        Assert.Equal(HttpStatusCode.Conflict,malformed.StatusCode);
        Assert.Contains("NegotiationTransfer.PreviewExpired",await malformed.Content.ReadAsStringAsync());
    }
}
