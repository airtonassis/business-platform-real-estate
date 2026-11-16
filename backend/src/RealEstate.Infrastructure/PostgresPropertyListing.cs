using Npgsql;
using RealEstate.Domain;
namespace RealEstate.Infrastructure;
public sealed partial class PostgresPropertyStore:IPropertyListingStore
{
    private static readonly IReadOnlyDictionary<string,string> ListingColumns=new Dictionary<string,string>{
        ["updated"]="updated_at",["code"]="coalesce(commercial_code,code)",["title"]="title",["municipality"]="address_municipality",["state"]="address_uf",
        ["neighborhood"]="address_neighborhood",["category"]="category",["type"]="property_type",["purpose"]="purpose",["price"]="price",["status"]="status"};
    public async Task<PropertyListPage> ListPageAsync(AccessScope scope,PropertyListQuery query,CancellationToken token)
    {
        if(!query.IsValid)throw new ArgumentException("Invalid internal listing query");
        await using var c=await dataSource.OpenConnectionAsync(token);await using var tx=await Begin(c,scope,token);
        var clauses=new List<string>{"organization_id=@org"};if(query.Commercial)clauses.Add("status='Available'");var args=new Dictionary<string,object>{{"org",scope.OrganizationId}};
        static string Pattern(string value)=>"%"+value.Trim().Replace("\\","\\\\").Replace("%","\\%").Replace("_","\\_")+"%";
        static string AnnouncementCode(string parameter)=>"EXISTS(SELECT 1 FROM real_estate.property_announcements a WHERE a.organization_id=real_estate.properties.organization_id AND a.property_id=real_estate.properties.id AND a.commercial_code ILIKE @"+parameter+" ESCAPE E'\\\\')";
        foreach(var (key,value) in query.TextFilters()){
            if(key=="status"){clauses.Add("status=@status");args["status"]=value;continue;}
            if(query.Commercial&&key=="purpose")clauses.Add("EXISTS(SELECT 1 FROM real_estate.property_announcements a WHERE a.organization_id=real_estate.properties.organization_id AND a.property_id=real_estate.properties.id AND a.status='Draft' AND a.purpose ILIKE @purpose ESCAPE E'\\\\')");
            else clauses.Add("("+ListingColumns[key]+" ILIKE @"+key+" ESCAPE E'\\\\'"+(key=="code"?" OR code ILIKE @code ESCAPE E'\\\\' OR "+AnnouncementCode("code"):"")+")");args[key]=Pattern(value);
        }
        if(!string.IsNullOrWhiteSpace(query.Search)){
            clauses.Add("("+string.Join(" OR ",new[]{"commercial_code","code","title","address_municipality","address_uf","address_neighborhood","category","property_type","purpose","status"}.Select(col=>col+" ILIKE @search ESCAPE E'\\\\'"))+" OR "+AnnouncementCode("search")+")");args["search"]=Pattern(query.Search);
        }
        // Commercial values follow the active announcement; legacy Price is not an amount by operation.
        const string operation="CASE a.purpose WHEN 'Venda' THEN 'sale' WHEN 'Locação' THEN 'rental' WHEN 'Temporada' THEN 'seasonal' END";
        var values="(CASE WHEN a.legacy_anchor THEN real_estate.properties.capture->'values'->("+operation+") ELSE a.commercial_data->'values' END)";
        var amount="(CASE WHEN ("+values+"->>'differentAdvertised')::boolean IS TRUE THEN "+values+"->>'advertised' ELSE "+values+"->>'authorized' END)::numeric";
        var announcements=" FROM real_estate.property_announcements a WHERE a.organization_id=real_estate.properties.organization_id AND a.property_id=real_estate.properties.id AND a.status='Draft'"+(string.IsNullOrWhiteSpace(query.Purpose)?"":" AND a.purpose ILIKE @purpose ESCAPE E'\\\\'");
        if(query.Commercial&&(query.MinPrice.HasValue||query.MaxPrice.HasValue))clauses.Add("EXISTS(SELECT 1"+announcements+(query.MinPrice.HasValue?" AND "+amount+">=@min":"")+(query.MaxPrice.HasValue?" AND "+amount+"<=@max":"")+")");
        if(query.MinPrice is {} min){if(!query.Commercial)clauses.Add("price>=@min");args["min"]=min;}
        if(query.MaxPrice is {} max){if(!query.Commercial)clauses.Add("price<=@max");args["max"]=max;}
        if(query.MinBedrooms is {} bedrooms){clauses.Add("(capture->'rooms'->>'bedrooms')::int>=@bedrooms");args["bedrooms"]=bedrooms;}
        if(query.MinParking is {} parking){clauses.Add("(capture->'rooms'->>'parking')::int>=@parking");args["parking"]=parking;}
        if(query.MinArea.HasValue||query.MaxArea.HasValue){
            // Kind and unit are data parameters, never SQL identifiers. No comparison across units.
            clauses.Add("capture->'areas'->>'unit'=@areaUnit");args["areaUnit"]=query.AreaUnit!;args["areaKind"]=query.AreaKind!;
            if(query.AreaUnit=="alqueire"){clauses.Add("capture->'areas'->>'regionalVariant'=@variant");args["variant"]=query.RegionalVariant!;}
            if(query.MinArea is {} minArea){clauses.Add("(capture->'areas'->>@areaKind)::numeric>=@minArea");args["minArea"]=minArea;}
            if(query.MaxArea is {} maxArea){clauses.Add("(capture->'areas'->>@areaKind)::numeric<=@maxArea");args["maxArea"]=maxArea;}
        }
        if(query.FeatureGroup is {} group){
            args["featureGroup"]=group;args["featureOption"]=query.FeatureOption!;
            // Explicit local selections and effective inherited condominium selections use the existing rules.
            clauses.Add("""
                (EXISTS(SELECT 1 FROM jsonb_array_elements(coalesce(capture->'selections','[]'::jsonb)) s
                  WHERE s->>'groupId'=@featureGroup AND s->>'optionId'=@featureOption AND (
                    ((s->>'selected')::boolean IS TRUE AND (coalesce((capture->'condominium'->>'inherit')::boolean,false)=false OR @featureGroup NOT LIKE 'collective-%'))
                    OR (@featureGroup LIKE 'collective-%' AND (capture->'condominium'->>'inherit')::boolean IS TRUE
                      AND (s->>'sharedOverride'='true' OR (s->>'sharedOverride' IS DISTINCT FROM 'false' AND (s->>'selected')::boolean IS TRUE)))))
                 OR (@featureGroup LIKE 'collective-%' AND (capture->'condominium'->>'inherit')::boolean IS TRUE
                   AND EXISTS(SELECT 1 FROM real_estate.condominiums co,jsonb_array_elements(co.characteristics) s
                     WHERE co.organization_id=real_estate.properties.organization_id AND co.id=(capture->'condominium'->>'id')::uuid
                     AND s->>'groupId'=@featureGroup AND s->>'optionId'=@featureOption AND (s->>'selected')::boolean IS TRUE
                     AND NOT EXISTS(SELECT 1 FROM jsonb_array_elements(coalesce(capture->'selections','[]'::jsonb)) local
                       WHERE local->>'groupId'=@featureGroup AND local->>'optionId'=@featureOption AND local->>'sharedOverride'='false'))))
                """);
        }
        if(query.EstablishmentId is {} unit){clauses.Add("establishment_id=@unit");args["unit"]=unit;}
        var where=" WHERE "+string.Join(" AND ",clauses);
        void Parameters(NpgsqlCommand cmd){foreach(var (key,value) in args)cmd.Parameters.AddWithValue(key,value);}
        long total;
        await using(var count=new NpgsqlCommand("SELECT count(*) FROM real_estate.properties"+where,c,tx)){Parameters(count);total=(long)(await count.ExecuteScalarAsync(token))!;}
        var rows=new List<PropertyV2>();
        // Only code-owned identifiers and direction enter SQL; all supplied values are parameters.
        var order=(query.Commercial&&query.Sort=="price"?"(SELECT min("+amount+")"+announcements+")":ListingColumns[query.Sort])+(query.Descending?" DESC":" ASC")+" NULLS LAST,id ASC";
        await using(var cmd=new NpgsqlCommand("SELECT "+V2Columns+" FROM real_estate.properties"+where+" ORDER BY "+order+" LIMIT @take OFFSET @skip",c,tx)){
            Parameters(cmd);cmd.Parameters.AddWithValue("take",PropertyListQuery.PageSize);cmd.Parameters.AddWithValue("skip",(query.Page-1)*PropertyListQuery.PageSize);
            await using var r=await cmd.ExecuteReaderAsync(token);while(await r.ReadAsync(token))rows.Add(ReadV2(r));
        }
        var active=new Dictionary<Guid,List<ListingAnnouncement>>();
        if(rows.Count>0){
            await using var cmd=new NpgsqlCommand("SELECT property_id,id,purpose,commercial_code,commercial_data->'values',legacy_anchor,(commercial_data->>'condominiumFee')::numeric,commercial_data->>'condominiumPeriod',(commercial_data->>'propertyTax')::numeric,commercial_data->>'propertyTaxPeriod' FROM real_estate.property_announcements WHERE organization_id=@org AND property_id=ANY(@ids) AND status='Draft' ORDER BY created_at,id",c,tx);
            cmd.Parameters.AddWithValue("org",scope.OrganizationId);cmd.Parameters.AddWithValue("ids",rows.Select(p=>p.Id).ToArray());
            await using var r=await cmd.ExecuteReaderAsync(token);while(await r.ReadAsync(token)){
                var id=r.GetGuid(0);if(!active.TryGetValue(id,out var items))active[id]=items=[];
                items.Add(new(r.GetGuid(1),r.GetString(2),Text(r,3),r.IsDBNull(4)?null:System.Text.Json.JsonSerializer.Deserialize<OperationValue>(r.GetString(4),CaptureJson),r.GetBoolean(5),
                    r.IsDBNull(6)?null:r.GetDecimal(6),Text(r,7),r.IsDBNull(8)?null:r.GetDecimal(8),Text(r,9)));
            }
        }
        await tx.CommitAsync(token);return new(rows,total,scope.Establishments??[],active.ToDictionary(x=>x.Key,x=>(IReadOnlyList<ListingAnnouncement>)x.Value));
    }
}
