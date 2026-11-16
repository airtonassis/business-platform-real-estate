using RealEstate.Domain;
namespace RealEstate.Web;

// HTML presentation metadata only. Existing domain vocabularies remain authoritative.
public static class CadastroPresentation
{
    public sealed record Category(int Id,string Name,string Icon,string Availability,string Requirement);
    private static readonly string[] Icons=["home","pin","ruler","rooms","tools","wallet","users","file","image","sofa","building","percent","shield","exchange"];
    public static readonly Category[] Categories=IdentificationRules.Categories.Select((name,index)=>new Category(index+1,name,Icons[index],(index+1) switch{1 or 2 or 3 or 4 or 5 or 10 or 13=>"Disponível",6 or 7 or 9 or 11 or 12=>"Parcial",_=>"Prevista"},(index+1) switch{8=>"later",14=>"integrated",11=>"condominium",13=>"rental",_=>"always"})).ToArray();
    // Approved human revalidation: presentation changes, stable category IDs/contracts do not.
    public static readonly int[] NavigationOrder=[1,2,3,4,10,5,6,7,8,9,11,12,13,14];
    public static IEnumerable<Category> NavigationCategories=>NavigationOrder.Select(id=>Categories.Single(c=>c.Id==id));
    public static bool IsApplicable(Category category,PropertyInputV2 input)=>CaptureAdaptation.Applies(category.Requirement,input);
    public static readonly object Adaptation=new{
        version=CaptureAdaptation.Version,
        conditions=CaptureAdaptation.Conditions,
        collectiveSelection=CollectiveInheritance.SelectionRule,
        categories=NavigationCategories.ToArray(),
        ruralTypes=IdentificationRules.RuralTypes,
        commercialTypes=PropertyRules.Types.Where(CaptureCatalog.Commercial).ToArray(),
        habitableTypes=PropertyRules.Types.Where(t=>!CaptureCatalog.Commercial(t)&&t is not("Terreno" or "Empreendimento")).ToArray(),
        salePurposes=IdentificationRules.Purposes.Where(IdentificationRules.HasSale).ToArray(),
        rentalPurposes=IdentificationRules.Purposes.Where(IdentificationRules.HasRental).ToArray(),
        urbanAddressFields=new[]{"Street","Number","WithoutNumber","Complement","Neighborhood"}};
}
