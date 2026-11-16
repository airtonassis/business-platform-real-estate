namespace RealEstate.Domain;

public sealed record CustomerInput(string Name,string PersonType,string? Phone,string? Email,string Origin,string Interest,string ResponsibleSubject,string? Notes=null,string? Document=null,string? ResponsibleName=null);
public sealed record Customer(Guid Id,Guid EstablishmentId,CustomerInput Data,long Version);
public sealed record CustomerPage(IReadOnlyList<Customer> Items,int Page,int Total);
public sealed record CustomerContact(Guid Id,DateTimeOffset At,string Kind,string Description,string Actor,string ResponsibleSubject);
public sealed record CustomerResponsibility(DateTimeOffset At,string Actor,string PreviousSubject,string NewSubject);
public sealed record CustomerFile(Customer Customer,IReadOnlyList<CustomerContact> Contacts,IReadOnlyList<CustomerResponsibility> ResponsibilityHistory);
public sealed record AppointmentCustomerReference(Guid Id,Guid EstablishmentId,string Name,string ResponsibleSubject);
public sealed record ContactInput(string Kind,string Description);
public interface ICustomerDirectory
{
    Task<Outcome<CaptureResponsibleDirectory>> ReadAsync(AccessScope scope,Guid unit,string? subject=null,CancellationToken token=default);
}
public interface ICustomerStore
{
    Task<CustomerPage> SearchAsync(AccessScope scope,string query,int page,CancellationToken token);
    Task<CustomerFile?> FindAsync(AccessScope scope,Guid id,CancellationToken token);
    Task<AppointmentCustomerReference?> FindAppointmentReferenceAsync(AccessScope scope,Guid id,CancellationToken token);
    Task<Outcome<Customer>> SaveAsync(AccessScope scope,CustomerInput input,Guid? id,long version,bool acknowledgeDuplicates,CancellationToken token);
    Task<Outcome<CustomerContact>> AddContactAsync(AccessScope scope,Guid id,ContactInput input,CancellationToken token);
}
public static class CustomerRules
{
    public static readonly string[] Origins=["Indicação","Telefone","WhatsApp","Site","Redes sociais","Presencial","Outros"];
    public static readonly string[] Interests=["Compra","Locação","Temporada","Captação de imóveis"];
    public static readonly string[] ContactKinds=["Telefone","WhatsApp","E-mail","Presencial","Outros"];
    public static string? Clean(string? text)=>string.IsNullOrWhiteSpace(text)?null:text.Trim();
    public static string Digits(string? text)=>new((text??"").Where(char.IsAsciiDigit).ToArray());
    public static Outcome<CustomerInput> Validate(CustomerInput p)
    {
        if(p is null||Clean(p.Name) is not {} name||name.Length>200)return Outcome<CustomerInput>.Fail("Customer.NameRequired");
        if(p.PersonType is not("Física" or "Jurídica"))return Outcome<CustomerInput>.Fail("Customer.PersonTypeInvalid");
        var phone=Clean(p.Phone);var email=Clean(p.Email)?.ToLowerInvariant();
        if(phone is null&&email is null)return Outcome<CustomerInput>.Fail("Customer.ContactRequired");
        if(phone is not null&&(phone.Length>30||phone.Any(c=>!char.IsAsciiDigit(c)&&!" +()-. ".Contains(c))||Digits(phone).Length is <8 or >15))return Outcome<CustomerInput>.Fail("Customer.PhoneInvalid");
        if(email is not null&&(email.Length>254||!System.Net.Mail.MailAddress.TryCreate(email,out var parsed)||parsed.Address!=email))return Outcome<CustomerInput>.Fail("Customer.EmailInvalid");
        if(!Origins.Contains(p.Origin)||!Interests.Contains(p.Interest))return Outcome<CustomerInput>.Fail("Customer.ChoiceInvalid");
        if(Clean(p.ResponsibleSubject) is not {} responsible||responsible.Length>255)return Outcome<CustomerInput>.Fail("Customer.ResponsibleRequired");
        if(p.Notes?.Length>4000)return Outcome<CustomerInput>.Fail("Customer.NotesTooLong");
        var document=Clean(p.Document);
        if(document is not null){var valid=OwnerProfileRules.Validate(new(name,p.PersonType=="Física"?"PF":"PJ",document));if(!valid.IsSuccess)return Outcome<CustomerInput>.Fail("Customer.DocumentInvalid");document=valid.Value!.TaxId;}
        return new(p with{Name=name,Phone=phone is null?null:Digits(phone),Email=email,ResponsibleSubject=responsible,Notes=Clean(p.Notes),Document=document});
    }
}
