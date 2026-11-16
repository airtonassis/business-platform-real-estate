namespace RealEstate.Domain;

public sealed record NegotiationActivityTransferCandidate(NegotiationAction Action, CommercialAppointment? Appointment);
public sealed record NegotiationActivityTransferDecision(Guid ActionId, bool WillTransfer, Guid? ConflictingAppointmentId, string? ConflictReason);
public sealed record NegotiationActivityTransferSelection(Guid ActionId, long ActionVersion, long? AppointmentVersion, bool Selected);
public sealed record NegotiationActivityTransferObservedAppointment(Guid Id, long Version, DateTimeOffset StartsAt,
    DateTimeOffset EndsAt, string Status, string ResponsibleSubject);
public sealed record NegotiationActivityTransferRequest(Guid RequestId, string DestinationSubject, string FollowupResponsibleSubject,
    IReadOnlyList<Guid> SelectedActionIds, long ExpectedNegotiationVersion, string? Reason = null);
public sealed record NegotiationActivityTransferSnapshot(Guid OrganizationId, Guid EstablishmentId, Guid NegotiationId,
    Guid RequestId, long NegotiationVersion, string DestinationSubject, string FollowupResponsibleSubject, string? Reason,
    IReadOnlyList<NegotiationActivityTransferSelection> Selection, IReadOnlyList<NegotiationActivityTransferObservedAppointment> Schedule,
    string Fingerprint, DateTimeOffset IssuedAtUtc);
public sealed record NegotiationActivityTransferPreview(string Token, IReadOnlyList<NegotiationActivityTransferCandidate> EligibleActivities,
    IReadOnlyList<NegotiationActivityTransferDecision> Decisions);
public sealed record NegotiationActivityTransferPreviewInput(Guid RequestId, string DestinationSubject,
    string FollowupResponsibleSubject, IReadOnlyList<Guid> SelectedActionIds, long ExpectedNegotiationVersion, string? Reason = null);
public sealed record NegotiationActivityTransferConfirm(string Token);
public sealed record NegotiationActivityTransferFollowup(Guid Id, Guid NegotiationId, Guid ActionId, Guid? AppointmentId,
    string Reason, string Status, string ResponsibleSubject, string ResponsibleName, string OriginalResponsibleSubject,
    string? Result, string CreatedBy, DateTimeOffset CreatedAt, long Version, DateTimeOffset UpdatedAt);
public sealed record NegotiationActivityFollowupEvent(Guid Id, Guid FollowupId, long Version, string Event,
    string Actor, string ResponsibleSubject, string? Result, Guid RequestId, DateTimeOffset At);
public sealed record NegotiationActivityFollowupResolution(Guid RequestId, long ExpectedVersion, string Result);
public sealed record NegotiationActivityFollowupResolutionReplay(Guid RequestId, string Actor, long Version, string Result);
public sealed record NegotiationActivityTransferReceipt(Guid RequestId, int TransferredActions, int PreservedAppointments,
    int CreatedFollowups, IReadOnlyList<NegotiationActivityTransferDecision> Decisions);

public interface INegotiationActivityTransferStore
{
    Task<Outcome<NegotiationActivityTransferReceipt?>> ExistingReceiptAsync(AccessScope scope, Guid requestId, string fingerprint, CancellationToken token);
    Task<IReadOnlyList<NegotiationActivityTransferCandidate>> EligibleActivitiesAsync(AccessScope scope, Guid negotiationId, CancellationToken token);
    Task<IReadOnlyList<CommercialAppointment>> RecipientScheduleAsync(AccessScope scope, string responsibleSubject,
        DateTimeOffset from, DateTimeOffset until, CancellationToken token);
    Task<Outcome<NegotiationActivityTransferReceipt>> ConfirmTransferAsync(AccessScope scope,
        NegotiationActivityTransferSnapshot snapshot, NegotiationAssignee recipient, IReadOnlyList<NegotiationActivityTransferDecision> decisions,
        CancellationToken token);
    Task<IReadOnlyList<NegotiationActivityTransferFollowup>> FollowupsAsync(AccessScope scope, Guid negotiationId, CancellationToken token);
    Task<IReadOnlyList<NegotiationActivityFollowupEvent>> FollowupEventsAsync(AccessScope scope, Guid negotiationId, CancellationToken token);
    Task<Outcome<NegotiationActivityTransferFollowup>> ResolveFollowupAsync(AccessScope scope, Guid negotiationId, Guid followupId,
        NegotiationActivityFollowupResolution resolution, CancellationToken token);
}

public interface ITransferPreviewProtector
{
    string Protect(string value);
    string? Unprotect(string protectedValue);
}

public static class NegotiationActivityTransferSnapshotRules
{
    public static bool Matches(NegotiationActivityTransferSnapshot snapshot, Guid organizationId, Guid establishmentId,
        Guid negotiationId, string destinationSubject, string followupResponsibleSubject, string fingerprint, DateTimeOffset nowUtc) =>
        snapshot.OrganizationId == organizationId && snapshot.EstablishmentId == establishmentId && snapshot.NegotiationId == negotiationId &&
        snapshot.DestinationSubject == destinationSubject && snapshot.FollowupResponsibleSubject == followupResponsibleSubject &&
        snapshot.Fingerprint == fingerprint && snapshot.IssuedAtUtc <= nowUtc && nowUtc - snapshot.IssuedAtUtc <= TimeSpan.FromMinutes(20);

