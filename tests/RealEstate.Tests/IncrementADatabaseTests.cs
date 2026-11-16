using Npgsql;
using RealEstate.Application;
using RealEstate.Domain;
using RealEstate.Infrastructure;
using Xunit;
namespace RealEstate.Tests;
[Collection("PostgreSQL")]
public sealed class IncrementADatabaseTests
{
    sealed class Access(AccessScope s):ICoreAccess
    {public Task<Outcome<AccessScope>> RequireAsync(string r,string p,CancellationToken t)=>Task.FromResult(new Outcome<AccessScope>(s));}
    sealed class Directory:ICaptureDirectory
    {
        public int Calls;public bool Eligible=true;
        public Task<Outcome<CaptureResponsibleDirectory>> ReadAsync(AccessScope s,Guid unit,string? subject=null,CancellationToken token=default)
        {Calls++;return Task.FromResult(new Outcome<CaptureResponsibleDirectory>(new(s.OrganizationId,unit,Eligible?[new("candidate","Synthetic",Guid.NewGuid(),"Descriptive")]:[])));}
    }
    [Fact] public async Task Omitted_capture_revalidates_directory_while_preserving_historical_assignment_and_data_in_postgres()
    {
        await using var ds=NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("ConnectionStrings__Erp")??throw new InvalidOperationException("Isolated DB only"));
        var unit=Guid.NewGuid();var scope=new AccessScope(Guid.NewGuid(),"increment-a-fixture","Synthetic",[new(unit,"Unit")],ActiveEstablishmentId:unit);var directory=new Directory();var service=new PropertyServiceV2(new Access(scope),new PostgresPropertyStore(ds),directory);
        var capture=new CaptureData{Acquisition=new(){ResponsibleSubject="candidate"},Areas=new(){Total=333,Unit="m2"},Selections=[new(){GroupId="collective-leisure",OptionId="pool",SharedOverride=false}]};
        var first=(await service.SaveAsync(new(Type:"Apartamento",Purpose:"Locação",Capture:capture))).Value!;directory.Eligible=false;
        var changed=await service.SaveAsync(new(Code:first.Data.Code,Title:"Other field"),first.Id,first.Version);Assert.True(changed.IsSuccess,changed.Code);
        var reopened=(await service.FindAsync(first.Id)).Value!;Assert.Equal(2,directory.Calls);Assert.Equal("candidate",reopened.Data.Capture!.Acquisition.ResponsibleSubject);Assert.Equal(333,reopened.Data.Capture.Areas.Total);Assert.False(Assert.Single(reopened.Data.Capture.Selections).SharedOverride);Assert.Equal(first.CommercialCode,reopened.CommercialCode);Assert.Equal(2,reopened.Version);
    }
}
