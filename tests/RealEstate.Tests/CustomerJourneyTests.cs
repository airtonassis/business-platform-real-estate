using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using RealEstate.Application;
using RealEstate.Domain;
using RealEstate.Infrastructure;
using Xunit;
namespace RealEstate.Tests;

public sealed class CustomerTestDirectory:ICustomerDirectory,ICaptureDirectory
{
    public Task<Outcome<CaptureResponsibleDirectory>> ReadAsync(AccessScope scope,Guid unit,string? subject=null,CancellationToken token=default)=>Task.FromResult(new Outcome<CaptureResponsibleDirectory>(new(scope.OrganizationId,unit,new[]{new CaptureResponsible("test-only-actor","Atendente sintético",Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaa01"),"Atendimento",Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb1")),new CaptureResponsible("second-actor","Segundo responsável",Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaa02"),"Administrativo",Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb2"))}.Where(p=>subject is null||p.Subject==subject).ToArray())));
}
[Collection("PostgreSQL")]
public sealed class CustomerJourneyTests
{
    private sealed class Authority(AccessScope s):ICoreAccess {public bool Deny;public Task<Outcome<AccessScope>> RequireAsync(string r,string p,CancellationToken t)=>Task.FromResult(Deny?Outcome<AccessScope>.Fail("Access.Forbidden"):new Outcome<AccessScope>(s));}
    private static CustomerInput Input(string name="Cliente sintético")=>new(name,"Física","11999998888",null,"Indicação","Locação","test-only-actor");
    private static AccessScope Scope(){var unit=Guid.NewGuid();return new(Guid.NewGuid(),"test-only-actor","Sintética",[new(unit,"Filial sintética")],ActiveEstablishmentId:unit);}

