using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication;
using RealEstate.Domain;
namespace RealEstate.Web;

public sealed class CoreTeamDirectory(HttpClient client,IHttpContextAccessor accessor):ITeamDirectory
{
    public async Task<Outcome<TeamDirectory>> ReadAsync(AccessScope scope,Guid? id=null,CancellationToken token=default)
    {
        var http=accessor.HttpContext;
        if(http?.User.Identity?.IsAuthenticated!=true)return Outcome<TeamDirectory>.Fail("Authentication.Required");
        if(!scope.LegacyManager||scope.OrganizationId==Guid.Empty||scope.ActiveEstablishmentId is not {} unit||scope.Establishments?.Any(e=>e.Id==unit)!=true)return Outcome<TeamDirectory>.Fail("Access.Forbidden");
        var bearer=http.Request.Path.StartsWithSegments("/api")?http.Request.Headers.Authorization.ToString():"";
        var credential=bearer.StartsWith("Bearer ",StringComparison.OrdinalIgnoreCase)?bearer[7..]:await http.GetTokenAsync("access_token");
        if(string.IsNullOrWhiteSpace(credential))return Outcome<TeamDirectory>.Fail("Authentication.Required");
        try{
            var path=$"api/v1/enterprise-directory/team?establishmentId={unit:D}&productKey=real-estate"+(id.HasValue?$"&id={id:D}":"");
            using var request=new HttpRequestMessage(HttpMethod.Get,path);request.Headers.Authorization=new("Bearer",credential);
            using var response=await client.SendAsync(request,token);
            if(!response.IsSuccessStatusCode)return Outcome<TeamDirectory>.Fail(response.StatusCode switch{HttpStatusCode.Unauthorized=>"Authentication.Required",HttpStatusCode.Forbidden=>"Access.Forbidden",HttpStatusCode.NotFound=>"Team.ContractUnavailable",_=>"Dependency.Unavailable"});
            var data=await response.Content.ReadFromJsonAsync<TeamDirectory>(token);
            if(data is null||data.OrganizationId!=scope.OrganizationId||data.EstablishmentId!=unit||data.ProductKey!="real-estate"||data.Items is null||data.Items.Select(p=>p?.Id).Distinct().Count()!=data.Items.Count||data.Items.Any(p=>p is null||p.Id==Guid.Empty||id.HasValue&&p.Id!=id||string.IsNullOrWhiteSpace(p.DisplayName)||string.IsNullOrWhiteSpace(p.CorporateLogin)||p.ProfileId==Guid.Empty||p.Permissions is null||p.Permissions.Any(g=>g is null||g.ProductKey!="real-estate"||string.IsNullOrWhiteSpace(g.ResourceKey)||string.IsNullOrWhiteSpace(g.PermissionKey))||p.Establishments is null||!p.Establishments.Any(e=>e.Id==unit)||p.Establishments.Any(e=>!e.Selected||!e.Available||!scope.Establishments!.Any(s=>s.Id==e.Id))))return Outcome<TeamDirectory>.Fail("Dependency.InvalidResponse");
            return new(data);
        }catch(System.Text.Json.JsonException){return Outcome<TeamDirectory>.Fail("Dependency.InvalidResponse");}
        catch(Exception e) when(e is HttpRequestException||e is TaskCanceledException&&!token.IsCancellationRequested){return Outcome<TeamDirectory>.Fail("Dependency.Unavailable");}
    }
}

