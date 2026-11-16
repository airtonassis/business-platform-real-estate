using System.Text.Json;
namespace RealEstate.Domain;

// Configuration for preliminary capture only. No payable, effective commission or contracted guarantee.
public sealed record CommissionAllocation(string Operation,string ParticipantKind,string? RoleLabel,decimal? Percent,decimal? Amount,string? BaseDescription);
public sealed record CommercialPolicyData(PreliminaryFees Fees,IReadOnlyList<CaptureSelection> Guarantees);
public sealed record CommercialPolicyInput(string Name,string Reason,CommercialPolicyData Data,Guid? PropertyId=null,Guid? BaseRevisionId=null);
public sealed record CommercialPolicyRevision(Guid Id,long Version,string Status,CommercialPolicyInput Input,string RequestedBy,string? ApprovedBy,DateTimeOffset CreatedAt,IReadOnlyList<ProfileRevision> History);
public sealed record PolicyBinding(Guid RevisionId,long Version);
public interface ICommercialPolicyStore
{
    Task<IReadOnlyList<CommercialPolicyRevision>> ListAsync(AccessScope scope,CancellationToken token);
    Task<CommercialPolicyRevision?> FindAsync(AccessScope scope,Guid id,CancellationToken token);
    Task<Outcome<CommercialPolicyRevision>> SaveDraftAsync(AccessScope scope,CommercialPolicyInput input,Guid? id,long version,CancellationToken token);
    Task<Outcome<CommercialPolicyRevision>> ApproveAsync(AccessScope scope,Guid id,long version,string reason,CancellationToken token);
}
public static class CommercialPolicyRules
{
    static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    public static Outcome<CommercialPolicyInput> Validate(CommercialPolicyInput? input)
    {
        if(input is null||string.IsNullOrWhiteSpace(input.Name)||input.Name.Length>200||string.IsNullOrWhiteSpace(input.Reason)||input.Reason.Length>1000||input.Data?.Fees is null||input.Data.Guarantees is null||input.PropertyId==Guid.Empty||input.BaseRevisionId==Guid.Empty||input.PropertyId.HasValue&&!input.BaseRevisionId.HasValue)return Outcome<CommercialPolicyInput>.Fail("Policy.InvalidInput");
        if(input.Data.Guarantees.Any(s=>s is null||s.GroupId!="guarantees"||s.PhotoId is not null||s.SharedOverride is not null||s.Quantity is not null||s.Condition is not null||s.Inclusion is not null))return Outcome<CommercialPolicyInput>.Fail("Policy.InvalidInput");
        var capture=CaptureRules.Validate(new(){Fees=input.Data.Fees,Selections=input.Data.Guarantees.ToList()});
        if(!capture.IsSuccess)return Outcome<CommercialPolicyInput>.Fail(capture.Code!);
        if(new[]{input.Data.Fees.SalePercent,input.Data.Fees.RentalPercent,input.Data.Fees.AdministrationPercent}.Any(p=>p>100))return Outcome<CommercialPolicyInput>.Fail("Policy.PercentInvalid");
        return new(input with{Name=input.Name.Trim(),Reason=input.Reason.Trim(),Data=new(input.Data.Fees,capture.Value!.Selections.Where(s=>s.Selected).ToArray())});
    }
    public static bool AllocationsValid(IReadOnlyList<CommissionAllocation>? items)=>items is not null&&items.Count<=30&&items.All(x=>x is not null&&x.Operation is "sale" or "rental" or "administration"&&x.ParticipantKind is "internal" or "external" or "organization"&& !string.IsNullOrWhiteSpace(x.RoleLabel)&&x.RoleLabel.Length<=200&&x.BaseDescription?.Length is not >500&&(x.Percent is null||x.Percent>=0&&x.Percent<=100&&decimal.Round(x.Percent.Value,4)==x.Percent)&&(x.Amount is null||x.Amount>=0&&x.Amount<=999999999999.99m&&decimal.Round(x.Amount.Value,2)==x.Amount)&&!(x.Percent is not null&&x.Amount is not null))&&items.GroupBy(x=>x.Operation).All(g=>g.Sum(x=>x.Percent??0)<=100);
    public static string Signature(CommercialPolicyData data)=>JsonSerializer.Serialize(new{
        data.Fees,
        Guarantees=data.Guarantees.Where(s=>s.Selected&&s.GroupId=="guarantees").OrderBy(s=>s.OptionId,StringComparer.Ordinal).Select(s=>new{s.OptionId,s.Label,s.Detail})
    },Json);
    public static bool Matches(CommercialPolicyData policy,CaptureData capture)=>Signature(policy)==Signature(new(capture.Fees,capture.Selections));
    public static CaptureData Apply(CommercialPolicyRevision revision,CaptureData? current)
    {
        // A detached copy avoids mutating the caller or overwriting any unrelated/hidden category.
        var capture=JsonSerializer.Deserialize<CaptureData>(JsonSerializer.Serialize(current??new(),Json),Json)!;
        capture.Fees=JsonSerializer.Deserialize<PreliminaryFees>(JsonSerializer.Serialize(revision.Input.Data.Fees,Json),Json)!;
        capture.Selections.RemoveAll(s=>s.GroupId=="guarantees");
        capture.Selections.AddRange(JsonSerializer.Deserialize<List<CaptureSelection>>(JsonSerializer.Serialize(revision.Input.Data.Guarantees,Json),Json)!);
        capture.Policy=new(revision.Id,revision.Version);return capture;
    }
}
