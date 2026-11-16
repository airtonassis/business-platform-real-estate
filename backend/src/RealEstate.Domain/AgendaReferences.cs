namespace RealEstate.Domain;

public sealed record AgendaOwnerReference(Guid Id, string Name);
public sealed record AgendaPropertyReference(Guid Id, Guid EstablishmentId, string? CommercialCode, string Code, string Title, string Status);

/// <summary>Minimal, scope-aware projections used only by the Commercial Agenda.</summary>
public interface IAgendaReferenceStore
{
    Task<IReadOnlyList<AgendaOwnerReference>> SearchAgendaOwnersAsync(AccessScope scope, string query, CancellationToken token);
    Task<AgendaOwnerReference?> FindAgendaOwnerReferenceAsync(AccessScope scope, Guid id, CancellationToken token);
    Task<AgendaPropertyReference?> FindAgendaPropertyReferenceAsync(AccessScope scope, Guid id, CancellationToken token);
}
