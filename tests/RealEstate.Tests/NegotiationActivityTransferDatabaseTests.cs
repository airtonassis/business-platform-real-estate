using Npgsql;
using RealEstate.Application;
using RealEstate.Domain;
using RealEstate.Infrastructure;
using Xunit;

namespace RealEstate.Tests;

[Collection("PostgreSQL")]
public sealed class NegotiationActivityTransferDatabaseTests
{
    private sealed class Access(AccessScope scope) : ICoreAccess
    {
        public Task<Outcome<AccessScope>> RequireAsync(string resource, string permission, CancellationToken token) => Task.FromResult(new Outcome<AccessScope>(scope));
    }

    private sealed class Directory(Guid organizationId, Guid establishmentId) : ICustomerDirectory
    {
        private readonly CaptureResponsible[] people =
        [
            new("test-only-actor", "Atendente de teste", Guid.NewGuid(), "Corretor", Guid.NewGuid()),
            new("second-actor", "Segundo atendente", Guid.NewGuid(), "Corretor", Guid.NewGuid())
        ];

        public Task<Outcome<CaptureResponsibleDirectory>> ReadAsync(AccessScope scope, Guid unit, string? subject = null, CancellationToken token = default) =>
            Task.FromResult(new Outcome<CaptureResponsibleDirectory>(new(organizationId, establishmentId,
                subject is null ? people : people.Where(x => x.Subject == subject).ToArray())));
    }

    private sealed class Team : ITeamStore
    {
        public Task<TeamRecord?> FindAsync(AccessScope scope, Guid id, CancellationToken token) =>
            Task.FromResult<TeamRecord?>(new(id, new("Corretor", null, "SP", ["Venda"], null, "Ativo"), 1));
        public Task<Outcome<TeamRecord>> SaveAsync(AccessScope scope, Guid id, TeamProfessional input, long version, CancellationToken token) =>
            throw new NotImplementedException();
    }

    private sealed class Hierarchy : IOperationalHierarchyAccess
    {
        public Task<Outcome<OperationalAuthority>> AuthorityAsync(AccessScope scope, Guid subordinateMembershipId, CancellationToken token) =>
            Task.FromResult(new Outcome<OperationalAuthority>(new(true, false, 1, DateTimeOffset.UtcNow)));
        public Task<Outcome<OperationalRecipient>> RecipientAsync(AccessScope scope, Guid membershipId, CancellationToken token) =>
            Task.FromResult(new Outcome<OperationalRecipient>(new(true, DateTimeOffset.UtcNow)));
    }

    // Token protection is deliberately a test-only identity adapter. This test validates PostgreSQL
    // persistence and transactional behavior; token protection has separate unit coverage.
    private sealed class TestPreviewProtector : ITransferPreviewProtector
    {
        public string Protect(string value) => value;
        public string? Unprotect(string protectedValue) => protectedValue;
    }

