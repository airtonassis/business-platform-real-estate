namespace RealEstate.Domain;

public sealed record OpenNegotiation(Guid RequestId,Guid CustomerId,Guid PropertyId,Guid AnnouncementId,string? ResponsibleSubject=null);
public sealed record Negotiation(Guid Id,Guid CustomerId,Guid PropertyId,Guid AnnouncementId,Guid OwnerId,
    Guid EstablishmentId,string ResponsibleSubject,string Status,long Version,DateTimeOffset CreatedAt,Guid ResponsibleMembershipId=default);
public sealed record NegotiationEvent(Guid Id,Guid NegotiationId,long Version,string Action,string Actor,DateTimeOffset At,
    string? PreviousResponsibleSubject=null,string? ResponsibleSubject=null,string? Reason=null,Guid? PreviousMembershipId=null,Guid? MembershipId=null);
public sealed record NegotiationAssignee(string Subject,Guid MembershipId,Guid ProfileId,string DisplayName);
public sealed record NegotiationTransfer(Guid RequestId,string ResponsibleSubject,long ExpectedVersion,string? Reason=null);
public sealed record NegotiationResponsibleOption(string Subject,string DisplayName,Guid MembershipId);
public sealed record OperationalAuthority(bool IsImmediateSupervisor,bool CanRecoverOperationalResponsibility,long RelationshipVersion,DateTimeOffset DecidedAtUtc);
public sealed record OperationalRecipient(bool IsEligible,DateTimeOffset ValidatedAtUtc);
public interface IOperationalHierarchyAccess
{
    Task<Outcome<OperationalAuthority>> AuthorityAsync(AccessScope scope,Guid subordinateMembershipId,CancellationToken token);
    Task<Outcome<OperationalRecipient>> RecipientAsync(AccessScope scope,Guid membershipId,CancellationToken token);
}
public sealed record NegotiationPage(IReadOnlyList<Negotiation> Items,long Total,int Page);
public interface INegotiationStore
{
    Task<Outcome<Negotiation>> OpenAsync(AccessScope scope,OpenNegotiation input,string customerResponsibleSubject,NegotiationAssignee assignee,CancellationToken token);
    Task<Outcome<Negotiation>> TransferAsync(AccessScope scope,Guid negotiationId,NegotiationTransfer input,NegotiationAssignee assignee,bool recovery,CancellationToken token);
    Task<Negotiation?> FindNegotiationAsync(AccessScope scope,Guid id,CancellationToken token);
    Task<NegotiationPage> ListAsync(AccessScope scope,Guid? customerId,int page,CancellationToken token);
    Task<IReadOnlyList<NegotiationEvent>> NegotiationEventsAsync(AccessScope scope,Guid id,CancellationToken token);
}
public static class NegotiationRules
{
    public static bool Valid(OpenNegotiation input)=>input.RequestId!=Guid.Empty&&input.CustomerId!=Guid.Empty&&input.PropertyId!=Guid.Empty&&input.AnnouncementId!=Guid.Empty;
}
