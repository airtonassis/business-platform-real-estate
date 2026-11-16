using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using RealEstate.Domain;
using RealEstate.Web;

namespace RealEstate.Tests;

// TEST ONLY: the real Razor page and application service use deterministic in-memory reads.
// Every write operation is rejected; this fixture cannot confirm or persist a transfer.
public static class NegotiationB2VisualFixture
{
    private const string ScenarioKey = "fixtureScenario";
    private static readonly Guid ContactId = Guid.Parse("61000000-0000-0000-0000-000000000001");
    private static readonly Guid AppointmentOneActionId = Guid.Parse("61000000-0000-0000-0000-000000000002");
    private static readonly Guid AppointmentTwoActionId = Guid.Parse("61000000-0000-0000-0000-000000000003");
    private static readonly Guid AppointmentThreeActionId = Guid.Parse("61000000-0000-0000-0000-000000000004");
    private static readonly Guid AppointmentOneId = Guid.Parse("62000000-0000-0000-0000-000000000001");
    private static readonly Guid AppointmentTwoId = Guid.Parse("62000000-0000-0000-0000-000000000002");
    private static readonly Guid AppointmentThreeId = Guid.Parse("62000000-0000-0000-0000-000000000003");
    private static readonly Guid ExistingAppointmentId = Guid.Parse("62000000-0000-0000-0000-000000000004");
    private static readonly Guid PendingFollowupId = Guid.Parse("63000000-0000-0000-0000-000000000001");
    private static readonly Guid ResolvedFollowupId = Guid.Parse("63000000-0000-0000-0000-000000000002");
    private static readonly Guid ActorEventId = Guid.Parse("64000000-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset DueAt = new(2026, 11, 16, 18, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset CreatedAt = new(2026, 11, 14, 14, 0, 0, TimeSpan.Zero);

    public static string DossierUrl => NegotiationVisualFixture.DossierUrl;

    public static void Configure(WebApplicationBuilder builder)
    {
        NegotiationVisualFixture.Configure(builder);
        builder.Configuration["S05B2VisualFixture"] = "TEST ONLY";
        builder.Services.RemoveAll<INegotiationActionStore>();
        builder.Services.AddSingleton<INegotiationActionStore, SyntheticActionStore>();
        builder.Services.RemoveAll<INegotiationActivityTransferStore>();
        builder.Services.AddSingleton<INegotiationActivityTransferStore, SyntheticTransferStore>();
    }

    public static void Serve(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "http" || uri.Host != "127.0.0.1")
            throw new InvalidOperationException("S05-B2 visual fixture requires loopback HTTP.");
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../backend/src/RealEstate.Web"));
        Program.Build(["--contentRoot", root, "--urls", url], Configure).Run();
    }

    private static string Scenario(IHttpContextAccessor accessor)
    {
        var request = accessor.HttpContext?.Request;
        if (request is null) return "conflicts";
        var scenario = request.Query[ScenarioKey].ToString();
        // The real Razor forms post to the production handler URL. Recover the TEST ONLY
        // scenario from same-origin navigation metadata so a POST preview stays deterministic.
        if (string.IsNullOrWhiteSpace(scenario) && Uri.TryCreate(request.Headers.Referer.ToString(), UriKind.Absolute, out var referer))
            scenario = QueryHelpers.ParseQuery(referer.Query).TryGetValue(ScenarioKey, out var value) ? value.ToString() : "";
        return scenario is "clear" or "conflicts" or "empty" or "error" ? scenario : "conflicts";
    }

    private static IReadOnlyList<NegotiationAction> Actions =>
    [
        new(ContactId, NegotiationVisualFixture.NegotiationSale, NegotiationVisualFixture.CustomerSale,
            NegotiationVisualFixture.PropertySale, null, NegotiationActionKinds.Contact,
            "Retornar sobre as condições da proposta", DueAt, "test-only-actor", "Ana Costa", "Aberta", null,
            "test-only-actor", CreatedAt, 1, CreatedAt),
        MakeAppointmentAction(AppointmentOneActionId, AppointmentOneId, "Confirmar visita ao imóvel"),
        MakeAppointmentAction(AppointmentTwoActionId, AppointmentTwoId, "Visita técnica ao imóvel"),
        MakeAppointmentAction(AppointmentThreeActionId, AppointmentThreeId, "Revisar proposta presencialmente")
    ];

