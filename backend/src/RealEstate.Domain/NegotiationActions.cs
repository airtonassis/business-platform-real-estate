namespace RealEstate.Domain;

public static class NegotiationActionKinds
{
    public const string Contact = "Contato";
    public const string Appointment = "Compromisso";
    public const string Pending = "Pendência";
    public const string Other = "Outros";
    public static readonly string[] All = [Contact, Appointment, Pending, Other];
}

public sealed record NegotiationActionInput(Guid RequestId, string Kind, string Description, DateTimeOffset? DueAt,
    string? ResponsibleSubject, AppointmentInput? Appointment = null, long ExpectedNegotiationVersion = 0);
public sealed record NegotiationActionCompletion(Guid RequestId, long ExpectedVersion, string Result, long ExpectedAppointmentVersion = 0);
public sealed record NegotiationAction(Guid Id, Guid NegotiationId, Guid CustomerId, Guid PropertyId, Guid? AppointmentId,
    string Kind, string Description, DateTimeOffset? DueAt, string ResponsibleSubject, string ResponsibleName,
    string Status, string? Result, string CreatedBy, DateTimeOffset CreatedAt, long Version, DateTimeOffset UpdatedAt);
public sealed record NegotiationActionEvent(Guid Id, Guid ActionId, long Version, string Event, string Actor,
    string ResponsibleSubject, string Description, DateTimeOffset? DueAt, string? Result, DateTimeOffset At);
public sealed record NegotiationActionAppointment(AppointmentInput Input, string ResponsibleSubject, string ResponsibleName);
public sealed record NegotiationActionCompletionResult(NegotiationAction Action, CommercialAppointment? Appointment);

public interface INegotiationActionStore
{
    Task<IReadOnlyList<NegotiationAction>> ListActionsAsync(AccessScope scope, Guid negotiationId, CancellationToken token);
    Task<IReadOnlyList<NegotiationActionEvent>> ActionEventsAsync(AccessScope scope, Guid negotiationId, CancellationToken token);
    Task<Outcome<NegotiationAction?>> ExistingCreateRequestAsync(AccessScope scope, Guid negotiationId, NegotiationActionInput input, CancellationToken token);
    Task<Outcome<NegotiationAction>> CreateActionAsync(AccessScope scope, Negotiation negotiation,
        NegotiationActionInput input, NegotiationAssignee responsible, NegotiationActionAppointment? appointment, CancellationToken token);
    Task<Outcome<NegotiationActionCompletionResult>> CompleteActionAsync(AccessScope scope, Guid actionId,
        NegotiationActionCompletion input, CancellationToken token);
}

public static class NegotiationActionRules
{
    public static Outcome<NegotiationActionInput> Validate(NegotiationActionInput input)
    {
        if (input is null || input.RequestId == Guid.Empty || !NegotiationActionKinds.All.Contains(input.Kind))
            return Outcome<NegotiationActionInput>.Fail("NegotiationAction.InvalidType");
        var description = CustomerRules.Clean(input.Description);
        if (description is null || description.Length > 4000)
            return Outcome<NegotiationActionInput>.Fail("NegotiationAction.DescriptionRequired");
        if (input.Kind == NegotiationActionKinds.Other && description.Length < 12)
            return Outcome<NegotiationActionInput>.Fail("NegotiationAction.OtherDescriptionRequired");
        if (input.Kind is NegotiationActionKinds.Contact or NegotiationActionKinds.Pending && input.DueAt is null)
            return Outcome<NegotiationActionInput>.Fail("NegotiationAction.DueRequired");
        if (input.Kind == NegotiationActionKinds.Appointment && (input.DueAt is not null || input.Appointment is null))
            return Outcome<NegotiationActionInput>.Fail("NegotiationAction.AppointmentFieldsRequired");
        if (input.Kind == NegotiationActionKinds.Appointment && input.Appointment!.RequestId != input.RequestId)
            return Outcome<NegotiationActionInput>.Fail("NegotiationAction.InvalidInput");
        if (input.Kind != NegotiationActionKinds.Appointment && input.Appointment is not null)
            return Outcome<NegotiationActionInput>.Fail("NegotiationAction.InvalidInput");
        return new(input with { Description = description });
    }

    public static Outcome<NegotiationActionCompletion> ValidateCompletion(NegotiationActionCompletion input)
    {
        var result = CustomerRules.Clean(input?.Result);
        if (input is null || input.RequestId == Guid.Empty || input.ExpectedVersion < 1 || result is null || result.Length > 1000)
            return Outcome<NegotiationActionCompletion>.Fail("NegotiationAction.ResultRequired");
        return new(input with { Result = result });
    }
}
