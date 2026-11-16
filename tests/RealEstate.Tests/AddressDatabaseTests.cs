using Npgsql;
using RealEstate.Domain;
using RealEstate.Application;
using RealEstate.Infrastructure;
using Xunit;
namespace RealEstate.Tests;

[CollectionDefinition("PostgreSQL",DisableParallelization=true)]
public sealed class PostgreSqlCollection { }

[Collection("PostgreSQL")]
public sealed class AddressDatabaseTests
{
    private static NpgsqlDataSource Source()=>NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("ConnectionStrings__Erp")??throw new InvalidOperationException("Run scripts/end01_database.py --execute"));
    private sealed class Grant(AccessScope scope):ICoreAccess
    {
        public bool Denied;
        public Task<Outcome<AccessScope>> RequireAsync(string resource,string permission,CancellationToken token)=>Task.FromResult(Denied?Outcome<AccessScope>.Fail("Access.Forbidden"):new Outcome<AccessScope>(scope with{ActiveEstablishmentId=scope.Establishments?.FirstOrDefault()?.Id}));
    }
    private static AccessScope Scope()=>new(Guid.NewGuid(),"address-test","Synthetic",[new(Guid.NewGuid(),"Unit")]);
    private static PropertyInputV2 Input(Guid owner,string? code=null,StructuredAddress? address=null)=>new(code??Guid.NewGuid().ToString("N"),"Synthetic rural property",owner,"Rural","Fazenda","Venda",3000,address);
    private static PropertyInput Legacy(Guid owner,string code)=>new(code,"Legacy",owner,"Residencial","Casa","Venda","  Unparsed legacy text  ","01000000",0,0,3000);

    [Fact] public async Task Partial_persists_and_v1_checks_after_authorization_and_rls()
    {
        await using var ds=Source();var scope=Scope();var grant=new Grant(scope);var store=new PostgresPropertyStore(ds);
        var v1=new PropertyService(grant,store);var v2=new PropertyServiceV2(grant,store);
        var owner=(await v1.CreateOwnerAsync("Synthetic")).Value!;
        var saved=await v2.SaveAsync(Input(owner.Id));Assert.True(saved.IsSuccess,saved.Code);
        var read=await v2.FindAsync(saved.Value!.Id);Assert.Null(read.Value!.Data.Address!.PostalCode);Assert.Null(read.Value.Data.Address.Latitude);Assert.Equal("Draft",read.Value.Status);
        Assert.Equal("Property.AddressVersionRequired",(await v1.FindAsync(saved.Value.Id)).Code);
        Assert.DoesNotContain((await v1.ListAsync()).Value!.Properties,p=>p.Id==saved.Value.Id);
        grant.Denied=true;Assert.Equal("Access.Forbidden",(await v1.FindAsync(saved.Value.Id)).Code);grant.Denied=false;
        var foreign=new PropertyService(new Grant(Scope()),store);Assert.Equal("Resource.NotFound",(await foreign.FindAsync(saved.Value.Id)).Code);
        var otherUnit=scope with{Establishments=[new(Guid.NewGuid(),"Other")]};
        Assert.Equal("Resource.NotFound",(await new PropertyServiceV2(new Grant(otherUnit),store).FindAsync(saved.Value.Id)).Code);
    }
    [Fact] public async Task V1_filters_before_limit_and_never_serializes_incomplete_data()
    {
        await using var ds=Source();var scope=Scope();var store=new PostgresPropertyStore(ds);var grant=new Grant(scope);
        var v1=new PropertyService(grant,store);var v2=new PropertyServiceV2(grant,store);var owner=(await v1.CreateOwnerAsync("Owner")).Value!;
        var legacy=(await v1.SaveAsync(Legacy(owner.Id,"BEFORE-PARTIALS"))).Value!;
        for(var i=0;i<105;i++)Assert.True((await v2.SaveAsync(Input(owner.Id,"PARTIAL-"+i))).IsSuccess);
        var list=(await v1.ListAsync()).Value!;Assert.Single(list.Properties);Assert.Equal(legacy.Id,list.Properties[0].Id);
        Assert.Equal(100,(await v2.ListAsync()).Value!.Properties.Count);
    }
    [Fact] public async Task Structured_roundtrip_and_legacy_update_preserve_unknown_fields()
    {
        await using var ds=Source();var scope=Scope();var store=new PostgresPropertyStore(ds);var grant=new Grant(scope);
        var v1=new PropertyService(grant,store);var v2=new PropertyServiceV2(grant,store);var owner=(await v1.CreateOwnerAsync("Owner")).Value!;
        var old=(await v1.SaveAsync(Legacy(owner.Id,"ROUNDTRIP"))).Value!;
        var first=(await v2.FindAsync(old.Id)).Value!;Assert.Null(first.Data.Address!.Street);
        var a=first.Data.Address with{MunicipalityIbgeCode="3550308",State="sp",Municipality="São Paulo",Street="Road",WithoutNumber=true,Complement="Rural access",Neighborhood="District",Country="br",CoordinateSource="Manual"};
        var update=await v2.SaveAsync(first.Data with{Address=a},old.Id,first.Version);Assert.True(update.IsSuccess,update.Code);
        var legacy=(await v1.FindAsync(old.Id)).Value!;Assert.Equal(old.Data.Address,legacy.Data.Address);
        Assert.True((await v1.SaveAsync(legacy.Data with{Title="Changed by v1"},legacy.Id,legacy.Version)).IsSuccess);
        var reopened=(await v2.FindAsync(old.Id)).Value!;
        Assert.Equal("SP",reopened.Data.Address!.State);Assert.Equal("3550308",reopened.Data.Address.MunicipalityIbgeCode);Assert.True(reopened.Data.Address.WithoutNumber);
        Assert.Equal("Rural access",reopened.Data.Address.Complement);Assert.Equal("Changed by v1",reopened.Data.Title);
        // Missing legacy text in a v2 update does not erase the original.
        var again=await v2.SaveAsync(reopened.Data with{Address=reopened.Data.Address with{LegacyText=null}},old.Id,reopened.Version);
        Assert.Equal(old.Data.Address,again.Value!.Data.Address!.LegacyText);
    }
    [Fact] public async Task Concurrent_v2_updates_have_one_winner_and_audit_is_atomic()
    {
        await using var ds=Source();var scope=Scope();var store=new PostgresPropertyStore(ds);var grant=new Grant(scope);
        var v1=new PropertyService(grant,store);var v2=new PropertyServiceV2(grant,store);var owner=(await v1.CreateOwnerAsync("Owner")).Value!;
        var saved=(await v2.SaveAsync(Input(owner.Id))).Value!;
        var results=await Task.WhenAll(v2.SaveAsync(saved.Data with{Title="First"},saved.Id,saved.Version),v2.SaveAsync(saved.Data with{Title="Second"},saved.Id,saved.Version));
        Assert.Single(results,r=>r.IsSuccess);Assert.Single(results,r=>r.Code=="Property.VersionConflict");
        var before=(await v2.FindAsync(saved.Id)).Value!;Assert.Equal(2,before.Version);
        await using var admin=new NpgsqlConnection(Environment.GetEnvironmentVariable("ErpTestAdminConnection"));await admin.OpenAsync();
        try
        {
            await using(var revoke=new NpgsqlCommand("REVOKE INSERT ON real_estate.events FROM erp_runtime",admin))await revoke.ExecuteNonQueryAsync();
            await Assert.ThrowsAsync<PostgresException>(()=>v2.SaveAsync(before.Data with{Title="Must roll back"},saved.Id,before.Version));
        }
        finally{await using var restore=new NpgsqlCommand("GRANT INSERT ON real_estate.events TO erp_runtime",admin);await restore.ExecuteNonQueryAsync();}
        var after=(await v2.FindAsync(saved.Id)).Value!;Assert.Equal(before,after);
        await using var c=await ds.OpenConnectionAsync();await using var tx=await c.BeginTransactionAsync();
        await using var cmd=new NpgsqlCommand("SELECT set_config('erp.organization_id',@org,true); SELECT count(*) FROM real_estate.events WHERE entity_id=@id",c,tx);
        cmd.Parameters.AddWithValue("org",scope.OrganizationId.ToString());cmd.Parameters.AddWithValue("id",saved.Id);
        await using var reader=await cmd.ExecuteReaderAsync();await reader.NextResultAsync();await reader.ReadAsync();Assert.Equal(2,reader.GetInt64(0));
    }
    [Theory]
    [InlineData("address_ibge='123'")][InlineData("address_uf='XX'")]
    [InlineData("latitude=0,longitude=NULL")][InlineData("address_number='10',address_without_number=true")]
    [InlineData("postal_code='123'")]
    public async Task Constraints_reject_invalid_fields_even_without_application(string assignment)
    {
        await using var ds=Source();var scope=Scope();var grant=new Grant(scope);var store=new PostgresPropertyStore(ds);
        var owner=(await new PropertyService(grant,store).CreateOwnerAsync("Owner")).Value!;
        var saved=(await new PropertyServiceV2(grant,store).SaveAsync(Input(owner.Id))).Value!;
        await using var c=await ds.OpenConnectionAsync();await using var tx=await c.BeginTransactionAsync();
        await using(var context=new NpgsqlCommand("SELECT set_config('erp.organization_id',@org,true),set_config('erp.establishments',@units,true)",c,tx))
        {context.Parameters.AddWithValue("org",scope.OrganizationId.ToString());context.Parameters.AddWithValue("units","{"+scope.Establishments![0].Id+"}");await context.ExecuteNonQueryAsync();}
        await using var cmd=new NpgsqlCommand("UPDATE real_estate.properties SET "+assignment+" WHERE id=@id",c,tx);cmd.Parameters.AddWithValue("id",saved.Id);
        var error=await Assert.ThrowsAsync<PostgresException>(()=>cmd.ExecuteNonQueryAsync());Assert.Equal("23514",error.SqlState);
    }
}