    private static NegotiationAction MakeAppointmentAction(Guid actionId, Guid appointmentId, string description) =>
        new(actionId, NegotiationVisualFixture.NegotiationSale, NegotiationVisualFixture.CustomerSale,
            NegotiationVisualFixture.PropertySale, appointmentId, NegotiationActionKinds.Appointment,
            description, null, "test-only-actor", "Ana Costa", "Aberta", null, "test-only-actor", CreatedAt, 1, CreatedAt);

    private static CommercialAppointment Appointment(Guid id, DateTimeOffset start, DateTimeOffset end, string responsible) =>
        new(id, NegotiationVisualFixture.CustomerSale, NegotiationVisualFixture.Owner,
            NegotiationVisualFixture.PropertySale, AppointmentRules.PropertyVisit, "Agendado", responsible,
            responsible == "second-actor" ? "Bruno Reis" : "Ana Costa", start, end, "Casa Jardim · IM-123",
            "Dados sintéticos para avaliação visual", null, 1, CreatedAt);

    private static DateTimeOffset At(int day, int hour, int minute = 0) =>
        new(2026, 11, day, hour, minute, 0, TimeSpan.Zero);

    private static IReadOnlyList<NegotiationActivityTransferCandidate> Candidates(string scenario)
    {
        if (scenario == "empty") return [];
        var clear = scenario == "clear";
        var firstStart = clear ? At(17, 10) : At(17, 10);
        var secondStart = At(17, clear ? 13 : 13);
        var thirdStart = At(17, clear ? 15 : 13, clear ? 0 : 30);
        var appointments = new Dictionary<Guid, CommercialAppointment>
        {
            [AppointmentOneId] = Appointment(AppointmentOneId, firstStart, firstStart.AddHours(1), "test-only-actor"),
            [AppointmentTwoId] = Appointment(AppointmentTwoId, secondStart, secondStart.AddHours(1), "test-only-actor"),
            [AppointmentThreeId] = Appointment(AppointmentThreeId, thirdStart, thirdStart.AddHours(1), "test-only-actor")
        };
        return Actions.Select(action => new NegotiationActivityTransferCandidate(action,
            action.AppointmentId is { } appointmentId ? appointments[appointmentId] : null)).ToArray();
    }

    private static IReadOnlyList<NegotiationActivityTransferFollowup> Followups(string scenario)
    {
        if (scenario is "empty" or "error") return [];
        var pending = new NegotiationActivityTransferFollowup(PendingFollowupId,
            NegotiationVisualFixture.NegotiationSale, AppointmentOneActionId, AppointmentOneId,
            "ExistingAppointment", "Pendente de tratamento", "test-only-actor", "Ana Costa", "test-only-actor",
            null, "supervisor", CreatedAt, 1, CreatedAt);
        var resolved = new NegotiationActivityTransferFollowup(ResolvedFollowupId,
            NegotiationVisualFixture.NegotiationSale, AppointmentThreeActionId, AppointmentThreeId,
            "SelectedAppointment", "Resolvido", "test-only-actor", "Ana Costa", "test-only-actor",
            "A visita foi mantida com Ana Costa após alinhamento com o cliente.", "supervisor", CreatedAt.AddMinutes(5), 2, CreatedAt.AddMinutes(10));
        return [pending, resolved];
    }

    private sealed class SyntheticActionStore(IHttpContextAccessor accessor) : INegotiationActionStore
    {
        public Task<IReadOnlyList<NegotiationAction>> ListActionsAsync(AccessScope scope, Guid negotiationId, CancellationToken token) =>
            Task.FromResult<IReadOnlyList<NegotiationAction>>(Scenario(accessor) == "empty" || negotiationId != NegotiationVisualFixture.NegotiationSale ? [] : Actions);

        public Task<IReadOnlyList<NegotiationActionEvent>> ActionEventsAsync(AccessScope scope, Guid negotiationId, CancellationToken token) =>
            Task.FromResult<IReadOnlyList<NegotiationActionEvent>>(Actions.Select((action, index) =>
                new NegotiationActionEvent(Guid.Parse($"65000000-0000-0000-0000-{index + 1:000000000000}"), action.Id,
                    action.Version, "Criada", "test-only-actor", action.ResponsibleSubject, action.Description,
                    action.DueAt, null, action.CreatedAt)).ToArray());

