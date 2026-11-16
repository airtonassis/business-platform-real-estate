namespace RealEstate.Web;

public static class CommercialCardFormatting
{
    public static string PeriodLabel(string? period)=>period switch
    {
        "daily"=>"/dia",
        "weekly"=>"/semana",
        "monthly"=>"/mês",
        "annual"=>"/ano",
        "single"=>"pagamento único",
        _=>"período não informado"
    };
}
