namespace RealEstate.Domain;

public sealed record PresentationInput(Guid[] PropertyIds,string Channel,string? Notes);
public sealed record FeedbackInput(Guid PresentationId,string Status,string? Category,string? Reason,string? Notes,string Channel="Outro");
public sealed record CommercialInteraction(Guid Id,Guid PresentationId,Guid CustomerId,Guid PropertyId,Guid ContactId,DateTimeOffset At,string Channel,string Status,string? Category,string? Reason,string? Notes,string Actor,string ResponsibleSubject,string PropertyCode,string? PropertyTitle,string CustomerName);
public sealed record CommercialSummary(DateTimeOffset From,DateTimeOffset Until,int UniqueCustomers,int Presentations,int VisitRecords,int InterestedCustomers,IReadOnlyDictionary<string,int> NegativeCategories,IReadOnlyDictionary<string,int> InteractionsByDay);
public interface ICommercialInteractionStore
{
    Task<bool> CustomerExistsAsync(AccessScope scope,Guid customerId,CancellationToken token);
    Task<bool> PropertyExistsAsync(AccessScope scope,Guid propertyId,CancellationToken token);
    Task<bool> IsCaptureResponsibleAsync(AccessScope scope,Guid propertyId,CancellationToken token);
    Task<IReadOnlyList<CommercialInteraction>> HistoryAsync(AccessScope scope,Guid? customerId,Guid? propertyId,CancellationToken token);
    Task<Outcome<IReadOnlyList<CommercialInteraction>>> PresentAsync(AccessScope scope,Guid customerId,PresentationInput input,CancellationToken token);
    Task<Outcome<CommercialInteraction>> FeedbackAsync(AccessScope scope,Guid customerId,FeedbackInput input,CancellationToken token);
    Task<Outcome<CommercialSummary>> SummaryAsync(AccessScope scope,Guid propertyId,DateTimeOffset from,DateTimeOffset until,CancellationToken token);
}
public static class CommercialInteractionRules
{
    public const string Presented="Apresentado — aguardando retorno";
    public static readonly string[] Channels=["Presencial","Telefone","WhatsApp","E-mail","Visita","Outro"];
    public static readonly string[] Statuses=[Presented,"Demonstrou interesse","Deseja agendar visita","Visitado","Sem interesse","Em negociação"];
    public static readonly string[] Categories=["Preço e condições","Localização","Área e dimensões","Distribuição e ambientes","Conservação","Estrutura e comodidades","Condomínio e infraestrutura","Garagem e estacionamento","Documentação ou condições comerciais","Outros"];
    public static Outcome<PresentationInput> Validate(PresentationInput p)
    {
        if(p?.PropertyIds is not {Length:>0 and <=25} ids||ids.Any(id=>id==Guid.Empty)||ids.Distinct().Count()!=ids.Length||!Channels.Contains(p.Channel)||p.Notes?.Length>4000)return Outcome<PresentationInput>.Fail("Commercial.InvalidPresentation");
        return new(p with{Notes=CustomerRules.Clean(p.Notes)});
    }
    public static Outcome<FeedbackInput> Validate(FeedbackInput p)
    {
        if(p is null||p.PresentationId==Guid.Empty||!Statuses.Contains(p.Status)||!Channels.Contains(p.Channel)||p.Notes?.Length>4000||p.Reason?.Length>1000)return Outcome<FeedbackInput>.Fail("Commercial.InvalidFeedback");
        var category=CustomerRules.Clean(p.Category);var reason=CustomerRules.Clean(p.Reason);
        if(category is not null&&!Categories.Contains(category)||p.Status=="Sem interesse"&&(category is null||reason is null))return Outcome<FeedbackInput>.Fail("Commercial.ReasonRequired");
        return new(p with{Category=category,Reason=reason,Notes=CustomerRules.Clean(p.Notes)});
    }
}