        public Task<Outcome<NegotiationAction?>> ExistingCreateRequestAsync(AccessScope scope, Guid negotiationId, NegotiationActionInput input, CancellationToken token) =>
            Task.FromResult(Outcome<NegotiationAction?>.Fail("Access.Forbidden"));

        public Task<Outcome<NegotiationAction>> CreateActionAsync(AccessScope scope, Negotiation negotiation, NegotiationActionInput input,
            NegotiationAssignee responsible, NegotiationActionAppointment? appointment, CancellationToken token) =>
            Task.FromResult(Outcome<NegotiationAction>.Fail("Access.Forbidden"));

        public Task<Outcome<NegotiationActionCompletionResult>> CompleteActionAsync(AccessScope scope, Guid actionId,
            NegotiationActionCompletion input, CancellationToken token) =>
            Task.FromResult(Outcome<NegotiationActionCompletionResult>.Fail("Access.Forbidden"));
    }

    private sealed class SyntheticTransferStore(IHttpContextAccessor accessor) : INegotiationActivityTransferStore
    {
        public Task<Outcome<NegotiationActivityTransferReceipt?>> ExistingReceiptAsync(AccessScope scope, Guid requestId, string fingerprint, CancellationToken token) =>
            Task.FromResult(new Outcome<NegotiationActivityTransferReceipt?>((NegotiationActivityTransferReceipt?)null));

        public Task<IReadOnlyList<NegotiationActivityTransferCandidate>> EligibleActivitiesAsync(AccessScope scope, Guid negotiationId, CancellationToken token)
        {
            var scenario = Scenario(accessor);
            if (scenario == "error") throw new NpgsqlException("Synthetic visual fixture dependency unavailable");
            return Task.FromResult<IReadOnlyList<NegotiationActivityTransferCandidate>>(
                negotiationId == NegotiationVisualFixture.NegotiationSale ? Candidates(scenario) : []);
        }

        public Task<IReadOnlyList<CommercialAppointment>> RecipientScheduleAsync(AccessScope scope, string responsibleSubject,
            DateTimeOffset from, DateTimeOffset until, CancellationToken token)
        {
            if (Scenario(accessor) != "conflicts") return Task.FromResult<IReadOnlyList<CommercialAppointment>>([]);
            var existing = Appointment(ExistingAppointmentId, At(17, 10, 30), At(17, 11, 30), "second-actor");
            return Task.FromResult<IReadOnlyList<CommercialAppointment>>(
                responsibleSubject == "second-actor" && existing.StartsAt < until && existing.EndsAt > from ? [existing] : []);
        }

        public Task<Outcome<NegotiationActivityTransferReceipt>> ConfirmTransferAsync(AccessScope scope,
            NegotiationActivityTransferSnapshot snapshot, NegotiationAssignee recipient,
            IReadOnlyList<NegotiationActivityTransferDecision> decisions, CancellationToken token) =>
            Task.FromResult(Outcome<NegotiationActivityTransferReceipt>.Fail("Access.Forbidden"));

        public Task<IReadOnlyList<NegotiationActivityTransferFollowup>> FollowupsAsync(AccessScope scope, Guid negotiationId, CancellationToken token) =>
            Task.FromResult<IReadOnlyList<NegotiationActivityTransferFollowup>>(
                negotiationId == NegotiationVisualFixture.NegotiationSale ? Followups(Scenario(accessor)) : []);

        public Task<IReadOnlyList<NegotiationActivityFollowupEvent>> FollowupEventsAsync(AccessScope scope, Guid negotiationId, CancellationToken token)
        {
            var rows = Followups(Scenario(accessor));
            return Task.FromResult<IReadOnlyList<NegotiationActivityFollowupEvent>>(rows.Select((item, index) =>
                new NegotiationActivityFollowupEvent(index == 0 ? ActorEventId : Guid.Parse("64000000-0000-0000-0000-000000000002"),
                    item.Id, item.Version, item.Status == "Resolvido" ? "Resolvido" : "Follow-up criado",
                    "test-only-actor", "test-only-actor", item.Result, Guid.Parse("66000000-0000-0000-0000-000000000001"), item.UpdatedAt)).ToArray());
        }

        public Task<Outcome<NegotiationActivityTransferFollowup>> ResolveFollowupAsync(AccessScope scope, Guid negotiationId,
            Guid followupId, NegotiationActivityFollowupResolution resolution, CancellationToken token) =>
            Task.FromResult(Outcome<NegotiationActivityTransferFollowup>.Fail("Access.Forbidden"));
    }
}
