using Npgsql;
using RealEstate.Application;
using RealEstate.Domain;
using RealEstate.Infrastructure;
using Xunit;
namespace RealEstate.Tests;
[Collection("PostgreSQL")]
public sealed class PropertyListingDatabaseTests
{
    private sealed class Grant(AccessScope scope):ICoreAccess
    {
        public bool Denied;
        public Task<Outcome<AccessScope>> RequireAsync(string resource,string permission,CancellationToken token)=>Task.FromResult(Denied?Outcome<AccessScope>.Fail("Access.Forbidden"):new Outcome<AccessScope>(scope with{ActiveEstablishmentId=scope.Establishments?.FirstOrDefault()?.Id}));
    }
    [Fact]
    public async Task Filters_and_sort_precede_paging_and_respect_tenant_units_and_literal_patterns()
    {
        await using var ds=NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("ConnectionStrings__Erp")??throw new InvalidOperationException("Disposable database required"));
        var scope=new AccessScope(Guid.NewGuid(),"ux02-test","Synthetic",[new(Guid.NewGuid(),"Unit")]);
        var grant=new Grant(scope);var store=new PostgresPropertyStore(ds);var legacy=new PropertyService(grant,store);var service=new PropertyServiceV2(grant,store);var listing=new PropertyListingService(grant,store);
        var owner=(await legacy.CreateOwnerAsync("UX02 owner")).Value!;
        for(var i=0;i<107;i++){
            var saved=await service.SaveAsync(new("UX"+i.ToString("000"),"Synthetic "+i,owner.Id,"Rural","Fazenda","Venda",3000+i,
                new(Municipality:i==0?"Alvo":"Outra",State:"SP",Neighborhood:i==0?"Centro":null)));
            Assert.True(saved.IsSuccess,saved.Code);
        }
        var target=(await listing.ListAsync(new(){Municipality="Alvo"})).Value!;
        Assert.Equal(1,target.Total);Assert.Equal("UX000",Assert.Single(target.Properties).Data.Code);
        var first=(await listing.ListAsync(new(){Sort="code",Descending=false})).Value!;
        var second=(await listing.ListAsync(new(){Sort="code",Descending=false,Page=2})).Value!;
        Assert.Equal(107,first.Total);Assert.Equal(25,first.Properties.Count);Assert.Equal("UX000",first.Properties[0].Data.Code);Assert.Equal("UX025",second.Properties[0].Data.Code);
        Assert.Empty(first.Properties.Select(p=>p.Id).Intersect(second.Properties.Select(p=>p.Id)));
        var highest=(await listing.ListAsync(new(){Sort="price",Descending=true})).Value!;Assert.Equal(3106,highest.Properties[0].Data.Price);
        Assert.Equal(2,(await listing.ListAsync(new(){MinPrice=3105,MaxPrice=3106})).Value!.Total);
        Assert.Equal(0,(await listing.ListAsync(new(){Code="%"})).Value!.Total);
        Assert.Equal(0,(await listing.ListAsync(new(){Search="_' OR true --"})).Value!.Total);
        Assert.Equal("Listing.InvalidQuery",(await listing.ListAsync(new(){Sort="price;DROP TABLE real_estate.properties"})).Code);
        Assert.Equal("Access.Forbidden",(await listing.ListAsync(new(){EstablishmentId=Guid.NewGuid()})).Code);
        var otherUnit=scope with{Establishments=[new(Guid.NewGuid(),"Other")]};
        Assert.Equal(0,(await new PropertyListingService(new Grant(otherUnit),store).ListAsync(new())).Value!.Total);
        Assert.Equal(0,(await new PropertyListingService(new Grant(scope with{OrganizationId=Guid.NewGuid()}),store).ListAsync(new())).Value!.Total);
        grant.Denied=true;Assert.Equal("Access.Forbidden",(await listing.ListAsync(new())).Code);
    }
}
