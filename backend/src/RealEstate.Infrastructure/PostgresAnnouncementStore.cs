using System.Text.Json;
using Npgsql;
using RealEstate.Domain;
namespace RealEstate.Infrastructure;

public sealed partial class PostgresPropertyStore:IAnnouncementStore
{
 private const string AnnouncementColumns="a.id,a.organization_id,a.property_id,a.establishment_id,a.purpose,a.commercial_code,a.status,a.version,a.legacy_anchor,a.source_announcement_id,a.commercial_data,a.created_by,a.created_at";
 private static PropertyAnnouncement ReadAnnouncement(NpgsqlDataReader r)
  =>new(r.GetGuid(0),r.GetGuid(1),r.GetGuid(2),r.GetGuid(3),r.GetString(4),Text(r,5),r.GetString(6),r.GetInt64(7),r.GetBoolean(8),r.IsDBNull(9)?null:r.GetGuid(9),JsonSerializer.Deserialize<AnnouncementInput>(r.GetString(10),CaptureJson)??new(),r.GetString(11),r.GetFieldValue<DateTimeOffset>(12));
 private static async Task<PropertyAnnouncement?> Announcement(NpgsqlConnection c,NpgsqlTransaction tx,Guid org,Guid id,bool lockRow,CancellationToken ct)
 {
  await using var cmd=new NpgsqlCommand($"SELECT {AnnouncementColumns} FROM real_estate.property_announcements a WHERE a.organization_id=@org AND a.id=@id"+(lockRow?" FOR UPDATE":""),c,tx);
  cmd.Parameters.AddWithValue("org",org);cmd.Parameters.AddWithValue("id",id);
  await using var r=await cmd.ExecuteReaderAsync(ct);return await r.ReadAsync(ct)?ReadAnnouncement(r):null;
 }
 public async Task<IReadOnlyList<PropertyAnnouncement>> ListAnnouncementsAsync(AccessScope s,Guid property,CancellationToken ct)
 {
  await using var c=await dataSource.OpenConnectionAsync(ct);await using var tx=await Begin(c,s,ct);
  // Initial announcement is a compatibility projection of existing commercial data, not a second writable truth.
  var columns=AnnouncementColumns.Replace("a.commercial_data", "CASE WHEN a.legacy_anchor THEN jsonb_build_object('values',p.capture->'values'->CASE a.purpose WHEN 'Venda' THEN 'sale' ELSE 'rental' END,'propertyTax',p.capture->'values'->'propertyTax','propertyTaxPeriod',p.capture->'values'->'propertyTaxPeriod','condominiumFee',p.capture->'values'->'condominiumFee','condominiumPeriod',p.capture->'values'->'condominiumPeriod','financing',CASE WHEN a.purpose='Venda' THEN p.capture->'values'->'financing' END,'acceptsExchange',CASE WHEN a.purpose='Venda' THEN p.capture->'values'->'acceptsExchange' END,'exchangeNotes',CASE WHEN a.purpose='Venda' THEN p.capture->'values'->'exchangeNotes' END) ELSE a.commercial_data END");
  await using var cmd=new NpgsqlCommand($"SELECT {columns} FROM real_estate.property_announcements a JOIN real_estate.properties p ON p.organization_id=a.organization_id AND p.id=a.property_id WHERE a.organization_id=@org AND a.property_id=@id ORDER BY a.created_at,a.id",c,tx);
  cmd.Parameters.AddWithValue("org",s.OrganizationId);cmd.Parameters.AddWithValue("id",property);
  var items=new List<PropertyAnnouncement>();await using(var r=await cmd.ExecuteReaderAsync(ct))while(await r.ReadAsync(ct))items.Add(ReadAnnouncement(r));
  await tx.CommitAsync(ct);return items;
 }
 private static string AnnouncementError(PostgresException e)=>e.ConstraintName switch{
  "announcement_active_modality"=>"Announcement.ActiveExists","property_purpose_immutable" or "announcement_identity_immutable"=>"Announcement.PurposeImmutable",
  "announcement_closed"=>"Announcement.Closed",_=>e.SqlState=="23505"?"Announcement.Conflict":e.SqlState=="42501"?"Access.Forbidden":"Property.InvalidInput"};
 public async Task<Outcome<PropertyAnnouncement>> CreateAnnouncementAsync(AccessScope s,Guid property,CreateAnnouncement command,CancellationToken ct)
 {
  await using var c=await dataSource.OpenConnectionAsync(ct);await using var tx=await Begin(c,s,ct);
  try{
   // Serialize sibling creations and physical identity inspection in a consistent lock order.
   await using(var p=new NpgsqlCommand("SELECT establishment_id,purpose FROM real_estate.properties WHERE organization_id=@org AND id=@id FOR UPDATE",c,tx)){
    p.Parameters.AddWithValue("org",s.OrganizationId);p.Parameters.AddWithValue("id",property);
    await using var r=await p.ExecuteReaderAsync(ct);
    if(!await r.ReadAsync(ct))return Outcome<PropertyAnnouncement>.Fail("Resource.NotFound");
    if(r.IsDBNull(0)||s.ActiveEstablishmentId!=r.GetGuid(0))return Outcome<PropertyAnnouncement>.Fail("Access.Forbidden");
    if(r.IsDBNull(1))return Outcome<PropertyAnnouncement>.Fail("Announcement.InitialPurposeRequired");
   }
   await using(var retry=new NpgsqlCommand($"SELECT {AnnouncementColumns} FROM real_estate.property_announcements a WHERE a.organization_id=@org AND a.request_id=@request",c,tx)){
    retry.Parameters.AddWithValue("org",s.OrganizationId);retry.Parameters.AddWithValue("request",command.RequestId);
    await using var r=await retry.ExecuteReaderAsync(ct);if(await r.ReadAsync(ct)){
     var old=ReadAnnouncement(r);return old.PropertyId==property&&old.Purpose==command.Purpose&&old.SourceAnnouncementId==command.SourceAnnouncementId&&JsonSerializer.Serialize(old.Data,CaptureJson)==JsonSerializer.Serialize(command.Data??new(),CaptureJson)?new(old):Outcome<PropertyAnnouncement>.Fail("Announcement.Conflict");
    }
   }
   if(command.SourceAnnouncementId is {} source){
    var original=await Announcement(c,tx,s.OrganizationId,source,true,ct);
    if(original is null||original.PropertyId!=property)return Outcome<PropertyAnnouncement>.Fail("Resource.NotFound");
    if(command.SourceVersion!=original.Version)return Outcome<PropertyAnnouncement>.Fail("Property.VersionConflict");
    if(original.Purpose==command.Purpose)return Outcome<PropertyAnnouncement>.Fail("Property.PurposeInvalid");
   }
   var valid=AnnouncementRules.Validate(command.Data??new(),command.Purpose);if(!valid.IsSuccess)return Outcome<PropertyAnnouncement>.Fail(valid.Code!);
   await using var cmd=new NpgsqlCommand("INSERT INTO real_estate.property_announcements AS a(organization_id,id,property_id,establishment_id,purpose,source_announcement_id,request_id,commercial_data,created_by,updated_by) VALUES(@org,@id,@property,@unit,@purpose,@source,@request,@data,@actor,@actor) RETURNING "+AnnouncementColumns,c,tx);
   cmd.Parameters.AddWithValue("data",NpgsqlTypes.NpgsqlDbType.Jsonb,JsonSerializer.Serialize(valid.Value!,CaptureJson));
   cmd.Parameters.AddWithValue("org",s.OrganizationId);cmd.Parameters.AddWithValue("id",Guid.NewGuid());cmd.Parameters.AddWithValue("property",property);cmd.Parameters.AddWithValue("unit",s.ActiveEstablishmentId!.Value);
   cmd.Parameters.AddWithValue("purpose",command.Purpose);cmd.Parameters.AddWithValue("source",NpgsqlTypes.NpgsqlDbType.Uuid,(object?)command.SourceAnnouncementId??DBNull.Value);cmd.Parameters.AddWithValue("request",command.RequestId);cmd.Parameters.AddWithValue("actor",s.Subject);
   PropertyAnnouncement item;await using(var r=await cmd.ExecuteReaderAsync(ct)){await r.ReadAsync(ct);item=ReadAnnouncement(r);}await tx.CommitAsync(ct);return new(item);
  }catch(PostgresException e) when(e.SqlState is "23505" or "23514" or "42501"){return Outcome<PropertyAnnouncement>.Fail(AnnouncementError(e));}
 }
 public async Task<Outcome<PropertyAnnouncement>> UpdateAnnouncementAsync(AccessScope s,Guid id,long version,AnnouncementInput? input,bool close,CancellationToken ct)
 {
  await using var c=await dataSource.OpenConnectionAsync(ct);await using var tx=await Begin(c,s,ct);
  try{
   // Parent lock first, matching duplicate / physical saves.
   await using(var parent=new NpgsqlCommand("SELECT p.id FROM real_estate.properties p JOIN real_estate.property_announcements a ON a.organization_id=p.organization_id AND a.property_id=p.id WHERE a.organization_id=@org AND a.id=@id FOR UPDATE OF p",c,tx)){
    parent.Parameters.AddWithValue("org",s.OrganizationId);parent.Parameters.AddWithValue("id",id);if(await parent.ExecuteScalarAsync(ct) is null)return Outcome<PropertyAnnouncement>.Fail("Resource.NotFound");
   }
   var current=await Announcement(c,tx,s.OrganizationId,id,true,ct);if(current is null)return Outcome<PropertyAnnouncement>.Fail("Resource.NotFound");
   if(current.EstablishmentId!=s.ActiveEstablishmentId)return Outcome<PropertyAnnouncement>.Fail("Access.Forbidden");
   if(current.Version!=version)return Outcome<PropertyAnnouncement>.Fail("Property.VersionConflict");
   if(current.Status=="Closed")return Outcome<PropertyAnnouncement>.Fail("Announcement.Closed");
   if(!close&&current.LegacyAnchor)return Outcome<PropertyAnnouncement>.Fail("Announcement.EditOriginal");
   if(!close){var valid=AnnouncementRules.Validate(input,current.Purpose);if(!valid.IsSuccess)return Outcome<PropertyAnnouncement>.Fail(valid.Code!);}
   await using var cmd=new NpgsqlCommand("UPDATE real_estate.property_announcements AS a SET commercial_data=CASE WHEN @close THEN commercial_data ELSE @data END,status=CASE WHEN @close THEN 'Closed' ELSE status END,updated_by=@actor WHERE organization_id=@org AND id=@id RETURNING "+AnnouncementColumns,c,tx);
   cmd.Parameters.AddWithValue("org",s.OrganizationId);cmd.Parameters.AddWithValue("id",id);cmd.Parameters.AddWithValue("close",close);cmd.Parameters.AddWithValue("actor",s.Subject);cmd.Parameters.AddWithValue("data",NpgsqlTypes.NpgsqlDbType.Jsonb,JsonSerializer.Serialize(input??new(),CaptureJson));
   PropertyAnnouncement item;await using(var r=await cmd.ExecuteReaderAsync(ct)){await r.ReadAsync(ct);item=ReadAnnouncement(r);}await tx.CommitAsync(ct);return new(item);
  }catch(PostgresException e) when(e.SqlState is "23505" or "23514" or "42501"){return Outcome<PropertyAnnouncement>.Fail(AnnouncementError(e));}
 }
}
