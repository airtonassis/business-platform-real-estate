using Npgsql;
using RealEstate.Application;
using RealEstate.Domain;
using RealEstate.Infrastructure;
using Xunit;
namespace RealEstate.Tests;
[Collection("PostgreSQL")]
public sealed class CommercialPolicyScopeTests
{
    sealed class Access(AccessScope scope):ICoreAccess{public AccessScope Scope=scope;public Task<Outcome<AccessScope>> RequireAsync(string r,string p,CancellationToken t)=>Task.FromResult(new Outcome<AccessScope>(Scope));}
    [Fact]public async Task Restricted_SQL_cannot_read_or_insert_other_establishment_exceptions_or_application_history()
    {
        await using var ds=NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("ConnectionStrings__Erp")??throw new InvalidOperationException("Disposable PostgreSQL required"));
        var org=Guid.NewGuid();var unit=Guid.NewGuid();var access=new Access(new(org,"author","Synthetic",[new(unit,"Unit")],true,unit));var store=new PostgresPropertyStore(ds);var service=new CommercialPolicyService(access,store,store);var properties=new PropertyServiceV2(access,store);
        var general=(await service.SaveDraftAsync(new("General","Explicit test",new(new(),[])))).Value!;access.Scope=access.Scope with{Subject="manager"};general=(await service.ApproveAsync(general.Id,1,"Approved test")).Value!;
        var p=(await properties.SaveAsync(new())).Value!;access.Scope=access.Scope with{Subject="author"};var exception=(await service.SaveDraftAsync(new("Exception","For this property",new(new(){SalePercent=5},[]),p.Id,general.Id))).Value!;access.Scope=access.Scope with{Subject="manager"};exception=(await service.ApproveAsync(exception.Id,1,"Approved exception")).Value!;Assert.True((await properties.SaveAsync(p.Data with{Capture=CommercialPolicyRules.Apply(exception,null)},p.Id,1)).IsSuccess);
        await using var c=await ds.OpenConnectionAsync();await using var tx=await c.BeginTransactionAsync();await using(var settings=new NpgsqlCommand("SELECT set_config('erp.organization_id',@org,true),set_config('erp.establishments',@units,true),set_config('erp.legacy_manager','disabled',true)",c,tx)){settings.Parameters.AddWithValue("org",org.ToString());settings.Parameters.AddWithValue("units","{"+Guid.NewGuid()+"}");await settings.ExecuteNonQueryAsync();}
        await using(var check=new NpgsqlCommand("SELECT (SELECT count(*) FROM real_estate.commercial_policy_revisions WHERE organization_id=@org),(SELECT count(*) FROM real_estate.commercial_policy_applications WHERE organization_id=@org)",c,tx)){check.Parameters.AddWithValue("org",org);await using var r=await check.ExecuteReaderAsync();await r.ReadAsync();Assert.Equal(1,r.GetInt64(0));Assert.Equal(0,r.GetInt64(1));}
        await using(var insertion=new NpgsqlCommand("INSERT INTO real_estate.commercial_policy_applications(organization_id,id,property_id,property_version,revision_id,revision_version,actor) VALUES(@org,@id,@property,999,@revision,2,'forged')",c,tx)){insertion.Parameters.AddWithValue("org",org);insertion.Parameters.AddWithValue("id",Guid.NewGuid());insertion.Parameters.AddWithValue("property",p.Id);insertion.Parameters.AddWithValue("revision",exception.Id);var error=await Assert.ThrowsAsync<PostgresException>(()=>insertion.ExecuteNonQueryAsync());Assert.Equal("42501",error.SqlState);}
        await tx.RollbackAsync();
    }
}
