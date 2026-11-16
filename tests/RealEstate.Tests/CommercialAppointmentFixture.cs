using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RealEstate.Domain;
using RealEstate.Web;
namespace RealEstate.Tests;

// Disposable in-memory HTTP fixture. It never connects to the persistent ERP database.
public static class CommercialAppointmentFixture
{
    public static readonly Guid CustomerId=Guid.Parse("77777777-7777-7777-7777-777777777777");
    public static readonly Guid OwnerId=Guid.Parse("88888888-8888-8888-8888-888888888888");
    public static readonly Guid PresentationId=Guid.Parse("99999999-9999-9999-9999-999999999999");
    public static void Configure(WebApplicationBuilder builder)
    {
        UiFixture.Configure(builder);
        builder.Configuration["Agenda:Enabled"]="true";
        builder.Services.RemoveAll<ICustomerStore>();builder.Services.AddSingleton<FixtureCustomers>();builder.Services.AddSingleton<ICustomerStore>(s=>s.GetRequiredService<FixtureCustomers>());
        builder.Services.RemoveAll<ICustomerDirectory>();builder.Services.AddSingleton<ICustomerDirectory,CustomerTestDirectory>();
        builder.Services.RemoveAll<ICommercialInteractionStore>();builder.Services.AddSingleton<FixtureInteractions>();builder.Services.AddSingleton<ICommercialInteractionStore>(s=>s.GetRequiredService<FixtureInteractions>());
        builder.Services.RemoveAll<ICommercialAppointmentStore>();builder.Services.AddSingleton<FixtureAppointments>();builder.Services.AddSingleton<ICommercialAppointmentStore>(s=>s.GetRequiredService<FixtureAppointments>());
        builder.Services.RemoveAll<IOwnerProfileStore>();builder.Services.AddSingleton<IOwnerProfileStore,FixtureOwners>();
        builder.Services.RemoveAll<ITeamStore>();builder.Services.AddSingleton<ITeamStore,FixtureTeamStore>();
    }
    public static void Serve(string url)
    {
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../backend/src/RealEstate.Web"));
        Program.Build(["--contentRoot",root,"--urls",url],Configure).Run();
    }
}
public sealed class FixtureTeamStore:ITeamStore
{
    public Task<TeamRecord?> FindAsync(AccessScope scope,Guid id,CancellationToken token)=>Task.FromResult<TeamRecord?>(new(id,new("Atendimento",null,null,[],null,"Ativo"),1));
    public Task<Outcome<TeamRecord>> SaveAsync(AccessScope scope,Guid id,TeamProfessional input,long version,CancellationToken token)=>Task.FromResult(Outcome<TeamRecord>.Fail("Access.Forbidden"));
}
public sealed class FixtureOwners:IOwnerProfileStore
{
    private static readonly OwnerProfile Owner=new(CommercialAppointmentFixture.OwnerId,"Proprietário de teste","PF","12345678909");
    public Task<OwnerProfile?> FindOwnerProfileAsync(AccessScope scope,Guid id,CancellationToken token)=>Task.FromResult<OwnerProfile?>(id==Owner.Id?Owner:null);
    public Task<IReadOnlyList<OwnerProfile>> SearchOwnersAsync(AccessScope scope,string query,CancellationToken token)=>Task.FromResult<IReadOnlyList<OwnerProfile>>(Owner.Name.Contains(query,StringComparison.OrdinalIgnoreCase)?[Owner]:[]);
    public Task<Outcome<OwnerProfile>> CreateOwnerProfileAsync(AccessScope scope,OwnerProfileInput input,CancellationToken token)=>Task.FromResult(Outcome<OwnerProfile>.Fail("Access.Forbidden"));
    public Task<Outcome<OwnerProfile>> UpdateOwnerProfileAsync(AccessScope scope,Guid id,long version,OwnerProfileInput input,CancellationToken token)=>Task.FromResult(Outcome<OwnerProfile>.Fail("Access.Forbidden"));
}
public sealed class FixtureCustomers:ICustomerStore
{
    private static readonly Customer Customer=new(CommercialAppointmentFixture.CustomerId,FixtureProgram.OrganizationA,new("Cliente de teste","Física","11999998888",null,"Site","Compra","test-only-actor",ResponsibleName:"Atendente sintético"),1);
    public int SaveCalls{get;private set;}public int ContactCalls{get;private set;}
    private bool Authorized(AccessScope scope)=>scope.OrganizationId==Customer.EstablishmentId&&scope.ActiveEstablishmentId==Customer.EstablishmentId&&scope.Establishments?.Any(e=>e.Id==Customer.EstablishmentId)==true;
    public Task<CustomerPage> SearchAsync(AccessScope scope,string query,int page,CancellationToken token)=>Task.FromResult(Authorized(scope)&&Customer.Data.Name.Contains(query,StringComparison.OrdinalIgnoreCase)?new CustomerPage([Customer],page,1):new CustomerPage([],page,0));
    public Task<CustomerFile?> FindAsync(AccessScope scope,Guid id,CancellationToken token)=>Task.FromResult<CustomerFile?>(id==Customer.Id&&Authorized(scope)?new(Customer,[],[]):null);
    public Task<AppointmentCustomerReference?> FindAppointmentReferenceAsync(AccessScope scope,Guid id,CancellationToken token)=>Task.FromResult<AppointmentCustomerReference?>(id==Customer.Id&&Authorized(scope)?new(Customer.Id,Customer.EstablishmentId,Customer.Data.Name,Customer.Data.ResponsibleSubject):null);
    public Task<Outcome<Customer>> SaveAsync(AccessScope scope,CustomerInput input,Guid? id,long version,bool acknowledgeDuplicates,CancellationToken token){SaveCalls++;return Task.FromResult(new Outcome<Customer>(Customer));}
    public Task<Outcome<CustomerContact>> AddContactAsync(AccessScope scope,Guid id,ContactInput input,CancellationToken token){ContactCalls++;return Task.FromResult(Outcome<CustomerContact>.Fail("Resource.NotFound"));}
}
public sealed class FixtureInteractions:ICommercialInteractionStore
{
    public bool HasPresentation{get;set;}public int PresentCalls{get;private set;}public int FeedbackCalls{get;private set;}
    public Task<bool> CustomerExistsAsync(AccessScope scope,Guid id,CancellationToken token)=>Task.FromResult(id==CommercialAppointmentFixture.CustomerId);
    public Task<bool> PropertyExistsAsync(AccessScope scope,Guid id,CancellationToken token)=>Task.FromResult(id==UiFixture.PropertyId);
    public Task<bool> IsCaptureResponsibleAsync(AccessScope scope,Guid id,CancellationToken token)=>Task.FromResult(false);
    public Task<IReadOnlyList<CommercialInteraction>> HistoryAsync(AccessScope scope,Guid? customer,Guid? property,CancellationToken token)=>Task.FromResult<IReadOnlyList<CommercialInteraction>>(HasPresentation&&scope.OrganizationId==FixtureProgram.OrganizationA&&scope.ActiveEstablishmentId==FixtureProgram.OrganizationA&&customer==CommercialAppointmentFixture.CustomerId&&(property is null||property==UiFixture.PropertyId)?[new CommercialInteraction(CommercialAppointmentFixture.PresentationId,CommercialAppointmentFixture.PresentationId,CommercialAppointmentFixture.CustomerId,UiFixture.PropertyId,Guid.NewGuid(),DateTimeOffset.UtcNow.AddDays(-1),"Telefone",CommercialInteractionRules.Presented,null,null,null,"test-only-actor","test-only-actor","IM-001","Imóvel de teste","Cliente de teste")]:[]);
    public Task<Outcome<IReadOnlyList<CommercialInteraction>>> PresentAsync(AccessScope scope,Guid customer,PresentationInput input,CancellationToken token){PresentCalls++;return Task.FromResult(Outcome<IReadOnlyList<CommercialInteraction>>.Fail("Access.Forbidden"));}
    public Task<Outcome<CommercialInteraction>> FeedbackAsync(AccessScope scope,Guid customer,FeedbackInput input,CancellationToken token){FeedbackCalls++;return Task.FromResult(Outcome<CommercialInteraction>.Fail("Access.Forbidden"));}
    public Task<Outcome<CommercialSummary>> SummaryAsync(AccessScope scope,Guid property,DateTimeOffset from,DateTimeOffset until,CancellationToken token)=>Task.FromResult(Outcome<CommercialSummary>.Fail("Access.Forbidden"));
}
public sealed class FixtureAppointments:ICommercialAppointmentStore
{
    private readonly Dictionary<Guid,CommercialAppointment> items=[];
    private readonly List<AppointmentEvent> events=[];
    private readonly Dictionary<Guid,Guid> requests=[];
    public void MakeDue(Guid id){var item=items[id];items[id]=item with{StartsAt=DateTimeOffset.UtcNow.AddHours(-2),EndsAt=DateTimeOffset.UtcNow.AddHours(-1)};}
    public Task<IReadOnlyList<CommercialAppointment>> ListAsync(AccessScope scope,Guid? customerId,DateTimeOffset from,DateTimeOffset until,CancellationToken token)=>Task.FromResult<IReadOnlyList<CommercialAppointment>>(items.Values.Where(x=>(customerId is null||x.CustomerId==customerId)&&x.StartsAt>=from&&x.StartsAt<until).ToArray());
    public Task<CommercialAppointment?> FindAppointmentAsync(AccessScope scope,Guid id,CancellationToken token)=>Task.FromResult(items.GetValueOrDefault(id));
    public Task<IReadOnlyList<AppointmentEvent>> EventsAsync(AccessScope scope,Guid customerId,CancellationToken token)=>Task.FromResult<IReadOnlyList<AppointmentEvent>>(events.Where(x=>items[x.AppointmentId].CustomerId==customerId).ToArray());
    public Task<IReadOnlyList<AppointmentEvent>> EventsForAppointmentAsync(AccessScope scope,Guid appointmentId,CancellationToken token)=>Task.FromResult<IReadOnlyList<AppointmentEvent>>(events.Where(x=>x.AppointmentId==appointmentId).OrderByDescending(x=>x.Version).ToArray());
    public Task<Outcome<CommercialAppointment>> CreateAsync(AccessScope scope,Guid? customerId,AppointmentInput input,string responsibleSubject,string responsibleName,CancellationToken token)
    {
        if(requests.TryGetValue(input.RequestId,out var prior))return Task.FromResult(new Outcome<CommercialAppointment>(items[prior]));
        var item=new CommercialAppointment(Guid.NewGuid(),customerId,input.OwnerId,input.PropertyId,input.Kind,"Agendado",responsibleSubject,responsibleName,input.StartsAt,input.EndsAt,input.Location,input.Notes,null,1,DateTimeOffset.UtcNow);items[item.Id]=item;requests[input.RequestId]=item.Id;Record(item,"Criado",scope.Subject);return Task.FromResult(new Outcome<CommercialAppointment>(item));
    }
    public Task<Outcome<CommercialAppointment>> ChangeAsync(AccessScope scope,AppointmentChange change,CancellationToken token)
    {
        if(!items.TryGetValue(change.AppointmentId,out var before)||before.CustomerId!=change.CustomerId||before.OwnerId!=change.OwnerId)return Task.FromResult(Outcome<CommercialAppointment>.Fail("Resource.NotFound"));
        if(before.Version!=change.Version)return Task.FromResult(Outcome<CommercialAppointment>.Fail("Appointment.VersionConflict"));
        var after=before with{Status=change.Action switch{"Cancelar"=>"Cancelado","Realizar"=>"Realizado",_=>"Agendado"},StartsAt=change.Schedule?.StartsAt??before.StartsAt,EndsAt=change.Schedule?.EndsAt??before.EndsAt,Location=change.Schedule?.Location??before.Location,Notes=change.Schedule?.Notes??before.Notes,Result=change.Result,Version=before.Version+1,UpdatedAt=DateTimeOffset.UtcNow};items[after.Id]=after;Record(after,change.Action=="Reagendar"?"Reagendado":after.Status,scope.Subject);return Task.FromResult(new Outcome<CommercialAppointment>(after));
    }
    private void Record(CommercialAppointment item,string action,string actor)=>events.Add(new(Guid.NewGuid(),item.Id,item.Version,action,DateTimeOffset.UtcNow,actor,item.ResponsibleSubject,item.StartsAt,item.EndsAt,item.Result));
}
