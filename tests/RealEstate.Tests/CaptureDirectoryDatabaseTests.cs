using Npgsql;
using RealEstate.Application;
using RealEstate.Domain;
using RealEstate.Infrastructure;
using Xunit;
namespace RealEstate.Tests;

[Collection("PostgreSQL")]
public sealed class CaptureDirectoryDatabaseTests
{
    sealed class Access(AccessScope scope):ICoreAccess{public AccessScope Scope=scope;public Task<Outcome<AccessScope>> RequireAsync(string r,string p,CancellationToken t)=>Task.FromResult(new Outcome<AccessScope>(Scope));}
    sealed class Directory:ICaptureDirectory
    {
        public bool Revoked;
        public Task<Outcome<CaptureResponsibleDirectory>> ReadAsync(AccessScope s,Guid unit,string? subject=null,CancellationToken token=default)=>Task.FromResult(new Outcome<CaptureResponsibleDirectory>(new(s.OrganizationId,unit,Revoked?[]:[new("synthetic-candidate","Synthetic candidate",Guid.NewGuid(),"Descriptive only")])));
    }
    [Fact]public async Task Responsible_reopens_in_real_postgres_preserving_uuid_version_and_other_capture_data()
    {
        await using var ds=NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("ConnectionStrings__Erp")??throw new InvalidOperationException("Disposable database required"));
        var org=Guid.NewGuid();var unit=Guid.NewGuid();var access=new Access(new(org,"synthetic-manager","Synthetic",[new(unit,"Unit")],ActiveEstablishmentId:unit));var directory=new Directory();var store=new PostgresPropertyStore(ds);var service=new PropertyServiceV2(access,store,directory);
        var c=new CaptureData{Acquisition=new(){ResponsibleSubject="synthetic-candidate"}};c.Rooms.Bedrooms=3;
        var saved=await service.SaveAsync(new(Capture:c));Assert.True(saved.IsSuccess,saved.Code);var p=saved.Value!;
        var reopened=(await service.FindAsync(p.Id)).Value!;Assert.Equal("synthetic-candidate",reopened.Data.Capture!.Acquisition.ResponsibleSubject);Assert.Equal(3,reopened.Data.Capture.Rooms.Bedrooms);Assert.Equal(unit,reopened.Data.EstablishmentId);
        directory.Revoked=true;
        Assert.Equal("Access.Forbidden",(await service.SaveAsync(reopened.Data with{Capture=new(){Acquisition=new(){ResponsibleSubject="another"}}},p.Id,1)).Code);
        Assert.Equal(1,(await service.FindAsync(p.Id)).Value!.Version);
        var kept=await service.SaveAsync(reopened.Data with{Title="Changed title"},p.Id,1);Assert.True(kept.IsSuccess,kept.Code);Assert.Equal(p.Id,kept.Value!.Id);Assert.Equal(2,kept.Value.Version);Assert.Equal("synthetic-candidate",(await service.FindAsync(p.Id)).Value!.Data.Capture!.Acquisition.ResponsibleSubject);
        Assert.Equal("Property.VersionConflict",(await service.SaveAsync(reopened.Data,p.Id,1)).Code);
        access.Scope=access.Scope with{OrganizationId=Guid.NewGuid()};Assert.Equal("Resource.NotFound",(await service.FindAsync(p.Id)).Code);
    }
}
