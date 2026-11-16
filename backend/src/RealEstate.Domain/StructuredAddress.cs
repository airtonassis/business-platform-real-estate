using System.Text.RegularExpressions;
namespace RealEstate.Domain;

public sealed record StructuredAddress(string? LegacyText=null,string? PostalCode=null,string? MunicipalityIbgeCode=null,
    string? State=null,string? Municipality=null,string? Street=null,string? Number=null,bool? WithoutNumber=null,
    string? Complement=null,string? Neighborhood=null,string? Country=null,decimal? Latitude=null,decimal? Longitude=null,string? CoordinateSource=null);

public static class StructuredAddressRules
{
    public static readonly string[] States=["AC","AL","AP","AM","BA","CE","DF","ES","GO","MA","MT","MS","MG","PA","PB","PR","PE","PI","RJ","RN","RS","RO","RR","SC","SP","SE","TO"];
    private static string? Text(string? value)=>string.IsNullOrWhiteSpace(value)?null:value.Trim();
    public static Outcome<StructuredAddress> Validate(StructuredAddress? input)
    {
        var a=input??new();
        var postal=AddressRules.NormalizePostalCode(Text(a.PostalCode),required:false);
        if(!postal.IsSuccess)return Outcome<StructuredAddress>.Fail(postal.Code!);
        a=a with{PostalCode=postal.Value,State=Text(a.State)?.ToUpperInvariant(),MunicipalityIbgeCode=Text(a.MunicipalityIbgeCode),
            Municipality=Text(a.Municipality),Street=Text(a.Street),Number=Text(a.Number),Complement=Text(a.Complement),
            Neighborhood=Text(a.Neighborhood),Country=Text(a.Country)?.ToUpperInvariant(),CoordinateSource=Text(a.CoordinateSource)};
        // Preserve legacy text byte-for-byte; never parse or silently rewrite an old address.
        if(a.LegacyText is not null && (string.IsNullOrWhiteSpace(a.LegacyText)||a.LegacyText.Length>500))return Outcome<StructuredAddress>.Fail("Address.LegacyTextInvalid");
        if(a.State is not null && !States.Contains(a.State))return Outcome<StructuredAddress>.Fail("Address.StateInvalid");
        if(a.MunicipalityIbgeCode is not null && !Regex.IsMatch(a.MunicipalityIbgeCode,"^[0-9]{7}$",RegexOptions.NonBacktracking))return Outcome<StructuredAddress>.Fail("Address.MunicipalityCodeInvalid");
        if(a.WithoutNumber==true && a.Number is not null)return Outcome<StructuredAddress>.Fail("Address.NumberConflict");
        if(a.Country is not null && !Regex.IsMatch(a.Country,"^[A-Z]{2}$",RegexOptions.NonBacktracking))return Outcome<StructuredAddress>.Fail("Address.CountryInvalid");
        if(!AddressRules.CoordinatesValid(a.Latitude,a.Longitude,required:false))return Outcome<StructuredAddress>.Fail("Property.LocationInvalid");
        if(a.Municipality?.Length>200 || a.Street?.Length>300 || a.Number?.Length>50 || a.Complement?.Length>200 || a.Neighborhood?.Length>200 || a.CoordinateSource?.Length>100)return Outcome<StructuredAddress>.Fail("Address.TextTooLong");
        return new(a);
    }
    public static bool LegacyCompatible(string? text,string? postal,decimal? lat,decimal? lon)=>
        !string.IsNullOrWhiteSpace(text)&&text.Length<=500&&AddressRules.NormalizePostalCode(postal).IsSuccess&&AddressRules.CoordinatesValid(lat,lon,required:true);
}

public sealed record PropertyInputV2(string? Code=null,string? Title=null,Guid? OwnerId=null,string? Category=null,string? Type=null,string? Purpose=null,decimal? Price=null,StructuredAddress? Address=null,Guid? EstablishmentId=null,string? Summary=null,string? InternalReference=null,bool? IsInCondominium=null,CaptureData? Capture=null);
public sealed record PropertyV2(Guid Id,Guid OrganizationId,PropertyInputV2 Data,long Version,string Status,string? CommercialCode=null);
public sealed record WorkspaceV2(IReadOnlyList<PropertyV2> Properties,IReadOnlyList<Owner> Owners,IReadOnlyList<EstablishmentAccess>? Establishments=null);
public interface IPropertyStoreV2
{
    Task<WorkspaceV2> ListV2Async(AccessScope scope,CancellationToken token);
    Task<PropertyV2?> FindV2Async(AccessScope scope,Guid id,CancellationToken token);
    Task<Outcome<PropertyV2>> SaveV2Async(AccessScope scope,PropertyInputV2 input,Guid? id,long version,CancellationToken token);
    Task<Outcome<PropertyV2>> SubmitForReviewAsync(AccessScope scope,Guid id,long version,CancellationToken token)=>Task.FromResult(Outcome<PropertyV2>.Fail("Dependency.Unavailable"));
    Task<Outcome<PropertyV2>> ApproveAsync(AccessScope scope,Guid id,long version,CancellationToken token)=>Task.FromResult(Outcome<PropertyV2>.Fail("Dependency.Unavailable"));
    Task<Outcome<PropertyV2>> CopyV2Async(AccessScope scope,PropertyInputV2 input,PropertyCopySource source,CancellationToken token)=>Task.FromResult(Outcome<PropertyV2>.Fail("Dependency.Unavailable"));
}
public sealed record PropertyCopySource(Guid Id,long Version,string MediaRevision,Guid[]? Excluded=null,Guid? Cover=null,Guid[]? Order=null,IReadOnlyList<MediaFile>? Files=null);
