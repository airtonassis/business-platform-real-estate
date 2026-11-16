using System.Globalization;
using System.Text;
namespace RealEstate.Domain;

// Typed, optional capture data. JSON storage does not replace the domain contract.
public sealed class CaptureData
{
    public PolicyBinding? Policy {get;set;}
    public AreaData Areas {get;set;}=new();
    public RoomData Rooms {get;set;}=new();
    public ValueData Values {get;set;}=new();
    public AcquisitionData Acquisition {get;set;}=new();
    public PreliminaryFees Fees {get;set;}=new();
    public CondominiumLink Condominium {get;set;}=new();
    public string? FurnishingState {get;set;}
    public string? InspectionNotes {get;set;}
    public List<CaptureSelection> Selections {get;set;}=[];
    public List<MediaAnnotation> Media {get;set;}=[];
    // Server-owned snapshots; submitted history is never trusted.
    public List<PriceRevision> PriceHistory {get;set;}=[];
}
public sealed class AreaData
{
    public decimal? Total {get;set;} public decimal? Built {get;set;} public decimal? Private {get;set;} public decimal? Usable {get;set;} public decimal? Land {get;set;}
    public string? Unit {get;set;} public string? RegionalVariant {get;set;}
    public decimal? Front {get;set;} public decimal? Back {get;set;} public decimal? Left {get;set;} public decimal? Right {get;set;}
    public string? LegalReserve {get;set;} public string? PreservationArea {get;set;}
}
public sealed class RoomData
{
    public int? Bedrooms {get;set;} public int? Suites {get;set;} public int? Bathrooms {get;set;} public int? Washrooms {get;set;} public int? Parking {get;set;}
    public int? Living {get;set;} public int? Dining {get;set;} public int? Tv {get;set;} public int? Offices {get;set;} public int? Kitchens {get;set;} public int? Laundry {get;set;}
    public int? Guests {get;set;} public int? Beds {get;set;}
}
public sealed class OperationValue
{
    public decimal? Authorized {get;set;} public bool DifferentAdvertised {get;set;} public decimal? Advertised {get;set;} public string? Period {get;set;}
    public bool? Negotiable {get;set;} public string? Conditions {get;set;}
}
public sealed class ValueData
{
    public OperationValue Sale {get;set;}=new(); public OperationValue Rental {get;set;}=new(); public OperationValue Seasonal {get;set;}=new();
    public decimal? PropertyTax {get;set;} public string? PropertyTaxPeriod {get;set;} public decimal? CondominiumFee {get;set;} public string? CondominiumPeriod {get;set;}
    public bool? Financing {get;set;} public bool? Negotiable {get;set;} public string? Conditions {get;set;}
    public bool? AcceptsExchange {get;set;} public string? ExchangeNotes {get;set;}
}
public sealed class AcquisitionData
{
    public string? ResponsibleSubject {get;set;}
    public string? Origin {get;set;} public DateOnly? Date {get;set;} public string? Status {get;set;} public string? InternalNotes {get;set;}
    public bool? Exclusive {get;set;} public DateOnly? ExclusiveStart {get;set;} public DateOnly? ExclusiveEnd {get;set;} public bool? PublicationAuthorized {get;set;}
}
public sealed class PreliminaryFees
{
    public List<CommissionAllocation> Allocations {get;set;}=[];
    public decimal? SalePercent {get;set;} public decimal? SaleAmount {get;set;}
    public decimal? RentalPercent {get;set;} public decimal? RentalAmount {get;set;}
    public decimal? AdministrationPercent {get;set;} public decimal? AdministrationAmount {get;set;}
    public decimal? InternalAmount {get;set;} public string? Notes {get;set;}
}
public sealed class CondominiumLink
{
    public bool? Inherit {get;set;} public long? ObservedVersion {get;set;}
    public Guid? Id {get;set;} public string? Tower {get;set;} public string? Block {get;set;} public string? Unit {get;set;} public string? Notes {get;set;}
}
public sealed class CaptureSelection
{
    public bool? SharedOverride {get;set;}
    public string GroupId {get;set;}="";public string OptionId {get;set;}="";public string? Label {get;set;} public bool Selected {get;set;} public string? Detail {get;set;}
    public int? Quantity {get;set;} public string? Condition {get;set;} public string? Inclusion {get;set;} public Guid? PhotoId {get;set;}
}
public sealed record PriceRevision(long Version,string Actor,DateTimeOffset At,ValueData Values);
public sealed class MediaAnnotation
{
    public Guid MediaId {get;set;} public string? Caption {get;set;} public string? Room {get;set;} public string? Differential {get;set;}
    public string? InternalNotes {get;set;} public string? Visibility {get;set;}
}
public sealed record CaptureOption(string Id,string Label);
public sealed record CaptureGroup(string Id,string Label,int Category,string Applicability,CaptureOption[] Options);
public static class CaptureCatalog
{
    static CaptureOption[] Options(params string[] pairs)=>pairs.Select(x=>{var p=x.Split('|');return new CaptureOption(p[0],p[1]);}).ToArray();
    public static readonly CaptureGroup[] Groups=[
        new("rooms","Ambientes adicionais",4,"always",Options("balcony|Varanda","garden|Jardim","storage|Depósito","yard|Quintal")),
        new("security","Segurança e proteção",5,"always",Options("alarm|Alarme","cameras|Câmeras","fence|Cerca elétrica","gate|Portão eletrônico")),
        new("comfort","Conforto e climatização",5,"always",Options("air|Ar-condicionado","heating|Aquecimento","fireplace|Lareira")),
        new("private-leisure","Lazer privativo",5,"always",Options("pool|Piscina privativa","barbecue|Churrasqueira privativa","gourmet|Espaço gourmet privativo")),
        new("technology","Tecnologia e sustentabilidade",5,"always",Options("solar|Energia solar","water|Reaproveitamento de água","automation|Automação")),
        new("finishes","Acabamentos",5,"always",Options("porcelain|Porcelanato","wood|Madeira","stone|Pedra")),
        new("technical","Instalações técnicas comerciais e industriais",5,"commercial",Options("loading|Doca","three-phase|Energia trifásica","freight|Elevador de carga")),
        new("rural","Benfeitorias rurais",5,"rural",Options("barn|Galpão rural","corral|Curral","well|Poço","irrigation|Irrigação")),
        new("exchange","Preferências de permuta",6,"exchange",Options("apartment|Apartamento","house|Casa","land|Terreno","commercial|Imóvel comercial","rural|Imóvel rural","vehicle|Veículo","other-asset|Outro bem")),
        new("furniture-living","Sala",10,"always",Options("sofa|Sofá","table|Mesa","chairs|Cadeiras","tv|Televisor")),
        new("furniture-kitchen","Cozinha e lavanderia",10,"always",Options("fridge|Geladeira","stove|Fogão","washer|Máquina de lavar","cabinet|Armário")),
        new("furniture-bedrooms","Dormitórios",10,"always",Options("bed|Cama","wardrobe|Guarda-roupa","desk|Escrivaninha")),
        new("equipment","Outros equipamentos",10,"always",Options("tools|Ferramentas","pump|Bomba")),
        new("collective-security","Segurança e acesso coletivos",11,"condominium",Options("concierge24|Portaria 24h","virtual|Portaria virtual","access|Controle de acesso","cameras|Câmeras","fence|Cerca elétrica","biometrics|Biometria")),
        new("collective-leisure","Lazer e convivência coletivos",11,"condominium",Options("pool|Piscina","heated-pool|Piscina aquecida","barbecue|Churrasqueira","party|Salão de festas","gourmet|Espaço gourmet","playground|Playground","toys|Brinquedoteca","court|Quadra","gym|Academia","sauna|Sauna")),
        new("collective-services","Serviços e conveniências",11,"condominium",Options("elevator|Elevadores","laundry|Lavanderia coletiva","coworking|Coworking","market|Mercadinho","bikes|Bicicletário","pet|Espaço pet","generator|Gerador")),
        new("collective-infrastructure","Infraestrutura e sustentabilidade coletivas",11,"condominium",Options("solar|Energia solar coletiva","water|Reaproveitamento de água","recycling|Coleta seletiva","charging|Carregador elétrico","accessibility|Acessibilidade","visitors|Vagas visitantes")),
        new("guarantees","Modalidades aceitas pelo proprietário",13,"rental",Options("guarantor|Fiador","insurance|Seguro-fiança","cash|Caução em dinheiro","capitalization|Título de capitalização","bank|Garantia por instituição financeira","none|Sem garantia"))];
    public static bool Commercial(string? type)=>type is "Sala Comercial" or "Galpão" or "Prédio Comercial" or "Imóvel Industrial";
}
public static class CaptureRules
{
    public static string Normalize(string text)=>string.Concat(text.Normalize(NormalizationForm.FormD).Where(c=>CharUnicodeInfo.GetUnicodeCategory(c)!=UnicodeCategory.NonSpacingMark)).Trim().ToUpperInvariant();
    public static Outcome<CaptureData> Validate(CaptureData? input)
    {
        var c=input??new();
        if(c.Fees is not null&&!CommercialPolicyRules.AllocationsValid(c.Fees.Allocations))return Outcome<CaptureData>.Fail("Policy.AllocationInvalid");
        if(c.Areas is null||c.Rooms is null||c.Values is null||c.Acquisition is null||c.Fees is null||c.Condominium is null||c.Selections is null||c.Media is null||c.Selections.Any(s=>s is null)||c.Media.Any(m=>m is null)||c.Values.Sale is null||c.Values.Rental is null||c.Values.Seasonal is null)return Outcome<CaptureData>.Fail("Capture.Invalid");
        static bool BadText(object obj)=>obj.GetType().GetProperties().Where(p=>p.PropertyType==typeof(string)).Any(p=>(p.GetValue(obj) as string)?.Length>2000);
        if(new object[]{c,c.Areas,c.Values,c.Values.Sale,c.Values.Rental,c.Values.Seasonal,c.Acquisition,c.Fees,c.Condominium}.Any(BadText))return Outcome<CaptureData>.Fail("Capture.TextTooLong");
        if(c.Condominium.Inherit==true&&c.Condominium.Id is null)return Outcome<CaptureData>.Fail("Condominium.Unavailable");
        if(c.Areas.Unit is not null && c.Areas.Unit is not("m2" or "ha" or "alqueire"))return Outcome<CaptureData>.Fail("Capture.UnitInvalid");
        var measures=typeof(AreaData).GetProperties().Where(p=>p.PropertyType==typeof(decimal?)).Select(p=>(decimal?)p.GetValue(c.Areas)).ToArray();
        if(measures.Any(x=>x<0||x>999999999999m) || measures.Take(5).Any(x=>x is not null)&&c.Areas.Unit is null)return Outcome<CaptureData>.Fail("Capture.UnitRequired");
        if(c.Areas.Unit=="alqueire"&&string.IsNullOrWhiteSpace(c.Areas.RegionalVariant))return Outcome<CaptureData>.Fail("Capture.RegionalUnitRequired");
        if(typeof(RoomData).GetProperties().Any(p=>(int?)p.GetValue(c.Rooms)<0 || (int?)p.GetValue(c.Rooms)>100000))return Outcome<CaptureData>.Fail("Capture.CountInvalid");
        if(c.Rooms.Bedrooms is {} beds&&c.Rooms.Suites>beds)return Outcome<CaptureData>.Fail("Capture.SuitesInvalid");
        static bool Money(decimal? value)=>value is null || value>=0&&value<=999999999999.99m&&decimal.Round(value.Value,2)==value;
        if(new[]{c.Values.Sale,c.Values.Rental,c.Values.Seasonal}.Any(v=>!Money(v.Authorized)||!Money(v.Advertised))||!Money(c.Values.PropertyTax)||!Money(c.Values.CondominiumFee))return Outcome<CaptureData>.Fail("Capture.MoneyInvalid");
        if(typeof(PreliminaryFees).GetProperties().Where(p=>p.PropertyType==typeof(decimal?)).Any(p=>!Money((decimal?)p.GetValue(c.Fees))))return Outcome<CaptureData>.Fail("Capture.MoneyInvalid");
        if(new[]{c.Values.Sale.Period,c.Values.Rental.Period,c.Values.Seasonal.Period,c.Values.PropertyTaxPeriod,c.Values.CondominiumPeriod}.Any(p=>p is not null&&p is not("daily" or "weekly" or "monthly" or "annual" or "single")))return Outcome<CaptureData>.Fail("Capture.PeriodInvalid");
        if(c.Acquisition.ExclusiveStart>c.Acquisition.ExclusiveEnd)return Outcome<CaptureData>.Fail("Capture.DateInvalid");
        if(c.FurnishingState is not null&&c.FurnishingState is not("none" or "partial" or "full" or "undecided"))return Outcome<CaptureData>.Fail("Capture.FurnishingInvalid");
        if(c.Selections.Count>300)return Outcome<CaptureData>.Fail("Capture.TooManyChoices");
        var ids=new HashSet<string>();var labels=new HashSet<string>();
        foreach(var s in c.Selections)
        {
            var g=CaptureCatalog.Groups.FirstOrDefault(g=>g.Id==s.GroupId);if(g is null||!ids.Add(s.GroupId+":"+s.OptionId))return Outcome<CaptureData>.Fail("Capture.ChoiceInvalid");
            var standard=g.Options.FirstOrDefault(o=>o.Id==s.OptionId);
            if(standard is not null)s.Label=standard.Label;
            else if(s.OptionId is null||!s.OptionId.StartsWith("custom:",StringComparison.Ordinal)||!Guid.TryParse(s.OptionId[7..],out _)||string.IsNullOrWhiteSpace(s.Label)||s.Label.Trim().Length>100||g.Options.Any(o=>Normalize(o.Label)==Normalize(s.Label)))return Outcome<CaptureData>.Fail("Capture.ChoiceInvalid");
            if(!labels.Add(s.GroupId+":"+Normalize(s.Label!))||s.Detail?.Length>2000||s.Condition?.Length>2000||s.Quantity<0||s.Quantity>100000||s.Inclusion is not null&&s.Inclusion is not("included" or "excluded" or "negotiable"))return Outcome<CaptureData>.Fail("Capture.ChoiceInvalid");
        }
        if(c.Selections.Any(s=>s.GroupId=="guarantees"&&s.OptionId=="none"&&s.Selected)&&c.Selections.Count(s=>s.GroupId=="guarantees"&&s.Selected)>1)return Outcome<CaptureData>.Fail("Capture.GuaranteeConflict");
        if(c.Media.Count>200||c.Media.Select(m=>m.MediaId).Distinct().Count()!=c.Media.Count||c.Media.Any(m=>m.MediaId==Guid.Empty||m.Caption?.Length>2000||m.Room?.Length>100||m.Differential?.Length>2000||m.InternalNotes?.Length>2000||m.Visibility is not(null or "internal" or "public")))return Outcome<CaptureData>.Fail("Capture.MediaInvalid");
        return new(c);
    }
}
