using Npgsql;
using RealEstate.Application;
using RealEstate.Domain;
using RealEstate.Infrastructure;
using Xunit;
namespace RealEstate.Tests;

[Collection("PostgreSQL")]
public sealed class PropertyLifecycleDatabaseTests
{
    private sealed class Grant(AccessScope scope,string? deniedPermission=null):ICoreAccess
    {
        public Task<Outcome<AccessScope>> RequireAsync(string resource,string permission,CancellationToken token)=>Task.FromResult(permission==deniedPermission?Outcome<AccessScope>.Fail("Access.Forbidden"):new Outcome<AccessScope>(scope));
    }

    [Fact] public async Task Draft_to_review_is_audited_and_never_commercially_listed()
    {
        await using var ds=NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("ConnectionStrings__Erp")!);
        var org=Guid.NewGuid();var unit=Guid.NewGuid();var other=Guid.NewGuid();
        var scope=new AccessScope(org,"synthetic-reviewer","Synthetic",[new(unit,"Branch")],ActiveEstablishmentId:unit);
        var store=new PostgresPropertyStore(ds);var service=new PropertyServiceV2(new Grant(scope),store);
        var owner=(await new PropertyService(new Grant(scope),store).CreateOwnerAsync("Synthetic owner")).Value!;
        var input=new PropertyInputV2("REVIEW-"+Guid.NewGuid().ToString("N")[..8],"Synthetic property",owner.Id,"Residential","Apartamento","Venda",100m,
            new StructuredAddress("Synthetic address","12345678",Latitude:1,Longitude:1),unit);
        var draft=(await service.SaveAsync(input)).Value!;
        Assert.Equal(PropertyLifecycle.Draft,draft.Status);
        Assert.Empty((await store.ListPageAsync(scope,new PropertyListQuery{Commercial=true},default)).Properties);
        var sent=await service.SubmitForReviewAsync(draft.Id,draft.Version);
        Assert.True(sent.IsSuccess);Assert.Equal(PropertyLifecycle.InReview,sent.Value!.Status);
        Assert.Equal("Property.StatusConflict",(await service.SubmitForReviewAsync(draft.Id,sent.Value.Version)).Code);
        Assert.Equal("Property.VersionConflict",(await service.SubmitForReviewAsync(draft.Id,draft.Version)).Code);
        Assert.Empty((await store.ListPageAsync(scope,new PropertyListQuery{Commercial=true},default)).Properties);
        Assert.Single((await store.ListPageAsync(scope,new PropertyListQuery{Status=PropertyLifecycle.InReview},default)).Properties);
        Assert.Null(await store.FindV2Async(scope with{OrganizationId=other},draft.Id,default));
        await using var c=await ds.OpenConnectionAsync();await using var tx=await c.BeginTransactionAsync();
        await using(var context=new NpgsqlCommand("SELECT set_config('erp.organization_id',@org,true),set_config('erp.establishments',@units,true)",c,tx))
        {context.Parameters.AddWithValue("org",org.ToString());context.Parameters.AddWithValue("units","{"+unit+"}");await context.ExecuteNonQueryAsync();}
        await using(var eventQuery=new NpgsqlCommand("SELECT actor,establishment_id,from_status,to_status,occurred_at FROM real_estate.events WHERE organization_id=@org AND entity_id=@id AND event_type='PropertySubmittedForReview'",c,tx))
        {eventQuery.Parameters.AddWithValue("org",org);eventQuery.Parameters.AddWithValue("id",draft.Id);await using var r=await eventQuery.ExecuteReaderAsync();Assert.True(await r.ReadAsync());Assert.Equal("synthetic-reviewer",r.GetString(0));Assert.Equal(unit,r.GetGuid(1));Assert.Equal(PropertyLifecycle.Draft,r.GetString(2));Assert.Equal(PropertyLifecycle.InReview,r.GetString(3));Assert.True(r.GetFieldValue<DateTimeOffset>(4)<=DateTimeOffset.UtcNow);Assert.False(await r.ReadAsync());}
        await tx.RollbackAsync();
        await using var blocked=await c.BeginTransactionAsync();
        await using(var context=new NpgsqlCommand("SELECT set_config('erp.organization_id',@org,true),set_config('erp.establishments',@units,true)",c,blocked))
        {context.Parameters.AddWithValue("org",org.ToString());context.Parameters.AddWithValue("units","{"+unit+"}");await context.ExecuteNonQueryAsync();}
        await using(var command=new NpgsqlCommand("UPDATE real_estate.properties SET status='Available' WHERE organization_id=@org AND id=@id",c,blocked))
        {command.Parameters.AddWithValue("org",org);command.Parameters.AddWithValue("id",draft.Id);var error=await Assert.ThrowsAsync<PostgresException>(()=>command.ExecuteNonQueryAsync());Assert.Equal("23514",error.SqlState);}
        await blocked.RollbackAsync();
        Assert.Equal("Access.Forbidden",(await new PropertyServiceV2(new Grant(scope,"PROPERTY_APPROVE"),store).ApproveAsync(draft.Id,sent.Value.Version)).Code);
        Assert.Equal("Access.Forbidden",(await new PropertyServiceV2(new Grant(scope with{ActiveEstablishmentId=Guid.NewGuid()}),store).ApproveAsync(draft.Id,sent.Value.Version)).Code);
        var approved=await service.ApproveAsync(draft.Id,sent.Value.Version);
        Assert.True(approved.IsSuccess);Assert.Equal(PropertyLifecycle.Available,approved.Value!.Status);
        Assert.Equal("Property.StatusConflict",(await service.ApproveAsync(draft.Id,approved.Value.Version)).Code);
        Assert.Single((await store.ListPageAsync(scope,new PropertyListQuery{Commercial=true},default)).Properties);
        await using var audit=await c.BeginTransactionAsync();
        await using(var context=new NpgsqlCommand("SELECT set_config('erp.organization_id',@org,true),set_config('erp.establishments',@units,true)",c,audit))
        {context.Parameters.AddWithValue("org",org.ToString());context.Parameters.AddWithValue("units","{"+unit+"}");await context.ExecuteNonQueryAsync();}
        await using(var eventQuery=new NpgsqlCommand("SELECT actor,establishment_id,from_status,to_status,occurred_at FROM real_estate.events WHERE organization_id=@org AND entity_id=@id AND event_type='PropertyApproved'",c,audit))
        {eventQuery.Parameters.AddWithValue("org",org);eventQuery.Parameters.AddWithValue("id",draft.Id);await using var r=await eventQuery.ExecuteReaderAsync();Assert.True(await r.ReadAsync());Assert.Equal("synthetic-reviewer",r.GetString(0));Assert.Equal(unit,r.GetGuid(1));Assert.Equal(PropertyLifecycle.InReview,r.GetString(2));Assert.Equal(PropertyLifecycle.Available,r.GetString(3));Assert.True(r.GetFieldValue<DateTimeOffset>(4)<=DateTimeOffset.UtcNow);Assert.False(await r.ReadAsync());}
        await audit.RollbackAsync();
        await using var admin=new NpgsqlConnection(Environment.GetEnvironmentVariable("ErpTestAdminConnection"));await admin.OpenAsync();
        await using var mutation=new NpgsqlCommand("UPDATE real_estate.events SET actor='forged' WHERE organization_id=@org AND entity_id=@id AND event_type='PropertyApproved'",admin);
        mutation.Parameters.AddWithValue("org",org);mutation.Parameters.AddWithValue("id",draft.Id);
        Assert.Equal("42501",(await Assert.ThrowsAsync<PostgresException>(()=>mutation.ExecuteNonQueryAsync())).SqlState);
    }
}
