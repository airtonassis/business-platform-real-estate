namespace RealEstate.Domain;

public static class PropertyLifecycle
{
    public const string Draft = "Draft";
    public const string InReview = "InReview";
    public const string Available = "Available";
    public const string InNegotiation = "InNegotiation";
    public const string Negotiated = "Negotiated";
    public const string Inactive = "Inactive";
    public static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>
    {
        [Draft] = "Rascunho", [InReview] = "Em revisão", [Available] = "Disponível",
        [InNegotiation] = "Em negociação", [Negotiated] = "Negociado", [Inactive] = "Inativo"
    };
    public static string Label(string status) => Labels.GetValueOrDefault(status, status);
}
