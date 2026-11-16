using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication;
using RealEstate.Domain;
namespace RealEstate.Web;

public sealed class CoreCustomerDirectory(HttpClient client,IHttpContextAccessor accessor):ICustomerDirectory
{
    public async Task<Outcome<CaptureResponsibleDirectory>> ReadAsync(AccessScope scope,Guid unit,string? subject=null,CancellationToken token=default)
    {
        var http=accessor.HttpContext;
        if(http?.User.Identity?.IsAuthenticated!=true)return Outcome<CaptureResponsibleDirectory>.Fail("Authentication.Required");
        if(scope.OrganizationId==Guid.Empty||scope.ActiveEstablishmentId!=unit||scope.Establishments?.Any(e=>e.Id==unit)!=true)return Outcome<CaptureResponsibleDirectory>.Fail("Access.Forbidden");
        var bearer=http.Request.Path.StartsWithSegments("/api")?http.Request.Headers.Authorization.ToString():"";
        var credential=bearer.StartsWith("Bearer ",StringComparison.OrdinalIgnoreCase)?bearer[7..]:await http.GetTokenAsync("access_token");
        if(string.IsNullOrWhiteSpace(credential))return Outcome<CaptureResponsibleDirectory>.Fail("Authentication.Required");
        try{
            var path=$"api/v1/enterprise-directory/operational-customer-candidates?establishmentId={unit:D}";
            if(subject is not null)path+="&subject="+Uri.EscapeDataString(subject);
            using var request=new HttpRequestMessage(HttpMethod.Get,path);request.Headers.Authorization=new("Bearer",credential);
            using var response=await client.SendAsync(request,token);
            if(!response.IsSuccessStatusCode)return Outcome<CaptureResponsibleDirectory>.Fail(response.StatusCode switch{HttpStatusCode.Unauthorized=>"Authentication.Required",HttpStatusCode.Forbidden=>"Access.Forbidden",_=>"Dependency.Unavailable"});
            var data=await response.Content.ReadFromJsonAsync<CaptureResponsibleDirectory>(token);
            if(data is null||data.OrganizationId!=scope.OrganizationId||data.EstablishmentId!=unit||data.Items is null||data.Items.Any(p=>p is null||string.IsNullOrWhiteSpace(p.Subject)||string.IsNullOrWhiteSpace(p.DisplayName)||p.ProfileId==Guid.Empty||subject is not null&&p.Subject!=subject)||data.Items.Select(p=>p.Subject).Distinct(StringComparer.Ordinal).Count()!=data.Items.Count)return Outcome<CaptureResponsibleDirectory>.Fail("Dependency.InvalidResponse");
            return new(data);
        }catch(System.Text.Json.JsonException){return Outcome<CaptureResponsibleDirectory>.Fail("Dependency.InvalidResponse");}
        catch(Exception e) when(e is HttpRequestException||e is TaskCanceledException&&!token.IsCancellationRequested){return Outcome<CaptureResponsibleDirectory>.Fail("Dependency.Unavailable");}
    }
}
