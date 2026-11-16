using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RealEstate.Domain;
using RealEstate.Web;

namespace RealEstate.Tests;

// TEST ONLY. Real Razor pages and CSS, synthetic read-only stores, no external service.
public static class NegotiationVisualFixture
{
    public static readonly Guid Unit=FixtureProgram.OrganizationA;
    public static readonly Guid CustomerSale=Guid.Parse("51000000-0000-0000-0000-000000000001");
    public static readonly Guid CustomerRental=Guid.Parse("51000000-0000-0000-0000-000000000002");
    public static readonly Guid PropertySale=Guid.Parse("52000000-0000-0000-0000-000000000001");
    public static readonly Guid PropertyRental=Guid.Parse("52000000-0000-0000-0000-000000000002");
    public static readonly Guid OfferSale=Guid.Parse("53000000-0000-0000-0000-000000000001");
    public static readonly Guid OfferRental=Guid.Parse("53000000-0000-0000-0000-000000000002");
    public static readonly Guid NegotiationSale=Guid.Parse("54000000-0000-0000-0000-000000000001");
    public static readonly Guid NegotiationRental=Guid.Parse("54000000-0000-0000-0000-000000000002");
    public static readonly Guid Owner=Guid.Parse("55000000-0000-0000-0000-000000000001");
    public static readonly Guid ResponsibleMembership=Guid.Parse("56000000-0000-0000-0000-000000000001");
    public static readonly Guid OtherMembership=Guid.Parse("56000000-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset BaseTime=new(2026,11,10,14,30,0,TimeSpan.Zero);

    public static string ListUrl=>"/commercial/negotiations";
    public static string EmptyUrl=>ListUrl+"?CustomerId="+Guid.Parse("51000000-0000-0000-0000-000000000099");
    public static string OpenUrl=>ListUrl+"?CustomerId="+CustomerSale+"&PropertyId="+PropertySale+"&AnnouncementId="+OfferSale;
    public static string DossierUrl=>ListUrl+"/"+NegotiationSale;
    public static string DossierWithoutTransferUrl=>DossierUrl+"?fixtureTransfer=off";
    public static string CustomerUrl=>"/commercial/customers/"+CustomerSale+"?Panel=properties&Search=true";
    public static string PropertyUrl=>"/commercial/properties/"+PropertySale;

    public static void Configure(WebApplicationBuilder builder)
    {
        CommercialAppointmentFixture.Configure(builder);
        builder.Configuration["UiOnlyFixture"]="true";
        builder.Configuration["Agenda:Enabled"]="false";
        builder.Services.RemoveAll<ICoreAccess>();builder.Services.AddScoped<ICoreAccess,VisualAccess>();
        builder.Services.RemoveAll<ICustomerStore>();builder.Services.AddSingleton<ICustomerStore,VisualCustomers>();
        builder.Services.RemoveAll<ICustomerDirectory>();builder.Services.AddSingleton<ICustomerDirectory,VisualDirectory>();
        builder.Services.RemoveAll<IPropertyStoreV2>();builder.Services.AddSingleton<VisualProperties>();builder.Services.AddSingleton<IPropertyStoreV2>(s=>s.GetRequiredService<VisualProperties>());
        builder.Services.RemoveAll<IPropertyListingStore>();builder.Services.AddSingleton<IPropertyListingStore>(s=>s.GetRequiredService<VisualProperties>());
        builder.Services.RemoveAll<IAnnouncementStore>();builder.Services.AddSingleton<IAnnouncementStore,VisualOffers>();
        builder.Services.RemoveAll<INegotiationStore>();builder.Services.AddSingleton<INegotiationStore,VisualNegotiations>();
        builder.Services.RemoveAll<INegotiationActionStore>();builder.Services.AddSingleton<INegotiationActionStore,VisualActions>();
        builder.Services.RemoveAll<IOperationalHierarchyAccess>();builder.Services.AddSingleton<IOperationalHierarchyAccess,VisualHierarchy>();
        builder.Services.RemoveAll<ICommercialInteractionStore>();builder.Services.AddSingleton<ICommercialInteractionStore,VisualInteractions>();
    }
    public static void Serve(string url)
    {
        if(!Uri.TryCreate(url,UriKind.Absolute,out var uri)||uri.Scheme!="http"||uri.Host!="127.0.0.1")throw new InvalidOperationException("Loopback fixture required.");
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../backend/src/RealEstate.Web"));
        Program.Build(["--contentRoot",root,"--urls",url],Configure).Run();
    }

    private sealed class VisualAccess(IHttpContextAccessor accessor):ICoreAccess
    {
        public Task<Outcome<AccessScope>> RequireAsync(string resource,string permission,CancellationToken token)
        {
            var request=accessor.HttpContext!.Request;
            if(request.Query["fixtureTransfer"]=="off"&&resource=="negotiations"&&permission=="NEGOTIATION_MANAGE")return Task.FromResult(Outcome<AccessScope>.Fail("Access.Forbidden"));
            var scope=new AccessScope(Unit,"test-only-actor","TEST ONLY · Imobiliária sintética",[new(Unit,"Filial fictícia")],ActiveEstablishmentId:Unit,MembershipId:ResponsibleMembership);
            accessor.HttpContext.Items[typeof(AccessScope)]=scope;
            return Task.FromResult(new Outcome<AccessScope>(scope));
        }
    }
    private sealed class VisualCustomers:ICustomerStore
    {
        private static readonly Customer[] Items=[
            new(CustomerSale,Unit,new("Maria Silva","Física","11999990001",null,"Indicação","Compra","test-only-actor",ResponsibleName:"Ana Costa"),1),
            new(CustomerRental,Unit,new("João Lima","Física","11999990002",null,"Site","Locação","test-only-actor",ResponsibleName:"Ana Costa"),1)];
        public Task<CustomerFile?> FindAsync(AccessScope scope,Guid id,CancellationToken token)=>Task.FromResult<CustomerFile?>(scope.OrganizationId==Unit&&scope.ActiveEstablishmentId==Unit&&Items.FirstOrDefault(c=>c.Id==id)is {} c?new(c,[],[]):null);
        public Task<AppointmentCustomerReference?> FindAppointmentReferenceAsync(AccessScope scope,Guid id,CancellationToken token)=>Task.FromResult<AppointmentCustomerReference?>(scope.OrganizationId==Unit&&scope.ActiveEstablishmentId==Unit&&Items.FirstOrDefault(c=>c.Id==id)is {} c?new(c.Id,c.EstablishmentId,c.Data.Name,c.Data.ResponsibleSubject):null);
        public Task<CustomerPage> SearchAsync(AccessScope scope,string query,int page,CancellationToken token){var rows=scope.OrganizationId==Unit&&scope.ActiveEstablishmentId==Unit?Items.Where(c=>c.Data.Name.Contains(query??"",StringComparison.OrdinalIgnoreCase)).ToArray():[];return Task.FromResult(new CustomerPage(rows,page,rows.Length));}
        public Task<Outcome<Customer>> SaveAsync(AccessScope scope,CustomerInput input,Guid? id,long version,bool acknowledgeDuplicates,CancellationToken token)=>Task.FromResult(Outcome<Customer>.Fail("Access.Forbidden"));
        public Task<Outcome<CustomerContact>> AddContactAsync(AccessScope scope,Guid id,ContactInput input,CancellationToken token)=>Task.FromResult(Outcome<CustomerContact>.Fail("Access.Forbidden"));
    }
    private sealed class VisualDirectory:ICustomerDirectory
    {
        public Task<Outcome<CaptureResponsibleDirectory>> ReadAsync(AccessScope scope,Guid unit,string? subject=null,CancellationToken token=default)
        {
            CaptureResponsible[] people=[new("test-only-actor","Ana Costa",Guid.Parse("57000000-0000-0000-0000-000000000001"),"Corretora",ResponsibleMembership),new("second-actor","Bruno Reis",Guid.Parse("57000000-0000-0000-0000-000000000002"),"Corretor",OtherMembership)];
            return Task.FromResult(new Outcome<CaptureResponsibleDirectory>(new(scope.OrganizationId,unit,people.Where(p=>subject is null||p.Subject==subject).ToArray())));
        }
    }
    private sealed class VisualHierarchy:IOperationalHierarchyAccess
    {
        public Task<Outcome<OperationalAuthority>> AuthorityAsync(AccessScope scope,Guid subordinateMembershipId,CancellationToken token)=>Task.FromResult(new Outcome<OperationalAuthority>(new(true,false,1,BaseTime)));
        public Task<Outcome<OperationalRecipient>> RecipientAsync(AccessScope scope,Guid membershipId,CancellationToken token)=>Task.FromResult(new Outcome<OperationalRecipient>(new(true,BaseTime)));
    }
    private sealed class VisualProperties:IPropertyStoreV2,IPropertyListingStore
    {
        private static readonly PropertyV2[] Items=[
            new(PropertySale,Unit,new("IM-123","Casa Jardim",Owner,"Residencial","Casa","Venda",850000,new(Municipality:"São Paulo",Neighborhood:"Jardim Paulista"),Unit),1,PropertyLifecycle.Available,"IM-123"),
            new(PropertyRental,Unit,new("IM-456","Apartamento Centro",Owner,"Residencial","Apartamento","Locação",3200,new(Municipality:"São Paulo",Neighborhood:"Centro"),Unit),1,PropertyLifecycle.Available,"IM-456")];
        public Task<PropertyV2?> FindV2Async(AccessScope scope,Guid id,CancellationToken token)=>Task.FromResult<PropertyV2?>(scope.OrganizationId==Unit&&scope.ActiveEstablishmentId==Unit?Items.FirstOrDefault(p=>p.Id==id):null);
        public Task<WorkspaceV2> ListV2Async(AccessScope scope,CancellationToken token)=>Task.FromResult(new WorkspaceV2(scope.OrganizationId==Unit&&scope.ActiveEstablishmentId==Unit?Items:[],[],scope.Establishments));
        public Task<PropertyListPage> ListPageAsync(AccessScope scope,PropertyListQuery query,CancellationToken token)
        {
            var rows=scope.OrganizationId==Unit&&scope.ActiveEstablishmentId==Unit?Items.Where(p=>string.IsNullOrWhiteSpace(query.Search)||p.Data.Title!.Contains(query.Search,StringComparison.OrdinalIgnoreCase)||p.CommercialCode!.Contains(query.Search,StringComparison.OrdinalIgnoreCase)).ToArray():[];
            IReadOnlyDictionary<Guid,IReadOnlyList<ListingAnnouncement>> offers=new Dictionary<Guid,IReadOnlyList<ListingAnnouncement>>{{PropertySale,[new(OfferSale,"Venda","IM-123-V")]},{PropertyRental,[new(OfferRental,"Locação","IM-456-L")]}};
            return Task.FromResult(new PropertyListPage(rows,rows.Length,scope.Establishments??[],offers));
        }
        public Task<Outcome<PropertyV2>> SaveV2Async(AccessScope scope,PropertyInputV2 input,Guid? id,long version,CancellationToken token)=>Task.FromResult(Outcome<PropertyV2>.Fail("Access.Forbidden"));
    }
    private sealed class VisualOffers:IAnnouncementStore
    {
        private static readonly PropertyAnnouncement[] Items=[new(OfferSale,Unit,PropertySale,Unit,"Venda","IM-123-V","Draft",1,false,null,new(),"fixture",BaseTime),new(OfferRental,Unit,PropertyRental,Unit,"Locação","IM-456-L","Draft",1,false,null,new(),"fixture",BaseTime)];
        public Task<IReadOnlyList<PropertyAnnouncement>> ListAnnouncementsAsync(AccessScope scope,Guid property,CancellationToken token)=>Task.FromResult<IReadOnlyList<PropertyAnnouncement>>(scope.OrganizationId==Unit&&scope.ActiveEstablishmentId==Unit?Items.Where(o=>o.PropertyId==property).ToArray():[]);
        public Task<Outcome<PropertyAnnouncement>> CreateAnnouncementAsync(AccessScope scope,Guid property,CreateAnnouncement command,CancellationToken token)=>Task.FromResult(Outcome<PropertyAnnouncement>.Fail("Access.Forbidden"));
        public Task<Outcome<PropertyAnnouncement>> UpdateAnnouncementAsync(AccessScope scope,Guid id,long version,AnnouncementInput? input,bool close,CancellationToken token)=>Task.FromResult(Outcome<PropertyAnnouncement>.Fail("Access.Forbidden"));
    }
    private sealed class VisualNegotiations:INegotiationStore
    {
        private static readonly Negotiation[] Items=[new(NegotiationSale,CustomerSale,PropertySale,OfferSale,Owner,Unit,"test-only-actor","Preparing",2,BaseTime,ResponsibleMembership),new(NegotiationRental,CustomerRental,PropertyRental,OfferRental,Owner,Unit,"second-actor","Preparing",1,BaseTime.AddHours(1),OtherMembership)];
        public Task<Negotiation?> FindNegotiationAsync(AccessScope scope,Guid id,CancellationToken token)=>Task.FromResult<Negotiation?>(scope.OrganizationId==Unit&&scope.ActiveEstablishmentId==Unit?Items.FirstOrDefault(n=>n.Id==id):null);
        public Task<NegotiationPage> ListAsync(AccessScope scope,Guid? customerId,int page,CancellationToken token){var rows=scope.OrganizationId==Unit&&scope.ActiveEstablishmentId==Unit?Items.Where(n=>customerId is null||n.CustomerId==customerId).ToArray():[];return Task.FromResult(new NegotiationPage(rows,rows.Length,page));}
        public Task<IReadOnlyList<NegotiationEvent>> NegotiationEventsAsync(AccessScope scope,Guid id,CancellationToken token)
        {
            if(scope.OrganizationId!=Unit||scope.ActiveEstablishmentId!=Unit||Items.All(n=>n.Id!=id))return Task.FromResult<IReadOnlyList<NegotiationEvent>>([]);
            var opened=new NegotiationEvent(Guid.Parse("58000000-0000-0000-0000-000000000001"),id,1,"Opened","test-only-actor",BaseTime);
            return Task.FromResult<IReadOnlyList<NegotiationEvent>>(id==NegotiationSale?[opened,new(Guid.Parse("58000000-0000-0000-0000-000000000002"),id,2,"ResponsibilityTransferred","supervisor",BaseTime.AddMinutes(40),"second-actor","test-only-actor","Redistribuição sintética")]:[opened]);
        }
        public Task<Outcome<Negotiation>> OpenAsync(AccessScope scope,OpenNegotiation input,string customerResponsibleSubject,NegotiationAssignee assignee,CancellationToken token)=>Task.FromResult(Outcome<Negotiation>.Fail("Access.Forbidden"));
        public Task<Outcome<Negotiation>> TransferAsync(AccessScope scope,Guid negotiationId,NegotiationTransfer input,NegotiationAssignee assignee,bool recovery,CancellationToken token)=>Task.FromResult(Outcome<Negotiation>.Fail("Access.Forbidden"));
    }

    private sealed class VisualActions:INegotiationActionStore
    {
        private readonly Dictionary<Guid,NegotiationAction> items=[];private readonly List<NegotiationActionEvent> events=[];private readonly Dictionary<Guid,Guid> requests=[];
        public Task<IReadOnlyList<NegotiationAction>> ListActionsAsync(AccessScope scope,Guid negotiationId,CancellationToken token)=>Task.FromResult<IReadOnlyList<NegotiationAction>>(scope.OrganizationId==Unit&&scope.ActiveEstablishmentId==Unit?items.Values.Where(x=>x.NegotiationId==negotiationId).OrderByDescending(x=>x.CreatedAt).ToArray():[]);
        public Task<IReadOnlyList<NegotiationActionEvent>> ActionEventsAsync(AccessScope scope,Guid negotiationId,CancellationToken token){var ids=items.Values.Where(x=>x.NegotiationId==negotiationId).Select(x=>x.Id).ToHashSet();return Task.FromResult<IReadOnlyList<NegotiationActionEvent>>(events.Where(x=>ids.Contains(x.ActionId)).ToArray());}
        public Task<Outcome<NegotiationAction?>> ExistingCreateRequestAsync(AccessScope scope,Guid negotiationId,NegotiationActionInput input,CancellationToken token)=>Task.FromResult(new Outcome<NegotiationAction?>((NegotiationAction?)null));
        public Task<Outcome<NegotiationAction>> CreateActionAsync(AccessScope scope,Negotiation negotiation,NegotiationActionInput input,NegotiationAssignee responsible,NegotiationActionAppointment? appointment,CancellationToken token)
        {
            if(requests.TryGetValue(input.RequestId,out var prior))return Task.FromResult(new Outcome<NegotiationAction>(items[prior]));
            var id=Guid.NewGuid();var item=new NegotiationAction(id,negotiation.Id,negotiation.CustomerId,negotiation.PropertyId,null,input.Kind,input.Description,input.DueAt,responsible.Subject,responsible.DisplayName,"Aberta",null,scope.Subject,DateTimeOffset.UtcNow,1,DateTimeOffset.UtcNow);
            items[id]=item;requests[input.RequestId]=id;events.Add(new(Guid.NewGuid(),id,1,"Criada",scope.Subject,responsible.Subject,input.Description,input.DueAt,null,DateTimeOffset.UtcNow));return Task.FromResult(new Outcome<NegotiationAction>(item));
        }
        public Task<Outcome<NegotiationActionCompletionResult>> CompleteActionAsync(AccessScope scope,Guid actionId,NegotiationActionCompletion input,CancellationToken token)
        {
            if(!items.TryGetValue(actionId,out var before))return Task.FromResult(Outcome<NegotiationActionCompletionResult>.Fail("Resource.NotFound"));
            var after=before with{Status="Concluída",Result=input.Result,Version=before.Version+1,UpdatedAt=DateTimeOffset.UtcNow};items[actionId]=after;events.Add(new(Guid.NewGuid(),actionId,after.Version,"Concluída",scope.Subject,after.ResponsibleSubject,after.Description,after.DueAt,input.Result,DateTimeOffset.UtcNow));return Task.FromResult(new Outcome<NegotiationActionCompletionResult>(new(after,null)));
        }
    }
    private sealed class VisualInteractions:ICommercialInteractionStore
    {
        private static readonly CommercialInteraction[] Items=[new(Guid.Parse("59000000-0000-0000-0000-000000000001"),Guid.Parse("59000000-0000-0000-0000-000000000001"),CustomerSale,PropertySale,Guid.Parse("59000000-0000-0000-0000-000000000002"),BaseTime.AddDays(-1),"Telefone","Demonstrou interesse",null,null,null,"test-only-actor","test-only-actor","IM-123","Casa Jardim","Maria Silva")];
        public Task<bool> CustomerExistsAsync(AccessScope scope,Guid customerId,CancellationToken token)=>Task.FromResult(scope.OrganizationId==Unit&&customerId==CustomerSale);
        public Task<bool> PropertyExistsAsync(AccessScope scope,Guid propertyId,CancellationToken token)=>Task.FromResult(scope.OrganizationId==Unit&&propertyId==PropertySale);
        public Task<bool> IsCaptureResponsibleAsync(AccessScope scope,Guid propertyId,CancellationToken token)=>Task.FromResult(false);
        public Task<IReadOnlyList<CommercialInteraction>> HistoryAsync(AccessScope scope,Guid? customerId,Guid? propertyId,CancellationToken token)=>Task.FromResult<IReadOnlyList<CommercialInteraction>>(scope.OrganizationId==Unit&&scope.ActiveEstablishmentId==Unit?Items.Where(i=>(customerId is null||i.CustomerId==customerId)&&(propertyId is null||i.PropertyId==propertyId)).ToArray():[]);
        public Task<Outcome<IReadOnlyList<CommercialInteraction>>> PresentAsync(AccessScope scope,Guid customerId,PresentationInput input,CancellationToken token)=>Task.FromResult(Outcome<IReadOnlyList<CommercialInteraction>>.Fail("Access.Forbidden"));
        public Task<Outcome<CommercialInteraction>> FeedbackAsync(AccessScope scope,Guid customerId,FeedbackInput input,CancellationToken token)=>Task.FromResult(Outcome<CommercialInteraction>.Fail("Access.Forbidden"));
        public Task<Outcome<CommercialSummary>> SummaryAsync(AccessScope scope,Guid propertyId,DateTimeOffset from,DateTimeOffset until,CancellationToken token)=>Task.FromResult(Outcome<CommercialSummary>.Fail("Access.Forbidden"));
    }
}
