namespace RealEstate.Domain;

// Internal selection and disclosure decision. No external delivery is performed here.
public sealed record ShowcaseSelection(IReadOnlyList<string> Fields, IReadOnlyList<Guid>? MediaIds = null,
    string? Description = null);
public sealed record ShowcaseEntry(Guid PropertyId, Guid OrganizationId, Guid EstablishmentId,
    long Version, string State, ShowcaseSelection Selection, string UpdatedBy, DateTimeOffset UpdatedAt);
public sealed record ShowcaseChange(long Version, ShowcaseSelection Selection);

public static class ShowcaseRules
{
    public static readonly IReadOnlySet<string> AllowedFields = new HashSet<string>(StringComparer.Ordinal)
    { "title", "commercialCode", "price", "purpose", "type", "category", "municipality", "state",
      "neighborhood", "description", "area", "bedrooms", "suites", "bathrooms", "parking" };

    public static bool CanPrepare(string status) => status is PropertyLifecycle.Available or
        PropertyLifecycle.InNegotiation or PropertyLifecycle.Negotiated;

    public static bool Valid(ShowcaseSelection? selection) => selection is not null &&
        selection.Fields is { Count: > 0 and <= 15 } &&
        selection.Fields.Distinct(StringComparer.Ordinal).Count() == selection.Fields.Count &&
        selection.Fields.All(AllowedFields.Contains) &&
        (selection.Description is null || selection.Description.Length <= 2000) &&
        (!selection.Fields.Contains("description") || !string.IsNullOrWhiteSpace(selection.Description)) &&
        (selection.MediaIds is null || selection.MediaIds.Count <= MediaRules.FileCountLimit &&
         selection.MediaIds.All(id => id != Guid.Empty) && selection.MediaIds.Distinct().Count() == selection.MediaIds.Count);

    public static bool CanAuthorize(string propertyStatus, ShowcaseSelection? selection, bool explicitDecision) =>
        explicitDecision && CanPrepare(propertyStatus) && Valid(selection);

    public static bool CanPresentAsAvailable(string status) => status is PropertyLifecycle.Available or PropertyLifecycle.InNegotiation;
}

public interface IShowcaseStore
{
    Task<ShowcaseEntry?> FindAsync(AccessScope scope, Guid propertyId, CancellationToken token);
    Task<Outcome<ShowcaseEntry>> ChangeAsync(AccessScope scope, Guid propertyId, long version,
        ShowcaseSelection? selection, string action, CancellationToken token);
}
