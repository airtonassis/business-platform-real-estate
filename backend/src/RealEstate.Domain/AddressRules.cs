using System.Text.RegularExpressions;
namespace RealEstate.Domain;

/// <summary>Shared local rules. Syntax is not proof of postal existence or location.</summary>
public static class AddressRules
{
    private static readonly Regex PostalPattern=new("^[0-9]{5}-?[0-9]{3}$",RegexOptions.NonBacktracking);

    public static Outcome<string> NormalizePostalCode(string? value,bool required=true)
    {
        if(string.IsNullOrEmpty(value) && !required)return new(null);
        if(value is null || !PostalPattern.IsMatch(value))return Outcome<string>.Fail("Property.PostalCodeInvalid");
        return new(value.Replace("-",""));
    }

    public static bool CoordinatesValid(decimal? latitude,decimal? longitude,bool required)
    {
        if(latitude is null && longitude is null)return !required;
        return latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180;
    }
}
