using Npgsql;
using RealEstate.Domain;
using RealEstate.Application;
using RealEstate.Infrastructure;
using Xunit;
namespace RealEstate.Tests;
public sealed class OwnerContactPersistenceTests
{
 sealed class Authority(AccessScope scope):ICoreAccess{public Task<Outcome<AccessScope>> RequireAsync(string r,string p,CancellationToken t)=>Task.FromResult(new Outcome<AccessScope>(scope));}
 [Fact] public async Task Contact_address_reopen_update_duplicate_and_tenant_boundary()
 {
  await using var ds=NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("ConnectionStrings__Erp")!);
  var store=new PostgresPropertyStore(ds);var org=Guid.NewGuid();var scope=new AccessScope(org,"owner-contact-fixture","Synthetic",[new(org,"Unit")],ActiveEstablishmentId:org);
  var service=new OwnerProfileService(new Authority(scope),store);
  var details=new OwnerDetails("Synthetic trade","(11) 99999-0000","owner@example.invalid",new(PostalCode:"01001-000",State:"SP",Municipality:"São Paulo",Street:"Praça da Sé",Number:"s/n",Complement:"Synthetic",Neighborhood:"Sé"));
  var created=await service.CreateAsync(new("Synthetic owner","PJ","11.222.333/0001-81",details));Assert.True(created.IsSuccess,created.Code);
  var id=created.Value!.Id;var found=(await service.FindForEditAsync(id)).Value!;Assert.Equal("01001000",found.Details!.Address!.PostalCode);Assert.Equal("s/n",found.Details.Address.Number);Assert.Equal(details.Email,found.Details.Email);
  Assert.Equal(id,Assert.Single((await service.SearchAsync("11222333000181")).Value!).Id);
  Assert.Equal("Owner.DuplicateTaxId",(await service.CreateAsync(new("Duplicate","PJ","11222333000181",details))).Code);
  // An older consumer without the new optional details must not erase them.
  var updated=await service.UpdateAsync(id,1,new("Updated owner","PJ","11222333000181"));Assert.True(updated.IsSuccess,updated.Code);Assert.Equal(2,updated.Value!.Version);Assert.Equal(details.Email,updated.Value.Details!.Email);
  Assert.Equal(2,updated.Value.History!.Count);Assert.Contains("owner@example.invalid",updated.Value.History.Last().Data);
  Assert.Null(await store.FindOwnerProfileAsync(scope with{OrganizationId=Guid.NewGuid()},id,default));
  Assert.Equal("Owner.ContactInvalid",(await service.CreateAsync(new("Invalid","PF","52998224725",new(Email:"not-an-email")))).Code);
 }
}