    [Fact]public async Task Persistence_is_scoped_audited_versioned_and_history_is_immutable(){
        await using var ds=NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("ConnectionStrings__Erp")!);
        var scope=Scope();var authority=new Authority(scope);ICustomerStore store=new PostgresPropertyStore(ds);var service=new CustomerService(authority,store,new CustomerTestDirectory());
        var created=await service.SaveAsync(Input());Assert.True(created.IsSuccess,created.Code);var client=created.Value!;
        Assert.Equal("Atendente sintético",client.Data.ResponsibleName);
        Assert.Equal(client.Id,Assert.Single((await service.SearchAsync("(11) 99999-8888")).Value!.Items).Id);
        Assert.Equal("Customer.PossibleDuplicate",(await service.SaveAsync(Input())).Code);
        Assert.True((await service.SaveAsync(Input(),acknowledgeDuplicates:true)).IsSuccess);
        Assert.True((await service.SaveAsync(Input() with{Phone="11888887777"})).IsSuccess); // same name is allowed
        Assert.Equal("Customer.ResponsibleUnavailable",(await service.SaveAsync(Input() with{ResponsibleSubject="foreign-user"})).Code);
        var otherOrg=new CustomerService(new Authority(scope with{OrganizationId=Guid.NewGuid()}),store,new CustomerTestDirectory());Assert.Equal("Resource.NotFound",(await otherOrg.FindAsync(client.Id)).Code);Assert.Empty((await otherOrg.SearchAsync()).Value!.Items);
        var unit=Guid.NewGuid();var otherUnit=new CustomerService(new Authority(scope with{ActiveEstablishmentId=unit,Establishments=[new(unit,"Outra")] }),store,new CustomerTestDirectory());Assert.Equal("Resource.NotFound",(await otherUnit.AddContactAsync(client.Id,new("Telefone","Proibido"))).Code);Assert.Empty((await otherUnit.SearchAsync()).Value!.Items);
        authority.Deny=true;Assert.Equal("Access.Forbidden",(await service.AddContactAsync(client.Id,new("Telefone","Negado"))).Code);authority.Deny=false;
        var contact=await service.AddContactAsync(client.Id,new("WhatsApp","Contato inicial"));Assert.True(contact.IsSuccess,contact.Code);
        var changed=await service.SaveAsync(Input() with{ResponsibleSubject="second-actor"},client.Id,client.Version,true);Assert.True(changed.IsSuccess,changed.Code);
        Assert.Equal("Customer.VersionConflict",(await service.SaveAsync(Input(),client.Id,client.Version,true)).Code);
        Assert.True((await service.AddContactAsync(client.Id,new("Telefone","Novo atendimento"))).IsSuccess);
        var file=(await service.FindAsync(client.Id)).Value!;Assert.Equal(2,file.Contacts.Count);Assert.Contains(file.Contacts,c=>c.ResponsibleSubject=="test-only-actor");Assert.Contains(file.Contacts,c=>c.ResponsibleSubject=="second-actor");
        var change=Assert.Single(file.ResponsibilityHistory);Assert.Equal("test-only-actor",change.PreviousSubject);Assert.Equal("second-actor",change.NewSubject);Assert.Equal(scope.Subject,change.Actor);
        await using var c=await ds.OpenConnectionAsync();
        await using(var role=new NpgsqlCommand("SELECT rolsuper OR rolbypassrls FROM pg_roles WHERE rolname=current_user",c)){Assert.False((bool)(await role.ExecuteScalarAsync())!);}
        await using(var flags=new NpgsqlCommand("SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='real_estate' AND c.relname IN ('customers','customer_contacts','customer_responsibility_history') AND c.relrowsecurity AND c.relforcerowsecurity",c)){Assert.Equal(3L,await flags.ExecuteScalarAsync());}
        await using(var tx=await c.BeginTransactionAsync()){
            await using var settings=new NpgsqlCommand("SELECT set_config('erp.organization_id',@org,true),set_config('erp.active_establishment',@unit,true),set_config('erp.establishments',@units,true)",c,tx);settings.Parameters.AddWithValue("org",scope.OrganizationId.ToString());settings.Parameters.AddWithValue("unit",scope.ActiveEstablishmentId.ToString()!);settings.Parameters.AddWithValue("units","{"+scope.ActiveEstablishmentId+"}");await settings.ExecuteNonQueryAsync();
            await using var events=new NpgsqlCommand("SELECT count(*) FROM real_estate.events WHERE entity_id=@id",c,tx);events.Parameters.AddWithValue("id",client.Id);Assert.Equal(4L,await events.ExecuteScalarAsync());
            await using var mutate=new NpgsqlCommand("UPDATE real_estate.customer_contacts SET description='erased' WHERE customer_id=@id",c,tx);mutate.Parameters.AddWithValue("id",client.Id);var error=await Assert.ThrowsAsync<PostgresException>(()=>mutate.ExecuteNonQueryAsync());Assert.Equal("42501",error.SqlState);await tx.RollbackAsync();
        }
        await using(var bare=new NpgsqlCommand("SELECT count(*) FROM real_estate.customers",c)){Assert.Equal(0L,await bare.ExecuteScalarAsync());}
    }
    [Fact]public async Task Razor_journey_saves_reopens_and_records_contact_and_enforces_csrf(){
        await using var app=RealEstate.Web.Program.Build([],b=>{b.WebHost.UseTestServer();FixtureProgram.Configure(b);b.Services.RemoveAll<ICustomerDirectory>();b.Services.AddScoped<ICustomerDirectory,CustomerTestDirectory>();});await app.StartAsync();
        var http=app.GetTestClient();var unit=Guid.NewGuid();http.DefaultRequestHeaders.Add("X-Fixture-Unit",unit.ToString());
        var list=await http.GetStringAsync("/commercial/customers");Assert.Contains("Novo cliente",list);Assert.Contains("Comercial",list);Assert.DoesNotContain("href=\"/commercial/proposals",list);
        var form=await http.GetAsync("/commercial/customers/new");var html=await form.Content.ReadAsStringAsync();Assert.Equal(HttpStatusCode.OK,form.StatusCode);Assert.Contains("Atendente sintético",WebUtility.HtmlDecode(html));
        var csrf=Regex.Match(html,"name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;Assert.NotEmpty(csrf);
        var cookie=string.Join("; ",form.Headers.GetValues("Set-Cookie").Select(v=>v.Split(';')[0]));http.DefaultRequestHeaders.Add("Cookie",cookie);
        var fields=new Dictionary<string,string>{{"Input.Name","Cliente percurso real"},{"Input.PersonType","Física"},{"Input.Phone","11987654321"},{"Input.Origin","Site"},{"Input.Interest","Compra"},{"Input.ResponsibleSubject","test-only-actor"},{"__RequestVerificationToken",csrf}};
        var saved=await http.PostAsync("/commercial/customers/new",new FormUrlEncodedContent(fields));var savedHtml=WebUtility.HtmlDecode(await saved.Content.ReadAsStringAsync());Assert.True(saved.StatusCode==HttpStatusCode.Redirect,Regex.Match(savedHtml,"<div[^>]*validation-summary.*?</div>",RegexOptions.Singleline).Value+" HTTP "+saved.StatusCode);var path=saved.Headers.Location!.ToString();
        var reopen=await http.GetStringAsync(path);Assert.Contains("Cliente percurso real",reopen);csrf=Regex.Match(reopen,"name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        var contact=await http.PostAsync(path+"?handler=Contact",new FormUrlEncodedContent(new Dictionary<string,string>{{"ContactKind","Telefone"},{"Description","Atendimento persistido do percurso"},{"__RequestVerificationToken",csrf}}));Assert.Equal(HttpStatusCode.Redirect,contact.StatusCode);
        Assert.Contains("Atendimento persistido do percurso",await http.GetStringAsync(path));
        Assert.Equal(HttpStatusCode.OK,(await http.GetAsync("/api/v1/customers/"+path.Split('/').Last())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await http.PostAsync(path+"?handler=Contact",new FormUrlEncodedContent(new Dictionary<string,string>{{"ContactKind","Telefone"},{"Description","Sem CSRF"}}))).StatusCode);
        http.DefaultRequestHeaders.Add("X-Fixture-Deny","true");Assert.Equal(HttpStatusCode.Forbidden,(await http.GetAsync(path)).StatusCode);
        http.DefaultRequestHeaders.Remove("X-Fixture-Deny");http.DefaultRequestHeaders.Remove("X-Fixture-Unit");http.DefaultRequestHeaders.Add("X-Fixture-Unit",Guid.NewGuid().ToString());Assert.Equal(HttpStatusCode.NotFound,(await http.GetAsync(path)).StatusCode);
    }
    [Fact]public async Task Production_authentication_rejects_anonymous_customer_api(){
        await using var app=RealEstate.Web.Program.Build([],b=>b.WebHost.UseTestServer());await app.StartAsync();
        Assert.Equal(HttpStatusCode.Unauthorized,(await app.GetTestClient().GetAsync("/api/v1/customers")).StatusCode);
    }
    [Fact]public async Task Concurrent_edits_have_one_winner_and_audit_failure_rolls_back(){
        await using var ds=NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("ConnectionStrings__Erp")!);var scope=Scope();var service=new CustomerService(new Authority(scope),new PostgresPropertyStore(ds),new CustomerTestDirectory());
        var created=(await service.SaveAsync(Input())).Value!;
        var edits=await Task.WhenAll(service.SaveAsync(Input("First"),created.Id,1),service.SaveAsync(Input("Second"),created.Id,1));Assert.Single(edits,r=>r.IsSuccess);Assert.Single(edits,r=>r.Code=="Customer.VersionConflict");
        var version=(await service.FindAsync(created.Id)).Value!.Customer.Version;
        await using var admin=new NpgsqlConnection(Environment.GetEnvironmentVariable("ErpTestAdminConnection"));await admin.OpenAsync();
        try{
            await using(var revoke=new NpgsqlCommand("REVOKE INSERT ON real_estate.events FROM erp_runtime",admin))await revoke.ExecuteNonQueryAsync();
            await Assert.ThrowsAsync<PostgresException>(()=>service.SaveAsync(Input("Must rollback"),created.Id,version));
            await Assert.ThrowsAsync<PostgresException>(()=>service.AddContactAsync(created.Id,new("Telefone","Must rollback")));
        }finally{await using var restore=new NpgsqlCommand("GRANT INSERT ON real_estate.events TO erp_runtime",admin);await restore.ExecuteNonQueryAsync();}
        var file=(await service.FindAsync(created.Id)).Value!;Assert.Equal(version,file.Customer.Version);Assert.DoesNotContain("rollback",file.Customer.Data.Name);Assert.Empty(file.Contacts);
        var document=Input("Fiscal identity") with{Phone="11912340000",Document="529.982.247-25"};Assert.True((await service.SaveAsync(document)).IsSuccess);
        Assert.Equal("Customer.DocumentUnavailable",(await service.SaveAsync(document with{Phone="11912340001"})).Code);
        // Distinct telephone/subject, same name is never considered a duplicate.
        Assert.True((await service.SaveAsync(document with{Phone="11912340002",Document=null})).IsSuccess);
    }
}
