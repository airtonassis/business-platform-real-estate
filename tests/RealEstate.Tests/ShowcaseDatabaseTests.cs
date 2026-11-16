using Npgsql;
using RealEstate.Application;
using RealEstate.Domain;
using RealEstate.Infrastructure;
using Xunit;

namespace RealEstate.Tests;

[Collection("PostgreSQL")]
public sealed class ShowcaseDatabaseTests
{
    private sealed class Access(AccessScope scope, string? denied = null) : ICoreAccess
    {
        public Task<Outcome<AccessScope>> RequireAsync(string resource, string permission, CancellationToken token) =>
            Task.FromResult(permission == denied ? Outcome<AccessScope>.Fail("Access.Forbidden") : new Outcome<AccessScope>(scope));
    }

    [Fact]
    public async Task PrepareAuthorizeWithdrawAreVersionedAuditedAndScoped()
    {
        var runtime = Environment.GetEnvironmentVariable("ConnectionStrings__Erp")!;
        var admin = Environment.GetEnvironmentVariable("ErpTestAdminConnection")!;
        var org = Guid.NewGuid(); var unit = Guid.NewGuid(); var other = Guid.NewGuid();
        var owner = Guid.NewGuid(); var propertyId = Guid.NewGuid();
        await using (var c = new NpgsqlConnection(admin))
        {
            await c.OpenAsync();
            await using var tx = await c.BeginTransactionAsync();
            await using (var context = new NpgsqlCommand("SELECT set_config('erp.organization_id',@org,true),set_config('erp.establishments',@units,true),set_config('erp.active_establishment',@unit,true)", c, tx))
            {
                context.Parameters.AddWithValue("org", org.ToString()); context.Parameters.AddWithValue("units", "{" + unit + "}");
                context.Parameters.AddWithValue("unit", unit.ToString()); await context.ExecuteNonQueryAsync();
            }
            await using (var seed = new NpgsqlCommand("INSERT INTO real_estate.owners(organization_id,id,name,created_by) VALUES(@org,@owner,'Synthetic','test'); INSERT INTO real_estate.properties(organization_id,id,establishment_id,owner_id,code,title,category,property_type,purpose,address,postal_code,latitude,longitude,price,created_by,updated_by) VALUES(@org,@property,@unit,@owner,'S04-TEST','Synthetic','Residential','Apartamento','Venda','Synthetic address','12345678',1,1,100,'test','test')", c, tx))
            {
                seed.Parameters.AddWithValue("org", org); seed.Parameters.AddWithValue("owner", owner);
                seed.Parameters.AddWithValue("property", propertyId); seed.Parameters.AddWithValue("unit", unit);
                await seed.ExecuteNonQueryAsync();
            }
            await using (var transition = new NpgsqlCommand("SELECT set_config('erp.property_transition','submit',true); UPDATE real_estate.properties SET status='InReview',version=version+1,updated_by='test' WHERE organization_id=@org AND id=@id; SELECT set_config('erp.property_transition','approve',true); UPDATE real_estate.properties SET status='Available',version=version+1,updated_by='test' WHERE organization_id=@org AND id=@id", c, tx))
            {
                transition.Parameters.AddWithValue("org", org); transition.Parameters.AddWithValue("id", propertyId);
                await transition.ExecuteNonQueryAsync();
            }
            await tx.CommitAsync();
        }
        await using var ds = NpgsqlDataSource.Create(runtime);
        var store = new PostgresPropertyStore(ds);
        var scope = new AccessScope(org, "synthetic-editor", "Synthetic", [new(unit, "Branch")], ActiveEstablishmentId: unit);
        var service = new ShowcaseService(new Access(scope), store, store, store);
        var selected = new ShowcaseSelection(["title", "description"], Description: "Texto revisado");
        var prepared = await service.ChangeAsync(propertyId, new(0, selected), "prepare");
        Assert.True(prepared.IsSuccess, prepared.Code); Assert.Equal("Prepared", prepared.Value!.State);
        Assert.Equal("Showcase.VersionConflict", (await service.ChangeAsync(propertyId, new(0, selected), "prepare")).Code);
        Assert.Equal("Access.Forbidden", (await new ShowcaseService(new Access(scope with { ActiveEstablishmentId = other }), store, store, store)
            .ChangeAsync(propertyId, new(1, selected), "authorize")).Code);
        Assert.Equal("Access.Forbidden", (await new ShowcaseService(new Access(scope, "PROPERTY_APPROVE"), store, store, store)
            .ChangeAsync(propertyId, new(1, selected), "authorize")).Code);
        var authorized = await service.ChangeAsync(propertyId, new(1, selected), "authorize");
        Assert.True(authorized.IsSuccess, authorized.Code); Assert.Equal("Authorized", authorized.Value!.State);
        Assert.Null(await ((IShowcaseStore)store).FindAsync(scope with { OrganizationId = Guid.NewGuid() }, propertyId, default));
        Assert.Null(await ((IShowcaseStore)store).FindAsync(scope with { Establishments = [new(other, "Other")], ActiveEstablishmentId = other }, propertyId, default));
        var withdrawn = await service.ChangeAsync(propertyId, new(2, selected), "withdraw");
        Assert.True(withdrawn.IsSuccess, withdrawn.Code); Assert.Equal("Withdrawn", withdrawn.Value!.State);
        await using var check = new NpgsqlConnection(admin); await check.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT count(*) FROM real_estate.showcase_history WHERE organization_id=@org AND property_id=@id", check);
        command.Parameters.AddWithValue("org", org); command.Parameters.AddWithValue("id", propertyId);
        Assert.Equal(3L, await command.ExecuteScalarAsync());
    }
}
