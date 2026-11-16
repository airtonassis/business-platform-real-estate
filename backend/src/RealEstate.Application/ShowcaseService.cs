using RealEstate.Domain;

namespace RealEstate.Application;

public sealed class ShowcaseService(ICoreAccess access, IPropertyStoreV2 properties,
    IPropertyMediaStore media, IShowcaseStore store)
{
    public async Task<Outcome<ShowcaseEntry?>> FindAsync(Guid propertyId, CancellationToken token = default)
    {
        var read = await access.RequireAsync("properties", "PROPERTY_READ", token);
        if (!read.IsSuccess) return Outcome<ShowcaseEntry?>.Fail(read.Code!);
        var property = await properties.FindV2Async(read.Value!, propertyId, token);
        if (property is null) return Outcome<ShowcaseEntry?>.Fail("Resource.NotFound");
        if (read.Value!.ActiveEstablishmentId != property.Data.EstablishmentId ||
            read.Value.Establishments?.Any(e => e.Id == property.Data.EstablishmentId) != true)
            return Outcome<ShowcaseEntry?>.Fail("Access.Forbidden");
        var entry = await store.FindAsync(read.Value!, propertyId, token);
        if (entry is not null && (entry.State != "Authorized" || !ShowcaseRules.CanPrepare(property.Status)))
        {
            var edit = await access.RequireAsync("properties", "PROPERTY_UPDATE", token);
            var approve = await access.RequireAsync("properties", "PROPERTY_APPROVE", token);
            var permitted = new[] { edit, approve }.Any(g => g.IsSuccess &&
                g.Value!.OrganizationId == property.OrganizationId &&
                g.Value.ActiveEstablishmentId == property.Data.EstablishmentId &&
                g.Value.Establishments?.Any(e => e.Id == property.Data.EstablishmentId) == true);
            if (!permitted) return Outcome<ShowcaseEntry?>.Fail("Resource.NotFound");
        }
        return new(entry);
    }

    public async Task<Outcome<ShowcaseEntry>> ChangeAsync(Guid propertyId, ShowcaseChange command,
        string action, CancellationToken token = default)
    {
        if (action is not ("prepare" or "authorize" or "withdraw"))
            return Outcome<ShowcaseEntry>.Fail("Showcase.InvalidAction");
        var right = action == "authorize" ? "PROPERTY_APPROVE" : "PROPERTY_UPDATE";
        var grant = await access.RequireAsync("properties", right, token);
        if (!grant.IsSuccess) return Outcome<ShowcaseEntry>.Fail(grant.Code!);
        var scope = grant.Value!;
        if (string.IsNullOrWhiteSpace(scope.Subject) || scope.OrganizationId == Guid.Empty)
            return Outcome<ShowcaseEntry>.Fail("Context.Invalid");
        var property = await properties.FindV2Async(scope, propertyId, token);
        if (property is null) return Outcome<ShowcaseEntry>.Fail("Resource.NotFound");
        if (property.OrganizationId != scope.OrganizationId) return Outcome<ShowcaseEntry>.Fail("Resource.NotFound");
        if (scope.ActiveEstablishmentId != property.Data.EstablishmentId ||
            scope.Establishments?.Any(e => e.Id == property.Data.EstablishmentId) != true)
            return Outcome<ShowcaseEntry>.Fail("Access.Forbidden");
        if (command.Version < 0) return Outcome<ShowcaseEntry>.Fail("Showcase.VersionConflict");
        if (action != "withdraw" && !ShowcaseRules.CanPrepare(property.Status))
            return Outcome<ShowcaseEntry>.Fail("Showcase.NotEligible");
        if (action != "withdraw" && !ShowcaseRules.Valid(command.Selection))
            return Outcome<ShowcaseEntry>.Fail("Showcase.InvalidSelection");
        if (action == "authorize" && !ShowcaseRules.CanAuthorize(property.Status, command.Selection, true))
            return Outcome<ShowcaseEntry>.Fail("Showcase.NotEligible");
        if (action != "withdraw" && command.Selection.MediaIds is { Count: > 0 } ids)
        {
            var gallery = await media.ListMediaAsync(scope, propertyId, token);
            if (gallery is null || ids.Except(gallery.Items.Select(x => x.Id)).Any())
                return Outcome<ShowcaseEntry>.Fail("Showcase.InvalidSelection");
        }
        return await store.ChangeAsync(scope, propertyId, command.Version,
            action == "withdraw" ? null : command.Selection, action, token);
    }
}
