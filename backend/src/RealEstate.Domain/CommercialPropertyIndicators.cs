namespace RealEstate.Domain;

public sealed record CommercialPropertyIndicators(int? Bedrooms,int? Parking,decimal? CondominiumFee,string? CondominiumPeriod,
 decimal? PropertyTax,string? PropertyTaxPeriod)
{
    public static CommercialPropertyIndicators Resolve(PropertyV2 property,ListingAnnouncement? announcement)
    {
        var capture=property.Data.Capture;
        var captureValues=capture?.Values;
        // An explicit announcement amount, including zero, owns its period. If it is absent,
        // use the capture amount and period together; never combine periods across sources.
        var condominium=announcement?.CondominiumFee is {} condominiumFee
            ?(condominiumFee,announcement.CondominiumPeriod)
            :(captureValues?.CondominiumFee,captureValues?.CondominiumPeriod);
        var tax=announcement?.PropertyTax is {} propertyTax
            ?(propertyTax,announcement.PropertyTaxPeriod)
            :(captureValues?.PropertyTax,captureValues?.PropertyTaxPeriod);
        return new(capture?.Rooms?.Bedrooms,capture?.Rooms?.Parking,condominium.Item1,condominium.Item2,tax.Item1,tax.Item2);
    }
}
