using System.Text.Json;
using Npgsql;
using RealEstate.Application;
using RealEstate.Domain;
using RealEstate.Infrastructure;
using Xunit;
namespace RealEstate.Tests;
[Collection("PostgreSQL")]
public sealed class PropertyCopyPersistenceTests
{
 sealed class Authority(AccessScope s):ICoreAccess{public Task<Outcome<AccessScope>> RequireAsync(string r,string p,CancellationToken t)=>Task.FromResult(new Outcome<AccessScope>(s));}
 static PropertyInputV2 Clone(PropertyInputV2 p)=>JsonSerializer.Deserialize<PropertyInputV2>(JsonSerializer.Serialize(p))!;
 [Fact]public async Task Independent_copy_is_atomic_editable_and_does_not_share_mutable_media(){
  await using var ds=NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("ConnectionStrings__Erp")!);
  var unit=Guid.NewGuid();var scope=new AccessScope(Guid.NewGuid(),"copy-test-only","Synthetic",[new(unit,"Unit")],ActiveEstablishmentId:unit);
  var store=new PostgresPropertyStore(ds);var service=new PropertyServiceV2(new Authority(scope),store);
  var firstOwner=(await store.CreateOwnerAsync(scope,"First owner",default)).Value!;var nextOwner=(await store.CreateOwnerAsync(scope,"Second owner",default)).Value!;
  var initial=(await service.SaveAsync(new(Type:"Apartamento",Purpose:"Venda",OwnerId:firstOwner.Id,Price:777.35m,Capture:new(){Areas=new(){Built=75.5m,Unit="m2"},Condominium=new(){Unit="101"},Values=new(){Sale=new(){Authorized=100000m}}}))).Value!;
  var empty=(await store.ListMediaAsync(scope,initial.Id,default))!;
  var uploaded=await store.ChangeMediaAsync(scope,initial.Id,empty.Revision,new("upload",Files:[new("photo.png","image/png","image",[1,2,3],Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(new byte[]{1,2,3})))]),default);
  var oldPhoto=Assert.Single(uploaded.Value!.Items);var original=(await service.FindAsync(initial.Id)).Value!;
  var annotated=Clone(original.Data);annotated.Capture!.Media.Add(new(){MediaId=oldPhoto.Id,Caption="Original photo"});annotated.Capture.Selections.Add(new(){GroupId="private-features",OptionId="other",Label="Other",Selected=true,PhotoId=oldPhoto.Id});
  // Only approved catalogue options are accepted; caption alone exercises media identity remapping.
  annotated.Capture.Selections.Clear();
  var source=(await service.SaveAsync(annotated,original.Id,original.Version)).Value!;var sourceJson=JsonSerializer.Serialize(source);
  var data=Clone(source.Data) with{OwnerId=nextOwner.Id,Purpose="Temporada",Code=null,Title="Independent copy"};data.Capture!.Condominium.Unit="202";data.Capture.Values.Seasonal.Authorized=1500.5m;data.Capture.Policy=null;
  var copySource=new PropertyCopySource(source.Id,source.Version,uploaded.Value.Revision);
  var copied=await service.SaveAsync(data,copy:copySource);Assert.True(copied.IsSuccess,copied.Code);
  var target=copied.Value!;Assert.NotEqual(source.Id,target.Id);Assert.StartsWith("TEMAP-",target.CommercialCode);Assert.Equal(1,target.Version);Assert.Equal(nextOwner.Id,target.Data.OwnerId);Assert.Equal("202",target.Data.Capture!.Condominium.Unit);Assert.Equal(777.35m,target.Data.Price);Assert.Single(target.Data.Capture.PriceHistory);
  Assert.Equal(sourceJson,JsonSerializer.Serialize((await service.FindAsync(source.Id)).Value));
  var added=await service.SaveAsync(Clone(data),copy:copySource with{Excluded=[oldPhoto.Id],Files=[new("new-photo.png","image/png","image",[4,5,6],Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(new byte[]{4,5,6})))]});
  Assert.True(added.IsSuccess,added.Code);var addedGallery=(await store.ListMediaAsync(scope,added.Value!.Id,default))!;Assert.Equal("new-photo.png",Assert.Single(addedGallery.Items).Name);Assert.True(addedGallery.Items[0].IsCover);Assert.Equal(sourceJson,JsonSerializer.Serialize((await service.FindAsync(source.Id)).Value));
  var gallery=(await store.ListMediaAsync(scope,target.Id,default))!;var newPhoto=Assert.Single(gallery.Items);Assert.NotEqual(oldPhoto.Id,newPhoto.Id);Assert.Equal(1,newPhoto.Version);Assert.Equal(newPhoto.Id,Assert.Single(target.Data.Capture.Media).MediaId);Assert.Equal("Original photo",target.Data.Capture.Media[0].Caption);
  Assert.Equal((await store.ReadMediaAsync(scope,source.Id,oldPhoto.Id,default))!.Content,(await store.ReadMediaAsync(scope,target.Id,newPhoto.Id,default))!.Content);
  Assert.True((await store.ChangeMediaAsync(scope,target.Id,gallery.Revision,new("remove",newPhoto.Id),default)).IsSuccess);Assert.Single((await store.ListMediaAsync(scope,source.Id,default))!.Items);Assert.Equal(sourceJson,JsonSerializer.Serialize((await service.FindAsync(source.Id)).Value));
  Assert.Equal("Property.VersionConflict",(await service.SaveAsync(Clone(data),copy:copySource with{Version=source.Version-1})).Code);
  Assert.Equal("Media.VersionConflict",(await service.SaveAsync(Clone(data),copy:copySource with{MediaRevision="stale"})).Code);
  var foreign=new PropertyServiceV2(new Authority(scope with{OrganizationId=Guid.NewGuid()}),store);Assert.Equal("Resource.NotFound",(await foreign.SaveAsync(Clone(data),copy:copySource)).Code);
  var parallel=await Task.WhenAll(service.SaveAsync(Clone(data),copy:copySource),service.SaveAsync(Clone(data),copy:copySource));Assert.All(parallel,r=>Assert.True(r.IsSuccess,r.Code));Assert.NotEqual(parallel[0].Value!.Id,parallel[1].Value!.Id);Assert.NotEqual(parallel[0].Value!.CommercialCode,parallel[1].Value!.CommercialCode);
  Assert.Equal(sourceJson,JsonSerializer.Serialize((await service.FindAsync(source.Id)).Value));
 }
}
