namespace RealEstate.Domain;
public sealed record OwnerDetails(string? TradeName=null,string? Phone=null,string? Email=null,StructuredAddress? Address=null);
public sealed record OwnerProfile(Guid Id,string Name,string PersonKind,string TaxId,long Version=1,IReadOnlyList<ProfileRevision>? History=null,OwnerDetails? Details=null);
public sealed record OwnerProfileInput(string Name,string PersonKind,string TaxId,OwnerDetails? Details=null);
public interface IOwnerProfileStore
{
    Task<OwnerProfile?> FindOwnerProfileAsync(AccessScope scope,Guid id,CancellationToken token);
    Task<Outcome<OwnerProfile>> UpdateOwnerProfileAsync(AccessScope scope,Guid id,long version,OwnerProfileInput input,CancellationToken token);
    Task<IReadOnlyList<OwnerProfile>> SearchOwnersAsync(AccessScope scope,string query,CancellationToken token);
    Task<Outcome<OwnerProfile>> CreateOwnerProfileAsync(AccessScope scope,OwnerProfileInput input,CancellationToken token);
}
public static class OwnerProfileRules
{
    public static Outcome<OwnerProfileInput> ValidateDetails(OwnerProfileInput input)
    {
        if(input.Details is not {} d)return new(input);
        static string? Clean(string? text)=>string.IsNullOrWhiteSpace(text)?null:text.Trim();
        d=d with{TradeName=Clean(d.TradeName),Phone=Clean(d.Phone),Email=Clean(d.Email)};
        if(d.TradeName?.Length>200||d.Phone?.Length>30||d.Email?.Length>254||
            (d.Email is not null&&(!System.Net.Mail.MailAddress.TryCreate(d.Email,out var email)||email.Address!=d.Email)))
            return Outcome<OwnerProfileInput>.Fail("Owner.ContactInvalid");
        var address=StructuredAddressRules.Validate(d.Address);
        if(!address.IsSuccess)return Outcome<OwnerProfileInput>.Fail(address.Code!);
        return new(input with{Details=d with{Address=d.Address is null?null:address.Value}});
    }
    public static Outcome<OwnerProfileInput> Validate(OwnerProfileInput input)
    {
        if(input is null||string.IsNullOrWhiteSpace(input.Name)||input.Name.Trim().Length>200||input.TaxId is null)return Outcome<OwnerProfileInput>.Fail("Owner.Invalid");
        if(input.TaxId.Any(c=>!char.IsAsciiDigit(c)&&c is not('.' or '/' or '-' or ' ')))return Outcome<OwnerProfileInput>.Fail("Owner.TaxIdInvalid");
        var digits=new string(input.TaxId.Where(char.IsAsciiDigit).ToArray());
        if(input.PersonKind is not("PF" or "PJ")||digits.Length!=(input.PersonKind=="PF"?11:14)||digits.Distinct().Count()==1)return Outcome<OwnerProfileInput>.Fail("Owner.TaxIdInvalid");
        var body=digits[..^2].Select(c=>c-'0').ToList();
        for(var round=0;round<2;round++)
        {
            var weights=input.PersonKind=="PF"?Enumerable.Range(2,body.Count).Reverse().ToArray():Enumerable.Range(0,body.Count).Select(i=>(body.Count-i-1)%8+2).ToArray();
            var sum=body.Select((d,i)=>d*weights[i]).Sum();var check=sum%11<2?0:11-sum%11;
            if(check!=digits[body.Count]-'0')return Outcome<OwnerProfileInput>.Fail("Owner.TaxIdInvalid");body.Add(check);
        }
        return ValidateDetails(input with{Name=input.Name.Trim(),TaxId=digits});
    }
}
