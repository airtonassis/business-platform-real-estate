using RealEstate.Domain;

namespace RealEstate.Application;

public sealed class NegotiationActionService(ICoreAccess access, INegotiationStore negotiations,
    INegotiationActionStore actions, ICustomerDirectory directory, ITeamStore team,
    IOperationalHierarchyAccess hierarchy, CommercialAppointmentService agenda)
{
    private async Task<Outcome<AccessScope>> Scope(string? writePermission, CancellationToken token)
    {
        var view = await access.RequireAsync("negotiations", "NEGOTIATION_VIEW", token);
        if (!view.IsSuccess) return Outcome<AccessScope>.Fail(view.Code!);
        var scope = view.Value!;
        if (scope.OrganizationId == Guid.Empty || string.IsNullOrWhiteSpace(scope.Subject) ||
            scope.ActiveEstablishmentId is not { } unit || scope.Establishments?.Any(e => e.Id == unit) != true)
            return Outcome<AccessScope>.Fail("Context.ActiveEstablishmentRequired");
        foreach (var (resource, permission) in new[] { ("customers", "CUSTOMER_READ"), ("properties", "PROPERTY_READ") })
        {
            var read = await access.RequireAsync(resource, permission, token);
            if (!SameContext(scope, read)) return Outcome<AccessScope>.Fail("Access.Forbidden");
        }
        if (writePermission is not null)
        {
            var write = await access.RequireAsync("customers", writePermission, token);
            if (!SameContext(scope, write)) return Outcome<AccessScope>.Fail("Access.Forbidden");
        }
        return new(scope);
    }

    private static bool SameContext(AccessScope expected, Outcome<AccessScope> actual) =>
        actual.IsSuccess && actual.Value!.OrganizationId == expected.OrganizationId &&
        actual.Value.ActiveEstablishmentId == expected.ActiveEstablishmentId &&
        actual.Value.Subject == expected.Subject &&
        actual.Value.Establishments?.Any(e => e.Id == expected.ActiveEstablishmentId) == true;

    public async Task<Outcome<IReadOnlyList<NegotiationAction>>> ListAsync(Guid negotiationId, CancellationToken token = default)
    {
        var scope = await Scope(null, token);
        if (!scope.IsSuccess) return Outcome<IReadOnlyList<NegotiationAction>>.Fail(scope.Code!);
        if (await negotiations.FindNegotiationAsync(scope.Value!, negotiationId, token) is null)
            return Outcome<IReadOnlyList<NegotiationAction>>.Fail("Resource.NotFound");
        return new(await actions.ListActionsAsync(scope.Value!, negotiationId, token));
    }

    public async Task<Outcome<IReadOnlyList<NegotiationActionEvent>>> EventsAsync(Guid negotiationId, CancellationToken token = default)
    {
        var scope = await Scope(null, token);
        if (!scope.IsSuccess) return Outcome<IReadOnlyList<NegotiationActionEvent>>.Fail(scope.Code!);
        if (await negotiations.FindNegotiationAsync(scope.Value!, negotiationId, token) is null)
            return Outcome<IReadOnlyList<NegotiationActionEvent>>.Fail("Resource.NotFound");
        return new(await actions.ActionEventsAsync(scope.Value!, negotiationId, token));
    }

    public async Task<Outcome<IReadOnlyList<NegotiationResponsibleOption>>> ResponsibleOptionsAsync(Guid negotiationId, CancellationToken token = default)
    {
        var scope = await Scope("CUSTOMER_CONTACT_CREATE", token);
        if (!scope.IsSuccess) return Outcome<IReadOnlyList<NegotiationResponsibleOption>>.Fail(scope.Code!);
        var negotiation = await negotiations.FindNegotiationAsync(scope.Value!, negotiationId, token);
        if (negotiation is null) return Outcome<IReadOnlyList<NegotiationResponsibleOption>>.Fail("Resource.NotFound");
        var directoryResult = await directory.ReadAsync(scope.Value!, scope.Value!.ActiveEstablishmentId!.Value, token: token);
        if (!directoryResult.IsSuccess || directoryResult.Value is not { Items: not null } people ||
            people.OrganizationId != scope.Value.OrganizationId || people.EstablishmentId != scope.Value.ActiveEstablishmentId ||
            people.Items.Any(p => p is null || string.IsNullOrWhiteSpace(p.Subject) || string.IsNullOrWhiteSpace(p.DisplayName)) ||
            people.Items.Select(p => p.Subject).Distinct(StringComparer.Ordinal).Count() != people.Items.Count)
            return Outcome<IReadOnlyList<NegotiationResponsibleOption>>.Fail(directoryResult.IsSuccess ? "Dependency.InvalidResponse" : directoryResult.Code!);
        var manage = await access.RequireAsync("negotiations", "NEGOTIATION_MANAGE", token);
        var mayAssignOthers = SameContext(scope.Value, manage);
        var options = new List<NegotiationResponsibleOption>();
        foreach (var person in people.Items.Where(p => mayAssignOthers || p.Subject == negotiation.ResponsibleSubject))
        {
            if (person.MembershipId == Guid.Empty || person.ProfileId == Guid.Empty) continue;
            var profile = await team.FindAsync(scope.Value!, person.ProfileId, token);
            if (profile is null || profile.Data.Status != "Ativo") continue;
            var recipient = await hierarchy.RecipientAsync(scope.Value!, person.MembershipId, token);
            if (!recipient.IsSuccess || !recipient.Value!.IsEligible) continue;
            options.Add(new(person.Subject, person.DisplayName, person.MembershipId));
        }
        return new(options.OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    public async Task<Outcome<NegotiationAction>> CreateAsync(Guid negotiationId, NegotiationActionInput input, CancellationToken token = default)
    {
        var valid = NegotiationActionRules.Validate(input);
        if (!valid.IsSuccess) return Outcome<NegotiationAction>.Fail(valid.Code!);
        var scope = await Scope("CUSTOMER_CONTACT_CREATE", token);
        if (!scope.IsSuccess) return Outcome<NegotiationAction>.Fail(scope.Code!);
        var negotiation = await negotiations.FindNegotiationAsync(scope.Value!, negotiationId, token);
        if (negotiation is null || negotiation.EstablishmentId != scope.Value!.ActiveEstablishmentId)
            return Outcome<NegotiationAction>.Fail("Resource.NotFound");
        var replay = await actions.ExistingCreateRequestAsync(scope.Value!, negotiationId, valid.Value!, token);
        if (!replay.IsSuccess) return Outcome<NegotiationAction>.Fail(replay.Code!);
        if (replay.Value is not null) return new(replay.Value);
        if (input.ExpectedNegotiationVersion < 1) return Outcome<NegotiationAction>.Fail("NegotiationAction.VersionRequired");

        var requestedSubject = string.IsNullOrWhiteSpace(input.ResponsibleSubject) ? negotiation.ResponsibleSubject : input.ResponsibleSubject.Trim();
        if (requestedSubject != negotiation.ResponsibleSubject)
        {
            var manage = await access.RequireAsync("negotiations", "NEGOTIATION_MANAGE", token);
            if (!SameContext(scope.Value!, manage)) return Outcome<NegotiationAction>.Fail("Access.Forbidden");
        }
        var candidate = await directory.ReadAsync(scope.Value!, scope.Value!.ActiveEstablishmentId!.Value, requestedSubject, token);
        if (!candidate.IsSuccess || candidate.Value is not { Items: not null } directoryResult ||
            directoryResult.OrganizationId != scope.Value.OrganizationId || directoryResult.EstablishmentId != scope.Value.ActiveEstablishmentId ||
            directoryResult.Items.Any(x => x is null || string.IsNullOrWhiteSpace(x.Subject) || string.IsNullOrWhiteSpace(x.DisplayName)) ||
            directoryResult.Items.Select(x => x.Subject).Distinct(StringComparer.Ordinal).Count() != directoryResult.Items.Count)
            return Outcome<NegotiationAction>.Fail(candidate.IsSuccess ? "Dependency.InvalidResponse" : candidate.Code!);
        var person = directoryResult.Items.SingleOrDefault(x => x.Subject == requestedSubject);
        if (person is null || person.MembershipId == Guid.Empty || person.ProfileId == Guid.Empty)
            return Outcome<NegotiationAction>.Fail("Negotiation.ResponsibleIneligible");
        var profile = await team.FindAsync(scope.Value!, person.ProfileId, token);
        if (profile is null || profile.Data.Status != "Ativo") return Outcome<NegotiationAction>.Fail("Negotiation.ResponsibleIneligible");
        var recipient = await hierarchy.RecipientAsync(scope.Value!, person.MembershipId, token);
        if (!recipient.IsSuccess) return Outcome<NegotiationAction>.Fail(recipient.Code!);
        if (!recipient.Value!.IsEligible) return Outcome<NegotiationAction>.Fail("Negotiation.ResponsibleIneligible");
        var responsible = new NegotiationAssignee(person.Subject, person.MembershipId, person.ProfileId, person.DisplayName);

        NegotiationActionAppointment? appointment = null;
        if (valid.Value!.Kind == NegotiationActionKinds.Appointment)
        {
            var prepared = await agenda.ValidateNegotiationAppointmentAsync(negotiation, valid.Value.Appointment!, responsible.Subject, token);
            if (!prepared.IsSuccess) return Outcome<NegotiationAction>.Fail(prepared.Code!);
            appointment = new(prepared.Value!, responsible.Subject, responsible.DisplayName);
        }
        var normalized = valid.Value with { ResponsibleSubject = responsible.Subject };
        return await actions.CreateActionAsync(scope.Value!, negotiation, normalized, responsible, appointment, token);
    }

    public async Task<Outcome<NegotiationActionCompletionResult>> CompleteAsync(Guid negotiationId, Guid actionId,
        NegotiationActionCompletion input, CancellationToken token = default)
    {
        var valid = NegotiationActionRules.ValidateCompletion(input);
        if (!valid.IsSuccess) return Outcome<NegotiationActionCompletionResult>.Fail(valid.Code!);
        var scope = await Scope("CUSTOMER_CONTACT_CREATE", token);
        if (!scope.IsSuccess) return Outcome<NegotiationActionCompletionResult>.Fail(scope.Code!);
        var negotiation = await negotiations.FindNegotiationAsync(scope.Value!, negotiationId, token);
        if (negotiation is null) return Outcome<NegotiationActionCompletionResult>.Fail("Resource.NotFound");
        var existing = (await actions.ListActionsAsync(scope.Value!, negotiationId, token)).SingleOrDefault(x => x.Id == actionId);
        if (existing is null) return Outcome<NegotiationActionCompletionResult>.Fail("Resource.NotFound");
        if (existing.Kind == NegotiationActionKinds.Appointment)
        {
            if (existing.AppointmentId is not { } appointmentId) return Outcome<NegotiationActionCompletionResult>.Fail("NegotiationAction.IntegrityError");
            var appointment = await agenda.ValidateLinkedRealizationAsync(appointmentId, negotiation.CustomerId, valid.Value!.Result, token);
            if (!appointment.IsSuccess) return Outcome<NegotiationActionCompletionResult>.Fail(appointment.Code!);
            valid = new(valid.Value! with { ExpectedAppointmentVersion = appointment.Value!.Version });
        }
        return await actions.CompleteActionAsync(scope.Value!, actionId, valid.Value!, token);
    }
}
