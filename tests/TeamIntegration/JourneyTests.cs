extern alias CoreApi;
using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Business.Platform.Core.Application.Tenancy;
using Business.Platform.Core.Domain.Shared;
using Business.Platform.Core.Domain.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.Authentication;
using Npgsql;
using RealEstate.Application;
using RealEstate.Domain;
using RealEstate.Infrastructure;
using RealEstate.Web;
using RealEstate.Tests;
using Xunit;
namespace TeamIntegration;

// Real Core endpoint/application code; synthetic identity and Core persistence facts only.
public sealed class CoreFacts:IAuthenticatedSubject,IEnterpriseContextReader,IEnterpriseDirectoryStore,IBusinessAccessStore,IMemberEstablishmentStore
{
 public readonly Guid Org=Guid.NewGuid(),Unit=Guid.NewGuid(),HiddenUnit=Guid.NewGuid(),Broker=Guid.NewGuid(),Other=Guid.NewGuid(),Profile=Guid.NewGuid(),BrokerMember=Guid.NewGuid(),OtherMember=Guid.NewGuid();
 public string CallerRole="Owner";public readonly string TargetRole="Member";public int Writes;
 public bool IsProvisioned=>true;
 public OidcSubject? GetSubject()=>new("https://synthetic.invalid","test-only-actor");
 public Task<Result<EnterpriseContextSnapshot>> ReadAsync(OidcSubject subject,CancellationToken token)
 {var identity=Guid.NewGuid();var account=Guid.NewGuid();return Task.FromResult(Result.Success(new EnterpriseContextSnapshot(identity,new(BindingHistoryState.Consumed,Org),[new(identity,account,Org,account,Org,true,true,true,CallerRole,"Synthetic",Guid.NewGuid())])));}
 public Task<Result<EnterpriseDirectoryWorkspace>> ExecuteAsync(OidcSubject actor,EnterpriseDirectoryCommand command,CancellationToken token)
 {if(command.Action!="Read"){Writes++;throw new InvalidOperationException("No identity writes permitted");}return Task.FromResult(Result.Success(new EnterpriseDirectoryWorkspace(null,[new(Broker,"broker@corp","Corretor comum","private@test.invalid",Profile,[Unit,HiddenUnit],BrokerMember,"ordinary-broker","Active",1,null,true),new(Other,"other@corp","Outro corretor","other@test.invalid",Profile,[Unit],OtherMember,"other-broker","Active",1,null,true)],[])));}
 public Task<Result<bool>> CompleteAsync(OidcSubject actor,Guid id,string subject,CancellationToken token){Writes++;throw new InvalidOperationException();}
 public Task<Result<BusinessAccessWorkspace>> ManageAsync(OidcSubject actor,Guid org,BusinessAccessCommand command,CancellationToken token)
 {Assert.Equal(Org,org);Assert.Equal("Read",command.Action);return Task.FromResult(Result.Success(new BusinessAccessWorkspace([new("real-estate","Imob",true,null,null,[])],[])));}
 public Task<Result<IReadOnlyList<EffectiveProductGrant>>> EffectiveAsync(OidcSubject actor,Guid? member,CancellationToken token)
 {Assert.Contains(member!.Value,new[]{BrokerMember,OtherMember});return Task.FromResult(Result.Success<IReadOnlyList<EffectiveProductGrant>>([new("real-estate","customers","CUSTOMER_RESPONSIBLE")]));}
 public Task<Result<MemberEstablishmentWorkspace>> ExecuteAsync(OidcSubject actor,Guid? member,bool save,Guid[] ids,long version,CancellationToken token)
 {Assert.False(save);return Task.FromResult(Result.Success(new MemberEstablishmentWorkspace(member??Guid.NewGuid(),1,member is null?[new(Unit,"Matriz",true,true,true)]:[new(Unit,"Matriz",true,true,true),new(HiddenUnit,"Oculta",false,true,true)])));}
}
public sealed class Authority(CoreFacts facts):ICoreAccess
{
 public bool Manager=true;public bool Denied;
 public AccessScope Scope=>new(facts.Org,"test-only-actor","Synthetic",[new(facts.Unit,"Matriz")],Manager,facts.Unit);
 public Task<Outcome<AccessScope>> RequireAsync(string resource,string permission,CancellationToken token){var http=new HttpContextAccessor().HttpContext;if(http is not null)http.Items[typeof(AccessScope)]=Scope;return Task.FromResult(Denied?Outcome<AccessScope>.Fail("Access.Forbidden"):new Outcome<AccessScope>(Scope));}
}
public sealed class TeamAuthentication(Microsoft.Extensions.Options.IOptionsMonitor<AuthenticationSchemeOptions> options,Microsoft.Extensions.Logging.ILoggerFactory logger,System.Text.Encodings.Web.UrlEncoder encoder):AuthenticationHandler<AuthenticationSchemeOptions>(options,logger,encoder)
{
 protected override Task<AuthenticateResult> HandleAuthenticateAsync(){var properties=new AuthenticationProperties();properties.StoreTokens([new AuthenticationToken{Name="access_token",Value="synthetic-only"}]);return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new(new System.Security.Claims.ClaimsIdentity([new("sub","test-only-actor")],Scheme.Name)),properties,Scheme.Name)));}
}
public sealed class JourneyTests
{
 [Fact]public async Task Administrator_links_reopens_and_assigns_ordinary_broker_using_current_Core_endpoint_and_PostgreSQL()
 {
  var builder=WebApplication.CreateBuilder();builder.WebHost.UseTestServer();builder.Services.AddHttpContextAccessor();
  CoreApi::Business.Platform.Core.Api.TenantMembershipEndpoints.AddServices(builder.Services,builder.Configuration);
  var facts=new CoreFacts();builder.Services.AddSingleton<IAuthenticatedSubject>(facts);builder.Services.AddSingleton<IEnterpriseContextReader>(facts);builder.Services.AddScoped<ResolveEnterpriseContext>();builder.Services.RemoveAll<IEnterpriseDirectoryStore>();builder.Services.AddSingleton<IEnterpriseDirectoryStore>(facts);builder.Services.RemoveAll<IBusinessAccessStore>();builder.Services.AddSingleton<IBusinessAccessStore>(facts);builder.Services.RemoveAll<IMemberEstablishmentStore>();builder.Services.AddSingleton<IMemberEstablishmentStore>(facts);
  builder.Services.AddAuthentication("TeamTest").AddScheme<AuthenticationSchemeOptions,TeamAuthentication>("TeamTest",_=>{});builder.Services.AddAuthorization();
  await using var core=builder.Build();core.UseAuthentication();core.UseAuthorization();CoreApi::Business.Platform.Core.Api.TenantMembershipEndpoints.Map(core);await core.StartAsync();
  using(var probe=core.GetTestClient()){var response=await probe.GetAsync($"/api/v1/enterprise-directory/team?establishmentId={facts.Unit}&productKey=real-estate");Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.Equal("no-store",response.Headers.CacheControl?.ToString());}
  var authority=new Authority(facts);
  await using var erp=RealEstate.Web.Program.Build([],b=>{
   b.WebHost.UseTestServer();FixtureProgram.Configure(b);b.Services.RemoveAll<ICoreAccess>();b.Services.AddSingleton<ICoreAccess>(authority);
   b.Services.AddAuthentication(o=>{o.DefaultAuthenticateScheme="TeamTest";o.DefaultScheme="TeamTest";o.DefaultChallengeScheme="TeamTest";}).AddScheme<AuthenticationSchemeOptions,TeamAuthentication>("TeamTest",_=>{});
   b.Services.RemoveAll<ITeamDirectory>();b.Services.AddScoped<ITeamDirectory>(sp=>new CoreTeamDirectory(new HttpClient(core.GetTestServer().CreateHandler()){BaseAddress=new("https://core.test/")},sp.GetRequiredService<IHttpContextAccessor>()));
   b.Services.RemoveAll<ICustomerDirectory>();b.Services.AddScoped<ICustomerDirectory>(sp=>new CoreCustomerDirectory(new HttpClient(core.GetTestServer().CreateHandler()){BaseAddress=new("https://core.test/")},sp.GetRequiredService<IHttpContextAccessor>()));
  });await erp.StartAsync();using var client=erp.GetTestClient();client.BaseAddress=new("https://erp.test/");client.DefaultRequestHeaders.Authorization=new("Bearer","synthetic-only");
  var listing=await client.GetAsync("/administration/team?Query=broker@corp");Assert.Equal(HttpStatusCode.OK,listing.StatusCode);var listingHtml=await listing.Content.ReadAsStringAsync();Assert.Contains("broker@corp",listingHtml);Assert.Contains("Equipe e Colaboradores",listingHtml);Assert.DoesNotContain("Oculta",listingHtml);
  var path="/administration/team/"+facts.Broker;var page=await client.GetAsync(path);Assert.Equal(HttpStatusCode.OK,page.StatusCode);var html=await page.Content.ReadAsStringAsync();Assert.Contains(facts.Profile.ToString(),html);Assert.Contains("CUSTOMER_RESPONSIBLE",html);
  var csrf=Regex.Match(html,"name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;Assert.NotEmpty(csrf);client.DefaultRequestHeaders.Add("Cookie",string.Join("; ",page.Headers.GetValues("Set-Cookie").Select(v=>v.Split(';')[0])));
  var form=new Dictionary<string,string>{{"__RequestVerificationToken",csrf},{"Version","0"},{"Input.JobTitle","Corretor"},{"Input.Creci","45321"},{"Input.CreciUf","SP"},{"Input.Areas","Locação"},{"Input.Status","Ativo"},{"Input.Notes","Atuação residencial"}};
  Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsync(path,new FormUrlEncodedContent(new Dictionary<string,string>{{"Input.JobTitle","Forged"}}))).StatusCode);
  var saved=await client.PostAsync(path,new FormUrlEncodedContent(form));Assert.Equal(HttpStatusCode.Found,saved.StatusCode);
  var reopened=await client.GetStringAsync(path);Assert.Contains("45321",reopened);Assert.Contains("Atua",reopened);
  var file=(await client.GetFromJsonAsync<TeamFile>("/api/v1/team/"+facts.Broker))!;Assert.Equal("Corretor",file.Professional!.Data.JobTitle);Assert.Equal("SP",file.Professional.Data.CreciUf);Assert.Equal(1,file.Professional.Version);
  var input=new CustomerInput("Team journey","Física","11912345678",null,"Site","Locação","other-broker");
  var created=await client.PostAsJsonAsync("/api/v1/customers",new CustomerEndpoints.SaveCustomer(input));Assert.Equal(HttpStatusCode.OK,created.StatusCode);var customer=(await created.Content.ReadFromJsonAsync<Customer>())!;
  var assign=await client.PutAsJsonAsync("/api/v1/customers/"+customer.Id,new CustomerEndpoints.SaveCustomer(input with{ResponsibleSubject="ordinary-broker"},customer.Version));Assert.Equal(HttpStatusCode.OK,assign.StatusCode);
  var contact=await client.PostAsJsonAsync($"/api/v1/customers/{customer.Id}/contacts",new ContactInput("Telefone","Retorno ao cliente"));Assert.Equal(HttpStatusCode.OK,contact.StatusCode);
  var customerFile=(await client.GetFromJsonAsync<CustomerFile>("/api/v1/customers/"+customer.Id))!;Assert.Equal("ordinary-broker",customerFile.Customer.Data.ResponsibleSubject);var history=Assert.Single(customerFile.ResponsibilityHistory);Assert.Equal("other-broker",history.PreviousSubject);Assert.Equal("ordinary-broker",history.NewSubject);Assert.Equal("ordinary-broker",Assert.Single(customerFile.Contacts).ResponsibleSubject);
  Assert.Equal("Member",facts.TargetRole);Assert.Equal(0,facts.Writes);Assert.Equal("CUSTOMER_RESPONSIBLE",Assert.Single(file.Person.Permissions).PermissionKey);
  var professional=file.Professional.Data;
  var conflict=await client.PutAsJsonAsync("/api/v1/team/"+facts.Broker,new TeamEndpoints.SaveTeam(professional,0));Assert.Equal(HttpStatusCode.PreconditionFailed,conflict.StatusCode);
  var edited=await client.PutAsJsonAsync("/api/v1/team/"+facts.Broker,new TeamEndpoints.SaveTeam(professional with{Notes="Editada"},1));Assert.Equal(HttpStatusCode.OK,edited.StatusCode);Assert.Equal("Editada",(await client.GetFromJsonAsync<TeamFile>("/api/v1/team/"+facts.Broker))!.Professional!.Data.Notes);
  // Both the Imob guard and actual Core use case deny a non-administrative caller.
  authority.Manager=false;Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync(path)).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await client.PutAsJsonAsync("/api/v1/team/"+facts.Broker,new TeamEndpoints.SaveTeam(professional,2))).StatusCode);authority.Manager=true;
  facts.CallerRole="Member";Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync(path)).StatusCode);facts.CallerRole="Owner";
  authority.Denied=true;Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync(path)).StatusCode);authority.Denied=false;
  await using var ds=NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("ConnectionStrings__Erp")!);ITeamStore store=new PostgresPropertyStore(ds);Assert.Null(await store.FindAsync(authority.Scope with{OrganizationId=Guid.NewGuid()},facts.Broker,default));var foreignUnit=Guid.NewGuid();Assert.Null(await store.FindAsync(authority.Scope with{ActiveEstablishmentId=foreignUnit,Establishments=[new(foreignUnit,"Other")]},facts.Broker,default));Assert.Null(await store.FindAsync(authority.Scope with{LegacyManager=false},facts.Broker,default));
  await using(var c=await ds.OpenConnectionAsync()){await using var bare=new NpgsqlCommand("SELECT count(*) FROM real_estate.team_professionals",c);Assert.Equal(0L,await bare.ExecuteScalarAsync());}
  // A failed audit must roll back the professional update atomically.
  await using(var admin=new NpgsqlConnection(Environment.GetEnvironmentVariable("ErpTestAdminConnection"))){
   await admin.OpenAsync();
   try{await using(var revoke=new NpgsqlCommand("REVOKE INSERT ON real_estate.events FROM erp_runtime",admin))await revoke.ExecuteNonQueryAsync();await Assert.ThrowsAsync<PostgresException>(()=>store.SaveAsync(authority.Scope,facts.Broker,professional with{Notes="Must roll back"},2,default));}
   finally{await using var restore=new NpgsqlCommand("GRANT INSERT ON real_estate.events TO erp_runtime",admin);await restore.ExecuteNonQueryAsync();}
   Assert.Equal("Editada",(await store.FindAsync(authority.Scope,facts.Broker,default))!.Data.Notes);
  }
  await using(var admin=new NpgsqlConnection(Environment.GetEnvironmentVariable("ErpTestAdminConnection"))){await admin.OpenAsync();await using var audit=new NpgsqlCommand("SELECT count(*) FROM real_estate.events WHERE organization_id=@org AND entity_id=@id AND event_type IN ('TeamProfessionalLinked','TeamProfessionalUpdated')",admin);audit.Parameters.AddWithValue("org",facts.Org);audit.Parameters.AddWithValue("id",facts.Broker);Assert.Equal(2L,await audit.ExecuteScalarAsync());}
 }
}


