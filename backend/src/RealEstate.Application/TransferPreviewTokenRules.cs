using System.Security.Cryptography;
using System.Text.Json;
using RealEstate.Domain;

namespace RealEstate.Application;

public static class TransferPreviewTokenRules
{
    public static Outcome<NegotiationActivityTransferSnapshot> Read(string? token,
        ITransferPreviewProtector protector, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 16_384)
            return Outcome<NegotiationActivityTransferSnapshot>.Fail("NegotiationTransfer.InvalidInput");

        try
        {
            var json = protector.Unprotect(token);
            if (string.IsNullOrWhiteSpace(json))
                return Outcome<NegotiationActivityTransferSnapshot>.Fail("NegotiationTransfer.PreviewExpired");

            var snapshot = JsonSerializer.Deserialize<NegotiationActivityTransferSnapshot>(json);
            if (snapshot is null || snapshot.OrganizationId == Guid.Empty || snapshot.EstablishmentId == Guid.Empty ||
                snapshot.NegotiationId == Guid.Empty || snapshot.RequestId == Guid.Empty || snapshot.NegotiationVersion < 1 ||
                string.IsNullOrWhiteSpace(snapshot.DestinationSubject) || string.IsNullOrWhiteSpace(snapshot.FollowupResponsibleSubject) ||
                snapshot.Selection is null || snapshot.Schedule is null || snapshot.Fingerprint is null ||
                snapshot.IssuedAtUtc > nowUtc || nowUtc - snapshot.IssuedAtUtc > TimeSpan.FromMinutes(20) ||
                snapshot.Selection.Any(x => x.ActionId == Guid.Empty || x.ActionVersion < 1 || x.AppointmentVersion is < 1) ||
                snapshot.Selection.Select(x => x.ActionId).Distinct().Count() != snapshot.Selection.Count ||
                snapshot.Schedule.Any(x => x.Id == Guid.Empty || x.Version < 1) ||
                snapshot.Schedule.Select(x => x.Id).Distinct().Count() != snapshot.Schedule.Count)
                return Outcome<NegotiationActivityTransferSnapshot>.Fail("NegotiationTransfer.PreviewExpired");
            return new(snapshot);
        }
        catch (Exception ex) when (ex is JsonException or CryptographicException or ArgumentException or FormatException)
        {
            return Outcome<NegotiationActivityTransferSnapshot>.Fail("NegotiationTransfer.PreviewExpired");
        }
    }
}
