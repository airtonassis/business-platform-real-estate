namespace RealEstate.Domain;

public sealed record PropertyInput(string Code,string Title,Guid OwnerId,string Category,string Type,string Purpose,string Address,string PostalCode,decimal? Latitude,decimal? Longitude,decimal Price,Guid? EstablishmentId=null);
public sealed record Outcome<T>(T? Value,string? Code=null)
{
    public bool IsSuccess=>Code is null;
    public static Outcome<T> Fail(string code)=>new(default,code);
}
public static class PropertyRules
{
    public static readonly string[] Types=["Casa","Apartamento","Cobertura","Kitnet","Studio","Loft","Sala Comercial","Galpão","Terreno","Chácara","Fazenda","Sítio","Prédio Comercial","Empreendimento","Imóvel Industrial"];
    public static readonly string[] Purposes=["Venda","Locação","Temporada","Permuta","Lançamento"];
    public static Outcome<PropertyInput> Validate(PropertyInput p)
    {
        if(string.IsNullOrWhiteSpace(p.Title)||p.Title.Length>200)return Outcome<PropertyInput>.Fail("Property.TitleRequired");
        if(p.OwnerId==Guid.Empty)return Outcome<PropertyInput>.Fail("Property.OwnerRequired");
        if(p.Price<=0||p.Price>999999999999.99m||decimal.Round(p.Price,2)!=p.Price)return Outcome<PropertyInput>.Fail("Property.PriceInvalid");
        var postal=AddressRules.NormalizePostalCode(p.PostalCode);
        if(!postal.IsSuccess)return Outcome<PropertyInput>.Fail(postal.Code!);
        if(!AddressRules.CoordinatesValid(p.Latitude,p.Longitude,required:true))return Outcome<PropertyInput>.Fail("Property.LocationInvalid");
        if(!Types.Contains(p.Type))return Outcome<PropertyInput>.Fail("Property.TypeInvalid");
        if(!Purposes.Contains(p.Purpose))return Outcome<PropertyInput>.Fail("Property.PurposeInvalid");
        if(string.IsNullOrWhiteSpace(p.Code)||p.Code.Length>50 || string.IsNullOrWhiteSpace(p.Category)||p.Category.Length>100 || string.IsNullOrWhiteSpace(p.Address)||p.Address.Length>500)return Outcome<PropertyInput>.Fail("Property.InvalidInput");
        return new(p with {Code=p.Code.Trim(),Title=p.Title.Trim(),Category=p.Category.Trim(),Address=p.Address.Trim(),PostalCode=postal.Value!});
    }
}
public sealed record EstablishmentAccess(Guid Id,string Name,bool IsHeadquarters=false);
public sealed record AccessScope(Guid OrganizationId,string Subject,string OrganizationName,IReadOnlyList<EstablishmentAccess>? Establishments=null,bool LegacyManager=false,Guid? ActiveEstablishmentId=null,Guid? MembershipId=null);
public sealed record Owner(Guid Id,string Name);
public sealed record Property(Guid Id,Guid OrganizationId,PropertyInput Data,long Version,string Status);
public sealed record Workspace(IReadOnlyList<Property> Properties,IReadOnlyList<Owner> Owners,IReadOnlyList<EstablishmentAccess>? Establishments=null);
public interface ICoreAccess { Task<Outcome<AccessScope>> RequireAsync(string resource,string permission,CancellationToken token); }
public interface IPropertyStore
{
    Task<Workspace> ListAsync(AccessScope scope,CancellationToken token);
    Task<Property?> FindAsync(AccessScope scope,Guid id,CancellationToken token);
    Task<Outcome<Property>> SaveAsync(AccessScope scope,PropertyInput input,Guid? id,long version,CancellationToken token);
    Task<Outcome<Owner>> CreateOwnerAsync(AccessScope scope,string name,CancellationToken token);
}
