using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication;
using RealEstate.Domain;
namespace RealEstate.Web;

// No people database, cookie grants or candidate cache. The caller's real access token is forwarded.
public sealed class CoreCaptureDirectory(HttpClient client,IHttpContextAccessor accessor):ICaptureDirectory
{
    public async Task<Outcome<CaptureResponsibleDirectory>> ReadAsync(AccessScope scope,Guid establishmentId,string? subject=null,CancellationToken token=default)
    {
        var http=accessor.HttpContext;
        if(http?.User.Identity?.IsAuthenticated!=true)return Outcome<CaptureResponsibleDirectory>.Fail("Authentication.Required");
        if(scope.OrganizationId==Guid.Empty||scope.Establishments?.Any(e=>e.Id==establishmentId)!=true)return Outcome<CaptureResponsibleDirectory>.Fail("Access.Forbidden");
        var bearer=http.Request.Path.StartsWithSegments("/api")?http.Request.Headers.Authorization.ToString():"";
        var accessToken=bearer.StartsWith("Bearer ",StringComparison.OrdinalIgnoreCase)?bearer[7..]:await http.GetTokenAsync("access_token");
        if(string.IsNullOrWhiteSpace(accessToken))return Outcome<CaptureResponsibleDirectory>.Fail("Authentication.Required");
        try{
            var path=$"api/v1/enterprise-directory/candidates?establishmentId={establishmentId:D}&productKey=real-estate&resourceKey=properties&permissionKey=CAPTURE_RESPONSIBLE";
            if(subject is not null)path+="&subject="+Uri.EscapeDataString(subject);
            using var request=new HttpRequestMessage(HttpMethod.Get,path);request.Headers.Authorization=new("Bearer",accessToken);
            using var response=await client.SendAsync(request,token);
            if(!response.IsSuccessStatusCode)return Outcome<CaptureResponsibleDirectory>.Fail(response.StatusCode switch{HttpStatusCode.Unauthorized=>"Authentication.Required",HttpStatusCode.Forbidden=>"Access.Forbidden",_=>"Dependency.Unavailable"});
            var data=await response.Content.ReadFromJsonAsync<CaptureResponsibleDirectory>(token);
            if(data is null||data.OrganizationId!=scope.OrganizationId||data.EstablishmentId!=establishmentId||data.Items is null||data.Items.Any(p=>p is null||string.IsNullOrWhiteSpace(p.Subject)||string.IsNullOrWhiteSpace(p.DisplayName)||p.ProfileId==Guid.Empty||subject is not null&&p.Subject!=subject)||data.Items.Select(p=>p.Subject).Distinct(StringComparer.Ordinal).Count()!=data.Items.Count)return Outcome<CaptureResponsibleDirectory>.Fail("Dependency.InvalidResponse");
            return new(data);
        }catch(System.Text.Json.JsonException){return Outcome<CaptureResponsibleDirectory>.Fail("Dependency.InvalidResponse");}
        catch(Exception e) when(e is HttpRequestException || e is TaskCanceledException&&!token.IsCancellationRequested){return Outcome<CaptureResponsibleDirectory>.Fail("Dependency.Unavailable");}
    }
}
