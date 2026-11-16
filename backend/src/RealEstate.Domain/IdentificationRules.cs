namespace RealEstate.Domain;

public static class IdentificationRules
{
    public static readonly string[] Purposes=[..PropertyRules.Purposes,"Venda e locação"];
    public static readonly string[] Categories=["Identificação","Localização","Áreas e dimensões","Ambientes e características","Estrutura e comodidades","Valores e condições","Proprietários e captação","Documentação","Mídias e vistoria","Móveis e Equipamentos","Condomínio e Infraestrutura Coletiva","Comissões e taxas internas","Garantias aceitas para locação","Permuta e condições de negociação"];
    public static readonly string[] RuralTypes=["Chácara","Fazenda","Sítio"];
    public static bool IsRural(string? type)=>type is not null && RuralTypes.Contains(type);
    public static bool HasSale(string? purpose)=>purpose is "Venda" or "Venda e locação";
    public static bool HasRental(string? purpose)=>purpose is "Locação" or "Venda e locação";
}
