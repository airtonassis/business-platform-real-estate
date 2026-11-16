using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using RealEstate.Domain;

namespace RealEstate.Infrastructure;

public sealed partial class PostgresPropertyStore : IShowcaseStore
{
    private static ShowcaseEntry ReadShowcase(NpgsqlDataReader r) => new(
        r.GetGuid(0), r.GetGuid(1), r.GetGuid(2), r.GetInt64(3), r.GetString(4),
        JsonSerializer.Deserialize<ShowcaseSelection>(r.GetString(5))!, r.GetString(6),
        r.GetFieldValue<DateTimeOffset>(7));

    async Task<ShowcaseEntry?> IShowcaseStore.FindAsync(AccessScope scope, Guid propertyId, CancellationToken token)
    {
        await using var c = await dataSource.OpenConnectionAsync(token);
        await using var tx = await Begin(c, scope, token);
        await using var cmd = new NpgsqlCommand("SELECT property_id,organization_id,establishment_id,version,state,selection::text,updated_by,updated_at FROM real_estate.showcase_entries WHERE organization_id=@org AND property_id=@id", c, tx);
        cmd.Parameters.AddWithValue("org", scope.OrganizationId);
        cmd.Parameters.AddWithValue("id", propertyId);
        ShowcaseEntry? entry;
        await using (var r = await cmd.ExecuteReaderAsync(token)) entry = await r.ReadAsync(token) ? ReadShowcase(r) : null;
        await tx.CommitAsync(token);
        return entry;
    }

    public async Task<Outcome<ShowcaseEntry>> ChangeAsync(AccessScope scope, Guid propertyId, long version,
        ShowcaseSelection? selection, string action, CancellationToken token)
    {
        await using var c = await dataSource.OpenConnectionAsync(token);
        await using var tx = await Begin(c, scope, token);
        Guid unit; string status;
        await using (var parent = new NpgsqlCommand("SELECT establishment_id,status FROM real_estate.properties WHERE organization_id=@org AND id=@id FOR UPDATE", c, tx))
        {
            parent.Parameters.AddWithValue("org", scope.OrganizationId);
            parent.Parameters.AddWithValue("id", propertyId);
            await using var r = await parent.ExecuteReaderAsync(token);
            if (!await r.ReadAsync(token)) return Outcome<ShowcaseEntry>.Fail("Resource.NotFound");
            unit = r.GetGuid(0); status = r.GetString(1);
        }
        if (unit != scope.ActiveEstablishmentId || scope.Establishments?.Any(e => e.Id == unit) != true)
            return Outcome<ShowcaseEntry>.Fail("Access.Forbidden");
        if (action != "withdraw" && !ShowcaseRules.CanPrepare(status))
            return Outcome<ShowcaseEntry>.Fail("Showcase.NotEligible");
        if (selection?.MediaIds is { Count: > 0 } ids)
        {
            await using var check = new NpgsqlCommand("SELECT count(*) FROM real_estate.property_media WHERE organization_id=@org AND property_id=@id AND id=ANY(@media)", c, tx);
            check.Parameters.AddWithValue("org", scope.OrganizationId);
            check.Parameters.AddWithValue("id", propertyId);
            check.Parameters.AddWithValue("media", ids.ToArray());
            if ((long)(await check.ExecuteScalarAsync(token))! != ids.Count)
                return Outcome<ShowcaseEntry>.Fail("Showcase.InvalidSelection");
        }
        long currentVersion = 0; string? currentState = null; ShowcaseSelection? oldSelection = null;
        await using (var previous = new NpgsqlCommand("SELECT version,state,selection::text FROM real_estate.showcase_entries WHERE organization_id=@org AND property_id=@id FOR UPDATE", c, tx))
        {
            previous.Parameters.AddWithValue("org", scope.OrganizationId);
            previous.Parameters.AddWithValue("id", propertyId);
            await using var r = await previous.ExecuteReaderAsync(token);
            if (await r.ReadAsync(token))
            {
                currentVersion = r.GetInt64(0); currentState = r.GetString(1);
                oldSelection = JsonSerializer.Deserialize<ShowcaseSelection>(r.GetString(2));
            }
        }
        if (currentVersion != version) return Outcome<ShowcaseEntry>.Fail("Showcase.VersionConflict");
        if (action == "withdraw" && currentState is null) return Outcome<ShowcaseEntry>.Fail("Resource.NotFound");
        if (action == "authorize" && currentState != "Prepared") return Outcome<ShowcaseEntry>.Fail("Showcase.StatusConflict");
        if (action == "authorize" && !Equals(selection, oldSelection) &&
            JsonSerializer.Serialize(selection) != JsonSerializer.Serialize(oldSelection))
            return Outcome<ShowcaseEntry>.Fail("Showcase.SelectionConflict");
        var nextState = action switch { "prepare" => "Prepared", "authorize" => "Authorized", _ => "Withdrawn" };
        var nextSelection = action == "withdraw" ? oldSelection! : selection!;
        var json = JsonSerializer.Serialize(nextSelection);
        await using (var write = new NpgsqlCommand(currentState is null
            ? "INSERT INTO real_estate.showcase_entries(organization_id,property_id,establishment_id,version,state,selection,updated_by) VALUES(@org,@id,@unit,1,@state,@selection,@actor)"
            : "UPDATE real_estate.showcase_entries SET version=version+1,state=@state,selection=@selection,updated_by=@actor,updated_at=clock_timestamp() WHERE organization_id=@org AND property_id=@id", c, tx))
        {
            write.Parameters.AddWithValue("org", scope.OrganizationId); write.Parameters.AddWithValue("id", propertyId);
            write.Parameters.AddWithValue("unit", unit); write.Parameters.AddWithValue("state", nextState);
            write.Parameters.AddWithValue("selection", NpgsqlDbType.Jsonb, json); write.Parameters.AddWithValue("actor", scope.Subject);
            await write.ExecuteNonQueryAsync(token);
        }
        var nextVersion = currentVersion + 1;
        await using (var audit = new NpgsqlCommand("INSERT INTO real_estate.showcase_history(organization_id,id,property_id,establishment_id,version,action,state,selection,actor) VALUES(@org,@event,@id,@unit,@version,@action,@state,@selection,@actor)", c, tx))
        {
            audit.Parameters.AddWithValue("org", scope.OrganizationId); audit.Parameters.AddWithValue("event", Guid.NewGuid());
            audit.Parameters.AddWithValue("id", propertyId); audit.Parameters.AddWithValue("unit", unit);
            audit.Parameters.AddWithValue("version", nextVersion); audit.Parameters.AddWithValue("action", action);
            audit.Parameters.AddWithValue("state", nextState); audit.Parameters.AddWithValue("selection", NpgsqlDbType.Jsonb, json);
            audit.Parameters.AddWithValue("actor", scope.Subject); await audit.ExecuteNonQueryAsync(token);
        }
        ShowcaseEntry entry;
        await using (var read = new NpgsqlCommand("SELECT property_id,organization_id,establishment_id,version,state,selection::text,updated_by,updated_at FROM real_estate.showcase_entries WHERE organization_id=@org AND property_id=@id", c, tx))
        {
            read.Parameters.AddWithValue("org", scope.OrganizationId); read.Parameters.AddWithValue("id", propertyId);
            await using var r = await read.ExecuteReaderAsync(token); await r.ReadAsync(token); entry = ReadShowcase(r);
        }
        await tx.CommitAsync(token);
        return new(entry);
    }
}