    public static bool MatchesActivities(IReadOnlyList<NegotiationActivityTransferSelection> expected,
        IReadOnlyCollection<NegotiationActivityTransferCandidate> current)
    {
        if (expected is null || current is null || expected.Any(x => x.ActionId == Guid.Empty || x.ActionVersion < 1 ||
                x.AppointmentVersion is < 1) || expected.Select(x => x.ActionId).Distinct().Count() != expected.Count ||
            current.Any(x => x.Action is null || x.Action.Status != "Aberta") ||
            current.Select(x => x.Action.Id).Distinct().Count() != current.Count || current.Count != expected.Count)
            return false;

        var actual = current.ToDictionary(x => x.Action.Id);
        return expected.All(item => actual.TryGetValue(item.ActionId, out var candidate) &&
            candidate.Action.Version == item.ActionVersion &&
            candidate.Action.Status == "Aberta" &&
            candidate.Appointment?.Version == item.AppointmentVersion &&
            candidate.Appointment is not { Status: not "Agendado" } &&
            (candidate.Action.Kind == NegotiationActionKinds.Appointment) == (candidate.Appointment is not null));
    }
}

public static class NegotiationActivityFollowupResolutionRules
{
    public static bool MatchesReplay(NegotiationActivityFollowupResolutionReplay replay, Guid requestId,
        string actor, long expectedVersion, string result) =>
        replay.RequestId == requestId && replay.Actor == actor && replay.Version == expectedVersion + 1 && replay.Result == result;
}

public static class NegotiationActivityFollowupRules
{
    public static Outcome<Guid> CreatedOrPending(Guid? createdId) =>
        createdId is { } id && id != Guid.Empty
            ? new(id)
            : Outcome<Guid>.Fail("NegotiationTransfer.FollowupAlreadyPending");
}

public static class NegotiationActivityTransferRules
{
    public static string ConflictReasonLabel(string? reason) => reason switch
    {
        "ExistingAppointment" => "Conflito com compromisso já agendado",
        "SelectedAppointment" => "Conflito com outro compromisso selecionado",
        _ => "Conflito de agenda"
    };

    public static bool Overlaps(DateTimeOffset aStart, DateTimeOffset aEnd, DateTimeOffset bStart, DateTimeOffset bEnd) =>
        aStart < bEnd && bStart < aEnd;

    public static Outcome<IReadOnlyList<NegotiationActivityTransferDecision>> Classify(
        IReadOnlyCollection<NegotiationActivityTransferCandidate> selected,
        IReadOnlyCollection<CommercialAppointment> recipientSchedule,
        string destinationSubject)
    {
        if (string.IsNullOrWhiteSpace(destinationSubject) || selected.Any(x => x.Action is null || x.Action.Status != "Aberta") ||
            selected.Select(x => x.Action.Id).Distinct().Count() != selected.Count ||
            selected.Any(x => x.Action.Kind == NegotiationActionKinds.Appointment != (x.Appointment is not null)) ||
            selected.Where(x => x.Appointment is not null).Any(x => x.Action.AppointmentId != x.Appointment!.Id ||
                x.Appointment.Status != "Agendado" || x.Appointment.ResponsibleSubject != x.Action.ResponsibleSubject))
            return Outcome<IReadOnlyList<NegotiationActivityTransferDecision>>.Fail("NegotiationTransfer.SelectionInvalid");

        var existing = recipientSchedule.Where(x => x.Status == "Agendado" && x.ResponsibleSubject == destinationSubject)
            .OrderBy(x => x.StartsAt.UtcTicks).ThenBy(x => x.EndsAt.UtcTicks).ThenBy(x => x.Id).ToArray();
        var accepted = new List<CommercialAppointment>();
        var decisions = new Dictionary<Guid, NegotiationActivityTransferDecision>();
        foreach (var item in selected.Where(x => x.Appointment is not null)
                     .OrderBy(x => x.Appointment!.StartsAt.UtcTicks).ThenBy(x => x.Appointment!.EndsAt.UtcTicks).ThenBy(x => x.Appointment!.Id))
        {
            var appointment = item.Appointment!;
            var conflict = existing.FirstOrDefault(x => Overlaps(appointment.StartsAt, appointment.EndsAt, x.StartsAt, x.EndsAt));
            if (conflict is not null)
            {
                decisions[item.Action.Id] = new(item.Action.Id, false, conflict.Id, "ExistingAppointment");
                continue;
            }
            conflict = accepted.FirstOrDefault(x => Overlaps(appointment.StartsAt, appointment.EndsAt, x.StartsAt, x.EndsAt));
            if (conflict is not null)
            {
                decisions[item.Action.Id] = new(item.Action.Id, false, conflict.Id, "SelectedAppointment");
                continue;
            }
            accepted.Add(appointment);
            decisions[item.Action.Id] = new(item.Action.Id, true, null, null);
        }
        foreach (var item in selected.Where(x => x.Appointment is null))
            decisions[item.Action.Id] = new(item.Action.Id, true, null, null);
        return new(decisions.Values.OrderBy(x => x.ActionId).ToArray());
    }
}
