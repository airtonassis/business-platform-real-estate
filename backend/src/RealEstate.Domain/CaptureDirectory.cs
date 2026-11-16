namespace RealEstate.Domain;

public sealed record CaptureResponsible(string Subject,string DisplayName,Guid ProfileId,string JobTitle,Guid MembershipId=default);
public sealed record CaptureResponsibleDirectory(Guid OrganizationId,Guid EstablishmentId,IReadOnlyList<CaptureResponsible> Items);
public interface ICaptureDirectory
{
    Task<Outcome<CaptureResponsibleDirectory>> ReadAsync(AccessScope scope,Guid establishmentId,string? subject=null,CancellationToken token=default);
}
