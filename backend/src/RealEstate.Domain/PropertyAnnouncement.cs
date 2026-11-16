namespace RealEstate.Domain;

// Commercial state is separate from the shared physical property. Price is never inferred.
[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record AnnouncementInput(OperationValue? Values=null,decimal? PropertyTax=null,string? PropertyTaxPeriod=null,
 decimal? CondominiumFee=null,string? CondominiumPeriod=null,bool? Financing=null,bool? AcceptsExchange=null,string? ExchangeNotes=null);
public sealed record PropertyAnnouncement(Guid Id,Guid OrganizationId,Guid PropertyId,Guid EstablishmentId,string Purpose,
 string? CommercialCode,string Status,long Version,bool LegacyAnchor,Guid? SourceAnnouncementId,AnnouncementInput Data,
 string CreatedBy,DateTimeOffset CreatedAt);
public sealed record CreateAnnouncement(string Purpose,Guid RequestId,Guid? SourceAnnouncementId=null,long? SourceVersion=null,AnnouncementInput? Data=null);
[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateAnnouncement(long Version,AnnouncementInput Data);
public interface IAnnouncementStore
{
 Task<IReadOnlyList<PropertyAnnouncement>> ListAnnouncementsAsync(AccessScope scope,Guid property,CancellationToken token);
 Task<Outcome<PropertyAnnouncement>> CreateAnnouncementAsync(AccessScope scope,Guid property,CreateAnnouncement command,CancellationToken token);
 Task<Outcome<PropertyAnnouncement>> UpdateAnnouncementAsync(AccessScope scope,Guid id,long version,AnnouncementInput? input,bool close,CancellationToken token);
}
public static class AnnouncementRules
{
 public static AnnouncementInput ReviewFrom(AnnouncementInput original)=>new(PropertyTax:original.PropertyTax,PropertyTaxPeriod:original.PropertyTaxPeriod,
  CondominiumFee:original.CondominiumFee,CondominiumPeriod:original.CondominiumPeriod);
 public static bool Exclusive(string? purpose)=>purpose is "Venda" or "Locação";
 public static Outcome<AnnouncementInput> Validate(AnnouncementInput? data,string purpose)
 {
  if(data is null)return Outcome<AnnouncementInput>.Fail("Property.InvalidInput");
  if(purpose=="Locação"&&(data.Financing is not null||data.AcceptsExchange is not null||data.ExchangeNotes is not null))return Outcome<AnnouncementInput>.Fail("Announcement.Inapplicable");
  var c=new CaptureData();if(purpose=="Venda")c.Values.Sale=data.Values??new();else c.Values.Rental=data.Values??new();
  c.Values.PropertyTax=data.PropertyTax;c.Values.PropertyTaxPeriod=data.PropertyTaxPeriod;
  c.Values.CondominiumFee=data.CondominiumFee;c.Values.CondominiumPeriod=data.CondominiumPeriod;c.Values.ExchangeNotes=data.ExchangeNotes;
  var valid=CaptureRules.Validate(c);return valid.IsSuccess?new(data):Outcome<AnnouncementInput>.Fail(valid.Code!);
 }
}