    [Fact]
    public async Task Transfer_persists_conflicts_followups_replay_scope_and_transactional_rollback()
    {
        var runtime = Environment.GetEnvironmentVariable("ConnectionStrings__Erp");
        var adminConnection = Environment.GetEnvironmentVariable("ErpTestAdminConnection");
        Assert.False(string.IsNullOrWhiteSpace(runtime), "The gate must supply the real erp_runtime login.");
        Assert.False(string.IsNullOrWhiteSpace(adminConnection), "The gate must supply the isolated migration identity for controlled setup.");

        await using var adminSource = NpgsqlDataSource.Create(adminConnection!);
        await using var runtimeSource = NpgsqlDataSource.Create(runtime!);
        var organizationId = Guid.NewGuid();
        var establishmentId = Guid.NewGuid();
        var scope = new AccessScope(organizationId, "test-only-actor", "Synthetic",
            [new(establishmentId, "Unidade de teste")], LegacyManager: true, ActiveEstablishmentId: establishmentId);
        var access = new Access(scope);
        var directory = new Directory(organizationId, establishmentId);
        var team = new Team();
        var hierarchy = new Hierarchy();
        var store = new PostgresPropertyStore(runtimeSource);
        var agenda = new CommercialAppointmentService(access, store, directory, store, store);
        var actionService = new NegotiationActionService(access, store, store, directory, team, hierarchy, agenda);
        var negotiationService = new NegotiationService(access, store, store, directory, team, hierarchy);
        var transferService = new NegotiationActivityTransferService(access, store, store, directory, team, hierarchy, new TestPreviewProtector());

        var owner = (await new PropertyService(access, store).CreateOwnerAsync("Proprietário sintético")).Value!;
        var capture = new CaptureData { Areas = new() { Built = 80, Unit = "m2" }, Rooms = new() { Bedrooms = 2, Parking = 1 } };
        capture.Values.Sale.Authorized = 350000;
        var properties = new PropertyServiceV2(access, store);
        var property = await properties.SaveAsync(new(Code: "S05B2-" + Guid.NewGuid().ToString("N")[..8],
            Title: "Imóvel sintético", OwnerId: owner.Id, Category: "Residencial", Type: "Apartamento", Purpose: "Venda",
            Price: 350000, Address: new("Rua sintética", "12345678", Latitude: 1, Longitude: 1), Capture: capture));
        Assert.True(property.IsSuccess, property.Code);
        var submitted = await properties.SubmitForReviewAsync(property.Value!.Id, property.Value.Version);
        Assert.True(submitted.IsSuccess, submitted.Code);
        var approved = await properties.ApproveAsync(property.Value.Id, submitted.Value!.Version);
        Assert.True(approved.IsSuccess, approved.Code);
        var offer = Assert.Single(await store.ListAnnouncementsAsync(scope, approved.Value!.Id, default), x => x.LegacyAnchor);
        var customerResult = await new CustomerService(access, store, directory).SaveAsync(new("Cliente sintético", "Física",
            "11999998888", null, "Site", "Compra", scope.Subject, ResponsibleName: "Atendente de teste"));
        Assert.True(customerResult.IsSuccess, customerResult.Code);
        var negotiationResult = await negotiationService.OpenAsync(new(Guid.NewGuid(), customerResult.Value!.Id, approved.Value.Id, offer.Id));
        Assert.True(negotiationResult.IsSuccess, negotiationResult.Code);
        var negotiation = negotiationResult.Value!;

        var start = DateTimeOffset.UtcNow.Date.AddDays(5).AddHours(14);
        var contactInput = new NegotiationActionInput(Guid.NewGuid(), NegotiationActionKinds.Contact,
            "Retornar sobre a proposta", start.AddDays(1), null, ExpectedNegotiationVersion: negotiation.Version);
        var contact = await actionService.CreateAsync(negotiation.Id, contactInput);
        Assert.True(contact.IsSuccess, contact.Code);
        var appointmentInput = new AppointmentInput(Guid.NewGuid(), AppointmentRules.Office, null, start, start.AddHours(1), "Unidade de teste", "Visita vinculada");
        var actionAppointmentInput = new NegotiationActionInput(appointmentInput.RequestId, NegotiationActionKinds.Appointment,
            "Visita ao imóvel", null, null, appointmentInput, negotiation.Version);
        var linkedAction = await actionService.CreateAsync(negotiation.Id, actionAppointmentInput);
        Assert.True(linkedAction.IsSuccess, linkedAction.Code);
        var linkedAppointment = (await store.FindAppointmentAsync(scope, linkedAction.Value!.AppointmentId!.Value, default))!;
        var freeAppointmentInput = new AppointmentInput(Guid.NewGuid(), AppointmentRules.Office, null,
            start.AddHours(2), start.AddHours(3), "Unidade de teste", "Compromisso sem conflito");
        var freeLinkedAction = await actionService.CreateAsync(negotiation.Id,
            new(freeAppointmentInput.RequestId, NegotiationActionKinds.Appointment, "Reunião de alinhamento", null, null,
                freeAppointmentInput, negotiation.Version));
        Assert.True(freeLinkedAction.IsSuccess, freeLinkedAction.Code);
        var freeLinkedAppointment = (await store.FindAppointmentAsync(scope, freeLinkedAction.Value!.AppointmentId!.Value, default))!;

        var destinationCustomer = await new CustomerService(access, store, directory).SaveAsync(new("Cliente de agenda sintético", "Física",
            "11999998889", null, "Site", "Compra", "second-actor", ResponsibleName: "Segundo atendente"));
        Assert.True(destinationCustomer.IsSuccess, destinationCustomer.Code);
        var existingAppointment = await agenda.CreateAsync(destinationCustomer.Value!.Id,
            new(Guid.NewGuid(), AppointmentRules.Office, null, start.AddMinutes(30), start.AddHours(1).AddMinutes(30), "Unidade de teste", "Conflito sintético"));
        Assert.True(existingAppointment.IsSuccess, existingAppointment.Code);

        // A changed destination schedule invalidates the first preview, even though it still overlaps.
        var staleSchedulePreview = await transferService.PreviewAsync(negotiation.Id,
            new(Guid.NewGuid(), "second-actor", scope.Subject, [contact.Value!.Id, linkedAction.Value.Id, freeLinkedAction.Value.Id], negotiation.Version));
        Assert.True(staleSchedulePreview.IsSuccess, staleSchedulePreview.Code);
        var rescheduled = await agenda.ChangeAsync(new(existingAppointment.Value!.Id, destinationCustomer.Value.Id, null,
            existingAppointment.Value.Version, "Reagendar", new(Guid.NewGuid(), AppointmentRules.Office, null,
                start.AddMinutes(45), start.AddHours(1).AddMinutes(45), "Unidade de teste", "Conflito atualizado")));
        Assert.True(rescheduled.IsSuccess, rescheduled.Code);
        Assert.Equal("NegotiationTransfer.PreviewExpired", (await transferService.ConfirmAsync(negotiation.Id, staleSchedulePreview.Value!.Token)).Code);

        // A new eligible action added after preview invalidates the complete action snapshot.
        var staleActionsPreview = await transferService.PreviewAsync(negotiation.Id,
            new(Guid.NewGuid(), "second-actor", scope.Subject, [contact.Value.Id, linkedAction.Value.Id, freeLinkedAction.Value.Id], negotiation.Version));
        Assert.True(staleActionsPreview.IsSuccess, staleActionsPreview.Code);
        var unselected = await actionService.CreateAsync(negotiation.Id,
            new(Guid.NewGuid(), NegotiationActionKinds.Pending, "Ação fora da seleção", start.AddDays(2), null,
                ExpectedNegotiationVersion: negotiation.Version));
        Assert.True(unselected.IsSuccess, unselected.Code);
        Assert.Equal("NegotiationTransfer.PreviewExpired", (await transferService.ConfirmAsync(negotiation.Id, staleActionsPreview.Value!.Token)).Code);

        var rollbackPreview = await transferService.PreviewAsync(negotiation.Id,
            new(Guid.NewGuid(), "second-actor", scope.Subject, [contact.Value.Id, linkedAction.Value.Id, freeLinkedAction.Value.Id], negotiation.Version));
        Assert.True(rollbackPreview.IsSuccess, rollbackPreview.Code);
        await using (var admin = await adminSource.OpenConnectionAsync())
        await using (var revoke = new NpgsqlCommand("REVOKE INSERT ON real_estate.commercial_negotiation_transfer_followup_events FROM erp_runtime", admin))
            await revoke.ExecuteNonQueryAsync();
        try
        {
            await Assert.ThrowsAsync<PostgresException>(() => transferService.ConfirmAsync(negotiation.Id, rollbackPreview.Value!.Token));
            Assert.Equal("test-only-actor", (await store.FindNegotiationAsync(scope, negotiation.Id, default))!.ResponsibleSubject);
            Assert.Equal("test-only-actor", (await store.ListActionsAsync(scope, negotiation.Id, default)).Single(x => x.Id == contact.Value.Id).ResponsibleSubject);
            Assert.Equal("test-only-actor", (await store.FindAppointmentAsync(scope, linkedAppointment.Id, default))!.ResponsibleSubject);
            var rolledBackFollowups = await transferService.FollowupsAsync(negotiation.Id);
            Assert.True(rolledBackFollowups.IsSuccess, rolledBackFollowups.Code);
            Assert.Empty(rolledBackFollowups.Value!);
        }
        finally
        {
            await using var admin = await adminSource.OpenConnectionAsync();
            await using var grant = new NpgsqlCommand("GRANT SELECT,INSERT ON real_estate.commercial_negotiation_transfer_followup_events TO erp_runtime", admin);
            await grant.ExecuteNonQueryAsync();
        }

        var preview = await transferService.PreviewAsync(negotiation.Id,
            new(Guid.NewGuid(), "second-actor", scope.Subject, [contact.Value.Id, linkedAction.Value.Id, freeLinkedAction.Value.Id], negotiation.Version));
        Assert.True(preview.IsSuccess, preview.Code);
        Assert.True(Assert.Single(preview.Value!.Decisions, x => x.ActionId == contact.Value.Id).WillTransfer);
        var preservedDecision = Assert.Single(preview.Value.Decisions, x => x.ActionId == linkedAction.Value.Id);
        Assert.False(preservedDecision.WillTransfer);
        Assert.Equal("ExistingAppointment", preservedDecision.ConflictReason);
        Assert.True(Assert.Single(preview.Value.Decisions, x => x.ActionId == freeLinkedAction.Value.Id).WillTransfer);

        var confirmations = await Task.WhenAll(
            transferService.ConfirmAsync(negotiation.Id, preview.Value.Token),
            transferService.ConfirmAsync(negotiation.Id, preview.Value.Token));
        Assert.All(confirmations, x => Assert.True(x.IsSuccess, x.Code));
        Assert.Equal(confirmations[0].Value, confirmations[1].Value);
        var confirmed = confirmations[0].Value!;
        Assert.Equal(2, confirmed.TransferredActions);
        Assert.Equal(1, confirmed.PreservedAppointments);
        Assert.Equal(1, confirmed.CreatedFollowups);

        var afterTransfer = (await store.FindNegotiationAsync(scope, negotiation.Id, default))!;
        Assert.Equal("second-actor", afterTransfer.ResponsibleSubject);
        Assert.Equal(establishmentId, afterTransfer.EstablishmentId);
        var actions = await store.ListActionsAsync(scope, negotiation.Id, default);
        Assert.Equal("second-actor", actions.Single(x => x.Id == contact.Value.Id).ResponsibleSubject);
        Assert.Equal("test-only-actor", actions.Single(x => x.Id == linkedAction.Value.Id).ResponsibleSubject);
        Assert.Equal("second-actor", actions.Single(x => x.Id == freeLinkedAction.Value.Id).ResponsibleSubject);
        Assert.Equal("test-only-actor", actions.Single(x => x.Id == unselected.Value!.Id).ResponsibleSubject);
        Assert.Equal("test-only-actor", (await store.FindAppointmentAsync(scope, linkedAppointment.Id, default))!.ResponsibleSubject);
        Assert.Equal("second-actor", (await store.FindAppointmentAsync(scope, freeLinkedAppointment.Id, default))!.ResponsibleSubject);
        Assert.Equal("second-actor", (await store.FindAppointmentAsync(scope, existingAppointment.Value.Id, default))!.ResponsibleSubject);
        var actionEvents = await actionService.EventsAsync(negotiation.Id);
        Assert.True(actionEvents.IsSuccess, actionEvents.Code);
        Assert.Contains(actionEvents.Value!, x => x.ActionId == contact.Value.Id && x.Event == "ResponsibilityTransferred" &&
            x.Actor == scope.Subject && x.ResponsibleSubject == "second-actor");
        var negotiationEvents = await negotiationService.EventsAsync(negotiation.Id);
        Assert.True(negotiationEvents.IsSuccess, negotiationEvents.Code);
        Assert.Contains(negotiationEvents.Value!, x => x.Action == "ResponsibilityTransferred" &&
            x.ResponsibleSubject == "second-actor" && x.PreviousResponsibleSubject == "test-only-actor");
        var appointmentEvents = await agenda.EventsAsync(customerResult.Value.Id);
        Assert.True(appointmentEvents.IsSuccess, appointmentEvents.Code);
        Assert.Contains(appointmentEvents.Value!, x => x.AppointmentId == freeLinkedAppointment.Id && x.Action == "ResponsibilityTransferred" &&
            x.ResponsibleSubject == "second-actor");
        await using (var admin = await adminSource.OpenConnectionAsync())
        {
            await using var actionHistory = new NpgsqlCommand("SELECT previous_responsible_subject FROM real_estate.commercial_negotiation_action_events WHERE action_id=@id AND event='ResponsibilityTransferred'", admin);
            actionHistory.Parameters.AddWithValue("id", contact.Value.Id);
            Assert.Equal("test-only-actor", await actionHistory.ExecuteScalarAsync());
            await using var appointmentHistory = new NpgsqlCommand("SELECT previous_responsible_subject FROM real_estate.commercial_appointment_events WHERE appointment_id=@id AND action='ResponsibilityTransferred'", admin);
            appointmentHistory.Parameters.AddWithValue("id", freeLinkedAppointment.Id);
            Assert.Equal("test-only-actor", await appointmentHistory.ExecuteScalarAsync());
        }
        var transferEvents = await transferService.FollowupEventsAsync(negotiation.Id);
        Assert.True(transferEvents.IsSuccess, transferEvents.Code);
        var followups = await transferService.FollowupsAsync(negotiation.Id);
        Assert.True(followups.IsSuccess, followups.Code);
        var followup = Assert.Single(followups.Value!);
        Assert.Equal("Pendente de tratamento", followup.Status);
        Assert.Equal("test-only-actor", followup.OriginalResponsibleSubject);
        Assert.Single(transferEvents.Value!);

        await using (var runtimeConnection = await runtimeSource.OpenConnectionAsync())
        await using (var mutate = new NpgsqlCommand("UPDATE real_estate.commercial_negotiation_transfer_followup_events SET event='Alterado' WHERE id=@id", runtimeConnection))
        {
            mutate.Parameters.AddWithValue("id", followup.Id);
            var denied = await Assert.ThrowsAsync<PostgresException>(() => mutate.ExecuteNonQueryAsync());
            Assert.Equal("42501", denied.SqlState);
        }

        // The PostgreSQL login is the runtime role; RLS hides these records outside either tenant dimension.
        var otherBranchId = Guid.NewGuid();
        var otherBranch = scope with { ActiveEstablishmentId = otherBranchId, Establishments = [new(otherBranchId, "Outra unidade")] };
        var otherBranchService = new NegotiationActivityTransferService(new Access(otherBranch), store, store, directory, team, hierarchy, new TestPreviewProtector());
        Assert.Equal("Resource.NotFound", (await otherBranchService.EligibleAsync(negotiation.Id)).Code);
        var otherOrganization = scope with { OrganizationId = Guid.NewGuid() };
        var otherOrganizationService = new NegotiationActivityTransferService(new Access(otherOrganization), store, store, directory, team, hierarchy, new TestPreviewProtector());
        Assert.Equal("Resource.NotFound", (await otherOrganizationService.EligibleAsync(negotiation.Id)).Code);

        var resolution = new NegotiationActivityFollowupResolution(Guid.NewGuid(), followup.Version, "Contato realizado; agenda mantida");
        var resolved = await Task.WhenAll(
            transferService.ResolveAsync(negotiation.Id, followup.Id, resolution),
            transferService.ResolveAsync(negotiation.Id, followup.Id, resolution));
        Assert.All(resolved, x => Assert.True(x.IsSuccess, x.Code));
        Assert.Equal(resolved[0].Value, resolved[1].Value);
        var resolvedFollowup = resolved[0].Value!;
        Assert.Equal("Resolvido", resolvedFollowup.Status);
        Assert.Equal("Contato realizado; agenda mantida", resolvedFollowup.Result);
        Assert.Equal(2, (await transferService.FollowupEventsAsync(negotiation.Id)).Value!.Count);
    }
}
