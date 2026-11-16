using RealEstate.Domain;
using RealEstate.Application;
using Xunit;

namespace RealEstate.Tests;

public sealed class NegotiationActivityTransferRulesTests
{
    private static readonly DateTimeOffset Day = new(2026, 11, 16, 0, 0, 0, TimeSpan.Zero);
    private static readonly string Destination = "destination";
    private static NegotiationActivityTransferCandidate Item(int actionMinute, int startMinute, int endMinute, int id)
    {
        var appointmentId = Guid.Parse($"00000000-0000-0000-0000-{id:000000000000}");
        var action = new NegotiationAction(appointmentId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), appointmentId,
            NegotiationActionKinds.Appointment, "Visita", null, "origin", "Responsável original", "Aberta", null,
            "author", Day.AddMinutes(actionMinute), 1, Day.AddMinutes(actionMinute));
        var appointment = new CommercialAppointment(appointmentId, action.CustomerId, null, action.PropertyId,
            AppointmentRules.PropertyVisit, "Agendado", "origin", "Responsável original", Day.AddMinutes(startMinute),
            Day.AddMinutes(endMinute), "Local", null, null, 1, Day);
        return new(action, appointment);
    }

    [Fact]
    public void Adjacent_half_open_appointments_do_not_conflict()
    {
        var first = Item(1, 600, 660, 1);
        var second = Item(2, 660, 720, 2);

        var result = NegotiationActivityTransferRules.Classify([second, first], [], Destination);

        Assert.True(result.IsSuccess, result.Code);
        Assert.All(result.Value!, x => Assert.True(x.WillTransfer));
    }

    [Fact]
    public void Selected_appointment_conflicts_are_stable_by_start_end_and_id()
    {
        var later = Item(1, 630, 690, 2);
        var earlier = Item(2, 600, 660, 1);

        var forward = NegotiationActivityTransferRules.Classify([later, earlier], [], Destination);
        var reverse = NegotiationActivityTransferRules.Classify([earlier, later], [], Destination);

        Assert.True(forward.IsSuccess, forward.Code);
        Assert.Equal(forward.Value, reverse.Value);
        Assert.True(Assert.Single(forward.Value!, x => x.ActionId == earlier.Action.Id).WillTransfer);
        var rejected = Assert.Single(forward.Value!, x => x.ActionId == later.Action.Id);
        Assert.False(rejected.WillTransfer);
        Assert.Equal(earlier.Appointment!.Id, rejected.ConflictingAppointmentId);
        Assert.Equal("SelectedAppointment", rejected.ConflictReason);
    }

    [Fact]
    public void Appointment_rejected_by_existing_schedule_does_not_block_later_selected_item()
    {
        var rejected = Item(1, 570, 630, 3);
        var later = Item(2, 615, 645, 4);
        var existing = new CommercialAppointment(Guid.NewGuid(), Guid.NewGuid(), null, null, AppointmentRules.Office,
            "Agendado", Destination, "Destinatário", Day.AddMinutes(585), Day.AddMinutes(600), "Local", null, null, 1, Day);

        var result = NegotiationActivityTransferRules.Classify([rejected, later], [existing], Destination);

        Assert.True(result.IsSuccess, result.Code);
        Assert.False(Assert.Single(result.Value!, x => x.ActionId == rejected.Action.Id).WillTransfer);
        Assert.True(Assert.Single(result.Value!, x => x.ActionId == later.Action.Id).WillTransfer);
    }

    [Fact]
    public void Snapshot_detects_version_change_in_an_unselected_action_and_preserves_other_items()
    {
        var selected = Item(1, 600, 660, 1);
        var unselected = Item(2, 700, 760, 2);
        var snapshot = Snapshot([selected, unselected], selected.Action.Id);
        var current = new[] { selected, unselected with { Action = unselected.Action with { Version = 2 } } };

        Assert.False(NegotiationActivityTransferSnapshotRules.MatchesActivities(snapshot.Selection, current));
        Assert.True(NegotiationActivityTransferSnapshotRules.MatchesActivities(snapshot.Selection,
            [selected, unselected]));
        Assert.False(snapshot.Selection.Single(x => x.ActionId == unselected.Action.Id).Selected);
    }

    [Fact]
    public void Snapshot_detects_changed_conflicting_appointment_between_preview_and_confirmation()
    {
        var item = Item(1, 600, 660, 1);
        var existing = new CommercialAppointment(Guid.NewGuid(), Guid.NewGuid(), null, null, AppointmentRules.Office,
            "Agendado", Destination, "Destinatário", Day.AddMinutes(630), Day.AddMinutes(690), "Local", null, null, 1, Day);
        var conflict = NegotiationActivityTransferRules.Classify([item], [existing], Destination);
        var snapshot = Snapshot([item], item.Action.Id);
        var changed = item with { Appointment = item.Appointment! with { Version = 2 } };

        Assert.False(Assert.Single(conflict.Value!).WillTransfer);
        Assert.False(NegotiationActivityTransferSnapshotRules.MatchesActivities(snapshot.Selection, [changed]));
    }

    [Fact]
    public void Resolution_replay_requires_same_actor_version_and_result()
    {
        var request = Guid.NewGuid();
        var replay = new NegotiationActivityFollowupResolutionReplay(request, "actor-a", 4, "Resolvido: manter");

        Assert.True(NegotiationActivityFollowupResolutionRules.MatchesReplay(replay, request, "actor-a", 3, "Resolvido: manter"));
        Assert.False(NegotiationActivityFollowupResolutionRules.MatchesReplay(replay, request, "actor-b", 3, "Resolvido: manter"));
        Assert.False(NegotiationActivityFollowupResolutionRules.MatchesReplay(replay, request, "actor-a", 2, "Resolvido: manter"));
        Assert.False(NegotiationActivityFollowupResolutionRules.MatchesReplay(replay, request, "actor-a", 3, "Outro resultado"));
        Assert.False(NegotiationActivityFollowupResolutionRules.MatchesReplay(replay, Guid.NewGuid(), "actor-a", 3, "Resolvido: manter"));
    }

    [Fact]
    public void Existing_pending_followup_is_rejected_with_a_specific_conflict()
    {
        var created = NegotiationActivityFollowupRules.CreatedOrPending(Guid.NewGuid());
        var alreadyPending = NegotiationActivityFollowupRules.CreatedOrPending(null);

        Assert.True(created.IsSuccess);
        Assert.False(alreadyPending.IsSuccess);
        Assert.Equal("NegotiationTransfer.FollowupAlreadyPending", alreadyPending.Code);
        Assert.Equal(409, RealEstate.Web.Program.Status(alreadyPending.Code));
    }

    [Fact]
    public void Conflict_reasons_have_commercial_labels_without_technical_identifiers()
    {
        Assert.Equal("Conflito com compromisso já agendado", NegotiationActivityTransferRules.ConflictReasonLabel("ExistingAppointment"));
        Assert.Equal("Conflito com outro compromisso selecionado", NegotiationActivityTransferRules.ConflictReasonLabel("SelectedAppointment"));
        Assert.Equal("Conflito de agenda", NegotiationActivityTransferRules.ConflictReasonLabel("unexpected-value"));
    }

    [Theory]
    [InlineData(null, "NegotiationTransfer.InvalidInput")]
    [InlineData("", "NegotiationTransfer.InvalidInput")]
    [InlineData("bad-token", "NegotiationTransfer.PreviewExpired")]
    public void Preview_token_is_validated_before_snapshot_use(string? token, string expectedCode)
    {
        var protector = new TestPreviewProtector();
        var result = TransferPreviewTokenRules.Read(token, protector, DateTimeOffset.UtcNow);

        Assert.False(result.IsSuccess);
        Assert.Equal(expectedCode, result.Code);
        Assert.Equal(token is null or "" ? 0 : 1, protector.UnprotectCalls);
    }

    [Fact]
    public void Expired_preview_token_is_rejected()
    {
        var snapshot = Snapshot([], Guid.NewGuid()) with { IssuedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-21) };
        var protector = new TestPreviewProtector(System.Text.Json.JsonSerializer.Serialize(snapshot));

        var result = TransferPreviewTokenRules.Read("protected", protector, DateTimeOffset.UtcNow);

        Assert.False(result.IsSuccess);
        Assert.Equal("NegotiationTransfer.PreviewExpired", result.Code);
    }

    private static NegotiationActivityTransferSnapshot Snapshot(
        IReadOnlyCollection<NegotiationActivityTransferCandidate> candidates, Guid selectedId) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, Destination, "followup", null,
            candidates.Select(x => new NegotiationActivityTransferSelection(x.Action.Id, x.Action.Version,
                x.Appointment?.Version, x.Action.Id == selectedId)).ToArray(), [], new string('a', 64), DateTimeOffset.UtcNow);

    private sealed class TestPreviewProtector(string? plaintext = null) : ITransferPreviewProtector
    {
        public int UnprotectCalls { get; private set; }
        public string Protect(string value) => value;
        public string? Unprotect(string protectedValue)
        {
            UnprotectCalls++;
            return plaintext ?? throw new System.Security.Cryptography.CryptographicException("invalid");
        }
    }
}
