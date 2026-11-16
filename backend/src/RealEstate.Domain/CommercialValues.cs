namespace RealEstate.Domain;

public sealed record ActiveOperationValue(string Key,string Label,OperationValue Value);

// Presentation/consumption projection. Raw v2 data deliberately retains inactive history.
// Legacy Price is never a fallback for an authorized or advertised amount.
public static class CommercialValues
{
    public static IEnumerable<ActiveOperationValue> Active(PropertyInputV2 property)
    {
        if(property.Capture?.Values is not {} values)yield break;
        if(IdentificationRules.HasSale(property.Purpose))yield return new("sale","Venda",values.Sale);
        if(IdentificationRules.HasRental(property.Purpose))yield return new("rental","Locação",values.Rental);
        if(property.Purpose=="Temporada")yield return new("seasonal","Temporada",values.Seasonal);
    }
}
