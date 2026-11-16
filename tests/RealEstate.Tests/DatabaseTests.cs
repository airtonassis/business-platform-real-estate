using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using RealEstate.Application;
using RealEstate.Domain;
using RealEstate.Infrastructure;
using RealEstate.Web;
using Xunit;
namespace RealEstate.Tests;

// Kestrel integration is exercised by the fixture browser runner. These tests call
// the same application/store against real PostgreSQL with an explicit test-only authority.
[Collection("PostgreSQL")]
public sealed class DatabaseTests
{
    private static NpgsqlDataSource Source()=>NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("ConnectionStrings__Erp")??throw new InvalidOperationException("Run scripts/validate_local.py --execute-local."));
    private sealed class Grant(AccessScope scope):ICoreAccess {public bool Denied;public Task<Outcome<AccessScope>> RequireAsync(string r,string p,CancellationToken t)=>Task.FromResult(Denied?Outcome<AccessScope>.Fail("Access.Forbidden"):new Outcome<AccessScope>(scope with{ActiveEstablishmentId=scope.Establishments?.FirstOrDefault()?.Id}));}
    private static PropertyInput Valid(Guid owner,string? code=null)=>new(code??Guid.NewGuid().ToString("N"),"Casa de teste",owner,"Residencial","Casa","Venda","Rua Central, 10","01310-100",-23.55m,-46.63m,350000m);
    [Fact] public async Task Persist_edit_reopen_and_audit_with_restricted_login()
    {
        await using var ds=Source();var scope=new AccessScope(Guid.NewGuid(),"test-subject","Test",[new(Guid.NewGuid(),"Test unit")]);var grant=new Grant(scope);var service=new PropertyService(grant,new PostgresPropertyStore(ds));
        var owner=await service.CreateOwnerAsync("Proprietário de teste");Assert.True(owner.IsSuccess);
        var created=await service.SaveAsync(Valid(owner.Value!.Id));Assert.True(created.IsSuccess,created.Code);Assert.Equal("Draft",created.Value!.Status);
        var reopened=await new PropertyService(grant,new PostgresPropertyStore(ds)).FindAsync(created.Value.Id);Assert.Equal(created.Value!.Id,reopened.Value!.Id);Assert.Equal(created.Value.Data.EstablishmentId,reopened.Value.Data.EstablishmentId);
        var changed=await service.SaveAsync(created.Value.Data with {Title="Casa atualizada"},created.Value.Id,1);Assert.True(changed.IsSuccess);Assert.Equal(2,changed.Value!.Version);
        var stale=await service.SaveAsync(created.Value.Data,created.Value.Id,1);Assert.Equal("Property.VersionConflict",stale.Code);
        await using var c=await ds.OpenConnectionAsync();await using var tx=await c.BeginTransactionAsync();
        await using var cmd=new NpgsqlCommand("SELECT set_config('erp.organization_id',@org,true); SELECT count(*) FILTER(WHERE event_type IN ('PropertyCreated','PropertyUpdated')),count(*) FILTER(WHERE event_type='CommercialCodeAssigned') FROM real_estate.events WHERE entity_id=@id",c,tx);cmd.Parameters.AddWithValue("org",scope.OrganizationId.ToString());cmd.Parameters.AddWithValue("id",created.Value.Id);
        await using var reader=await cmd.ExecuteReaderAsync();await reader.NextResultAsync();await reader.ReadAsync();Assert.Equal(2,reader.GetInt64(0));Assert.Equal(1,reader.GetInt64(1));
    }
    [Fact] public async Task Foreign_organization_owner_and_record_are_denied_and_code_is_unique_per_org()
    {
        await using var ds=Source();var store=new PostgresPropertyStore(ds);
        var a=new PropertyService(new Grant(new(Guid.NewGuid(),"A","A",[new(Guid.NewGuid(),"A unit")])),store);var b=new PropertyService(new Grant(new(Guid.NewGuid(),"B","B",[new(Guid.NewGuid(),"B unit")])),store);
        var oa=await a.CreateOwnerAsync("A");var ob=await b.CreateOwnerAsync("B");var input=Valid(oa.Value!.Id);
        var saved=await a.SaveAsync(input);Assert.True(saved.IsSuccess);
        Assert.Equal("Resource.NotFound",(await b.FindAsync(saved.Value!.Id)).Code);
        Assert.Empty((await b.ListAsync()).Value!.Properties);
        Assert.Equal("Property.OwnerUnavailable",(await b.SaveAsync(input)).Code);
        Assert.Equal("Property.DuplicateCode",(await a.SaveAsync(input)).Code);
        Assert.True((await b.SaveAsync(input with {OwnerId=ob.Value!.Id})).IsSuccess);
        Assert.Equal("Resource.NotFound",(await b.SaveAsync(input with {OwnerId=ob.Value.Id},saved.Value.Id,1)).Code);
    }
    [Fact] public async Task Revoked_authority_blocks_next_operation_even_outside_http()
    {
        await using var ds=Source();var authority=new Grant(new(Guid.NewGuid(),"test","test",[new(Guid.NewGuid(),"Test unit")]));var service=new PropertyService(authority,new PostgresPropertyStore(ds));
        var owner=await service.CreateOwnerAsync("Owner");var saved=await service.SaveAsync(Valid(owner.Value!.Id));Assert.True(saved.IsSuccess);
        authority.Denied=true;Assert.Equal("Access.Forbidden",(await service.ListAsync()).Code);Assert.Equal("Access.Forbidden",(await service.FindAsync(saved.Value!.Id)).Code);Assert.Equal("Access.Forbidden",(await service.SaveAsync(saved.Value.Data)).Code);Assert.Equal("Access.Forbidden",(await service.CreateOwnerAsync("Another")).Code);
    }
    [Fact] public async Task Runtime_cannot_bypass_rls_or_change_audit()
    {
        await using var ds=Source();await using var c=await ds.OpenConnectionAsync();
        await using(var cmd=new NpgsqlCommand("SELECT rolsuper OR rolbypassrls FROM pg_roles WHERE rolname=current_user",c))Assert.False((bool)(await cmd.ExecuteScalarAsync())!);
        await using(var cmd=new NpgsqlCommand("SELECT count(*) FROM real_estate.properties",c))Assert.Equal(0L,await cmd.ExecuteScalarAsync());
        await using(var cmd=new NpgsqlCommand("SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='real_estate' AND c.relname IN ('owners','properties','events') AND c.relrowsecurity AND c.relforcerowsecurity",c))Assert.Equal(3L,await cmd.ExecuteScalarAsync());
        await using(var cmd=new NpgsqlCommand("SELECT has_table_privilege(current_user,'real_estate.events','UPDATE') OR has_table_privilege(current_user,'real_estate.events','DELETE')",c))Assert.False((bool)(await cmd.ExecuteScalarAsync())!);
    }
    [Fact] public async Task Same_organization_foreign_establishment_is_denied_and_revocation_is_immediate()
    {
        await using var ds=Source();var org=Guid.NewGuid();var one=Guid.NewGuid();var two=Guid.NewGuid();
        var scope=new AccessScope(org,"branch-test","Test",[new(one,"One")]);var store=new PostgresPropertyStore(ds);var service=new PropertyService(new Grant(scope),store);
        var owner=await service.CreateOwnerAsync("Branch owner");var saved=await service.SaveAsync(Valid(owner.Value!.Id));Assert.True(saved.IsSuccess);
        var foreignScope=scope with{Establishments=[new(two,"Two")]};var foreign=new PropertyService(new Grant(foreignScope),store);
        Assert.Equal("Resource.NotFound",(await foreign.FindAsync(saved.Value!.Id)).Code);Assert.Empty((await foreign.ListAsync()).Value!.Properties);
        Assert.Equal("Access.Forbidden",(await foreign.SaveAsync(Valid(owner.Value.Id) with{EstablishmentId=one})).Code);
        Assert.Null(await store.FindAsync(foreignScope,saved.Value.Id,default));
        var empty=scope with{Establishments=[]};Assert.Empty((await store.ListAsync(empty,default)).Properties);
    }
    [Fact] public async Task Audit_failure_rolls_back_property_write()
    {
        await using var ds=Source();var service=new PropertyService(new Grant(new(Guid.NewGuid(),"rollback-test","test",[new(Guid.NewGuid(),"Test unit")])),new PostgresPropertyStore(ds));
        var owner=await service.CreateOwnerAsync("Rollback owner");Assert.True(owner.IsSuccess);
        await using var admin=new NpgsqlConnection(Environment.GetEnvironmentVariable("ErpTestAdminConnection"));await admin.OpenAsync();
        try
        {
            await using(var revoke=new NpgsqlCommand("REVOKE INSERT ON real_estate.events FROM erp_runtime",admin))await revoke.ExecuteNonQueryAsync();
            var failure=await Assert.ThrowsAsync<PostgresException>(()=>service.SaveAsync(Valid(owner.Value!.Id)));
            Assert.Equal("42501",failure.SqlState);
        }
        finally {await using var restore=new NpgsqlCommand("GRANT INSERT ON real_estate.events TO erp_runtime",admin);await restore.ExecuteNonQueryAsync();}
        Assert.Empty((await service.ListAsync()).Value!.Properties);
    }}

