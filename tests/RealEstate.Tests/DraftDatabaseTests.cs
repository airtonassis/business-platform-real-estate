using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Npgsql;
using RealEstate.Application;
using RealEstate.Domain;
using RealEstate.Infrastructure;
using RealEstate.Web;
using Xunit;
namespace RealEstate.Tests;
[Collection("PostgreSQL")]
public sealed class DraftDatabaseTests
{
    private static NpgsqlDataSource Source()=>NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("ConnectionStrings__Erp")??throw new InvalidOperationException("Disposable database required"));
    private sealed class Grant(AccessScope scope):ICoreAccess
    {
        public AccessScope Scope=scope;public bool Denied;
        public Task<Outcome<AccessScope>> RequireAsync(string r,string p,CancellationToken t)=>Task.FromResult(Denied?Outcome<AccessScope>.Fail("Access.Forbidden"):new Outcome<AccessScope>(Scope));
    }
    private static AccessScope Scope(){var unit=Guid.NewGuid();return new(Guid.NewGuid(),"draft-author","Synthetic",[new(unit,"One")],ActiveEstablishmentId:unit);}
    [Fact] public async Task Minimum_draft_persists_reopens_versions_and_keeps_origin_after_context_switch()
    {
        await using var ds=Source();var grant=new Grant(Scope());var store=new PostgresPropertyStore(ds);var service=new PropertyServiceV2(grant,store);
        var result=await service.SaveAsync(new());Assert.True(result.IsSuccess,result.Code);var draft=result.Value!;
        Assert.NotEqual(Guid.Empty,draft.Id);Assert.Equal(grant.Scope.OrganizationId,draft.OrganizationId);Assert.Equal(grant.Scope.ActiveEstablishmentId,draft.Data.EstablishmentId);
        Assert.Null(draft.Data.OwnerId);Assert.Null(draft.Data.Title);Assert.Null(draft.Data.Price);Assert.Equal(1,draft.Version);Assert.Equal("Draft",draft.Status);
        var reopened=await new PropertyServiceV2(grant,new PostgresPropertyStore(ds)).FindAsync(draft.Id);Assert.Equal(draft,reopened.Value);
        var second=Guid.NewGuid();grant.Scope=grant.Scope with{ActiveEstablishmentId=second,Establishments=[..grant.Scope.Establishments!,new(second,"Two")]};
        var updated=await service.PatchAsync(draft.Id,1,JsonSerializer.SerializeToElement(new{title="Updated"}));Assert.True(updated.IsSuccess,updated.Code);Assert.Equal(draft.Data.EstablishmentId,updated.Value!.Data.EstablishmentId);
        var races=await Task.WhenAll(service.SaveAsync(updated.Value.Data with{Title="A"},draft.Id,2),service.SaveAsync(updated.Value.Data with{Title="B"},draft.Id,2));
        Assert.Single(races,x=>x.IsSuccess);Assert.Single(races,x=>x.Code=="Property.VersionConflict");
        await using var c=await ds.OpenConnectionAsync();await using var tx=await c.BeginTransactionAsync();
        await using var cmd=new NpgsqlCommand("SELECT set_config('erp.organization_id',@org,true),set_config('erp.establishments',@units,true)",c,tx);cmd.Parameters.AddWithValue("org",draft.OrganizationId.ToString());cmd.Parameters.AddWithValue("units","{"+draft.Data.EstablishmentId+"}");await cmd.ExecuteNonQueryAsync();
        await using var origin=new NpgsqlCommand("SELECT created_by,version,(SELECT count(*) FROM real_estate.events WHERE entity_id=@id) FROM real_estate.properties WHERE id=@id",c,tx);origin.Parameters.AddWithValue("id",draft.Id);
        await using var r=await origin.ExecuteReaderAsync();Assert.True(await r.ReadAsync());Assert.Equal("draft-author",r.GetString(0));Assert.Equal(3,r.GetInt64(1));Assert.Equal(3,r.GetInt64(2));
    }
    [Fact] public async Task Missing_active_context_forged_branch_identity_and_cross_tenant_access_are_rejected()
    {
        await using var ds=Source();var grant=new Grant(Scope());var service=new PropertyServiceV2(grant,new PostgresPropertyStore(ds));
        var valid=grant.Scope;grant.Scope=valid with{ActiveEstablishmentId=null};Assert.Equal("Context.ActiveEstablishmentRequired",(await service.SaveAsync(new())).Code);
        grant.Scope=valid with{Subject=""};Assert.Equal("Authentication.Required",(await service.SaveAsync(new())).Code);
        grant.Scope=valid;Assert.Equal("Access.Forbidden",(await service.SaveAsync(new(EstablishmentId:Guid.NewGuid()))).Code);
        var saved=(await service.SaveAsync(new())).Value!;
        grant.Scope=valid with{Establishments=[new(Guid.NewGuid(),"Foreign")],ActiveEstablishmentId=Guid.NewGuid()};Assert.Equal("Resource.NotFound",(await service.FindAsync(saved.Id)).Code);Assert.Equal("Resource.NotFound",(await service.SaveAsync(new(),saved.Id,1)).Code);
        grant.Scope=Scope();Assert.Equal("Resource.NotFound",(await service.FindAsync(saved.Id)).Code);
        grant.Scope=valid;grant.Denied=true;Assert.Equal("Access.Forbidden",(await service.FindAsync(saved.Id)).Code);
    }
    [Theory][InlineData("created_by='forged'")][InlineData("establishment_id='77777777-7777-7777-7777-777777777777'")][InlineData("price=0")][InlineData("owner_id='77777777-7777-7777-7777-777777777777'")]
    public async Task Database_rejects_mutated_origin_and_invalid_supplied_business_data(string mutation)
    {
        await using var ds=Source();var scope=Scope();var service=new PropertyServiceV2(new Grant(scope),new PostgresPropertyStore(ds));var saved=(await service.SaveAsync(new())).Value!;
        await using var c=await ds.OpenConnectionAsync();await using var tx=await c.BeginTransactionAsync();
        await using(var context=new NpgsqlCommand("SELECT set_config('erp.organization_id',@org,true),set_config('erp.establishments',@units,true)",c,tx)){
            context.Parameters.AddWithValue("org",scope.OrganizationId.ToString());context.Parameters.AddWithValue("units","{"+scope.ActiveEstablishmentId+",77777777-7777-7777-7777-777777777777}");await context.ExecuteNonQueryAsync();}
        await using var cmd=new NpgsqlCommand("UPDATE real_estate.properties SET "+mutation+" WHERE id=@id",c,tx);cmd.Parameters.AddWithValue("id",saved.Id);
        var error=await Assert.ThrowsAsync<PostgresException>(()=>cmd.ExecuteNonQueryAsync());Assert.Contains(error.SqlState,new[]{"23514","23503"});
    }
    [Fact] public async Task Razor_empty_form_saves_and_reopens_against_real_database()
    {
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../backend/src/RealEstate.Web"));
        await using var app=Program.Build(["--contentRoot",root],b=>{FixtureProgram.Configure(b);b.WebHost.UseTestServer();});await app.StartAsync();using var client=app.GetTestClient();
        var before=(await client.GetFromJsonAsync<WorkspaceV2>("/api/v2/properties"))!.Properties.Select(x=>x.Id).ToHashSet();
        var page=await client.GetAsync("/properties/new");var html=await page.Content.ReadAsStringAsync();
        var token=System.Text.RegularExpressions.Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;Assert.NotEmpty(token);
        client.DefaultRequestHeaders.Add("Cookie",string.Join("; ",page.Headers.GetValues("Set-Cookie").Select(x=>x.Split(';')[0])));
        var posted=await client.PostAsync("/properties/new",new FormUrlEncodedContent(new Dictionary<string,string>{{"__RequestVerificationToken",token},{"Version","0"}}));
        Assert.Equal(HttpStatusCode.Redirect,posted.StatusCode);
        var after=(await client.GetFromJsonAsync<WorkspaceV2>("/api/v2/properties"))!.Properties;var saved=Assert.Single(after,x=>!before.Contains(x.Id));
        Assert.Null(saved.Data.Title);Assert.Null(saved.Data.OwnerId);Assert.Null(saved.Data.Price);Assert.Equal("Draft",saved.Status);
        var reopened=await client.GetAsync("/properties/"+saved.Id);Assert.Equal(HttpStatusCode.OK,reopened.StatusCode);Assert.Contains("property-form",await reopened.Content.ReadAsStringAsync());
    }
    [Fact] public async Task Real_http_minimum_draft_legacy_conflict_and_nested_patch_preserve_data()
    {
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../backend/src/RealEstate.Web"));
        await using var app=Program.Build(["--contentRoot",root],b=>{FixtureProgram.Configure(b);b.WebHost.UseTestServer();});await app.StartAsync();using var client=app.GetTestClient();
        var response=await client.PostAsJsonAsync("/api/v2/properties",new{createdBy="forged",organizationId=Guid.NewGuid()});Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var saved=(await response.Content.ReadFromJsonAsync<PropertyV2>())!;Assert.Equal(FixtureProgram.OrganizationA,saved.OrganizationId);
        var one=await client.PatchAsJsonAsync("/api/v2/properties/"+saved.Id,new{version=1,data=new{address=new{municipality="Rural",withoutNumber=true}}});Assert.Equal(HttpStatusCode.OK,one.StatusCode);
        var two=await client.PatchAsJsonAsync("/api/v2/properties/"+saved.Id,new{version=2,data=new{title="Rural draft",address=new{state="SP"}}});Assert.Equal(HttpStatusCode.OK,two.StatusCode);
        var reopened=(await client.GetFromJsonAsync<PropertyV2>("/api/v2/properties/"+saved.Id))!;Assert.Equal("Rural",reopened.Data.Address!.Municipality);Assert.Equal("SP",reopened.Data.Address.State);Assert.True(reopened.Data.Address.WithoutNumber);Assert.Null(reopened.Data.Price);
        Assert.Equal(HttpStatusCode.Conflict,(await client.GetAsync("/api/v1/properties/"+saved.Id)).StatusCode);
        client.DefaultRequestHeaders.Add("X-Fixture-Deny","true");Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync("/api/v1/properties/"+saved.Id)).StatusCode);
    }
}
