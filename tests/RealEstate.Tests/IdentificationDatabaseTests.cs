using System.Text.Json;
using Npgsql;
using RealEstate.Application;
using RealEstate.Domain;
using RealEstate.Infrastructure;
using Xunit;
namespace RealEstate.Tests;
[Collection("PostgreSQL")]
public sealed class IdentificationDatabaseTests
{
    private sealed class Grant(AccessScope scope):ICoreAccess
    {
        public AccessScope Scope=scope;
        public Task<Outcome<AccessScope>> RequireAsync(string r,string p,CancellationToken t)=>Task.FromResult(new Outcome<AccessScope>(Scope));
    }
    [Fact] public async Task Identification_survives_adaptation_and_concurrent_drafts_have_unique_codes()
    {
        await using var ds=NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("ConnectionStrings__Erp")??throw new InvalidOperationException("Disposable database required"));
        var unit=Guid.NewGuid();var grant=new Grant(new(Guid.NewGuid(),"cad1b-synthetic","Synthetic",[new(unit,"One")],ActiveEstablishmentId:unit));
        var service=new PropertyServiceV2(grant,new PostgresPropertyStore(ds));
        var results=await Task.WhenAll(Enumerable.Range(0,12).Select(_=>service.SaveAsync(new())));
        Assert.All(results,r=>Assert.True(r.IsSuccess,r.Code));
        Assert.Equal(12,results.Select(r=>r.Value!.Data.Code).Distinct().Count());
        Assert.All(results,r=>Assert.StartsWith("IM-",r.Value!.Data.Code));
        var first=results[0].Value!;
        var data=first.Data with{Title="Vista livre",Type="Casa",Purpose="Venda e locação",Summary="Descrição sintética",InternalReference="Pasta 12",IsInCondominium=true,Address=new(Street:"Rua preservada",Neighborhood:"Bairro",Municipality:"Teste",State:"SP"),Price=100};
        var saved=await service.SaveAsync(data,first.Id,1);Assert.True(saved.IsSuccess,saved.Code);
        var changed=await service.PatchAsync(first.Id,2,JsonSerializer.SerializeToElement(new{type="Fazenda",purpose="Locação",isInCondominium=false}));Assert.True(changed.IsSuccess,changed.Code);
        var reopened=(await service.FindAsync(first.Id)).Value!;
        Assert.Equal(first.Data.Code,reopened.Data.Code);Assert.Equal("Rua preservada",reopened.Data.Address!.Street);
        Assert.Equal(data.Summary,reopened.Data.Summary);Assert.Equal(data.InternalReference,reopened.Data.InternalReference);Assert.Equal(100,reopened.Data.Price);
        Assert.False(reopened.Data.IsInCondominium);Assert.Equal("Locação",reopened.Data.Purpose);
        var stale=await service.SaveAsync(data,first.Id,2);Assert.Equal("Property.VersionConflict",stale.Code);
        var duplicate=await service.SaveAsync(new(Code:first.Data.Code));Assert.Equal("Property.DuplicateCode",duplicate.Code);
        var second=Guid.NewGuid();var original=grant.Scope;
        grant.Scope=original with{ActiveEstablishmentId=second,Establishments=[..original.Establishments!,new(second,"Two")]};
        var updated=await service.SaveAsync(reopened.Data,first.Id,3);Assert.True(updated.IsSuccess,updated.Code);Assert.Equal(unit,updated.Value!.Data.EstablishmentId);
        grant.Scope=original with{Establishments=[new(second,"Two")],ActiveEstablishmentId=second};Assert.Equal("Resource.NotFound",(await service.FindAsync(first.Id)).Code);
        grant.Scope=original with{OrganizationId=Guid.NewGuid()};Assert.Equal("Resource.NotFound",(await service.FindAsync(first.Id)).Code);
    }
}
