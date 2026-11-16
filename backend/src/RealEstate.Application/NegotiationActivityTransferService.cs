using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RealEstate.Domain;

namespace RealEstate.Application;

public sealed class NegotiationActivityTransferService(ICoreAccess access, INegotiationStore negotiations,
    INegotiationActivityTransferStore transfers, ICustomerDirectory directory, ITeamStore team,
    IOperationalHierarchyAccess hierarchy, ITransferPreviewProtector protector)
{
    private static bool Same(AccessScope s, Outcome<AccessScope> x) => x.IsSuccess && x.Value!.OrganizationId == s.OrganizationId &&
        x.Value.Subject == s.Subject && x.Value.ActiveEstablishmentId == s.ActiveEstablishmentId &&
        x.Value.Establishments?.Any(e => e.Id == s.ActiveEstablishmentId) == true;
    private async Task<Outcome<AccessScope>> Scope(CancellationToken ct)
    {
        var m = await access.RequireAsync("negotiations", "NEGOTIATION_MANAGE", ct);
        if (!m.IsSuccess) return Outcome<AccessScope>.Fail(m.Code!);
        var s = m.Value!;
        if (s.OrganizationId == Guid.Empty || string.IsNullOrWhiteSpace(s.Subject) || s.ActiveEstablishmentId is not { } unit || s.Establishments?.All(e => e.Id != unit) != false)
            return Outcome<AccessScope>.Fail("Context.ActiveEstablishmentRequired");
        if (!Same(s, await access.RequireAsync("negotiations", "NEGOTIATION_VIEW", ct))) return Outcome<AccessScope>.Fail("Access.Forbidden");
        foreach(var right in new[]{("customers","CUSTOMER_READ"),("properties","PROPERTY_READ")})
            if(!Same(s,await access.RequireAsync(right.Item1,right.Item2,ct)))return Outcome<AccessScope>.Fail("Access.Forbidden");
        return new(s);
    }
    private async Task<Outcome<Negotiation>> Current(AccessScope s, Guid id, CancellationToken ct)
    {
        var n = await negotiations.FindNegotiationAsync(s, id, ct);
        if (n is null || n.EstablishmentId != s.ActiveEstablishmentId) return Outcome<Negotiation>.Fail("Resource.NotFound");
        if (n.ResponsibleMembershipId == Guid.Empty) return Outcome<Negotiation>.Fail("Negotiation.ResponsibleHistoryUnavailable");
        var a = await hierarchy.AuthorityAsync(s, n.ResponsibleMembershipId, ct);
        if (!a.IsSuccess) return Outcome<Negotiation>.Fail(a.Code!);
        if (!a.Value!.IsImmediateSupervisor)
        {
            if (!a.Value.CanRecoverOperationalResponsibility) return Outcome<Negotiation>.Fail("Access.Forbidden");
            var old = await hierarchy.RecipientAsync(s, n.ResponsibleMembershipId, ct);
            if (!old.IsSuccess) return Outcome<Negotiation>.Fail(old.Code!);
            if (old.Value!.IsEligible) return Outcome<Negotiation>.Fail("Access.Forbidden");
        }
        return new(n);
    }
    private async Task<Outcome<NegotiationAssignee>> Person(AccessScope s, string subject, CancellationToken ct)
    {
        var r = await directory.ReadAsync(s, s.ActiveEstablishmentId!.Value, subject, ct);
        if (!r.IsSuccess) return Outcome<NegotiationAssignee>.Fail(r.Code!);
        if (r.Value is not { Items: not null } d || d.OrganizationId != s.OrganizationId || d.EstablishmentId != s.ActiveEstablishmentId || d.Items.Any(x => x is null || string.IsNullOrWhiteSpace(x.Subject)) || d.Items.Select(x => x.Subject).Distinct().Count() != d.Items.Count)
            return Outcome<NegotiationAssignee>.Fail("Dependency.InvalidResponse");
        var p = d.Items.SingleOrDefault(x => x.Subject == subject);
        if (p is null || p.MembershipId == Guid.Empty || p.ProfileId == Guid.Empty) return Outcome<NegotiationAssignee>.Fail("Negotiation.ResponsibleIneligible");
        var profile = await team.FindAsync(s, p.ProfileId, ct);
        if (profile?.Data.Status != "Ativo") return Outcome<NegotiationAssignee>.Fail("Negotiation.ResponsibleIneligible");
        var eligible = await hierarchy.RecipientAsync(s, p.MembershipId, ct);
        if (!eligible.IsSuccess) return Outcome<NegotiationAssignee>.Fail(eligible.Code!);
        return eligible.Value!.IsEligible ? new(new(p.Subject, p.MembershipId, p.ProfileId, p.DisplayName)) : Outcome<NegotiationAssignee>.Fail("Negotiation.ResponsibleIneligible");
    }
    public async Task<Outcome<IReadOnlyList<NegotiationActivityTransferCandidate>>> EligibleAsync(Guid id, CancellationToken ct = default)
    {
        var s = await Scope(ct); if (!s.IsSuccess) return Outcome<IReadOnlyList<NegotiationActivityTransferCandidate>>.Fail(s.Code!);
        var n = await Current(s.Value!, id, ct); return n.IsSuccess ? new(await transfers.EligibleActivitiesAsync(s.Value!, id, ct)) : Outcome<IReadOnlyList<NegotiationActivityTransferCandidate>>.Fail(n.Code!);
    }
    public async Task<Outcome<NegotiationActivityTransferPreview>> PreviewAsync(Guid id, NegotiationActivityTransferPreviewInput input, CancellationToken ct = default)
    {
        if (input is null || input.RequestId == Guid.Empty || input.ExpectedNegotiationVersion < 1 || string.IsNullOrWhiteSpace(input.DestinationSubject) || input.SelectedActionIds is null || input.SelectedActionIds.Distinct().Count() != input.SelectedActionIds.Count || input.Reason?.Length > 500)
            return Outcome<NegotiationActivityTransferPreview>.Fail("NegotiationTransfer.InvalidInput");
        var s = await Scope(ct); if (!s.IsSuccess) return Outcome<NegotiationActivityTransferPreview>.Fail(s.Code!);
        var n = await Current(s.Value!, id, ct); if (!n.IsSuccess) return Outcome<NegotiationActivityTransferPreview>.Fail(n.Code!);
        if (n.Value!.Version != input.ExpectedNegotiationVersion) return Outcome<NegotiationActivityTransferPreview>.Fail("NegotiationTransfer.PreviewExpired");
        var dest = await Person(s.Value!, input.DestinationSubject.Trim(), ct); if (!dest.IsSuccess) return Outcome<NegotiationActivityTransferPreview>.Fail(dest.Code!);
        var followSubject = string.IsNullOrWhiteSpace(input.FollowupResponsibleSubject) ? s.Value!.Subject : input.FollowupResponsibleSubject.Trim();
        var follow = await Person(s.Value!, followSubject, ct); if (!follow.IsSuccess) return Outcome<NegotiationActivityTransferPreview>.Fail(follow.Code!);
        var all = await transfers.EligibleActivitiesAsync(s.Value!, id, ct);
        if (input.SelectedActionIds.Except(all.Select(x => x.Action.Id)).Any()) return Outcome<NegotiationActivityTransferPreview>.Fail("NegotiationTransfer.PreviewExpired");
        var selected = all.Where(x => input.SelectedActionIds.Contains(x.Action.Id)).ToArray();
        if(selected.Any(x=>x.Appointment is not null)&&!Same(s.Value!,await access.RequireAsync("customers","CUSTOMER_CONTACT_CREATE",ct)))return Outcome<NegotiationActivityTransferPreview>.Fail("Access.Forbidden");
        var apps = selected.Where(x => x.Appointment is not null).Select(x => x.Appointment!).ToArray();
        var schedule = apps.Length == 0 ? Array.Empty<CommercialAppointment>() : (await transfers.RecipientScheduleAsync(s.Value!, dest.Value!.Subject, apps.Min(x => x.StartsAt), apps.Max(x => x.EndsAt), ct)).Where(x=>apps.All(selectedAppointment=>selectedAppointment.Id!=x.Id)).ToArray();
        var decisions = NegotiationActivityTransferRules.Classify(selected, schedule, dest.Value!.Subject);
        if (!decisions.IsSuccess) return Outcome<NegotiationActivityTransferPreview>.Fail(decisions.Code!);
        var selection = all.OrderBy(x => x.Action.Id).Select(x => new NegotiationActivityTransferSelection(x.Action.Id, x.Action.Version, x.Appointment?.Version, input.SelectedActionIds.Contains(x.Action.Id))).ToArray();
        var observed = schedule.OrderBy(x => x.StartsAt.UtcTicks).ThenBy(x => x.EndsAt.UtcTicks).ThenBy(x => x.Id).Select(x => new NegotiationActivityTransferObservedAppointment(x.Id, x.Version, x.StartsAt, x.EndsAt, x.Status, x.ResponsibleSubject)).ToArray();
        var fingerprint = Fingerprint(s.Value!, n.Value!, dest.Value.Subject, follow.Value!.Subject, selection, observed, input.Reason);
        var snapshot = new NegotiationActivityTransferSnapshot(s.Value!.OrganizationId, s.Value.ActiveEstablishmentId!.Value, id, input.RequestId, n.Value.Version, dest.Value.Subject, follow.Value.Subject, input.Reason, selection, observed, fingerprint, DateTimeOffset.UtcNow);
        return new(new(protector.Protect(JsonSerializer.Serialize(snapshot)), all, decisions.Value!));
    }
    public async Task<Outcome<NegotiationActivityTransferReceipt>> ConfirmAsync(Guid id, string? token, CancellationToken ct = default)
    {
        var decoded = TransferPreviewTokenRules.Read(token, protector, DateTimeOffset.UtcNow);
        if (!decoded.IsSuccess) return Outcome<NegotiationActivityTransferReceipt>.Fail(decoded.Code!);
        var s = await Scope(ct); if (!s.IsSuccess) return Outcome<NegotiationActivityTransferReceipt>.Fail(s.Code!);
        var snap = decoded.Value!;
        if (snap.NegotiationId != id || snap.OrganizationId != s.Value!.OrganizationId || snap.EstablishmentId != s.Value.ActiveEstablishmentId) return Outcome<NegotiationActivityTransferReceipt>.Fail("NegotiationTransfer.PreviewExpired");
        var replay=await transfers.ExistingReceiptAsync(s.Value,snap.RequestId,snap.Fingerprint,ct);
        if(!replay.IsSuccess)return Outcome<NegotiationActivityTransferReceipt>.Fail(replay.Code!);
        if(replay.Value is not null)return new(replay.Value);
        var n = await Current(s.Value!, id, ct); if (!n.IsSuccess) return Outcome<NegotiationActivityTransferReceipt>.Fail(n.Code!);
        if (n.Value!.Version != snap.NegotiationVersion) return Outcome<NegotiationActivityTransferReceipt>.Fail("NegotiationTransfer.PreviewExpired");
        var dest = await Person(s.Value!, snap.DestinationSubject, ct); var follow = await Person(s.Value!, snap.FollowupResponsibleSubject, ct);
        if (!dest.IsSuccess || !follow.IsSuccess) return Outcome<NegotiationActivityTransferReceipt>.Fail(!dest.IsSuccess ? dest.Code! : follow.Code!);
        var all = await transfers.EligibleActivitiesAsync(s.Value!, id, ct); var map = snap.Selection.ToDictionary(x => x.ActionId);
        if (!NegotiationActivityTransferSnapshotRules.MatchesActivities(snap.Selection, all)) return Outcome<NegotiationActivityTransferReceipt>.Fail("NegotiationTransfer.PreviewExpired");
        var selected = all.Where(x => map[x.Action.Id].Selected).ToArray(); var apps = selected.Where(x => x.Appointment is not null).Select(x => x.Appointment!).ToArray();
        if(apps.Length>0&&!Same(s.Value!,await access.RequireAsync("customers","CUSTOMER_CONTACT_CREATE",ct)))return Outcome<NegotiationActivityTransferReceipt>.Fail("Access.Forbidden");
        var schedule = apps.Length == 0 ? Array.Empty<CommercialAppointment>() : (await transfers.RecipientScheduleAsync(s.Value!, dest.Value!.Subject, apps.Min(x => x.StartsAt), apps.Max(x => x.EndsAt), ct)).Where(x=>apps.All(selectedAppointment=>selectedAppointment.Id!=x.Id)).ToArray();
        var observed = schedule.OrderBy(x => x.StartsAt.UtcTicks).ThenBy(x => x.EndsAt.UtcTicks).ThenBy(x => x.Id).Select(x => new NegotiationActivityTransferObservedAppointment(x.Id, x.Version, x.StartsAt, x.EndsAt, x.Status, x.ResponsibleSubject)).ToArray();
        var fingerprint = Fingerprint(s.Value!, n.Value!, dest.Value!.Subject, follow.Value!.Subject, snap.Selection, observed, snap.Reason);
        if (snap.Fingerprint != fingerprint) return Outcome<NegotiationActivityTransferReceipt>.Fail("NegotiationTransfer.PreviewExpired");
        var decisions = NegotiationActivityTransferRules.Classify(selected, schedule, dest.Value.Subject);
        if (!decisions.IsSuccess) return Outcome<NegotiationActivityTransferReceipt>.Fail(decisions.Code!);
        return await transfers.ConfirmTransferAsync(s.Value!, snap, dest.Value, decisions.Value!, ct);
    }
    public async Task<Outcome<IReadOnlyList<NegotiationActivityTransferFollowup>>> FollowupsAsync(Guid id, CancellationToken ct = default)
    {
        var s=await Scope(ct);if(!s.IsSuccess)return Outcome<IReadOnlyList<NegotiationActivityTransferFollowup>>.Fail(s.Code!);var n=await Current(s.Value!,id,ct);if(!n.IsSuccess)return Outcome<IReadOnlyList<NegotiationActivityTransferFollowup>>.Fail(n.Code!);
        var rows=await transfers.FollowupsAsync(s.Value!,id,ct);var people=await directory.ReadAsync(s.Value!,s.Value!.ActiveEstablishmentId!.Value,token:ct);
        var d=people.IsSuccess&&people.Value is {Items:not null} directoryResult&&directoryResult.OrganizationId==s.Value.OrganizationId&&directoryResult.EstablishmentId==s.Value.ActiveEstablishmentId?directoryResult:null;
        var names=d?.Items.Where(x=>x is not null&&!string.IsNullOrWhiteSpace(x.Subject)&&!string.IsNullOrWhiteSpace(x.DisplayName)).GroupBy(x=>x.Subject).ToDictionary(x=>x.Key,x=>x.First().DisplayName,StringComparer.Ordinal)??new Dictionary<string,string>(StringComparer.Ordinal);
        return new(rows.Select(x=>x with{ResponsibleName=names.TryGetValue(x.ResponsibleSubject,out var name)?name:"Responsável não identificado"}).ToArray());
    }
    public async Task<Outcome<IReadOnlyList<NegotiationActivityFollowupEvent>>> FollowupEventsAsync(Guid id, CancellationToken ct = default)
    { var s=await Scope(ct);if(!s.IsSuccess)return Outcome<IReadOnlyList<NegotiationActivityFollowupEvent>>.Fail(s.Code!);var n=await Current(s.Value!,id,ct);return n.IsSuccess?new(await transfers.FollowupEventsAsync(s.Value!,id,ct)):Outcome<IReadOnlyList<NegotiationActivityFollowupEvent>>.Fail(n.Code!); }
    public async Task<Outcome<NegotiationActivityTransferFollowup>> ResolveAsync(Guid id, Guid followupId, NegotiationActivityFollowupResolution input, CancellationToken ct = default)
    { var result=CustomerRules.Clean(input?.Result); if(input is null||input.RequestId==Guid.Empty||input.ExpectedVersion<1||result is null||result.Length>1000)return Outcome<NegotiationActivityTransferFollowup>.Fail("NegotiationTransfer.ResultRequired"); var s=await Scope(ct);if(!s.IsSuccess)return Outcome<NegotiationActivityTransferFollowup>.Fail(s.Code!);var n=await Current(s.Value!,id,ct);return n.IsSuccess?await transfers.ResolveFollowupAsync(s.Value!,id,followupId,input with{Result=result},ct):Outcome<NegotiationActivityTransferFollowup>.Fail(n.Code!); }
    private static string Fingerprint(AccessScope s, Negotiation n, string dest, string follow, IReadOnlyList<NegotiationActivityTransferSelection> sel, IReadOnlyList<NegotiationActivityTransferObservedAppointment> schedule, string? reason)
    { var json=JsonSerializer.Serialize(new{s.OrganizationId,s.ActiveEstablishmentId,n.Id,n.Version,n.ResponsibleSubject,dest,follow,reason,selection=sel.OrderBy(x=>x.ActionId),schedule=schedule.OrderBy(x=>x.StartsAt.UtcTicks).ThenBy(x=>x.EndsAt.UtcTicks).ThenBy(x=>x.Id)}); return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant(); }
}
