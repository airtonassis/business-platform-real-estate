namespace RealEstate.Domain;

// Internal web listing. Public Workspace/v1/v2 contracts remain unchanged.
public sealed record PropertyListQuery
{
    public string? Search {get;init;}
    public string? Code {get;init;}
    public string? Title {get;init;}
    public string? Municipality {get;init;}
    public string? State {get;init;}
    public string? Neighborhood {get;init;}
    public string? Category {get;init;}
    public string? Type {get;init;}
    public string? Purpose {get;init;}
    public string? Status {get;init;}
    public decimal? MinPrice {get;init;}
    public decimal? MaxPrice {get;init;}
    public Guid? EstablishmentId {get;init;}
    public bool Commercial {get;init;}
    public int? MinBedrooms {get;init;}
    public int? MinParking {get;init;}
    public decimal? MinArea {get;init;}
    public decimal? MaxArea {get;init;}
    public string? AreaKind {get;init;}
    public string? AreaUnit {get;init;}
    public string? RegionalVariant {get;init;}
    public string? FeatureGroup {get;init;}
    public string? FeatureOption {get;init;}
    public string Sort {get;init;}="updated";
    public bool Descending {get;init;}=true;
    public int Page {get;init;}=1;
    public const int PageSize=25;
    public static readonly string[] SortFields=["updated","code","title","municipality","state","neighborhood","category","type","purpose","price","status"];
    public bool IsValid=>Page is >=1 and <=1000000 && SortFields.Contains(Sort,StringComparer.Ordinal)
        && new[]{Search,Code,Title,Municipality,State,Neighborhood,Category,Type,Purpose,Status}.All(x=>x is null||x.Length<=200)
        && (string.IsNullOrWhiteSpace(Status)||PropertyLifecycle.Labels.ContainsKey(Status))
        && (MinPrice is null||MinPrice>=0) && (MaxPrice is null||MaxPrice>=0) && !(MinPrice>MaxPrice)
        && (MinBedrooms is null or >=0 and <=100000) && (MinParking is null or >=0 and <=100000)
        && (MinArea is null or >=0) && (MaxArea is null or >=0) && !(MinArea>MaxArea)
        && (!(MinArea.HasValue||MaxArea.HasValue)||(AreaKind is "total" or "built" or "private" or "usable" or "land")&&(AreaUnit is "m2" or "ha" or "alqueire")&&(AreaUnit!="alqueire"||!string.IsNullOrWhiteSpace(RegionalVariant)&&RegionalVariant.Length<=200))
        && (FeatureGroup is null&&FeatureOption is null||CaptureCatalog.Groups.Any(g=>g.Id==FeatureGroup&&g.Options.Any(o=>o.Id==FeatureOption)));
    public bool HasFilters=>new[]{Search,Code,Title,Municipality,State,Neighborhood,Category,Type,Purpose,Status}.Any(x=>!string.IsNullOrWhiteSpace(x))||MinPrice.HasValue||MaxPrice.HasValue||EstablishmentId.HasValue;
    public IEnumerable<(string Key,string Value)> TextFilters()=>new Dictionary<string,string?>{{"code",Code},{"title",Title},{"municipality",Municipality},{"state",State},{"neighborhood",Neighborhood},{"category",Category},{"type",Type},{"purpose",Purpose},{"status",Status}}.Where(x=>!string.IsNullOrWhiteSpace(x.Value)).Select(x=>(x.Key,x.Value!.Trim()));
}
public sealed record ListingAnnouncement(Guid Id,string Purpose,string? CommercialCode,OperationValue? Values=null,bool LegacyAnchor=true,
 decimal? CondominiumFee=null,string? CondominiumPeriod=null,decimal? PropertyTax=null,string? PropertyTaxPeriod=null);
public sealed record PropertyListPage(IReadOnlyList<PropertyV2> Properties,long Total,IReadOnlyList<EstablishmentAccess> Establishments,
 IReadOnlyDictionary<Guid,IReadOnlyList<ListingAnnouncement>>? Announcements=null);
public interface IPropertyListingStore
{
    Task<PropertyListPage> ListPageAsync(AccessScope scope,PropertyListQuery query,CancellationToken token);
}
