using Npgsql;
using RealEstate.Application;
using RealEstate.Domain;
using RealEstate.Infrastructure;
using Xunit;
namespace RealEstate.Tests;
[Collection("PostgreSQL")]
public sealed class CondominiumDatabaseTests
{
    sealed class Grant(AccessScope scope):ICoreAccess
    {public AccessScope Scope=scope;public bool Denied;public Task<Outcome<AccessScope>> RequireAsync(string r,string p,CancellationToken t)=>Task.FromResult(Denied?Outcome<AccessScope>.Fail("Access.Forbidden"):new Outcome<AccessScope>(Scope));}
    [Fact] public async Task Condominium_can_be_reused_without_copying_identity_across_properties_or_organizations()
    {
        await using var ds=NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("ConnectionStrings__Erp")??throw new InvalidOperationException("Disposable database required"));
        var unit=Guid.NewGuid();var grant=new Grant(new(Guid.NewGuid(),"condominium-test","Synthetic",[new(unit,"Unit")],ActiveEstablishmentId:unit));var store=new PostgresPropertyStore(ds);var service=new CondominiumService(grant,store);
        var created=await service.CreateAsync(new("Synthetic condominium","mixed",2,10));Assert.True(created.IsSuccess,created.Code);var id=created.Value!.Id;
        Assert.Equal(id,Assert.Single((await service.SearchAsync("Synthetic")).Value!).Id);
        var properties=new PropertyServiceV2(grant,store);var c=new CaptureData();c.Condominium.Id=id;c.Condominium.Unit="101";c.Fees.SalePercent=5;
        var first=await properties.SaveAsync(new(IsInCondominium:true,Capture:c));Assert.True(first.IsSuccess,first.Code);
        var second=await properties.SaveAsync(new(IsInCondominium:true,Capture:c));Assert.True(second.IsSuccess,second.Code);
        Assert.Equal(id,second.Value!.Data.Capture!.Condominium.Id);Assert.Equal(5,(await properties.FindAsync(first.Value!.Id)).Value!.Data.Capture!.Fees.SalePercent);
        grant.Scope=grant.Scope with{OrganizationId=Guid.NewGuid()};Assert.Empty((await service.SearchAsync("Synthetic")).Value!);
        Assert.Equal("Condominium.Unavailable",(await properties.SaveAsync(new(Capture:c))).Code);
        grant.Denied=true;Assert.Equal("Access.Forbidden",(await service.CreateAsync(new("Denied","other"))).Code);
    }
}
