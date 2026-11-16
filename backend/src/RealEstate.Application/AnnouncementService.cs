using RealEstate.Domain;
namespace RealEstate.Application;

public sealed class AnnouncementService(ICoreAccess access,IPropertyStoreV2 properties,IAnnouncementStore store)
{
 public async Task<Outcome<IReadOnlyList<PropertyAnnouncement>>> ListAsync(Guid property,CancellationToken ct=default)
 {
  var grant=await access.RequireAsync("properties","PROPERTY_READ",ct);
  if(!grant.IsSuccess)return Outcome<IReadOnlyList<PropertyAnnouncement>>.Fail(grant.Code!);
  if(await properties.FindV2Async(grant.Value!,property,ct) is null)return Outcome<IReadOnlyList<PropertyAnnouncement>>.Fail("Resource.NotFound");
  return new(await store.ListAnnouncementsAsync(grant.Value!,property,ct));
 }
 public async Task<Outcome<PropertyAnnouncement>> CreateAsync(Guid property,CreateAnnouncement command,CancellationToken ct=default)
 {
  var read=await access.RequireAsync("properties","PROPERTY_READ",ct);
  if(!read.IsSuccess)return Outcome<PropertyAnnouncement>.Fail(read.Code!);
  var grant=await access.RequireAsync("properties","PROPERTY_CREATE",ct);
  if(!grant.IsSuccess)return Outcome<PropertyAnnouncement>.Fail(grant.Code!);
  var s=grant.Value!;
  if(s.OrganizationId!=read.Value!.OrganizationId||s.Subject!=read.Value.Subject)return Outcome<PropertyAnnouncement>.Fail("Access.Forbidden");
  var p=await properties.FindV2Async(read.Value,property,ct);
  if(p is null)return Outcome<PropertyAnnouncement>.Fail("Resource.NotFound");
  if(s.ActiveEstablishmentId is null||p.Data.EstablishmentId!=s.ActiveEstablishmentId||s.Establishments?.Any(e=>e.Id==p.Data.EstablishmentId)!=true)return Outcome<PropertyAnnouncement>.Fail("Access.Forbidden");
  if(p.Data.Purpose is null)return Outcome<PropertyAnnouncement>.Fail("Announcement.InitialPurposeRequired");
  if(!AnnouncementRules.Exclusive(command.Purpose)||command.RequestId==Guid.Empty)return Outcome<PropertyAnnouncement>.Fail("Property.PurposeInvalid");
  var data=AnnouncementRules.Validate(command.Data??new(),command.Purpose);
  if(!data.IsSuccess)return Outcome<PropertyAnnouncement>.Fail(data.Code!);
  if(command.SourceAnnouncementId is null&&AnnouncementRules.Exclusive(p.Data.Purpose)&&command.Purpose!=p.Data.Purpose){
   var original=(await store.ListAnnouncementsAsync(s,property,ct)).SingleOrDefault(a=>a.LegacyAnchor);
   if(original is null)return Outcome<PropertyAnnouncement>.Fail("Resource.NotFound");
   command=command with{SourceAnnouncementId=original.Id,SourceVersion=original.Version};
  }
  return await store.CreateAnnouncementAsync(s,property,command,ct);
 }
 public async Task<Outcome<PropertyAnnouncement>> UpdateAsync(Guid id,long version,AnnouncementInput? input,bool close=false,CancellationToken ct=default)
 {
  var grant=await access.RequireAsync("properties","PROPERTY_UPDATE",ct);
  if(!grant.IsSuccess)return Outcome<PropertyAnnouncement>.Fail(grant.Code!);
  if(version<1)return Outcome<PropertyAnnouncement>.Fail("Property.VersionRequired");
  return await store.UpdateAnnouncementAsync(grant.Value!,id,version,input,close,ct);
 }
}
