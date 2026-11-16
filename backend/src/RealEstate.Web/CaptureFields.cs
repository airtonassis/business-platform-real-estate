using RealEstate.Web.Pages;
namespace RealEstate.Web;
public sealed record CaptureEditorModel(PropertyModel Page,int Category);
public sealed record CaptureField(int Category,string Path,string Label,string Kind="text",string When="always",string? Options=null);
public static class CaptureFields
{
    public static readonly CaptureField[] All=[
        new(3,"Areas.Unit","Unidade das áreas","select",Options:"m2|m²;ha|Hectares;alqueire|Alqueires"),
        new(3,"Areas.RegionalVariant","Variante regional do alqueire","text","alqueire"),
        new(3,"Areas.Total","Área total (terreno)","number","land-dimensions"),new(3,"Areas.Built","Área privativa (construção)","number"),new(3,"Areas.Private","Área privativa anterior","number"),new(3,"Areas.Usable","Área útil anterior","number"),new(3,"Areas.Land","Área do terreno anterior","number"),
        new(3,"Areas.Front","Frente (m)","number","land-dimensions"),new(3,"Areas.Back","Fundos (m)","number","land-dimensions"),new(3,"Areas.Right","Lateral direita (m)","number","land-dimensions"),new(3,"Areas.Left","Lateral esquerda (m)","number","land-dimensions"),
        new(3,"Areas.LegalReserve","Reserva legal — informação descritiva","textarea","rural"),new(3,"Areas.PreservationArea","APP — informação descritiva","textarea","rural"),
        new(4,"Rooms.Bedrooms","Dormitórios (inclui suítes)","count","habitable"),new(4,"Rooms.Suites","Suítes","count","habitable"),new(4,"Rooms.Bathrooms","Banheiros","count"),new(4,"Rooms.Washrooms","Lavabos","count"),new(4,"Rooms.Parking","Vagas","count"),
        new(4,"Rooms.Living","Salas de estar","count"),new(4,"Rooms.Dining","Salas de jantar","count"),new(4,"Rooms.Tv","Salas de TV","count"),new(4,"Rooms.Offices","Escritórios","count"),new(4,"Rooms.Kitchens","Cozinhas","count"),new(4,"Rooms.Laundry","Lavanderias / áreas de serviço","count"),
        new(4,"Rooms.Guests","Capacidade de hóspedes","count","seasonal"),new(4,"Rooms.Beds","Camas","count","seasonal"),
        new(6,"Values.Sale.Authorized","Venda — valor autorizado (R$)","money","sale"),new(6,"Values.Sale.DifferentAdvertised","Anunciar venda com valor diferente","bool","sale"),new(6,"Values.Sale.Advertised","Venda — valor anunciado (R$)","money","sale-advertised"),
        new(6,"Values.Financing","Aceita financiamento","nullable-bool","sale"),new(6,"Values.Sale.Negotiable","Venda — valor negociável","nullable-bool","sale"),new(6,"Values.Sale.Conditions","Condições de venda","textarea","sale"),
        new(6,"Values.AcceptsExchange","Aceita permuta?","nullable-bool","exchange-allowed"),new(6,"Values.ExchangeNotes","Região, faixa e condições da permuta","textarea","exchange"),
        new(6,"Values.Rental.Authorized","Locação — valor autorizado (R$)","money","rental"),new(6,"Values.Rental.Period","Locação — periodicidade","select","rental","daily|Diária;weekly|Semanal;monthly|Mensal;annual|Anual"),new(6,"Values.Rental.DifferentAdvertised","Anunciar locação com valor diferente","bool","rental"),new(6,"Values.Rental.Advertised","Locação — valor anunciado (R$)","money","rental-advertised"),
        new(6,"Values.Rental.Negotiable","Locação — valor negociável","nullable-bool","rental"),new(6,"Values.Rental.Conditions","Condições de locação","textarea","rental"),
        new(6,"Values.Seasonal.Authorized","Temporada — valor autorizado (R$)","money","seasonal"),new(6,"Values.Seasonal.Period","Temporada — periodicidade","select","seasonal","daily|Diária;weekly|Semanal;monthly|Mensal"),new(6,"Values.Seasonal.DifferentAdvertised","Anunciar temporada com valor diferente","bool","seasonal"),new(6,"Values.Seasonal.Advertised","Temporada — valor anunciado (R$)","money","seasonal-advertised"),
        new(6,"Values.PropertyTax","IPTU (R$)","money"),new(6,"Values.PropertyTaxPeriod","Periodicidade do IPTU","select",Options:"monthly|Mensal;annual|Anual"),new(6,"Values.CondominiumFee","Taxa condominial (R$)","money","condominium"),new(6,"Values.CondominiumPeriod","Frequência do condomínio","select","condominium","monthly|Mensal;annual|Anual;single|Única"),
        new(7,"Acquisition.ResponsibleSubject","Responsável pela captação","select"),new(7,"Acquisition.Origin","Origem da captação"),new(7,"Acquisition.Date","Data da captação","date"),new(7,"Acquisition.Status","Situação da captação"),new(7,"Acquisition.InternalNotes","Observações internas","textarea"),new(7,"Acquisition.Exclusive","Exclusividade","nullable-bool"),new(7,"Acquisition.ExclusiveStart","Início da exclusividade","date","exclusive"),new(7,"Acquisition.ExclusiveEnd","Fim da exclusividade","date","exclusive"),new(7,"Acquisition.PublicationAuthorized","Proprietário autoriza publicação","nullable-bool"),
        new(9,"InspectionNotes","Observações internas da vistoria simples","textarea"),
        new(10,"FurnishingState","Estado da mobília","select",Options:"none|Sem mobília;partial|Semimobiliado;full|Mobiliado;undecided|A definir"),
        new(11,"Condominium.Tower","Torre"),new(11,"Condominium.Block","Bloco"),new(11,"Condominium.Unit","Unidade"),new(11,"Condominium.Notes","Observações da unidade","textarea"),
        new(12,"Fees.SalePercent","Comissão preliminar de venda (%)","percent","sale"),new(12,"Fees.SaleAmount","Comissão preliminar de venda (R$)","money","sale"),
        new(12,"Fees.RentalPercent","Comissão preliminar de locação (%)","percent","rental"),new(12,"Fees.RentalAmount","Comissão preliminar de locação (R$)","money","rental"),
        new(12,"Fees.AdministrationPercent","Administração preliminar (%)","percent","rental"),new(12,"Fees.AdministrationAmount","Administração preliminar (R$)","money","rental"),
        new(12,"Fees.InternalAmount","Taxas internas preliminares (R$)","money"),new(12,"Fees.Notes","Condições e observações internas","textarea")];
}
